namespace SRT.Core;

/// <summary>
/// The time the app shows and stamps (the Log, Previous IPs, cooldowns, "5 min ago"). The demo of the dev build stops it at one
/// moment, so its captures come out the same on every run. Waits and timeouts measure real time with DateTime.Now, never this.
/// </summary>
public static class AppClock
{
    static DateTime? _stopped;

    public static DateTime Now => _stopped ?? DateTime.Now;
    public static DateTime Today => Now.Date;
    public static DateTime UtcNow => _stopped?.ToUniversalTime() ?? DateTime.UtcNow;
    public static bool Stopped => _stopped != null;

    /// <summary>From now on the clock says <paramref name="local"/> (local time) and doesn't move.</summary>
    public static void StopAt(DateTime local) => _stopped = DateTime.SpecifyKind(local, DateTimeKind.Local);

    /// <summary>The clock follows Windows again.</summary>
    public static void Run() => _stopped = null;
}
