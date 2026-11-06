using System.Windows;
using SRT.Core;

namespace SRT;

// What the stats need from the window (README > Telemetry): the mode, how the phone is connected, list sizes, the screen open,
// and for the live signal whether CS2 is open, protection is on and the window is in the tray. Nothing else.
public partial class MainWindow
{
    (string, string, Dictionary<string, int>) TelemetryNow()
    {
        var mode = _s == null ? null : _s.Mode == "all" ? "always" : _s.NoPhone ? "nophone" : _s.GameMode ? "cs2open" : "game";
        var link = TelemetryLink(_last?.Egress);
        var sizes = new Dictionary<string, int>
        {
            ["servers"] = _servers.Count,
            ["accounts"] = _accounts.Count,
            ["flags"] = _flags?.Count ?? 0,
            ["open_with"] = _openWith?.Count ?? 0,
            ["open_with_admin"] = _openWith?.Count(x => x.AsAdmin) ?? 0,
            ["regions_blocked"] = _s?.BlockedRegions?.Count ?? 0,
        };
        return (mode, link, sizes);
    }

    /// <summary>How the phone is connected, in the stats' words.</summary>
    static string TelemetryLink(Adapter eg) => eg == null ? "none" : eg.IsPhone ? "usb" : eg.IsWifi ? "hotspot" : "other";

    /// <summary>The screen's id in the stats, the same as the dashboard's ui-map.js.</summary>
    string TelemetryScreen() =>
        NavNet.IsChecked == true ? "connection" : NavServers.IsChecked == true ? "servers" : NavRegions.IsChecked == true ? "regions"
        : NavAccounts.IsChecked == true ? "accounts" : NavConfigs.IsChecked == true ? "configs" : NavProfiles.IsChecked == true ? "profiles"
        : NavLog.IsChecked == true ? "log" : NavSettings.IsChecked == true ? "settings" : null;

    (string, bool, bool, bool) TelemetryLive() =>
        (TelemetryScreen(), Steam.Cs2Running, _heroWanted.Item1 == "good", !IsVisible || WindowState == System.Windows.WindowState.Minimized);

    // The phone's connection going away while it was up (not during Change IP, which turns the data off on purpose). Counted for
    // the stats; on the USB cable, 3 drops within 10 minutes are most likely the cable or the port, and the app says so once.
    bool _egressUp, _egressUsb, _cableHinted;
    readonly List<DateTime> _drops = new();

    void TrackPhoneDrop(GuardReport rep)
    {
        // Each guard report: the stats keep the best link seen since their last report (audit D3: the report 20 s after the start
        // came before the phone was seen and always said "none")
        Telemetry.SeenLink(TelemetryLink(rep.Egress));
        var up = rep.Egress != null;
        if (IsDrop(_egressUp, up, PhoneLink.Exit(rep) != null, _changingIp || _ipChangeQuiet) && !_s.NoPhone)
        {
            Telemetry.PhoneDrop();
            var now = DateTime.Now;
            _drops.RemoveAll(t => now - t > TimeSpan.FromMinutes(10));
            _drops.Add(now);
            if (_egressUsb && CableHint(_drops, now) && !_cableHinted)
            {
                _cableHinted = true;
                Telemetry.Count("cable_hint");
                Log.Warn("The phone's USB connection dropped 3 times in 10 minutes. That is usually the cable or the USB port.");
                Toast("The phone's USB connection keeps dropping (3 times in 10 minutes). Try another cable (one made for data, not only charging) or a USB port at the back of the PC.", Tone.Warn);
            }
        }
        if (up) _egressUsb = rep.Egress.IsPhone && !rep.Egress.IsWifi;
        _egressUp = up;
    }

    /// <summary>
    /// The phone's connection dropped: it was protected and its adapter is gone now. Not a drop: a connection that is still there but
    /// no longer trusted (the phone's Wi-Fi came on, same IP as home), nor an IP change (Airplane Mode on the iPhone, data off on Android).
    /// </summary>
    public static bool IsDrop(bool wasUp, bool upNow, bool exitNow, bool changingIp) => wasUp && !upNow && !exitNow && !changingIp;

    /// <summary>3 drops of the phone's connection within 10 minutes.</summary>
    public static bool CableHint(IEnumerable<DateTime> drops, DateTime now) => drops.Count(t => now - t <= TimeSpan.FromMinutes(10)) >= 3;

    /// <summary>Settings > Delete my stats: the server deletes what it keeps under this install's ID, and the app takes a new one.</summary>
    async void DeleteStats_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast("Demo: delete my stats.", true); return; }
        if (!await Ask("Delete my stats", "Delete everything the stats server keeps under this install's ID? The app then starts over with a new ID. The stats stay on (they are required in the beta).", "Delete")) return;
        if (await Telemetry.Forget()) { Log.Ok("Usage stats deleted on the server. New stats ID made."); Toast("Your stats were deleted. The app has a new stats ID.", true); }
        else Toast("Couldn't reach the stats server. Nothing was deleted; try again later.", Tone.Error);
    }

    /// <summary>After Telemetry.Start: the screen open now, and the tray (no screen) when the window hides.</summary>
    void TelemetryHook()
    {
        Telemetry.LiveFrom(TelemetryLive);
        // beta.9 part 2: the start (with Windows or by hand), the settings changed from their factory value, the window for the
        // environment report (its width and the screen's scale, both in bands there), and the time until the window is ready
        Telemetry.Started(_startHidden);
        Telemetry.SettingsFrom(() => Telemetry.Changed(_s, new Settings()));
        Telemetry.RegionsFrom(() => Telemetry.RegionCodes(_regionInfos, _s?.BlockedRegions));
        Telemetry.ChecksFrom(() => _checks.Where(c => c.Ok == false).Select(c => c.Title).ToList());
        Telemetry.AtExit(CountSetupClosed);   // beta.11: the app closed with Phone setup open (its step there is where they stopped)
        Telemetry.WindowFrom(() => (ActualWidth, System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX));
        Loaded += (_, _) => Telemetry.Ready();
        Activated += (_, _) => Telemetry.Activated();
        Telemetry.Screen(TelemetryScreen());
        IsVisibleChanged += (_, _) => Telemetry.Screen(IsVisible ? TelemetryScreen() : null);
    }
}
