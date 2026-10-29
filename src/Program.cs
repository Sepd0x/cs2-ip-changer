using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SRT.Core;

namespace SRT;

/// <summary>
/// Entry point. The single-file exe unpacks WPF's native DLLs to %TEMP%\.net, which any program of yours can change, and the host
/// never checks them again on the next start. The app runs as admin, so before any WPF type is touched it makes sure they come from
/// %ProgramData%\CS2IPChanger\net (admins only); if not, it starts itself again with the host pointed there.
/// </summary>
public static class Program
{
    /// <summary>Internal argument of that second start: it is never shown to App or MainWindow.</summary>
    public const string Moved = "--net-moved";

    [STAThread]
    static int Main(string[] args)
    {
        // The app's own P/Invokes (iphlpapi, wintrust, winmm...) only load from System32, never a copy planted next to the exe
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, _, _) => SystemDll(name));
        // Single file (no path for the assembly), as admin, and the DLLs came out somewhere else
#pragma warning disable IL3000
        var singleFile = string.IsNullOrEmpty(typeof(Program).Assembly.Location);
#pragma warning restore IL3000
        if (singleFile && Adb.Elevated && !NativeDirSafe(SafeBase))
        {
            // The second start still isn't admin-only: don't loop and don't load the DLLs.
            if (args.Contains(Moved))
            {
                Log.Error("The WPF files couldn't be unpacked to a folder only admins can change (" + SafeBase + "). The app didn't start.");
                MessageBoxW(IntPtr.Zero, "CS2 IP Changer couldn't unpack its files to a folder only admins can change, so it didn't start. Restart the PC and try again.", "CS2 IP Changer", 0x10);
                return 1;
            }
            try
            {
                Adb.SafeDir(Path.GetDirectoryName(SafeBase)!);
                Adb.SafeDir(SafeBase);
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
                foreach (var a in args) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add(Moved);
                psi.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = SafeBase;
                Process.Start(psi)?.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("The app couldn't restart from a safe folder: " + ex.Message);
                MessageBoxW(IntPtr.Zero, "CS2 IP Changer couldn't restart from a folder only admins can change: " + ex.Message, "CS2 IP Changer", 0x10);
                return 1;
            }
        }
        return RunApp();
    }

    // Separate method: WPF's assemblies only load when this runs, after the check.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>The DLL from System32, or nothing (then .NET's own search runs, for a name System32 doesn't have).</summary>
    public static IntPtr SystemDll(string name)
    {
        if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.IndexOfAny(new[] { '\\', '/' }) >= 0) return IntPtr.Zero;
        var file = Path.Combine(Environment.SystemDirectory, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll");
        return NativeLibrary.TryLoad(file, out var h) ? h : IntPtr.Zero;
    }

    static string SafeBase => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger", "net");

    /// <summary>
    /// The folders the host unpacked to (NATIVE_DLL_SEARCH_DIRECTORIES, apart from the exe's own folder) sit under <paramref name="safeBase"/>
    /// and nobody but admins can change them or any folder in between.
    /// </summary>
    public static bool NativeDirSafe(string safeBase, string searchDirs = null, string appDir = null)
    {
        searchDirs ??= AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "";
        appDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir ?? AppContext.BaseDirectory));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(safeBase));
        foreach (var entry in searchDirs.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string dir;
            try { dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry)); } catch { return false; }
            if (dir.Equals(appDir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            // Up to %ProgramData%\CS2IPChanger: a folder above that others can change would let them rename the one below
            for (var d = dir; d != null && d.Length >= Path.GetDirectoryName(root)!.Length; d = Path.GetDirectoryName(d))
                if (!Adb.AdminOnly(d)) return false;
        }
        return true;
    }

    /// <summary>
    /// The unpacked folders of other versions under the base (net\&lt;app&gt;\&lt;id&gt;): every one except the one this copy runs from and the
    /// folders above it. Links and junctions are never picked, at either level. Nothing when this copy doesn't run from the base.
    /// </summary>
    public static List<string> StaleUnpacked(string safeBase, string current)
    {
        var list = new List<string>();
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(safeBase));
            var cur = current == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
            if (cur == null || !cur.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root) || IsLink(root)) return list;
            foreach (var app in Directory.GetDirectories(root))
            {
                if (IsLink(app)) continue;
                foreach (var v in Directory.GetDirectories(app))
                {
                    var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(v));
                    if (IsLink(v) || full.Equals(cur, StringComparison.OrdinalIgnoreCase) || cur.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(full);
                }
            }
        }
        catch { }
        return list;
    }

    static bool IsLink(string path)
    {
        try { return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch { return true; }
    }

    /// <summary>The folder this copy's WPF files were unpacked to, when it is under the base (single-file build run as admin).</summary>
    static string CurrentUnpacked()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SafeBase));
        foreach (var entry in (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry));
                if (dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return dir;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// After the single-instance lock (no other copy is running from those folders): removes the unpacked folders of older versions
    /// from %ProgramData%\CS2IPChanger\net. Only inside that admin-only base, never through a link, best effort; the Log says what went.
    /// </summary>
    public static void CleanOldUnpacked()
    {
        try
        {
            if (!Adb.Elevated || !Directory.Exists(SafeBase) || !Adb.AdminOnly(SafeBase)) return;
            var stale = StaleUnpacked(SafeBase, CurrentUnpacked());
            var removed = stale.Count(DeleteTree);
            if (removed > 0) Log.Info($"Removed {Fmt.Count(removed, "unpacked folder")} of older versions from {SafeBase}.");
            if (removed < stale.Count) Log.Info($"{stale.Count - removed} older unpacked folder(s) in {SafeBase} are still in use; they go on a later start.");
        }
        catch (Exception ex) { Log.Warn("Cleaning up older unpacked versions: " + ex.Message); }
    }

    /// <summary>Deletes a folder's files and folders without ever going through a link (a link inside is removed, not what it points to).</summary>
    public static bool DeleteTree(string dir)
    {
        try
        {
            if (IsLink(dir)) { Directory.Delete(dir); return true; }
            foreach (var f in Directory.GetFiles(dir)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
            foreach (var d in Directory.GetDirectories(dir))
            {
                if (IsLink(d)) Directory.Delete(d);
                else if (!DeleteTree(d)) return false;
            }
            Directory.Delete(dir);
            return true;
        }
        catch { return false; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
