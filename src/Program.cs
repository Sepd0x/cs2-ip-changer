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
/// programs without admin rights can change: there it only offers to install itself in Program Files (Core/Installer.cs).
/// </summary>
public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The app's own P/Invokes (kernel32, iphlpapi, wintrust, winmm...) only load from System32, never a copy planted next to the exe.
        // First thing, so LockDllSearch's own call goes through it too. .NET's CoreLib doesn't use this resolver and looks in the exe's
        // folder first (by full path) before and after Main: that is why the folder check below exists.
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, _, _) => SystemDll(name));
        // Windows looks for a DLL loaded by name (UMPDC, WindowsCodecs, DEVOBJ, ncrypt...) in the exe's folder and the current folder
        // first. From here on only System32 counts. What the host loaded before Main isn't covered: that is what the folder check is for.
        var locked = LockDllSearch();
        // As admin, a search Windows wouldn't lock is the same hole as the folder below: the app doesn't start. --restore still works.
        // No Log line here: it would write as admin into SRT_Dados before the folder check below.
        if (DllSearchProblem(locked, Adb.Elevated, args) is string dllWhy)
        {
            MessageBoxW(IntPtr.Zero, dllWhy, "CS2 IP Changer", 0x10);
            return 1;
        }
        // As admin from a folder any program can change (Downloads, Desktop): a DLL put next to the exe would run as admin on the next
        // start, before any check here. So the app doesn't run there, and writes nothing next to it (not even the log). It offers to
        // install itself in Program Files instead: this process only copies itself to an admin-only folder and starts that copy, which
        // shows the Install window (Core/Installer.cs). Nothing of the app's window ever loads from here. --restore still works: it's
        // the way out before deleting the folder.
        string folderWhy = null;
        var appDirUnsafe = false;
#if !DEBUG
        if (Adb.Elevated && !AppIds.Args.IsRestore(args)) folderWhy = FolderProblem(Paths.AppDir, Paths.Data);
        appDirUnsafe = Adb.Elevated && Adb.UsersCanWrite(Paths.AppDir);   // --restore from Downloads too
#endif
        var staged = Installer.SameDir(Paths.AppDir, Installer.StageDir);
        if (folderWhy != null || staged || appDirUnsafe) Paths.ReadOnly = true;
        // Single file (no path for the assembly), as admin, and the DLLs came out somewhere else
#pragma warning disable IL3000
        var singleFile = string.IsNullOrEmpty(typeof(Program).Assembly.Location);
#pragma warning restore IL3000
        if (folderWhy != null && (staged || appDirUnsafe))
        {
            if (staged || !Installer.Offer(Paths.AppDir)) { MessageBoxW(IntPtr.Zero, folderWhy, "CS2 IP Changer", 0x10); return 1; }
            return StageInstall(singleFile, folderWhy);
        }
        var moveNet = singleFile && Adb.Elevated && !NativeDirSafe(SafeBase);
        // CORECLR_PROFILER and the like come from your user environment even into an elevated app and load a DLL into it.
        var runtimeVars = Adb.Elevated ? RuntimeVars(Environment.GetEnvironmentVariables().Keys.Cast<string>()) : new List<string>();
        if (moveNet || runtimeVars.Count > 0)
        {
            // The second start still isn't safe: don't loop and don't load the DLLs.
            if (args.Contains(AppIds.Args.NetMoved))
            {
                var what = moveNet ? "unpack its files to a folder only admins can change" : "start without the runtime variables " + string.Join(", ", runtimeVars);
                if (folderWhy == null) Log.Error($"The app couldn't {what}. It didn't start.");
                MessageBoxW(IntPtr.Zero, $"CS2 IP Changer couldn't {what}, so it didn't start. Restart the PC and try again.", "CS2 IP Changer", 0x10);
                return 1;
            }
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
                foreach (var a in args) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add(AppIds.Args.NetMoved);
                foreach (var k in RuntimeVars(psi.Environment.Keys)) psi.Environment.Remove(k);
                if (singleFile)
                {
                    Adb.SafeDir(Path.GetDirectoryName(SafeBase)!);
                    Adb.SafeDir(SafeBase);
                    psi.Environment[ExtractBaseVar] = SafeBase;
                }
                if (runtimeVars.Count > 0 && folderWhy == null)
                    Log.Warn($"Your environment sets {string.Join(", ", runtimeVars)}, which can load code into .NET apps. The app started again without them; if you didn't set them, remove them in Windows' environment variables.");
                // Started again by its path: from a folder others can change (--restore there), held and checked to be the running
                // file first, so nothing swapped in is what starts
                using (appDirUnsafe ? Installer.OpenSelf() : null) Process.Start(psi)?.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                if (folderWhy == null) Log.Error("The app couldn't restart from a safe folder: " + ex.Message);
                MessageBoxW(IntPtr.Zero, "CS2 IP Changer couldn't restart from a folder only admins can change: " + ex.Message, "CS2 IP Changer", 0x10);
                return 1;
            }
        }
        if (staged) return Installer.StagedSource(args, Paths.AppDir) is string from ? RunInstall(from) : 1;
        return folderWhy != null ? RunFolderProblem(folderWhy) : RunApp();
    }

    /// <summary>
    /// Install, step 1, in the copy started from Downloads: no window here (WPF must not load next to this exe). It copies the running
    /// exe, held and checked, to the admin-only <see cref="Installer.StageDir"/> and starts that copy, which shows the Install window.
    /// </summary>
    static int StageInstall(bool singleFile, string why)
    {
        try
        {
            string exe;
            using (var self = Installer.OpenSelf()) exe = Installer.Stage(self);
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            psi.ArgumentList.Add(AppIds.Args.InstallFrom);
            psi.ArgumentList.Add(Paths.AppDir);
            foreach (var k in RuntimeVars(psi.Environment.Keys)) psi.Environment.Remove(k);
            if (singleFile)
            {
                Adb.SafeDir(Path.GetDirectoryName(SafeBase)!);
                Adb.SafeDir(SafeBase);
                psi.Environment[ExtractBaseVar] = SafeBase;
            }
            Process.Start(psi)?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            MessageBoxW(IntPtr.Zero, Installer.StageBusy() ? "The Install window of CS2 IP Changer is already open. Look for it in the taskbar."
                : why + "\n\n(The Install window couldn't open: " + ex.Message + ")", "CS2 IP Changer", 0x10);
            return 1;
        }
    }

    // Separate method: WPF's assemblies only load when this runs, after the check.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>The app's styles for a window without the App (the Install window and the data folder message).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Styles()
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
            { Source = new Uri($"pack://application:,,,/{typeof(Program).Assembly.GetName().Name};component/Theme.xaml") });
    }

    /// <summary>The exe's folder is fine but SRT_Dados next to it isn't (moved instead of copied): the reason in the app's own window.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunFolderProblem(string why)
    {
        Styles();
        NoticeWindow.Ask("CS2 IP Changer can't start here", why, "Close", problem: true);
        return 1;
    }

    /// <summary>Install, step 2, in the staged copy (admin-only folder): the Install window. Only this runs; the app itself doesn't start.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunInstall(string from)
    {
        Styles();
        return InstallWindow(from, Installer.Target).ShowAndWait() ? 0 : 1;
    }

    /// <summary>Install: the exe is in place but SRT_Dados isn't. The reason is an exception's message, with or without its full stop.</summary>
    public static string DataNotCopiedText(string dataError, string exe) =>
        $"{AppIds.ExeName} is installed, but SRT_Dados wasn't copied: {(dataError ?? "").TrimEnd().TrimEnd('.')}. Then start {exe}.";

    internal static NoticeWindow InstallWindow(string from, string target)
    {
        var keep = Directory.Exists(Path.Combine(target, AppIds.DataFolder)) && Directory.Exists(Path.Combine(from, AppIds.DataFolder));
        // beta.11 (audit P5): an older copy updated itself and left its protection on, which stays until the installed app starts
        // A Firewall that couldn't be read says nothing here: the note is only shown for a block that was seen
        var blockLeft = Firewall.BlockLeft(Firewall.Remaining(), unreadableCounts: false);
        return new NoticeWindow("Install CS2 IP Changer", InstallText(from, target, keep, Installer.InstalledVersion(target), AppInfo.Version, blockLeft), "Install", "Close", action: async w =>
        {
            w.Working = true;
            w.Say($"Copying to {target}…");
            Installer.Result r;
            try { r = await Task.Run(() => Installer.Install(from, target, AppInfo.Version)); }
            catch (Exception ex)
            {
                w.Working = false;
                w.Accepted = false;
                w.Say("Couldn't install: " + ex.Message, bad: true);
                return;
            }
            if (r.DataError != null)
            {
                // The exe is in place, but the app would start without your settings: it isn't started, the message says what to do
                w.Working = false;
                w.Finish();
                w.Say(DataNotCopiedText(r.DataError, r.Exe), bad: true);
                return;
            }
            var note = (r.ExeKept != null ? $"Version {r.ExeKept} was already there and stays. " : "")
                + (r.DataKept ? "The SRT_Dados already there was kept. " : "")
                + (r.Skipped > 0 ? $"Left out {Fmt.Count(r.Skipped, "file")} from SRT_Dados (links, files kept online by OneDrive, or in use). " : "");
            // beta.11 (audit P4): the old SRT_Dados keeps the Steam sign-ins. Only to the Recycle Bin, and only if you say so
            var old = Installer.OldCopy(from, r);
            if (old.Count > 0 && NoticeWindow.Ask("The old copy", Installer.OldCopyText(from), "Send to Recycle Bin", "Keep it"))
            {
                var left = old.Where(p => !Installer.Recycle(p)).ToList();
                note += left.Count == 0 ? "The old SRT_Dados is in the Recycle Bin. " : $"Couldn't move {string.Join(", ", left)} to the Recycle Bin: delete it yourself. ";
            }
            try { Installer.Start(r.Exe); }
            catch (Exception ex)
            {
                w.Working = false;
                w.Finish();
                w.Say($"{note}Installed, but couldn't start it ({ex.Message}). Start {r.Exe} yourself.", bad: true);
                return;
            }
            w.Say(note + "Starting the app from there…");
            await Task.Delay(note.Length > 0 ? 3000 : 800);
            w.Working = false;
            w.Close();
        });
    }

    /// <summary>What the Install window says: why the app doesn't run from <paramref name="from"/> and exactly what Install does.</summary>
    public static string InstallText(string from, string target, bool keepData, string installed = null, string version = null, bool blockLeft = false)
    {
        var keepExe = Installer.KeepExe(installed, version);
        if (blockLeft) return InstallText(from, target, keepData, installed, version) + "\n\n" + BlockLeftText(from);
        return $"CS2 IP Changer runs as admin, and programs without admin rights can change the folder it was started from:\n{from}\n\n"
            + "A file put next to the app there could run with admin rights, so the app doesn't run from it.\n\n"
            + (installed == null ? $"Install copies {AppIds.ExeName} to {target}, where only admins can change files, "
                : keepExe ? $"Version {installed} is already in {target}, the same or newer than this one, so Install keeps it, "
                : $"Install replaces version {installed} in {target} (only admins can change files there) with this one, ")
            + (keepData ? "keeps the SRT_Dados that is already there (the one in that folder isn't copied), " : "copies your SRT_Dados folder too if there is one there, ")
            + "and starts the app from there. Nothing in that folder is moved or deleted, so you can delete that copy afterwards.";
    }

    /// <summary>The old copy's protection is still in the Firewall: what that means, and how to lift it without installing.</summary>
    public static string BlockLeftText(string from) =>
        "Protection from the old copy is still on: CS2 and your servers stay blocked outside the phone. Install, and the app takes it over. "
        + "To turn it off without installing, run as admin:\n" + AppChanges.RestoreCommand(Path.Combine(from, AppIds.ExeName));

    /// <summary>The DLL from System32, or nothing (then .NET's own search runs, for a name System32 doesn't have).</summary>
    public static IntPtr SystemDll(string name)
    {
        if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.IndexOfAny(new[] { '\\', '/' }) >= 0) return IntPtr.Zero;
        var file = Path.Combine(Environment.SystemDirectory, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll");
        return NativeLibrary.TryLoad(file, out var h) ? h : IntPtr.Zero;
    }

    static string SafeBase => Path.Combine(AppIds.ProgramDataDir, "net");

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
    /// Why the app won't start after <see cref="LockDllSearch"/> (null = it can): as admin, a DLL search Windows wouldn't lock lets a
    /// DLL next to the exe or in the current folder run with admin rights. Without admin, or for --restore, it carries on.
    /// </summary>
    public static string DllSearchProblem(bool locked, bool elevated, IEnumerable<string> args) =>
        locked || !elevated || AppIds.Args.IsRestore(args) ? null
            : "Windows wouldn't limit where CS2 IP Changer loads its DLLs from, so a file next to the app could run with admin rights. "
              + $"The app didn't start.\n\nRestart the PC and try again. To undo the app's network changes, run {AppIds.ExeName} {AppIds.Args.Restore}.";

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
        var fix = $"Close this, create the folder {AppIds.InstallDir}, copy {AppIds.ExeName} into it (and your SRT_Dados folder, if you have one: copy it, don't move it) and start the app from there.";
        if (Adb.UsersCanWrite(appDir))
            return $"CS2 IP Changer runs as admin, and this folder can be changed by programs without admin rights:\n{appDir}\n\nA file put next to the app there would run with admin rights, so the app doesn't start from it.\n\n{fix}";
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

    // Reviewed 4 nov. (o192): every MessageBoxW here stays Windows' own box on purpose. Each runs where the app's window must not load:
    // DLL search not locked, a folder others can change (Downloads), files not unpacked to the admin-only folder, or runtime variables
    // that load code into .NET. WPF loads dozens of DLLs; user32 is already in. The app's own window starts at RunFolderProblem/RunInstall.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetDefaultDllDirectories(uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectoryW(string path);
}
