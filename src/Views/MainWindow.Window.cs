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
            (NavNet, PageNet, "Connection"), (NavServers, PageServers, "Servers"), (NavAccounts, PageAccounts, "Accounts"),
            (NavLog, PageLog, "Log"), (NavSettings, PageSettings, "Settings"),
        };
        foreach (var (nav, page, title) in map)
        {
            page.Visibility = nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (nav.IsChecked == true) PageTitle.Text = title;
        }
        if (NavAccounts.IsChecked == true && !_demo)
        {
            var stale = _accounts.Where(a => a.Meta.Profile == null || (DateTime.Now - a.Meta.Profile.Checked).TotalHours > 12).ToList();
            if (stale.Count > 0) _ = RefreshAccounts(stale);
        }
        if (NavLog.IsChecked == true) UpdateDiagInfo();
    }

    void LogCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, _log.Reverse().Select(l => $"{l.TimeText}  {l.Text}"));
        if (Rec.On) text = Rec.Scrub(text, PersonalWords().ToList());   // recording build: what you paste is as clean as the screen
        try { Clipboard.SetText(text); Toast("Log copied.", true); } catch { Toast("Couldn't copy. Try again.", false); }
    }

    void LogClear_Click(object sender, RoutedEventArgs e) => _log.Clear();

    // ------------------------------------------------------------ Hide logins also covers the Log on screen (the file keeps everything)

    /// <summary>With Hide logins on, the home IP and the account logins show as ••• in the Log page.</summary>
    void MaskLog(LogEntry e)
    {
        if (_s == null || !HideLogins) { e.Shown = null; return; }
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
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        RegisterHotkeys();
    }

    void RegisterHotkeys()
    {
        RegisterOne(HotkeyId, _s.HotkeySwitch, "the Switch button");
        RegisterOne(RejoinHotkeyId, _s.HotkeyRejoin, "the Change IP and rejoin button");
        RegisterDevHotkeys();
    }

    void RegisterOne(int id, string text, string instead)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!Hotkey.TryParse(text, out var mods, out var vk))
            Log.Warn($"The hotkey \"{text}\" can't be used (pick another one in Settings > Switch); use {instead}.");
        else if (!RegisterHotKey(_hwnd, id, mods | MOD_NOREPEAT, vk))
            Log.Warn($"The {text} hotkey is already in use by another program; pick another one in Settings > Switch, or use {instead}.");
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
            Log.Info($"Hotkey {_s.HotkeySwitch}: switch.");
            _ = QuickSwitch($"Hotkey {_s.HotkeySwitch}");
        }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == RejoinHotkeyId)
        {
            handled = true;
            _ = ChangeIpAndRejoin($"Hotkey {_s.HotkeyRejoin}");
        }
        else if (msg == WM_HOTKEY) OnDevHotkey(wParam.ToInt32(), ref handled);
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterHotkeys();
        base.OnClosed(e);
    }

    async void OnClosing(object sender, CancelEventArgs e)
    {
        if (_closingDone || _demo || !_s.CleanupOnExit) return;
        e.Cancel = true;
        // Removing the block with CS2 still in a match would send its next packets out with the home IP.
        if (Steam.Cs2Running && !_restored)
        {
            if (!await Ask("CS2 is still open", "Turning protection off now would send CS2's traffic through your home connection. Close CS2 first, or close the app and keep protection on this time.", "Keep protection and close")) return;
            _closingDone = true;
            Close();
            return;
        }
        _closingDone = true;
        ShowProgress("Undoing the routes and the kill switch…");

        try { await _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList()); } catch { }
        Close();
    }
}
