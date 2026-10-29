using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>One USB device Windows knows about, as the PnP manager reports it (instance ID, name, problem code).</summary>
public sealed record UsbDevice(string InstanceId, string Name, int? Problem);

/// <summary>What Windows sees of an Android phone on USB: nothing, a working device, or one whose driver failed.</summary>
public sealed record PhoneUsbState(bool Seen, int? Problem, string Vendor)
{
    public static readonly PhoneUsbState None = new(false, null, null);
}

/// <summary>Reads the USB devices from the PnP manager (cfgmgr32, the same data Device Manager shows), no extra packages.</summary>
public static class PhoneUsb
{
    // Vendors that make (almost) only phones and phone modems (USB-IF IDs): any device of theirs counts.
    static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["18D1"] = "Google", ["22B8"] = "Motorola", ["2717"] = "Xiaomi", ["12D1"] = "Huawei", ["2A70"] = "OnePlus", ["0FCE"] = "Sony",
        ["19D2"] = "ZTE", ["2D95"] = "vivo", ["22D9"] = "OPPO", ["2B0E"] = "Nothing", ["2AE5"] = "Fairphone",
    };

    // Vendors that also make keyboards, docks, monitors, SSDs and motherboards: only a phone-like name counts.
    static readonly Dictionary<string, string> BroadVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["04E8"] = "Samsung", ["0BB4"] = "HTC", ["1004"] = "LG", ["0B05"] = "Asus", ["17EF"] = "Lenovo",
    };

    static readonly Regex VidPid = new(@"^USB\\VID_([0-9A-F]{4})&PID_[0-9A-F]{4}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PhoneName = new("Android|ADB Interface|Mobile USB|Mobile Phone|Remote NDIS|RNDIS|Galaxy|Pixel|\\bMTP\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The phone among the USB devices: a known phone vendor or a phone-like name. A device with a problem code (10 = it didn't start,
    /// 28 = no driver, 43 = it stopped responding) wins over a working one, so a failed composite device isn't hidden by a child.
    /// </summary>
    public static PhoneUsbState Summarize(IEnumerable<UsbDevice> devices)
    {
        var phones = new List<(UsbDevice D, string Vendor)>();
        foreach (var d in devices ?? Enumerable.Empty<UsbDevice>())
        {
            var m = VidPid.Match(d.InstanceId ?? "");
            if (!m.Success) continue;
            var vid = m.Groups[1].Value;
            var named = PhoneName.IsMatch(d.Name ?? "");
            var vendor = Vendors.GetValueOrDefault(vid) ?? (named ? BroadVendors.GetValueOrDefault(vid) : null);
            if (vendor == null && !named) continue;
            phones.Add((d, vendor ?? "Android"));
        }
        if (phones.Count == 0) return PhoneUsbState.None;
        var bad = phones.FirstOrDefault(p => p.D.Problem is > 0);
        return bad.D != null ? new PhoneUsbState(true, bad.D.Problem, bad.Vendor) : new PhoneUsbState(true, null, phones[0].Vendor);
    }

    public static PhoneUsbState Scan()
    {
        try { return Summarize(Read()); }
        catch { return PhoneUsbState.None; }
    }

    const int CR_SUCCESS = 0, CM_GETIDLIST_FILTER_ENUMERATOR = 0x1, CM_GETIDLIST_FILTER_PRESENT = 0x100, CM_DRP_DEVICEDESC = 1, CM_DRP_FRIENDLYNAME = 0x0D;
    const uint DN_HAS_PROBLEM = 0x400;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List_SizeW(out int len, string filter, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, int len, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint devInst, string id, int flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, int prop, out int type, byte[] buffer, ref int len, int flags);

    /// <summary>Every USB device that is plugged in now, with its problem code (null = working).</summary>
    static List<UsbDevice> Read()
    {
        const int flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;
        var list = new List<UsbDevice>();
        if (CM_Get_Device_ID_List_SizeW(out var len, "USB", flags) != CR_SUCCESS || len <= 1) return list;
        var buf = new char[len];
        if (CM_Get_Device_ID_ListW("USB", buf, len, flags) != CR_SUCCESS) return list;
        foreach (var id in new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CM_Locate_DevNodeW(out var dev, id, 0) != CR_SUCCESS) continue;
            int? problem = null;
            if (CM_Get_DevNode_Status(out var status, out var code, dev, 0) == CR_SUCCESS && (status & DN_HAS_PROBLEM) != 0) problem = (int)code;
            list.Add(new UsbDevice(id, Property(dev, CM_DRP_FRIENDLYNAME) ?? Property(dev, CM_DRP_DEVICEDESC), problem));
        }
        return list;
    }

    static string Property(uint dev, int prop)
    {
        var b = new byte[1024];
        var n = b.Length;
        if (CM_Get_DevNode_Registry_PropertyW(dev, prop, out _, b, ref n, 0) != CR_SUCCESS || n < 2) return null;
        var s = System.Text.Encoding.Unicode.GetString(b, 0, n).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }
}

/// <summary>Where the phone setup is: what it found, in the order the steps go.</summary>
public sealed record PhoneSetupState
{
    /// <summary>adb.exe is installed (and signed by Google).</summary>
    public bool AdbTool { get; init; }
    /// <summary>"device", "unauthorized", "offline", "none" (no phone in ADB), or null when ADB didn't start.</summary>
    public string AdbState { get; init; }
    public PhoneUsbState Usb { get; init; } = PhoneUsbState.None;
    /// <summary>The PC uses the phone's Wi-Fi hotspot (picked under Phone): no USB tethering needed, the phone's Wi-Fi stays on.</summary>
    public bool Hotspot { get; init; }
    /// <summary>Windows has the phone's network adapter up (USB tethering works).</summary>
    public bool Tethered { get; init; }
    /// <summary>The phone's Wi-Fi is on a network (or Android doesn't say).</summary>
    public bool WifiOn { get; init; }
    /// <summary>Windows sends the rest of the PC through the phone while the mode isn't Whole PC.</summary>
    public bool PcOnPhone { get; init; }
}

public enum PhoneStep { NoAdbTool, AdbFailed, NoUsb, UsbDriverError, NoUsbDebugging, Unauthorized, Offline, TetherOff, WifiOn, PcOnPhone, Done }

/// <summary>The guided phone setup: the next step from what was found, and the one thing to tell you when the app can't do it.</summary>
public static class PhoneSetup
{
    /// <summary>
    /// The next thing to do. ADB problems come first, unless tethering already works without ADB (then only Change IP needs it,
    /// and the rest of the checks still run before the ADB step is shown).
    /// </summary>
    public static PhoneStep Next(PhoneSetupState s)
    {
        var adb = s.AdbState == "device";
        var connected = s.Tethered || s.Hotspot;
        if (!adb && !connected) return AdbStep(s);
        if (adb && !connected) return PhoneStep.TetherOff;
        if (adb && s.WifiOn && !s.Hotspot) return PhoneStep.WifiOn;
        if (s.PcOnPhone) return PhoneStep.PcOnPhone;
        return adb ? PhoneStep.Done : AdbStep(s);
    }

    static PhoneStep AdbStep(PhoneSetupState s)
    {
        if (!s.AdbTool) return PhoneStep.NoAdbTool;
        return s.AdbState switch
        {
            null => PhoneStep.AdbFailed,
            "unauthorized" => PhoneStep.Unauthorized,
            "none" when s.Usb.Problem != null => PhoneStep.UsbDriverError,
            // With tethering up the phone is plugged in even when its vendor isn't in the table.
            "none" when !s.Usb.Seen && !s.Tethered => PhoneStep.NoUsb,
            "none" => PhoneStep.NoUsbDebugging,
            _ => PhoneStep.Offline,
        };
    }

    /// <summary>Steps the app does itself (after asking, when it changes the phone). The rest is one thing for you to do.</summary>
    public static bool AppDoes(PhoneStep step) => step is PhoneStep.NoAdbTool or PhoneStep.Unauthorized or PhoneStep.TetherOff or PhoneStep.WifiOn or PhoneStep.PcOnPhone;

    public const string UsbDebuggingSteps =
        "1. On the phone, open Settings > About phone and tap \"Build number\" 7 times (Samsung: About phone > Software information).\n" +
        "2. Open Developer options (Samsung: Settings > Developer options) and turn on \"USB debugging\".\n" +
        "3. Unplug the cable and plug it in again, then press Phone setup.";

    /// <summary>What you have to do when the app can't do the step itself. <paramref name="samsung"/>: the phone is a Samsung.</summary>
    public static string Advice(PhoneStep step, int? problem = null, bool samsung = false) => step switch
    {
        PhoneStep.NoAdbTool => "Install the phone tool (ADB): press Install phone tool (ADB) under Phone. It's free, from Google, about 7 MB.",
        PhoneStep.AdbFailed => "The phone tool (ADB) didn't start. Close other Android tools (Android Studio, scrcpy) and try again, or reinstall it.",
        PhoneStep.NoUsb => "Plug the phone into the PC with a USB cable. If it's already plugged in, try another cable: some only charge.",
        PhoneStep.UsbDriverError => "Windows sees the phone but its USB driver failed" + (problem is int c ? $" (code {c})" : "") + ". Unplug it, use another USB port or cable, or restart the phone."
            + (problem == 28 && samsung ? " If it keeps failing, install \"Samsung USB Driver for Mobile Phones\" from Samsung's website." : ""),
        PhoneStep.NoUsbDebugging => "Turn on USB debugging. Without it everything works except Change IP.\n\n" + UsbDebuggingSteps
            + (samsung ? "\n\nStill not found? Install \"Samsung USB Driver for Mobile Phones\" from Samsung's website." : ""),
        PhoneStep.Unauthorized => "Allow USB debugging on the phone screen (tick \"Always allow from this computer\").",
        PhoneStep.Offline => "The phone is offline in ADB. Unplug the cable and plug it back in.",
        PhoneStep.TetherOff => "Turn on USB tethering by hand: Settings > Network & internet > Hotspot & tethering (Samsung: Settings > Connections > Mobile Hotspot and Tethering), then USB tethering.",
        PhoneStep.WifiOn => "Turn off the phone's Wi-Fi: with it on, the PC may get your home network instead of mobile data.",
        PhoneStep.PcOnPhone => "The rest of the PC is going through the phone. Turn on Settings > Protection > Save mobile data, or plug the PC into your home network.",
        _ => "The phone is ready.",
    };

    /// <summary>The short name of a step for the Log and the usage counts (never a serial, a name or a network).</summary>
    public static string Key(PhoneStep step) => step.ToString().ToLowerInvariant();
}
