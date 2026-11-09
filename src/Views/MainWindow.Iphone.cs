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

    /// <summary>After this long without the iPhone, Change IP says how to bring it back (Personal Hotspot screen, or the cable).</summary>
    public static readonly TimeSpan IphoneNudgeAfter = TimeSpan.FromSeconds(25);

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

    /// <summary>Change IP on an iPhone, guided: you toggle Airplane Mode, the app sees the drop, waits for it back and measures.</summary>
    async Task<bool> IphoneChangeIp()
    {
        // The iPhone's connection, protected or still being checked: with protection off there is never a protected one (Egress)
        if (PhoneLink.Exit(_last) is not { } exit) { Toast("Connect the iPhone first: " + PhoneSetup.IphoneHotspot, false); return false; }
        var first = _egressPublic;
        var since = DateTime.Now.AddSeconds(-1);   // the history line the IP check adds on the way back belongs to this change
        // Airplane Mode drops the iPhone on purpose: no alarm and no cable hint. From before the question, because people often turn
        // Airplane Mode on while they read it (9 nov.: the Log said "The phone connection dropped" in the middle of Change IP)
        _ipChangeQuiet = true;
        try
        {
            if (!await Ask("Change IP on the iPhone",
                    "1. On the iPhone, turn Airplane Mode on.\n2. Wait 3 seconds.\n3. Turn Airplane Mode off.\n\n"
                    + (exit.IsWifi ? "Keep Personal Hotspot on. " : "Keep the cable plugged in. ") + "The app waits for the iPhone to come back and shows the new IP. "
                    + (IpChangeDrops ? "If you are on a server, you will drop." : "Servers see the new IP once protection is on."), "I'm doing it", "Cancel"))
                return false;
            Log.Info("iPhone: Change IP started. Airplane Mode on, then off, on the iPhone.");
            using var wait = CommandWait();
            var sw = Stopwatch.StartNew();
            var sawDrop = PhoneLink.Exit(_last) == null;   // already off while the question was open
            var droppedAt = sawDrop ? TimeSpan.Zero : (TimeSpan?)null;
            var nudged = false;
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
                // The hotspot over USB doesn't always come back by itself after Airplane Mode: say how, once, and keep waiting
                if (!nudged && droppedAt is TimeSpan d && IphoneNeedsNudge(connected, sw.Elapsed - d))
                {
                    nudged = true;
                    Log.Info("iPhone: not back yet. " + PhoneSetup.IphoneBringBack);
                    Toast("Airplane Mode off and the iPhone still isn't back? " + PhoneSetup.IphoneBringBack, false);
                }
                var step = IphoneChangeStep(sawDrop, connected, first, now, sw.Elapsed, IphoneLimit(sawDrop));
                switch (step)
                {
                    case IphoneChange.BackNew:
                        _egressInfo = await PhoneIpInfo(now);
                        AddHistory(now, _egressInfo, "trocado", since);
                        ResetQuality();
                        Log.Ok($"iPhone: IP changed: {first ?? "?"} → {now}.");
                        Toast($"New IP: {now}.", true);
                        return true;
                    case IphoneChange.BackSame:
                        Log.Warn($"iPhone: back with the same IP ({now}).");
                        Toast("The carrier gave the same IP. Try again and leave Airplane Mode on a bit longer (10 to 20 s).", false);
                        return false;
                    case IphoneChange.TimedOut:
                        Log.Warn(sawDrop ? "iPhone: Change IP waited 5 minutes for the iPhone to come back." : "iPhone: Change IP waited 90 s without seeing Airplane Mode.");
                        Toast(sawDrop ? "The iPhone hasn't come back. " + PhoneSetup.IphoneBringBack : "The iPhone's connection never dropped: turn Airplane Mode on, then off.", false);
                        return false;
                }
                Step(step == IphoneChange.Off ? (nudged ? "not back yet: open Personal Hotspot on the iPhone, or plug the cable in again…" : "Airplane Mode on: now turn it off on the iPhone…")
                    : step == IphoneChange.Measuring ? "the iPhone is back: checking the new IP…" : "turn Airplane Mode on, on the iPhone…");
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
