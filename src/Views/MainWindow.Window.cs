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

// Navigation and window
public partial class MainWindow
{
    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageNet == null) return;
        var map = new (RadioButton nav, FrameworkElement page, string title)[]
        {
            (NavNet, PageNet, "Connection"), (NavServers, PageServers, "Servers"), (NavRegions, PageRegions, "Regions"), (NavAccounts, PageAccounts, "Accounts"),
            (NavConfigs, PageConfigs, "Configs"), (NavProfiles, PageProfiles, "Profiles"), (NavLog, PageLog, "Log"), (NavSettings, PageSettings, "Settings"),
        };
        foreach (var (nav, page, title) in map)
        {
            page.Visibility = nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (nav.IsChecked == true) PageTitle.Text = title;
        }
        if (IsVisible) Telemetry.Screen(TelemetryScreen());
        if (NavAccounts.IsChecked == true && !_demo)
        {
            var stale = _accounts.Where(a => a.Meta.Profile == null || Fmt.Stale(a.Meta.Profile.Checked, TimeSpan.FromHours(12))).ToList();
            if (stale.Count > 0) _ = RefreshAccounts(stale);
        }
        if (NavLog.IsChecked == true) UpdateDiagInfo();
        if (NavConfigs.IsChecked == true) ShowConfigs();
        if (NavSettings.IsChecked == true) ShowLaunchOptions();
    }

    void LogCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, _log.Reverse().Select(l => $"{l.TimeText}  {l.Text}"));
        if (Rec.On) text = Rec.Scrub(text, PersonalWords().ToList());   // recording build: what you paste is as clean as the screen
        try { Clipboard.SetText(text); Toast("Log copied.", true); } catch { Toast("Couldn't copy. Try again.", false); }
    }

    void LogClear_Click(object sender, RoutedEventArgs e) => _log.Clear();

    // ------------------------------------------------------------ Log filter (beta.10): by kind and a search. Copy all still copies everything.

    System.ComponentModel.ICollectionView _logView;

    /// <summary>A line passes: kind "all", "warn" (warnings and errors) or "error", and the search in the text shown (masked with Hide logins).</summary>
    public static bool LogShows(LogEntry e, string kind, string q) =>
        (kind switch { "warn" => e.Level is LogLevel.Warn or LogLevel.Error, "error" => e.Level == LogLevel.Error, _ => true })
        && (string.IsNullOrWhiteSpace(q) || (e.Shown ?? "").Contains(q.Trim(), StringComparison.OrdinalIgnoreCase));

    void LogFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (LogList == null || LogSearch == null) return;   // the radio buttons fire while the window is still being built
        _logView ??= System.Windows.Data.CollectionViewSource.GetDefaultView(_log);
        var kind = LogErr.IsChecked == true ? "error" : LogWarn.IsChecked == true ? "warn" : "all";
        var q = LogSearch.Text;
        _logView.Filter = kind == "all" && string.IsNullOrWhiteSpace(q) ? null : o => o is LogEntry l && LogShows(l, kind, q);
        LogCount.Text = _logView.Filter == null ? "" : $"{_logView.Cast<object>().Count()} of {_log.Count} lines";
    }

    // ------------------------------------------------------------ Hide logins also covers the Log on screen (the file keeps everything)

    /// <summary>With Hide logins on, the home IP and the account logins show as ••• in the Log page.</summary>
    void MaskLog(LogEntry e)
    {
        if (_s == null || !HideLogins || Rec.On) { e.Shown = null; return; }   // recording build: blurred on screen instead
        var text = e.Text ?? "";
        foreach (var ip in new[] { _s.LastHomeIp, _homePublic }.Where(x => !string.IsNullOrEmpty(x)).Distinct())
            text = Regex.Replace(text, @"(?<![\d.])" + Regex.Escape(ip) + @"(?![\d])", "•••");
        foreach (var login in _accounts.Select(a => a.Login).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase))
            text = Regex.Replace(text, @"(?<![\w])" + Regex.Escape(login) + @"(?![\w])", "•••", RegexOptions.IgnoreCase);
        e.Shown = text == e.Text ? null : text;
    }

    /// <summary>After Hide logins changes, or a new home IP or account list is known.</summary>
    void RemaskLog() { foreach (var e in _log) MaskLog(e); }

    void SideStatus_Click(object sender, MouseButtonEventArgs e) => NavNet.IsChecked = true;
    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------ Global hotkeys (Settings > Switch; by default Ctrl+Shift+F9 = switch, Ctrl+Shift+F10 = change IP and rejoin)

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const int HotkeyId = 0x5157, RejoinHotkeyId = 0x5152;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const int WM_HOTKEY = 0x0312;
    IntPtr _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_demo) return;
        if (Application.Current != null) Application.Current.SessionEnding += (_, _) => OnSessionEnding();
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        RegisterHotkeys();
        StartTray();
        ListenForShow();
        ListenForUninstall();
    }

    void RegisterHotkeys()
    {
        RegisterOne(HotkeyId, _s.HotkeySwitch, "the New IP + next account button");
        RegisterOne(RejoinHotkeyId, _s.HotkeyRejoin, "the Change IP and rejoin button");
        RegisterDevHotkeys();
    }

    void RegisterOne(int id, string text, string instead)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!Hotkey.TryParse(text, out var mods, out var vk))
            Log.Warn($"The hotkey \"{text}\" can't be used (pick another one in Settings > Hotkeys and sounds); use {instead}.");
        else if (!RegisterHotKey(_hwnd, id, mods | MOD_NOREPEAT, vk))
            Log.Warn($"The {text} hotkey is already in use by another program; pick another one in Settings > Hotkeys and sounds, or use {instead}.");
    }

    void UnregisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, HotkeyId);
        UnregisterHotKey(_hwnd, RejoinHotkeyId);
        UnregisterDevHotkeys();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Telemetry.Click("Hotkey_Switch");
            Log.Info($"Hotkey {_s.HotkeySwitch}: switch.");
            _ = SwitchFromHotkey($"Hotkey {_s.HotkeySwitch}");
        }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == RejoinHotkeyId)
        {
            handled = true;
            Telemetry.Click("Hotkey_Rejoin");
            _ = RejoinFromHotkey($"Hotkey {_s.HotkeyRejoin}");
        }
        else if (msg == WM_HOTKEY) OnDevHotkey(wParam.ToInt32(), ref handled);
        return IntPtr.Zero;
    }

    /// <summary>The first close with protection on. How the next start goes follows Turn on protection when the app starts (Guard.OnAtStart).</summary>
    public static string CloseKeepText(string blocked, bool protectOnStart) =>
        $"{blocked} stay blocked outside the phone after you close the app. Without the phone they can't connect until you open the app again "
        + (protectOnStart ? "(it starts with protection on: Settings > Protection)." : "(it starts with protection off, unless CS2 is still open).")
        + "\n\nThe app remembers your answer. To change it: Settings > Protection > Turn off protection when closing the app.";

    /// <summary>Windows is shutting down or signing out: closing goes through without questions.</summary>
    bool _sessionEnding, _closeAsking;

    protected override void OnClosed(EventArgs e)
    {
        UnregisterHotkeys();
        _tray?.Dispose();
        _tray = null;
        // The data counted since the last save. Closing goes on if it can't be written, but the Log says why.
        if (!_demo && _s.DataMonthBytes > 0) { try { SaveSettings(); } catch (Exception ex) { Log.Warn("Couldn't save the settings while closing: " + ex.Message); } }
        base.OnClosed(e);
    }

    /// <summary>
    /// The window never opens bigger than the screen's work area (1366x768 at 125% leaves about 590 of height, under MinHeight):
    /// the pages scroll instead. Before the window is shown, so CenterScreen centres the size that fits.
    /// </summary>
    void ClampToWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        (MinWidth, MinHeight, Width, Height) = FitWorkArea(MinWidth, MinHeight, Width, Height, wa.Width, wa.Height);
    }

    public static (double MinW, double MinH, double W, double H) FitWorkArea(double minW, double minH, double w, double h, double areaW, double areaH) =>
        (Math.Min(minW, areaW), Math.Min(minH, areaH), Math.Min(w, areaW), Math.Min(h, areaH));

    /// <summary>
    /// Before a question in OnClosing (e.Cancel already set): with the window hidden in the notification area (Quit in the icon's
    /// menu), the question has to show it, and WPF refuses Show while the window is still closing. Letting the Closing event end first
    /// fixes that; with the window on screen nothing waits.
    /// </summary>
    async Task AfterCancelledClose()
    {
        if (!IsVisible) await System.Windows.Threading.Dispatcher.Yield();
    }

    /// <summary>
    /// The close is under way (undoing the routes and the kill switch, Keep giving the phone its route back, Remove): a second X,
    /// Alt+F4 or Quit waits for it instead of ending the app halfway. Not when Windows signs out (WPF closes the window anyway).
    /// </summary>
    public static bool CloseWaits(bool closeBusy, bool sessionEnding) => closeBusy && !sessionEnding;

    /// <summary>What Turn off and close couldn't remove, said before the app goes (null: nothing to say). Never while Windows is shutting down.</summary>
    public static string CloseLeftText(string left, bool sessionEnding) => sessionEnding ? null : left;

    /// <summary>Windows signing out or shutting down: with Turn off protection when closing, and CS2 closed, every protection rule goes.</summary>
    public static bool CleanAtSessionEnd(bool cleanupOnExit, bool protOn, bool cs2Running) => cleanupOnExit && protOn && !cs2Running;

    bool _closeBusy;

    /// <summary>
    /// Windows is signing out or shutting down: WPF closes the window without waiting for anything after the first await, so the
    /// Firewall rules (they survive a restart) go here, first and with a short limit. Turn off protection when closing: all of them
    /// (the Regions rule stays); Keep, or CS2 still open: the Whole PC rules, as closing with Keep does. Routes and the phone's
    /// metric go with a restart. Not for an update (the new copy keeps the rules) or a close already under way.
    /// </summary>
    void OnSessionEnding()
    {
        _sessionEnding = true;
        if (_demo || !_protOn || _restored || _closingDone) return;
        _tick.Stop();
        var all = CleanAtSessionEnd(_s.CleanupOnExit, _protOn, Steam.Cs2Running);
        var done = _guard.RemoveRulesNow(all ? Firewall.RulesToRemove(keepRegions: true) : Firewall.WholePcRules, TimeSpan.FromSeconds(2));
        Log.Info("Windows is signing out: " + (all ? "protection turned off" : "the Whole PC rules removed, the block stays") + (done ? "." : " (not confirmed in time)."));
    }

    async void OnClosing(object sender, CancelEventArgs e)
    {
        if (CloseWaits(_closeBusy, _sessionEnding)) { e.Cancel = true; return; }
        // Settings > App > Close to the notification area: the X only hides the window; Quit in the icon's menu closes the app.
        // Only with the icon really there: a tray Windows refused (no notification area) would leave the app hidden with no way back
        if (GoesToTray(_s?.CloseToTray == true, _tray?.Shown == true, _quitting, _sessionEnding, _closingDone, _demo))
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        // Closing mid-run would cut an account off halfway through its changes.
        if (_profBusy && !_closingDone)
        {
            e.Cancel = true;
            await AfterCancelledClose();
            if (!await Ask("Profiles are still changing", "Stop after the current account and close?", "Stop and close")) { _quitting = false; return; }
            _profCts?.Cancel();
            while (_profBusy) await Task.Delay(200);
            Close();
            return;
        }
        if (_closingDone || _demo) return;
        // The first close with protection on asks once what to do with it, and the answer is kept (Settings > Protection).
        // Never for a Windows shutdown or sign-out (nothing may hold that up); an update sets _closingDone before it closes.
        if (_closeAsking) { e.Cancel = true; return; }   // the question is already on screen
        if (!_s.CleanupOnExit && !_s.CloseAsked && !_sessionEnding && ProtectionArmed)
        {
            e.Cancel = true;
            _closeAsking = true;
            bool keep;
            try
            {
                await AfterCancelledClose();
                keep = await Ask("Close the app", CloseKeepText(Blocked(_last), _s.ProtectOnStart), "Keep and close", "Turn off and close");
            }
            finally { _closeAsking = false; }
            if (!keep && !_dialogCancelClicked) { _quitting = false; return; }   // Escape, or another question came up: the app stays open
            _s.CloseAsked = true;
            _s.CleanupOnExit = !keep;
            _uiLock = true;
            try { CleanupBox.IsChecked = _s.CleanupOnExit; } finally { _uiLock = false; }
            SaveSettings();
            Log.Info(keep ? "Closing: protection stays on after the app closes (asked once)." : "Closing: protection is turned off when the app closes (asked once).");
            Close();
            return;
        }
        // Keep (CleanupOnExit off) keeps a block that is on; with protection off there is no block, so the app's own test route and
        // Save mobile data go like with Turn off and close
        if (!_s.CleanupOnExit && _protOn)
        {
            e.Cancel = true;
            await KeepAndClose();
            return;
        }
        e.Cancel = true;
        // Removing the block with CS2 still in a match would send its next packets out with the home IP. In No phone mode it already
        // goes through home, so there is nothing to ask (not ProtectionArmed: Whole PC with no servers and no CS2 lock still moves CS2).
        if (Steam.Cs2Running && !_restored && _protOn && _last?.Unprotected != true)
        {
            // Windows signing out or shutting down: no question may hold that up, so the block stays as with Keep
            if (!_sessionEnding)
            {
                await AfterCancelledClose();
                if (!await Ask("CS2 is still open", "Turning protection off now would send CS2's traffic through your home connection. Close CS2 first, or close the app and keep protection on this time.", "Keep protection and close")) { _quitting = false; return; }
            }
            await KeepAndClose();
            return;
        }
        _closingDone = true;
        _closeBusy = true;
        // Same as Restore network: no tick may put the rules back while RestoreAll runs or while the process exits.
        _restored = true;
        _tick.Stop();
        ShowProgress("Undoing the routes and the kill switch…");

        // A Regions write still running finishes first (10 s at most), so the app doesn't close halfway through it. The Regions rule
        // itself stays: regions don't depend on protection.
        var got = await _regionGate.WaitAsync(TimeSpan.FromSeconds(10));
        string left = null;
        try { left = await _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: true); }
        catch (Exception ex) { left = ex.Message; Log.Error("Closing: turning off protection stopped: " + ex.Message); }
        finally { if (got) _regionGate.Release(); }
        // What stayed is said before the app goes, with where to remove it (the same window as --restore)
        if (CloseLeftText(left, _sessionEnding) is string what)
        {
            HideProgress();
            App.RestoredWindow(what, false).ShowAndWait();
        }
        _closeBusy = false;
        Close();
    }

    /// <summary>
    /// Keep: the kill switch and the CS2 lock stay, the Whole PC rules, the phone's default route and (CS2 closed) the Whole PC routes
    /// go (Guard.KeepOnClose), so the PC isn't left without internet once the phone is gone. No sync puts anything back meanwhile.
    /// </summary>
    async Task KeepAndClose()
    {
        _closingDone = true;
        _closeBusy = true;
        _restored = true;
        _tick.Stop();
        try { await _guard.KeepOnClose(Steam.Cs2Running); }
        catch (Exception ex) { Log.Error("Closing: the Whole PC rules or the phone's default route weren't given back: " + ex.Message); }
        _closeBusy = false;
        Close();
    }
}
