using System.IO.Compression;
using System.Runtime.InteropServices;
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
            Path.Combine(Paths.Data, "platform-tools", "adb.exe"),
            Path.Combine(Paths.AppDir, "platform-tools", "adb.exe"),
            Path.Combine(Paths.AppDir, "adb.exe"),
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
        // The app runs as admin: only an adb.exe signed by Google is ever started.
        foreach (var c in candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsGoogleSigned(c)) return c;
            if (Skipped.Add(c)) Log.Warn($"Ignored {c}: it is not signed by Google.");
        }
        return null;
    }

    static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Valid Authenticode signature (WinVerifyTrust) whose signer is Google LLC.</summary>
    public static bool IsGoogleSigned(string path)
    {
        try
        {
            if (!Trust.Verify(path)) return false;
#pragma warning disable SYSLIB0057
            var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return System.Text.RegularExpressions.Regex.IsMatch(cert.Subject, @"(^|,\s*)O=Google LLC(,|$)");
        }
        catch { return false; }
    }

    static class Trust
    {
        static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        struct FileInfo { public uint cbStruct; [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

        [StructLayout(LayoutKind.Sequential)]
        struct Data
        {
            public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice;
            public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction;
            public IntPtr hWVTStateData; public IntPtr pwszURLReference; public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref Data data);

        public static bool Verify(string path)
        {
            var file = new FileInfo { cbStruct = (uint)Marshal.SizeOf<FileInfo>(), pcwszFilePath = path };
            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                // No UI, no revocation check and no network (works offline), a file.
                var data = new Data { cbStruct = (uint)Marshal.SizeOf<Data>(), dwUIChoice = 2, dwUnionChoice = 1, pFile = pFile, dwProvFlags = 0x10 | 0x1000 };
                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<FileInfo>(pFile);
                Marshal.FreeHGlobal(pFile);
            }
        }
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

    /// <summary>The phone says USB tethering (rndis) is on.</summary>
    public static async Task<bool> TetheringOn(string adb, string serial)
    {
        var r = await Run(adb, $"-s {serial} shell getprop sys.usb.state", 6000);
        return r.Code == 0 && r.Output.Contains("rndis");
    }

    public static async Task<PhoneInfo> Status(string adb)
    {
        var r = await Run(adb, "devices", 15000);
        if (r.Code == -1) return null;
        var line = r.Output.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains('\t') && !l.StartsWith("List of"));
        if (line == null) return new PhoneInfo(null, "none", null, null, null, null);
        var parts = line.Split('\t');
        var serial = parts[0];
        // The serial comes from the device and goes into command lines: letters, digits and . : - _ only.
        if (!System.Text.RegularExpressions.Regex.IsMatch(serial, @"^[\w.:-]+$")) return new PhoneInfo(null, "none", null, null, null, null);
        var state = parts[1].Trim();
        if (state != "device") return new PhoneInfo(serial, state, null, null, null, null);

        // One shell for everything (it runs every 2 s during a match): model, carrier, network, battery | Wi-Fi on, Wi-Fi network.
        var q = await Run(adb, $"-s {serial} shell \"getprop ro.product.model; getprop gsm.operator.alpha; getprop gsm.network.type; dumpsys battery | grep -m1 level; " +
            "echo ---; settings get global wifi_on; cmd wifi status 2>/dev/null | grep -m1 -i 'connected to'\"");
        var all = q.Output.Replace("\r", "").Split('\n');
        var cut = Array.FindIndex(all, l => l.Trim() == "---");
        var lines = (cut >= 0 ? all[..cut] : all).Select(l => l.Trim()).ToArray();
        var wifiPart = cut >= 0 ? string.Join("\n", all[(cut + 1)..]) : "";
        string L(int i) => i < lines.Length ? lines[i] : null;
        int? battery = null;
        var lv = L(3);
        if (lv != null && int.TryParse(lv.Replace("level:", "").Trim(), out var b)) battery = b;
        var carrier = L(1)?.Split(',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        var net = L(2)?.Split(',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "Unknown");
        // Phone Wi-Fi on = tethering shares the home network instead of mobile data.
        var wl = wifiPart.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var wifiOn = wl.Length > 0 && wl[0] != "0" && wl[0] != "null";
        string ssid = null;
        var m = System.Text.RegularExpressions.Regex.Match(wifiPart, "connected to \"?([^\"\r\n]+)\"?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
