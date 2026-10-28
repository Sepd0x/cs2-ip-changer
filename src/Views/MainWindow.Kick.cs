using System.Diagnostics;
using System.Windows;
using SRT.Core;

namespace SRT;

// Kick or ban seen in CS2's console, "Change IP and rejoin" (button and its hotkey) and the switch (new IP, next account, rejoin)
public partial class MainWindow
{
    readonly ConsoleTail _cs2Console = new();
    DateTime _lastKickAt = DateTime.MinValue;
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
            _cs2StartedAt = _cs2LogSeenAt = null;
            _cs2LogLen = -1;
            _cs2LogOk = false;
            return;
        }
        _cs2StartedAt ??= Cs2StartedAt();
        var path = Cs2Console.LogPath(_s.Cs2Path ?? Steam.Cs2Path(_s));
        if (path == null) return;
        CheckCondebug(path);
        var kicked = false;
        foreach (var line in _cs2Console.ReadNew(path))
        {
            var joined = Cs2Console.ConnectedServer(line);
            if (joined != null) _consoleServer = joined;
            var kind = Cs2Console.Classify(line);
            if (kind != KickKind.None && !kicked) { kicked = true; OnKicked(kind, line); }
            else if (Cs2Console.IsDisconnect(line) && _disconnectSamples++ < 40)
                Log.Info("CS2 console: " + Cut(line));   // samples to learn the real format
        }
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

    void OnKicked(KickKind kind, string line)
    {
        var what = kind == KickKind.Ban ? "banned" : "kicked";
        // The server CS2 was really on (also when joined from the console), if it's in the list.
        if (_consoleServer != null && _servers.Any(x => x.Key == _consoleServer) && _consoleServer != _s.LastServer)
        {
            _s.LastServer = _consoleServer;
            _s.LastServerAt = DateTime.Now;
            SaveSettings();
        }
        // One kick can write several lines; the raw line always goes to the Log to collect real samples.
        if ((DateTime.Now - _lastKickAt).TotalSeconds < 20) { Log.Info($"CS2 console ({what}): " + Cut(line)); return; }
        _lastKickAt = DateTime.Now;
        System.Media.SystemSounds.Hand.Play();
        var acc = ActiveAccount();
        if (kind == KickKind.Steam)
        {
            // VAC or Steam refused the account: no rejoin offer and no server ban mark, a new IP changes nothing.
            Log.Warn($"Steam or VAC refused the connection to {_s.LastServer ?? "the server"}" + (acc != null ? $" on {acc.Name}" : "") + ". A new IP won't help. Console: " + Cut(line));
            Diag.Write("kick", new { kind = "steam", line, server = _s.LastServer });
            Toast("Steam or VAC refused the connection. A new IP won't help: check the account.", Tone.Error);
            return;
        }
        Log.Warn($"You were {what} from {_s.LastServer ?? "the server"}" + (acc != null ? $" on {acc.Name}" : "") + ". Console: " + Cut(line));
        if (kind == KickKind.Ban && _servers.FirstOrDefault(x => x.Key == _s.LastServer) is ServerItem banned && acc != null) MarkServerBan(acc, banned);
        Diag.Write("kick", new { kind = what, line, server = _s.LastServer });
        Toast(_s.LastServer == null ? $"You were {what} from the server."
            : $"You were {what} from the server. Press {_s.HotkeyRejoin} to change IP and rejoin.", false);
        UpdateRejoinBtn();
        if (_s.LastServer != null && Dialog.Visibility != Visibility.Visible && !_changingIp && !_rejoining) _ = OfferRejoin(what);
    }

    async Task OfferRejoin(string what)
    {
        if (await Ask($"You were {what}", $"CS2 says you were {what} from {_s.LastServer}. Change IP and rejoin?" + (what == "banned" ? "\n\nIf the ban is on your Steam account, a new IP won't help." : ""), "Change IP and rejoin", "Not now"))
            await ChangeIpAndRejoin("After the " + (what == "banned" ? "ban" : "kick"));
    }

    // ------------------------------------------------------------ Bans by a server (on the account, not Steam)

    AccountItem ActiveAccount()
    {
        var id = _demo ? null : Steam.ActiveSteamId();
        return _accounts.FirstOrDefault(a => a.SteamId == id) ?? _accounts.FirstOrDefault(a => a.Active);
    }

    void MarkServerBan(AccountItem a, ServerItem s)
    {
        a.Meta.ServerBans ??= new();
        a.Meta.ServerBans[s.Key] = new ServerBan { At = DateTime.Now, Name = s.ServerName, Network = ServerNetwork.Clean(s.Network) };
        a.RefreshAll();
        SaveAccounts();
        ApplyServerBans();
        UpdateQuickInfo();
        var same = _servers.Count(x => x != s && ServerNetwork.Same(s.Key, s.ServerName, s.Network, x.Key, x.ServerName, x.Network));
        Log.Warn($"{a.Name} marked as banned on {s.Key}" + (same > 0 ? $" and {same} other server(s) of the same network" : "") + ". Switch skips it there.");
        Diag.Write("server-ban", new { steamId = a.SteamId, server = s.Key, name = s.ServerName });
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
        RejoinBtn.ToolTip = (_s.LastServer == null ? "Join a server from Servers first. " : $"Changes the IP, then joins {_s.LastServer} again. ")
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
            if (ip && next == null && !_demo && Steam.IsRunning && !await WaitSteamReady(20)) return await RestartSteamAndConnect(s, steps.OpenWith);
            return await Connect(s, openWith: steps.OpenWith);
        }
        finally { _rejoining = false; UpdateRejoinBtn(); }
    }

    async void Rejoin_Click(object sender, RoutedEventArgs e) => await ChangeIpAndRejoin("Button");

    /// <summary>The rejoin hotkey and the button: new IP, same account, back to the last server (with all the usual checks).</summary>
    Task ChangeIpAndRejoin(string from) => RunCommand("Change IP and rejoin", "starting…", () => RunSwitch(new SwitchSteps(true, false, true, true), from));
}
