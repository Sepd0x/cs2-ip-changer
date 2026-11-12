using System.Diagnostics;
using SRT.Core;

namespace SRT;

// beta.11: the iPhone. No ADB (Android only), so the app can't switch its data off and on: Change IP tells you when to toggle
// Airplane Mode on the iPhone, then waits for the connection to come back and measures the new IP.
public partial class MainWindow
{
    /// <summary>
    /// (is it an iPhone, is its connection up). The tethering adapter is checked first; the USB scan only without it. An iPhone
    /// used only through its hotspot over Wi-Fi is known from the saved answer (beta.12).
    /// </summary>
    async Task<(bool Iphone, bool Tethered)> IphoneState()
    {
        if (_demo) return (_iphone, _iphone && _last?.Egress != null);
        var adapter = _last?.Adapters?.FirstOrDefault(a => a.IsPhone && a.Up && PhoneUsb.IsIphone(a));
        var usb = adapter != null ? PhoneUsbState.None : await Task.Run(PhoneUsb.Scan);
        _iphone = NotePhoneKind(PhoneUsb.IphoneSeen(false, usb, adapter, _s.EgressSsid));
        if (adapter != null) return (_iphone, true);
        var hotspot = PhoneLink.Exit(_last) is { } exit && PhoneLink.IsHotspot(_s.EgressSsid, exit.IsWifi);
        return (_iphone, _iphone && hotspot);
    }

    /// <summary>Keeps what the PC saw about the kind of phone (Settings.PhoneIsIphone) and says whether it is an iPhone now.</summary>
    bool NotePhoneKind(bool? seen)
    {
        if (seen is bool b && b != _s.PhoneIsIphone) { _s.PhoneIsIphone = b; SaveSettings(); }
        return PhoneUsb.IsIphoneNow(seen, _s.PhoneIsIphone, _s.EgressSsid);
    }

    /// <summary>How long Change IP waits to see Airplane Mode go on (the connection drop).</summary>
    static readonly TimeSpan IphoneWait = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How long it waits for the iPhone to come back once it dropped (9 nov.: on the owner's iPhone the hotspot over USB didn't come
    /// back by itself after Airplane Mode, and 90 s gave up before he could open Personal Hotspot). Cancel stops it sooner.
    /// </summary>
    static readonly TimeSpan IphoneBackWait = TimeSpan.FromMinutes(5);

    /// <summary>
    /// After this long without the iPhone, Change IP says how to bring it back by hand (Personal Hotspot screen, or the cable): by
    /// then the app's own reconnects (<see cref="IphoneRestartDue"/>) had their go.
    /// </summary>
    public static readonly TimeSpan IphoneNudgeAfter = TimeSpan.FromSeconds(75);

    /// <summary>
    /// The app reconnects the iPhone itself (a USB restart, the same as the cable out and in) when it hasn't come back this long after
    /// it dropped, then again every <see cref="IphoneRestartEvery"/>, at most <see cref="IphoneRestartMax"/> times. 9 nov.: after
    /// Airplane Mode the iPhone's hotspot over USB only came back with the cable out and in, and the restart brought it back at once.
    /// Airplane Mode still on: a restart does nothing and the next one comes 20 s later.
    /// </summary>
    public static readonly TimeSpan IphoneRestartAfter = TimeSpan.FromSeconds(15), IphoneRestartEvery = TimeSpan.FromSeconds(20);
    public const int IphoneRestartMax = 8;

    public static bool IphoneRestartDue(TimeSpan sinceDrop, TimeSpan? sinceRestart, int restarts) =>
        sinceDrop >= IphoneRestartAfter && restarts < IphoneRestartMax && (sinceRestart == null || sinceRestart >= IphoneRestartEvery);

    /// <summary>Restarts the iPhone's USB device (pnputil, from System32; the app runs as admin). False when there is no iPhone on USB.</summary>
    async Task<bool> ReconnectIphone()
    {
        if (_demo) return false;
        var args = PhoneUsb.RestartArgs(await Task.Run(PhoneUsb.IphoneDeviceId));
        if (args == null) return false;
        var r = await Proc.Run("pnputil", args, 30000);
        if (!r.Ok && _iphoneRestartFailed++ == 0) Log.Warn("iPhone: couldn't reconnect it over USB: " + r.Output);
        return r.Ok;
    }

    int _iphoneRestartFailed;

    // The iPhone dropped while protected, with no Change IP running: the same reconnects, on their own (9 nov.)
    DateTime? _iphoneLostAt, _iphoneRestartAt;
    int _iphoneRestarts;
    bool _iphoneReconnecting;

    /// <summary>
    /// Called on every report: while protection is on and the iPhone's connection is gone (not during Change IP, which has its own),
    /// the app reconnects it over USB on the same schedule. Its connection back resets everything.
    /// </summary>
    async void IphoneWatch(GuardReport rep, bool home)
    {
        if (_demo || home || _changingIp || _ipChangeQuiet || !(_iphone || _s.PhoneIsIphone) || PhoneLink.Exit(rep) != null)
        {
            _iphoneLostAt = _iphoneRestartAt = null;
            _iphoneRestarts = 0;
            return;
        }
        var now = DateTime.Now;
        _iphoneLostAt ??= now;
        if (_iphoneReconnecting || !IphoneRestartDue(now - _iphoneLostAt.Value, _iphoneRestartAt is DateTime at ? now - at : null, _iphoneRestarts)) return;
        _iphoneReconnecting = true;
        try
        {
            _iphoneRestartAt = now;
            if (await ReconnectIphone() && _iphoneRestarts++ == 0) Log.Info("iPhone: its connection didn't come back, so the app reconnected it over USB (like the cable out and in).");
        }
        finally { _iphoneReconnecting = false; }
    }

    /// <summary>The wait that applies now: to see the drop, or (once it dropped) for the iPhone to come back.</summary>
    public static TimeSpan IphoneLimit(bool sawDrop) => sawDrop ? IphoneBackWait : IphoneWait;

    /// <summary>The iPhone is still away long enough after the drop that it needs a hand (the hint shows once).</summary>
    public static bool IphoneNeedsNudge(bool connected, TimeSpan sinceDrop) => !connected && sinceDrop >= IphoneNudgeAfter;

    /// <summary>
    /// What the guided Change IP says now, from what it sees: the connection still up (Airplane Mode not on yet), gone (it is on),
    /// back with the same IP, or back with a new one.
    /// </summary>
    public enum IphoneChange { WaitingOff, Off, Measuring, BackSame, BackNew, TimedOut }

    public static IphoneChange IphoneChangeStep(bool sawDrop, bool connected, string first, string now, TimeSpan elapsed, TimeSpan limit) =>
        connected && sawDrop && now != null && now != first ? IphoneChange.BackNew
        : elapsed > limit ? IphoneChange.TimedOut
        : connected && sawDrop && now != null ? IphoneChange.BackSame
        : connected && sawDrop ? IphoneChange.Measuring
        : !connected ? IphoneChange.Off
        : IphoneChange.WaitingOff;

    /// <summary>
    /// 10 nov. (dashboard audit, P2): the stats of a guided Change IP on an iPhone, as on Android: a new IP counts its method
    /// (Airplane Mode), time and one attempt (ip_changed comes from the history line), the same IP counts ip_same, and no
    /// connection back after the drop counts ip_no_network. Never seeing Airplane Mode isn't a network failure: nothing.
    /// </summary>
    public static List<string> IphoneChangeCounts(IphoneChange step, bool sawDrop, TimeSpan took) => step switch
    {
        IphoneChange.BackNew => Telemetry.ChangeCounts(airplane: true, took, 1, null, 0),
        IphoneChange.BackSame => new() { "ip_same" },
        IphoneChange.TimedOut when sawDrop => new() { "ip_no_network" },
        _ => new(),
    };

    /// <summary>Change IP on an iPhone, guided: you toggle Airplane Mode, the app sees the drop, waits for it back and measures.</summary>
    async Task<bool> IphoneChangeIp()
    {
        // The iPhone's connection, protected or still being checked: with protection off there is never a protected one (Egress)
        if (PhoneLink.Exit(_last) is not { } exit) { Toast(Loc.T("Connect the iPhone first:") + " " + Loc.T(PhoneSetup.IphoneHotspot), false); return false; }
        var first = _egressPublic;
        var since = DateTime.Now.AddSeconds(-1);   // the history line the IP check adds on the way back belongs to this change
        // Airplane Mode drops the iPhone on purpose: no alarm and no cable hint (9 nov.: the Log said "The phone connection dropped")
        _ipChangeQuiet = true;
        try
        {
            // No question first (9 nov., the owner: a button to press before it waited was confusing): Change IP starts
            // waiting at once, the bar says what to do on the iPhone and has Cancel, and the toast says it once in full
            Log.Info("iPhone: Change IP started. Airplane Mode on, then off, on the iPhone.");
            Toast(Loc.T("On the iPhone: Airplane Mode on, wait 3 seconds, then off.") + " " + (exit.IsWifi ? Loc.T("Keep Personal Hotspot on.") : Loc.T("Keep the cable plugged in.")) + " "
                + Loc.T("The app waits and shows the new IP.") + " " + (IpChangeDrops ? Loc.T("If you are on a server, you will drop.") : ""), true);
            using var wait = CommandWait();
            var sw = Stopwatch.StartNew();
            var sawDrop = PhoneLink.Exit(_last) == null;   // already off when Change IP was pressed
            var droppedAt = sawDrop ? TimeSpan.Zero : (TimeSpan?)null;
            var nudged = false;
            TimeSpan? restartedAt = null;
            var restarts = 0;
            while (true)
            {
                if (CommandCancelled) { Log.Info("iPhone: Change IP cancelled."); return false; }
                await SyncNow(force: true);
                var connected = PhoneLink.Exit(_last) != null;
                if (!connected && droppedAt == null) droppedAt = sw.Elapsed;
                sawDrop |= !connected;
                string now = null;
                // Only a checked IP counts: back on the home Wi-Fi the IP measured is the home one, and it never trusts that
                if (connected && sawDrop) { await RefreshPublicIps(); now = _egressFailSince == null && _verifiedKey != null && _verifiedKey == _last?.CandidateKey ? _egressPublic : null; }
                // The hotspot over USB doesn't always come back by itself after Airplane Mode: the app reconnects it over USB
                if (!connected && droppedAt is TimeSpan dr && IphoneRestartDue(sw.Elapsed - dr, sw.Elapsed - restartedAt, restarts))
                {
                    restartedAt = sw.Elapsed;
                    Step(Loc.T("reconnecting the iPhone over USB…"));
                    if (await ReconnectIphone() && restarts++ == 0) Log.Info("iPhone: not back yet, so the app reconnected it over USB (like the cable out and in).");
                }
                // Still not back after the app's own tries: say how by hand, once, and keep waiting
                if (!nudged && droppedAt is TimeSpan d && IphoneNeedsNudge(connected, sw.Elapsed - d))
                {
                    nudged = true;
                    Log.Info("iPhone: not back yet. " + PhoneSetup.IphoneBringBack);
                    Toast(Loc.T("Airplane Mode off and the iPhone still isn't back?") + " " + Loc.T(PhoneSetup.IphoneBringBack), false);
                }
                var step = IphoneChangeStep(sawDrop, connected, first, now, sw.Elapsed, IphoneLimit(sawDrop));
                foreach (var n in IphoneChangeCounts(step, sawDrop, sw.Elapsed)) Telemetry.Count(n);
                switch (step)
                {
                    case IphoneChange.BackNew:
                        _egressInfo = await PhoneIpInfo(now);
                        AddHistory(now, _egressInfo, "trocado", since);
                        ResetQuality();
                        Log.Ok($"iPhone: IP changed: {first ?? "?"} → {now}.");
                        Toast(Loc.F("New IP: {0}.", now), true);
                        return true;
                    case IphoneChange.BackSame:
                        Log.Warn($"iPhone: back with the same IP ({now}).");
                        Toast(Loc.T("The carrier gave the same IP. Try again and leave Airplane Mode on a bit longer (10 to 20 s)."), false);
                        return false;
                    case IphoneChange.TimedOut:
                        Log.Warn(sawDrop ? "iPhone: Change IP waited 5 minutes for the iPhone to come back." : "iPhone: Change IP waited 90 s without seeing Airplane Mode.");
                        Toast(sawDrop ? Loc.T("The iPhone hasn't come back.") + " " + Loc.T(PhoneSetup.IphoneBringBack) : Loc.T("The iPhone's connection never dropped: turn Airplane Mode on, then off."), false);
                        return false;
                }
                Step(Loc.T(step == IphoneChange.Off ? (nudged ? "not back yet: open Personal Hotspot on the iPhone, or plug the cable in again…" : "Airplane Mode on: now turn it off on the iPhone…")
                    : step == IphoneChange.Measuring ? "the iPhone is back: checking the new IP…" : "on the iPhone: Airplane Mode on, wait 3 s, then off…"));
                await Task.Delay(1500);
            }
        }
        finally
        {
            try { await SyncNow(force: true); }
            finally { _ipChangeQuiet = false; }
        }
    }
}
