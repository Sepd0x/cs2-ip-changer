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

// Settings
public partial class MainWindow
{
    void ApplySettingsToUi()
    {
        _uiLock = true;
        try
        {
            ModeServers.IsChecked = _s.Mode != "all" && !_s.GameMode;
            ModeGame.IsChecked = _s.Mode != "all" && _s.GameMode;
            ModeAll.IsChecked = _s.Mode == "all";
            DefaultPortBox.Text = _s.DefaultPort.ToString();
            AutoQueryBox.IsChecked = _s.AutoQuery;
            MethodCombo.SelectedIndex = _s.ChangeMethod == "airplane" ? 1 : 0;
            OffSecondsBox.Text = _s.OffSeconds.ToString();
            AttemptsBox.Text = _s.MaxAttempts.ToString();
            New16Box.IsChecked = _s.RequireNew16;
            CheckUpdatesBox.IsChecked = _s.CheckUpdates;
            KeepOffBox.IsChecked = _s.KeepPcOffPhone;
            LockCs2Box.IsChecked = _s.LockCs2;
            SteamEgressBox.IsChecked = _s.SteamOnEgress;
            ApplyDevSettings();
            CleanupBox.IsChecked = _s.CleanupOnExit;
            ApiKeyBox.Text = _s.SteamApiKey ?? "";
            OptCs2.IsChecked = _s.LaunchCs2;
            OptSilent.IsChecked = _s.SteamSilent;
            OptOffline.IsChecked = _s.SteamOffline;
            OptStreamer.IsChecked = _s.StreamerMode;
            AccSort.SelectedItem = AccSort.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.AccountSort) ?? AccSort.Items[0];
        }
        finally { _uiLock = false; }
    }

    void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_uiLock || !IsLoaded) return;
        _s.ChangeMethod = (MethodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "data";
        if (int.TryParse(OffSecondsBox.Text, out var off)) _s.OffSeconds = Math.Clamp(off, 1, 60);
        if (int.TryParse(AttemptsBox.Text, out var att)) _s.MaxAttempts = Math.Clamp(att, 1, 10);
        _s.RequireNew16 = New16Box.IsChecked == true;
        _s.KeepPcOffPhone = KeepOffBox.IsChecked == true;
        var guardChanged = _s.LockCs2 != (LockCs2Box.IsChecked == true) || _s.SteamOnEgress != (SteamEgressBox.IsChecked == true);
        _s.LockCs2 = LockCs2Box.IsChecked == true;
        _s.SteamOnEgress = SteamEgressBox.IsChecked == true;
        ReadDevSettings();
        if (guardChanged) _ = SyncNow(force: true);
        _s.CleanupOnExit = CleanupBox.IsChecked == true;
        _s.SteamApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Text) ? null : ApiKeyBox.Text.Trim();
        OffSecondsBox.Text = _s.OffSeconds.ToString();
        AttemptsBox.Text = _s.MaxAttempts.ToString();
        SaveSettings();
    }

    void SaveSettings() { if (!_demo) Store.Save(Paths.File("settings.json"), _s); }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ask("Restore network", "This removes the Firewall block, the routes and the \"Whole PC\" mode. The servers in the list will see your home IP until you open the app again. The list stays saved.", "Restore", danger: true)) return;
        if (_demo) return;
        // Stays restored: network changes no longer put the rules back until Run leak test or the next start.
        _restored = true;
        _tick.Stop();
        var left = await _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList());
        _last = null; // the old report would redraw "Protected" on the next IP check
        if (left == null) Toast("Network restored and checked. Protection comes back when you reopen the app or press Run leak test.", true);
        else Toast("Couldn't remove: " + left + ". Restart the PC to clear the routes, or run the app with --restore.", Tone.Error);
        foreach (var s in _servers) { s.Protected = false; s.RouteText = "Off"; }
        SetHero("bad", "Protection off", "You restored the network. Click Run leak test to protect again.");
    }

    void OpenFirewall_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wf.msc")) { UseShellExecute = true }); }
        catch (Exception ex) { Toast("Couldn't open the Firewall: " + ex.Message, Tone.Error); }
        Toast("In Outbound Rules, look for 'ServerRouteTool v9 - kill switch'.", true);

    }

    void OpenData_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(Paths.Data) { UseShellExecute = true });
    void ApiKeyLink_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://steamcommunity.com/dev/apikey") { UseShellExecute = true });
}
