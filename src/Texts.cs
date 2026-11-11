namespace SRT;

/// <summary>
/// Every visible text added since the onboarding work of 10 Nov 2026 (first-run tour, Phone setup's ADB download, cable and USB
/// debugging guides, the Regions note), in one place, so the app can be translated (pt-BR, es, ru) without hunting through the XAML.
/// XAML reads these with {x:Static local:Texts.Name}. Older texts still live where they are used; move them here as they are touched.
/// Menu names on the phone ("About phone", "Developer options"...) are the ones the phone shows in English: a translation uses the
/// names the phone shows in that language.
/// </summary>
public static class Texts
{
    // ------------------------------------------------------------ Phone setup: the phone tool (ADB), downloaded by the app

    public const string AdbTitle = "Phone tool (ADB)";
    public const string AdbStarting = "Free, from Google, about 7 MB. Downloading it now, nothing to do.";
    public const string AdbOnMobileData = "Free, from Google, about 7 MB. The PC is on the phone's mobile data right now, so it waits for you: press Install.";
    public const string AdbInstall = "Install";
    public const string AdbTryAgain = "Try again";
    public const string AdbUnpacking = "Unpacking…";
    public const string AdbChecking = "Checking Google's signature…";
    public const string AdbInstalledToast = "Phone tool installed. Next: plug in the phone.";

    /// <summary>The download's progress, in MB with one decimal; the percent only when Google says the size.</summary>
    public static string AdbDownloading(long got, long? total)
    {
        static string Mb(long b) => (b / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return total is long t && t > 0
            ? $"Downloading from Google: {Mb(got)} of {Mb(t)} MB ({Math.Clamp((int)(got * 100 / t), 0, 100)}%)"
            : $"Downloading from Google: {Mb(got)} MB";
    }

    /// <summary>The download failed: why, what to try, and the way that always works.</summary>
    public static string AdbFailed(string why) =>
        $"The download didn't work ({why}). Check that the PC has internet and press Try again.\n"
        + "Still failing? Download \"SDK Platform-Tools for Windows\" from developer.android.com and unzip it into the app's data folder (Settings > Maintenance > Open data folder).";

    // ------------------------------------------------------------ Phone setup: the USB cable

    /// <summary>Connection > Phone with nothing on USB (was "a USB cable": the cable has to carry data).</summary>
    public const string PhoneBoxPlugIn = "Plug in your phone with a USB data cable, then press Phone setup.";

    public const string CableTodo = "Plug the phone into the PC with a USB data cable. The one that came with the phone is best: many cheap cables only charge, and then the PC sees nothing.";

    /// <summary>The live line under the cable step: what the app sees right now, so it never looks stuck.</summary>
    public static string CableLive(int seconds) => $"Looking for the phone on USB every 2 s: nothing yet ({seconds} s).";

    /// <summary>After this long with nothing on USB, the cable step adds what to try next.</summary>
    public const int CableHelpAfterSeconds = 20;

    public const string CableStillNothing =
        "Still nothing? Unlock the phone. Try another cable, or another USB port (one on the back of the PC).\n"
        + "No data cable at hand? Turn on the phone's Wi-Fi hotspot, connect the PC to it and pick it in Connection > Phone > How the PC connects to the phone. Protection works over the hotspot; Change IP needs the cable later.";

    // ------------------------------------------------------------ Phone setup: USB debugging, with the steps drawn per brand

    public const string DebugTodo = "Needed for Change IP. Do the 3 steps below on the phone: this turns green by itself when it's on.";
    public const string DebugSafe = "The app only switches mobile data, Wi-Fi and USB tethering on the phone, and installs nothing there. You can turn USB debugging off later.";
    public const string DebugStillRed = "Done and still red? Unlock the phone, pull down the notifications, set USB to \"File transfer\", then unplug the cable and plug it back in.";
    public const string DebugSeenToast = "USB debugging is on. Now tap Allow on the phone.";
    public const string GuideYourPhone = "Your phone";
    public const string GuideSeen = "seen on USB";
    public const string GuideNotChecked = "Names can differ between versions: if one isn't there, use the search at the top of Settings.";
    public const string GuideOther = "Other";

    /// <summary>The marks on the drawn screens.</summary>
    public const string MarkTap7 = "tap 7×";
    public const string MarkTurnOn = "turn on";
    public const string MarkAllow = "Allow";

    /// <summary>The phone's own prompt, as Android shows it.</summary>
    public const string PhoneAllowTitle = "Allow USB debugging?";
    public const string PhoneAllowAlways = "Always allow from this computer";
    public const string PhoneAllowButton = "Allow";
    public const string PhoneCancelButton = "Cancel";

    public const string StepAllow = "Unplug the cable and plug it back in. Tick \"Always allow from this computer\", then tap Allow.";

    /// <summary>
    /// One drawn phone screen: its title, the one line to tap (the rest are grey bars, since those differ by model), the mark next to
    /// it, and whether that line is a switch. <paramref name="Prompt"/>: Android's "Allow USB debugging?" question instead of a list.
    /// </summary>
    public sealed record GuideScreen(string Title, string Row, string Mark, bool Switch = false, bool Prompt = false);
    public sealed record GuideStep(GuideScreen Screen, string Text);

    /// <summary>
    /// The USB debugging steps of one brand. <paramref name="Checked"/>: followed on a real phone of that brand (Samsung, beta.11);
    /// the others come from Android's and third-party guides (10 Nov 2026) and carry <see cref="GuideNotChecked"/>.
    /// </summary>
    public sealed record UsbGuide(string Brand, string Name, bool Checked, GuideStep[] Steps);

    static readonly GuideStep Allow = new(new(PhoneAllowTitle, PhoneAllowAlways, MarkAllow, Prompt: true), StepAllow);

    public static readonly UsbGuide[] Guides =
    {
        new("samsung", "Samsung", true, new[]
        {
            new GuideStep(new("Software information", "Build number", MarkTap7), "Settings > About phone > Software information. Tap \"Build number\" 7 times; enter your PIN if asked."),
            new GuideStep(new("Developer options", "USB debugging", MarkTurnOn, Switch: true), "Back in Settings, open Developer options (at the end of the list). Turn on \"USB debugging\"."),
            Allow,
        }),
        new("xiaomi", "Xiaomi", false, new[]
        {
            new GuideStep(new("About phone", "OS version", MarkTap7), "Settings > About phone. Tap \"OS version\" 7 times (older phones: \"MIUI version\")."),
            new GuideStep(new("Developer options", "USB debugging", MarkTurnOn, Switch: true), "Settings > Additional settings > Developer options. Turn on \"USB debugging\"."),
            Allow,
        }),
        new("google", "Pixel", false, new[]
        {
            new GuideStep(new("About phone", "Build number", MarkTap7), "Settings > About phone. Tap \"Build number\" 7 times."),
            new GuideStep(new("Developer options", "USB debugging", MarkTurnOn, Switch: true), "Settings > System > Developer options. Turn on \"USB debugging\"."),
            Allow,
        }),
        new("other", GuideOther, false, new[]
        {
            new GuideStep(new("About phone", "Build number", MarkTap7), "Settings > About phone (on some phones, then Software information). Tap \"Build number\" 7 times."),
            new GuideStep(new("Developer options", "USB debugging", MarkTurnOn, Switch: true), "Settings > Developer options (on some phones: Settings > System). Turn on \"USB debugging\"."),
            Allow,
        }),
    };

    // ------------------------------------------------------------ Phone setup: "Allowed on this PC"

    public const int AllowHelpAfterSeconds = 15;
    public const string AllowNoQuestion =
        "No question on the phone? Unlock it and look in the notifications. Still nothing: unplug the cable and plug it back in, or in Developer options tap \"Revoke USB debugging authorizations\" and plug in again.";

    // ------------------------------------------------------------ Regions: what the phone adds (shown once)

    public const string RegionsPitch = "Regions works without a phone. Plug your phone in and servers see your carrier's IP instead of your home IP, and Change IP gets you a new one in seconds.";
    public const string RegionsPitchSetup = "Set up the phone";
    public const string RegionsPitchHide = "Hide";

    // ------------------------------------------------------------ First-run tour (Settings > App > Show the tour again)

    public const string TourSkip = "Skip tour";
    public const string TourBack = "Back";
    public const string TourNext = "Next";
    public const string TourDone = "Done";
    public const string TourLater = "Later";
    public const string TourOpenSetup = "Open Phone setup";
    public const string TourAgain = "Show the tour again";
    public const string TourAgainTip = "The short tour of the app from the first start: Status, Change IP, Regions, Accounts and Phone setup.";
    public const string TourAgainHint = "Again in Settings > App > Show the tour again.";

    public static string TourCount(int step, int of) => $"{step} of {of}";

    /// <summary>One stop of the tour: the control it points at (an x:Name in MainWindow.xaml), its title and its text.</summary>
    public sealed record TourStop(string Target, string Title, string Text);

    public static readonly TourStop[] Tour =
    {
        new("Hero", "Status", "What servers see right now. Today that's your home IP. With the phone connected and protection on, they see the phone's IP, and if the phone drops they are blocked instead of seeing your home IP."),
        new("ChangeIpBtn", "Change IP", "Once the phone is set up, this gets a new IP from your carrier in a few seconds. Change IP and rejoin also takes you back to the server."),
        new("NavRegions", "Regions", "Pick the matchmaking regions CS2 may use. Works with or without the phone."),
        new("NavAccounts", "Accounts", "Your Steam accounts, one click to log in. New IP + next account at the top does both at once."),
        new("PhoneGroup", "Phone setup", "The phone is what changes your IP. Plug it in with a USB data cable and open Phone setup: it installs the phone tool and checks each step for you."),
    };
}
