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
        // Apple also makes keyboards, mice and displays: only "iPhone" or "Apple Mobile Device" counts (beta.11, iPhone setup)
        ["05AC"] = "Apple",
    };

    static readonly Regex VidPid = new(@"^USB\\VID_([0-9A-F]{4})&PID_[0-9A-F]{4}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PhoneName = new("Android|ADB Interface|Mobile USB|Mobile Phone|Remote NDIS|RNDIS|Galaxy|Pixel|\\bMTP\\b|iPhone|Apple Mobile Device", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The phone on USB is an iPhone (no ADB: its setup and Change IP are its own).</summary>
    public static bool IsIphone(PhoneUsbState usb) => usb?.Vendor == "Apple";

    /// <summary>The tethering adapter is an iPhone's (Apple's driver names it "Apple Mobile Device Ethernet").</summary>
    public static bool IsIphone(Adapter a) => a?.Description?.Contains("Apple Mobile Device Ethernet", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// What the PC sees right now about the kind of phone: true for an iPhone (Apple on USB, Apple's adapter, or a hotspot named like
    /// an iPhone's, "Ana's iPhone"), false for an Android (ready on ADB, or another phone vendor on USB), null when nothing says.
    /// A hotspot over Wi-Fi with no cable says nothing by itself, so the last answer is kept (Settings.PhoneIsIphone).
    /// </summary>
    public static bool? IphoneSeen(bool adbReady, PhoneUsbState usb, Adapter adapter, string hotspotSsid) =>
        adbReady ? false
        : IsIphone(usb) || IsIphone(adapter) || hotspotSsid?.Contains("iPhone", StringComparison.OrdinalIgnoreCase) == true ? true
        : usb?.Seen == true && usb.Vendor != null ? false
        : null;

    /// <summary>
    /// The phone is an iPhone: what the PC sees, or else the saved answer while a hotspot is picked (an iPhone on Wi-Fi alone must
    /// not get the Android steps, beta.12). Without a hotspot and without a cable nothing is assumed.
    /// </summary>
    public static bool IsIphoneNow(bool? seen, bool saved, string hotspotSsid) => seen ?? (saved && !string.IsNullOrEmpty(hotspotSsid));

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

    /// <summary>
    /// Windows has Apple's network driver for the iPhone's Personal Hotspot over USB (netaapl64.inf, "Apple Mobile Device Ethernet").
    /// Windows Update only brings Apple's plain USB driver: the network one comes with the Apple Devices app (or iTunes). Without it the
    /// iPhone shows up on USB but no network adapter ever appears (9 nov., on the owner's PC).
    /// </summary>
    public static bool AppleNetDriver()
    {
        try
        {
            var repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "DriverStore", "FileRepository");
            return Directory.EnumerateDirectories(repo, "netaapl*.inf_*").Any();
        }
        catch { return true; }   // can't tell: don't send anyone to install what may already be there
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
    /// <summary>The phone is an iPhone (seen on USB as Apple, or its tethering adapter is "Apple Mobile Device Ethernet").</summary>
    public bool Iphone { get; init; }
    /// <summary>Apple's network driver is installed (<see cref="PhoneUsb.AppleNetDriver"/>). Only read for an iPhone.</summary>
    public bool AppleDriver { get; init; } = true;
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
        // An iPhone has no ADB: connected is done (Change IP is by hand), otherwise the driver, the cable, Apple's network driver,
        // then Personal Hotspot
        if (s.Iphone) return connected ? PhoneStep.Done : s.Usb.Problem != null ? PhoneStep.UsbDriverError : !s.Usb.Seen ? PhoneStep.NoUsb
            : !s.AppleDriver ? PhoneStep.UsbDriverError : PhoneStep.TetherOff;
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
        "1. Settings > About phone (Samsung: also Software information). Tap \"Build number\" 7 times.\n" +
        "2. Settings > Developer options. Turn on \"USB debugging\".\n" +
        "3. Unplug the cable and plug it back in, then press Phone setup. On the phone, tap Allow.";

    /// <summary>The menu paths for one brand of phone. <see cref="Verified"/>: someone followed them on a real phone of that brand.</summary>
    public sealed record MenuPaths(string Brand, string BuildNumber, string DevOptions, string Tethering, bool Verified);

    /// <summary>Any Android phone (stock Android, Pixel and most others).</summary>
    public static readonly MenuPaths GenericPaths = new(null,
        "Settings > About phone. Tap \"Build number\" 7 times.",
        "Settings > Developer options (on some phones: Settings > System). Turn on \"USB debugging\".",
        "Settings > Network & internet > Hotspot & tethering. Turn on USB tethering.", true);

    /// <summary>
    /// Brand paths (beta.11, fix 3 of the simulated users). Only the verified ones reach the screen: a path nobody followed on a
    /// real phone is worse than the generic one. Xiaomi and Motorola are written down to check on a real phone first.
    /// </summary>
    public static readonly MenuPaths[] BrandPaths =
    {
        new("Samsung", "Settings > About phone > Software information. Tap \"Build number\" 7 times.",
            "Settings > Developer options (at the end of Settings). Turn on \"USB debugging\".",
            "Settings > Connections > Mobile Hotspot and Tethering. Turn on USB tethering.", true),
        // Not checked on a real phone yet (MIUI and HyperOS name the line differently; switching mobile data may also need
        // "USB debugging (Security settings)")
        new("Xiaomi", "Settings > About phone. Tap \"MIUI version\" (or \"OS version\") 7 times.",
            "Settings > Additional settings > Developer options. Turn on \"USB debugging\".",
            "Settings > Connection & sharing. Turn on USB tethering.", false),
        new("Motorola", "Settings > About phone. Tap \"Build number\" 7 times.",
            "Settings > System > Developer options. Turn on \"USB debugging\".",
            "Settings > Network & internet > Hotspot & tethering. Turn on USB tethering.", false),
    };

    /// <summary>The paths to show for the phone's brand: its own when verified, otherwise the generic ones.</summary>
    public static MenuPaths PathsFor(string vendor) =>
        BrandPaths.FirstOrDefault(p => p.Verified && string.Equals(p.Brand, vendor, StringComparison.OrdinalIgnoreCase)) ?? GenericPaths;

    /// <summary>Menus differ by brand: the one hint that is true on every Android phone, for the brands without their own paths.</summary>
    public const string SearchHint = "Can't find it? Use the search at the top of Settings.";

    /// <summary>The USB debugging steps for one brand (short lines, beta.11).</summary>
    public static string UsbDebuggingFor(MenuPaths p) =>
        $"1. {p.BuildNumber}\n2. {p.DevOptions}\n3. Unplug the cable and plug it back in. On the phone, tap Allow."
        + (p.Brand == null ? "\n" + SearchHint : "");

    /// <summary>Why the step is worth it, and what the app does with it (4 nov.: the simulated users quit here, scared or lost).</summary>
    public const string UsbDebuggingWhy = "Needed for Change IP. After this, the app does the rest.";
    public const string UsbDebuggingSafe = "The app only switches mobile data, Wi-Fi and USB tethering on the phone, and installs nothing there. You can turn USB debugging off later.";

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
        PhoneStep.Unauthorized => "On the phone, tick \"Always allow from this computer\", then tap Allow.",
        PhoneStep.Offline => "The phone is offline in ADB. Unplug the cable and plug it back in.",
        PhoneStep.TetherOff => "Turn on USB tethering by hand: Settings > Network & internet > Hotspot & tethering (Samsung: Settings > Connections > Mobile Hotspot and Tethering), then USB tethering.",
        PhoneStep.WifiOn => "Turn off the phone's Wi-Fi: with it on, the PC may get your home network instead of mobile data.",
        PhoneStep.PcOnPhone => "The rest of the PC is going through the phone. Turn on Settings > Mobile data > Save mobile data, or plug the PC into your home network.",
        _ => "The phone is ready.",
    };

    public enum StepState { Ok, Todo, Waiting }

    /// <summary>One line of the Phone setup panel: what it checks, how it is now, and the button when the app can do it (null = you do it).</summary>
    public sealed record SetupItem(PhoneStep Step, string Title, StepState State, string Detail, string Action);

    /// <summary>
    /// The Phone setup panel (beta.10), read again every 2 s while it is open: each step green when it is done, red when it is the
    /// thing to do, grey while it waits for the one before (USB debugging can't be checked with no cable). With the hotspot there is
    /// no tethering to turn on and the phone's Wi-Fi stays on; the cable is then only for Change IP.
    /// </summary>
    public static List<SetupItem> Steps(PhoneSetupState s, bool samsung = false, string model = null, string ssid = null)
    {
        if (s.Iphone) return IphoneSteps(s, ssid);
        var adb = s.AdbState == "device";
        var inAdb = s.AdbState is "device" or "unauthorized" or "offline";
        var plugged = inAdb || s.Usb.Seen || s.Tethered;
        var paths = PathsFor(samsung ? "Samsung" : s.Usb.Vendor);
        var list = new List<SetupItem>
        {
            !s.AdbTool ? new(PhoneStep.NoAdbTool, "Phone tool (ADB)", StepState.Todo, "Free, from Google, about 7 MB. Change IP and these checks need it.", "Install")
            : s.AdbState == null ? new(PhoneStep.AdbFailed, "Phone tool (ADB)", StepState.Todo, Advice(PhoneStep.AdbFailed), "Try again")
            : new(PhoneStep.NoAdbTool, "Phone tool (ADB)", StepState.Ok, "Installed", null),

            s.Usb.Problem is int code ? new(PhoneStep.UsbDriverError, "USB cable", StepState.Todo, Advice(PhoneStep.UsbDriverError, code, samsung), null)
            : plugged ? new(PhoneStep.NoUsb, "USB cable", StepState.Ok, model != null && adb ? $"{model} plugged in" : "Phone plugged in", null)
            : s.Hotspot ? new(PhoneStep.NoUsb, "USB cable", StepState.Waiting, "You use the hotspot: the cable is only needed for Change IP.", null)
            : new(PhoneStep.NoUsb, "USB cable", StepState.Todo, Advice(PhoneStep.NoUsb), null),

            inAdb ? new(PhoneStep.NoUsbDebugging, "USB debugging", StepState.Ok, "On", null)
            : !plugged || s.AdbState == null || !s.AdbTool ? new(PhoneStep.NoUsbDebugging, "USB debugging", StepState.Waiting, "Checked once the phone is plugged in.", null)
            : new(PhoneStep.NoUsbDebugging, "USB debugging", StepState.Todo, UsbDebuggingWhy + "\n" + UsbDebuggingFor(paths)
                + (samsung ? "\nStill not found? Install \"Samsung USB Driver for Mobile Phones\" from Samsung's website." : "") + "\n" + UsbDebuggingSafe, null),

            adb ? new(PhoneStep.Unauthorized, "Allowed on this PC", StepState.Ok, "Allowed", null)
            : s.AdbState == "unauthorized" ? new(PhoneStep.Unauthorized, "Allowed on this PC", StepState.Todo, Advice(PhoneStep.Unauthorized), null)
            : s.AdbState == "offline" ? new(PhoneStep.Offline, "Allowed on this PC", StepState.Todo, Advice(PhoneStep.Offline), null)
            : new(PhoneStep.Unauthorized, "Allowed on this PC", StepState.Waiting, "Your phone will ask \"Allow USB debugging?\". Tap Allow.", null),

            s.Hotspot ? new(PhoneStep.TetherOff, "Connection", StepState.Ok, ssid != null ? $"The phone's hotspot ({ssid})" : "The phone's hotspot", null)
            : s.Tethered ? new(PhoneStep.TetherOff, "USB tethering", StepState.Ok, "On", null)
            : adb ? new(PhoneStep.TetherOff, "USB tethering", StepState.Todo, "Off. The app can turn it on (on some phones you do it by hand).", "Turn on")
            : plugged ? new(PhoneStep.TetherOff, "USB tethering", StepState.Todo, paths.Tethering + (paths.Brand == null ? "\n" + SearchHint : ""), null)
            : new(PhoneStep.TetherOff, "USB tethering", StepState.Waiting, "Once the phone is plugged in.", null),

            s.Hotspot ? new(PhoneStep.WifiOn, "Phone Wi-Fi", StepState.Ok, "Stays on for the hotspot", null)
            : !adb ? new(PhoneStep.WifiOn, "Phone Wi-Fi off", StepState.Waiting, "Turn off Wi-Fi on the phone. Keep mobile data on.", null)
            : s.WifiOn ? new(PhoneStep.WifiOn, "Phone Wi-Fi off", StepState.Todo, "On: tethering may share your home Wi-Fi instead of mobile data.", "Turn off")
            : new(PhoneStep.WifiOn, "Phone Wi-Fi off", StepState.Ok, "Off: mobile data", null),
        };
        if (s.PcOnPhone) list.Add(new(PhoneStep.PcOnPhone, "Rest of the PC", StepState.Todo, "Going through the phone (uses mobile data).", "Keep it home"));
        return list;
    }

    public const string IphoneDriver = "Windows needs Apple's driver to use the iPhone's connection: install \"Apple Devices\" from the Microsoft Store (or iTunes), then plug the iPhone in again.";
    public const string IphoneHotspot = "On the iPhone: Settings > Personal Hotspot > turn on \"Allow Others to Join\". Keep the cable plugged in.";
    public const string IphoneChangeIp = "Change IP on an iPhone is done on the iPhone: turn Airplane Mode on, wait 3 seconds, turn it off. Change IP in the app tells you when, and shows the new IP.";

    /// <summary>Apple Devices in the Microsoft Store (free, by Apple): it brings the iPhone's network driver.</summary>
    public const string AppleDevicesStoreId = "9NP83LWLPZ9K";
    public const string IphoneDriverMissing = "Windows sees the iPhone but doesn't have Apple's network driver yet. Press Install: the Microsoft Store opens on \"Apple Devices\" (free, by Apple), press Get there. Then unplug the iPhone and plug it in again.";

    /// <summary>
    /// The Store's small install window for Apple Devices. The Store installs it as you, not the app: running winget from an app that
    /// runs as admin would run whatever winget.exe sits in your user folder with admin rights.
    /// </summary>
    public static string AppleDevicesStoreUri => $"ms-windows-store://pdp/?ProductId={AppleDevicesStoreId}&mode=mini";
    /// <summary>What to do when the iPhone's connection doesn't come back by itself (after Airplane Mode, or a cable that came out).</summary>
    public const string IphoneBringBack = "On the iPhone, open Settings > Personal Hotspot and leave that screen open. Still nothing after 10 seconds? Unplug the cable and plug it back in.";

    /// <summary>
    /// The iPhone's own steps (beta.11): no ADB (Android only). The driver (the app installs Apple's when it is missing, 9 nov.), the
    /// cable, Trust, Personal Hotspot. An iPhone's hotspot always shares mobile data, never Wi-Fi, so there is no Wi-Fi step. Without
    /// Apple's network driver the iPhone never trusts or shares anything the PC can see, so the rows after it wait for it.
    /// </summary>
    static List<SetupItem> IphoneSteps(PhoneSetupState s, string ssid)
    {
        var plugged = s.Usb.Seen || s.Tethered;
        var noDriver = plugged && !s.Tethered && !s.Hotspot && !s.AppleDriver;
        return new List<SetupItem>
        {
            s.Usb.Problem is int code ? new(PhoneStep.UsbDriverError, "Apple driver", StepState.Todo, IphoneDriver + $" (Windows says code {code}.)", "Install")
            : s.Tethered ? new(PhoneStep.UsbDriverError, "Apple driver", StepState.Ok, "Installed", null)
            : noDriver ? new(PhoneStep.UsbDriverError, "Apple driver", StepState.Todo, IphoneDriverMissing, "Install")
            : s.AppleDriver && plugged ? new(PhoneStep.UsbDriverError, "Apple driver", StepState.Ok, "Installed", null)
            : new(PhoneStep.UsbDriverError, "Apple driver", StepState.Waiting, "If the steps below stay red: " + IphoneDriver, null),

            plugged ? new(PhoneStep.NoUsb, "USB cable", StepState.Ok, "iPhone plugged in", null)
            : s.Hotspot ? new(PhoneStep.NoUsb, "USB cable", StepState.Waiting, "You use the hotspot over Wi-Fi: the cable is optional.", null)
            : new(PhoneStep.NoUsb, "USB cable", StepState.Todo, Advice(PhoneStep.NoUsb), null),

            s.Tethered || s.Hotspot ? new(PhoneStep.Unauthorized, "Trust this computer", StepState.Ok, "Trusted", null)
            : noDriver ? new(PhoneStep.Unauthorized, "Trust this computer", StepState.Waiting, "Once Apple's driver is installed.", null)
            : plugged ? new(PhoneStep.Unauthorized, "Trust this computer", StepState.Todo, "Unlock the iPhone. If it asks \"Trust This Computer?\", tap Trust and enter your passcode.\nNo question? " + IphoneBringBack, null)
            : new(PhoneStep.Unauthorized, "Trust this computer", StepState.Waiting, "The iPhone asks once it is plugged in.", null),

            s.Hotspot ? new(PhoneStep.TetherOff, "Connection", StepState.Ok, ssid != null ? $"The iPhone's hotspot ({ssid})" : "The iPhone's hotspot", null)
            : s.Tethered ? new(PhoneStep.TetherOff, "Personal Hotspot", StepState.Ok, "On, through the cable (mobile data)", null)
            : noDriver ? new(PhoneStep.TetherOff, "Personal Hotspot", StepState.Waiting, "Once Apple's driver is installed.", null)
            : plugged ? new(PhoneStep.TetherOff, "Personal Hotspot", StepState.Todo, IphoneHotspot + "\nStill red? " + IphoneBringBack, null)
            : new(PhoneStep.TetherOff, "Personal Hotspot", StepState.Waiting, "Once the iPhone is plugged in.", null),

            new(PhoneStep.Done, "Change IP", s.Tethered || s.Hotspot ? StepState.Ok : StepState.Waiting, IphoneChangeIp, null),
        };
    }

    /// <summary>
    /// What a row says (beta.11): only the step to do now is open. Done rows say "Done", the others only their title (null), so
    /// the one thing to do is the only text in the panel. With nothing to do every row says what it found, and the iPhone's
    /// Change IP row always keeps how to do it.
    /// </summary>
    public static string Shown(SetupItem item, SetupItem current)
    {
        if (item.Step == PhoneStep.Done) return current == null ? item.Detail : null;
        if (item.State == StepState.Ok) return "Done";
        if (current == null) return item.Detail;
        return item == current ? item.Detail : null;
    }

    /// <summary>The step to do now: the first red one (null = all done or waiting on nothing).</summary>
    public static SetupItem Current(IReadOnlyList<SetupItem> steps) => steps.FirstOrDefault(i => i.State == StepState.Todo);

    /// <summary>The step the panel is at, for the usage counts: the red one, or "done" when there is nothing left to do.</summary>
    public static string StepKey(IReadOnlyList<SetupItem> steps) => Current(steps) is { } now ? Key(now.Step) : "done";

    /// <summary>
    /// The usage counts when Phone setup closes (beta.11, dashboard v4 "where people get stuck"): the step when it was opened
    /// (phone_setup_, as before) and the step when it was closed (phone_left_, new), so the stats show where people give up.
    /// </summary>
    public static string[] ClosedCounts(string atOpen, string atClose) =>
        atOpen == null || atClose == null ? Array.Empty<string>() : new[] { "phone_setup_" + atOpen, "phone_left_" + atClose };

    /// <summary>The short name of a step for the Log and the usage counts (never a serial, a name or a network).</summary>
    public static string Key(PhoneStep step) => step.ToString().ToLowerInvariant();
}
