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
            ModeServers.IsChecked = _s.Mode != "all" && !_s.NoPhone && !_s.GameMode;
            ModeGame.IsChecked = _s.Mode != "all" && !_s.NoPhone && _s.GameMode;
            ModeAll.IsChecked = _s.Mode == "all";
            ModeHome.IsChecked = _s.NoPhone;
            DefaultPortBox.Text = _s.DefaultPort.ToString();
            AutoQueryBox.IsChecked = _s.AutoQuery;
            MethodCombo.SelectedIndex = _s.ChangeMethod == "airplane" ? 1 : 0;
            OffSecondsBox.Text = _s.OffSeconds.ToString();
            AttemptsBox.Text = _s.MaxAttempts.ToString();
            New16Box.IsChecked = _s.RequireNew16;
            GoodLinkBox.IsChecked = _s.RequireGoodLink;
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
            ProtectOnStartBox.IsChecked = _s.ProtectOnStart;
            UseOffBox.IsChecked = _s.UseWithProtectionOff;
            CloseToTrayBox.IsChecked = _s.CloseToTray;
            AutoStreamerBox.IsChecked = _s.AutoStreamer;
            LaunchOptsBox.IsChecked = _s.Cs2LaunchOptions;
            DataWarnBox.Text = _s.DataWarnGb > 0 ? _s.DataWarnGb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "0";
            ApiKeyBox.Text = _s.SteamApiKey ?? "";
            ApiKeyPwd.Password = _s.SteamApiKey ?? "";
            ShowApiKey(false);
            ApiKeyLostText.Visibility = _s.ApiKeyLost && _s.SteamApiKey == null ? Visibility.Visible : Visibility.Collapsed;
            OptCs2.IsChecked = _s.LaunchCs2;
            OptSilent.IsChecked = _s.SteamSilent;
            OptOffline.IsChecked = _s.SteamOffline;
            OptStreamer.IsChecked = HideLogins;
            OptStreamer.IsEnabled = !Rec.On;
            AccSort.SelectedItem = AccSort.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.AccountSort) ?? AccSort.Items[0];
            ApplyAccountView();
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
        _s.RequireGoodLink = GoodLinkBox.IsChecked == true;
        _s.AutoJoinFollowed = AutoJoinBox.IsChecked == true;
        var launchOptsOn = !_s.Cs2LaunchOptions && LaunchOptsBox.IsChecked == true;
        _s.Cs2LaunchOptions = LaunchOptsBox.IsChecked == true;
        if (launchOptsOn) AddLaunchOptionsIfSteamClosed();
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
        _s.ProtectOnStart = ProtectOnStartBox.IsChecked == true;
        var key = ApiKeyBox.Visibility == Visibility.Visible ? ApiKeyBox.Text : ApiKeyPwd.Password;
        _s.SteamApiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (_s.SteamApiKey != null) ApiKeyLostText.Visibility = Visibility.Collapsed;
        OffSecondsBox.Text = _s.OffSeconds.ToString();
        AttemptsBox.Text = _s.MaxAttempts.ToString();
        SaveSettings();
        UpdateQuickInfo();
    }

    public const string UseOffWarning =
        "While protection is off, Servers, Join and Log in with CS2 use your home connection and the app blocks nothing.\n\n" +
        "Every server you join sees your home IP. A ban there can reach that IP, and changing the phone's IP won't help with it.\n\n" +
        "Turn on protection (Status or the tray icon) whenever you want to play through the phone. You can turn this off again here.";

    /// <summary>Settings > Use the app with protection off: a warning before it turns on; turning it off needs none.</summary>
    async void UseOff_Click(object sender, RoutedEventArgs e)
    {
        if (_uiLock || !IsLoaded) return;
        var on = UseOffBox.IsChecked == true;
        if (on && !_s.UseWithProtectionOff && !await Ask("Use the app with protection off?", UseOffWarning, "Use without protection", danger: true))
        {
            UseOffBox.IsChecked = false;
            return;
        }
        if (on == _s.UseWithProtectionOff) return;
        _s.UseWithProtectionOff = on;
        SaveSettings();
        Log.Info(on ? "Use the app with protection off: on. With protection off, Servers, Join and Log in use your home connection."
                    : "Use the app with protection off: off. With protection off, Servers, Join and Log in wait for protection again.");
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
            Toast($"{text} is already the {(rejoin ? "New IP + next account" : "rejoin")} hotkey.", false);
            return;
        }
        box.Text = text;
        if (rejoin) _s.HotkeyRejoin = text; else _s.HotkeySwitch = text;
        SaveSettings();
        UpdateQuickInfo();
        UpdateRejoinBtn();
        Log.Info($"{(rejoin ? "Change IP and rejoin" : "New IP + next account")} hotkey: {text}.");
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
        ApiKeyShowBtn.ToolTip = HideLogins ? "Hide usernames is on (Accounts): the key stays hidden." : show ? "Hides the key" : "Shows the key";
    }

    void SwitchSteps_Click(object sender, RoutedEventArgs e)
    {
        NavSettings.IsChecked = true;
        Dispatcher.BeginInvoke(() => SwitchGroup.BringIntoView(), DispatcherPriority.Loaded);
    }

    void SaveSettings() { if (!_demo) Store.Save(Paths.File("settings.json"), _s); }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!_protOn || _turningOff || !await Ask("Turn off protection?", TurnOffText, "Turn off protection", danger: true)) return;
        _protOn = false;
        if (_demo) { DemoProtection(false); return; }
        // Everything of protection goes and is read back (the Regions rule stays: regions don't depend on protection); then the
        // sync goes on with protection off, so the phone and accounts keep being watched (the tick no longer stops). Turn on waits
        // until this is done (_turningOff), so the toast below never says "off" over protection you just turned on again.
        _turningOff = true;
        string left;
        try
        {
            left = await _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: true);
            await SyncNow(force: true);
        }
        finally { _turningOff = false; }
        ShowRegionsStatus();
        if (left == null) Toast("Protection is off and checked. Turn on protection (Status or the tray icon) brings it back.", true);
        else Toast("Couldn't remove: " + left + ". Restart the PC to clear the routes, or run the app with --restore.", Tone.Error);
    }

    const string TurnOffText = "Turns off the kill switch, the routes and the CS2 lock. Until you click Turn on protection (in Status, or the tray icon), servers see your home IP. " +
        "Your servers stay saved, and the regions you turned off stay off (Allow all on Regions lifts them).";

    void OpenFirewall_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(FirewallConsole()); }
        catch (Exception ex) { Toast("Couldn't open the Firewall: " + ex.Message, Tone.Error); }
        Toast("In Outbound Rules, look for 'ServerRouteTool v9 - kill switch'.", true);

    }

    /// <summary>
    /// mmc.exe from System32 with wf.msc, never "open wf.msc" through the shell: as admin, the shell looks up what opens a .msc
    /// file in your own registry too (HKCU\Software\Classes), which any program of yours can change, and it would run as admin.
    /// </summary>
    public static ProcessStartInfo FirewallConsole()
    {
        var sys = Environment.SystemDirectory;
        var psi = new ProcessStartInfo(Path.Combine(sys, "mmc.exe")) { UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(sys, "wf.msc"));
        return psi;
    }

    void OpenData_Click(object sender, RoutedEventArgs e) =>
        ShellLaunch.OpenAsUser(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{Paths.Data}\"", null);   // not as admin
    void ApiKeyLink_Click(object sender, RoutedEventArgs e) => OpenUrl("https://steamcommunity.com/dev/apikey");
}
