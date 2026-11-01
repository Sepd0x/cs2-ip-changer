using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SRT.Core;

/// <summary>
/// The icon in the notification area (next to the clock) and its menu. Windows' own Shell_NotifyIcon and popup menu on a hidden
/// window of the app: no Windows Forms, nothing new to ship.
/// </summary>
public sealed class Tray : IDisposable
{
    /// <summary>A menu line. Text null = separator. Bold = what a click on the icon does.</summary>
    public sealed record Item(string Text, Action Run = null, bool Enabled = true, bool Bold = false);

    readonly HwndSource _win;
    readonly Func<IReadOnlyList<Item>> _menu;
    readonly Action _open;
    readonly IntPtr _icon;
    readonly uint _taskbarCreated;
    string _tip = "CS2 IP Changer";
    bool _added;

    const int CallbackMsg = 0x8000 + 0x51;   // WM_APP + 0x51
    const uint Id = 1;

    /// <param name="menu">Built each time the menu opens, so it shows the state of that moment.</param>
    /// <param name="open">A click on the icon (the right button opens the menu).</param>
    public Tray(Func<IReadOnlyList<Item>> menu, Action open)
    {
        _menu = menu;
        _open = open;
        // A hidden top-level window (not message-only: those never get "TaskbarCreated" when Explorer restarts).
        _win = new HwndSource(new HwndSourceParameters("CS2IPChangerTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _win.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        // The app runs as admin and Explorer doesn't: Windows drops its messages to a higher window unless they are let through.
        ChangeWindowMessageFilterEx(_win.Handle, CallbackMsg, MsgFltAllow, IntPtr.Zero);
        ChangeWindowMessageFilterEx(_win.Handle, _taskbarCreated, MsgFltAllow, IntPtr.Zero);
        var small = new IntPtr[1];
        if (Environment.ProcessPath != null && ExtractIconExW(Environment.ProcessPath, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero) _icon = small[0];
        Add();
    }

    /// <summary>Windows took the icon (false: no notification area, Explorer not running).</summary>
    public bool Shown => _added;

    /// <summary>The text shown when the pointer rests on the icon (127 characters at most).</summary>
    public string Tip
    {
        get => _tip;
        set
        {
            var t = TrimTip(value);
            if (t == _tip) return;
            _tip = t;
            if (_added) Notify(NimModify, NifTip);
        }
    }

    public static string TrimTip(string text)
    {
        text = string.IsNullOrWhiteSpace(text) ? "CS2 IP Changer" : text.Trim();
        return text.Length <= 127 ? text : text[..126] + "…";
    }

    /// <summary>A Windows notification from the icon (what the app does when it goes to the notification area).</summary>
    public void Balloon(string title, string text)
    {
        if (!_added) return;
        var d = Data(NifInfo);
        d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        d.szInfo = text.Length > 255 ? text[..255] : text;
        d.dwInfoFlags = NiifInfo | NiifLargeIcon;
        Shell_NotifyIconW(NimModify, ref d);
    }

    void Add()
    {
        _added = Notify(NimAdd, NifMessage | NifIcon | NifTip);
    }

    bool Notify(uint what, uint flags)
    {
        var d = Data(flags);
        return Shell_NotifyIconW(what, ref d);
    }

    NotifyIconData Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _win.Handle,
        uID = Id,
        uFlags = flags,
        uCallbackMessage = CallbackMsg,
        hIcon = _icon,
        szTip = _tip,
    };

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMsg)
        {
            var mouse = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouse == WmLButtonUp || mouse == WmLButtonDblClk) { handled = true; if (_open != null) _win.Dispatcher.BeginInvoke(_open); }
            else if (mouse == WmRButtonUp) { handled = true; ShowMenu(); }
        }
        else if (_taskbarCreated != 0 && msg == (int)_taskbarCreated) Add();   // Explorer restarted: the icon is put back
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        var items = _menu?.Invoke() ?? [];
        var m = CreatePopupMenu();
        if (m == IntPtr.Zero) return;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Text == null) { AppendMenuW(m, MfSeparator, UIntPtr.Zero, null); continue; }
                AppendMenuW(m, MfString | (it.Enabled && it.Run != null ? 0 : MfGrayed), (UIntPtr)(uint)(i + 1), it.Text);
                if (it.Bold) SetMenuDefaultItem(m, (uint)(i + 1), 0);
            }
            GetCursorPos(out var pt);
            // Without this the menu stays open when you click somewhere else (documented Windows behaviour for tray menus).
            SetForegroundWindow(_win.Handle);
            var cmd = TrackPopupMenuEx(m, TpmReturnCmd | TpmRightButton | TpmNoNotify, pt.X, pt.Y, _win.Handle, IntPtr.Zero);
            PostMessageW(_win.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            // After this message is done: Quit closes the window, which disposes this icon and its window.
            if (cmd > 0 && cmd <= items.Count && items[cmd - 1].Run is Action run) _win.Dispatcher.BeginInvoke(run);
        }
        finally { DestroyMenu(m); }
    }

    public void Dispose()
    {
        if (_added) Notify(NimDelete, 0);
        _added = false;
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _win.Dispose();
    }

    // ------------------------------------------------------------ Windows

    const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NifInfo = 0x10;
    const uint NiifInfo = 1, NiifLargeIcon = 0x20;
    const int WmLButtonUp = 0x202, WmLButtonDblClk = 0x203, WmRButtonUp = 0x205;
    const uint MfString = 0, MfGrayed = 1, MfSeparator = 0x800;
    const uint TpmRightButton = 2, TpmNoNotify = 0x80, TpmReturnCmd = 0x100;
    const uint MsgFltAllow = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconExW(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr info);
    static bool ChangeWindowMessageFilterEx(IntPtr hwnd, int msg, uint action, IntPtr info) => ChangeWindowMessageFilterEx(hwnd, (uint)msg, action, info);
    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")]
    static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);
    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
}
