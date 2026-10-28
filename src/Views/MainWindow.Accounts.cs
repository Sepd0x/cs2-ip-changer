using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// Steam accounts
public partial class MainWindow
{
    void LoadAccounts()
    {
        _accMeta = Store.Load(Paths.File("accounts.json"), () => new Dictionary<string, AccountMeta>());
        _accounts.Clear();
        List<LocalAccount> locals;
        try { locals = Steam.ReadAccounts(); }
        catch (Exception ex) { Log.Warn("Could not read the Steam accounts: " + ex.Message); locals = new(); }
        var active = Steam.ActiveSteamId();
        foreach (var l in locals)
        {
            if (!_accMeta.TryGetValue(l.SteamId, out var meta)) _accMeta[l.SteamId] = meta = new AccountMeta();
            if (meta.Hidden) continue;
            _accounts.Add(new AccountItem(l, meta) { Active = l.SteamId == active, Streamer = HideLogins });
        }
        AccountsEmptyText.Text = Steam.InstallDir() == null
            ? "Steam is not installed on this PC."
            : "No saved accounts found.\nLog in to Steam with \"Remember me\" checked, or use Add account.";
        ApplyAccountFilter();
        if (HideLogins) RemaskLog();
        Log.Info($"{_accounts.Count} Steam account(s) on this PC.");
    }

    void SaveAccounts() { if (!_demo) Store.Save(Paths.File("accounts.json"), _accMeta); }

    DateTime _loginUsersAt;
    string _activeSeen;

    /// <summary>
    /// Every tick: an account added or logged in outside the app (Steam's own login, another switcher) shows up and
    /// becomes the one in use, so ban marks and Switch don't keep acting on the old one.
    /// </summary>
    void WatchSteamAccounts()
    {
        if (_demo || _switching || _refreshingAcc) return;
        DateTime written = default;
        try { if (Steam.LoginUsersPath is string p && File.Exists(p)) written = File.GetLastWriteTimeUtc(p); } catch { }
        var active = Steam.ActiveSteamId();
        var unknown = active != null && !_accounts.Any(a => a.SteamId == active) && !(_accMeta?.TryGetValue(active, out var m) == true && m.Hidden);
        if (written != _loginUsersAt || unknown)
        {
            var first = _loginUsersAt == default;
            _loginUsersAt = written;
            if (!first)
            {
                var before = _accounts.Select(a => a.SteamId).ToHashSet();
                LoadAccounts();
                var added = _accounts.Where(a => !before.Contains(a.SteamId)).ToList();
                if (added.Count > 0) Toast(added.Count == 1 ? $"New Steam account: {added[0].Name}." : $"{added.Count} new Steam accounts.", true);
            }
        }
        if (active == _activeSeen) return;
        _activeSeen = active;
        if (active == null) return;   // Steam closed or logging in: keep the last one shown
        var changed = false;
        foreach (var a in _accounts) { var me = a.SteamId == active; if (a.Active != me) { a.Active = me; changed = true; } }
        if (!changed) return;
        if (ActiveAccount() is AccountItem now) Log.Info($"Steam is now logged in to {now.Name}.");
        ApplyAccountFilter();
    }

    void AccFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiLock || !IsLoaded) return;
        if (AccSort.SelectedItem is ComboBoxItem it && (string)it.Tag != _s.AccountSort) { _s.AccountSort = (string)it.Tag; SaveSettings(); }
        ApplyAccountFilter();
    }

    // Cards in a grid: as many columns as fit (min. 250 px each), all the same width.
    System.Windows.Controls.Primitives.UniformGrid _accGrid;

    void AccGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _accGrid = (System.Windows.Controls.Primitives.UniformGrid)sender;
        FitAccountColumns();
    }

    void AccountsPanel_SizeChanged(object sender, SizeChangedEventArgs e) => FitAccountColumns();

    void FitAccountColumns()
    {
        if (_accGrid == null || AccountsPanel.ActualWidth <= 0) return;
        _accGrid.Columns = Math.Max(1, (int)((AccountsPanel.ActualWidth + 10) / 260));
    }

    void ApplyAccountFilter()
    {
        var q = AccSearch.Text.Trim();
        IEnumerable<AccountItem> list = _accounts;
        if (q.Length > 0)
            list = list.Where(a => (a.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Login ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (a.Note ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Tags ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || a.SteamId.Contains(q));
        if (FFav.IsChecked == true) list = list.Where(a => a.Favorite);
        if (FClean.IsChecked == true) list = list.Where(a => a.Clean);
        if (FBan.IsChecked == true) list = list.Where(a => a.HasVac || a.HasGameBan || a.HasTradeBan || a.HasCommunityBan);
        if (FCool.IsChecked == true) list = list.Where(a => a.InCooldown);
        if (FOut.IsChecked == true) list = list.Where(a => a.Excluded);

        var ordered = list.OrderByDescending(a => a.Favorite);
        ordered = _s.AccountSort switch
        {
            "name" => ordered.ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            "clean" => ordered.ThenByDescending(a => a.Clean).ThenBy(a => a.InCooldown).ThenBy(a => a.Name),
            "hours" => ordered.ThenByDescending(a => a.Meta.Profile?.Cs2Hours ?? -1),
            _ => ordered.ThenByDescending(a => a.Meta.LastUsed ?? DateTimeOffset.FromUnixTimeSeconds(a.Local.Timestamp).LocalDateTime),
        };
        _accShown.Clear();
        foreach (var a in ordered) _accShown.Add(a);
        var bans = _accounts.Count(a => a.HasVac || a.HasGameBan);
        AccCount.Text = _accounts.Count == 0 ? "" : $"{_accShown.Count} of {_accounts.Count}" + (bans > 0 ? $" · {bans} banned" : "");
        AccountsEmpty.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateQuickInfo();
    }

    // ------------------------------------------------------------ Quick switch

    /// <summary>The next account in the rotation: not skipped, no cooldown or ban (nor banned by that server), least recently used.</summary>
    AccountItem NextQuick(ServerItem server = null)
    {
        var active = _accounts.FirstOrDefault(a => a.Active)?.SteamId;
        return _accounts.Where(a => a.Usable && a.SteamId != active && a.BanFor(server?.Key, server?.ServerName, server?.Network) == null)
            .OrderBy(a => a.Meta.LastUsed ?? DateTime.MinValue)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    void UpdateQuickInfo()
    {
        var c = SwitchChoice;
        var next = c.Account ? NextQuick() : null;
        var pool = _accounts.Count(a => a.Usable);
        SideQuickLabel.Text = next == null ? "Switch" : "Switch to";
        SideQuickNext.Text = next?.Name ?? "";
        // Always clickable: a click that can't run says why, instead of a grey button that doesn't answer.
        var steps = new List<string>();
        if (c.Ip) steps.Add("changes the IP");
        if (c.Account) steps.Add(next == null ? "logs in to the next account (none now: all are skipped, on cooldown or banned)" : $"logs in to {next.Name}");
        if (c.Rejoin) steps.Add($"joins {_s.LastServer ?? "the last server"} again");
        var tip = steps.Count == 0 ? "Switch does nothing yet: pick its steps in Settings > Switch."
            : "Switch " + (steps.Count == 1 ? steps[0] : string.Join(", ", steps.Take(steps.Count - 1)) + " and " + steps[^1]) + " (with the usual checks)."
              + (c.Account ? $"\n{pool} account(s) in the rotation (not skipped, no cooldown or ban)." : "")
              + $"\nPick the steps in Settings > Switch. Shortcut anywhere, even in game: {_s.HotkeySwitch}.";
        QuickBtn.ToolTip = tip;
        SideQuick.ToolTip = tip;

        ApplyServerBans();   // the ban marks on Servers follow the account in use
    }

    async void Quick_Click(object sender, RoutedEventArgs e) => await QuickSwitch("Switch");

    /// <summary>The one switch, with the steps picked in Settings (by default: new IP, next account, back to the last server).</summary>
    Task QuickSwitch(string from) => RunCommand("Switch", "starting…", () => RunSwitch(SwitchChoice, from));

    /// <summary>The account quick switch would use now (also for Switch), or null with the reason shown.</summary>
    AccountItem PickQuick(ServerItem server = null)
    {
        var next = NextQuick(server);
        var skipped = _accounts.Where(a => !a.Usable).Select(a =>
            $"{a.Name} ({(a.Excluded ? "skipped" : a.InCooldown ? "cooldown" : "ban")})").ToList();
        if (skipped.Count > 0) Log.Info("Quick switch skipped: " + string.Join(", ", skipped));
        if (next == null) { Toast(server == null ? "No accounts for quick switch: all are skipped, on cooldown or banned." : $"No account left for {server.Title}: the others are skipped, on cooldown or banned (also by this server's network).", false); return null; }
        if (next.Meta.ExcludeQuick) { Log.Error("Quick switch picked a skipped account; cancelled."); return null; }
        return next;
    }

    void AccToggleQuick_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        a.Meta.ExcludeQuick = !a.Meta.ExcludeQuick;
        a.RefreshAll();
        SaveAccounts();
        ApplyAccountFilter();
        Toast(a.Excluded ? $"{a.Name} is now skipped in quick switch." : $"{a.Name} is back in quick switch.", true);
    }

    static AccountItem Acc(object sender) => (sender as FrameworkElement)?.DataContext as AccountItem;

    async void AccSwitch_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await RunCommand("Log in", $"logging in to {a.Name}…", () => SwitchTo(a, OptCs2.IsChecked == true)); }
    async void AccSwitchCs2_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await RunCommand("Log in", $"logging in to {a.Name}…", () => SwitchTo(a, true)); }

    /// <summary>Logs in to the account (closing CS2 first). True if Steam was started on it.</summary>
    async Task<bool> SwitchTo(AccountItem a, bool cs2, bool ask = true, bool openWith = true)
    {
        if (_switching) return false;
        _cs2ClosedByAppAt = DateTime.Now; // the switch closes CS2 itself: that exit is not a crash
        if (_demo)
        {
            foreach (var x in _accounts) x.Active = x == a;
            a.Meta.LastUsed = DateTime.Now;
            UpdateQuickInfo();
            Toast($"Demo: logging in to {a.Name}.", true);
            return true;
        }
        if (Steam.Cs2Running)
        {
            if (ask && !await Ask("CS2 is open", $"CS2 and Steam have to close to log in to {a.Name}. Continue?", "Close and switch")) return false;
            foreach (var p in Process.GetProcessesByName("cs2")) try { p.Kill(true); } catch { }
            for (var i = 0; i < 20 && Steam.Cs2Running; i++) await Task.Delay(150);
        }
        _switching = true;
        a.Busy = true;
        Step($"logging in to {a.Name}…");
        Toast($"Logging in to {a.Name}…", true);
        try
        {
            // Without CS2 locked to the phone connection, the game only opens with confirmed protection.
            if (cs2 && !_s.LockCs2 && (_last?.Egress == null || _distrust))
            {
                cs2 = false;
                Toast("Switched account but did not open CS2: protection is off and CS2 is not locked.", false);
            }
            var args = string.Join(" ", new[] { _s.SteamSilent ? "-silent" : null, cs2 ? "-applaunch 730 -condebug -conclearlog" : null }.Where(x => x != null));
            await Steam.SwitchTo(a.Login, a.SteamId, _s.SteamOffline, args);
            if (cs2 && openWith) OpenWithCs2();
            a.Meta.LastUsed = DateTime.Now;
            SaveAccounts();
            Log.Ok($"Switched to {a.Name}" + (cs2 ? " (CS2 opening)." : "."));
            Diag.Write("account", new { steamId = a.SteamId, cs2, offline = _s.SteamOffline });
            foreach (var x in _accounts) x.Active = x == a;
            UpdateQuickInfo();
            _ = Task.Delay(8000).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                var active = Steam.ActiveSteamId();
                if (active != null) foreach (var x in _accounts) x.Active = x.SteamId == active;
                ApplyServerBans();
            }));
            return true;
        }
        catch (Exception ex) { Log.Error("Account switch: " + ex.Message); Toast(ex.Message, Tone.Error); return false; }
        finally { a.Busy = false; _switching = false; UpdateQuickInfo(); }
    }

    async void AccAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (!await Ask("Add account", "Steam will close and open on the login screen. Log in with the new account and check \"Remember me\". Then come back here and it shows up in the list.", "Open login")) return;
        try { await Steam.AddNew(); WaitForNewAccount(); }
        catch (Exception ex) { Toast(ex.Message, false); }
    }

    async void WaitForNewAccount()
    {
        var before = _accounts.Count + _accMeta.Values.Count(m => m.Hidden);
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(5000);
            if (Steam.ReadAccounts().Count > before) { LoadAccounts(); Toast("New account added.", true); return; }
        }
    }

    async void AccRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAccounts(_accounts.ToList());

    async void AccRefreshOne_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await RefreshAccounts(new() { a }); }

    async Task RefreshAccounts(List<AccountItem> list)
    {
        if (_demo || _refreshingAcc || list.Count == 0) return;
        _refreshingAcc = true;
        AccRefreshBtn.IsEnabled = false;
        var errors = 0;
        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i];
                AccRefreshText.Text = $"Checking {i + 1}/{list.Count}…";
                try
                {
                    var oldUrl = a.Meta.Profile?.AvatarUrl;
                    var p = await Steam.FetchProfile(a.SteamId, _s.SteamApiKey);
                    a.Meta.Profile = p;
                    if (p.AvatarUrl != null && (p.AvatarUrl != oldUrl || a.Meta.AvatarPath == null || !File.Exists(a.Meta.AvatarPath)))
                    {
                        try { a.Meta.AvatarPath = await Steam.DownloadAvatar(a.SteamId, p.AvatarUrl); } catch { }
                    }
                    a.LoadAvatar();
                    a.RefreshAll();
                    if (p.VacBans > 0 || p.GameBans > 0) Log.Warn($"{a.Name}: {p.VacBans} VAC, {p.GameBans} game ban(s).");
                }
                catch (Exception ex) { errors++; Log.Warn($"{a.Name}: {ex.Message}"); }
                if (i % 5 == 4) SaveAccounts();
                if (i < list.Count - 1) await Task.Delay(1100);
            }
            SaveAccounts();
            ApplyAccountFilter();
            Toast(errors == 0 ? $"{list.Count} account(s) refreshed." : $"Refreshed, with {errors} error(s). Steam may be rate limiting requests: try again in a minute.", errors == 0);
        }
        finally
        {
            _refreshingAcc = false;
            AccRefreshBtn.IsEnabled = true;
            AccRefreshText.Text = "Refresh";
        }
    }

    void AccProfile_Click(object sender, RoutedEventArgs e) => OpenUrl(Acc(sender), "https://steamcommunity.com/profiles/{0}");
    void AccLeetify_Click(object sender, RoutedEventArgs e) => OpenUrl(Acc(sender), "https://leetify.com/app/profile/{0}");
    void AccCsstats_Click(object sender, RoutedEventArgs e) => OpenUrl(Acc(sender), "https://csstats.gg/player/{0}");

    static void OpenUrl(AccountItem a, string fmt)
    {
        if (a == null) return;
        OpenUrl(string.Format(fmt, a.SteamId));
    }

    void AccCopyId_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) { Clipboard.SetText(a.SteamId); Toast("SteamID64 copied.", true); } }
    void AccCopyLogin_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) { Clipboard.SetText(a.Login ?? ""); Toast("Login copied.", true); } }

    async void AccCooldown_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a || sender is not MenuItem mi) return;
        var minutes = int.Parse((string)mi.CommandParameter);
        if (minutes < 0)
        {
            DialogInput.Text = "";
            DialogInput.Tag = "E.g. 21h, 3d, 90m, 2d 12h";
            if (!await Ask($"Cooldown for {a.Name}", "How much time is left? Use d (days), h (hours) and m (minutes).", "Set", "Cancel", inputs: 1, label1: "Time left")) return;
            var t = Fmt.ParseDuration(DialogInput.Text);
            if (t == null) { Toast("Could not read that duration. Example: 3d 4h", false); return; }
            minutes = (int)Math.Ceiling(t.Value.TotalMinutes);
        }
        a.Meta.CooldownUntil = minutes == 0 ? null : DateTime.Now.AddMinutes(minutes);
        a.RefreshAll();
        SaveAccounts();
        ApplyAccountFilter();
        Toast(minutes == 0 ? $"Cooldown cleared for {a.Name}." : $"{a.Name}: cooldown until {Fmt.When(a.Meta.CooldownUntil)}.", true);
    }

    void AccClearBans_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a || !a.HasServerBans) return;
        a.Meta.ServerBans = null;
        a.RefreshAll();
        SaveAccounts();
        ApplyServerBans();
        UpdateQuickInfo();
        Toast($"Server bans cleared for {a.Name}.", true);
    }

    void AccFav_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        a.Favorite = !a.Favorite;
        SaveAccounts();
        ApplyAccountFilter();
    }

    async void AccNote_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        DialogInput.Text = a.Meta.Note ?? "";
        DialogInput.Tag = "E.g. main account, premier 15k";
        DialogInput2.Text = a.Meta.Tags ?? "";
        DialogInput2.Tag = "E.g. main, faceit";
        if (!await Ask($"Note for {a.Name}", "Stays on this PC only. Search looks here too.", "Save", "Cancel", inputs: 2, label1: "Note", label2: "Tags (comma separated)")) return;
        a.Meta.Note = string.IsNullOrWhiteSpace(DialogInput.Text) ? null : DialogInput.Text.Trim();
        a.Meta.Tags = string.IsNullOrWhiteSpace(DialogInput2.Text) ? null : DialogInput2.Text.Trim();
        a.RefreshAll();
        SaveAccounts();
    }

    void AccHide_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        a.Meta.Hidden = true;
        _accounts.Remove(a);
        SaveAccounts();
        ApplyAccountFilter();
        Toast($"{a.Name} hidden. Bring it back in Settings > Show hidden accounts.", true);
    }

    void UnhideAll_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast("Demo: all accounts are visible again.", true); return; }   // the demo never reads the real Steam accounts
        foreach (var m in _accMeta.Values) m.Hidden = false;
        SaveAccounts();
        LoadAccounts();
        Toast("All accounts are visible again.", true);
    }

    void AccOpt_Click(object sender, RoutedEventArgs e)
    {
        _s.LaunchCs2 = OptCs2.IsChecked == true;
        _s.SteamSilent = OptSilent.IsChecked == true;
        _s.SteamOffline = OptOffline.IsChecked == true;
        _s.StreamerMode = OptStreamer.IsChecked == true;
        foreach (var a in _accounts) a.Streamer = HideLogins;
        ShowApiKey(false);
        RemaskLog();
        SaveSettings();
    }
}
