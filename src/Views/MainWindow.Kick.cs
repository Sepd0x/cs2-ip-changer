using System.Windows;
using SRT.Core;

namespace SRT;

// Kick or ban seen in CS2's console, "Change IP and rejoin" (button and its hotkey) and the switch (new IP, next account, rejoin)
public partial class MainWindow
{
    readonly ConsoleTail _cs2Console = new();
    DateTime _lastKickAt = DateTime.MinValue;
    KickKind _lastKickKind;
    DateTime? _cs2StartedAt, _cs2LogSeenAt;
    long _cs2LogLen = -1;
    bool _condebugHinted, _cs2LogOk, _rejoining;
    string _consoleServer;
    int _disconnectSamples;

    /// <summary>Every tick while CS2 runs: new console lines, looking for a kick or ban.</summary>
    void WatchCs2Console()
    {
        if (_demo) return;
        if (!Steam.Cs2Running)
        {
            // Quit straight from a match: CS2 wrote its network summary as it closed, and no line comes after it
            if (_cs2StartedAt != null && Cs2Launch.ConsoleLog(_s) is string last)
                foreach (var line in _cs2Console.ReadNew(last))
                    if (_netReport.Feed(line) is NetReport r) LogNetReport(r);
            if (_netReport.Flush() is NetReport pending) LogNetReport(pending);
            _cs2Console.Reset();
            _consoleServer = null;   // a server from the last session is not where the next one goes
            _kicks.Forget();
            _cs2StartedAt = _cs2LogSeenAt = null;
            _cs2LogLen = -1;
            _cs2LogOk = false;
            return;
        }
        _cs2StartedAt ??= Cs2Launch.StartedAt();
        var path = Cs2Launch.ConsoleLog(_s);
        if (path == null) return;
        CheckCondebug(path);
        // One disconnect writes several lines, and the first one isn't always the clearest ("kicked", then the ban code):
        // the strongest of the batch counts, with the furthest the join got and what the server said just before.
        foreach (var line in _cs2Console.ReadNew(path))
        {
            if (_netReport.Feed(line) is NetReport report) LogNetReport(report);
            if (Cs2Console.IsNoSteamLogon(line)) { OnNoSteamLogon(line); continue; }
            var valveBefore = _kicks.OnValve;
            _kicks.Feed(line, DateTime.Now);
            if (Cs2Console.IsConnectLine(line))
            {
                _consoleServer = _kicks.Server;
                if (_consoleServer != null) RejoinBack(_consoleServer);
                // You went to another server yourself: the retry on the full one stops, or it would pull you out of this one
                if (FullRetryStops(_fullServer, _consoleServer)) _fullServer = null;
                OfferAddJoined(_consoleServer);
                if (_kicks.OnValve && !valveBefore) Log.Info("CS2 joined a Valve server: kicks and bans there are left alone.");
                continue;
            }
            if (!_kicks.OnValve && Cs2Console.Classify(line) == KickKind.None && Cs2Console.IsDisconnect(line) && _disconnectSamples++ < 40)
                Log.Info("CS2 console: " + Cut(line));   // samples to learn the real format
        }
        if (_kicks.Take(DateTime.Now) is Disconnect d) OnKicked(d);
    }

    readonly DisconnectTracker _kicks = new();
    readonly HashSet<string> _addOffered = new();

    /// <summary>
    /// beta.11 (simulated users: "I only know my server by its name"): CS2 joined a community server that isn't in your list, so the
    /// app offers to add it, once per server while the app is open. It never says the join was protected: a server outside the list
    /// goes through the home connection, or the phone only in Whole PC.
    /// </summary>
    public static bool OffersAdd(string server, IEnumerable<string> listed, ICollection<string> offered) =>
        server != null && !listed.Contains(server) && !offered.Contains(server);

    public static string AddJoinedText(string server) => $"You joined {server}, which isn't in your servers. Add it, to play it through the phone next time?";

    void OfferAddJoined(string server)
    {
        if (!OffersAdd(server, _servers.Select(x => x.Key), _addOffered)) return;
        _addOffered.Add(server);
        Toast(AddJoinedText(server), Tone.Info, "Add", () => AddFromText(server));
    }

    readonly NetReportReader _netReport = new();
    bool _lagTipShown;

    /// <summary>
    /// CS2's network summary after each server, as one Log line with the IP the match went out on, so matches can be compared
    /// by the phone's place, its IP and the time.
    /// </summary>
    void LogNetReport(NetReport r)
    {
        // The home connection on purpose (No phone, or protection off with the option): no phone IP and no phone tips
        var home = OnHome;
        var via = home ? "home connection" : "phone IP " + (_egressPublic ?? "not measured");
        var line = r.Line(via, _consoleServer);
        if (r.Laggy && !home) Log.Warn(line + " To lag less: " + NetReport.Tips(_last?.Egress?.IsWifi == true));
        else if (r.Laggy) Log.Warn(line); else Log.Info(line);
        // Once per start: after the match, not in it (the line above is in the Log every time).
        if (r.Laggy && !home && !_lagTipShown)
        {
            _lagTipShown = true;
            Toast("That match lagged on the phone's connection. " + NetReport.Tips(_last?.Egress?.IsWifi == true), Tone.Info);
        }
        // The stats: Valve or community and the numbers in bands (never the server). A local map or the menu isn't a match.
        if (_kicks.OnValve || _consoleServer != null) Telemetry.Match(r, _kicks.OnValve);
        Diag.Write("cs2-network", new { r.Seconds, r.PingP50, r.PingP95, r.JitterUp, r.JitterDown, r.UpLate, r.UpMissedLate, r.DownMissedLate, r.Dropped });
    }

    DateTime _noSteamLogonAt = DateTime.MinValue;

    /// <summary>The kick the app is there to prevent: said out loud once, never a ban mark or a new-IP offer.</summary>
    void OnNoSteamLogon(string line)
    {
        if ((DateTime.Now - _noSteamLogonAt).TotalSeconds < 20) return;
        _noSteamLogonAt = DateTime.Now;
        System.Media.SystemSounds.Hand.Play();
        Log.Warn("Kicked with \"No Steam logon\": Steam lost its session with Valve (it reconnected on another network, or after a crash). A new IP won't help. Console: " + Cut(line));
        Diag.Write("kick", new { kind = "no-steam-logon", line, server = _consoleServer });
        Toast("Kicked with \"No Steam logon\": Steam lost its session. Wait for Steam to reconnect through the phone (Checks), then join again.", Tone.Error);
    }

    static string Cut(string line) => line.Length > 220 ? line[..220] + "…" : line;

    /// <summary>CS2 open for a while and console.log not written: it was started without -condebug.</summary>
    void CheckCondebug(string path)
    {
        if (_condebugHinted || _cs2LogOk || _cs2StartedAt == null) return;
        DateTime? written = null;
        long len = -1;
        try { var fi = new FileInfo(path); if (fi.Exists) { written = fi.LastWriteTime; len = fi.Length; } } catch { }
        // The write time of a file kept open can lag, so a size change while watching also counts.
        if (_cs2LogSeenAt == null) { _cs2LogSeenAt = DateTime.Now; _cs2LogLen = len; return; }
        if (len != _cs2LogLen) { _cs2LogOk = true; return; }
        if (written >= _cs2StartedAt) return;
        if (DateTime.Now - _cs2StartedAt < TimeSpan.FromSeconds(90) || DateTime.Now - _cs2LogSeenAt < TimeSpan.FromSeconds(60)) return;
        _condebugHinted = true;
        Log.Warn("CS2 is not writing console.log, so kicks and bans can't be detected. " + (_s.Cs2LaunchOptions
            ? "The app adds -condebug -conclearlog to CS2's launch options the next time Steam is closed (Settings > Steam has a button that does it now)."
            : "Settings > Steam > See kicks and bans when Steam opens CS2 adds -condebug -conclearlog to CS2's launch options."));
        Toast(_s.Cs2LaunchOptions ? "CS2 was opened without its console log, so kicks and bans aren't seen. Settings > Steam adds it in one click (Steam restarts)."
            : "To see kicks and bans, turn on Settings > Steam > See kicks and bans when Steam opens CS2.", false);
    }

    /// <summary>The demo's kicks (it has no console): the same path as a real one.</summary>
    void OnKicked(KickKind kind, string line) => OnKicked(new Disconnect(kind, line, null, BanCode: kind == KickKind.Ban, Why: kind == KickKind.Ban ? KickReason.Banned : KickReason.Other));

    /// <summary>
    /// A disconnect from a community server (Valve's never get here). A ban or a VPN block offers New IP + next account; a kick
    /// only a rejoin; a timeout says it was the connection; a full server just says so. A map change or you leaving: nothing.
    /// </summary>
    void OnKicked(Disconnect d)
    {
        if (!Cs2Console.Acts(d.Kind)) return;
        var kind = d.Kind;
        var line = d.Line;
        var what = kind switch { KickKind.Ban => "banned", KickKind.Vpn => "blocked", KickKind.Full => "turned away", _ => "kicked" };
        var why = d.ReasonInBrackets;
        // The server CS2 was really on (also when joined from the console or the game's browser). The last server
        // joined through the app only stands in if that happened in this CS2 session, never one from days ago.
        var server = d.Server ?? _consoleServer
            ?? (_s.LastServerAt != null && _cs2StartedAt != null && _s.LastServerAt >= _cs2StartedAt ? _s.LastServer : null);
        var inList = _servers.FirstOrDefault(x => x.Key == server);
        if (inList != null && server != _s.LastServer)
        {
            _s.LastServer = server;
            _s.LastServerAt = DateTime.Now;
            SaveSettings();
        }
        // One disconnect can come over two reads: within 20 s only something stronger still counts (a ban after a kick or a timeout).
        var recent = (DateTime.Now - _lastKickAt).TotalSeconds < 20;
        if (recent && Cs2Console.Stronger(_lastKickKind, kind) == _lastKickKind) { Log.Info($"CS2 console ({what}): " + Cut(line)); return; }
        // The app's own IP change or the phone dropping also time the game out: that's not the server, and it doesn't count.
        if (kind == KickKind.NoAnswer && (_changingIp || _rejoining || (_last?.Egress == null && _last?.Unprotected != true))) { Log.Info("CS2 lost the server while the connection was changing. Console: " + Cut(line)); return; }
        _lastKickAt = DateTime.Now;
        _lastKickKind = kind;
        if (kind != KickKind.Full) System.Media.SystemSounds.Hand.Play();
        var acc = ActiveAccount();
        if (acc == null && !_demo && Steam.InUseSteamId() != null) { LoadAccounts(); acc = ActiveAccount(); }   // an account Steam logged in to a moment ago
        var on = acc != null ? $" on {acc.Name}" : "";
        if (kind == KickKind.NoAnswer)
        {
            // The connection, not the admin. A server that never answers may also be down or blocking the IP: said, not assumed.
            var never = d.Why == KickReason.NeverAnswered;
            Log.Warn($"{server ?? "The server"} {(never ? "never answered" : "stopped answering")}: that's the connection, not a kick. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "no-answer", line, server });
            Toast(never ? "The server never answered. If other servers work, it may be offline or blocking this IP."
                : "Lost the connection to the server (timeout). That was the connection, not a kick: check the phone's signal and data.", false);
            UpdateRejoinBtn();
            return;
        }
        if (kind == KickKind.Steam)
        {
            // VAC or Steam refused the account: no rejoin offer and no server ban mark, a new IP changes nothing.
            Log.Warn($"Steam or VAC refused the connection to {server ?? "the server"}{on}. A new IP won't help. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "steam", line, server });
            Toast("Steam or VAC refused the connection. A new IP won't help: check the account.", Tone.Error);
            return;
        }
        if (kind == KickKind.Full)
        {
            Log.Info($"{server ?? "The server"}: {d.Reason}. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "full", line, server });
            // Turned away right after a Join or Rejoin from the app: the list's count can be old or count spectators (3 nov.: kicked
            // as a spectator for AFK, two left, Rejoin said full). So the app tries again every 15 s, 8 times (2 minutes).
            if (inList != null && server == _s.LastServer && _s.LastServerAt is DateTime at && DateTime.Now - at < TimeSpan.FromMinutes(3)
                && d.Why != KickReason.ReservedSlot)
            {
                if (_fullServer != server || DateTime.Now - _fullSince > TimeSpan.FromMinutes(5)) { _fullServer = server; _fullSince = DateTime.Now; _fullTries = 0; }
                if (_fullTries < FullTries)
                {
                    _fullTries++;
                    if (_fullTries == 1)
                    {
                        Telemetry.Rejoin("wait");
                        Toast("The server said it's full. The app tries again every 15 s for 2 minutes.", Tone.Info);
                    }
                    Log.Info($"Trying {server} again in 15 s ({_fullTries}/{FullTries}).");
                    _ = RetryFull(inList);
                    return;
                }
                Toast("The server stayed full for 2 minutes. Try another server, or Wait for a slot on the Servers page.", Tone.Info);
                return;
            }
            Toast(d.Why == KickReason.ReservedSlot ?"The server kicked you to make room for a reserved slot. Not a ban: join again when there's space."
                : "The server is full. Not a ban: try again in a moment or pick another server.", Tone.Info);
            return;
        }
        Telemetry.Count(kind switch { KickKind.Ban => "ban_seen", KickKind.Vpn => "vpn_seen", _ => "kick_seen" });
        Log.Warn($"You were {what} from {server ?? "the server"}{on}{(kind == KickKind.Ban ? d.For : "")}{why}. Console: " + Cut(line));
        // A ban is on the account there (also a VPN block made with a ban): marked, so New IP + next account skips it
        if ((kind == KickKind.Ban || (kind == KickKind.Vpn && d.BanCode)) && server != null)
        {
            if (acc == null) Log.Warn($"The ban on {server} wasn't marked: the account Steam is logged in to isn't in Accounts. Log in once with \"Remember me\" so it shows up.");
            else if (inList != null) MarkServerBan(acc, inList, d.Minutes);
            else
            {
                // Not in your list: marked by address, so it counts as soon as you add the server.
                MarkServerBan(acc, server, null, null, d.Minutes);
                Log.Info($"{server} isn't in your server list. Add it on Servers to rejoin it with the app and see the ban mark there.");
            }
        }
        Diag.Write("kick", new { kind = what, line, server, reason = d.Reason, minutes = d.Minutes });
        // A ban or a VPN block is about the IP (and a ban about the account too): coming back needs both new, so the offer is
        // the switch. A kick is only a rejoin. The app only goes back to a server in your list: no promise for one that isn't.
        if (inList == null && server != null)
        {
            // beta.11: the button adds it here, instead of sending you to the Servers page
            _addOffered.Add(server);
            Toast($"You were {what} from the server{why}. It isn't in your list, so the app can't take you back. Add it for next time?", Tone.Warn, "Add", () => AddFromText(server));
        }
        else Toast(inList == null ? $"You were {what} from the server{why}."
            : kind == KickKind.Vpn ? "The server blocked the phone's IP as a VPN or proxy. New IP + next account gets you back (if every IP is blocked, it blocks your carrier)."
            : kind == KickKind.Ban ? BanToast(d.For, _s.HotkeyRejoin, NextQuick(inList)?.Name)
            : $"You were kicked from the server{why}. Not a ban: you can rejoin.", false);
        UpdateRejoinBtn();
        if (inList != null && Dialog.Visibility != Visibility.Visible && !_changingIp && !_rejoining) _ = OfferRejoin(d);
    }

    /// <summary>
    /// The ban toast (beta.11): says which account comes next before you press anything (Hugo: "F9 or F10, and as who?").
    /// With no account left it says so, instead of a key that can't help.
    /// </summary>
    public static string BanToast(string forText, string hotkey, string next) => next != null
        ? $"Banned{forText}. Press {hotkey} to rejoin with a new IP as {next}."
        : $"Banned{forText}. No other account to rejoin with: the rest are out of rotation, on cooldown or banned there.";

    async Task OfferRejoin(Disconnect d)
    {
        var why = d.ReasonInBrackets;
        if (!d.WantsSwitch)
        {
            // A kick is not about the IP or the account: the same account joins again, no new IP
            if (await Ask("You were kicked", $"CS2 says you were kicked from {_s.LastServer}{why}. Rejoin?", "Rejoin", "Not now"))
                await RunCommand("Rejoin", "starting…", () => RunSwitch(new SwitchSteps(false, false, true, true), "After the kick"));
            return;
        }
        var next = NextQuick(_servers.FirstOrDefault(x => x.Key == _s.LastServer));
        var title = d.Kind == KickKind.Vpn ? "VPN or proxy blocked" : "You were banned";
        if (next == null)
        {
            Toast($"{(d.Kind == KickKind.Vpn ? "Blocked on" : "Banned from")} {_s.LastServer}. No other account to rejoin with: the rest are out of rotation, on cooldown or banned there.", false);
            return;
        }
        var text = d.Kind == KickKind.Vpn
            ? $"{_s.LastServer} took the phone's IP for a VPN or proxy{(d.BanCode ? " and banned the account" : "")}.\n\nGet a new IP, log in to {next.Name} and rejoin?"
            : $"CS2 says you were banned from {_s.LastServer}{d.For}. The ban is on your IP and on the account, so a new IP alone is not enough.\n\nGet a new IP, log in to {next.Name} and rejoin?";
        if (await Ask(title, text, "Switch and rejoin", "Not now"))
            await RejoinAfterBan(d.Kind == KickKind.Vpn ? "After the VPN block" : "After the ban");
    }

    /// <summary>New IP, the next account the server hasn't banned (nor its community), back to the last server.</summary>
    Task RejoinAfterBan(string from) => RunCommand("Switch and rejoin", "starting…", () => RunSwitch(new SwitchSteps(true, true, true, true, Ban: true), from));

    /// <summary>The account in use has a ban mark on the last server (or its community).</summary>
    bool BannedOnLast() =>
        ActiveAccount() is AccountItem a && _servers.FirstOrDefault(x => x.Key == _s.LastServer) is ServerItem s && a.BanFor(s.Key, s.ServerName, s.Network) != null;

    // ------------------------------------------------------------ Bans by a server (on the account, not Steam)

    AccountItem ActiveAccount()
    {
        var id = _demo ? null : Steam.InUseSteamId();
        // Steam logged in to an account the list doesn't have yet: not the old one (the next tick reloads the list).
        if (id != null) return _accounts.FirstOrDefault(a => a.SteamId == id);
        return _accounts.FirstOrDefault(a => a.Active);
    }

    void MarkServerBan(AccountItem a, ServerItem s, int? minutes = null) => MarkServerBan(a, s.Key, s.ServerName, s.Network, minutes);

    /// <summary><paramref name="minutes"/>: a timed ban the server said (the mark ends with it); null or 0 stays until cleared.</summary>
    void MarkServerBan(AccountItem a, string key, string name, string network, int? minutes = null)
    {
        a.Meta.ServerBans ??= new();
        a.Meta.ServerBans[key] = new ServerBan { At = DateTime.Now, Name = name, Network = ServerNetwork.Clean(network), Until = minutes > 0 ? DateTime.Now.AddMinutes(minutes.Value) : null };
        a.RefreshAll();
        SaveAccounts();
        ApplyServerBans();
        UpdateQuickInfo();
        var same = _servers.Count(x => x.Key != key && ServerNetwork.Same(key, name, network, x.Key, x.ServerName, x.Network));
        Log.Warn($"{a.Name} marked as banned on {key}" + (same > 0 ? $" and {Fmt.Count(same, "other server")} of the same network" : "")
            + (minutes > 0 ? $" until {DateTime.Now.AddMinutes(minutes.Value):g}" : "") + ". New IP + next account skips it there.");
        Diag.Write("server-ban", new { steamId = a.SteamId, server = key, name, minutes });
    }

    /// <summary>On each server row: whether the account you're logged in with is banned there (or on that server's network). Ended timed bans go.</summary>
    void ApplyServerBans()
    {
        var dropped = false;
        foreach (var a in _accounts) dropped |= a.DropEndedBans();
        if (dropped) SaveAccounts();
        var acc = ActiveAccount();
        foreach (var s in _servers)
        {
            var b = acc?.BanFor(s.Key, s.ServerName, s.Network);
            s.BannedText = b == null ? null : b.Value.Key == s.Key ? $"Banned here ({acc.Name})" : $"Banned on this network ({acc.Name})";
        }
    }

    void SrvMarkBan_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServerItem s) return;
        if (ActiveAccount() is not AccountItem a) { Toast("No Steam account in use right now.", false); return; }
        MarkServerBan(a, s);
        Toast($"{a.Name} marked as banned on {s.Title}.", true);
    }

    void SrvClearBans_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServerItem s) return;
        foreach (var a in _accounts)
        {
            if (a.BanFor(s.Key, s.ServerName, s.Network) is not { } b) continue;
            a.Meta.ServerBans.Remove(b.Key);
            if (a.Meta.ServerBans.Count == 0) a.Meta.ServerBans = null;
            a.RefreshAll();
        }
        SaveAccounts();
        ApplyServerBans();
        UpdateQuickInfo();
        Toast($"Ban marks cleared on {s.Title}.", true);
    }

    /// <summary>Puts a server in a community by hand: servers with the same name share ban marks. Empty goes back to the name tag.</summary>
    async void SrvNetwork_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServerItem s) return;
        var known = _servers.Select(x => x.Network).Where(n => n != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        DialogInput.Text = s.Network ?? "";
        DialogInput.Tag = known.Count > 0 ? "E.g. " + string.Join(", ", known.Take(3)) : "E.g. EXAMPLE.GG, MY COMMUNITY";
        var guess = ServerNetwork.Tag(s.ServerName);
        if (!await Ask($"Community of {s.Title}", "Servers with the same community name share ban marks: a ban on one counts on all of them, and New IP + next account skips that account there.\n\n"
                + (guess != null ? $"Empty: the app uses the tag from the server name ({guess})." : "Empty: the app guesses from the server name (none found for this one)."),
                "Save", "Cancel", inputs: 1, label1: "Community name")) return;
        s.Network = ServerNetwork.Clean(DialogInput.Text);
        // Ban marks already on this server take the new name too.
        foreach (var a in _accounts)
            if (a.Meta.ServerBans?.TryGetValue(s.Key, out var b) == true && b != null) { b.Network = s.Network; a.RefreshAll(); }
        SaveServers();
        SaveAccounts();
        ApplyServerBans();
        UpdateQuickInfo();
        Log.Info(s.Network == null ? $"{s.Key}: community from the server name again." : $"{s.Key}: community set to {s.Network}.");
        Toast(s.Network == null ? $"{s.Title} uses the tag from its name again." : $"{s.Title} is now in the community {s.Network}.", true);
    }

    /// <summary>One primary in Status: Turn on protection while it is off, else Change IP and rejoin with a last server, else Change IP.</summary>
    /// <paramref name="fixFirst"/> (beta.11, simulated users: "three blue buttons"): something blocks the game (the phone's Wi-Fi is
    /// on, or the phone is gone), so its fix (Turn off phone Wi-Fi, Phone setup) is the one blue button, not Change IP.
    public static (string Rejoin, string ChangeIp) StatusPrimary(bool protOn, bool noPhone, bool lastServer, bool fixFirst = false) =>
        (!protOn && !noPhone) || (fixFirst && !noPhone) ? ("Btn", "Btn") : lastServer && !noPhone ? ("Primary", "Btn") : ("Btn", "Primary");

    void UpdateRejoinBtn()
    {
        RejoinBtn.IsEnabled = !_changingIp && !_rejoining && _s.LastServer != null && _phone?.Ready == true && !_s.NoPhone;
        RejoinBtn.ToolTip = _s.NoPhone ? "Off in the no phone mode: there is no IP to change." : (_s.LastServer == null ? "Join a server from Servers first. " : $"Changes the IP, then joins {_s.LastServer} again (with another account if this one is banned there). ")
            + (_protOn ? "Only joins with protection confirmed." : _s.UseWithProtectionOff ? "Protection is off: it joins on your home connection without changing the IP (after a ban it asks to turn protection on first)." : "Protection is off: it asks to turn it on first.")
            + $" In game: {_s.HotkeyRejoin}.";
        // After a kick the way back is the rejoin: with a last server it is the primary, and its hotkey shows under it. With protection
        // off, Turn on protection is the one primary (beta.10).
        var (rejoinStyle, changeStyle) = StatusPrimary(_protOn, _s.NoPhone, _s.LastServer != null, PhoneWifiOn || (_last != null && _last.Egress == null));
        var want = rejoinStyle + "|" + changeStyle;
        if (!Equals(RejoinBtn.Tag, want))
        {
            RejoinBtn.Tag = want;
            RejoinBtn.Style = (Style)FindResource(rejoinStyle);
            ChangeIpBtn.Style = (Style)FindResource(changeStyle);
        }
        RejoinHotkeyKeys.Text = _s.HotkeyRejoin;
        RejoinHotkeyHint.Visibility = _s.NoPhone ? Visibility.Collapsed : Visibility.Visible;
    }


    /// <summary>What a switch does. Switch (button and hotkey) takes the steps from Settings; the rejoin hotkey is new IP, same account, rejoin.</summary>
    /// <summary><paramref name="Ban"/>: the rejoin after a ban or a VPN block, where the home IP is the one the server refused.</summary>
    readonly record struct SwitchSteps(bool Ip, bool Account, bool Rejoin, bool OpenWith, bool Ban = false);

    SwitchSteps SwitchChoice => new(_s.SwitchIp, _s.SwitchAccount, _s.SwitchRejoin, _s.SwitchOpenWith);

    /// <summary>The normal Change IP, the next account by the quick switch rules, then the normal join to the last server: each step only if asked.</summary>
    async Task<bool> RunSwitch(SwitchSteps steps, string from)
    {
        ServerItem s = null;
        if (steps.Rejoin && (s = _servers.FirstOrDefault(x => x.Key == _s.LastServer)) == null)
        {
            var why = _s.LastServer == null ? "No last server yet. Join one from Servers first." : $"The last server ({_s.LastServer}) is no longer in your list.";
            if (!steps.Ip && !steps.Account) { Toast(why, false); return false; }
            Log.Info($"{from}: not rejoining. {why}");
        }
        var ip = steps.Ip;
        if (ip && steps.Account && !_demo && (_adb == null || _phone?.Ready != true || _s.NoPhone))
        {
            ip = false;
            Log.Info(_s.NoPhone ? $"{from}: no phone mode, so only the account changes." : $"{from}: the phone isn't ready to change the IP, so only the account changes.");
            Toast(_s.NoPhone ? "Switching the account only: no phone mode has no IP to change." : "Switching the account only: the phone isn't ready to change the IP.", false);
        }
        // The account first: protection isn't turned on for a switch that then has no account to go to
        AccountItem next = null;
        if (steps.Account && (next = PickQuick(s)) == null) return false;
        // Protection off: a new phone IP isn't what servers see (they see your home IP). With "Use the app with protection off" the IP
        // step is left out and said so; without it, or after a ban (the home IP is the banned one), one question turns protection on
        // first, as Join does, or nothing starts.
        if (ip && !_demo && !_s.NoPhone && IpStep(_protOn, _s.UseWithProtectionOff, steps.Ban) is var step && step != IpWhenOff.Change)
        {
            if (step == IpWhenOff.Skip)
            {
                ip = false;
                Log.Info($"{from}: protection is off, so servers see your home IP and only the " + (steps.Account ? "account changes." : "rejoin runs."));
                Toast("Protection is off: servers see your home IP, so the IP isn't changed" + (steps.Account ? ", only the account." : "."), false);
            }
            else
            {
                var why = steps.Ban ? "After a ban the server knows your home IP, so going back from it bans the next account too. " : "";
                if (!await Ask("Protection is off", why + "A new IP only counts with protection on: with it off, servers see your home IP. Turn protection on first, then go on?", "Turn on and go on")) return false;
                if (!await TurnProtectionOn(ask: false) || CommandCancelled) return false;
            }
        }
        if (!ip && next == null && s == null) { Toast("New IP + next account has nothing to do: turn on at least one step in Settings > New IP + next account.", false); return false; }
        Log.Info($"{from}: " + string.Join(", ", new[] { ip ? "new IP" : null, next != null ? $"log in to {next.Name}" : null, s != null ? $"rejoin {s.Key}" : null }.Where(x => x != null)) + ".");
        _rejoining = true;
        UpdateRejoinBtn();
        ShowCancel();
        try
        {
            if (ip && _demo) { ShowProgress($"Attempt 1/{_s.MaxAttempts}: phone data off for {_s.OffSeconds} s…"); await Task.Delay(1500); HideProgress(); }
            else if (ip && !await ChangeIp(confirm: false)) return false;
            // Cancel during a step stops here, after it: an IP change or a login is never left half done.
            if (StopAfterStep(ip ? "the new IP" : null)) return false;
            // A new account restarts Steam through the new IP; Connect waits for its session before joining.
            if (next != null && !await SwitchTo(next, cs2: s == null, ask: false, openWith: steps.OpenWith)) return false;
            if (StopAfterStep(next != null ? $"the login to {next.Name}" : null)) return false;
            if (s == null) return true;
            // Same account on a new IP: Steam has to log in again. If it hasn't within 20 s, restart it (a dialog can't be seen in game).
            if (ip && next == null && !_demo && Steam.IsRunning && !await WaitSteamReady(20))
                return !CommandCancelled && await RestartSteamAndConnect(s, steps.OpenWith);
            return await Connect(s, openWith: steps.OpenWith);
        }
        finally { _rejoining = false; UpdateRejoinBtn(); ShowCancel(); }
    }

    public enum IpWhenOff { Change, Skip, AskTurnOn }

    /// <summary>
    /// The IP step of New IP + next account (and the rejoins) by protection: on, the IP changes; off, servers see the home IP, so it is
    /// left out with "Use the app with protection off", else protection is turned on first (one question). After a ban the home IP is
    /// the refused one, so the option never skips the new IP there. No phone mode is handled before.
    /// </summary>
    public static IpWhenOff IpStep(bool protOn, bool useWithOff, bool afterBan = false) =>
        protOn ? IpWhenOff.Change : useWithOff && !afterBan ? IpWhenOff.Skip : IpWhenOff.AskTurnOn;

    /// <summary>True when Cancel was pressed during a step of a switch: says what was done and that nothing else starts.</summary>
    bool StopAfterStep(string done)
    {
        if (!_stopAfterStep && !CommandCancelled) return false;
        if (_stopAfterStep)
        {
            Log.Info($"{_command} stopped after " + (done ?? "the last step") + ", as asked.");
            Toast("Stopped" + (done == null ? "." : $" after {done}.") + " Nothing else was started.", Tone.Info);
        }
        return true;
    }

    const int FullTries = 8;
    string _fullServer, _rejoinTarget;
    DateTime _fullSince, _rejoinAt;
    int _fullTries;

    /// <summary>15 s after the server said full: joins it again, unless CS2 closed or another server came in between.</summary>
    async Task RetryFull(ServerItem s)
    {
        await Task.Delay(15000);
        if (_fullServer == s.Key && Steam.Cs2Running && _servers.Contains(s)) await Join(s, fromSlotWait: true);
    }

    /// <summary>CS2 connected to a server: the retry on the full one goes on only if it is that same server (or CS2 says no server).</summary>
    public static bool FullRetryStops(string fullServer, string connectedTo) => fullServer != null && connectedTo != null && connectedTo != fullServer;

    /// <summary>For the stats: a Rejoin counts as back when CS2 connects to that server within 3 minutes.</summary>
    void RejoinBack(string server)
    {
        if (_rejoinTarget == null || server != _rejoinTarget || DateTime.Now - _rejoinAt > TimeSpan.FromMinutes(3)) return;
        _rejoinTarget = null;
        Telemetry.Rejoin("back");
    }

    async void Rejoin_Click(object sender, RoutedEventArgs e) => await ChangeIpAndRejoin("Button");

    /// <summary>
    /// The rejoin hotkey and the button: new IP, same account, back to the last server (with all the usual checks).
    /// If that account is banned there, the same account would be refused again, so it also switches account.
    /// </summary>
    Task ChangeIpAndRejoin(string from)
    {
        Telemetry.Rejoin("try");
        (_rejoinTarget, _rejoinAt) = (_s.LastServer, DateTime.Now);
        if (!BannedOnLast()) return RunCommand("Change IP and rejoin", "starting…", () => RunSwitch(new SwitchSteps(true, false, true, true), from));
        Log.Info($"{from}: {ActiveAccount()?.Name} is banned on {_s.LastServer}, so the rejoin also switches account.");
        return RejoinAfterBan(from);
    }
}
