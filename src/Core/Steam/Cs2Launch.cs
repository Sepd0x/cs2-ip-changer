using System.Diagnostics;

namespace SRT.Core;

/// <summary>
/// Opening and closing CS2 from the app. Static: what CS2 is started with, where its console.log is, whether it runs. One instance
/// (the main window's) remembers when the app itself closed CS2 and why, so that exit is never taken for a crash and the Configs
/// page doesn't copy a config CS2 never saved.
/// </summary>
public sealed class Cs2Launch
{
    /// <summary>cs2.exe's process name.</summary>
    public const string ProcessName = "cs2";
    public const string AppId = "730";

    /// <summary>CS2 writes console.log (cleared at each start) only with these: kicks and bans are read from it.
    /// Also what the app adds to CS2's launch options in Steam (<see cref="Cs2LaunchOptions"/>).</summary>
    public static readonly IReadOnlyList<string> ConsoleFlags = new[] { "-condebug", "-conclearlog" };

    /// <summary>Steam's arguments to open CS2 with its console log: "-applaunch 730 -condebug -conclearlog".</summary>
    public static readonly string SteamArgs = "-applaunch " + AppId + " " + string.Join(" ", ConsoleFlags);

    /// <summary>The same, for a Steam that starts again (-silent first when Steam opens minimised).</summary>
    public static string SteamRestartArgs(bool silent) => (silent ? "-silent " : "") + SteamArgs;

    /// <summary>How long after the app closed CS2 its exit still counts as the app's.</summary>
    public static readonly TimeSpan ClosedWindow = TimeSpan.FromSeconds(60);

    public static Process[] Processes() => Process.GetProcessesByName(ProcessName);

    public static bool Running
    {
        get
        {
            var all = Processes();
            foreach (var p in all) p.Dispose();
            return all.Length > 0;
        }
    }

    /// <summary>When the running CS2 started (null: not running, or Windows won't say).</summary>
    public static DateTime? StartedAt()
    {
        DateTime? t = null;
        foreach (var p in Processes())
        {
            try { t ??= p.StartTime; } catch { }
            finally { p.Dispose(); }
        }
        return t;
    }

    /// <summary>CS2's console.log next to <paramref name="cs2Exe"/> (the saved cs2.exe, or the one found: <see cref="Steam.FindCs2"/>). Null if CS2 isn't found.</summary>
    public static string ConsoleLog(string cs2Exe) => Cs2Console.LogPath(cs2Exe);

    readonly Func<DateTime> _now;
    readonly Action _kill;
    readonly Func<bool> _running;
    DateTime _closedAt = DateTime.MinValue;
    string _closedWhy;

    /// <param name="now">For the 60 s window (a wait, so the PC's clock, not <see cref="AppClock"/>). The tests pass their own.</param>
    /// <param name="kill">Kills CS2; the tests pass their own so no real CS2 is touched.</param>
    public Cs2Launch(Func<DateTime> now = null, Action kill = null, Func<bool> running = null)
    {
        _now = now ?? (() => DateTime.Now);
        _kill = kill ?? KillAll;
        _running = running ?? (() => Running);
    }

    /// <summary>
    /// Something the app does is about to close CS2 (Steam closing takes it along): the exit that follows is not a crash.
    /// <paramref name="reason"/> finishes "CS2 closed by …" in the Log ("the account switch").
    /// </summary>
    public void Closing(string reason)
    {
        _closedAt = _now();
        _closedWhy = reason;
    }

    /// <summary>Closes CS2 (stamped first, so the exit is never taken for a crash) and waits up to 3 s for it to go.</summary>
    public async Task Close(string reason)
    {
        Closing(reason);
        _kill();
        for (var i = 0; i < 20 && _running(); i++) await Task.Delay(150);
    }

    /// <summary>The app closed CS2 less than a minute ago.</summary>
    public bool ClosedByAppRecently => _now() - _closedAt < ClosedWindow;

    /// <summary>Why the app closed CS2 last ("the account switch"), while <see cref="ClosedByAppRecently"/>; null otherwise.</summary>
    public string ClosedReason => ClosedByAppRecently ? _closedWhy : null;

    static void KillAll()
    {
        foreach (var p in Processes())
            using (p)
                try { p.Kill(true); } catch { }
    }
}
