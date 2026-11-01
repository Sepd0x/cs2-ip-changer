using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SRT.Core;

namespace SRT;

/// <summary>
/// Entry point. The single-file exe unpacks WPF's native DLLs to %TEMP%\.net, which any program of yours can change, and the host
/// never checks them again on the next start. The app runs as admin, so before any WPF type is touched it makes sure they come from
/// %ProgramData%\CS2IPChanger\net (admins only); if not, it starts itself again with the host pointed there. The same second start
/// drops the runtime variables (CORECLR_PROFILER...) that would load a DLL into it, and as admin it never starts from a folder
/// programs without admin rights can change.
/// </summary>
public static class Program
{
    /// <summary>Internal argument of that second start: it is never shown to App or MainWindow.</summary>
    public const string Moved = "--net-moved";

    [STAThread]
    static int Main(string[] args)
    {
        // Windows looks for a DLL loaded by name (UMPDC, WindowsCodecs, DEVOBJ, ncrypt...) in the exe's folder and the current folder
        // first. From here on only System32 counts. What the host loaded before Main isn't covered: that is what the folder check is for.
        LockDllSearch();
        // The app's own P/Invokes (iphlpapi, wintrust, winmm...) only load from System32, never a copy planted next to the exe
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, _, _) => SystemDll(name));
#if !DEBUG
        // As admin from a folder any program can change (Downloads, Desktop): a DLL put next to the exe would run as admin on the next
        // start, before any check here. So the app doesn't start there. --restore still works: it's the way out before deleting the folder.
        if (Adb.Elevated && !args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase)) && FolderProblem(Paths.AppDir, Paths.Data) is string why)
        {
            MessageBoxW(IntPtr.Zero, why, "CS2 IP Changer", 0x10);
            return 1;
        }
#endif
        // Single file (no path for the assembly), as admin, and the DLLs came out somewhere else
#pragma warning disable IL3000
        var singleFile = string.IsNullOrEmpty(typeof(Program).Assembly.Location);
#pragma warning restore IL3000
        var moveNet = singleFile && Adb.Elevated && !NativeDirSafe(SafeBase);
        // CORECLR_PROFILER and the like come from your user environment even into an elevated app and load a DLL into it.
        var runtimeVars = Adb.Elevated ? RuntimeVars(Environment.GetEnvironmentVariables().Keys.Cast<string>()) : new List<string>();
        if (moveNet || runtimeVars.Count > 0)
        {
            // The second start still isn't safe: don't loop and don't load the DLLs.
            if (args.Contains(Moved))
            {
                var what = moveNet ? "unpack its files to a folder only admins can change" : "start without the runtime variables " + string.Join(", ", runtimeVars);
                Log.Error($"The app couldn't {what}. It didn't start.");
                MessageBoxW(IntPtr.Zero, $"CS2 IP Changer couldn't {what}, so it didn't start. Restart the PC and try again.", "CS2 IP Changer", 0x10);
                return 1;
            }
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
                foreach (var a in args) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add(Moved);
                foreach (var k in RuntimeVars(psi.Environment.Keys)) psi.Environment.Remove(k);
                if (singleFile)
                {
                    Adb.SafeDir(Path.GetDirectoryName(SafeBase)!);
                    Adb.SafeDir(SafeBase);
                    psi.Environment[ExtractBaseVar] = SafeBase;
                }
                if (runtimeVars.Count > 0)
                    Log.Warn($"Your environment sets {string.Join(", ", runtimeVars)}, which can load code into .NET apps. The app started again without them; if you didn't set them, remove them in Windows' environment variables.");
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

    const string ExtractBaseVar = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    /// <summary>
    /// DLLs loaded by name come only from System32 (and folders added on purpose, none today); never the exe's folder, the current
    /// folder or PATH. DLLs loaded by full path (WPF's, from the admin-only unpack folder) still find their neighbours.
    /// </summary>
    public static bool LockDllSearch()
    {
        const uint System32 = 0x800, UserDirs = 0x400;   // LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_USER_DIRS
        var ok = SetDefaultDllDirectories(System32 | UserDirs);
        return SetDllDirectoryW("") && ok;
    }

    /// <summary>
    /// The variables of the .NET runtime (CORECLR_, COR_, COMPlus_, DOTNET_): a profiler DLL, a diagnostics port, another GC or JIT.
    /// The app needs none of them; only the unpack folder it sets itself is left.
    /// </summary>
    public static List<string> RuntimeVars(IEnumerable<string> names) => names
        .Where(n => n != null && !n.Equals(ExtractBaseVar, StringComparison.OrdinalIgnoreCase)
            && new[] { "CORECLR_", "COR_", "COMPlus_", "DOTNET_" }.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    /// <summary>
    /// Why the app can't run as admin from here (null = it can): the exe's folder, or the SRT_Dados next to it, can be changed by
    /// programs without admin rights.
    /// </summary>
    public static string FolderProblem(string appDir, string dataDir)
    {
        appDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir));
        const string Fix = "Close this, create the folder C:\\Program Files\\CS2 IP Changer, copy CS2IPChanger.exe into it (and your SRT_Dados folder, if you have one: copy it, don't move it) and start the app from there.";
        if (Adb.UsersCanWrite(appDir))
            return $"CS2 IP Changer runs as admin, and this folder can be changed by programs without admin rights:\n{appDir}\n\nA file put next to the app there would run with admin rights, so the app doesn't start from it.\n\n{Fix}";
        if ((Directory.Exists(dataDir) || File.Exists(dataDir)) && Adb.UsersCanWrite(dataDir))
            return $"CS2 IP Changer runs as admin, and its data folder can be changed by programs without admin rights:\n{dataDir}\n\nThis happens when SRT_Dados is moved instead of copied. Close this, copy SRT_Dados to another place, delete the one here and copy it back, then start the app again.";
        return null;
    }

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

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetDefaultDllDirectories(uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectoryW(string path);
}
