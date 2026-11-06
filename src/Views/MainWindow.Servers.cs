using System.Collections.ObjectModel;
using System.ComponentModel;
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
        var list = _demo ? null : Store.Load<List<ServerItem>>(Paths.File(AppIds.Files.Servers), () => null);
        if (list == null && !_demo)
        {
            // Migration: the v8 list (servers.json next to the .exe) moves to SRT_Dados.
            var old = Path.Combine(Paths.AppDir, AppIds.Files.Servers);
            list = File.Exists(old) ? Store.Load<List<ServerItem>>(old, () => null) : null;
        }
        if (list == null)
        {
            // First run: the list starts empty (paste servers in Servers).
            var defaults = new List<(string Ip, int Port)>();
            AddDefaultServers(defaults);
            list = defaults.Select(d => new ServerItem { Ip = d.Ip, Port = d.Port }).ToList();
        }
        // Lists from before beta.6 could hold local addresses; they go, and the Log says so
        // Review C21: only private, carrier NAT and link-local are "local"; the rest that isn't a public IPv4, the ports out of range
        // and the repeats get their own line
        var bad = list.Where(s => !ValidIp(s.Ip)).ToList();
        var local = bad.Count(s => IsLocalNet(s.Ip));
        if (local > 0) Log.Warn($"Removed {Fmt.Count(local, "address", "addresses")} from the server list: local network addresses can't be game servers.");
        if (bad.Count > local) Log.Warn($"Removed {Fmt.Count(bad.Count - local, "address", "addresses")} from the server list: not a valid public IPv4 ("
            + string.Join(", ", bad.Where(s => !IsLocalNet(s.Ip)).Take(5).Select(s => s.Ip ?? "empty")) + (bad.Count - local > 5 ? "…" : "") + ").");
        var badPort = list.Count(s => ValidIp(s.Ip) && s.Port is < 1 or > 65535);
        var twice = list.Where(s => ValidIp(s.Ip) && s.Port is >= 1 and <= 65535).GroupBy(s => s.Key).Sum(g => g.Count() - 1);
        if (badPort > 0) Log.Warn($"Removed {Fmt.Count(badPort, "server")} from the server list: the port isn't between 1 and 65535.");
        if (twice > 0) Log.Info($"Removed {Fmt.Count(twice, "server")} from the server list that {(twice == 1 ? "was" : "were")} in it twice.");
        foreach (var s in list.Where(s => ValidIp(s.Ip) && s.Port is >= 1 and <= 65535).GroupBy(s => s.Key).Select(g => g.First()))
        {
            s.PlayersText = "—"; s.PingText = "—"; s.Map = "—";
            _servers.Add(s);
        }
        if (!_demo) SaveServers();
    }

    void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var n = ServerList.SelectedItems.Count;
        RemoveBtn.IsEnabled = n > 0;
        // The button says what it asks (3 nov.: with one server marked, Refresh only ever asked that one)
        QueryBtn.Content = RefreshLabel(n, !string.IsNullOrEmpty(ServerSearch.Text.Trim()));
    }

    public static string RefreshLabel(int selected) => RefreshLabel(selected, false);

    /// <summary>With a search, none marked asks only the servers shown (review C19: it said "Refresh all").</summary>
    public static string RefreshLabel(int selected, bool filtered) =>
        selected <= 0 ? (filtered ? "Refresh shown" : "Refresh all") : selected == 1 ? "Refresh 1" : $"Refresh {selected}";

    /// <summary>
    /// Unmarking a server (3 nov.: there was no way back to none marked): a click on the only marked row, a click on the empty
    /// part of the list, or Escape. Ctrl+click still adds and takes away as usual; buttons in a row and double-click work as before.
    /// </summary>
    void ServerList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None) return;
        if (OnRowControl(e.OriginalSource as DependencyObject, ServerList, out var row)) return;
        if (row == null) { ServerList.UnselectAll(); return; }
        if (row.IsSelected && ServerList.SelectedItems.Count == 1 && !InPlayerList(e.OriginalSource as DependencyObject)) { ServerList.UnselectAll(); e.Handled = true; }
    }

    /// <summary>
    /// The click landed on a button, scroll bar or text box inside a row (the star, the player count, Join), not on the row itself.
    /// <paramref name="row"/> is the row it is in (null: the empty part of the list).
    /// </summary>
    static bool OnRowControl(DependencyObject d, DependencyObject list, out ListBoxItem row)
    {
        row = null;
        for (; d != null && !ReferenceEquals(d, list); d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.ScrollBar or TextBox) return true;
            if (d is ListBoxItem li) { row = li; return false; }
        }
        return false;
    }

    void ServerList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ServerList.SelectedItems.Count > 0) { ServerList.UnselectAll(); e.Handled = true; }
    }

    void SaveServers() { if (!_demo) Store.Save(Paths.File(AppIds.Files.Servers), _servers.ToList()); }

    /// <summary>A public IPv4 a game server can have. Local network addresses (the home router, the phone, a LAN) are refused:
    /// the kill switch would block them on every adapter except the phone and route them through it, which can cut the PC's DNS.</summary>
    public static bool ValidIp(string ip)
    {
        if (ip == null) return false;
        var p = ip.Split('.');
        // Digits only, 1 to 3 of them, no leading zero: int.TryParse also takes " 1" and "+1" (a space would split the netsh argument
        // it goes into), and "010" is read as 8 by some Windows tools.
        if (p.Length != 4 || !p.All(x => x.Length is >= 1 and <= 3 && x.All(char.IsAsciiDigit) && (x.Length == 1 || x[0] != '0') && int.Parse(x) <= 255)) return false;
        int a = int.Parse(p[0]), b = int.Parse(p[1]);
        if (a == 0 || a == 127 || a >= 224) return false;
        if (IsLocalNet(ip)) return false;
        if (a == 198 && b is 18 or 19) return false;                                                   // benchmarking
        return ip != Net.EchoIp;                                                                       // the app's own IP test
    }

    /// <summary>A local network address: private networks, carrier NAT and link-local (what the Log calls "local network").</summary>
    public static bool IsLocalNet(string ip)
    {
        var p = ip?.Split('.');
        if (p is not { Length: 4 } || !int.TryParse(p[0], out var a) || !int.TryParse(p[1], out var b)) return false;
        return a == 10 || (a == 172 && b is >= 16 and <= 31) || (a == 192 && b == 168)   // private networks
            || (a == 100 && b is >= 64 and <= 127) || (a == 169 && b == 254);            // carrier NAT, link-local
    }

    int ParseInto(string text, out List<string> ignored, out int dupes)
    {
        ignored = new();
        var port = int.TryParse(DefaultPortBox.Text.Trim(), out var dp) && dp is >= 1 and <= 65535 ? dp : 27015;
        var keys = ParseServers(text, port, _servers.Select(s => s.Key).ToHashSet(), ignored, out dupes);
        foreach (var k in keys)
        {
            var at = k.LastIndexOf(':');
            _servers.Add(new ServerItem { Ip = k[..at], Port = int.Parse(k[(at + 1)..]), PlayersText = "—", PingText = "—", Map = "—" });
        }
        return keys.Count;
    }

    public static List<string> ParseServers(string text, int defaultPort) => ParseServers(text, defaultPort, new HashSet<string>(), new List<string>(), out _);

    /// <summary>
    /// The new "IP:port" in a pasted text, in order. <paramref name="known"/> are the ones in the list (it grows with each new one, so
    /// a repeat in the same text counts once); <paramref name="dupes"/> counts the ones already there (review C4: a text with no IPv4
    /// said "already in the list"); what isn't a public IP:port goes to <paramref name="ignored"/>.
    /// </summary>
    public static List<string> ParseServers(string text, int defaultPort, ISet<string> known, List<string> ignored, out int dupes)
    {
        var added = new List<string>();
        dupes = 0;
        foreach (Match m in ServerRegex.Matches(text ?? ""))
        {
            var ip = string.Join(".", m.Groups[1].Value.Split('.').Select(o => int.TryParse(o, out var n) ? n.ToString() : o));
            if (!ValidIp(ip)) { ignored.Add(m.Value.Trim()); continue; }
            var p = defaultPort;
            if (m.Groups["port"].Success && (!int.TryParse(m.Groups["port"].Value, out p) || p < 1 || p > 65535)) { ignored.Add(m.Value.Trim()); continue; }
            if (!known.Add($"{ip}:{p}")) { dupes++; continue; }
            added.Add($"{ip}:{p}");
        }
        return added;
    }

    /// <summary>Nothing new in the text: already in the list, not a public IP:port, or no IPv4 at all (a name or an IPv6).</summary>
    public static string NothingAdded(int dupes, int ignored) =>
        ignored > 0 ? "No valid IP:port." + (dupes > 0 ? $" {dupes} already in the list." : "")
        : dupes > 0 ? "Those servers are already in the list."
        : "No IPv4 address found. The app needs the server's IP:port, e.g. 1.2.3.4:27015 (names and IPv6 aren't supported).";

    async Task AddFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { Toast("Nothing to add.", false); return; }
        var n = ParseInto(text, out var ignored, out var dupes);
        SaveServers();
        ShowLastServer();
        if (n == 0 && ignored.Count == 0)
        {
            if (dupes == 0) Log.Info("Add servers: no IPv4 address in the text (names and IPv6 aren't supported).");
            Toast(NothingAdded(dupes, 0), false);
            return;
        }
        Log.Info($"Added {Fmt.Count(n, "server")}." + (ignored.Count > 0 ? " Ignored (not a public IP:port): " + string.Join(", ", ignored.Take(20)) + (ignored.Count > 20 ? $" and {ignored.Count - 20} more" : "") : ""));
        Toast(n > 0 ? AddedText(n, ignored.Count, _protOn, _s.NoPhone, _last?.Egress != null) : NothingAdded(dupes, ignored.Count), n > 0);
        _s.DefaultPort = int.TryParse(DefaultPortBox.Text, out var dp) ? dp : 27015;
        SaveSettings();
        await SyncNow(force: true);
        await QueryServers(true, queue: true);   // the new ones get asked even while the 30 s refresh runs
    }

    /// <summary>
    /// What adding means now (review C1: "added and protected" also showed with protection off and in No phone, where nothing is
    /// blocked or routed): through the phone, blocked until the phone is ready, or on the home IP.
    /// </summary>
    public static string AddedText(int added, int ignored, bool protOn, bool noPhone, bool egress)
    {
        var one = added == 1;
        var now = noPhone ? (one ? "No phone mode: it sees your home IP." : "No phone mode: they see your home IP.")
            : !protOn ? (one ? "Protection is off: it sees your home IP until you turn it on." : "Protection is off: they see your home IP until you turn it on.")
            : egress ? (one ? "It goes through the phone." : "They go through the phone.")
            : one ? "It stays blocked until the phone is connected and checked." : "They stay blocked until the phone is connected and checked.";
        return $"{Fmt.Count(added, "server")} added. {now}" + (ignored > 0 ? $" {ignored} ignored." : "");
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
        QueryBtn.Content = RefreshLabel(ServerList.SelectedItems.Count, !string.IsNullOrEmpty(q));
    }

    List<ServerItem> SelectedOrAll()
    {
        var sel = ServerList.SelectedItems.Cast<ServerItem>().ToList();
        return sel.Count > 0 ? sel : _serverView.Cast<ServerItem>().ToList();
    }

    /// <summary>
    /// Why there is no protected connection, in the Guard's words (review C2: with the phone plugged in but its IP still being
    /// checked, or its Wi-Fi on, the app said "Phone disconnected"). The Guard only leaves it empty when there is no phone at all.
    /// </summary>
    public static string NoLinkWhy(GuardReport rep) => rep?.EgressProblem ?? "The phone isn't connected.";

    /// <summary>Join stopped with protection on: the connection's reason, or this server's route when the connection is there.</summary>
    public static string JoinBlockedText(string title, GuardReport rep, string routeText) => rep?.Egress == null
        ? $"Not joining {title}. {NoLinkWhy(rep)}"
        : $"Not joining {title}: its route through the phone isn't confirmed ({routeText}).";

    /// <summary>The slot wait can't ask the server now: no connection, protection turned off since, or the phone's reason.</summary>
    public static string SlotNoLinkText(string title, bool onHome, bool protOn, GuardReport rep) =>
        onHome ? $"Waiting for a slot on {title}: no connection."
        : !protOn ? $"Waiting for a slot on {title}: protection is off."
        : $"Waiting for a slot on {title}. {NoLinkWhy(rep)}";

    async void QueryServers_Click(object sender, RoutedEventArgs e) => await QueryServers(false);

    /// <summary>A Refresh or new servers came in while the servers were being asked: one more round right after (review C20).</summary>
    bool _queryAgain;

    async Task QueryServers(bool quiet, bool queue = false)
    {
        if (_demo) return;
        if (_querying)
        {
            if (!quiet || queue) _queryAgain = true;
            if (!quiet) Toast("Already asking the servers: one moment.", Tone.Info);
            return;
        }
        // No phone mode: the game uses the home connection, so the servers are asked from there too.
        // Protection off with "Use the app with protection off": the same, on purpose.
        var egress = OnHome && _last?.Unprotected == true ? HomeAdapter : _last?.Egress;
        if (egress?.LocalIp == null)
        {
            if (!quiet) Toast(OnHome ? "No connection to ask the servers through."
                : !_protOn ? "Protection is off: the servers aren't asked over your home network. Turn it on to see players and ping (or Settings > Use the app with protection off)."
                : NoLinkWhy(_last) + " Servers aren't asked over your home network.", false);
            // Nothing is asked now: the last answers would read as live (review C6)
            foreach (var srv in _servers) ClearLive(srv);
            return;
        }
        _querying = true;
        try
        {
            var targets = quiet ? _servers.ToList() : SelectedOrAll();
            var gate = new SemaphoreSlim(8);
            var ok = 0;
            var notAsked = OnHome ? 0 : targets.Count(srv => !Routed(srv));
            await Task.WhenAll(targets.Select(async srv =>
            {
                await gate.WaitAsync();
                try { if (await QueryOne(srv, egress)) Interlocked.Increment(ref ok); }
                finally { gate.Release(); }
            }));
            if (!quiet) Toast(RespondedText(ok, targets.Count, notAsked), ok > 0);
        }
        finally { _querying = false; }
        ApplyServerBans();   // the network of a ban is known by name only after the server answers
        await CheckFollowed();
        if (_queryAgain) { _queryAgain = false; await QueryServers(true); }
    }

    /// <summary>Name, map, players and ping of one server, only through the phone. False if it didn't answer (or isn't routed).</summary>
    async Task<bool> QueryOne(ServerItem srv, Adapter egress)
    {
        if (!OnHome && !Routed(srv)) { ClearLive(srv); return false; }
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
            srv.Answered = true;
            Log.Again("servers-query|");
            return true;
        }
        catch (Exception ex)
        {
            if (!A2S.NoAnswer(ex)) Log.WarnOnce("servers-query|" + ex.GetType().Name, $"Servers: asking a server failed ({ex.GetType().Name}: {ex.Message}), so it shows no ping or players.");
            ClearLive(srv);
            srv.Answered = false;
            return false;
        }
    }

    /// <summary>With protection on, a server is only asked once Windows sends it through the phone.</summary>
    bool Routed(ServerItem srv) => _last?.RouteOk.TryGetValue(srv.Ip, out var routed) == true && routed;

    /// <summary>
    /// A row without an answer now (not asked, or no answer): no map, players, bar or ping from an older answer. The name stays,
    /// it is still that server (review C6: "12/20" and "35 ms" stayed for good after protection was turned off).
    /// </summary>
    public static void ClearLive(ServerItem srv)
    {
        srv.PingMs = -1;
        srv.PingText = "—";
        srv.PlayersText = "—";
        srv.Map = "—";
        srv.Players = null;
        srv.PlayersHidden = false;
        srv.PlayerCount = 0;
        srv.MaxPlayers = 0;
        srv.Answered = null;
    }

    /// <summary>The Refresh toast: servers not asked (no route through the phone yet) aren't counted as not answering.</summary>
    public static string RespondedText(int ok, int total, int notAsked) =>
        $"{ok} of {Fmt.Count(total, "server")} responded" + (notAsked > 0 ? $" ({notAsked} not asked: not routed through the phone yet)." : ".");

    void AutoQuery_Click(object sender, RoutedEventArgs e) { _s.AutoQuery = AutoQueryBox.IsChecked == true; SaveSettings(); }

    void CopyServers_Click(object sender, RoutedEventArgs e)
    {
        var list = SelectedOrAll();
        if (list.Count == 0) return;
        Clipboard.SetText(string.Join("\r\n", list.Select(s => s.Key)));
        Toast($"{Fmt.Count(list.Count, "server")} copied.", true);
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
        Log.Info($"Removed {Fmt.Count(sel.Count, "server")}.");
        UpdateQuickInfo();   // the last server may be gone: no "+ rejoin" then
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
        // The double click reaches the list even when a button in the row took both clicks (review C9: the star, the player count
        // or the row's Join also joined the server)
        if (OnRowControl(e.OriginalSource as DependencyObject, ServerList, out _) || InPlayerList(e.OriginalSource as DependencyObject)) return;
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
            // No phone mode: you chose the home connection, so there is no protection to wait for.
            // Protection off: joining would show the server your home IP. One question turns it on and goes on with the join.
            // With "Use the app with protection off" on, you chose that: the join goes ahead on the home connection.
            if (!_protOn && !OnHome)
            {
                if (!await Ask("Protection is off", $"{s.Title} would see your home IP. Turn protection on and join through the phone?", "Turn on and join")) return false;
                if (!await TurnProtectionOn(ask: false)) return false;
            }
            await SyncNow(force: true);
            if (!OnHome && (_distrust || _cDiff.Ok == false)) { Toast("Not joining: the phone IP is the same as your home IP.", false); return false; }
            var fullOk = _last?.Egress != null && _last.EffectiveMode == "all" && _last.FullModeOk;
            if (!OnHome && s.Protected != true && !fullOk)
            {
                Toast(JoinBlockedText(s.Title, _last, s.RouteText), false);
                return false;
            }
            // The two known causes of the "No Steam logon" kick: Steam in offline mode, or Steam on a different network than CS2.
            if (Steam.WantsOffline(Steam.InUseSteamId()) && !await Ask("Steam is in offline mode",
                    "In offline mode Steam does not confirm your account, and the server kicks you with \"No Steam logon\" after a few minutes.\n\n" +
                    "Turn off \"Offline mode\" in Accounts and log in to the account again.", "Join anyway"))
                return false;
            if (ActiveAccount() is AccountItem acc && acc.BanFor(s.Key, s.ServerName, s.Network) is { } ban && !await Ask($"{acc.Name} was banned here",
                    (ban.Key == s.Key ? $"{s.Title} banned {acc.Name}" : $"{ban.Value?.Name ?? ban.Key}, on the same network as {s.Title}, banned {acc.Name}")
                    + $" on {Fmt.When(ban.Value?.At)}. Networks usually share their ban list, and if the ban is on the account, a new IP won't help.\n\nNew IP + next account ({_s.HotkeySwitch}) changes the IP and the account together.",
                    "Join anyway"))
                return false;
            if (!await WaitAfterCrash(s)) return false;
            if (!fromSlotWait && (OnHome ? HomeAdapter : _last?.Egress) is Adapter eg && await QueryOne(s, eg) && s.MaxPlayers > 0 && s.PlayerCount >= s.MaxPlayers)
            {
                if (await Ask("Server is full", FullText(s.Title, s.PlayersText), "Wait for a slot", "Join anyway")) { StartSlotWait(s); return true; }
                if (!_dialogCancelClicked) return false;   // Escape: neither
                Log.Info($"{s.Key} shows full ({s.PlayersText}): joining anyway, as asked.");
            }
            if (!SteamReady && !await WaitSteamReady(60) && !await Ask("Steam has no session yet",
                    "Steam has not reconnected to Valve yet (for example, after you changed the IP). If you join now, the server kicks you with \"No Steam logon\" after a few minutes.",
                    "Join anyway"))
                return false;
            if (_s.SteamOnEgress && _last?.SteamOnEgress == false)
            {
                if (await Ask("Steam is on another network",
                        "CS2 goes through the phone, but Steam is connected through your home network. When Steam reconnects, the server kicks you with \"No Steam logon\".\n\n" +
                        $"Restart Steam through the phone and join {s.Title}?" + (Steam.Cs2Running ? " CS2 closes and opens again." : ""),
                        "Restart and join", "Join anyway"))
                    return await RestartSteamAndConnect(s, openWith);
                if (!_dialogCancelClicked) return false;   // X or Escape: neither, so no join
            }
            if (CommandCancelled) return false;
        }
        if (_demo) { Toast($"Demo: joining {s.Title}.", true); MarkLast(s); return true; }
        try
        {
            if (!Steam.Cs2Running && Steam.Exe is string steam && File.Exists(steam))
            {
                // With CS2 closed, a connect sent at launch gets lost while the main menu loads: open CS2, wait for the menu, then connect.
                if (openWith) OpenWithCs2();
                var log = Cs2ConsoleLog();
                var menu = new ConsoleTail();
                menu.StartAt(log);
                ConfigsBeforeCs2();
                Steam.Launch(Cs2Launch.SteamArgs);
                JoinStarted(s);
                return await ConnectWhenMenuReady(s, log, menu);
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
        Log.Ok(_s.NoPhone ? $"Joining {s.Key} on your home connection (no phone mode)." : OnHome ? $"Joining {s.Key} on your home connection (protection is off)." : $"Joining {s.Key} through the phone.");
        Toast($"Opening {s.Title} in Steam…", true);
        MarkLast(s);
    }

    /// <summary>After opening CS2: waits for its main menu (the console shows the menu's "loopback" map), then joins. Up to 2 min.</summary>
    async Task<bool> ConnectWhenMenuReady(ServerItem s, string log, ConsoleTail menu)
    {
        ShowProgress($"Opening CS2. It joins {s.Title} when the main menu is ready. Cancel at the top stops the join…");
        using var wait = CommandWait();
        try
        {
            var ready = false;
            for (var i = 0; i < 120 && !ready; i++)
            {
                await Task.Delay(1000);
                if (CommandCancelled) return false;
                if (!Steam.Cs2Running || log == null) continue;
                ready = menu.ReadNew(log).Any(Cs2Console.IsMenu);   // a new console.log (-conclearlog) is read from its start
            }
            if (!Steam.Cs2Running) { Log.Warn($"CS2 didn't open, so {s.Key} wasn't joined."); Toast($"CS2 didn't open, so {s.Title} wasn't joined.", false); return false; }
            if (!OnHome && (!_protOn || _distrust)) { Log.Warn($"Not joining {s.Key}: protection changed while CS2 was opening."); Toast($"Not joining {s.Title}: protection changed while CS2 was opening.", false); return false; }
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

    /// <summary>
    /// The list's count is what the server shows: many keep spectator or reserved slots it doesn't count (the "full" server a console
    /// connect still got into, 3 nov.). Join anyway asks the server itself; a real "full" comes back from CS2's console and the app
    /// tries again every 15 s (RetryFull). CS2 has no command to ask for a spectator slot: with the teams full the server puts you there.
    /// </summary>
    public static string FullText(string title, string players) =>
        $"{title} shows as full ({players}). Many servers keep spectator or reserved slots the list doesn't count: Join anyway asks the server "
        + "itself, and if the teams are full you come in as a spectator (pick a team when a slot frees up).\n\n"
        + "Or wait for a slot: the app checks every 5 s and joins (or tells you) when one opens.";

    /// <summary>
    /// A free slot (or the retry on a full server) only joins by itself when nothing else runs and no question is open (review C10:
    /// the join was refused as "still busy" and the wait was gone, or it took over a waiting command and closed your question).
    /// </summary>
    public static bool SlotAutoJoins(bool autoOn, bool busy, bool dialogOpen) => autoOn && !busy && !dialogOpen;

    /// <summary>A command, an IP change, an account switch or a rejoin is running: a new command would be refused or take over.</summary>
    bool CommandBusy => _command != null || _changingIp || _switching || _rejoining;

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
        // On the home connection on purpose (No phone, or protection off with the option) the slot is checked from there, as Connect does
        if ((OnHome ? HomeAdapter : _last?.Egress) is not Adapter eg) { SlotText.Text = SlotNoLinkText(s.Title, OnHome, _protOn, _last); return; }
        _slotChecking = true;
        try
        {
            if (!await QueryOne(s, eg) || s.MaxPlayers == 0) return;
            if (s.PlayerCount >= s.MaxPlayers) { SlotText.Text = $"Waiting for a slot on {s.Title} ({s.PlayersText})."; return; }
            System.Media.SystemSounds.Asterisk.Play();
            var auto = SlotAutoBox.IsChecked == true;
            if (SlotAutoJoins(auto, CommandBusy, Dialog.Visibility == Visibility.Visible))
            {
                StopSlotWait();
                Log.Ok($"A slot opened on {s.Key} ({s.PlayersText}): joining.");
                await Join(s, fromSlotWait: true);
                return;
            }
            _slotTimer.Stop();
            SlotText.Text = $"A slot is free on {s.Title} ({s.PlayersText}).";
            SlotJoinBtn.Visibility = Visibility.Visible;
            Log.Ok($"A slot opened on {s.Key} ({s.PlayersText})." + (auto ? " Not joined by itself: another command or a question was open." : ""));
            Toast(auto ? $"A slot is free on {s.Title}. The app was busy, so it didn't join by itself: join from Servers." : $"A slot is free on {s.Title}. Join from Servers.", Tone.Info);
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
            await _cs2.Close("the Steam restart");
            // Valve routes already go through the phone: the new Steam connects that way, then CS2 opens and joins from its menu.
            var log = Cs2ConsoleLog();
            var menu = new ConsoleTail();
            menu.StartAt(log);
            ConfigsBeforeCs2();
            await Steam.Restart(Cs2Launch.SteamRestartArgs(_s.SteamSilent), WhileSteamClosed);
            if (openWith) OpenWithCs2();
            Diag.Write("steam-restarted", new { server = s.Key });
            Log.Ok($"Steam restarted through the phone; CS2 opens and joins {s.Key}.");
            Toast($"Steam is restarting. CS2 opens and joins {s.Title}.", true);
            MarkLast(s);
            HideProgress();
            return await ConnectWhenMenuReady(s, log, menu);
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
        var activeId = _demo ? null : Steam.InUseSteamId();
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
        UpdateQuickInfo();   // New IP + next account says "+ rejoin" only with a last server to go back to
    }
}
