using SRT.Core;

namespace SRT;

// What the stats need from the window (README > Telemetry): the mode, how the phone is connected, list sizes, the screen open,
// and for the live signal whether CS2 is open, protection is on and the window is in the tray. Nothing else.
public partial class MainWindow
{
    (string, string, Dictionary<string, int>) TelemetryNow()
    {
        var mode = _s == null ? null : _s.Mode == "all" ? "always" : _s.NoPhone ? "nophone" : _s.GameMode ? "cs2open" : "game";
        var eg = _last?.Egress;
        var link = eg == null ? "none" : eg.IsPhone ? "usb" : eg.IsWifi ? "hotspot" : "other";
        var sizes = new Dictionary<string, int>
        {
            ["servers"] = _servers.Count,
            ["accounts"] = _accounts.Count,
            ["flags"] = _flags?.Count ?? 0,
            ["open_with"] = _openWith?.Count ?? 0,
            ["regions_blocked"] = _s?.BlockedRegions?.Count ?? 0,
        };
        return (mode, link, sizes);
    }

    /// <summary>The screen's id in the stats, the same as the dashboard's ui-map.js.</summary>
    string TelemetryScreen() =>
        NavNet.IsChecked == true ? "connection" : NavServers.IsChecked == true ? "servers" : NavRegions.IsChecked == true ? "regions"
        : NavAccounts.IsChecked == true ? "accounts" : NavConfigs.IsChecked == true ? "configs" : NavProfiles.IsChecked == true ? "profiles"
        : NavLog.IsChecked == true ? "log" : NavSettings.IsChecked == true ? "settings" : null;

    (string, bool, bool, bool) TelemetryLive() =>
        (TelemetryScreen(), Steam.Cs2Running, _heroWanted.Item1 == "good", !IsVisible || WindowState == System.Windows.WindowState.Minimized);

    /// <summary>After Telemetry.Start: the screen open now, and the tray (no screen) when the window hides.</summary>
    void TelemetryHook()
    {
        Telemetry.LiveFrom(TelemetryLive);
        Telemetry.Screen(TelemetryScreen());
        IsVisibleChanged += (_, _) => Telemetry.Screen(IsVisible ? TelemetryScreen() : null);
    }
}
