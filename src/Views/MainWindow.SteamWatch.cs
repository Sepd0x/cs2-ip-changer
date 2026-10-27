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
    /// (Oct 27: rejoining 2 min later got a kick; 3 min 25 s later did not). Until this time, don't return to the same server.
    /// </summary>
    static readonly TimeSpan AfterCrashWait = TimeSpan.FromMinutes(5);
    DateTime? _cs2CrashAt;
    string _cs2CrashServer;

    /// <summary>Steam has a Valve session and, if the IP changed, has already redone it on the new IP.</summary>
    bool SteamReady => !Steam.IsRunning || (_steamLog.Online != false && (_ipChangedAt == null || _steamLog.LastLogon >= _ipChangedAt));

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

    /// <summary>Waits up to <paramref name="seconds"/> s for Steam to be ready. false = it was not.</summary>
    async Task<bool> WaitSteamReady(int seconds)
    {
        WatchSteamLog();
        if (SteamReady) return true;
        ShowProgress("Waiting for Steam to get a Valve session again…");
        try
        {
            for (var i = seconds; i > 0; i--)
            {
                await Task.Delay(1000);
                WatchSteamLog();
                if (SteamReady) { Log.Ok("Steam is ready: you can join without getting kicked."); return true; }
                ProgressText.Text = $"Waiting for Steam to get a Valve session again… {i} s";
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
        if (n > 0) Log.Info($"Cut {n} old Steam connection(s): it reconnects now on the new IP.");
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
        var min = (int)(DateTime.Now - start).TotalMinutes;
        var hex = code == null ? "?" : $"0x{(uint)code.Value:X8}";
        var name = code is int c && CrashCodes.TryGetValue((uint)c, out var n) ? n : null;
        Diag.Write("cs2-exit", new { code, hex, reason = name, minutes = min });
        if (code == 0) Log.Info($"CS2 closed normally after {min} min.");
        else
        {
            Log.Warn($"CS2 closed with error {hex}{(name != null ? $" ({name})" : "")} after {min} min. If it was a crash, export diagnostics in Log.");
            if (min >= 1 && _s.LastServer != null)
            {
                _cs2CrashAt = DateTime.Now;
                _cs2CrashServer = _s.LastServer;
                var until = _cs2CrashAt.Value + AfterCrashWait;
                Log.Warn($"CS2 crashed. Do not rejoin {_cs2CrashServer} before {until:HH:mm}: the server still has the old session and kicks with \"No Steam logon\". Another server is fine right away.");
                Toast($"CS2 crashed. Rejoin the same server only after {until:HH:mm} (another one is fine right away).", false);
            }
            else Toast($"CS2 closed with error {hex}. It is in the diagnostics.", false);
        }
    }

    /// <summary>If CS2 crashed on this server recently, asks and (if you want) waits. false = don't join.</summary>
    async Task<bool> WaitAfterCrash(ServerItem s)
    {
        if (_cs2CrashAt == null || s.Key != _cs2CrashServer) return true;
        var until = _cs2CrashAt.Value + AfterCrashWait;
        if (DateTime.Now >= until) return true;
        var wait = await Ask("CS2 crashed on this server recently",
            $"CS2 crashed at {_cs2CrashAt:HH:mm} inside {s.Title}. The server still has that session and, a few minutes later, kicks the account with \"No Steam logon\", even if you already rejoined.\n\n" +
            $"Wait until {until:HH:mm:ss} and join automatically? Another server is fine right away.", "Wait and join", "Join now");
        Diag.Write("wait-after-crash", new { server = s.Key, seconds = (int)(until - DateTime.Now).TotalSeconds, waited = wait });
        if (!wait) return true;
        ShowProgress("Waiting for the server to drop the old session…");
        try
        {
            while (DateTime.Now < until)
            {
                ProgressText.Text = $"Waiting for the server to drop the old session… {Fmt.Left(until - DateTime.Now)}";
                await Task.Delay(1000);
            }
        }
        finally { HideProgress(); }
        return true;
    }
}
