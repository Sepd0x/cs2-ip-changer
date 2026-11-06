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

    /// <summary>How long the iPhone gets to come back after Airplane Mode (the hotspot needs a moment to share again).</summary>
    static readonly TimeSpan IphoneWait = TimeSpan.FromSeconds(90);

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
        if (_last?.Egress == null) { Toast("Connect the iPhone first: " + PhoneSetup.IphoneHotspot, false); return false; }
        var first = _egressPublic;
        if (!await Ask("Change IP on the iPhone",
                "1. On the iPhone, turn Airplane Mode on.\n2. Wait 3 seconds.\n3. Turn Airplane Mode off.\n\n"
                + (_last.Egress.IsWifi ? "Keep Personal Hotspot on. " : "Keep the cable plugged in. ") + "The app waits for the iPhone to come back (up to 90 s) and shows the new IP. "
                + (IpChangeDrops ? "If you are on a server, you will drop." : "Servers see the new IP once protection is on."), "I'm doing it", "Cancel"))
            return false;
        using var wait = CommandWait();
        var sw = Stopwatch.StartNew();
        var sawDrop = false;
        try
        {
            while (true)
            {
                if (CommandCancelled) return false;
                await SyncNow(force: true);
                var connected = _last?.Egress != null;
                sawDrop |= !connected;
                string now = null;
                if (connected && sawDrop) { await RefreshPublicIps(); now = _egressFailSince == null ? _egressPublic : null; }
                var step = IphoneChangeStep(sawDrop, connected, first, now, sw.Elapsed, IphoneWait);
                switch (step)
                {
                    case IphoneChange.BackNew:
                        _egressInfo = await PhoneIpInfo(now);
                        AddHistory(now, _egressInfo, "trocado");
                        ResetQuality();
                        Log.Ok($"iPhone: IP changed: {first ?? "?"} → {now}.");
                        Toast($"New IP: {now}.", true);
                        return true;
                    case IphoneChange.BackSame:
                        Log.Warn($"iPhone: back with the same IP ({now}).");
                        Toast("The carrier gave the same IP. Try again and leave Airplane Mode on a bit longer (10 to 20 s).", false);
                        return false;
                    case IphoneChange.TimedOut:
                        Log.Warn("iPhone: Change IP waited 90 s " + (sawDrop ? "for the iPhone to come back." : "without seeing Airplane Mode."));
                        Toast(sawDrop ? "The iPhone hasn't come back yet. Check Personal Hotspot is on and the cable is in." : "The iPhone's connection never dropped: turn Airplane Mode on, then off.", false);
                        return false;
                }
                Step(step == IphoneChange.Off ? "Airplane Mode on: now turn it off on the iPhone…"
                    : step == IphoneChange.Measuring ? "the iPhone is back: checking the new IP…" : "turn Airplane Mode on, on the iPhone…");
                await Task.Delay(1500);
            }
        }
        finally { await SyncNow(force: true); }
    }
}
