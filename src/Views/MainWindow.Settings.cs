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
            AutoJoinBox.IsChecked = _s.AutoJoinFollowed;
            SwIpBox.IsChecked = _s.SwitchIp;
            SwAccountBox.IsChecked = _s.SwitchAccount;
            SwRejoinBox.IsChecked = _s.SwitchRejoin;
            SwOpenWithBox.IsChecked = _s.SwitchOpenWith;
            SoundsBox.IsChecked = _s.CommandSounds;
            HotkeySwitchBox.Text = _s.HotkeySwitch;
            HotkeyRejoinBox.Text = _s.HotkeyRejoin;
            CheckUpdatesBox.IsChecked = _s.CheckUpdates;
            KeepOffBox.IsChecked = _s.KeepPcOffPhone;
            LockCs2Box.IsChecked = _s.LockCs2;
            SteamEgressBox.IsChecked = _s.SteamOnEgress;
            AutoModeBox.IsChecked = _s.AutoModeByQuality;
            ApplyDevSettings();
            CleanupBox.IsChecked = _s.CleanupOnExit;
            ApiKeyBox.Text = _s.SteamApiKey ?? "";
            ApiKeyPwd.Password = _s.SteamApiKey ?? "";
            ShowApiKey(false);
            OptCs2.IsChecked = _s.LaunchCs2;
            OptSilent.IsChecked = _s.SteamSilent;
            OptOffline.IsChecked = _s.SteamOffline;
            OptStreamer.IsChecked = HideLogins;
            OptStreamer.IsEnabled = !Rec.On;
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
        _s.AutoJoinFollowed = AutoJoinBox.IsChecked == true;
        _s.SwitchIp = SwIpBox.IsChecked == true;
        _s.SwitchAccount = SwAccountBox.IsChecked == true;
        _s.SwitchRejoin = SwRejoinBox.IsChecked == true;
        _s.SwitchOpenWith = SwOpenWithBox.IsChecked == true;
        if (!_s.CommandSounds && SoundsBox.IsChecked == true) Tones.Play(Tones.Kind.Done);   // so you know how they sound
        _s.CommandSounds = SoundsBox.IsChecked == true;
        _s.KeepPcOffPhone = KeepOffBox.IsChecked == true;
        _s.AutoModeByQuality = AutoModeBox.IsChecked == true;
        var guardChanged = _s.LockCs2 != (LockCs2Box.IsChecked == true) || _s.SteamOnEgress != (SteamEgressBox.IsChecked == true);
        _s.LockCs2 = LockCs2Box.IsChecked == true;
        _s.SteamOnEgress = SteamEgressBox.IsChecked == true;
        ReadDevSettings();
        if (guardChanged) _ = SyncNow(force: true);
        _s.CleanupOnExit = CleanupBox.IsChecked == true;
        var key = ApiKeyBox.Visibility == Visibility.Visible ? ApiKeyBox.Text : ApiKeyPwd.Password;
        _s.SteamApiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        OffSecondsBox.Text = _s.OffSeconds.ToString();
        AttemptsBox.Text = _s.MaxAttempts.ToString();
        SaveSettings();
        UpdateQuickInfo();
    }

    // ------------------------------------------------------------ Hotkeys: the box takes the combination you press

    // While a box has focus the hotkeys are off, so pressing the current one sets it instead of running it.
    void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) { if (!_demo) UnregisterHotkeys(); }
    void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) { if (!_demo && !_closingDone) RegisterHotkeys(); }

    void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var rejoin = box == HotkeyRejoinBox;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key == Key.Tab && mods == ModifierKeys.None) return;   // Tab still moves on
        e.Handled = true;
        if (Hotkey.IsModifier(key) || key is Key.ImeProcessed or Key.DeadCharProcessed) return;
        string text;
        if (key == Key.Escape && mods == ModifierKeys.None) text = rejoin ? Settings.DefaultHotkeyRejoin : Settings.DefaultHotkeySwitch;
        else
        {
            if (Hotkey.Problem(mods, key) is string why) { Toast(why, false); return; }
            text = Hotkey.Format(mods, key);
        }
        if (string.Equals(text, rejoin ? _s.HotkeySwitch : _s.HotkeyRejoin, StringComparison.OrdinalIgnoreCase))
        {
            Toast($"{text} is already the {(rejoin ? "Switch" : "rejoin")} hotkey.", false);
            return;
        }
        box.Text = text;
        if (rejoin) _s.HotkeyRejoin = text; else _s.HotkeySwitch = text;
        SaveSettings();
        UpdateQuickInfo();
        UpdateRejoinBtn();
        Log.Info($"{(rejoin ? "Change IP and rejoin" : "Switch")} hotkey: {text}.");
    }

    // ------------------------------------------------------------ Steam Web API key: hidden unless you ask (never while Hide logins is on)

    void ApiKeyShow_Click(object sender, RoutedEventArgs e) => ShowApiKey(ApiKeyBox.Visibility != Visibility.Visible);

    void ShowApiKey(bool show)
    {
        show &= !HideLogins;
        // The two boxes always hold the same key; only one is on screen.
        if (show) ApiKeyBox.Text = ApiKeyPwd.Password; else ApiKeyPwd.Password = ApiKeyBox.Text;
        ApiKeyBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPwd.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ApiKeyShowBtn.Content = show ? "Hide" : "Show";
        ApiKeyShowBtn.IsEnabled = !HideLogins;
        ApiKeyShowBtn.ToolTip = HideLogins ? "Hide logins is on (Accounts): the key stays hidden." : show ? "Hides the key" : "Shows the key";
    }

    void SwitchSteps_Click(object sender, RoutedEventArgs e)
    {
        NavSettings.IsChecked = true;
        Dispatcher.BeginInvoke(() => SwitchGroup.BringIntoView(), DispatcherPriority.Loaded);
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

    void OpenData_Click(object sender, RoutedEventArgs e) =>
        ShellLaunch.OpenAsUser(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{Paths.Data}\"", null);   // not as admin
    void ApiKeyLink_Click(object sender, RoutedEventArgs e) => OpenUrl("https://steamcommunity.com/dev/apikey");
}
