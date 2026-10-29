using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SRT.Core;

/// <param name="WifiConnected">The phone's Wi-Fi is on a network (true), on but on none (false), or the phone doesn't say (null, Android 10 and older).</param>
/// <param name="WifiUnknown">The Wi-Fi reading didn't come back as a value (an adb error, a timeout, an empty answer): WifiOn says nothing.</param>
public sealed record PhoneInfo(string Serial, string State, string Model, string Carrier, string NetType, int? Battery, bool WifiOn = false, string WifiSsid = null,
    bool? WifiConnected = null, bool WifiUnknown = false)
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
        // The app runs as admin: only an adb.exe signed by Google is ever started, and only from a copy in a folder only admins can change.
        foreach (var c in candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsGoogleSigned(c))
            {
                try { return Secure(c); }
                catch (Exception ex) { Log.Warn($"ADB found in {Path.GetDirectoryName(c)} but it couldn't be copied to a safe folder ({ex.Message}). Restart the app; if it repeats, save a bug report (Log page)."); return null; }
            }
            if (Skipped.Add(c)) Log.Warn($"Ignored {c}: it is not signed by Google.");
        }
        return null;
    }

    // ------------------------------------------------------------ The copy that runs

    static readonly string[] AdbFiles = { "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll" };

    /// <summary>
    /// adb.exe loads its two DLLs from its own folder, and the folders it comes from (Downloads, SRT_Dados, AppData, PATH) are yours to change.
    /// Started from there by an app with admin rights, a program of yours could swap a DLL and run as admin. So the three files are copied to
    /// %ProgramData%\CS2IPChanger\adb, which only admins can change, all three are checked for Google's signature there, and only that copy runs.
    /// Without admin rights (test build) there is nothing to gain and the file found is used as it is.
    /// </summary>
    static string Secure(string adb)
    {
        if (!Elevated) return adb;
        var from = Path.GetDirectoryName(adb)!;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger");
        var dir = Path.Combine(root, "adb");
        SafeDir(root);
        SafeDir(dir);
        foreach (var name in AdbFiles)
        {
            var src = Path.Combine(from, name);
            var dst = Path.Combine(dir, name);
            if (!File.Exists(src)) continue;
            if (File.Exists(dst) && Same(src, dst)) continue;
            var tmp = dst + ".new";
            File.Copy(src, tmp, true);
            try { File.Move(tmp, dst, true); }
            catch (IOException) when (File.Exists(dst)) { File.Delete(tmp); }   // the copy in use (adb server running) stays; it is checked below
        }
        var bad = AdbFiles.Select(n => Path.Combine(dir, n)).Where(f => File.Exists(f) && !IsGoogleSigned(f)).ToList();
        if (bad.Count > 0 || !File.Exists(Path.Combine(dir, "adb.exe")))
            throw new InvalidDataException(bad.Count > 0 ? "not signed by Google: " + string.Join(", ", bad.Select(Path.GetFileName)) : "adb.exe missing");
        return Path.Combine(dir, "adb.exe");
    }

    internal static bool Elevated
    {
        get { try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
    }

    static bool Same(string a, string b)
    {
        if (new System.IO.FileInfo(a).Length != new System.IO.FileInfo(b).Length) return false;
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        return System.Security.Cryptography.SHA256.HashData(sa).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(sb));
    }

    static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");
    const FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>
    /// Creates the folder for admins only (Users can read and run). Anyone can create folders in %ProgramData%, so one that already
    /// exists and isn't like that (made by someone else first, a link, a file with that name) is moved aside and made again.
    /// </summary>
    public static void SafeDir(string d)
    {
        if (File.Exists(d) || (Directory.Exists(d) && !AdminOnly(d)))
        {
            var aside = $"{d}.untrusted-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (File.Exists(d)) File.Move(d, aside); else Directory.Move(d, aside);
            Log.Warn($"{d} could be changed by programs without admin rights: moved to {aside} and made again.");
        }
        if (Directory.Exists(d)) return;
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(true, false);
        sec.SetOwner(Admins);
        const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        // Whoever owns a file can change its permissions; this takes that away (files copied in keep only read and run for their owner).
        sec.AddAccessRule(new FileSystemAccessRule(OwnerRights, FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(d).Create(sec);
    }

    /// <summary>Owned by Administrators or SYSTEM, not a link, and nobody else can write, delete or change permissions.</summary>
    public static bool AdminOnly(string d)
    {
        try
        {
            var info = new DirectoryInfo(d);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            var sec = info.GetAccessControl();
            var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner != Admins && owner != SystemSid) return false;
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (r.AccessControlType != AccessControlType.Allow || (r.FileSystemRights & WriteRights) == 0) continue;
                var who = r.IdentityReference as SecurityIdentifier;
                if (who != Admins && who != SystemSid) return false;
            }
            return true;
        }
        catch { return false; }
    }

    static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
    static readonly SecurityIdentifier[] Anyone =
    {
        Users, new(WellKnownSidType.AuthenticatedUserSid, null), new(WellKnownSidType.WorldSid, null), new(WellKnownSidType.InteractiveSid, null),
    };

    /// <summary>
    /// Programs without admin rights can change the folder: a link, owned by someone other than Administrators, SYSTEM or TrustedInstaller
    /// (the owner can rewrite the permissions), or write rights for Users, Authenticated Users, Everyone, INTERACTIVE or you.
    /// C:\Program Files\CS2 IP Changer passes (its TrustedInstaller and CREATOR OWNER entries don't count).
    /// </summary>
    public static bool UsersCanWrite(string d)
    {
        try
        {
            var info = new DirectoryInfo(d);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
            var sec = info.GetAccessControl();
            var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner != Admins && owner != SystemSid && owner != TrustedInstaller) return true;
            using var me = WindowsIdentity.GetCurrent();
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (r.AccessControlType != AccessControlType.Allow || (r.FileSystemRights & WriteRights) == 0 || r.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;
                var who = r.IdentityReference as SecurityIdentifier;
                if (Anyone.Contains(who) || who == me.User) return true;
            }
            return false;
        }
        catch { return true; }
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
            "echo ---; settings get global wifi_on; cmd wifi status 2>/dev/null | grep -m1 -i 'connected'\"");
        var all = q.Output.Replace("\r", "").Split('\n');
        var cut = Array.FindIndex(all, l => l.Trim() == "---");
        var lines = (cut >= 0 ? all[..cut] : all).Select(l => l.Trim()).ToArray();
        string L(int i) => i < lines.Length ? lines[i] : null;
        int? battery = null;
        var lv = L(3);
        if (lv != null && int.TryParse(lv.Replace("level:", "").Trim(), out var b)) battery = b;
        var carrier = L(1)?.Split(',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        var net = L(2)?.Split(',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "Unknown");
        // Not q.Ok: with Wi-Fi off the last grep finds nothing and the shell exits 1, though the answer is right (seen on a Samsung, 29 Oct).
        // An adb error has no "---", so ParseWifi still gives unknown for it.
        var (wifiOn, ssid, connected) = ParseWifi(q.Output);
        return new PhoneInfo(serial, state, L(0), carrier, PrettyNet(net), battery, wifiOn == true, ssid, wifiOn == true ? connected : false, wifiOn == null);
    }

    /// <summary>
    /// The Wi-Fi part of the status shell (after "---"): "settings get global wifi_on", then the "connected" line of "cmd wifi status".
    /// On only for 1 or 2 (Settings.Global.WIFI_ON: on, on in airplane mode), off for 0 or 3. Anything else (an adb error, a timeout,
    /// an empty answer that leaves the next line first, no "---") is unknown: null, and the caller keeps what it had.
    /// </summary>
    public static (bool? On, string Ssid, bool? Connected) ParseWifi(string output)
    {
        if (output == null) return (null, null, null);
        var all = output.Replace("\r", "").Split('\n');
        var cut = Array.FindIndex(all, l => l.Trim() == "---");
        if (cut < 0 || cut + 1 >= all.Length) return (null, null, null);
        var first = all[cut + 1].Trim();
        bool? on = first switch { "1" or "2" => true, "0" or "3" => false, _ => null };
        if (on != true) return (on, null, on == false ? false : null);
        var rest = string.Join("\n", all[(cut + 2)..]);
        var m = System.Text.RegularExpressions.Regex.Match(rest, "(?<!not )connected to \"?([^\"\r\n]+)\"?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // "Wifi is not connected": the radio is on (Samsung turns it on by itself to scan), but tethering is still on mobile data.
        bool? connected = m.Success ? true : rest.Contains("not connected", StringComparison.OrdinalIgnoreCase) ? false : null;
        return (true, m.Success ? m.Groups[1].Value.Trim() : null, connected);
    }

    /// <summary>
    /// The Wi-Fi state the app acts on: an unknown reading keeps the one before, and "on" only counts on the second reading in a row
    /// (one stray answer must not block servers and CS2; the home IP check is still the third barrier). Off counts at once.
    /// </summary>
    public static PhoneInfo SteadyWifi(PhoneInfo now, PhoneInfo before, ref int onReads)
    {
        if (now?.Ready != true) { onReads = 0; return now; }
        var wasOn = before?.Ready == true && before.WifiOn;
        if (now.WifiUnknown)
            return before?.Ready == true ? now with { WifiOn = before.WifiOn, WifiSsid = before.WifiSsid, WifiConnected = before.WifiConnected } : now;
        if (!now.WifiOn) { onReads = 0; return now; }
        onReads++;
        return wasOn || onReads >= 2 ? now : now with { WifiOn = false, WifiSsid = null, WifiConnected = false };
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
        // Turning data or airplane mode off can drop USB tethering, and ADB with it, for a few seconds: keep trying for about 20 s.
        var r2 = await Run(adb, $"-s {serial} shell {on}");
        for (var i = 0; i < 4 && !r2.Ok; i++)
        {
            await Task.Delay(5000);
            r2 = await Run(adb, $"-s {serial} shell {on}");
        }
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
