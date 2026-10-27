using System.IO.Compression;
using System.Net.Http;

namespace SRT.Core;

public sealed record PhoneInfo(string Serial, string State, string Model, string Carrier, string NetType, int? Battery, bool WifiOn = false, string WifiSsid = null)
{
    public bool Ready => State == "device";

    public string Summary => State switch
    {
        "device" => string.Join(" · ", new[] { Model, Carrier, NetType, Battery is int b ? $"{b}%" : null }
                        .Where(s => !string.IsNullOrWhiteSpace(s))),
        "unauthorized" => "Accept the \"Allow USB debugging\" prompt on your phone screen",
        "offline" => "Phone offline in ADB: unplug and reconnect the cable",
        _ => State,
    };
}

/// <summary>Controls the Android phone through ADB (USB debugging) to renew the mobile data IP.</summary>
public static class Adb
{
    public const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    public static string Find()
    {
        var candidates = new List<string>
        {
            Path.Combine(Paths.AppDir, "platform-tools", "adb.exe"),
            Path.Combine(Paths.AppDir, "adb.exe"),
            Path.Combine(Paths.Data, "platform-tools", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
        };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim(), "adb.exe"));
        try
        {
            var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(winget))
                foreach (var d in Directory.GetDirectories(winget, "Google.PlatformTools*"))
                    candidates.Add(Path.Combine(d, "platform-tools", "adb.exe"));
        }
        catch { }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Downloads Google's official platform-tools into the app folder (~7 MB).</summary>
    public static async Task<string> Download(IProgress<string> progress)
    {
        var zip = Path.Combine(Paths.Data, "platform-tools.zip");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        {
            progress?.Report("Downloading ADB from Google…");
            var bytes = await http.GetByteArrayAsync(PlatformToolsUrl);
            await File.WriteAllBytesAsync(zip, bytes);
        }
        progress?.Report("Extracting…");
        await Task.Run(() => ZipFile.ExtractToDirectory(zip, Paths.Data, true));
        try { File.Delete(zip); } catch { }
        return Find();
    }

    static Task<ProcResult> Run(string adb, string args, int timeout = 12000) => Proc.Run(adb, args, timeout);

    public static async Task<PhoneInfo> Status(string adb)
    {
        var r = await Run(adb, "devices", 15000);
        if (r.Code == -1) return null;
        var line = r.Output.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains('\t') && !l.StartsWith("List of"));
        if (line == null) return new PhoneInfo(null, "none", null, null, null, null);
        var parts = line.Split('\t');
        var serial = parts[0];
        var state = parts[1].Trim();
        if (state != "device") return new PhoneInfo(serial, state, null, null, null, null);

        var q = await Run(adb, $"-s {serial} shell \"getprop ro.product.model; getprop gsm.operator.alpha; getprop gsm.network.type; dumpsys battery | grep -m1 level\"");
        var lines = q.Output.Split('\n').Select(l => l.Trim()).ToArray();
        string L(int i) => i < lines.Length ? lines[i] : null;
        int? battery = null;
        var lv = L(3);
        if (lv != null && int.TryParse(lv.Replace("level:", "").Trim(), out var b)) battery = b;
        var carrier = L(1)?.Split(',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        var net = L(2)?.Split(',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "Unknown");
        // Phone Wi-Fi on = tethering shares the home network instead of mobile data.
        var w = await Run(adb, $"-s {serial} shell \"settings get global wifi_on; cmd wifi status 2>/dev/null | grep -m1 -i 'connected to'\"");
        var wl = w.Output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var wifiOn = wl.Length > 0 && wl[0] != "0" && wl[0] != "null";
        string ssid = null;
        var m = System.Text.RegularExpressions.Regex.Match(w.Output, "connected to \"?([^\"\r\n]+)\"?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) ssid = m.Groups[1].Value.Trim();
        return new PhoneInfo(serial, state, L(0), carrier, PrettyNet(net), battery, wifiOn, ssid);
    }

    static string PrettyNet(string t) => t?.ToUpperInvariant() switch
    {
        null or "" => null,
        "NR" or "NR_SA" or "NR_NSA" => "5G",
        "LTE" or "LTE_CA" => "4G",
        "HSPA" or "HSPAP" or "HSDPA" or "HSUPA" or "UMTS" => "3G",
        "EDGE" or "GPRS" => "2G",
        var x => x,
    };

    public enum Method { MobileData, Airplane }

    /// <summary>Turns the phone's data connection off and on again. USB tethering stays on.</summary>
    public static async Task<string> Cycle(string adb, string serial, Method method, int offSeconds)
    {
        string off, on;
        if (method == Method.Airplane)
        {
            off = "cmd connectivity airplane-mode enable";
            on = "cmd connectivity airplane-mode disable";
        }
        else
        {
            off = "svc data disable";
            on = "svc data enable";
        }
        var r1 = await Run(adb, $"-s {serial} shell {off}");
        if (!r1.Ok || r1.Output.Contains("Exception") || r1.Output.Contains("not found"))
        {
            // On some Android versions "svc data" is not available: try airplane mode.
            if (method == Method.MobileData) return await Cycle(adb, serial, Method.Airplane, offSeconds);
            return "The phone refused the command: " + r1.Output;
        }
        await Task.Delay(TimeSpan.FromSeconds(offSeconds));
        var r2 = await Run(adb, $"-s {serial} shell {on}");
        if (!r2.Ok) return "Turned data off but could not turn it back on: " + r2.Output + " (turn it on manually on the phone)";
        return null;
    }

    /// <summary>Turns off the phone's Wi-Fi so tethering uses mobile data.</summary>
    public static Task<ProcResult> DisableWifi(string adb, string serial) =>
        Run(adb, $"-s {serial} shell svc wifi disable");

    /// <summary>Asks Android to turn on USB tethering (may not work on every model).</summary>
    public static Task<ProcResult> EnableUsbTether(string adb, string serial) =>
        Run(adb, $"-s {serial} shell svc usb setFunctions rndis");
}
