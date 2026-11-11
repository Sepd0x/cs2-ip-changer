using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// Phone and IP change
public partial class MainWindow
{
    async Task RefreshPhone()
    {
        if (_demo || _phoneBusy) return;
        _phoneBusy = true;
        try { await RefreshPhoneCore(); }
        finally { _phoneBusy = false; }
    }

    /// <summary>The phone is an iPhone (beta.11): no ADB, so the Phone box, Phone setup and Change IP have its own steps.</summary>
    bool _iphone;

    async Task RefreshPhoneCore()
    {
        _adb ??= Adb.Find();
        // An iPhone first: it never shows up in ADB, and "install the phone tool" or "turn on USB debugging" would send you the wrong way
        if (_phone?.Ready != true && await IphoneState() is (true, var tethered))
        {
            AdbInstallBtn.Visibility = Visibility.Collapsed;
            if (_s.NoPhone) SetPhone(Ui.Faint, Loc.T("iPhone not in use"), Loc.T("No phone mode is on"));
            else if (tethered) SetPhone(Ui.Good, Loc.T("iPhone connected"), Loc.T("Change IP: the app tells you when to turn Airplane Mode on and off, then shows the new IP."));
            else SetPhone(Ui.Warn, Loc.T("iPhone found, no connection yet"), Loc.T(PhoneSetup.IphoneHotspot));
            UpdatePhoneCheck();
            return;
        }
        _iphone = false;
        if (_adb == null)
        {
            SetPhone(Ui.Faint, Loc.T("Phone tool not installed"), Loc.T("ADB, free from Google, about 7 MB. Phone setup installs it for you."));
            AdbInstallBtn.Visibility = Visibility.Visible;
            return;
        }
        AdbInstallBtn.Visibility = Visibility.Collapsed;
        // One stray Wi-Fi answer (an adb error, a timeout) neither flips the block nor lifts it: see Adb.SteadyWifi.
        var info = Adb.SteadyWifi(await Adb.Status(_adb), _phone, ref _wifiOnReads);
        var wifiBefore = PhoneWifiOn;
        _phone = info;   // SetPhone below redraws the buttons for it (PhoneFirstRun)
        if (info?.Ready == true) NotePhoneKind(false);
        Telemetry.Phone(info);
        DiagPhone(info);
        if (info == null) SetPhone(Ui.Bad, Loc.T("ADB failed to start"), Loc.T("Try reinstalling ADB."));
        else if (info.State == "none")
        {
            // ADB sees nothing: Windows may still see the phone, with its USB driver failed (code 10 until a restart, on Samsung).
            var usb = await Task.Run(PhoneUsb.Scan);
            NoteUsb(usb);
            if (usb.Problem is int code) SetPhone(Ui.Bad, Loc.T("Phone driver failed"), PhoneSetup.Advice(PhoneStep.UsbDriverError, code, usb.Vendor == "Samsung"));
            // No jargon before there is a reason for it (4 nov., simulated users): Phone setup explains USB debugging when it's the step
            else SetPhone(Ui.Faint, Loc.T("Phone not found"), usb.Seen ? Loc.T("Phone plugged in. Press Phone setup to finish.") : Loc.T(Texts.PhoneBoxPlugIn));
        }
        else if (!info.Ready) SetPhone(Ui.Warn, Loc.T("Waiting for authorization"), info.Summary);
        else if (info.WifiOn && info.WifiConnected == false)
            SetPhone(Ui.Good, Loc.T("Ready to change IP"), info.Summary + " · " + Loc.T("Wi-Fi on, not connected (still mobile data)"));
        else if (info.WifiOn)
            SetPhone(Ui.Warn, Loc.T("Phone Wi-Fi is on"),
                (info.WifiSsid != null ? Loc.F("Connected to {0}.", info.WifiSsid) + " " : "") + Loc.T("With Wi-Fi on, the PC may get your home network instead of mobile data."));
        else if (_last != null && _last.Egress == null && _last.Candidate == null)
            SetPhone(Ui.Warn, Loc.T("Phone found, no connection yet"), info.Summary + ". " + Loc.T("Turn on USB tethering."));
        else SetPhone(Ui.Good, Loc.T("Ready to change IP"), info.Summary);
        PhoneNotInUse();
        // The same rule as the block (PhoneWifiOn): on the hotspot the button stays hidden, since on some phones turning Wi-Fi off
        // also stops the hotspot (Change IP leaves it alone for that reason). The Phone box still warns when Wi-Fi is on.
        WifiOffBtn.Visibility = info?.Ready == true && PhoneWifiOn ? Visibility.Visible : Visibility.Collapsed;
        UpdatePhoneCheck();
        // No phone adapter by its name: the phone says which addresses it shares on, and an adapter with one as its gateway is the phone
        // (a driver name the app doesn't know, 9 nov.). Read again at most every 5 s, only while there is none.
        if (info?.Ready == true && _last?.Adapters != null && _last.Candidate == null && !_last.Adapters.Any(a => a.IsPhone && a.Up && a.LocalIp != null))
        {
            await ReadPhoneAddresses(info.Serial);
            if (_last.Adapters.Any(a => !a.IsWifi && a.Up && a.Gateway != null && Net.PhoneUsbIps.Contains(a.Gateway)))
            {
                Log.Info("Found the phone's USB tethering by its address (Windows gives its adapter a name the app didn't know).");
                await SyncNow(force: true);
                _ = RefreshPublicIps();
            }
        }
        // Tethering on in the phone but Windows has no phone adapter: happens after the cable flickers.
        // No phone mode has no candidate, so the adapters say whether Windows sees the phone.
        if (info?.Ready == true && _last != null && _last.Candidate == null && !(_last.Adapters?.Any(a => a.IsPhone && a.Up && a.LocalIp != null) ?? false)
            && !PhoneLink.IsHotspot(_s.EgressSsid, PhoneLink.Exit(_last)?.IsWifi) && await Adb.TetheringOn(_adb, info.Serial))
        {
            _tetherMissingSince ??= DateTime.UtcNow;
            if (!_tetherHinted && TetherHintDue(_tetherAskedAt, _tetherMissingSince.Value, DateTime.UtcNow))
            {
                _tetherHinted = true;
                Log.Warn("The phone has USB tethering on, but Windows doesn't see it. Unplug the cable and plug it back in, or turn tethering off and on.");
                Toast(Loc.T("Windows doesn't see the phone's USB tethering. Unplug the cable and plug it back in."), false);
            }
        }
        else { _tetherHinted = false; _tetherMissingSince = null; }

        if (PhoneWifiOn != wifiBefore)
        {
            if (PhoneWifiOn) { if (PhoneWifiOnText(_phone.WifiSsid, _protOn, _s.NoPhone) is string t) Log.Warn(t); _verifiedKey = null; }
            else Log.Ok("The phone's Wi-Fi is off again. Checking the phone's IP before trusting it.");
            await SyncNow(force: true);
            if (!PhoneWifiOn) _ = RefreshPublicIps();
        }
    }

    /// <summary>What the Log says when the phone's Wi-Fi comes on. Only protection on blocks anything; in No phone mode the phone's Wi-Fi doesn't matter.</summary>
    public static string PhoneWifiOnText(string ssid, bool protOn, bool noPhone)
    {
        if (noPhone) return null;
        var on = "The phone's Wi-Fi is on" + (ssid != null ? $" (connected to {ssid})" : "") + ".";
        return on + (protOn ? " Servers and CS2 blocked until it's off." : " Turn it off before you turn on protection.");
    }

    int? _usbProblem;
    int _wifiOnReads;

    /// <summary>A failed USB driver goes to the Log once (the vendor and the code only, never the device's serial).</summary>
    void NoteUsb(PhoneUsbState usb)
    {
        if (usb.Problem == _usbProblem) return;
        _usbProblem = usb.Problem;
        if (usb.Problem is int code) Log.Warn($"Windows sees the phone ({usb.Vendor}) but its USB driver failed (code {code}). Unplug it, use another USB port or cable, or restart the phone.");
        else Log.Info("The phone's USB driver works again.");
    }

    /// <summary>"Mobile data" check: the phone shares its data and not the home Wi-Fi.</summary>
    void UpdatePhoneCheck()
    {
        PhoneFirstRun();
        // Change IP only makes sense with the phone ready on ADB, or an iPhone connected (and not in the no phone mode).
        var iphone = _iphone && _phone?.Ready != true;
        ChangeIpBtn.IsEnabled = !_changingIp && ChangeIpReady;
        var drop = IpChangeDrops ? " " + Loc.T("If you are on a server, you will drop.") : "";
        ChangeIpBtn.ToolTip = _s.NoPhone ? Loc.T("Off in the no phone mode: pick another option under What goes through the phone.")
            : iphone ? Loc.T("Tells you when to turn Airplane Mode on and off on the iPhone, then shows the new IP.") + drop
            : Loc.T("Turns the phone's mobile data off and on until the carrier gives a new IP. Needs the phone set up over ADB.") + drop;
        UpdateRejoinBtn();
        UpdateDataCheck();
    }

    /// <summary>
    /// Change IP can run: the phone ready on ADB, or an iPhone whose connection is up, protected or still being checked (with
    /// protection off there is never a protected one). Never in No phone mode.
    /// </summary>
    public static bool CanChangeIp(bool noPhone, bool adbReady, bool iphone, bool iphoneUp) => !noPhone && (adbReady || iphone && iphoneUp);

    /// <summary>
    /// The one rule for "the phone can change its IP now": the Change IP button, Change IP and rejoin (RejoinEnabled) and the IP step
    /// of New IP + next account on an iPhone (QuickChoice) follow it. With the account step the switch also needs ADB (SwitchChangesIp).
    /// </summary>
    bool ChangeIpReady => CanChangeIp(_s.NoPhone, _phone?.Ready == true, IphoneByHand, PhoneLink.Exit(_last) != null);

    /// <summary>The Mobile data row of Checks. While an IP change runs it says the data is off (or coming back), never green.</summary>
    void UpdateDataCheck()
    {
        // The phone's connection, protected or still being checked: on the hotspot the phone's Wi-Fi says nothing (PhoneLink.WifiOn)
        var egress = PhoneLink.Exit(_last);
        if (_changingData != null && !_s.NoPhone)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = _changingData;
        }
        else if (_s.NoPhone)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = Loc.T("Off: no phone mode");
        }
        else if (egress != null && egress.IsWifi)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = Loc.T("Through the hotspot: the phone is already using its data");
        }
        else if (_phone?.Ready != true)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = _adb == null ? Loc.T("Needs Phone setup") : Loc.T("Phone not found (check the USB cable)");
        }
        else
        {
            var wifi = PhoneLink.WifiOn(_phone, egress?.IsWifi);
            (_cPhoneData.Ok, _cPhoneData.Waiting) = DataRow(wifi, _last?.KillSwitchOk == true, _last?.Unprotected == true);
            var detail = wifi ? Loc.T("Phone Wi-Fi is on") + (_phone.WifiSsid != null ? $" ({_phone.WifiSsid})" : "") : $"{_phone.Carrier} · {_phone.NetType}".Trim(' ', '·');
            _cPhoneData.Detail = _last?.Unprotected == true ? DataDetailOff(detail) : detail;
        }
    }

    /// <summary>
    /// The Mobile data row with the phone on ADB. Protection off: grey, and it doesn't count as needing you (6 nov.: red and amber only
    /// while protection is on, like the Phone connection row). Wi-Fi on with the block confirmed: blocked and safe (amber); with
    /// protection on and no block, a real risk (red).
    /// </summary>
    public static (bool? Ok, bool Waiting) DataRow(bool wifiOn, bool killSwitchOk, bool unprotected) =>
        unprotected ? (null, false) : wifiOn && killSwitchOk ? (null, true) : (!wifiOn, false);

    /// <summary>The Mobile data row with protection off: what the phone says, and that it isn't in use.</summary>
    public static string DataDetailOff(string detail) => (string.IsNullOrEmpty(detail) ? "" : detail + " · ") + Loc.T("not in use: protection is off");

    async void WifiOff_Click(object sender, RoutedEventArgs e)
    {
        if (_phone?.Ready != true) return;
        await TurnPhoneWifiOff();
        await Task.Delay(5000);
        await RefreshPhone();
        await RefreshPublicIps();
    }

    async Task TurnPhoneWifiOff()
    {
        var serial = _phone.Serial;
        // Read before Wi-Fi goes off: a phone with mobile data off would be left with no internet (Adb.PhoneNetSteps)
        var dataOn = await Adb.MobileDataOn(_adb, serial);
        var r = await Adb.DisableWifi(_adb, serial);
        if (r.Ok) { Log.Ok("Phone Wi-Fi turned off: tethering now uses mobile data."); Toast(Loc.T("Phone Wi-Fi turned off."), true); }
        else { Log.Warn("Turning off the phone Wi-Fi: " + r.Output); Toast(Loc.T("The phone refused. Turn off Wi-Fi manually."), false); }
        await TurnPhoneDataOn(serial, dataOn);
    }

    /// <summary>Mobile data on, unless the phone says it already is. Said in the Log only when the phone said it was off.</summary>
    async Task TurnPhoneDataOn(string serial, bool? dataOn)
    {
        if (!Adb.PhoneNetSteps(false, dataOn, false).DataOn) return;
        if (dataOn == false) Log.Warn("Mobile data is off on the phone: turning it on.");
        var r = await Adb.EnableData(_adb, serial);
        if (!r.Ok && dataOn == false) { Log.Warn("Turning on the phone's mobile data: " + r.Output); Toast(Loc.T("Turn on mobile data on the phone."), false); }
    }

    void SetPhone(Brush dot, string state, string detail)
    {
        PhoneDot.Fill = dot;
        PhoneState.Text = state;
        PhoneDetail.Text = detail;
        PhoneFirstRun();
    }

    /// <summary>
    /// Until a phone has been seen once, Phone setup is the one button to press: it goes first, as the only primary, and the single
    /// steps it does by itself (install ADB, USB tethering) stay hidden. After that the buttons are back as before.
    /// </summary>
    /// <summary>Turn on USB tethering only when it can work and isn't on yet.</summary>
    public static bool ShowTetherButton(bool firstRun, bool phoneReady, bool tethered) => !firstRun && phoneReady && !tethered;

    /// <summary>
    /// Phone setup is the blue button on the first run, and when protection is on and the phone is gone: Status leaves Change IP and
    /// rejoin grey then (StatusPrimary, fixFirst), so the fix is the one to press. With the phone's Wi-Fi on, Turn off phone Wi-Fi is.
    /// </summary>
    public static bool SetupPrimary(bool firstRun, bool phoneLost, bool phoneWifiOn) => firstRun || phoneLost && !phoneWifiOn;

    void PhoneFirstRun()
    {
        var first = !PhoneKnown && !_s.NoPhone;
        AdbInstallHost.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
        // USB tethering can only be asked of a phone ADB sees: while it isn't found the button would only fail.
        // beta.11 (2nd round, Hugo: "Ready to change IP" next to "Turn on USB tethering"): not while the phone's tethering already carries the connection
        TetherHost.Visibility = ShowTetherButton(first, _phone?.Ready == true, _last?.Egress?.IsPhone == true || _last?.Candidate?.IsPhone == true) ? Visibility.Visible : Visibility.Collapsed;
        // Protection on and the phone gone (seen before, Wi-Fi off): Phone setup is the fix, so it is the one blue button then too
        var lost = _protOn && !_s.NoPhone && _last != null && PhoneLink.Exit(_last) == null;
        PhoneSetupBtn.Style = (Style)FindResource(SetupPrimary(first, lost, PhoneWifiOn) ? "Primary" : typeof(Button));
        WifiOffBtn.Style = (Style)FindResource(first ? typeof(Button) : "Primary");
        var at = PhoneButtons.Children.IndexOf(PhoneSetupBtn);
        var want = first ? 0 : PhoneButtons.Children.Count - 1;
        if (at != want)
        {
            PhoneButtons.Children.Remove(PhoneSetupBtn);
            PhoneButtons.Children.Insert(want, PhoneSetupBtn);
        }
    }

    /// <summary>
    /// The first start (4 nov.): what's missing of Steam and CS2, each with what to do. Empty when both are there. The phone has its
    /// own first run (Phone setup goes first until a phone is seen), so it isn't asked here.
    /// </summary>
    public static List<string> StartProblems(bool steam, bool cs2)
    {
        var p = new List<string>();
        if (!steam) p.Add(Loc.T("Steam wasn't found on this PC. Accounts, Join and Rejoin need it: install Steam, log in, then open the app again."));
        else if (!cs2) p.Add(Loc.T("CS2 wasn't found in your Steam libraries. Install it in Steam. If it's on a drive Steam doesn't list, open CS2 once while the app is open and the app finds it."));
        return p;
    }

    async void FirstStartCheck()
    {
        var steam = Steam.InstallDir() != null;
        var saved = _s.Cs2Path;
        var found = steam ? await Task.Run(() => Steam.FindCs2(saved)) : null;
        if (NoteCs2(_s, found)) SaveSettings();
        var cs2 = found != null;
        var p = StartProblems(steam, cs2);
        if (p.Count == 0) { Log.Ok("First start: Steam and CS2 found."); return; }
        Log.Warn("First start: " + string.Join(" ", p.Select(Loc.En)));
        await Ask(Loc.T("Before you start"), string.Join("\n\n", p) + "\n\n" + Loc.T("The phone comes next: Phone setup on the Connection page walks you through it."), Loc.T("OK"), null);
    }

    async void AdbInstall_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Ask(Loc.T("Install phone tool (ADB)"), Loc.T("Download Google's official platform-tools (about 7 MB) from dl.google.com into the app's data folder? You only need to do this once.") + MobileDataNote(), Loc.T("Download"));
        if (!ok) return;
        await InstallAdb(t => ProgressText.Text = t, showProgress: true);
    }

    /// <summary>Downloads ADB (you pressed Install, or opened Phone setup without it). True when it's installed.</summary>
    async Task<bool> InstallAdb(Action<string> report, bool showProgress, Action<double> percent = null)
    {
        AdbInstallBtn.IsEnabled = false;
        _adbError = null;
        if (showProgress) ShowProgress(Loc.T("Downloading the phone tool (ADB) from Google…"));
        try
        {
            _adb = await Adb.Download(new Progress<string>(report), percent == null ? null : new Progress<double>(percent));
            if (_adb != null) { Log.Ok("ADB installed at " + _adb); Toast(Loc.T(showProgress ? "ADB installed." : Texts.AdbInstalledToast), true); }
            else { _adbError = Loc.T("adb.exe was not found in it"); Toast(Loc.T("Downloaded, but adb.exe was not found."), Tone.Error); }
        }
        catch (Exception ex)
        {
            _adbError = AdbErrorText(ex);
            Log.Error("ADB download: " + ex.Message);
            if (showProgress) Toast(Loc.T("Couldn't download ADB:") + " " + ex.Message, Tone.Error);
        }
        finally { if (showProgress) HideProgress(); AdbInstallBtn.IsEnabled = true; await RefreshPhone(); }
        return _adb != null;
    }

    /// <summary>Why the last ADB download failed, short (Phone setup shows it in the row with Try again).</summary>
    string _adbError;

    /// <summary>The download's failure in a few words: no internet, too slow, or what the server said.</summary>
    public static string AdbErrorText(Exception ex) => ex switch
    {
        TaskCanceledException => Loc.T("it took too long"),
        System.Net.Http.HttpRequestException { StatusCode: { } code } => Loc.F("Google answered {0}", (int)code),
        System.Net.Http.HttpRequestException => Loc.T("no connection to dl.google.com"),
        IOException => Loc.T("the file couldn't be saved"),
        _ => ex.GetType().Name,
    };

    /// <summary>Phone setup's own download of ADB: in the row, with the MB and a bar (10 Nov: one new user left Phone setup here twice).</summary>
    async Task SetupInstallAdb(bool auto)
    {
        if (_setupBusy != null || _adb != null) return;
        _setupBusy = PhoneStep.NoAdbTool;
        _setupPercent = 0;
        // The dashboard's "pressed Install" (Setup_ clicks): since 10 Nov also when Phone setup starts the download by itself
        Telemetry.Click("Setup_" + PhoneSetup.Key(PhoneStep.NoAdbTool));
        if (auto) { Telemetry.Count("adb_auto"); Log.Info("Phone setup: downloading the phone tool (ADB) from Google."); }
        void Busy(string text) { if (text != null) _setupBusyText = text; if (_setupState != null && SetupPane.Visibility == Visibility.Visible) ShowSetup(_setupState); }
        bool ok;
        try
        {
            Busy(Texts.AdbDownloading(0, null));
            ok = await InstallAdb(Busy, showProgress: false, percent: p => { _setupPercent = p; Busy(null); });
        }
        finally { _setupBusy = null; _setupBusyText = null; _setupPercent = null; }
        _adbFailed = ok ? null : _adbError ?? Loc.T("unknown error");
        Telemetry.Count(ok ? "adb_ok" : "adb_fail");
        await ReadSetupNow();
    }

    /// <summary>The last ADB download from Phone setup failed (the row says why and offers Try again); null after one that worked.</summary>
    string _adbFailed;
    double? _setupPercent;

    async void Tether_Click(object sender, RoutedEventArgs e)
    {
        if (_phone?.Ready != true) { Toast(Loc.T("The phone has to be ready on ADB first."), false); return; }
        if (_demo) { Toast(Loc.F("USB tethering is on: Windows sees the phone as {0}.", "Ethernet 2"), true); return; }
        TetherBtn.IsEnabled = false;
        ShowProgress(Loc.T("Turning on USB tethering…"));
        try
        {
            var (adapter, why) = await TurnOnTethering(t => ProgressText.Text = t);
            if (adapter != null) Toast(Loc.F("USB tethering is on: Windows sees the phone as {0}.", adapter.Name), true);
            else Toast(why, false);
        }
        finally { HideProgress(); TetherBtn.IsEnabled = true; }
    }

    /// <summary>
    /// Asks the phone for USB tethering (rndis) and waits up to 30 s for Windows to add the phone's adapter. Returns that adapter,
    /// or null with what to do by hand. ADB stays connected on the phones tried (Android 15); if it drops, it comes back on its own.
    /// </summary>
    async Task<(Adapter Adapter, string Why)> TurnOnTethering(Action<string> report)
    {
        // Asking again while it's on resets the phone's USB link and drops the connection for up to a minute: only ask when Windows
        // doesn't see it. The phone has to say tethering is on too, so a USB modem or another phone isn't taken for this one.
        if (Net.PickEgress(await Task.Run(Net.Scan), null, null) is Adapter on && await Adb.TetheringOn(_adb, _phone.Serial))
        {
            Log.Info($"USB tethering is already on (Windows sees the phone as {on.Name}).");
            return (on, null);
        }
        // Changing the USB mode resets the phone's USB link, so adb often reports an error even when it worked (Android 15):
        // what counts is Windows seeing the phone below, not adb's answer.
        _tetherAskedAt = DateTime.UtcNow;
        var r = await Adb.EnableUsbTether(_adb, _phone.Serial);
        Log.Info("USB tethering request: " + (r.Ok ? "sent" : "sent (adb dropped while the phone switched its USB mode)"));
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TetherWait)
        {
            await Task.Delay(1000);
            if (CommandCancelled) return (null, Loc.T("Cancelled."));
            report?.Invoke(Loc.F("Turning on USB tethering: waiting for Windows to see the phone ({0} s)…", (int)sw.Elapsed.TotalSeconds));
            var phone = Net.PickEgress(await Task.Run(Net.Scan), null, null);
            if (phone != null)
            {
                Log.Ok($"USB tethering is on: Windows sees the phone as {phone.Name} after {(int)sw.Elapsed.TotalSeconds} s.");
                await SyncNow(force: true);
                _ = RefreshPublicIps();
                return (phone, null);
            }
        }
        Log.Warn($"USB tethering: the phone took the request, but Windows didn't add its adapter within {TetherWait.TotalSeconds:0} s.");
        await SyncNow(force: true);
        return (null, Loc.F("Windows didn't see the phone's tethering within {0} s.", TetherWait.TotalSeconds.ToString("0")) + " " + PhoneSetup.Advice(PhoneStep.TetherOff));
    }

    /// <summary>Windows can take 20 s or more to add the phone's adapter after the USB mode changes.</summary>
    public static readonly TimeSpan TetherWait = TimeSpan.FromSeconds(30);
    DateTime _tetherAskedAt = DateTime.MinValue;

    /// <summary>
    /// "Tethering on, but Windows doesn't see it" is only said when it lasts: the phone has been missing for longer than Windows
    /// may take to add it, counted from the first check that missed it or from the app's last request, whichever came later.
    /// Tethering turned on in the phone's own settings, or a cable plugged back in, gets the same time as a request.
    /// </summary>
    public static bool TetherHintDue(DateTime askedAt, DateTime missingSince, DateTime now) =>
        now - (askedAt > missingSince ? askedAt : missingSince) > TetherWait;
    DateTime? _tetherMissingSince;

    // ------------------------------------------------------------ Phone setup

    async void PhoneHelp_Click(object sender, RoutedEventArgs e) => await RunPhoneSetup();

    /// <summary>Demo only (development build): the panel with made-up steps.</summary>
    partial void DemoPhoneSetup();

    // ------------------------------------------------------------ The Phone setup panel (beta.10): steps that check themselves

    readonly System.Collections.ObjectModel.ObservableCollection<SetupStepRow> _setupRows = new();
    DispatcherTimer _setupTimer;
    PhoneSetupState _setupState;
    PhoneStep? _setupBusy;
    string _setupBusyText;
    bool _setupReading;
    string _setupFirstTodo;

    /// <summary>Opens the panel: every 2 s it reads the phone again and redraws the steps, until you close it.</summary>
    async Task RunPhoneSetup()
    {
        SetupSteps.ItemsSource = _setupRows;
        SetupPane.Visibility = Visibility.Visible;
        if (_demo) { DemoPhoneSetup(); return; }
        if (_setupTimer?.IsEnabled == true) return;
        Log.Info("Phone setup opened.");
        _setupFirstTodo = _setupLeft = _setupStepKey = null;
        _setupTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _setupTimer.Tick -= SetupTick;
        _setupTimer.Tick += SetupTick;
        _setupTimer.Start();
        await ReadSetupNow();
        // No phone tool: the download starts with the panel (10 Nov), unless the PC is on mobile data (then the row waits for Install)
        // or the last one failed (Try again). Opening Phone setup is the yes: it can't do anything without it.
        if (AutoInstallAdb(_adb != null, _adbFailed != null, MobileDataNote().Length > 0, _setupState?.Iphone == true)) await SetupInstallAdb(auto: true);
    }

    /// <summary>Phone setup downloads the phone tool by itself: missing, not failed already, not on mobile data, not for an iPhone (no ADB there).</summary>
    public static bool AutoInstallAdb(bool adbThere, bool failedBefore, bool onMobileData, bool iphone) => !adbThere && !failedBefore && !onMobileData && !iphone;

    string _setupStepKey;
    DateTime _setupStepSince = DateTime.UtcNow;
    /// <summary>Demo only: how long the step to do now has been on screen, to capture the hints that come after a while.</summary>
    partial void DemoSetupSeconds(ref int seconds);

    async void SetupTick(object sender, EventArgs e) => await ReadSetupNow();

    async Task ReadSetupNow()
    {
        if (_setupReading || _setupBusy != null) return;
        _setupReading = true;
        try { ShowSetup(await ReadSetupState()); }
        catch (Exception ex) { Log.Warn("Phone setup: " + ex.Message); }
        finally { _setupReading = false; }
    }

    /// <summary>Draws the steps from what was read. The summary says what to do now, or that the phone is ready.</summary>
    void ShowSetup(PhoneSetupState st)
    {
        _setupState = st;
        var samsung = st.Usb.Vendor == "Samsung" || _phone?.Model?.StartsWith("SM-", StringComparison.OrdinalIgnoreCase) == true;
        var steps = PhoneSetup.Steps(st, samsung, _phone?.Model, _s.EgressSsid);
        var now = PhoneSetup.Current(steps);
        // The usage counts: where it stood when you opened it, and where it is now (counted when you close it, beta.11)
        _setupFirstTodo ??= PhoneSetup.StepKey(steps);
        _setupLeft = PhoneSetup.StepKey(steps);
        // How long the step to do now has been the one (the cable and Allow steps add what to try after a while), and USB debugging
        // turning on is said out loud: the person is looking at the phone, not at the PC (10 Nov)
        var key = PhoneSetup.StepKey(steps);
        if (key != _setupStepKey)
        {
            if (!_demo && PhoneSetup.DebugJustOn(_setupStepKey, key)) { Log.Ok("USB debugging is on."); Toast(Texts.DebugSeenToast, true); }
            _setupStepKey = key;
            _setupStepSince = DateTime.UtcNow;
        }
        var seconds = (int)(DateTime.UtcNow - _setupStepSince).TotalSeconds;
        DemoSetupSeconds(ref seconds);
        _setupRows.Clear();
        foreach (var i in steps)
            _setupRows.Add(new SetupStepRow(i, now, _setupBusy == i.Step ? _setupBusyText : null, _setupBusy == null,
                i == now ? PhoneSetup.LiveDetail(i, seconds) : null, _setupBusy == i.Step ? _setupPercent : null));
        ShowGuide(now?.Step == PhoneStep.NoUsbDebugging && _setupBusy == null ? st : null);
        var ok = steps.Count(i => i.State == PhoneSetup.StepState.Ok);
        SetupSummary.Text = now == null && steps.All(i => i.State == PhoneSetup.StepState.Ok)
            ? SetupReadyText(_protOn)
            : now == null ? Loc.F("{0} of {1} done.", ok, steps.Count) : Loc.F("{0} of {1} done. Next: {2}.", ok, steps.Count, now.Title);
        SetupFoot.Text = _s.NoPhone ? Loc.T("You picked No phone under What goes through the phone, so the app isn't using the phone right now.")
            : now == null ? Loc.T("For less lag in game:") + " " + NetReport.Tips(_last?.Egress?.IsWifi == true)
            : Loc.T("Updates by itself. Do the red step and it turns green.");
        SetupCloseBtn.Content = Loc.T(now == null ? "Done" : "Close");
        SetupCloseBtn.Style = (Style)FindResource(now == null ? "Primary" : "Btn");
    }

    /// <summary>Phone setup with every step done. With protection off servers still see the home IP, so it says what is left.</summary>
    public static string SetupReadyText(bool protOn) => protOn
        ? Loc.T("The phone is ready. The app checks its IP, then servers see it instead of your home IP.")
        : Loc.T("The phone is ready. Turn on protection so servers see its IP instead of your home IP.");

    void SetupClose_Click(object sender, RoutedEventArgs e)
    {
        SetupPane.Visibility = Visibility.Collapsed;
        _setupTimer?.Stop();
        if (_demo || _setupFirstTodo == null) return;
        CountSetupClosed();
        Log.Info("Phone setup closed" + (PhoneSetup.Current(PhoneSetup.Steps(_setupState ?? new PhoneSetupState())) is { } left ? " at " + PhoneSetup.Key(left.Step) + "." : ": the phone is ready."));
        _ = RefreshPhone();
    }

    string _setupLeft;

    /// <summary>
    /// Counts this opening of Phone setup once: the step when it opened and the step when it closed. Also at app exit with the panel
    /// still open (Telemetry.AtExit), which before lost the count.
    /// </summary>
    void CountSetupClosed()
    {
        if (_demo) return;
        foreach (var n in PhoneSetup.ClosedCounts(_setupFirstTodo, _setupLeft)) Telemetry.Count(n);
        _setupFirstTodo = _setupLeft = null;
    }

    /// <summary>The button of a step: the app does it, the row says what is happening, then the steps are read again.</summary>
    async void SetupAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SetupStepRow row || _setupBusy != null) return;
        if (_demo) { Toast("Demo: " + row.Action + ".", true); return; }   // demo only: stays in English
        var step = row.Item.Step;
        // Install (on mobile data) and Try again: the download in the row, no second question (pressing it is the yes)
        if (step == PhoneStep.NoAdbTool) { await SetupInstallAdb(auto: false); return; }
        Telemetry.Click("Setup_" + PhoneSetup.Key(step));
        string Busy(string text) { _setupBusyText = text; if (_setupState != null) ShowSetup(_setupState); return text; }
        _setupBusy = step;
        string why = null;
        try
        {
            switch (step)
            {
                case PhoneStep.UsbDriverError:
                    // Only the iPhone's rows have this button: Apple Devices brings its network driver (9 nov.)
                    Busy(Loc.T("Opening Apple Devices in the Microsoft Store…"));
                    try
                    {
                        Process.Start(new ProcessStartInfo(PhoneSetup.AppleDevicesStoreUri) { UseShellExecute = true });
                        Log.Info("Phone setup opened Apple Devices in the Microsoft Store, for the iPhone's network driver.");
                        Toast(Loc.T("In the Microsoft Store, press Get. When it's installed, unplug the iPhone and plug it in again."), true);
                    }
                    catch (Exception ex) { Log.Warn("Couldn't open the Microsoft Store: " + ex.Message); why = Loc.T("Couldn't open the Microsoft Store. Open it and install \"Apple Devices\" (by Apple)."); }
                    break;
                case PhoneStep.AdbFailed:
                    Busy(Loc.T("Starting the phone tool again…"));
                    _adb = Adb.Find();
                    break;
                case PhoneStep.TetherOff when _setupState?.Offer is { CanUse: true } offer:
                    Busy(Loc.T(offer.Ssid != null ? "Using the phone's hotspot…" : "Using the phone over USB…"));
                    await UseOffer(offer);
                    await Task.Delay(1500);
                    break;
                case PhoneStep.TetherOff:
                    Busy(Loc.T("Turning on USB tethering…"));
                    var (adapter, w) = await TurnOnTethering(t => Busy(t));
                    if (adapter == null) why = w;
                    break;
                case PhoneStep.WifiOn:
                    Busy(Loc.T("Turning off the phone's Wi-Fi…"));
                    await TurnPhoneWifiOff();
                    await Task.Delay(3000);
                    await RefreshPhone();
                    break;
                case PhoneStep.PcOnPhone:
                    Busy(Loc.T("Keeping the rest of the PC on your home network…"));
                    if (!_s.KeepPcOffPhone)
                    {
                        _s.KeepPcOffPhone = true;
                        _uiLock = true;
                        try { KeepOffBox.IsChecked = true; } finally { _uiLock = false; }
                        SaveSettings();
                        Log.Info("Phone setup turned on Save mobile data, so the rest of the PC stays on the home network.");
                    }
                    await SyncNow(force: true);
                    await Task.Delay(1500);
                    break;
            }
        }
        catch (Exception ex) { Log.Error("Phone setup: " + ex.Message); why = Loc.T("Something failed:") + " " + ex.Message; }
        finally { _setupBusy = null; _setupBusyText = null; }
        if (why != null) Toast(why, false);
        await ReadSetupNow();
    }

    async Task<PhoneSetupState> ReadSetupState()
    {
        _adb ??= Adb.Find();
        var info = _adb == null ? null : Adb.SteadyWifi(await Adb.Status(_adb), _phone, ref _wifiOnReads);
        if (info != null) _phone = info;
        var usb = info?.Ready == true ? PhoneUsbState.None : await Task.Run(PhoneUsb.Scan);
        // The phone's own addresses first: an adapter Windows names in a way the app doesn't know is still found by its gateway
        var phoneIps = info?.Ready == true ? await ReadPhoneAddresses(info.Serial) : null;
        var adapters = await Task.Run(Net.Scan);
        var phone = Net.PickEgress(adapters, _s.EgressAdapterId, _s.EgressSsid);
        var hotspot = PhoneLink.IsHotspot(_s.EgressSsid, phone?.IsWifi);
        var other = phone != null && PhoneLink.HasHome(adapters, phone);
        var mode = PhoneLink.EffectiveMode(_protOn, _s.Mode, _s.GameMode, Steam.Cs2Running);
        var iphone = NotePhoneKind(PhoneUsb.IphoneSeen(info?.Ready == true, usb, phone, _s.EgressSsid));
        return new PhoneSetupState
        {
            AdbTool = _adb != null,
            AdbState = _adb == null ? "none" : info?.State,
            Usb = usb,
            Hotspot = hotspot,
            Tethered = phone != null && !hotspot,
            WifiOn = PhoneLink.WifiOn(info, phone?.IsWifi),
            PcOnPhone = Guard.NeedsPcOffPhone(mode, true, Net.BestInterface(Net.PcProbeIp), phone?.Index ?? -1, other),
            Iphone = iphone,
            AppleDriver = !iphone || phone != null || await Task.Run(PhoneUsb.AppleNetDriver),
            Offer = iphone ? null : ConnectionOffer.Find(adapters, phoneIps, _s.EgressAdapterId, !string.IsNullOrEmpty(_s.EgressSsid), phone),
            AdbFailed = _adb == null ? _adbFailed : null,
            AdbWaits = _adb == null && MobileDataNote().Length > 0,
        };
    }

    List<Adb.PhoneAddress> _phoneAddrs;
    DateTime _phoneAddrsAt = DateTime.MinValue;

    /// <summary>
    /// The phone's sharing addresses over ADB, read again at most every 5 s; the USB ones go to Net.Scan, so the phone's adapter is
    /// found whatever Windows calls it.
    /// </summary>
    async Task<List<Adb.PhoneAddress>> ReadPhoneAddresses(string serial)
    {
        if (_adb == null || serial == null) return _phoneAddrs;
        if (_phoneAddrs != null && DateTime.UtcNow - _phoneAddrsAt < TimeSpan.FromSeconds(5)) return _phoneAddrs;
        _phoneAddrsAt = DateTime.UtcNow;
        try
        {
            var list = await Adb.SharingAddresses(_adb, serial);
            var usb = list.Where(a => !a.Wifi).Select(a => a.Ip).ToList();
            if (!usb.ToHashSet().SetEquals(Net.PhoneUsbIps)) Net.SetPhoneUsbIps(usb);
            _phoneAddrs = list;
        }
        catch (Exception ex) { Log.Warn("Could not read the phone's addresses: " + ex.Message); }
        return _phoneAddrs;
    }

    /// <summary>Phone setup's "Use it": the connection the PC already has from the phone (its hotspot, or the USB cable when pinned elsewhere).</summary>
    async Task UseOffer(ConnectionOffer offer)
    {
        _s.EgressAdapterId = offer.AdapterId;
        _s.EgressSsid = offer.Ssid;
        _distrust = false;
        SaveSettings();
        EgressCombo.Tag = null;
        Log.Info(offer.Ssid != null ? $"Phone connection: hotspot \"{offer.Ssid}\" (Phone setup saw the PC on it)." : "Phone connection: phone over USB (automatic).");
        await SyncNow(force: true);
        _ = RefreshPublicIps();
    }

    async void ChangeIp_Click(object sender, RoutedEventArgs e) => await RunCommand("Change IP", "starting…", () => ChangeIp(confirm: true));

    /// <summary>The carrier kept the same IP. Airplane mode is suggested only where it would be new: not already the method, never on the hotspot.</summary>
    public static string SameIpToast(bool airplane, bool hotspot) =>
        Loc.T("The carrier kept giving the same IP.") + " " + (airplane || hotspot ? Loc.T("Wait a few minutes and try again.") : Loc.T("Try Airplane mode in Settings > Change IP > Method, or wait a few minutes."));

    /// <summary>Cycles the phone's data until the carrier gives a new IP. True if the IP really changed (and isn't the home IP).</summary>
    async Task<bool> ChangeIp(bool confirm)
    {
        if (_changingIp) return false;
        if (_demo) { Toast("Demo: IP changed.", true); return false; }   // demo only: stays in English
        if (_s.NoPhone) { Toast(Loc.T("No phone mode: there is no IP to change. Pick another option under What goes through the phone."), false); return false; }
        // beta.11: an iPhone has no ADB: the change is guided (you toggle Airplane Mode, the app waits and measures)
        if (_iphone && _phone?.Ready != true) return await IphoneChangeIp();
        // USB tethering, or the phone's hotspot (saved by name) with the cable plugged in only for ADB.
        // With protection off there is no egress in the report: the saved hotspot name decides, as Phone setup does
        var hotspot = PhoneLink.IsHotspot(_s.EgressSsid, PhoneLink.Exit(_last)?.IsWifi);
        if (_last?.Egress != null && !_last.Egress.IsPhone && !hotspot)
        {
            Toast(Loc.T("The chosen connection is not a phone: the Change IP button only works with a phone over ADB (USB tethering, or its hotspot with the cable plugged in)."), false);
            return false;
        }
        if (_adb == null || _phone?.Ready != true)
        {
            Toast(_adb == null ? Loc.T("Install the phone tool first (button under Phone).") : Loc.T("The phone is not ready on ADB. See \"Phone setup\"."), false);
            return false;
        }
        // With protection off CS2 is on the home connection and doesn't drop, unless the whole PC is on the phone
        if (confirm && Steam.Cs2Running && IpChangeDrops && !await Ask(Loc.T("CS2 is open"), Loc.T("If you are on a server, you will be disconnected when the IP changes. Continue?"), Loc.T("Change IP"))) return false;

        _changingIp = true;
        _ipChangeQuiet = true;   // until the new IP is checked and synced (finally): the drop is on purpose
        // Protection off: the phone's IP changes all the same (servers see it once protection is on). The new IP isn't known yet: untrusted (servers and CS2 blocked) until it is measured again.
        _verifiedKey = null;
        await SyncNow(force: true);
        ChangeIpBtn.IsEnabled = false;
        RejoinBtn.IsEnabled = false;
        ChangeIpBtn.Content = Loc.T("Changing…");
        var method = _s.ChangeMethod == "airplane" ? Adb.Method.Airplane : Adb.Method.MobileData;
        var first = _egressPublic;
        string got = null;
        var goodRetries = 0;
        var tries = 0;
        QualityLevel? measured = null;
        var took = Stopwatch.StartNew();   // for the stats: how long, how many attempts, the quality (Telemetry.IpChange)
        _ipChangedAt = DateTime.Now.AddTicks(-(DateTime.Now.Ticks % TimeSpan.TicksPerSecond));
        Diag.Write("change-ip", new { from = first });
        try
        {
            // On the hotspot the phone's Wi-Fi is left alone: on some phones turning it off also stops the hotspot.
            // Not PhoneLink.WifiOn on purpose: Wi-Fi on with no network also goes off, or the phone may join the home Wi-Fi while its data is off.
            if (_phone.WifiOn && !hotspot)
            {
                ShowProgress(Loc.T("The phone's Wi-Fi is on: turning it off to use mobile data…"));
                await TurnPhoneWifiOff();
                await Task.Delay(3000);
            }
            // A connection too jittery to keep adds an attempt (Quality.GoodRetries at most), so it never uses up the attempts for a new IP
            var max = Math.Max(1, _s.MaxAttempts);
            var afterBadLink = false;   // the last attempt's new IP was too jittery to keep: this one looks for another cell
            for (var attempt = 1; attempt <= max + goodRetries; attempt++)
            {
                var of = max + goodRetries;
                tries = attempt;
                var how = Quality.MethodFor(method, afterBadLink, hotspot);
                afterBadLink = false;
                ShowProgress(how != method ? Loc.F("Attempt {0}/{1}: airplane mode for {2} s, so the phone looks for another cell…", attempt, of, _s.OffSeconds)
                    : Loc.F("Attempt {0}/{1}: phone data off for {2} s…", attempt, of, _s.OffSeconds));
                // On the hotspot airplane mode is never used, not even as the fallback where "svc data" is missing: it drops the hotspot
                var (err, used) = await Adb.Cycle(_adb, _phone.Serial, how, Math.Clamp(_s.OffSeconds, 1, 60), allowAirplane: !hotspot);
                // A phone that refuses airplane mode was left as it was: the retry goes the usual way
                if (err != null && how != method && err.StartsWith(Adb.Refused))
                {
                    Log.Info("The phone refused airplane mode: trying again with " + (method == Adb.Method.Airplane ? "airplane mode." : "mobile data off and on."));
                    how = method;
                    (err, used) = await Adb.Cycle(_adb, _phone.Serial, how, Math.Clamp(_s.OffSeconds, 1, 60), allowAirplane: !hotspot);
                }
                how = used;   // the method the phone really went through (a fallback to airplane mode too), for the tethering check below
                if (err != null) { Log.Error(err); Toast(CycleErrorText(err), Tone.Error); return false; }

                ShowProgress(Loc.F("Attempt {0}/{1}: waiting for the mobile network…", attempt, of));
                got = await WaitForEgressIp(TimeSpan.FromSeconds(50));
                // Some phones drop USB tethering in airplane mode: turned back on once (only asked when Windows no longer sees the phone).
                if (got == null && how == Adb.Method.Airplane && !hotspot)
                {
                    var (back, _) = await TurnOnTethering(ShowProgress);
                    if (back != null) got = await WaitForEgressIp(TimeSpan.FromSeconds(30));
                }
                if (got == null) { ShowProgress(Loc.T("The network hasn't come back yet…")); continue; }
                if (got == first) { Log.Info($"The carrier gave back the same IP ({got})."); continue; }
                // Many admins ban the whole range: optionally keep going until the /16 is new, against every earlier IP
                // (the same list the range note after the change uses).
                if (_s.RequireNew16 && attempt < of && IpRange.FirstSame16(got, _history.Select(h => h.Ip).Append(first).Where(x => x != null)) is string seen)
                {
                    Log.Info($"New IP {got} is in the same range as {seen}: trying again.");
                    continue;
                }
                // On mobile data the lag is upload jitter, and another IP can land on another cell: measured like the Quality check
                if (_s.RequireGoodLink && await MeasureNewLink(got, attempt, of) is LinkQuality q)
                {
                    measured = q.Level;
                    if (Quality.ChangeAgain(q, goodRetries))
                    {
                        goodRetries++;
                        afterBadLink = true;
                        Log.Info($"New IP {got}: {q.Text}. Jitter of {Quality.GoodJitterMs} ms or more, or 2 of 10 pings lost: trying again ({goodRetries}/{Quality.GoodRetries})"
                            + (hotspot ? "." : " with airplane mode, so the phone looks for another cell."));
                        continue;
                    }
                    Log.Info($"New IP {got}: {q.Text}.");
                }
                break;
            }

            if (got != null) ResetSteamAfterIpChange();
            if (got != null && got != first && got != (_homePublic ?? _s.LastHomeIp))
                Telemetry.IpChange(method == Adb.Method.Airplane, took.Elapsed, tries, measured, goodRetries);
            Diag.Write("ip-changed", new { from = first, to = got });
            var home = _homePublic ?? _s.LastHomeIp;
            if (got != null && got == home)
            {
                _distrust = true;
                Telemetry.Count("ip_home_back");
                Toast(HomeBackText(_protOn), Tone.Error);
            }
            else if (got != null && first == null)
            {
                var range = RangeNote(got, out var sameRange);
                _egressPublic = got;
                _egressInfo = await PhoneIpInfo(got);
                AddHistory(got, _egressInfo, "trocado", _ipChangedAt);
                ResetQuality();
                Log.Ok($"Mobile data back on: {got} (the IP before wasn't known, so it can't be compared). {Loc.En(range)}");
                Toast(Loc.F("New IP: {0}.", got) + " " + range + (_protOn ? "" : " " + Loc.T("Protection is off: turn it on so servers see this IP.")), !sameRange);
                return true;
            }
            else if (got != null && got != first)
            {
                var range = RangeNote(got, out var sameRange);
                _egressPublic = got;
                _egressInfo = await PhoneIpInfo(got);
                AddHistory(got, _egressInfo, "trocado", _ipChangedAt);
                ResetQuality();
                if (sameRange) Log.Warn($"IP changed: {first} → {got}. {Loc.En(range)}");
                else Log.Ok($"IP changed: {first} → {got}. {Loc.En(range)}");
                Toast(Loc.F("New IP: {0}.", got) + " " + range + (_protOn ? "" : " " + Loc.T("Protection is off: turn it on so servers see this IP.")), !sameRange);
                return true;
            }
            else if (got != null && got == first)
            {
                Telemetry.Count("ip_same");
                Toast(SameIpToast(method == Adb.Method.Airplane, hotspot), false);
            }
            else
            {
                Telemetry.Count("ip_no_network");
                Toast(Loc.T("The mobile network didn't come back in time. Check the phone."), false);
            }
            return false;
        }
        finally
        {
            _changingIp = false;
            ChangeIpBtn.Content = Loc.T("Change IP");
            UpdatePhoneCheck();
            HideProgress();
            ShowIps();
            try
            {
                await SyncNow(force: true);
                // Measures the new connection: only a confirmed IP that isn't the home one lifts the block.
                await RefreshPublicIps();
                await SyncNow();   // waits for the sync that trust started, so no report from before it raises the alarm
            }
            finally { _ipChangeQuiet = false; }
        }
    }

    /// <summary>The English error of Adb.Cycle, in the app's language for the toast (the Log keeps the English).</summary>
    static string CycleErrorText(string err)
    {
        const string back = "Turned data off but could not turn it back on: ", hand = " (turn it on manually on the phone)";
        if (err.StartsWith(Adb.Refused)) return Loc.T("The phone refused the command:") + " " + err[Adb.Refused.Length..];
        if (err.StartsWith(back) && err.EndsWith(hand)) return Loc.F("Turned data off but could not turn it back on: {0} (turn it on manually on the phone)", err[back.Length..^hand.Length]);
        return err;
    }

    /// <summary>Change IP came back with the home IP: the phone shares the home Wi-Fi. Only protection on blocks your servers then.</summary>
    public static string HomeBackText(bool protOn) => protOn
        ? Loc.T("The phone came back with your home IP. Your servers stay blocked: turn off the phone's Wi-Fi.")
        : Loc.T("The phone came back with your home IP: it is sharing your Wi-Fi. Turn off the phone's Wi-Fi.");

    /// <summary>
    /// "Keep changing until the connection is good": 10 pings to the IP test address, only through the phone. Null when the test
    /// route isn't on the phone (the home connection would be measured instead), so nothing is decided on it.
    /// </summary>
    async Task<LinkQuality> MeasureNewLink(string ip, int attempt, int of)
    {
        ShowProgress(Loc.F("Attempt {0}/{1}: checking the connection on {2}…", attempt, of, ip));
        await Task.Delay(3000);   // the first seconds after data comes back are always rough
        var egress = Net.PickEgress(await Task.Run(Net.Scan), _s.EgressAdapterId, _s.EgressSsid);
        if (egress != null && Net.BestInterface(Net.EchoIp) != egress.Index) await SyncNow(force: true);
        if (egress == null || Net.BestInterface(Net.EchoIp) != egress.Index)
        {
            Log.Info($"New IP {ip}: the connection check was skipped (the test route isn't on the phone right now).");
            return null;
        }
        return await Quality.Measure(Net.EchoIp, _phone?.Ready == true ? _phone.NetType : null, count: 10);
    }

    /// <summary>Whether the new IP's /16 shows up in the previous IPs (call before adding it to the history).</summary>
    string RangeNote(string ip, out bool same)
    {
        same = false;
        var r = IpRange.Slash16(ip);
        if (r == null) return "";
        var seen = IpRange.FirstSame16(ip, _history.Select(h => h.Ip));
        same = seen != null;
        return !same ? Loc.F("New range ({0}.x.x).", r)
            : Loc.F("Same range ({0}.x.x) as {1}.", r, seen) + " " + (_s.RequireNew16 ? Loc.T("If the server banned that range, change again.")
                : Loc.T("If the server banned that range, change again or turn on \"Keep changing until the IP is in a new range\" in Settings."));
    }

    /// <summary>The new IP's ISP and country, asked through the phone and never through home (see Net.Lookup).</summary>
    async Task<Net.IpInfo> PhoneIpInfo(string ip)
    {
        var egress = Net.PickEgress(await Task.Run(Net.Scan), _s.EgressAdapterId, _s.EgressSsid);
        return await Net.Lookup(ip, egress?.Index ?? -1, egress?.LocalIp);
    }

    async Task<string> WaitForEgressIp(TimeSpan max)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(2500);
        while (sw.Elapsed < max)
        {
            var adapters = await Task.Run(Net.Scan);
            var egress = Net.PickEgress(adapters, _s.EgressAdapterId, _s.EgressSsid);
            if (egress != null)
            {
                // If the cable came back with another adapter, the IP-test route has to be rebuilt before asking.

                if (Net.BestInterface(Net.EchoIp) != egress.Index) await SyncNow(force: true);
                try
                {
                    using var cts = new CancellationTokenSource(6000);
                    var ip = await Net.EgressPublicIp(egress, cts.Token);
                    if (ip != null) return ip;
                }
                catch { }
            }
            await Task.Delay(2000);
        }
        return null;
    }
}

/// <summary>One line of the Phone setup panel: green done, red to do (the current one stands out), grey waiting for the one before.</summary>
public sealed class SetupStepRow
{
    /// <param name="live">The step to do now as it reads while it lasts (PhoneSetup.LiveDetail); null = its usual text.</param>
    /// <param name="percent">The download's progress (0 to 100) while the row's step runs one; null = no bar.</param>
    public SetupStepRow(PhoneSetup.SetupItem item, PhoneSetup.SetupItem now, string busy, bool idle, string live = null, double? percent = null)
    {
        Item = item; Current = item == now; Busy = busy; Idle = idle; Percent = percent;
        // beta.11: only the step to do now is open; the ones after it are only their title, in grey, until it is their turn
        _later = now != null && !Current && item.State != PhoneSetup.StepState.Ok;
        _shown = Current && live != null ? live : PhoneSetup.Shown(item, now);
    }
    public double? Percent { get; }
    public double PercentValue => Percent ?? 0;
    public Visibility PercentVisibility => Percent == null ? Visibility.Collapsed : Visibility.Visible;
    readonly bool _later;
    readonly string _shown;
    /// <summary>After the step to do now: grey, title only, no button.</summary>
    public bool Later => _later;
    public PhoneSetup.SetupItem Item { get; }
    public bool Current { get; }
    public string Busy { get; }
    public bool Idle { get; }
    public string Title => Item.Title;
    public string Detail => Busy ?? _shown;
    public Visibility DetailVisibility => Detail == null ? Visibility.Collapsed : Visibility.Visible;
    public string Action => Item.Action;
    public bool HasAction => Item.Action != null && Item.State == PhoneSetup.StepState.Todo && !_later;
    public Brush Brush => _later ? Ui.Faint : Item.State switch { PhoneSetup.StepState.Ok => Ui.Good, PhoneSetup.StepState.Todo => Busy != null ? Ui.Warn : Ui.Bad, _ => Ui.Faint };
    public Brush TitleBrush => _later || Item.State == PhoneSetup.StepState.Waiting ? Ui.Faint : Ui.Text;
    public Brush DetailBrush => Busy != null ? Ui.Warn : Current ? Ui.Text : Ui.Faint;
    /// <summary>The step to do now gets a faint red band, so it is the first thing you see.</summary>
    public Brush RowBack => Current ? Ui.BadSoft : Brushes.Transparent;
}
