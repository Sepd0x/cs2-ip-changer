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
            if (_cs2StartedAt != null && Cs2ConsoleLog() is string last)
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
        var path = Cs2ConsoleLog();
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
                Telemetry.Count("join");   // beta.13: CS2 joined a server (only how many times, never which one)
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

    /// <summary><paramref name="home"/>: you play on the home connection on purpose (No phone, or protection off with the option), so no phone promise.</summary>
    public static string AddJoinedText(string server, bool home = false) => home ? $"You joined {server}, which isn't in your servers. Add it?"
        : $"You joined {server}, which isn't in your servers. Add it, to play it through the phone next time?";

    void OfferAddJoined(string server)
    {
        if (!OffersAdd(server, _servers.Select(x => x.Key), _addOffered)) return;
        _addOffered.Add(server);
        Toast(AddJoinedText(server, OnHome), Tone.Info, "Add", () => AddFromText(server));
    }

    readonly NetReportReader _netReport = new();
    bool _lagTipShown;

    /// <summary>
    /// CS2's network summary after each server, as one Log line with the IP the match went out on, so matches can be compared
    /// by the phone's place, its IP and the time.
    /// </summary>
    void LogNetReport(NetReport r)
    {
        // The home connection (No phone, or protection off, with or without the option): no phone IP and no phone tips (review C11)
        var home = PingOnHome(_s.NoPhone, _protOn);
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
        // Only the code that is "No Steam logon" is called that; Steam dropping or a VAC check that timed out is the same family (review F3)
        var said = Cs2Console.SaysNoSteamLogon(line);
        Log.Warn((said ? "Kicked with \"No Steam logon\": Steam" : "The server dropped you because Steam")
            + " lost its session with Valve (it reconnected on another network, or after a crash). A new IP won't help. Console: " + Cut(line));
        Diag.Write("kick", new { kind = "no-steam-logon", line, server = _consoleServer });
        Toast(NoSteamLogonToast(OnHome, said), Tone.Error);
    }

    /// <summary><paramref name="said"/> false: another code of the family (Steam dropped, the VAC check timed out), not "No Steam logon" itself.</summary>
    public static string NoSteamLogonToast(bool home, bool said = true) =>
        (said ? "Kicked with \"No Steam logon\": Steam lost its session." : "Steam lost its session, so the server dropped you.")
        + $" Wait for Steam to reconnect{(home ? "" : " through the phone")} (Checks), then join again.";

    /// <summary>Steam or VAC said no to the account; another PC logging in to it has its own words (review F3).</summary>
    public static string SteamRefusedToast(bool elsewhere) => elsewhere
        ? "This account was logged in somewhere else, so Steam ended its session here. Log out there, then join again."
        : "Steam or VAC refused the connection. A new IP won't help: check the account.";

    /// <summary>A join refused before getting in, with no reason: not marked as a ban (a whitelist or Prime only server does it too).</summary>
    public const string RefusedJoinToast = "The server refused the join without saying why. Not marked as a ban: it may let in only some players, or this account may be banned there.";

    /// <summary>
    /// One disconnect can come over two reads: within 20 s only something stronger still counts (a ban after a kick or a timeout).
    /// A full server after a new Join (<paramref name="joinedAt"/>) is a new refusal, so the wait for a slot goes on (review F6).
    /// </summary>
    public static bool SameDisconnect(KickKind lastKind, DateTime lastAt, KickKind kind, DateTime now, DateTime? joinedAt) =>
        (now - lastAt).TotalSeconds < 20 && Cs2Console.Stronger(lastKind, kind) == lastKind && !(kind == KickKind.Full && joinedAt > lastAt);

    /// <summary>A timeout: the connection, not the admin. On the home connection there is no phone to check.</summary>
    public static string TimeoutToast(bool never, bool home) => never ? "The server never answered. If other servers work, it may be offline or blocking this IP."
        : "Lost the connection to the server (timeout). That was the connection, not a kick: " + (home ? "check your internet." : "check the phone's signal and data.");

    /// <summary>A VPN or proxy block. On the home connection it was the home IP, and a new phone IP only counts with the phone.</summary>
    public static string VpnToast(bool home, bool noPhone) => !home
        ? "The server blocked the phone's IP as a VPN or proxy. New IP + next account gets you back (if every IP is blocked, it blocks your carrier)."
        : "The server blocked your home IP as a VPN or proxy. " + (noPhone ? "Pick Game only or Whole PC on Connection to play through the phone." : "Turn on protection to play through the phone.");

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
        // A Join or Rejoin from the app a moment ago, read before a join from CS2's own browser becomes the last server below
        var byApp = JoinedByApp(server, _s.LastServer, _s.LastServerAt, DateTime.Now);
        if (inList != null && server != _s.LastServer)
        {
            _s.LastServer = server;
            _s.LastServerAt = DateTime.Now;
            SaveSettings();
            ShowLastServer();   // the "last" mark on Servers and the names that use the last server
        }
        // One disconnect can come over two reads: within 20 s only something stronger still counts (a ban after a kick or a timeout).
        if (SameDisconnect(_lastKickKind, _lastKickAt, kind, DateTime.Now, _s.LastServerAt)) { Log.Info($"CS2 console ({what}): " + Cut(line)); return; }
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
            Toast(TimeoutToast(never, OnHome), false);
            UpdateRejoinBtn();
            return;
        }
        if (kind == KickKind.Steam)
        {
            // VAC or Steam refused the account: no rejoin offer and no server ban mark, a new IP changes nothing.
            var elsewhere = Cs2Console.LoggedInElsewhere(line);
            Log.Warn((elsewhere ? $"The account{on} was logged in somewhere else, so Steam ended its session on {server ?? "the server"}."
                : $"Steam or VAC refused the connection to {server ?? "the server"}{on}.") + " A new IP won't help. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "steam", line, server });
            Toast(SteamRefusedToast(elsewhere), Tone.Error);
            return;
        }
        if (kind == KickKind.Kick && d.Why == KickReason.RefusedJoin)
        {
            // Refused before getting in, with no reason: no ban mark and no rejoin question (the same join would be refused again)
            Telemetry.Count("kick_seen");
            Log.Warn($"{server ?? "The server"} refused the join{on} without saying why (sign-on state {d.SignOn}). Not marked as a ban: it may let in only some players"
                + " (a whitelist, Prime only, a Steam level), or this account may be banned there. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "refused", line, server });
            Toast(RefusedJoinToast, Tone.Warn);
            UpdateRejoinBtn();
            return;
        }
        if (kind == KickKind.Full)
        {
            Log.Info($"{server ?? "The server"}: {d.Reason}. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "full", line, server });
            // Turned away right after a Join or Rejoin from the app: the list's count can be old or count spectators (3 nov.: kicked
            // as a spectator for AFK, two left, Rejoin said full). So the app tries again every 15 s, 8 times (2 minutes).
            if (inList != null && byApp && d.Why != KickReason.ReservedSlot)
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
                Toast("The server stayed full for 2 minutes. Try another server, or press Join on Servers: when it's full, Wait for a slot joins you as soon as one opens.", Tone.Info);
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
            : kind == KickKind.Vpn ? VpnToast(OnHome, _s.NoPhone)
            : kind == KickKind.Ban ? BanToast(d.For, _s.HotkeyRejoin, NextQuick(inList)?.Name, marked: acc != null, ipChanges: SwitchChangesIp,
                asksProtection: SwitchChangesIp && IpStep(_protOn, _s.UseWithProtectionOff, afterBan: true) != IpWhenOff.Change)
            : $"You were kicked from the server{why}. Not a ban: you can rejoin.", false);
        UpdateRejoinBtn();
        if (inList != null && Dialog.Visibility != Visibility.Visible && !_changingIp && !_rejoining) _ = OfferRejoin(d);
    }

    /// <summary>
    /// The ban toast (beta.11): says which account comes next before you press anything (Hugo: "F9 or F10, and as who?").
    /// With no account left it says so, instead of a key that can't help. It promises only what the key does now: <paramref name="marked"/>
    /// false (the account isn't in Accounts) rejoins as the same account; <paramref name="ipChanges"/> false (no phone mode, a phone not
    /// ready) keeps the IP; <paramref name="asksProtection"/> (protection off) asks in the app first, which can't be seen in game.
    /// </summary>
    public static string BanToast(string forText, string hotkey, string next, bool marked = true, bool ipChanges = true, bool asksProtection = false) =>
        next == null ? $"Banned{forText}. No other account to rejoin with: the rest are out of rotation, on cooldown or banned there."
        : !marked ? $"Banned{forText}. This account isn't in Accounts, so {hotkey} would rejoin with it: log in to another account first."
        : !ipChanges ? $"Banned{forText}. Press {hotkey} to rejoin as {next} (the IP stays the same)."
        : asksProtection ? $"Banned{forText}. Turn on protection in the app, then press {hotkey} to rejoin with a new IP as {next}."
        : $"Banned{forText}. Press {hotkey} to rejoin with a new IP as {next}.";

    /// <summary>
    /// Whether the IP changes in a switch that also logs in to the next account: only with the phone ready on ADB (an iPhone changes its IP
    /// by hand, and no phone mode has none). Without it the switch keeps the IP and the other steps run.
    /// </summary>
    bool SwitchChangesIp => _demo || !_s.NoPhone && _adb != null && _phone?.Ready == true;

    /// <summary>An iPhone: no ADB, so its IP changes by hand (Change IP tells you when to toggle Airplane Mode).</summary>
    bool IphoneByHand => _iphone && _phone?.Ready != true;

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
        var text = BanQuestion(d.Kind == KickKind.Vpn, _s.LastServer, d.BanCode, d.For, next.Name, OnHome, SwitchChangesIp);
        if (await Ask(title, text, "Switch and rejoin", "Not now"))
            await RejoinAfterBan(d.Kind == KickKind.Vpn ? "After the VPN block" : "After the ban");
    }

    /// <summary>The last line of Join's "banned here" question: on the home connection the switch can't change the IP.</summary>
    public static string BanJoinTip(string hotkey, bool home) => home
        ? $"On your home connection the IP stays: New IP + next account ({hotkey}) only logs in to another account."
        : $"New IP + next account ({hotkey}) changes the IP and the account together.";

    /// <summary>The question after a ban or a VPN block: the IP it names is the one the server saw, and "a new IP" only when the switch changes it.</summary>
    public static string BanQuestion(bool vpn, string server, bool banCode, string forText, string next, bool home, bool ipChanges)
    {
        var go = ipChanges ? $"Get a new IP, log in to {next} and rejoin?" : $"Log in to {next} and rejoin?";
        return vpn
            ? $"{server} took {(home ? "your home IP" : "the phone's IP")} for a VPN or proxy{(banCode ? " and banned the account" : "")}.\n\n{go}"
            : $"CS2 says you were banned from {server}{forText}. The ban is on your IP and on the account, so a new IP alone is not enough.\n\n{go}";
    }

    /// <summary>New IP, the next account the server hasn't banned (nor its community), back to the last server.</summary>
    Task RejoinAfterBan(string from) => RunCommand("Switch and rejoin", "starting…", () => RunSwitch(new SwitchSteps(true, true, true, true, Ban: true), from));

    /// <summary>The account in use has a ban mark on the last server (or its community).</summary>
    bool BannedOnLast() =>
        ActiveAccount() is AccountItem a && _servers.FirstOrDefault(x => x.Key == _s.LastServer) is ServerItem s && a.BanFor(s.Key, s.ServerName, s.Network) != null;

    // ------------------------------------------------------------ Bans by a server (on the account, not Steam)

    /// <summary>The account in use, hidden ones too: a ban on a hidden account still counts (it stays out of the rotation all the same).</summary>
    AccountItem ActiveAccount() => ActiveIn(_demo ? null : Steam.InUseSteamId(), _accounts, _accHiddenItems);

    /// <summary>
    /// The account Steam is on (<paramref name="id"/>) in the list or among the hidden ones. Steam on an account neither has yet: none,
    /// not the old one (the next tick reloads the list). No id (Steam closed): the one marked in use.
    /// </summary>
    public static AccountItem ActiveIn(string id, IEnumerable<AccountItem> shown, IEnumerable<AccountItem> hidden)
    {
        var all = shown.Concat(hidden);
        return id != null ? all.FirstOrDefault(a => a.SteamId == id) : all.FirstOrDefault(a => a.Active);
    }

    /// <summary>
    /// CS2 was turned away from <paramref name="server"/> right after a Join or Rejoin from the app: it is the last server the app
    /// joined, within 3 minutes. Read before a join from CS2's own browser makes that server the last one.
    /// </summary>
    public static bool JoinedByApp(string server, string lastServer, DateTime? lastAt, DateTime now) =>
        server != null && server == lastServer && lastAt is DateTime at && now - at < TimeSpan.FromMinutes(3);

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
        Log.Warn($"{a.Name} marked as banned on {key}" + (same > 0 ? $" and {Fmt.Count(same, "other server")} of the same community" : "")
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
        var lastInList = _s.LastServer != null && _servers.Any(x => x.Key == _s.LastServer);
        // The click works with the phone ready on ADB, an iPhone connected (guided in the app; ChangeIpReady, the same rule as the
        // Change IP button, also with protection off), or on the home connection with no ban there
        RejoinBtn.IsEnabled = RejoinEnabled(_changingIp || _rejoining, lastInList, _s.NoPhone, ChangeIpReady, OnHome && !BannedOnLast());
        RejoinBtn.ToolTip = RejoinTip(_s.NoPhone, _s.LastServer, lastInList, _protOn, _s.UseWithProtectionOff, _s.HotkeyRejoin);
        // Named for what a click does: just "Rejoin" on the home connection on purpose (after a ban the click switches and asks first)
        RejoinBtn.Content = RejoinBtnText(_s.NoPhone, _protOn, _s.UseWithProtectionOff, OnHome && BannedOnLast());
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

    /// <summary>
    /// Change IP and rejoin can run: a last server still in your list, and the IP can change (<paramref name="canChangeIp"/>) or it
    /// joins on the home connection (<paramref name="homeNoBan"/>: protection off with the option, and no ban there). Off in No phone mode.
    /// </summary>
    public static bool RejoinEnabled(bool busy, bool lastInList, bool noPhone, bool canChangeIp, bool homeNoBan) =>
        !busy && lastInList && !noPhone && (canChangeIp || homeNoBan);

    public static string RejoinTip(bool noPhone, string last, bool lastInList, bool protOn, bool useWithOff, string hotkey)
    {
        if (noPhone) return "Off in the no phone mode: there is no IP to change.";
        if (last != null && lastInList && IpStep(protOn, useWithOff) == IpWhenOff.Skip)
            return $"Joins {last} again on your home connection: protection is off, so the IP isn't changed. After a ban it asks to turn protection on first. In game: {hotkey}.";
        return (last == null ? "Join a server from Servers first. " : !lastInList ? $"{last} is no longer in your list: join a server from Servers first. "
                : $"Changes the IP, then joins {last} again (with another account if this one is banned there). ")
            + (protOn ? "Only joins with protection confirmed." : useWithOff ? "Protection is off: it joins on your home connection without changing the IP (after a ban it asks to turn protection on first)." : "Protection is off: it asks to turn it on first.")
            + $" In game: {hotkey}.";
    }

    /// <summary>Why there is no rejoin: no last server yet, or it left your list.</summary>
    public static string NoRejoinWhy(string last) => last == null ? "No last server yet. Join one from Servers first." : $"The last server ({last}) is no longer in your list.";


    /// <summary>What a switch does. Switch (button and hotkey) takes the steps from Settings; the rejoin hotkey is new IP, same account, rejoin.</summary>
    /// <summary><paramref name="Ban"/>: the rejoin after a ban or a VPN block, where the home IP is the one the server refused.</summary>
    public readonly record struct SwitchSteps(bool Ip, bool Account, bool Rejoin, bool OpenWith, bool Ban = false);

    SwitchSteps SwitchChoice => new(_s.SwitchIp, _s.SwitchAccount, _s.SwitchRejoin, _s.SwitchOpenWith);

    /// <summary>
    /// The switch's steps from Settings as the button names them and a click runs them: no IP step in No phone mode (there is none to
    /// change, with or without the account step), nor on an iPhone with the account step (its IP changes by hand, with Change IP), nor
    /// with an Android phone that isn't ready on ADB (<paramref name="adbReady"/> false): the other steps still run, as Settings says.
    /// An iPhone without the account step keeps it: a click runs the guided Change IP in the app.
    /// </summary>
    public static SwitchSteps QuickSteps(SwitchSteps choice, bool noPhone, bool iphoneByHand, bool adbReady = true) =>
        choice with { Ip = choice.Ip && !noPhone && !(iphoneByHand && choice.Account) && (adbReady || iphoneByHand) };

    // An iPhone that isn't connected now can't run its guided change either (ChangeIpReady, the Change IP button's rule): no IP step then
    SwitchSteps QuickChoice => QuickSteps(SwitchChoice, _s.NoPhone, IphoneByHand && (_demo || ChangeIpReady), _demo || _adb != null && _phone?.Ready == true);

    /// <summary>Why <see cref="QuickSteps"/> left the IP step out now, for the Log and the "nothing to do" toast.</summary>
    string IpLeftOutWhy => _s.NoPhone ? "no phone mode" : IphoneByHand ? "the iPhone changes its IP by hand" : "the phone isn't ready on ADB";

    /// <summary>
    /// The normal Change IP, the next account by the quick switch rules, then the normal join to the last server: each step only if asked.
    /// <paramref name="ipLeftOut"/>: why Settings has the IP step but <see cref="QuickSteps"/> took it out (No phone mode, an iPhone, a
    /// phone not ready on ADB), or null.
    /// </summary>
    async Task<bool> RunSwitch(SwitchSteps steps, string from, string ipLeftOut = null)
    {
        if (ipLeftOut != null) Log.Info($"{from}: {ipLeftOut}, so the IP stays the same.");
        ServerItem s = null;
        if (steps.Rejoin && (s = _servers.FirstOrDefault(x => x.Key == _s.LastServer)) == null)
        {
            var why = NoRejoinWhy(_s.LastServer);
            if (!steps.Ip && !steps.Account) { Toast(why, false); return false; }
            Log.Info($"{from}: not rejoining. {why}");
        }
        var ip = steps.Ip;
        if (ip && steps.Account && !SwitchChangesIp)
        {
            ip = false;
            Log.Info(_s.NoPhone ? $"{from}: no phone mode, so the IP stays the same." : IphoneByHand ? $"{from}: the iPhone changes its IP by hand, so the IP stays the same."
                : $"{from}: the phone isn't ready to change the IP, so the IP stays the same.");
            Toast(_s.NoPhone ? "The IP stays the same: no phone mode has no IP to change." : IphoneByHand ? "The IP stays the same. The iPhone changes its IP by hand: use Change IP."
                : "The IP stays the same: the phone isn't ready to change it.", false);
        }
        // The account first: protection isn't turned on for a switch that then has no account to go to
        AccountItem next = null;
        if (steps.Account && (next = PickQuick(s)) == null) return false;
        // Steam has to let the switch happen before the IP changes or CS2 closes for it (a link, a loginusers.vdf it can't write back)
        if (next != null && !_demo && SwitchProblem(next) is string cant) { Log.Warn($"{from}: {cant}"); Toast(cant, Tone.Error); return false; }
        // Protection off: a new phone IP isn't what servers see (they see your home IP). With "Use the app with protection off" the IP
        // step is left out and said so; without it, or after a ban (the home IP is the banned one), one question turns protection on
        // first, as Join does, or nothing starts.
        if (ip && !_demo && !_s.NoPhone && IpStep(_protOn, _s.UseWithProtectionOff, steps.Ban) is var step && step != IpWhenOff.Change)
        {
            if (step == IpWhenOff.Skip)
            {
                ip = false;
                Log.Info($"{from}: protection is off, so servers see your home IP and the IP stays the same.");
                Toast("Protection is off: servers see your home IP, so the IP isn't changed.", false);
            }
            else
            {
                var why = steps.Ban ? "After a ban the server knows your home IP, so going back from it bans the next account too. " : "";
                if (!await Ask("Protection is off", why + "A new IP only counts with protection on: with it off, servers see your home IP. Turn protection on first, then go on?", "Turn on and go on")) return false;
                if (!await TurnProtectionOn(ask: false) || CommandCancelled) return false;
            }
        }
        if (!ip && next == null && s == null) { Toast(NothingText(steps.Ip || ipLeftOut != null, steps.Rejoin, ipLeftOut), false); return false; }
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

    /// <summary>
    /// A switch left with nothing to run. <paramref name="ipSkipped"/>: the IP step was on but the IP can't change now (said just before);
    /// <paramref name="serverMissing"/>: the rejoin step was on but there is no last server in your list. Only with every step off, Settings.
    /// <paramref name="ipWhy"/>: why the IP stays, when nothing said it before (no phone mode, the phone not ready on ADB).
    /// </summary>
    public static string NothingText(bool ipSkipped, bool serverMissing, string ipWhy = null) => !ipSkipped && !serverMissing
        ? "New IP + next account has nothing to do: turn on at least one step in Settings > New IP + next account."
        : "Nothing to do now: " + string.Join(" and ", new[] { ipSkipped ? "the IP stays the same" + (ipWhy != null ? $" ({ipWhy})" : "") : null,
            serverMissing ? "the last server isn't in your list" : null }.Where(x => x != null)) + ".";

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
        // Another command or a question open: wait another round instead of being refused or closing the question (review C10)
        for (var i = 0; i < FullTries && !SlotAutoJoins(true, CommandBusy, Dialog.Visibility == Visibility.Visible); i++) await Task.Delay(15000);
        if (_fullServer == s.Key && Steam.Cs2Running && _servers.Contains(s) && SlotAutoJoins(true, CommandBusy, Dialog.Visibility == Visibility.Visible)) await Join(s, fromSlotWait: true);
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
        // No server to go back to: nothing starts, so the IP doesn't change and drop you from the game for nothing
        if (!_servers.Any(x => x.Key == _s.LastServer))
        {
            var why = NoRejoinWhy(_s.LastServer);
            Log.Info($"{from}: not rejoining. {why}");
            Toast(why, false);
            return Task.CompletedTask;
        }
        Telemetry.Rejoin("try");
        (_rejoinTarget, _rejoinAt) = (_s.LastServer, DateTime.Now);
        if (!BannedOnLast()) return RunCommand(RejoinName(_s.NoPhone, _protOn, _s.UseWithProtectionOff), "starting…", () => RunSwitch(new SwitchSteps(true, false, true, true), from));
        Log.Info($"{from}: {ActiveAccount()?.Name} is banned on {_s.LastServer}, so the rejoin also switches account.");
        return RejoinAfterBan(from);
    }

    /// <summary>The rejoin's name in the top bar and the Log: just "Rejoin" when the IP step is left out (protection off with the option).</summary>
    public static string RejoinName(bool noPhone, bool protOn, bool useWithOff) =>
        !noPhone && IpStep(protOn, useWithOff) == IpWhenOff.Skip ? "Rejoin" : "Change IP and rejoin";

    /// <summary>The button in Status: the rejoin's name, but "Change IP and rejoin" after a ban there (the click gets a new IP first).</summary>
    public static string RejoinBtnText(bool noPhone, bool protOn, bool useWithOff, bool bannedOnLast) =>
        bannedOnLast ? "Change IP and rejoin" : RejoinName(noPhone, protOn, useWithOff);

    /// <summary>
    /// The rejoin hotkey on an iPhone, where the click would change the IP: refused, because the iPhone's Change IP is guided in the app
    /// (Airplane Mode on and off) and that can't be seen in game. Not when the IP step is left out, nor after a ban (the IP stays then).
    /// </summary>
    public static bool RejoinHotkeyRefused(bool iphoneByHand, bool noPhone, bool protOn, bool useWithOff, bool bannedOnLast) =>
        iphoneByHand && !noPhone && !bannedOnLast && IpStep(protOn, useWithOff) != IpWhenOff.Skip;

    /// <summary>
    /// The switch hotkey on an iPhone, the same rule: refused when the click would change the IP (<paramref name="steps"/> from
    /// <see cref="QuickSteps"/> still has it, and protection off with the option doesn't leave it out).
    /// </summary>
    public static bool SwitchHotkeyRefused(bool iphoneByHand, SwitchSteps steps, bool protOn, bool useWithOff) =>
        iphoneByHand && steps.Ip && IpStep(protOn, useWithOff) != IpWhenOff.Skip;

    /// <summary>The switch hotkey: as the button, except on an iPhone when it would change the IP, where nothing starts and a toast says why.</summary>
    Task SwitchFromHotkey(string from)
    {
        if (!SwitchHotkeyRefused(IphoneByHand, QuickChoice, _protOn, _s.UseWithProtectionOff)) return QuickSwitch(from);
        Log.Info($"{from}: not switching. {IphoneHotkeyText}");
        Toast(IphoneHotkeyText, false);
        return Task.CompletedTask;
    }

    public const string IphoneHotkeyText = "On an iPhone, Change IP is done in the app: its Airplane Mode steps can't be seen in game.";

    /// <summary>The rejoin hotkey: as the button, except on an iPhone, where nothing starts and a toast says why.</summary>
    Task RejoinFromHotkey(string from)
    {
        if (!RejoinHotkeyRefused(IphoneByHand, _s.NoPhone, _protOn, _s.UseWithProtectionOff, BannedOnLast())) return ChangeIpAndRejoin(from);
        Log.Info($"{from}: not rejoining. {IphoneHotkeyText}");
        Toast(IphoneHotkeyText, false);
        return Task.CompletedTask;
    }
}
