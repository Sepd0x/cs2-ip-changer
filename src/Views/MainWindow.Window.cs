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

// Navegação e janela
public partial class MainWindow
{
    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageNet == null) return;
        var map = new (RadioButton nav, FrameworkElement page, string title)[]
        {
            (NavNet, PageNet, "Ligação"), (NavServers, PageServers, "Servidores"), (NavAccounts, PageAccounts, "Contas Steam"),
            (NavLog, PageLog, "Registo"), (NavSettings, PageSettings, "Definições"),
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
        try { Clipboard.SetText(text); Toast("Registo copiado.", true); } catch { Toast("Não consegui copiar. Tenta outra vez.", false); }
    }

    void LogClear_Click(object sender, RoutedEventArgs e) => _log.Clear();

    void SideStatus_Click(object sender, MouseButtonEventArgs e) => NavNet.IsChecked = true;
    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------ Atalho global (Ctrl+Alt+Q = troca rápida)

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const int HotkeyId = 0x5157;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const int WM_HOTKEY = 0x0312;
    IntPtr _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_demo) return;
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        if (!RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.Q)))
            Log.Warn("O atalho Ctrl+Alt+Q já está a ser usado por outro programa; usa o botão Troca rápida.");
        RegisterDevHotkeys();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Log.Info("Atalho Ctrl+Alt+Q: troca rápida.");
            _ = QuickSwitch();
        }
        else if (msg == WM_HOTKEY) OnDevHotkey(wParam.ToInt32(), ref handled);
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwnd != IntPtr.Zero) { UnregisterHotKey(_hwnd, HotkeyId); UnregisterDevHotkeys(); }
        base.OnClosed(e);
    }

    async void OnClosing(object sender, CancelEventArgs e)
    {
        if (_closingDone || _demo || !_s.CleanupOnExit) return;
        e.Cancel = true;
        _closingDone = true;
        ShowProgress("A desfazer as rotas e o kill switch…");
        try { await _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList()); } catch { }
        Close();
    }
}
