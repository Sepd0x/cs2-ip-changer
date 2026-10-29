using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using SRT.Core;

namespace SRT;

// Steam and CS2 seen from outside: Valve session (from the Steam log) and how CS2 exits
public partial class MainWindow
{
    readonly SteamLogWatcher _steamLog = new();
    /// <summary>Time (to the second) of the last IP change: until Steam logs on again after it, don't join.</summary>
    DateTime? _ipChangedAt;
    readonly List<Process> _cs2Watched = new();
    /// <summary>
    /// After a crash inside a server, that server keeps the old session. About 3 to 4 min later Steam
    /// tells it the session is over and it kicks the account with "No Steam logon", even if you already rejoined
    /// (measured: rejoining 2 min later got the kick; 3 min 25 s later did not). Until this time, don't return to the same server.
    /// </summary>
    static readonly TimeSpan AfterCrashWait = TimeSpan.FromMinutes(5);
    DateTime? _cs2CrashAt;
    string _cs2CrashServer;
    DateTime _cs2ClosedByAppAt;

    /// <summary>Steam has a Valve session and, if the IP changed, has already redone it on the new IP.</summary>
    /// <summary>
    /// Steam on the home network (Steam through the phone off) doesn't log on again when the phone's IP changes, and
    /// after 2 minutes without a new logon line the IP change no longer holds joins back (the log may just not show it).
    /// </summary>
    bool SteamReady => !Steam.IsRunning || (_steamLog.Online != false && (_ipChangedAt == null || !_s.SteamOnEgress
        || _steamLog.LastLogon >= _ipChangedAt || DateTime.Now - _ipChangedAt > TimeSpan.FromMinutes(2)));

    void WatchSteamLog()
    {
        foreach (var ev in _steamLog.Poll())
        {
            Diag.Write("steam-log", new { time = ev.Time.ToString("HH:mm:ss"), kind = ev.Kind, line = ev.Line });
            switch (ev.Kind)
            {
                case "dropped":
                    var why = Regex.Match(ev.Line, @"ConnectionDisconnected\('([^']*)'\)").Groups[1].Value;
                    Log.Warn($"Steam dropped from Valve at {ev.Time:HH:mm:ss} ({why})."
                        + (Steam.Cs2Running ? " If you were on a server, this is what causes the \"No Steam logon\" kick." : ""));
                    break;
                case "online":
                    Log.Ok($"Steam has a Valve session ({ev.Time:HH:mm:ss}).");
                    break;
                case "refused":
                    Log.Warn("Steam could not log on: " + ev.Line);
                    break;
            }
        }
    }

    /// <summary>
    /// Waits up to <paramref name="seconds"/> s for Steam to be ready. false = it was not.
    /// Steam's log doesn't always show the new login after an IP change; Steam holding its connections to Valve
    /// through the phone for 5 s in a row also counts (the old ones were cut when the IP changed).
    /// </summary>
    async Task<bool> WaitSteamReady(int seconds)
    {
        WatchSteamLog();
        if (SteamReady) return true;
        ShowProgress("Waiting for Steam to get a Valve session again…");
        using var wait = CommandWait();
        var linked = 0;
        try
        {
            for (var i = seconds; i > 0; i--)
            {
                await Task.Delay(1000);
                if (CommandCancelled) return false;
                WatchSteamLog();
                if (SteamReady) { Log.Ok("Steam is ready: you can join without getting kicked."); return true; }
                var egress = _last?.Egress?.LocalIp;
                linked = egress != null && Net.ReadSteamLink().LocalIps.Contains(egress) ? linked + 1 : 0;
                if (linked >= 5)
                {
                    _ipChangedAt = null;
                    Log.Ok("Steam is connected to Valve through the phone (its log didn't show the new login, so the connection counts): you can join.");
                    return true;
                }
                ShowProgress($"Waiting for Steam to get a Valve session again… {i} s");
            }
            return false;
        }
        finally { HideProgress(); }
    }

    /// <summary>After the IP changes: cuts Steam's old connections so it reconnects now, not in 30 s.</summary>
    void ResetSteamAfterIpChange()
    {
        if (!Steam.IsRunning) return;
        var link = Net.ReadSteamLink();
        var n = Net.ResetLink(link);
        Diag.Write("steam-reset", new { connections = n, total = link.Keys.Count });
        if (n > 0) Log.Info($"Closed {Fmt.Count(n, "old Steam connection")}. Steam reconnects on the new IP.");
    }

    // ------------------------------------------------------------ How CS2 exits

    static readonly Dictionary<uint, string> CrashCodes = new()
    {
        [0xC0000005] = "invalid memory access",
        [0xC0000409] = "stack corrupted",
        [0xC0000374] = "heap corrupted",
        [0xC000001D] = "illegal instruction",
        [0xC00000FD] = "stack exhausted",
        [0xC0000094] = "divide by zero",
        [0x80000003] = "forced stop (breakpoint)",
        [0xE06D7363] = "unhandled exception",
    };

    void WatchCs2Exit()
    {
        foreach (var p in Process.GetProcessesByName("cs2"))
        {
            if (_cs2Watched.Any(w => w.Id == p.Id)) { p.Dispose(); continue; }
            try
            {
                var start = p.StartTime;
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) =>
                {
                    int? code = null;
                    try { code = p.ExitCode; } catch { }
                    Dispatcher.BeginInvoke(() => OnCs2Exit(p, code, start));
                };
                _cs2Watched.Add(p);
            }
            catch (Exception ex) { Log.Warn("Cannot watch CS2: " + ex.Message); p.Dispose(); }
        }
    }

    void OnCs2Exit(Process p, int? code, DateTime start)
    {
        _cs2Watched.Remove(p);
        p.Dispose();
        ConfigsCs2Closed();   // queued config copies, and Latest change wins (Configs page)
        var min = (int)(DateTime.Now - start).TotalMinutes;
        var hex = code == null ? "?" : $"0x{(uint)code.Value:X8}";
        var name = code is int c && CrashCodes.TryGetValue((uint)c, out var n) ? n : null;
        Diag.Write("cs2-exit", new { code, hex, reason = name, minutes = min });
        if ((DateTime.Now - _cs2ClosedByAppAt).TotalSeconds < 60) Log.Info($"CS2 closed by the account switch after {min} min.");
        else if (code == 0) Log.Info($"CS2 closed normally after {min} min.");
        else
        {
            Log.Warn($"CS2 closed with error {hex}{(name != null ? $" ({name})" : "")} after {min} min. If it was a crash, use Copy all in Log and add it to your report.");
            if (min >= 1 && _s.LastServer != null)
            {
                _cs2CrashAt = DateTime.Now;
                _cs2CrashServer = _s.LastServer;
                var until = _cs2CrashAt.Value + AfterCrashWait;
                Log.Warn($"CS2 crashed. Do not rejoin {_cs2CrashServer} before {until:HH:mm}: the server still has the old session and kicks with \"No Steam logon\". Another server is fine right away.");
                Toast($"CS2 crashed. Rejoin the same server only after {until:HH:mm} (another one is fine right away).", false);
            }
            else Toast($"CS2 closed with error {hex}. Details are in the Log.", false);
        }
    }

    /// <summary>If CS2 crashed on this server recently, asks and (if you want) waits. false = don't join.</summary>
    async Task<bool> WaitAfterCrash(ServerItem s)
    {
        if (_cs2CrashAt == null || s.Key != _cs2CrashServer) return true;
        var until = _cs2CrashAt.Value + AfterCrashWait;
        if (DateTime.Now >= until) return true;
        var wait = await Ask("CS2 crashed on this server recently",
            $"CS2 crashed at {_cs2CrashAt:HH:mm} on {s.Title}. The server still remembers your old session. If you rejoin now, it kicks you a few minutes later (\"No Steam logon\").\n\n" +
            $"Wait until {until:HH:mm:ss} and join automatically? Another server is fine right away.", "Wait and join", "Join now");
        Diag.Write("wait-after-crash", new { server = s.Key, seconds = (int)(until - DateTime.Now).TotalSeconds, waited = wait });
        if (CommandCancelled) return false;
        if (!wait) return true;
        ShowProgress("Waiting for the server to drop the old session…");
        using var waiting = CommandWait();
        try
        {
            while (DateTime.Now < until)
            {
                if (CommandCancelled) return false;
                ShowProgress($"Waiting for the server to drop the old session… {Fmt.Left(until - DateTime.Now)}");
                await Task.Delay(1000);
            }
        }
        finally { HideProgress(); }
        return true;
    }
}
