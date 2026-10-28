using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SRT.Core;

/// <summary>
/// Opens a program as the signed-in user, without the app's admin rights: the desktop shell (Explorer) starts it, like a double-click.
/// There is no fallback here that starts it from this process, because that would pass admin rights on
/// (Steam has one of its own, logged, so a broken Explorer doesn't stop the game from opening).
/// </summary>
public static class ShellLaunch
{
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    }

    // Only the slot order matters: the methods before the one used are never called.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB(); void SetStatusTextSB();
        void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
        [PreserveSig] int QueryActiveShellView(out IShellView view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellView
    {
        void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless(); void UIActivate(); void Refresh();
        void CreateViewWindow(); void DestroyViewWindow(); void GetCurrentInfo(); void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
        [PreserveSig] int GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    }

    static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    static Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static Guid ShellBrowserIid = new("000214E2-0000-0000-C000-000000000046");
    static Guid DispatchIid = new("00020400-0000-0000-C000-000000000046");
    const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1, SVGIO_BACKGROUND = 0;

    /// <summary>
    /// Starts the exe with no arguments. <paramref name="asAdmin"/>: Explorer asks for "Run as administrator", so Windows shows
    /// its UAC prompt and the program gets its own admin rights, never the app's. Null if it worked, otherwise the reason. UI thread (STA).
    /// </summary>
    public static string Open(string exe, bool asAdmin = false) =>
        IsLaunchable(exe, out var why) ? OpenAsUser(exe, "", Path.GetDirectoryName(exe), asAdmin ? "runas" : "open") : why;

    /// <summary>
    /// Opens a program (with arguments) or a link (steam://, https://) as the signed-in user, through Explorer.
    /// Null if it worked, otherwise the reason. No check on what it opens: the caller decides. Call it on the UI thread (STA).
    /// </summary>
    public static string OpenAsUser(string fileOrUrl, string args, string workDir, string verb = "open")
    {
        if (string.IsNullOrWhiteSpace(fileOrUrl)) return "nothing to open";
        try
        {
            dynamic windows = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellWindowsClsid, true)!);
            object loc = 0, root = null;   // CSIDL_DESKTOP
            object desktop = windows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out int _, SWFO_NEEDDISPATCH);
            if (desktop is not IServiceProvider sp || sp.QueryService(ref TopLevelBrowser, ref ShellBrowserIid, out var b) != 0 || b is not IShellBrowser browser)
                return "Explorer's desktop isn't running";
            if (browser.QueryActiveShellView(out var view) != 0 || view.GetItemObject(SVGIO_BACKGROUND, ref DispatchIid, out var bg) != 0)
                return "Explorer's desktop isn't ready";
            dynamic app = ((dynamic)bg).Application;
            app.ShellExecute(fileOrUrl, args ?? "", workDir ?? "", verb, 1);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>An existing .exe file, not CS2, Steam or this app.</summary>
    public static bool IsLaunchable(string exe, out string why)
    {
        why = null;
        if (string.IsNullOrWhiteSpace(exe) || !Path.IsPathFullyQualified(exe)) why = "not a full path";
        else if (!string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase)) why = "not an .exe file";
        else if (!File.Exists(exe)) why = "the file no longer exists";
        else if (new[] { "cs2", "steam", "steamwebhelper", Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "") }
                 .Contains(Path.GetFileNameWithoutExtension(exe), StringComparer.OrdinalIgnoreCase)) why = "the app already opens it";
        return why == null;
    }

    public static bool IsRunning(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        var ps = Process.GetProcessesByName(name);
        foreach (var p in ps) p.Dispose();
        return ps.Length > 0;
    }
}
