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
        if (_demo) return;
        _adb ??= Adb.Find();
        if (_adb == null)
        {
            SetPhone(Ui.Faint, "ADB not installed", "You need it to change the IP with the button. Install it with one click.");
            AdbInstallBtn.Visibility = Visibility.Visible;
            return;
        }
        AdbInstallBtn.Visibility = Visibility.Collapsed;
        var info = await Adb.Status(_adb);
        _phone = info;
        DiagPhone(info);
        if (info == null) SetPhone(Ui.Bad, "ADB failed to start", "Try reinstalling ADB.");
        else if (info.State == "none") SetPhone(Ui.Faint, "Phone not seen by ADB", "Turn on USB debugging in developer options. See \"Phone setup\".");
        else if (!info.Ready) SetPhone(Ui.Warn, "Waiting for authorization", info.Summary);
        else if (info.WifiOn)
            SetPhone(Ui.Warn, "Phone Wi-Fi is on",
                (info.WifiSsid != null ? $"Connected to {info.WifiSsid}. " : "") + "With Wi-Fi on, the PC may get your home network instead of mobile data.");
        else SetPhone(Ui.Good, "Ready to change IP", info.Summary);
        WifiOffBtn.Visibility = info?.Ready == true && info.WifiOn ? Visibility.Visible : Visibility.Collapsed;
        UpdatePhoneCheck();
    }

    /// <summary>"Mobile data" check: the phone shares its data and not the home Wi-Fi.</summary>
    void UpdatePhoneCheck()
    {
        // Change IP only makes sense with the phone ready on ADB.
        ChangeIpBtn.IsEnabled = !_changingIp && _phone?.Ready == true;
        var egress = _last?.Egress;
        if (egress != null && egress.IsWifi)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Detail = "Through the hotspot: the phone is already using its data";
        }
        else if (_phone?.Ready != true)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Detail = _adb == null ? "No ADB: make sure the phone's Wi-Fi is off" : "Phone not seen by ADB";
        }
        else
        {
            _cPhoneData.Ok = !_phone.WifiOn;
            _cPhoneData.Detail = _phone.WifiOn ? $"The phone's Wi-Fi is on{(_phone.WifiSsid != null ? $" ({_phone.WifiSsid})" : "")}: turn it off" : $"{_phone.Carrier} · {_phone.NetType}".Trim(' ', '·');
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
        var ok = await Ask("Install ADB", "I'll download Google's official platform-tools (about 7 MB) from dl.google.com into the SRT_Dados folder next to the app. You only need to do this once.", "Download");
        if (!ok) return;
        AdbInstallBtn.IsEnabled = false;
        ShowProgress("Downloading ADB from Google…");
        try
        {
            _adb = await Adb.Download(new Progress<string>(t => ProgressText.Text = t));
            if (_adb != null) { Log.Ok("ADB installed at " + _adb); Toast("ADB installed.", true); }
            else Toast("Downloaded, but adb.exe was not found.", false);
        }
        catch (Exception ex) { Log.Error("ADB download: " + ex.Message); Toast("Couldn't download ADB: " + ex.Message, false); }
        finally { HideProgress(); AdbInstallBtn.IsEnabled = true; await RefreshPhone(); }
    }

    async void Tether_Click(object sender, RoutedEventArgs e)
    {
        if (_phone?.Ready != true) { Toast("The phone has to be ready on ADB first.", false); return; }
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

    async void ChangeIp_Click(object sender, RoutedEventArgs e)
    {
        if (_changingIp) return;
        if (_demo) { Toast("Demo: IP changed.", true); return; }
        if (_last?.Egress != null && !_last.Egress.IsPhone)
        {
            Toast("The chosen connection is not a phone: the Change IP button only works with a phone over ADB.", false);
            return;
        }
        if (_adb == null || _phone?.Ready != true)
        {
            Toast(_adb == null ? "Install ADB first (Phone card)." : "The phone is not ready on ADB. See \"Phone setup\".", false);
            return;
        }
        if (Steam.Cs2Running && !await Ask("CS2 is open", "If you're in a server, you'll be dropped when the IP changes. Continue?", "Change IP")) return;

        _changingIp = true;
        ChangeIpBtn.IsEnabled = false;
        ChangeIpBtn.Content = "Changing…";
        var method = _s.ChangeMethod == "airplane" ? Adb.Method.Airplane : Adb.Method.MobileData;
        var old = _egressPublic;
        string got = null;
        _ipChangedAt = DateTime.Now.AddTicks(-(DateTime.Now.Ticks % TimeSpan.TicksPerSecond));
        Diag.Write("change-ip", new { from = old });
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
                if (err != null) { Log.Error(err); Toast(err, false); return; }

                ProgressText.Text = $"Attempt {attempt}/{_s.MaxAttempts}: waiting for the mobile network to come back…";
                got = await WaitForEgressIp(TimeSpan.FromSeconds(50));
                if (got == null) { ProgressText.Text = "The network hasn't come back yet…"; continue; }
                if (got != old) break;
                Log.Info($"The carrier gave back the same IP ({got}).");
            }

            if (got != null) ResetSteamAfterIpChange();
            Diag.Write("ip-changed", new { from = old, to = got });
            if (got != null && got != old)
            {
                _egressPublic = got;
                _egressInfo = await Net.Lookup(got);
                AddHistory(got, _egressInfo, "trocado");
                Log.Ok($"IP changed: {old ?? "?"} → {got}");
                Toast($"New IP: {got}", true);
            }
            else if (got == old)
                Toast("The carrier kept giving the same IP. Try airplane mode in Settings or wait a few minutes.", false);
            else
                Toast("The mobile network didn't come back in time. Check the phone.", false);
        }
        finally
        {
            _changingIp = false;
            ChangeIpBtn.Content = "Change IP";
            UpdatePhoneCheck();
            HideProgress();
            ShowIps();
            await SyncNow(force: true);
        }
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
