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
            SetPhone(Ui.Faint, "ADB not installed", "You need it to change the IP with the button. Install it with one click.");
            AdbInstallBtn.Visibility = Visibility.Visible;
            return;
        }
        AdbInstallBtn.Visibility = Visibility.Collapsed;
        var info = await Adb.Status(_adb);
        var wifiBefore = PhoneWifiOn;
        _phone = info;
        DiagPhone(info);
        if (info == null) SetPhone(Ui.Bad, "ADB failed to start", "Try reinstalling ADB.");
        else if (info.State == "none") SetPhone(Ui.Faint, "Phone not seen by ADB", "Turn on USB debugging in developer options. See \"Phone setup\".");
        else if (!info.Ready) SetPhone(Ui.Warn, "Waiting for authorization", info.Summary);
        else if (info.WifiOn)
            SetPhone(Ui.Warn, "Phone Wi-Fi is on",
                (info.WifiSsid != null ? $"Connected to {info.WifiSsid}. " : "") + "With Wi-Fi on, the PC may get your home network instead of mobile data.");
        else if (_last != null && _last.Egress == null && _last.Candidate == null)
            SetPhone(Ui.Warn, "Phone found, no connection yet", info.Summary + ". Turn on USB tethering.");
        else SetPhone(Ui.Good, "Ready to change IP", info.Summary);
        WifiOffBtn.Visibility = info?.Ready == true && info.WifiOn ? Visibility.Visible : Visibility.Collapsed;
        UpdatePhoneCheck();
        // Tethering on in the phone but Windows has no phone adapter: happens after the cable flickers.
        if (info?.Ready == true && _last != null && _last.Candidate == null && string.IsNullOrEmpty(_s.EgressSsid) && await Adb.TetheringOn(_adb, info.Serial))
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
            if (PhoneWifiOn) { Log.Warn("The phone's Wi-Fi is on. Servers and CS2 blocked until it's off."); _verifiedKey = null; }
            else Log.Ok("The phone's Wi-Fi is off again. Checking the phone's IP before trusting it.");
            await SyncNow(force: true);
            if (!PhoneWifiOn) _ = RefreshPublicIps();
        }
    }

    /// <summary>"Mobile data" check: the phone shares its data and not the home Wi-Fi.</summary>
    void UpdatePhoneCheck()
    {
        // Change IP only makes sense with the phone ready on ADB.
        ChangeIpBtn.IsEnabled = !_changingIp && _phone?.Ready == true;
        UpdateRejoinBtn();
        var egress = _last?.Egress;
        if (egress != null && egress.IsWifi)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = "Through the hotspot: the phone is already using its data";
        }
        else if (_phone?.Ready != true)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Waiting = false;
            _cPhoneData.Detail = _adb == null ? "Needs ADB (keep the phone's Wi-Fi off)" : "Phone not seen by ADB";
        }
        else
        {
            // Wi-Fi on with the block confirmed: blocked and safe (amber); without the block it's a real risk (red).
            _cPhoneData.Waiting = _phone.WifiOn && _last?.KillSwitchOk == true;
            _cPhoneData.Ok = _cPhoneData.Waiting ? null : !_phone.WifiOn;
            _cPhoneData.Detail = _phone.WifiOn ? $"Phone Wi-Fi is on{(_phone.WifiSsid != null ? $" ({_phone.WifiSsid})" : "")}" : $"{_phone.Carrier} · {_phone.NetType}".Trim(' ', '·');
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
        var ok = await Ask("Install ADB", "Download Google's official platform-tools (about 7 MB) from dl.google.com into the app's data folder? You only need to do this once.", "Download");
        if (!ok) return;
        AdbInstallBtn.IsEnabled = false;
        ShowProgress("Downloading ADB from Google…");
        try
        {
            _adb = await Adb.Download(new Progress<string>(t => ProgressText.Text = t));
            if (_adb != null) { Log.Ok("ADB installed at " + _adb); Toast("ADB installed.", true); }
            else Toast("Downloaded, but adb.exe was not found.", Tone.Error);
        }
        catch (Exception ex) { Log.Error("ADB download: " + ex.Message); Toast("Couldn't download ADB: " + ex.Message, Tone.Error); }
        finally { HideProgress(); AdbInstallBtn.IsEnabled = true; await RefreshPhone(); }
    }

    async void Tether_Click(object sender, RoutedEventArgs e)
    {
        if (_phone?.Ready != true) { Toast("The phone has to be ready on ADB first.", false); return; }
        if (_demo) { Toast("Demo: USB tethering request sent.", true); return; }
        var r = await Adb.EnableUsbTether(_adb, _phone.Serial);
        Log.Info("USB tethering request: " + (r.Ok ? "sent" : r.Output));
        Toast(r.Ok ? "Request sent. If it doesn't turn on within 10 s, turn on USB tethering manually." : "The phone refused. Turn on USB tethering manually.", r.Ok);
        await Task.Delay(4000);
        await SyncNow(force: true);
    }

    void PhoneHelp_Click(object sender, RoutedEventArgs e) => _ = Ask("Phone setup (once)",
        "1. Settings > About phone > Software information: tap \"Build number\" 7 times.\n" +
        "2. Developer options: turn on \"USB debugging\".\n" +
        "3. Plug in the cable. On the phone, accept \"Allow USB debugging\" (tick \"Always allow\").\n" +
        "4. Settings > Connections > Mobile Hotspot and Tethering: turn on \"USB tethering\".\n" +
        "5. Turn off the phone's Wi-Fi: otherwise it shares the home network and the IP stays the same.\n\n" +
        "If the app doesn't see the phone in ADB: install the \"Samsung USB Driver for Mobile Phones\" (Samsung website) and plug the cable in again.\n" +
        "Without USB debugging everything works except the Change IP button: in that case turn airplane mode on and off manually.",
        "Got it", null);

    async void ChangeIp_Click(object sender, RoutedEventArgs e) => await ChangeIp(confirm: true);

    /// <summary>Cycles the phone's data until the carrier gives a new IP. True if the IP really changed (and isn't the home IP).</summary>
    async Task<bool> ChangeIp(bool confirm)
    {
        if (_changingIp) return false;
        if (_demo) { Toast("Demo: IP changed.", true); return false; }
        if (_last?.Egress != null && !_last.Egress.IsPhone)
        {
            Toast("The chosen connection is not a phone: the Change IP button only works with a phone over ADB.", false);
            return false;
        }
        if (_adb == null || _phone?.Ready != true)
        {
            Toast(_adb == null ? "Install ADB first (button under Phone)." : "The phone is not ready on ADB. See \"Phone setup\".", false);
            return false;
        }
        if (confirm && Steam.Cs2Running && !await Ask("CS2 is open", "If you're in a server, you'll be dropped when the IP changes. Continue?", "Change IP")) return false;

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
            if (_phone.WifiOn)
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

                ProgressText.Text = $"Attempt {attempt}/{_s.MaxAttempts}: waiting for the mobile network to come back…";
                got = await WaitForEgressIp(TimeSpan.FromSeconds(50));
                if (got == null) { ProgressText.Text = "The network hasn't come back yet…"; continue; }
                if (got == first) { Log.Info($"The carrier gave back the same IP ({got})."); continue; }
                // Many admins ban the whole range: optionally keep going until the /16 is different too.
                if (_s.RequireNew16 && first != null && IpRange.Same16(got, first) && attempt < _s.MaxAttempts)
                {
                    Log.Info($"New IP {got} is in the same /16 as {first}: trying again.");
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
                Toast("The phone came back with your home IP. Your servers stay blocked: turn off the phone's Wi-Fi.", Tone.Error);
            }
            else if (got != null && first == null)
            {
                var range = RangeNote(got, out var sameRange);
                _egressPublic = got;
                _egressInfo = await Net.Lookup(got);
                AddHistory(got, _egressInfo, "trocado");
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
                if (sameRange) Log.Warn($"IP changed: {first} → {got}. {range}");
                else Log.Ok($"IP changed: {first} → {got}. {range}");
                Toast($"New IP: {got}. {range}", !sameRange);
                return true;
            }
            else if (got == first)
                Toast("The carrier kept giving the same IP. Try airplane mode in Settings or wait a few minutes.", false);
            else
                Toast("The mobile network didn't come back in time. Check the phone.", false);
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
            : $"Same range ({r}.x.x) as {seen}. If the server banned that range, change again" + (_s.RequireNew16 ? "." : " or turn on \"Keep changing until the IP is in a new /16\" in Settings.");
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
