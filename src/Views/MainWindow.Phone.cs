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

    async Task RefreshPhoneCore()
    {
        _adb ??= Adb.Find();
        if (_adb == null)
        {
            SetPhone(Ui.Faint, "Phone tool (ADB) not installed", "Needed for the Change IP button. Free, from Google, about 7 MB.");
            AdbInstallBtn.Visibility = Visibility.Visible;
            return;
        }
        AdbInstallBtn.Visibility = Visibility.Collapsed;
        // One stray Wi-Fi answer (an adb error, a timeout) neither flips the block nor lifts it: see Adb.SteadyWifi.
        var info = Adb.SteadyWifi(await Adb.Status(_adb), _phone, ref _wifiOnReads);
        var wifiBefore = PhoneWifiOn;
        _phone = info;
        DiagPhone(info);
        if (info == null) SetPhone(Ui.Bad, "ADB failed to start", "Try reinstalling ADB.");
        else if (info.State == "none")
        {
            // ADB sees nothing: Windows may still see the phone, with its USB driver failed (code 10 until a restart, on Samsung).
            var usb = await Task.Run(PhoneUsb.Scan);
            NoteUsb(usb);
            if (usb.Problem is int code) SetPhone(Ui.Bad, "Phone driver failed", PhoneSetup.Advice(PhoneStep.UsbDriverError, code, usb.Vendor == "Samsung"));
            else SetPhone(Ui.Faint, "Phone not found", usb.Seen ? "Windows sees the phone, but not over USB debugging. See \"Phone setup\"." : "Turn on USB debugging in developer options. See \"Phone setup\".");
        }
        else if (!info.Ready) SetPhone(Ui.Warn, "Waiting for authorization", info.Summary);
        else if (info.WifiOn && info.WifiConnected == false)
            SetPhone(Ui.Good, "Ready to change IP", info.Summary + " · Wi-Fi on, not connected (still mobile data)");
        else if (info.WifiOn)
            SetPhone(Ui.Warn, "Phone Wi-Fi is on",
                (info.WifiSsid != null ? $"Connected to {info.WifiSsid}. " : "") + "With Wi-Fi on, the PC may get your home network instead of mobile data.");
        else if (_last != null && _last.Egress == null && _last.Candidate == null)
            SetPhone(Ui.Warn, "Phone found, no connection yet", info.Summary + ". Turn on USB tethering.");
        else SetPhone(Ui.Good, "Ready to change IP", info.Summary);
        PhoneNotInUse();
        WifiOffBtn.Visibility = info?.Ready == true && info.WifiOn ? Visibility.Visible : Visibility.Collapsed;
        UpdatePhoneCheck();
        // Tethering on in the phone but Windows has no phone adapter: happens after the cable flickers.
        // No phone mode has no candidate, so the adapters say whether Windows sees the phone.
        if (info?.Ready == true && _last != null && _last.Candidate == null && !(_last.Adapters?.Any(a => a.IsPhone && a.Up && a.LocalIp != null) ?? false)
            && string.IsNullOrEmpty(_s.EgressSsid) && await Adb.TetheringOn(_adb, info.Serial))
        {
            if (!_tetherHinted)
            {
                _tetherHinted = true;
                Log.Warn("The phone has USB tethering on, but Windows doesn't see it. Unplug the cable and plug it back in, or turn tethering off and on.");
                Toast("Windows doesn't see the phone's USB tethering. Unplug the cable and plug it back in.", false);
            }
        }
        else _tetherHinted = false;

        if (PhoneWifiOn != wifiBefore)
        {
            if (PhoneWifiOn) { Log.Warn("The phone's Wi-Fi is on" + (_phone.WifiSsid != null ? $" (connected to {_phone.WifiSsid})" : "") + ". Servers and CS2 blocked until it's off."); _verifiedKey = null; }
            else Log.Ok("The phone's Wi-Fi is off again. Checking the phone's IP before trusting it.");
            await SyncNow(force: true);
            if (!PhoneWifiOn) _ = RefreshPublicIps();
        }
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
        // Change IP only makes sense with the phone ready on ADB (and not in the no phone mode).
        ChangeIpBtn.IsEnabled = !_changingIp && _phone?.Ready == true && !_s.NoPhone;
        ChangeIpBtn.ToolTip = _s.NoPhone ? "Off in the no phone mode: pick another option under What goes through the phone."
            : "Turns the phone's mobile data off and on until the carrier gives a new IP. Needs the phone set up over ADB. If you are on a server, you will drop.";
        UpdateRejoinBtn();
        var egress = _last?.Egress;
        if (_s.NoPhone)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = "Off: no phone mode";
        }
        else if (egress != null && egress.IsWifi)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = "Through the hotspot: the phone is already using its data";
        }
        else if (_phone?.Ready != true)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = _adb == null ? "Needs the phone tool (ADB)" : "Phone not found (check the USB cable)";
        }
        else
        {
            // Wi-Fi on with the block confirmed: blocked and safe (amber); without the block it's a real risk (red).
            var wifi = _phone.WifiOn && _phone.WifiConnected != false;
            _cPhoneData.Waiting = wifi && _last?.KillSwitchOk == true;
            _cPhoneData.Ok = _cPhoneData.Waiting ? null : !wifi;
            _cPhoneData.Detail = wifi ? $"Phone Wi-Fi is on{(_phone.WifiSsid != null ? $" ({_phone.WifiSsid})" : "")}" : $"{_phone.Carrier} · {_phone.NetType}".Trim(' ', '·');
        }
    }

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
        var r = await Adb.DisableWifi(_adb, _phone.Serial);
        if (r.Ok) { Log.Ok("Phone Wi-Fi turned off: tethering now uses mobile data."); Toast("Phone Wi-Fi turned off.", true); }
        else { Log.Warn("Turning off the phone Wi-Fi: " + r.Output); Toast("The phone refused. Turn off Wi-Fi manually.", false); }
    }

    void SetPhone(Brush dot, string state, string detail)
    {
        PhoneDot.Fill = dot;
        PhoneState.Text = state;
        PhoneDetail.Text = detail;
    }

    async void AdbInstall_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Ask("Install phone tool (ADB)", "Download Google's official platform-tools (about 7 MB) from dl.google.com into the app's data folder? You only need to do this once." + MobileDataNote(), "Download");
        if (!ok) return;
        await InstallAdb(t => ProgressText.Text = t, showProgress: true);
    }

    /// <summary>Downloads ADB (you said yes first). True when it's installed.</summary>
    async Task<bool> InstallAdb(Action<string> report, bool showProgress)
    {
        AdbInstallBtn.IsEnabled = false;
        if (showProgress) ShowProgress("Downloading the phone tool (ADB) from Google…");
        try
        {
            _adb = await Adb.Download(new Progress<string>(report));
            if (_adb != null) { Log.Ok("ADB installed at " + _adb); Toast("ADB installed.", true); }
            else Toast("Downloaded, but adb.exe was not found.", Tone.Error);
        }
        catch (Exception ex) { Log.Error("ADB download: " + ex.Message); Toast("Couldn't download ADB: " + ex.Message, Tone.Error); }
        finally { if (showProgress) HideProgress(); AdbInstallBtn.IsEnabled = true; await RefreshPhone(); }
        return _adb != null;
    }

    async void Tether_Click(object sender, RoutedEventArgs e)
    {
        if (_phone?.Ready != true) { Toast("The phone has to be ready on ADB first.", false); return; }
        if (_demo) { Toast("USB tethering is on: Windows sees the phone as Ethernet 2.", true); return; }
        TetherBtn.IsEnabled = false;
        ShowProgress("Turning on USB tethering…");
        try
        {
            var (adapter, why) = await TurnOnTethering(t => ProgressText.Text = t);
            if (adapter != null) Toast($"USB tethering is on: Windows sees the phone as {adapter.Name}.", true);
            else Toast(why, false);
        }
        finally { HideProgress(); TetherBtn.IsEnabled = true; }
    }

    /// <summary>
    /// Asks the phone for USB tethering (rndis) and waits up to 15 s for Windows to add the phone's adapter. Returns that adapter,
    /// or null with what to do by hand. ADB stays connected on the phones tried (Android 15); if it drops, it comes back on its own.
    /// </summary>
    async Task<(Adapter Adapter, string Why)> TurnOnTethering(Action<string> report)
    {
        // Changing the USB mode resets the phone's USB link, so adb often reports an error even when it worked (Android 15):
        // what counts is Windows seeing the phone below, not adb's answer.
        var r = await Adb.EnableUsbTether(_adb, _phone.Serial);
        Log.Info("USB tethering request: " + (r.Ok ? "sent" : "sent (adb dropped while the phone switched its USB mode)"));
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(1000);
            report?.Invoke($"Turning on USB tethering: waiting for Windows to see the phone ({(int)sw.Elapsed.TotalSeconds} s)…");
            var phone = Net.PickEgress(await Task.Run(Net.Scan), null, null);
            if (phone != null)
            {
                Log.Ok($"USB tethering is on: Windows sees the phone as {phone.Name} after {(int)sw.Elapsed.TotalSeconds} s.");
                await SyncNow(force: true);
                _ = RefreshPublicIps();
                return (phone, null);
            }
        }
        Log.Warn("USB tethering: the phone took the request, but Windows didn't add its adapter within 15 s.");
        await SyncNow(force: true);
        return (null, "Windows didn't see the phone's tethering within 15 s. " + PhoneSetup.Advice(PhoneStep.TetherOff));
    }

    // ------------------------------------------------------------ Phone setup: does each step it can, says the one thing left

    bool _setupRunning;
    readonly List<string> _setupDone = new();

    async void PhoneHelp_Click(object sender, RoutedEventArgs e) => await RunPhoneSetup();

    /// <summary>Demo only (development build): the setup as it looks after a run.</summary>
    partial void DemoPhoneSetup();

    async Task RunPhoneSetup()
    {
        if (_setupRunning) { Dialog.Visibility = Visibility.Visible; return; }
        if (_demo) { DemoPhoneSetup(); return; }
        _setupRunning = true;
        _setupDone.Clear();
        Log.Info("Phone setup started.");
        _ = Ask("Phone setup", "Checking the phone…", "Close", null);
        var tried = new HashSet<PhoneStep>();
        PhoneSetupState st = null;
        var step = PhoneStep.Done;
        string why = null;
        PhoneStep? whyFor = null;
        try
        {
            for (var round = 0; round < 10; round++)
            {
                SetupShow(st, "Checking the phone…");
                st = await ReadSetupState();
                step = PhoneSetup.Next(st);
                SetupShow(st, null);
                if (step == PhoneStep.Done || !PhoneSetup.AppDoes(step) || !tried.Add(step)) break;
                Log.Info("Phone setup: " + PhoneSetup.Key(step));
                var go = await DoSetupStep(step, st);
                if (go.Why != null) (why, whyFor) = (go.Why, step);
                if (!go.Continue) break;
            }
        }
        catch (Exception ex) { Log.Error("Phone setup: " + ex.Message); why = "Something failed: " + ex.Message; }
        finally { _setupRunning = false; }
        await RefreshPhone();
        Telemetry.Count("phone_setup_" + PhoneSetup.Key(step));
        var samsung = st?.Usb.Vendor == "Samsung" || _phone?.Model?.StartsWith("SM-", StringComparison.OrdinalIgnoreCase) == true;
        var last = step == PhoneStep.Done ? "Ready. The app checks the phone's IP, then servers see it instead of your home IP."
            : "Left for you: " + (whyFor == step && why != null ? why : PhoneSetup.Advice(step, st?.Usb.Problem, samsung));
        if (_s.NoPhone) last += "\n\nYou picked No phone under What goes through the phone, so the app isn't using the phone right now.";
        Log.Info(step == PhoneStep.Done ? "Phone setup: done." : "Phone setup: stopped at " + PhoneSetup.Key(step) + ".");
        await Ask("Phone setup", SetupText(st, null) + "\n\n" + last, "Got it", null);
    }

    /// <summary>What the setup found, one line each, then what it did and what it's doing now.</summary>
    string SetupText(PhoneSetupState st, string now)
    {
        var lines = new List<string>();
        if (st != null)
        {
            lines.Add("Phone tool (ADB): " + (st.AdbTool ? "installed" : "not installed"));
            lines.Add("USB: " + (st.AdbState == "device" ? $"{_phone?.Model ?? "phone"} over USB debugging"
                : st.Usb.Problem is int c ? $"driver failed (code {c})"
                : st.AdbState == "unauthorized" ? "waiting for you to allow USB debugging"
                : st.Usb.Seen || st.Tethered ? "phone plugged in, USB debugging off"
                : "no phone found"));
            lines.Add("Connection: " + (st.Hotspot ? $"the phone's Wi-Fi hotspot ({_s.EgressSsid})" : st.Tethered ? "USB tethering on" : "USB tethering off"));
            if (st.AdbState == "device" && !st.Hotspot) lines.Add("Phone Wi-Fi: " + (st.WifiOn ? "on" : "off (mobile data)"));
            var pc = HomeAdapter?.Name;
            lines.Add("Rest of the PC: " + (st.PcOnPhone ? "goes through the phone" : _s.WholePc && _last?.EffectiveMode == "all" ? "through the phone (Whole PC)" : pc != null ? $"home network ({pc})" : "home network"));
        }
        if (_setupDone.Count > 0) lines.Add("\n" + string.Join("\n", _setupDone));
        if (now != null) lines.Add("\n" + now);
        return string.Join("\n", lines);
    }

    void SetupShow(PhoneSetupState st, string now)
    {
        if (Dialog.Visibility == Visibility.Visible && DialogTitle.Text == "Phone setup") DialogText.Text = SetupText(st, now);
    }

    async Task<PhoneSetupState> ReadSetupState()
    {
        _adb ??= Adb.Find();
        var info = _adb == null ? null : Adb.SteadyWifi(await Adb.Status(_adb), _phone, ref _wifiOnReads);
        if (info != null) _phone = info;
        var usb = info?.Ready == true ? PhoneUsbState.None : await Task.Run(PhoneUsb.Scan);
        var adapters = await Task.Run(Net.Scan);
        var hotspot = !string.IsNullOrEmpty(_s.EgressSsid);
        var phone = Net.PickEgress(adapters, _s.EgressAdapterId, _s.EgressSsid);
        var other = phone != null && adapters.Any(a => a.Up && a.Gateway != null && a.Index != phone.Index && !a.IsPhone);
        var mode = Guard.ModeFor(_s.Mode, _s.GameMode, Steam.Cs2Running);
        return new PhoneSetupState
        {
            AdbTool = _adb != null,
            AdbState = _adb == null ? "none" : info?.State,
            Usb = usb,
            Hotspot = hotspot,
            Tethered = phone != null && !hotspot,
            WifiOn = info?.Ready == true && info.WifiOn && info.WifiConnected != false,
            PcOnPhone = Guard.NeedsPcOffPhone(mode, true, Net.BestInterface("1.1.1.1"), phone?.Index ?? -1, other),
        };
    }

    /// <summary>One step the app can do. Continue = check again; Why = what to tell you if it stops here.</summary>
    async Task<(bool Continue, string Why)> DoSetupStep(PhoneStep step, PhoneSetupState st)
    {
        switch (step)
        {
            case PhoneStep.NoAdbTool:
                if (!await Ask("Phone setup", SetupText(st, null) + "\n\nThe phone tool (ADB) is needed for Change IP and for the rest of this check. Download Google's official platform-tools (about 7 MB) from dl.google.com?" + MobileDataNote(), "Download", "Not now"))
                    return (false, null);
                _ = Ask("Phone setup", SetupText(st, "Downloading the phone tool (ADB) from Google…"), "Close", null);
                if (!await InstallAdb(t => SetupShow(st, t), showProgress: false)) return (false, "The phone tool (ADB) didn't install. See the Log.");
                _setupDone.Add("Installed the phone tool (ADB).");
                return (true, null);
            case PhoneStep.Unauthorized:
                // The prompt is on the phone: wait for your tap (up to 30 s).
                for (var i = 0; i < 15; i++)
                {
                    SetupShow(st, $"Allow USB debugging on the phone screen (tick \"Always allow\")… {30 - i * 2} s");
                    await Task.Delay(2000);
                    if ((await Adb.Status(_adb))?.Ready == true) { _setupDone.Add("USB debugging allowed."); return (true, null); }
                }
                return (false, null);
            case PhoneStep.TetherOff:
            {
                var (adapter, why) = await TurnOnTethering(t => SetupShow(st, t));
                if (adapter == null) return (false, why);
                _setupDone.Add($"Turned on USB tethering ({adapter.Name}).");
                return (true, null);
            }
            case PhoneStep.WifiOn:
                if (!await Ask("Phone setup", SetupText(st, null) + "\n\nThe phone's Wi-Fi is on" + (_phone?.WifiSsid != null ? $" ({_phone.WifiSsid})" : "") +
                        ": tethering may share your home network instead of mobile data. Turn it off on the phone?", "Turn Wi-Fi off", "Leave it on"))
                    return (false, null);
                _ = Ask("Phone setup", SetupText(st, "Turning off the phone's Wi-Fi…"), "Close", null);
                await TurnPhoneWifiOff();
                await Task.Delay(3000);
                await RefreshPhone();
                _setupDone.Add("Turned off the phone's Wi-Fi.");
                return (true, null);
            case PhoneStep.PcOnPhone:
                SetupShow(st, "Keeping the rest of the PC on your home network…");
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
                if (Net.BestInterface("1.1.1.1") != _last?.PhoneIndex) _setupDone.Add("Moved the rest of the PC back to your home network.");
                return (true, "Windows still sends the rest of the PC through the phone. Check the Log, or plug the PC into your home network.");
        }
        return (false, null);
    }

    async void ChangeIp_Click(object sender, RoutedEventArgs e) => await RunCommand("Change IP", "starting…", () => ChangeIp(confirm: true));

    /// <summary>Cycles the phone's data until the carrier gives a new IP. True if the IP really changed (and isn't the home IP).</summary>
    async Task<bool> ChangeIp(bool confirm)
    {
        if (_changingIp) return false;
        if (_demo) { Toast("Demo: IP changed.", true); return false; }
        if (_s.NoPhone) { Toast("No phone mode: there is no IP to change. Pick another option under What goes through the phone.", false); return false; }
        // USB tethering, or the phone's hotspot (saved by name) with the cable plugged in only for ADB.
        var hotspot = _last?.Egress is { IsWifi: true } && !string.IsNullOrEmpty(_s.EgressSsid);
        if (_last?.Egress != null && !_last.Egress.IsPhone && !hotspot)
        {
            Toast("The chosen connection is not a phone: the Change IP button only works with a phone over ADB (USB tethering, or its hotspot with the cable plugged in).", false);
            return false;
        }
        if (_adb == null || _phone?.Ready != true)
        {
            Toast(_adb == null ? "Install the phone tool first (button under Phone)." : "The phone is not ready on ADB. See \"Phone setup\".", false);
            return false;
        }
        if (confirm && Steam.Cs2Running && !await Ask("CS2 is open", "If you are on a server, you will be disconnected when the IP changes. Continue?", "Change IP")) return false;

        _changingIp = true;
        // The new IP isn't known yet: untrusted (servers and CS2 blocked) until it is measured again.
        _verifiedKey = null;
        await SyncNow(force: true);
        ChangeIpBtn.IsEnabled = false;
        RejoinBtn.IsEnabled = false;
        ChangeIpBtn.Content = "Changing…";
        var method = _s.ChangeMethod == "airplane" ? Adb.Method.Airplane : Adb.Method.MobileData;
        var first = _egressPublic;
        string got = null;
        _ipChangedAt = DateTime.Now.AddTicks(-(DateTime.Now.Ticks % TimeSpan.TicksPerSecond));
        Diag.Write("change-ip", new { from = first });
        try
        {
            // On the hotspot the phone's Wi-Fi is left alone: on some phones turning it off also stops the hotspot.
            if (_phone.WifiOn && !hotspot)
            {
                ShowProgress("The phone's Wi-Fi is on: turning it off to use mobile data…");
                await TurnPhoneWifiOff();
                await Task.Delay(3000);
            }
            for (var attempt = 1; attempt <= Math.Max(1, _s.MaxAttempts); attempt++)
            {
                ShowProgress($"Attempt {attempt}/{_s.MaxAttempts}: turning off the phone's data for {_s.OffSeconds} s…");
                var err = await Adb.Cycle(_adb, _phone.Serial, method, Math.Clamp(_s.OffSeconds, 1, 60));
                if (err != null) { Log.Error(err); Toast(err, Tone.Error); return false; }

                ShowProgress($"Attempt {attempt}/{_s.MaxAttempts}: waiting for the mobile network to come back…");
                got = await WaitForEgressIp(TimeSpan.FromSeconds(50));
                if (got == null) { ShowProgress("The network hasn't come back yet…"); continue; }
                if (got == first) { Log.Info($"The carrier gave back the same IP ({got})."); continue; }
                // Many admins ban the whole range: optionally keep going until the /16 is new, against every earlier IP
                // (the same list the range note after the change uses).
                if (_s.RequireNew16 && attempt < _s.MaxAttempts && IpRange.FirstSame16(got, _history.Select(h => h.Ip).Append(first).Where(x => x != null)) is string seen)
                {
                    Log.Info($"New IP {got} is in the same range as {seen}: trying again.");
                    continue;
                }
                break;
            }

            if (got != null) ResetSteamAfterIpChange();
            Diag.Write("ip-changed", new { from = first, to = got });
            var home = _homePublic ?? _s.LastHomeIp;
            if (got != null && got == home)
            {
                _distrust = true;
                Telemetry.Count("ip_home_back");
                Toast("The phone came back with your home IP. Your servers stay blocked: turn off the phone's Wi-Fi.", Tone.Error);
            }
            else if (got != null && first == null)
            {
                var range = RangeNote(got, out var sameRange);
                _egressPublic = got;
                _egressInfo = await Net.Lookup(got);
                AddHistory(got, _egressInfo, "trocado");
                ResetQuality();
                Log.Ok($"Mobile data back on: {got} (the IP before wasn't known, so it can't be compared). {range}");
                Toast($"New IP: {got}. {range}", !sameRange);
                return true;
            }
            else if (got != null && got != first)
            {
                var range = RangeNote(got, out var sameRange);
                _egressPublic = got;
                _egressInfo = await Net.Lookup(got);
                AddHistory(got, _egressInfo, "trocado");
                ResetQuality();
                if (sameRange) Log.Warn($"IP changed: {first} → {got}. {range}");
                else Log.Ok($"IP changed: {first} → {got}. {range}");
                Toast($"New IP: {got}. {range}", !sameRange);
                return true;
            }
            else if (got == first)
            {
                Telemetry.Count("ip_same");
                Toast("The carrier kept giving the same IP. Try airplane mode in Settings or wait a few minutes.", false);
            }
            else
            {
                Telemetry.Count("ip_no_network");
                Toast("The mobile network didn't come back in time. Check the phone.", false);
            }
            return false;
        }
        finally
        {
            _changingIp = false;
            ChangeIpBtn.Content = "Change IP";
            UpdatePhoneCheck();
            HideProgress();
            ShowIps();
            await SyncNow(force: true);
            // Measures the new connection: only a confirmed IP that isn't the home one lifts the block.
            await RefreshPublicIps();
        }
    }

    /// <summary>Whether the new IP's /16 shows up in the previous IPs (call before adding it to the history).</summary>
    string RangeNote(string ip, out bool same)
    {
        same = false;
        var r = IpRange.Slash16(ip);
        if (r == null) return "";
        var seen = IpRange.FirstSame16(ip, _history.Select(h => h.Ip));
        same = seen != null;
        return !same ? $"New range ({r}.x.x)."
            : $"Same range ({r}.x.x) as {seen}. If the server banned that range, change again" + (_s.RequireNew16 ? "." : " or turn on \"Keep changing until the IP is in a new range\" in Settings.");
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
