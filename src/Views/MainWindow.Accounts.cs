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
    void LoadAccounts(List<LocalAccount> locals = null)
    {
        if (locals == null)
        {
            try { locals = Steam.ReadAccounts(); }
            catch (Exception ex)
            {
                // Steam writing the file right now: the list on screen stays, and the next tick reads it again (WatchSteamAccounts)
                Log.Warn("Could not read the Steam accounts: " + ex.Message);
                if (_accounts.Count > 0) return;
                locals = new();
            }
        }
        _accMeta = Store.Load(Paths.File("accounts.json"), () => new Dictionary<string, AccountMeta>());
        _accounts.Clear();
        var active = Steam.InUseSteamId();
        _accHidden = 0;   // only hidden accounts Steam still has, not old entries in accounts.json
        foreach (var l in locals)
        {
            if (!_accMeta.TryGetValue(l.SteamId, out var meta)) _accMeta[l.SteamId] = meta = new AccountMeta();
            if (meta.Hidden) { _accHidden++; continue; }
            _accounts.Add(new AccountItem(l, meta) { Active = l.SteamId == active, Streamer = HideLogins });
        }
        AccountsEmptyText.Text = AccEmptyText();
        ApplyAccountFilter();
        if (HideLogins) RemaskLog();
        Log.Info($"{Fmt.Count(_accounts.Count, "Steam account")} on this PC.");
        ConfigsAccountsLoaded();   // a new account gets the CS2 config (Configs page)
        // The other pages that list accounts, when they are the one on screen (otherwise they rebuild when they are opened)
        if (PageProfiles.IsVisible) ShowProfiles();
        if (PageConfigs.IsVisible) ShowConfigs();
    }

    void SaveAccounts() { if (!_demo) Store.Save(Paths.File("accounts.json"), _accMeta); }

    DateTime _loginUsersAt, _loginUsersReadAt;
    string _activeSeen;

    /// <summary>Hidden on purpose (right-click > Hide from list): not "missing" from the list.</summary>
    bool AccHiddenId(string id) => _accMeta?.TryGetValue(id, out var m) == true && m.Hidden;

    /// <summary>
    /// Every tick (it runs with protection off too): an account added or logged in outside the app (Steam's own login, another
    /// switcher) shows up on Accounts, Configs and Profiles and becomes the one in use, so ban marks and Switch don't keep acting on
    /// the old one. loginusers.vdf is read when it changed, when Steam is on an account the list doesn't have, and every 30 s anyway:
    /// a read while Steam was writing it (locked, or half written) used to leave the new account out until Steam wrote it again.
    /// </summary>
    void WatchSteamAccounts()
    {
        if (_demo || _switching || _refreshingAcc) return;
        DateTime written = default;
        try { if (Steam.LoginUsersPath is string p && File.Exists(p)) written = File.GetLastWriteTimeUtc(p); } catch { }
        var active = Steam.InUseSteamId();   // today's client leaves ActiveUser at 0: then loginusers.vdf says who
        var unknown = active != null && !_accounts.Any(a => a.SteamId == active) && !AccHiddenId(active);
        if (written != _loginUsersAt || unknown || DateTime.Now - _loginUsersReadAt > TimeSpan.FromSeconds(30))
        {
            List<LocalAccount> disk;
            try { disk = Steam.ReadAccounts(); }
            catch { return; }   // Steam is writing it: the next tick reads it
            _loginUsersAt = written;
            _loginUsersReadAt = DateTime.Now;
            if (Steam.AccountsDiffer(_accounts.Select(a => a.SteamId), disk, AccHiddenId))
            {
                var before = _accounts.Select(a => a.SteamId).ToHashSet();
                LoadAccounts(disk);
                var added = _accounts.Where(a => !before.Contains(a.SteamId)).ToList();
                if (added.Count > 0)
                {
                    // A new account goes where the sort puts it (at the top in Most recent), not at the end of a long list
                    _accResort = true;
                    ApplyAccountFilter();
                    Toast(added.Count == 1 ? $"New Steam account: {added[0].Name}." : $"{added.Count} new Steam accounts.", true);
                }
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

    /// <summary>Accounts hidden from the list (right-click > Hide from list) that Steam still has on this PC.</summary>
    int _accHidden;

    string AccEmptyText() => Steam.InstallDir() == null ? "Steam is not installed on this PC."
        : _accounts.Count == 0 && _accHidden > 0 ? "All your accounts are hidden.\nBring them back in Settings > Maintenance > Show hidden accounts."
        : "No saved accounts found.\nLog in to Steam with \"Remember me\" checked, or use Add account.";

    void AccFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiLock || !IsLoaded) return;
        if (AccSort.SelectedItem is ComboBoxItem it && (string)it.Tag != _s.AccountSort) { _s.AccountSort = (string)it.Tag; SaveSettings(); }
        _accResort = true;   // your own sort, filter or search: the order follows it at once
        ApplyAccountFilter();
    }

    /// <summary>
    /// While the Accounts page is on screen, the cards keep their places (a switch moves the account to the top of "Most recent", and
    /// the next click would land on another account). The new order shows on the next visit, or when you change the sort or filter.
    /// </summary>
    bool _accResort, _accVisibleHooked;

    public static List<string> StableOrder(IReadOnlyList<string> shownBefore, IReadOnlyList<string> sorted, bool keep)
    {
        if (!keep || shownBefore == null || shownBefore.Count == 0) return sorted.ToList();
        var pos = new Dictionary<string, int>();
        for (var i = 0; i < shownBefore.Count; i++) pos.TryAdd(shownBefore[i], i);
        return sorted.Where(pos.ContainsKey).OrderBy(id => pos[id]).Concat(sorted.Where(id => !pos.ContainsKey(id))).ToList();
    }

    // Cards in a grid: as many columns as fit (min. 250 px each), all the same width.
    System.Windows.Controls.Primitives.UniformGrid _accGrid;

    void AccGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _accGrid = sender as System.Windows.Controls.Primitives.UniformGrid;   // null in rows (a StackPanel)
        FitAccountColumns();
        if (!_accVisibleHooked)
        {
            _accVisibleHooked = true;
            PageAccounts.IsVisibleChanged += (_, _) => { if (PageAccounts.IsVisible) { _accResort = true; ApplyAccountFilter(); } };
        }
    }

    void AccountsPanel_SizeChanged(object sender, SizeChangedEventArgs e) => FitAccountColumns();

    void FitAccountColumns()
    {
        if (_accGrid == null || AccountsPanel.ActualWidth <= 0) return;
        _accGrid.Columns = Math.Max(1, (int)((AccountsPanel.ActualWidth + 10) / 260));
    }

    /// <summary>Rows are offered from 12 accounts, until a view is picked (the button or the offer's own buttons).</summary>
    public static bool SuggestRows(int accounts, string view) => view == null && accounts >= 12;

    void ApplyAccountView()
    {
        var rows = _s.AccountsView == "rows";
        AccountsPanel.ItemTemplate = (DataTemplate)FindResource(rows ? "AccountRow" : "AccountCard");
        AccountsPanel.ItemsPanel = (ItemsPanelTemplate)FindResource(rows ? "AccRowsPanel" : "AccCardsPanel");
        AccViewBtn.Content = rows ? "" : "";
        AccViewBtn.ToolTip = rows ? "Show as cards" : "Show as rows: more accounts at once";
        AccRowsHint.Visibility = SuggestRows(_accounts.Count, _s.AccountsView) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The view button flips cards and rows; the offer's buttons say which (Tag).</summary>
    void AccView_Click(object sender, RoutedEventArgs e)
    {
        _s.AccountsView = (sender as FrameworkElement)?.Tag as string ?? (_s.AccountsView == "rows" ? "cards" : "rows");
        SaveSettings();
        ApplyAccountView();
    }

    /// <summary>"Ready first" (many accounts, 3 nov.): ready now, then in cooldown (the one ending first on top), then out of rotation, then banned.</summary>
    public static int ReadyRank(AccountItem a) => a.HasVac || a.HasGameBan ? 3 : a.Excluded ? 2 : a.InCooldown ? 1 : 0;

    void ApplyAccountFilter()
    {
        var q = AccSearch.Text.Trim();
        IEnumerable<AccountItem> list = _accounts;
        if (q.Length > 0)
            list = list.Where(a => (a.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Login ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (a.Note ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Tags ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || a.SteamId.Contains(q));
        if (FReady.IsChecked == true) list = list.Where(a => a.Usable);
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
            "ready" => ordered.ThenBy(ReadyRank).ThenBy(a => a.InCooldown ? a.Meta.CooldownUntil : null).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => ordered.ThenByDescending(a => a.Meta.LastUsed ?? DateTimeOffset.FromUnixTimeSeconds(a.Local.Timestamp).LocalDateTime),
        };
        var sorted = ordered.ToList();
        var byId = sorted.GroupBy(a => a.SteamId).ToDictionary(g => g.Key, g => g.First());
        var ids = StableOrder(_accShown.Select(a => a.SteamId).ToList(), sorted.Select(a => a.SteamId).ToList(), keep: PageAccounts.IsVisible && !_accResort);
        _accResort = false;
        _accShown.Clear();
        foreach (var id in ids) _accShown.Add(byId[id]);
        var bans = _accounts.Count(a => a.HasVac || a.HasGameBan);
        var hid = _accHidden > 0 ? $"{_accHidden} hidden" : null;
        var core = _accounts.Count == 0 ? "" : $"{_accShown.Count} of {_accounts.Count}" + (bans > 0 ? $" · {bans} banned" : "");
        AccCount.Text = string.Join(" · ", new[] { core, hid }.Where(s => !string.IsNullOrEmpty(s)));
        AccCount.ToolTip = _accHidden > 0
            ? (core.Length > 0 ? core + "\n" : "") + $"{_accHidden} hidden. Bring {(_accHidden == 1 ? "it" : "them")} back in Settings > Maintenance > Show hidden accounts."
            : (core.Length > 0 ? core : null);
        AccountsEmpty.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccRowsHint.Visibility = SuggestRows(_accounts.Count, _s.AccountsView) ? Visibility.Visible : Visibility.Collapsed;
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
        // Named for what it does: a new IP first, then the account (or only one of them, if Settings leaves out a step).
        var who = c.Account ? next?.Name ?? "next account" : null;
        var head = c.Ip && who != null ? "New IP + " : c.Ip ? "New IP" : who != null ? "Log in to " : "Switch";
        SideQuickLabel.Text = head.TrimEnd();
        SideQuickNext.Text = who ?? "";
        SideQuickNext.Margin = new Thickness(who == null ? 0 : 4, 0, 0, 0);
        // Shorter than the top bar: at the minimum width (1020) a longer name pushes the "On login:" row onto two lines. The tooltip has it whole.
        QuickBtn.Content = head + (who == null ? "" : who.Length > 10 ? who.Substring(0, 9) + "…" : who);
        // With fewer than two accounts there is nothing to rotate to: the command isn't offered.
        var offered = !c.Account || _accounts.Count >= 2;
        SideQuick.Visibility = QuickBtn.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
        // Always clickable: a click that can't run says why, instead of a grey button that doesn't answer.
        var steps = new List<string>();
        if (c.Ip) steps.Add("changes the IP");
        if (c.Account) steps.Add(next == null ? "logs in to the next account (none now: all are out of rotation, on cooldown or banned)" : $"logs in to {next.Name}");
        if (c.Rejoin) steps.Add($"joins {_s.LastServer ?? "the last server"} again");
        var tip = steps.Count == 0 ? "This button does nothing yet: pick its steps in Settings > New IP + next account."
            : "In order, it " + (steps.Count == 1 ? steps[0] : string.Join(", ", steps.Take(steps.Count - 1)) + " and " + steps[^1]) + " (with the usual checks)."
              + (c.Account ? $"\n{Fmt.Count(pool, "account")} in the rotation (no cooldown or ban)." : "")
              + $"\nPick the steps in Settings > New IP + next account. Shortcut anywhere, even in game: {_s.HotkeySwitch}.";
        QuickBtn.ToolTip = tip;
        SideQuick.ToolTip = tip;

        ApplyServerBans();   // the ban marks on Servers follow the account in use
    }

    async void Quick_Click(object sender, RoutedEventArgs e) => await QuickSwitch("New IP + next account");

    /// <summary>The one switch, with the steps picked in Settings (by default: new IP, next account, back to the last server).</summary>
    Task QuickSwitch(string from) => RunCommand("New IP + next account", "starting…", () => RunSwitch(SwitchChoice, from));

    /// <summary>The account quick switch would use now (also for Switch), or null with the reason shown.</summary>
    AccountItem PickQuick(ServerItem server = null)
    {
        if (_accounts.Count < 2) { Toast("New IP + next account needs at least 2 saved accounts. Add one on Accounts.", false); return null; }
        var next = NextQuick(server);
        var skipped = _accounts.Where(a => !a.Usable).Select(a =>
            $"{a.Name} ({(a.Excluded ? "not in rotation" : a.InCooldown ? "cooldown" : "ban")})").ToList();
        if (skipped.Count > 0) Log.Info("Left out of the rotation: " + string.Join(", ", skipped));
        if (next == null) { Toast(server == null ? "No account to switch to: all are out of rotation, on cooldown or banned." : $"No account left for {server.Title}: the others are out of rotation, on cooldown or banned (also by this server's network).", false); return null; }
        if (next.Meta.ExcludeQuick) { Log.Error("Picked an account that is not in rotation; cancelled."); return null; }
        return next;
    }

    void AccToggleQuick_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        a.Meta.ExcludeQuick = !a.Meta.ExcludeQuick;
        a.RefreshAll();
        SaveAccounts();
        ApplyAccountFilter();
        Toast(a.Excluded ? $"{a.Name} is out of the rotation now." : $"{a.Name} is back in the rotation.", true);
    }

    static AccountItem Acc(object sender) => (sender as FrameworkElement)?.DataContext as AccountItem;

    async void AccSwitch_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await RunCommand("Log in", $"logging in to {a.Name}…", () => SwitchTo(a, OptCs2.IsChecked == true)); }
    async void AccSwitchCs2_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await RunCommand("Log in", $"logging in to {a.Name}…", () => SwitchTo(a, true)); }

    /// <summary>Logs in to the account (closing CS2 first). True if Steam was started on it.</summary>
    async Task<bool> SwitchTo(AccountItem a, bool cs2, bool ask = true, bool openWith = true)
    {
        if (_switching) return false;
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
            if (ask && !await Ask("CS2 is open", $"CS2 and Steam have to close to log in to {a.Name}. Continue?", "Close and log in")) return false;
            // Stamped only now, right before the kill: a declined switch hides no crash, and a long wait on the dialog doesn't make this exit one
            _cs2ClosedByAppAt = DateTime.Now; // the switch closes CS2 itself: that exit is not a crash
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
            if (cs2 && !_s.LockCs2 && !_s.NoPhone && (_last?.Egress == null || _distrust))
            {
                cs2 = false;
                Toast("Switched account but did not open CS2: protection is off and CS2 is not locked.", false);
            }
            ConfigsBeforeLogin(a);   // CS2 config copied in, while CS2 is closed (Configs page)
            // Steam first, CS2 only once Steam is really on this account: today's client can come up on another one.
            Steam.LoginState state;
            string other;
            for (var attempt = 1; ; attempt++)
            {
                var stamp = await Steam.SwitchTo(a.Login, a.SteamId, _s.SteamOffline, _s.SteamSilent ? "-silent" : "");
                Step($"waiting for Steam to log in to {a.Name}…");
                (state, other) = await Steam.WaitForLogin(a.SteamId, stamp, TimeSpan.FromSeconds(30));
                Diag.Write("account", new { steamId = a.SteamId, cs2, offline = _s.SteamOffline, login = state.ToString(), other, attempt });
                if (!Steam.RetrySwitch(state, attempt)) break;
                Log.Info($"Steam came up on another account instead of {a.Name}: trying once more.");
                Step($"Steam took another account, logging in to {a.Name} again…");
            }
            if (state != Steam.LoginState.Target)
            {
                var got = _accounts.FirstOrDefault(x => x.SteamId == other);
                var msg = state == Steam.LoginState.Other
                    ? $"Steam logged in to {got?.Name ?? "another account"} instead of {a.Name}. Log in to {a.Name} once in Steam with Remember me, then switch again."
                    : $"Steam didn't log in to {a.Name} within 30 s. If Steam asks for the password, log in once with Remember me, then switch again.";
                Log.Warn(msg + (cs2 ? " CS2 wasn't opened." : ""));
                Toast(msg, Tone.Error);
                if (got != null) { foreach (var x in _accounts) x.Active = x == got; UpdateQuickInfo(); }
                return false;
            }
            if (cs2) Steam.Launch("-applaunch 730 -condebug -conclearlog");
            if (cs2 && openWith) OpenWithCs2();
            // A switch you started here, with no CS2 to open: Steam's window came up in front, the app goes back on top.
            if (!cs2 && ask) _ = BackToFront();
            a.Meta.LastUsed = DateTime.Now;
            SaveAccounts();
            Log.Ok($"Switched to {a.Name}" + (cs2 ? " (CS2 opening)." : "."));
            foreach (var x in _accounts) x.Active = x == a;
            UpdateQuickInfo();
            _ = Task.Delay(8000).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                var active = Steam.InUseSteamId();
                if (active != null) foreach (var x in _accounts) x.Active = x.SteamId == active;
                ApplyServerBans();
            }));
            return true;
        }
        catch (Exception ex) { Log.Error("Account switch: " + ex.Message); Toast(ex.Message, Tone.Error); return false; }
        finally { a.Busy = false; _switching = false; UpdateQuickInfo(); }
    }

    /// <summary>Steam opens its window a moment after it logs in: the app waits for that, then comes back in front (not when minimized).</summary>
    async Task BackToFront()
    {
        await Task.Delay(2500);
        if (WindowState == WindowState.Minimized || !IsVisible) return;
        Activate();
        Topmost = true;    // Windows only lets a window come forward this way when it isn't the active one
        Topmost = false;
        Focus();
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
        // By SteamID, not by count: old hidden entries in accounts.json (Steam no longer has them) made the count never go up
        var before = _accounts.Select(a => a.SteamId).ToHashSet();
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(5000);
            if (!_accounts.All(a => before.Contains(a.SteamId))) return;   // the tick found it already (and said so)
            List<LocalAccount> disk;
            try { disk = Steam.ReadAccounts(); } catch { continue; }
            if (disk.Any(a => !before.Contains(a.SteamId) && !AccHiddenId(a.SteamId))) { LoadAccounts(disk); _accResort = true; ApplyAccountFilter(); Toast("New account added.", true); return; }
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
                    if (p.VacBans > 0 || p.GameBans > 0) Log.Warn($"{a.Name}: {p.VacBans} VAC, {Fmt.Count(p.GameBans, "game ban")}.");
                }
                catch (Exception ex) { errors++; Log.Warn($"{a.Name}: {ex.Message}"); }
                if (i % 5 == 4) SaveAccounts();
                if (i < list.Count - 1) await Task.Delay(1100);
            }
            SaveAccounts();
            ApplyAccountFilter();
            Toast(errors == 0 ? $"{Fmt.Count(list.Count, "account")} refreshed." : $"Refreshed, with {Fmt.Count(errors, "error")}. Steam may be rate limiting requests: try again in a minute.", errors == 0);
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
    void AccCopyFriendCode_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a || Steam.Cs2FriendCode(a.SteamId) is not string code) return;
        Clipboard.SetText(code);
        Toast($"CS2 friend code copied ({code}). In CS2: Friends > Add a friend.", true);
    }
    void AccCopyLogin_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) { Clipboard.SetText(a.Login ?? ""); Toast("Username copied.", true); } }

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
        _accHidden++;
        SaveAccounts();
        if (_accounts.Count == 0) AccountsEmptyText.Text = AccEmptyText();
        ApplyAccountFilter();
        Toast($"{a.Name} hidden. Bring it back in Settings > Maintenance > Show hidden accounts.", true);
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
