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

// Servers
public partial class MainWindow
{
    void LoadServers()
    {
        var list = _demo ? null : Store.Load<List<ServerItem>>(Paths.File("servers.json"), () => null);
        if (list == null && !_demo)
        {
            // Migration: the v8 list (servers.json next to the .exe) moves to SRT_Dados.
            var old = Path.Combine(Paths.AppDir, "servers.json");
            list = File.Exists(old) ? Store.Load<List<ServerItem>>(old, () => null) : null;
        }
        if (list == null)
        {
            // First run: the list starts empty (paste servers in Servers).
            var defaults = new List<(string Ip, int Port)>();
            AddDefaultServers(defaults);
            list = defaults.Select(d => new ServerItem { Ip = d.Ip, Port = d.Port }).ToList();
        }
        foreach (var s in list.Where(s => ValidIp(s.Ip) && s.Port is >= 1 and <= 65535).GroupBy(s => s.Key).Select(g => g.First()))
        {
            s.PlayersText = "—"; s.PingText = "—"; s.Map = "—";
            _servers.Add(s);
        }
        if (!_demo) SaveServers();
    }

    void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RemoveBtn.IsEnabled = ServerList.SelectedItems.Count > 0;

    void SaveServers() { if (!_demo) Store.Save(Paths.File("servers.json"), _servers.ToList()); }

    static bool ValidIp(string ip)
    {
        if (ip == null) return false;
        var p = ip.Split('.');
        if (p.Length != 4 || !p.All(x => int.TryParse(x, out var n) && n is >= 0 and <= 255)) return false;
        var first = int.Parse(p[0]);
        return first != 0 && first != 127 && first < 224;
    }

    int ParseInto(string text, out List<string> ignored)
    {
        ignored = new();
        var port = int.TryParse(DefaultPortBox.Text.Trim(), out var dp) && dp is >= 1 and <= 65535 ? dp : 27015;
        var added = 0;
        foreach (Match m in ServerRegex.Matches(text ?? ""))
        {
            var ip = string.Join(".", m.Groups[1].Value.Split('.').Select(o => int.TryParse(o, out var n) ? n.ToString() : o));
            if (!ValidIp(ip)) { ignored.Add(m.Value.Trim()); continue; }
            var p = port;
            if (m.Groups[2].Success && (!int.TryParse(m.Groups[2].Value, out p) || p < 1 || p > 65535)) { ignored.Add(m.Value.Trim()); continue; }
            if (_servers.Any(s => s.Ip == ip && s.Port == p)) continue;
            _servers.Add(new ServerItem { Ip = ip, Port = p, PlayersText = "—", PingText = "—", Map = "—" });
            added++;
        }
        return added;
    }

    async Task AddFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { Toast("Nothing to add.", false); return; }
        var n = ParseInto(text, out var ignored);
        SaveServers();
        ShowLastServer();
        if (n == 0 && ignored.Count == 0) { Toast("Those servers are already in the list.", false); return; }
        Log.Info($"Added {n} server(s)." + (ignored.Count > 0 ? " Ignored: " + string.Join(", ", ignored) : ""));
        Toast(n > 0 ? $"{n} server(s) added and protected." + (ignored.Count > 0 ? $" {ignored.Count} ignored." : "") : "No valid IP:port.", n > 0);
        _s.DefaultPort = int.TryParse(DefaultPortBox.Text, out var dp) ? dp : 27015;
        SaveSettings();
        await SyncNow(force: true);
        await QueryServers(true);
    }

    async void AddServers_Click(object sender, RoutedEventArgs e) { var t = BulkInput.Text; BulkInput.Clear(); await AddFromText(t); }

    async void PasteServers_Click(object sender, RoutedEventArgs e)
    {
        string t = null;
        try { t = Clipboard.GetText(); } catch { }
        await AddFromText(t);
    }

    async void ImportServers_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Lists (*.txt;*.csv;*.cfg;*.log)|*.txt;*.csv;*.cfg;*.log|All (*.*)|*.*", Title = "Choose a server list" };
        if (dlg.ShowDialog(this) == true) await AddFromText(await File.ReadAllTextAsync(dlg.FileName));
    }

    void ImportStartupFile()
    {
        var f = Path.Combine(Paths.AppDir, "importar.txt");
        if (!File.Exists(f)) return;
        _ = AddFromText(File.ReadAllText(f));
        try { File.Move(f, Path.Combine(Paths.Data, $"importar_feito_{DateTime.Now:yyyyMMdd_HHmmss}.txt")); } catch { }
    }

    async void BulkInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            var t = BulkInput.Text; BulkInput.Clear();
            await AddFromText(t);
        }
    }

    void ServerSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var q = ServerSearch.Text.Trim();
        _serverView.Filter = string.IsNullOrEmpty(q) ? null : o =>
        {
            var s = (ServerItem)o;
            return s.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || (s.ServerName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (s.Map ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (s.Label ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        };
    }

    List<ServerItem> SelectedOrAll()
    {
        var sel = ServerList.SelectedItems.Cast<ServerItem>().ToList();
        return sel.Count > 0 ? sel : _serverView.Cast<ServerItem>().ToList();
    }

    async void QueryServers_Click(object sender, RoutedEventArgs e) => await QueryServers(false);

    async Task QueryServers(bool quiet)
    {
        if (_demo || _querying) return;
        var egress = _last?.Egress;
        if (egress == null) { if (!quiet) Toast("Phone disconnected: not querying servers over your home network.", false); return; }
        _querying = true;
        try
        {
            var targets = quiet ? _servers.ToList() : SelectedOrAll();
            var gate = new SemaphoreSlim(8);
            var ok = 0;
            await Task.WhenAll(targets.Select(async srv =>
            {
                await gate.WaitAsync();
                try { if (await QueryOne(srv, egress)) Interlocked.Increment(ref ok); }
                finally { gate.Release(); }
            }));
            if (!quiet) Toast($"{ok} of {targets.Count} server(s) responded.", ok > 0);
        }
        finally { _querying = false; }
        ApplyServerBans();   // the network of a ban is known by name only after the server answers
        await CheckFollowed();
    }

    /// <summary>Name, map, players and ping of one server, only through the phone. False if it didn't answer (or isn't routed).</summary>
    async Task<bool> QueryOne(ServerItem srv, Adapter egress)
    {
        if (_last?.RouteOk.TryGetValue(srv.Ip, out var routed) != true || !routed) { srv.PingText = "—"; srv.Players = null; return false; }
        try
        {
            var info = await A2S.Query(srv.Ip, srv.Port, egress.LocalIp);
            srv.ServerName = info.Name;
            srv.Map = info.Map;
            srv.PlayerCount = info.Players;
            srv.MaxPlayers = info.MaxPlayers;
            srv.PlayersText = info.Bots > 0 ? $"{info.Players}/{info.MaxPlayers} ({info.Bots} bots)" : $"{info.Players}/{info.MaxPlayers}";
            srv.PingMs = info.PingMs;
            srv.PingText = $"{info.PingMs} ms";
            // Some servers answer the player list only now and then: keep the last one for 2 min instead of flipping to "hidden".
            if (info.PlayerList != null) { srv.Players = ToRows(srv, info.PlayerList); srv.PlayersAt = DateTime.Now; }
            else if (info.Players == 0 || DateTime.Now - srv.PlayersAt > TimeSpan.FromMinutes(2)) srv.Players = null;
            srv.PlayersHidden = info.Players > 0 && srv.Players == null;
            return true;
        }
        catch
        {
            srv.PingMs = -1;
            srv.PingText = "—";
            srv.PlayersText = "—";
            srv.Players = null;
            return false;
        }
    }

    void AutoQuery_Click(object sender, RoutedEventArgs e) { _s.AutoQuery = AutoQueryBox.IsChecked == true; SaveSettings(); }

    void CopyServers_Click(object sender, RoutedEventArgs e)
    {
        var list = SelectedOrAll();
        if (list.Count == 0) return;
        Clipboard.SetText(string.Join("\r\n", list.Select(s => s.Key)));
        Toast($"{list.Count} server(s) copied.", true);
    }

    async void RemoveServers_Click(object sender, RoutedEventArgs e)
    {
        var sel = ServerList.SelectedItems.Cast<ServerItem>().ToList();
        if (sel.Count == 0) { Toast("Select the servers to remove first.", false); return; }
        foreach (var s in sel) _servers.Remove(s);
        SaveServers();
        var still = _servers.Select(s => s.Ip).ToHashSet();
        var gone = sel.Select(s => s.Ip).Where(ip => !still.Contains(ip)).Distinct().ToList();
        // The kill switch stops covering these IPs only after their routes are gone (no leak window for the ones that stay).
        if (!_demo && gone.Count > 0) await _guard.RemoveIps(gone, _s);
        Log.Info($"Removed {sel.Count} server(s).");
        await SyncNow(force: true);
        UpdateStatusUi();
    }

    void SrvCopyConnect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServerItem s) return;
        try { Clipboard.SetText("connect " + s.Key); Toast($"Copied: connect {s.Key}", true); } catch { Toast("Couldn't copy. Try again.", false); }
    }

    void SrvFav_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServerItem s) return;
        s.Favorite = !s.Favorite;
        _serverView.Refresh();
        SaveServers();
    }

    async void SrvConnect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServerItem s) await Join(s);
    }

    async void ServerList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InPlayerList(e.OriginalSource as DependencyObject)) return;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ServerItem s) await Join(s);
    }

    /// <summary>Join from a button, the list, a notice or a free slot: with the command feedback.</summary>
    Task Join(ServerItem s, bool fromSlotWait = false) => RunCommand("Join", $"joining {s.Title}…", () => Connect(s, fromSlotWait));

    /// <summary>Joins through the phone after all the checks. True when CS2 was sent to the server (or a slot wait started).</summary>
    async Task<bool> Connect(ServerItem s, bool fromSlotWait = false, bool openWith = true)
    {
        Step($"joining {s.Title}…");
        if (!_demo)
        {
            if (_restored) { Toast("Protection is off since Restore network. Click Run leak test first.", false); return false; }
            await SyncNow(force: true);
            if (_distrust || _cDiff.Ok == false) { Toast("Not joining: the phone IP is the same as your home IP.", false); return false; }
            var fullOk = _last?.Egress != null && _last.EffectiveMode == "all" && _last.FullModeOk;
            if (s.Protected != true && !fullOk)
            {
                Toast($"Not joining {s.Key}: protection is not confirmed ({s.RouteText}).", false);
                return false;
            }
            // The two known causes of the "No Steam logon" kick: Steam in offline mode, or Steam on a different network than CS2.
            if (Steam.WantsOffline(Steam.ActiveSteamId()) && !await Ask("Steam is in offline mode",
                    "In offline mode Steam does not confirm your account, and the server kicks you with \"No Steam logon\" after a few minutes.\n\n" +
                    "Turn off \"Offline\" in Accounts and log in to the account again.", "Join anyway"))
                return false;
            if (ActiveAccount() is AccountItem acc && acc.BanFor(s.Key, s.ServerName, s.Network) is { } ban && !await Ask($"{acc.Name} was banned here",
                    (ban.Key == s.Key ? $"{s.Title} banned {acc.Name}" : $"{ban.Value?.Name ?? ban.Key}, on the same network as {s.Title}, banned {acc.Name}")
                    + $" on {Fmt.When(ban.Value?.At)}. Networks usually share their ban list, and if the ban is on the account, a new IP won't help.\n\nSwitch ({_s.HotkeySwitch}) changes the IP and the account together.",
                    "Join anyway"))
                return false;
            if (!await WaitAfterCrash(s)) return false;
            if (!fromSlotWait && _last?.Egress is Adapter eg && await QueryOne(s, eg) && s.MaxPlayers > 0 && s.PlayerCount >= s.MaxPlayers)
            {
                if (!await Ask("Server is full", $"{s.Title} is full ({s.PlayersText}). Wait for a free slot? The app checks every 5 s and joins (or tells you) when one opens.", "Wait for a slot"))
                    return false;
                StartSlotWait(s);
                return true;
            }
            if (!SteamReady && !await WaitSteamReady(60) && !await Ask("Steam has no session yet",
                    "Steam has not reconnected to Valve yet (for example, after you changed the IP). If you join now, the server kicks you with \"No Steam logon\" after a few minutes.",
                    "Join anyway"))
                return false;
            if (_s.SteamOnEgress && _last?.SteamOnEgress == false && await Ask("Steam is on another network",
                    "CS2 goes through the phone, but Steam is connected through your home network. When Steam reconnects, the server kicks you with \"No Steam logon\".\n\n" +
                    $"Restart Steam through the phone and join {s.Title}?" + (Steam.Cs2Running ? " CS2 closes and opens again." : ""),
                    "Restart and join", "Join anyway"))
                return await RestartSteamAndConnect(s, openWith);
            if (CommandCancelled) return false;
        }
        if (_demo) { Toast($"Demo: joining {s.Title}.", true); MarkLast(s); return true; }
        try
        {
            if (!Steam.Cs2Running && Steam.Exe is string steam && File.Exists(steam))
            {
                // With CS2 closed, a connect sent at launch gets lost while the main menu loads: open CS2, wait for the menu, then connect.
                if (openWith) OpenWithCs2();
                var log = Cs2Console.LogPath(_s.Cs2Path ?? Steam.Cs2Path(_s));
                long from = 0;
                try { from = log != null && File.Exists(log) ? new FileInfo(log).Length : 0; } catch { }
                Steam.Launch("-applaunch 730 -condebug -conclearlog");
                JoinStarted(s);
                return await ConnectWhenMenuReady(s, log, from);
            }
            Steam.OpenUrl($"steam://connect/{s.Key}");
            JoinStarted(s);
            return true;
        }
        catch (Exception ex) { Toast("Couldn't open Steam: " + ex.Message, Tone.Error); return false; }
    }

    void JoinStarted(ServerItem s)
    {
        Diag.Write("join", new { server = s.Key, name = s.Title, isProtected = s.Protected, mode = _last?.EffectiveMode, steamOnEgress = _last?.SteamOnEgress, steamIps = _last?.Steam.LocalIps });
        Log.Ok($"Joining {s.Key} through the phone.");
        Toast($"Opening {s.Title} in Steam…", true);
        MarkLast(s);
    }

    /// <summary>After opening CS2: waits for its main menu (the console shows the menu's "loopback" map), then joins. Up to 2 min.</summary>
    async Task<bool> ConnectWhenMenuReady(ServerItem s, string log, long from)
    {
        ShowProgress($"Opening CS2. It joins {s.Title} when the main menu is ready…");
        using var wait = CommandWait();
        try
        {
            var ready = false;
            for (var i = 0; i < 120 && !ready; i++)
            {
                await Task.Delay(1000);
                if (CommandCancelled) return false;
                if (!Steam.Cs2Running || log == null) continue;
                try
                {
                    using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (f.Length < from) from = 0;   // a new console.log
                    f.Seek(from, SeekOrigin.Begin);
                    ready = new StreamReader(f).ReadToEnd().Contains("Connected to 'loopback", StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            }
            if (!Steam.Cs2Running) { Log.Warn($"CS2 didn't open, so {s.Key} wasn't joined."); Toast($"CS2 didn't open, so {s.Title} wasn't joined.", false); return false; }
            if (_restored || _distrust) { Log.Warn($"Not joining {s.Key}: protection changed while CS2 was opening."); Toast($"Not joining {s.Title}: protection changed while CS2 was opening.", false); return false; }
            if (ready) await Task.Delay(2000);
            Steam.OpenUrl($"steam://connect/{s.Key}");
            Log.Info($"CS2 menu {(ready ? "ready" : "not seen after 2 min")}: joining {s.Key}.");
            return true;
        }
        catch (Exception ex) { Log.Error("Join after opening CS2: " + ex.Message); return false; }
        finally { HideProgress(); }
    }

    // ------------------------------------------------------------ Full server: wait for a slot

    ServerItem _slotServer;
    readonly System.Windows.Threading.DispatcherTimer _slotTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool _slotChecking;

    void StartSlotWait(ServerItem s)
    {
        _slotServer = s;
        SlotAutoBox.IsChecked = true;
        SlotJoinBtn.Visibility = Visibility.Collapsed;
        SlotText.Text = $"Waiting for a slot on {s.Title} ({s.PlayersText}).";
        SlotBar.Visibility = Visibility.Visible;
        Log.Info($"Waiting for a free slot on {s.Key} ({s.PlayersText}).");
        _slotTimer.Tick -= SlotTick;
        _slotTimer.Tick += SlotTick;
        _slotTimer.Start();
    }

    void StopSlotWait()
    {
        _slotTimer.Stop();
        _slotServer = null;
        SlotBar.Visibility = Visibility.Collapsed;
    }

    async void SlotTick(object sender, EventArgs e)
    {
        var s = _slotServer;
        if (s == null || _slotChecking) return;
        if (!_servers.Contains(s)) { StopSlotWait(); return; }
        if (_last?.Egress is not Adapter eg) { SlotText.Text = $"Waiting for a slot on {s.Title}: the phone is not connected."; return; }
        _slotChecking = true;
        try
        {
            if (!await QueryOne(s, eg) || s.MaxPlayers == 0) return;
            if (s.PlayerCount >= s.MaxPlayers) { SlotText.Text = $"Waiting for a slot on {s.Title} ({s.PlayersText})."; return; }
            System.Media.SystemSounds.Asterisk.Play();
            if (SlotAutoBox.IsChecked == true)
            {
                StopSlotWait();
                Log.Ok($"A slot opened on {s.Key} ({s.PlayersText}): joining.");
                await Join(s, fromSlotWait: true);
                return;
            }
            _slotTimer.Stop();
            SlotText.Text = $"A slot is free on {s.Title} ({s.PlayersText}).";
            SlotJoinBtn.Visibility = Visibility.Visible;
            Log.Ok($"A slot opened on {s.Key} ({s.PlayersText}).");
            Toast($"A slot is free on {s.Title}. Join from Servers.", Tone.Info);
        }
        finally { _slotChecking = false; }
    }

    async void SlotJoin_Click(object sender, RoutedEventArgs e)
    {
        var s = _slotServer;
        StopSlotWait();
        if (s != null) await Join(s);
    }

    void SlotCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_slotServer != null) Log.Info($"Stopped waiting for a slot on {_slotServer.Key}.");
        StopSlotWait();
    }

    async Task<bool> RestartSteamAndConnect(ServerItem s, bool openWith = true)
    {
        ShowProgress("Restarting Steam through the phone…");
        try
        {
            _cs2ClosedByAppAt = DateTime.Now;   // closed on purpose: that exit is not a crash
            foreach (var p in Process.GetProcessesByName("cs2")) try { p.Kill(true); } catch { }
            for (var i = 0; i < 20 && Steam.Cs2Running; i++) await Task.Delay(150);
            // Valve routes already go through the phone: the new Steam connects that way, then CS2 opens and joins from its menu.
            var log = Cs2Console.LogPath(_s.Cs2Path ?? Steam.Cs2Path(_s));
            long from = 0;
            try { from = log != null && File.Exists(log) ? new FileInfo(log).Length : 0; } catch { }
            await Steam.Restart((_s.SteamSilent ? "-silent " : "") + "-applaunch 730 -condebug -conclearlog");
            if (openWith) OpenWithCs2();
            Diag.Write("steam-restarted", new { server = s.Key });
            Log.Ok($"Steam restarted through the phone; CS2 opens and joins {s.Key}.");
            Toast($"Steam is restarting. CS2 opens and joins {s.Title}.", true);
            MarkLast(s);
            HideProgress();
            return await ConnectWhenMenuReady(s, log, from);
        }
        catch (Exception ex) { Log.Error("Restart Steam: " + ex.Message); Toast(ex.Message, Tone.Error); return false; }
        finally { HideProgress(); }
    }

    /// <summary>Saves the server as the last one (in the app and in the logged-in account) and highlights it in the list.</summary>
    void MarkLast(ServerItem s)
    {
        _s.LastServer = s.Key;
        _s.LastServerAt = DateTime.Now;
        SaveSettings();
        ShowLastServer();
        var activeId = _demo ? null : Steam.ActiveSteamId();
        var acc = _accounts.FirstOrDefault(a => a.SteamId == activeId) ?? _accounts.FirstOrDefault(a => a.Active);
        if (acc != null)
        {
            acc.Meta.LastServer = s.Key;
            acc.Meta.LastServerName = s.ServerName ?? s.Label;
            acc.Meta.LastServerAt = _s.LastServerAt;
            acc.RefreshAll();
            SaveAccounts();
        }
    }

    void ShowLastServer()
    {
        // Old versions only stored the last server per account: use the most recent across all accounts.
        if (_s.LastServer == null && _accounts.Select(a => a.Meta).Where(m => m.LastServer != null).MaxBy(m => m.LastServerAt) is AccountMeta m)
        {
            _s.LastServer = m.LastServer;
            _s.LastServerAt = m.LastServerAt;
        }
        foreach (var x in _servers)
        {
            x.IsLast = x.Key == _s.LastServer;
            x.LastAt = x.IsLast ? _s.LastServerAt : null;
        }
        ApplyServerBans();
        _serverView.Refresh();
        UpdateRejoinBtn();
    }
}
