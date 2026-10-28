using System.Diagnostics;
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
            _cs2Console.Reset();
            _consoleServer = null;   // a server from the last session is not where the next one goes
            _cs2StartedAt = _cs2LogSeenAt = null;
            _cs2LogLen = -1;
            _cs2LogOk = false;
            return;
        }
        _cs2StartedAt ??= Cs2StartedAt();
        var path = Cs2Console.LogPath(_s.Cs2Path ?? Steam.Cs2Path(_s));
        if (path == null) return;
        CheckCondebug(path);
        // One disconnect writes several lines, and the first one isn't always the clearest ("kicked", then the ban code):
        // the strongest of the batch counts, with the furthest the join got.
        KickKind kind = KickKind.None;
        string kindLine = null, kindServer = null;
        int? signOn = null;
        foreach (var line in _cs2Console.ReadNew(path))
        {
            if (Cs2Console.IsConnectLine(line))
            {
                // The menu (loopback) or a Valve relay counts too: a later kick there is not about the last community server.
                // The kick read before it keeps the server it happened on.
                _consoleServer = Cs2Console.ConnectedServer(line);
                if (_consoleServer != null) { kind = KickKind.None; kindLine = null; signOn = null; }
                continue;
            }
            if (Cs2Console.IsNoSteamLogon(line)) { OnNoSteamLogon(line); continue; }
            var k = Cs2Console.Classify(line);
            signOn = Cs2Console.SignOnState(line) ?? signOn;
            if (k != KickKind.None && Cs2Console.Stronger(kind, k) != kind) { kind = k; kindLine = line; kindServer = _consoleServer; }
            else if (k == KickKind.None && Cs2Console.IsDisconnect(line) && _disconnectSamples++ < 40)
                Log.Info("CS2 console: " + Cut(line));   // samples to learn the real format
        }
        if (kind == KickKind.None) return;
        // Kicked before getting into the game, with no reason given: that's how ban plugins refuse a banned player who joins again.
        if (kind == KickKind.Kick && signOn is int st && st < 6)
        {
            Log.Info($"Kicked while still joining (sign-on state {st}): counted as a ban.");
            kind = KickKind.Ban;
        }
        OnKicked(kind, kindLine, kindServer);
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

    static DateTime? Cs2StartedAt()
    {
        DateTime? t = null;
        foreach (var p in Process.GetProcessesByName("cs2"))
        {
            try { t ??= p.StartTime; } catch { }
            finally { p.Dispose(); }
        }
        return t;
    }

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
        Log.Warn("CS2 is not writing console.log, so kicks and bans can't be detected. In Steam, add -condebug -conclearlog to CS2's launch options.");
        Toast("To detect kicks and bans, add -condebug -conclearlog to CS2's launch options in Steam (CS2 > Properties).", false);
    }

    void OnKicked(KickKind kind, string line, string consoleServer = null)
    {
        var what = kind == KickKind.Ban ? "banned" : "kicked";
        // The server CS2 was really on (also when joined from the console or the game's browser). The last server
        // joined through the app only stands in if that happened in this CS2 session, never one from days ago.
        var server = consoleServer ?? _consoleServer
            ?? (_s.LastServerAt != null && _cs2StartedAt != null && _s.LastServerAt >= _cs2StartedAt ? _s.LastServer : null);
        var inList = _servers.FirstOrDefault(x => x.Key == server);
        if (inList != null && server != _s.LastServer)
        {
            _s.LastServer = server;
            _s.LastServerAt = DateTime.Now;
            SaveSettings();
        }
        // One disconnect can come over two reads: within 20 s only a ban after a kick or a timeout still counts.
        var recent = (DateTime.Now - _lastKickAt).TotalSeconds < 20;
        if (recent && !(kind == KickKind.Ban && _lastKickKind is KickKind.Kick or KickKind.NoAnswer)) { Log.Info($"CS2 console ({what}): " + Cut(line)); return; }
        // The app's own IP change or the phone dropping also time the game out: that's not the server, and it doesn't count.
        if (kind == KickKind.NoAnswer && (_changingIp || _rejoining || _last?.Egress == null)) { Log.Info("CS2 lost the server while the connection was changing. Console: " + Cut(line)); return; }
        _lastKickAt = DateTime.Now;
        _lastKickKind = kind;
        System.Media.SystemSounds.Hand.Play();
        var acc = ActiveAccount();
        if (acc == null && !_demo && Steam.ActiveSteamId() != null) { LoadAccounts(); acc = ActiveAccount(); }   // an account Steam logged in to a moment ago
        if (kind == KickKind.NoAnswer)
        {
            Log.Warn($"{server ?? "The server"} stopped answering. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "no-answer", line, server });
            Toast(inList == null ? "The server stopped answering. If the phone connection is fine, it may have banned this IP at its firewall: change IP and join again."
                : $"The server stopped answering. If it banned this IP at its firewall, a new IP gets you back: {_s.HotkeyRejoin}.", false);
            UpdateRejoinBtn();
            return;
        }
        if (kind == KickKind.Steam)
        {
            // VAC or Steam refused the account: no rejoin offer and no server ban mark, a new IP changes nothing.
            Log.Warn($"Steam or VAC refused the connection to {_s.LastServer ?? "the server"}" + (acc != null ? $" on {acc.Name}" : "") + ". A new IP won't help. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "steam", line, server = _s.LastServer });
            Toast("Steam or VAC refused the connection. A new IP won't help: check the account.", Tone.Error);
            return;
        }
        Log.Warn($"You were {what} from {server ?? "the server"}" + (acc != null ? $" on {acc.Name}" : "") + ". Console: " + Cut(line));
        if (kind == KickKind.Ban && server != null)
        {
            if (acc == null) Log.Warn($"The ban on {server} wasn't marked: the account Steam is logged in to isn't in Accounts. Log in once with \"Remember me\" so it shows up.");
            else if (inList != null) MarkServerBan(acc, inList);
            else
            {
                // Not in your list: marked by address, so it counts as soon as you add the server.
                MarkServerBan(acc, server, null, null);
                Log.Info($"{server} isn't in your server list. Add it on Servers to rejoin it with the app and see the ban mark there.");
            }
        }
        Diag.Write("kick", new { kind = what, line, server = _s.LastServer });
        // A ban is on the IP and on the account: coming back needs both new, so the offer is the switch.
        // The hotkey only goes back to a server in your list: no promise for one that isn't.
        Toast(inList == null ? $"You were {what} from the server." + (server != null ? " It isn't in your list, so the rejoin hotkey can't take you back: add it on Servers." : "")
            : kind == KickKind.Ban ? $"You were banned from the server. Press {_s.HotkeyRejoin} for a new IP and another account, then rejoin."
            : $"You were {what} from the server. Press {_s.HotkeyRejoin} to change IP and rejoin.", false);
        UpdateRejoinBtn();
        if (inList != null && Dialog.Visibility != Visibility.Visible && !_changingIp && !_rejoining) _ = OfferRejoin(what);
    }

    async Task OfferRejoin(string what)
    {
        if (what != "banned")
        {
            if (await Ask($"You were {what}", $"CS2 says you were {what} from {_s.LastServer}. Change IP and rejoin?", "Change IP and rejoin", "Not now"))
                await ChangeIpAndRejoin("After the kick");
            return;
        }
        var next = NextQuick(_servers.FirstOrDefault(x => x.Key == _s.LastServer));
        if (next == null)
        {
            Toast($"Banned from {_s.LastServer}. No other account to rejoin with: the rest are skipped, on cooldown or banned there.", false);
            return;
        }
        if (await Ask("You were banned", $"CS2 says you were banned from {_s.LastServer}. The ban is on your IP and on the account, so a new IP alone won't get you back in.\n\nGet a new IP, log in to {next.Name} and rejoin?", "New IP, switch and rejoin", "Not now"))
            await RejoinAfterBan("After the ban");
    }

    /// <summary>New IP, the next account the server hasn't banned (nor its community), back to the last server.</summary>
    Task RejoinAfterBan(string from) => RunCommand("Switch and rejoin", "starting…", () => RunSwitch(new SwitchSteps(true, true, true, true), from));

    /// <summary>The account in use has a ban mark on the last server (or its community).</summary>
    bool BannedOnLast() =>
        ActiveAccount() is AccountItem a && _servers.FirstOrDefault(x => x.Key == _s.LastServer) is ServerItem s && a.BanFor(s.Key, s.ServerName, s.Network) != null;

    // ------------------------------------------------------------ Bans by a server (on the account, not Steam)

    AccountItem ActiveAccount()
    {
        var id = _demo ? null : Steam.ActiveSteamId();
        // Steam logged in to an account the list doesn't have yet: not the old one (the next tick reloads the list).
        if (id != null) return _accounts.FirstOrDefault(a => a.SteamId == id);
        return _accounts.FirstOrDefault(a => a.Active);
    }

    void MarkServerBan(AccountItem a, ServerItem s) => MarkServerBan(a, s.Key, s.ServerName, s.Network);

    void MarkServerBan(AccountItem a, string key, string name, string network)
    {
        a.Meta.ServerBans ??= new();
        a.Meta.ServerBans[key] = new ServerBan { At = DateTime.Now, Name = name, Network = ServerNetwork.Clean(network) };
        a.RefreshAll();
        SaveAccounts();
        ApplyServerBans();
        UpdateQuickInfo();
        var same = _servers.Count(x => x.Key != key && ServerNetwork.Same(key, name, network, x.Key, x.ServerName, x.Network));
        Log.Warn($"{a.Name} marked as banned on {key}" + (same > 0 ? $" and {same} other server(s) of the same network" : "") + ". Switch skips it there.");
        Diag.Write("server-ban", new { steamId = a.SteamId, server = key, name });
    }

    /// <summary>On each server row: whether the account you're logged in with is banned there (or on that server's network).</summary>
    void ApplyServerBans()
    {
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
        if (!await Ask($"Community of {s.Title}", "Servers with the same community name share ban marks: a ban on one counts on all of them, and Switch skips that account there.\n\n"
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

    void UpdateRejoinBtn()
    {
        RejoinBtn.IsEnabled = !_changingIp && !_rejoining && _s.LastServer != null && _phone?.Ready == true;
        RejoinBtn.ToolTip = (_s.LastServer == null ? "Join a server from Servers first. " : $"Changes the IP, then joins {_s.LastServer} again (with another account if this one is banned there). ")
            + $"Only joins with protection confirmed. In game: {_s.HotkeyRejoin}.";

    }


    /// <summary>What a switch does. Switch (button and hotkey) takes the steps from Settings; the rejoin hotkey is new IP, same account, rejoin.</summary>
    readonly record struct SwitchSteps(bool Ip, bool Account, bool Rejoin, bool OpenWith);

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
        if (ip && steps.Account && !_demo && (_adb == null || _phone?.Ready != true))
        {
            ip = false;
            Log.Info($"{from}: the phone isn't ready to change the IP, so only the account changes.");
            Toast("Switching the account only: the phone isn't ready to change the IP.", false);
        }
        AccountItem next = null;
        if (steps.Account && (next = PickQuick(s)) == null) return false;
        if (!ip && next == null && s == null) { Toast("Switch has nothing to do: turn on at least one step in Settings > Switch.", false); return false; }
        Log.Info($"{from}: " + string.Join(", ", new[] { ip ? "new IP" : null, next != null ? $"log in to {next.Name}" : null, s != null ? $"rejoin {s.Key}" : null }.Where(x => x != null)) + ".");
        _rejoining = true;
        UpdateRejoinBtn();
        try
        {
            if (ip && _demo) { ShowProgress($"Attempt 1/{_s.MaxAttempts}: turning off the phone's data for {_s.OffSeconds} s…"); await Task.Delay(1500); HideProgress(); }
            else if (ip && !await ChangeIp(confirm: false)) return false;
            // A new account restarts Steam through the new IP; Connect waits for its session before joining.
            if (next != null && !await SwitchTo(next, cs2: s == null, ask: false, openWith: steps.OpenWith)) return false;
            if (s == null) return true;
            // Same account on a new IP: Steam has to log in again. If it hasn't within 20 s, restart it (a dialog can't be seen in game).
            if (ip && next == null && !_demo && Steam.IsRunning && !await WaitSteamReady(20))
                return !CommandCancelled && await RestartSteamAndConnect(s, steps.OpenWith);
            return await Connect(s, openWith: steps.OpenWith);
        }
        finally { _rejoining = false; UpdateRejoinBtn(); }
    }

    async void Rejoin_Click(object sender, RoutedEventArgs e) => await ChangeIpAndRejoin("Button");

    /// <summary>
    /// The rejoin hotkey and the button: new IP, same account, back to the last server (with all the usual checks).
    /// If that account is banned there, the same account would be refused again, so it also switches account.
    /// </summary>
    Task ChangeIpAndRejoin(string from)
    {
        if (!BannedOnLast()) return RunCommand("Change IP and rejoin", "starting…", () => RunSwitch(new SwitchSteps(true, false, true, true), from));
        Log.Info($"{from}: {ActiveAccount()?.Name} is banned on {_s.LastServer}, so the rejoin also switches account.");
        return RejoinAfterBan(from);
    }
}
