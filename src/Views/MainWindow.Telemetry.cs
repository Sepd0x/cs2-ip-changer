using SRT.Core;

namespace SRT;

// What the daily stats need from the window (README > Telemetry): the mode, how the phone is connected and list sizes. Nothing else.
public partial class MainWindow
{
    (string, string, Dictionary<string, int>) TelemetryNow()
    {
        var mode = _s == null ? null : _s.Mode == "all" ? "always" : _s.GameMode ? "cs2open" : "game";
        var eg = _last?.Egress;
        var link = eg == null ? "none" : eg.IsPhone ? "usb" : eg.IsWifi ? "hotspot" : "other";
        var sizes = new Dictionary<string, int>
        {
            ["servers"] = _servers.Count,
            ["accounts"] = _accounts.Count,
            ["flags"] = _flags?.Count ?? 0,
            ["open_with"] = _openWith?.Count ?? 0,
        };
        return (mode, link, sizes);
    }
}
