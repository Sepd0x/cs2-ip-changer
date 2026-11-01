using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SRT.Core;

/// <summary>
/// Install (o192): run as admin from a folder other programs can change (Downloads, the Desktop), the app offers to copy itself to
/// C:\Program Files\CS2 IP Changer and start from there. In two steps, so nothing of the app's window ever loads next to that exe:
/// 1. The copy in Downloads only checks it is the file that is running, holds it, copies it to an admin-only folder in ProgramData
///    (<see cref="StageDir"/>) and starts that copy with <see cref="FromArg"/>. No window, no log, nothing written next to it.
/// 2. That copy shows the Install window. Install copies it to Program Files, copies SRT_Dados from the old folder without ever going
///    through a link, and starts the installed app. Nothing at the old place is moved or deleted; no Start menu shortcut.
/// </summary>
public static class Installer
{
    public static string Target => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CS2 IP Changer");

    /// <summary>%ProgramData%\CS2IPChanger\install: admin-only, holds the copy that shows the Install window.</summary>
    public static string StageDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger", "install");

    /// <summary>The argument the staged copy gets: the folder the app was started from (where SRT_Dados is copied from).</summary>
    public const string FromArg = "--install-from";

    /// <summary>The Install window replaces the error: the app's own folder is the problem, and it isn't the install folder already.</summary>
    public static bool Offer(string appDir, string target = null) =>
        !SameDir(appDir, target ?? Target) && Adb.UsersCanWrite(Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)));

    public static bool SameDir(string a, string b) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder to copy SRT_Dados from, only in the staged copy (it runs from <paramref name="stageDir"/>) and only a full path
    /// to a folder that exists. Null otherwise.</summary>
    public static string StagedSource(IReadOnlyList<string> args, string appDir, string stageDir = null)
    {
        if (!SameDir(appDir, stageDir ?? StageDir)) return null;
        var i = args.ToList().IndexOf(FromArg);
        var from = i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        return from != null && Path.IsPathFullyQualified(from) && Directory.Exists(from) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(from)) : null;
    }

    /// <summary>
    /// Step 1: copies the held, checked exe to <see cref="StageDir"/> (made admin-only, and refused if it isn't) and returns the copy.
    /// </summary>
    public static string Stage(FileStream self, string stageDir = null)
    {
        var dir = stageDir ?? StageDir;
        Adb.SafeDir(Path.GetDirectoryName(dir)!);
        Adb.SafeDir(dir);
        if (!Adb.AdminOnly(dir)) throw new IOException(dir + " can be changed by programs without admin rights");
        var exe = Path.Combine(dir, Updater.ExeName);
        CopyExe(self, exe);
        return exe;
    }

    /// <summary>After an install: the staged copy is no longer needed (best effort; it may still be closing, then it goes next time).</summary>
    public static void CleanStage()
    {
        try
        {
            if (Directory.Exists(StageDir) && !SameDir(AppContext.BaseDirectory, StageDir) && Adb.AdminOnly(StageDir) && SRT.Program.DeleteTree(StageDir))
                Log.Info("Removed the copy used by Install from " + StageDir + ".");
        }
        catch { }
    }

    /// <summary>Why the install folder can't be used (null = it can): it, or the folder it would be made in, can be changed by programs
    /// without admin rights, or it is a link.</summary>
    public static string TargetProblem(string target)
    {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (Directory.Exists(dir))
            return Paths.IsLink(dir) || Adb.UsersCanWrite(dir) ? $"{dir} can be changed by programs without admin rights, so nothing was copied there." : null;
        var parent = Path.GetDirectoryName(dir);
        return parent == null || !Directory.Exists(parent) || Paths.IsLink(parent) || Adb.UsersCanWrite(parent)
            ? $"{parent} can be changed by programs without admin rights, so nothing was copied there." : null;
    }

    /// <summary>The version of the app already installed there (null = none), from its file's product version.</summary>
    public static string InstalledVersion(string target)
    {
        try
        {
            var exe = Path.Combine(target, Updater.ExeName);
            return File.Exists(exe) ? Updater.Clean(FileVersionInfo.GetVersionInfo(exe).ProductVersion) : null;
        }
        catch { return null; }
    }

    /// <summary>Exe: the installed app. ExeKept: the version there was the same or newer, so it stayed. DataKept: an SRT_Dados was
    /// there already, so the old folder's wasn't copied. DataError: why SRT_Dados wasn't copied (the exe is installed anyway).</summary>
    public sealed record Result(string Exe, string ExeKept, int Files, int Skipped, bool DataKept, string DataError = null);

    /// <summary>The staged copy is running (its Install window is open), so a second one can't be put there.</summary>
    public static bool StageBusy(string stageDir = null)
    {
        var exe = Path.Combine(stageDir ?? StageDir, Updater.ExeName);
        if (!File.Exists(exe)) return false;
        try { using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None)) return false; }
        catch (IOException) { return true; }
        catch { return false; }
    }

    /// <summary>
    /// Step 2: copies this running exe (unless the same or a newer version is installed) and, when the install folder has none yet,
    /// SRT_Dados from <paramref name="fromDir"/>. SRT_Dados is copied to a ".partial" folder first, so a copy cut short is never taken
    /// for a finished one.
    /// </summary>
    public static Result Install(string fromDir, string target, string version)
    {
        if (TargetProblem(target) is string before) throw new IOException(before);
        Directory.CreateDirectory(target);
        // Made now, it takes Program Files' permissions; checked again in case something was there first
        if (TargetProblem(target) is string after) throw new IOException(after);
        var exe = Path.Combine(target, Updater.ExeName);
        var installed = InstalledVersion(target);
        var keepExe = KeepExe(installed, version);
        if (!keepExe)
            using (var self = OpenSelf()) CopyExe(self, exe);
        try
        {
            var (files, skipped, kept) = InstallData(fromDir, target);
            return new Result(exe, keepExe ? installed : null, files, skipped, kept);
        }
        catch (Exception ex) { return new Result(exe, keepExe ? installed : null, 0, 0, false, ex.Message); }
    }

    /// <summary>The version already installed is the same or newer: it stays (Install never takes it back to an older one).</summary>
    public static bool KeepExe(string installed, string version) =>
        installed != null && version != null && Updater.IsValid(installed) && Updater.Compare(installed, version) >= 0;

    /// <summary>
    /// SRT_Dados from <paramref name="fromDir"/> into <paramref name="target"/>, only when the target has none (Kept = it had one and the
    /// other wasn't copied). Copied to SRT_Dados.partial first and renamed at the end, so a copy cut short is never taken for a finished one.
    /// Throws, with what to do, when it can't be copied at all.
    /// </summary>
    public static (int Files, int Skipped, bool Kept) InstallData(string fromDir, string target)
    {
        var data = Path.Combine(fromDir, "SRT_Dados");
        var toData = Path.Combine(target, "SRT_Dados");
        if (Directory.Exists(toData))
        {
            // Moved there instead of copied, it keeps permissions that let other programs change it, and the app won't start with it
            if (Paths.IsLink(toData) || Adb.UsersCanWrite(toData))
                throw new IOException($"{toData} can be changed by programs without admin rights (it was moved there instead of copied). Copy it to another place, delete it there and copy it back.");
            return (0, 0, Directory.Exists(data));
        }
        if (!Directory.Exists(data)) return (0, 0, false);
        var partial = toData + ".partial";
        if (Directory.Exists(partial) && !SRT.Program.DeleteTree(partial)) throw new IOException(partial + " is left from an earlier try and can't be removed");
        var (files, skipped) = CopyData(data, partial);
        if (files < 0)
            throw new IOException($"{data} goes through a link, or where it really is couldn't be confirmed (a folder synced by OneDrive does this), so it wasn't copied. Copy it to {toData} yourself.");
        Directory.Move(partial, toData);
        return (files, skipped, false);
    }

    /// <summary>Starts the installed copy. It inherits this process's admin rights and its environment, already without the runtime variables.</summary>
    public static void Start(string exe) =>
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();

    /// <summary>
    /// The exe this process runs from, opened so nobody can change, rename or delete it while it's held, and checked to be the very
    /// file Windows started: the image's current name must be the held file's. A file put at the same path after the start is refused.
    /// </summary>
    public static FileStream OpenSelf()
    {
        var path = Environment.ProcessPath ?? throw new IOException("the app's own path is unknown");
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var held = FinalPath(fs.SafeFileHandle, NtVolume);
            var image = MappedName(GetModuleHandleW(null));
            if (held == null || image == null || !held.Equals(image, StringComparison.OrdinalIgnoreCase))
                throw new IOException(path + " is not the file that is running, so it wasn't copied");
            return fs;
        }
        catch { fs.Dispose(); throw; }
    }

    /// <summary>Copies the held exe to <paramref name="dest"/> through a .tmp, then reads the copy back: same SHA-256 or nothing.</summary>
    public static void CopyExe(FileStream self, string dest)
    {
        var tmp = dest + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        byte[] want;
        self.Position = 0;
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buf = new byte[81920];
            int n;
            while ((n = self.Read(buf, 0, buf.Length)) > 0) { sha.AppendData(buf, 0, n); output.Write(buf, 0, n); }
            output.Flush(true);
            want = sha.GetHashAndReset();
        }
        using (var check = File.OpenRead(tmp))
            if (!SHA256.HashData(check).AsSpan().SequenceEqual(want)) { File.Delete(tmp); throw new IOException("the copy doesn't match the app"); }
        try { File.Move(tmp, dest, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { File.Delete(tmp); throw new IOException($"{dest} is in use. If CS2 IP Changer is open from there, close it and try again."); }
    }

    /// <summary>Files never copied: owned.json (the routes the app added, read only from an admin-only folder: one planted in the old
    /// folder must not become trusted), and leftovers of saves and starts.</summary>
    public static bool Skip(string name) =>
        name.Equals("owned.json", StringComparison.OrdinalIgnoreCase) || name.Equals("running.flag", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.Contains(".broken-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies a data folder file by file. The folder itself is opened without following a link and must really be where it was named;
    /// inside, a junction, a symbolic link, a file with more than one name (hard link) or a file whose real place isn't where it was
    /// listed (a folder swapped for a link meanwhile) is skipped: as admin that would copy someone else's file into Program Files.
    /// Returns the files copied and skipped; Files is -1 when the folder itself was refused (nothing copied, <paramref name="to"/> not made).
    /// </summary>
    public static (int Files, int Skipped) CopyData(string from, string to)
    {
        int files = 0, skipped = 0;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from));
        using var hold = OpenDir(root);   // held without delete sharing: it can't be renamed or swapped while the copy runs
        if (hold == null || !GetFileInformationByHandle(hold, out var info) || (info.Attributes & 0x400) != 0
            || !string.Equals(FinalPath(hold, 0), root, StringComparison.OrdinalIgnoreCase))
            return (-1, 0);
        void Copy(string rel, int depth)
        {
            var src = rel.Length == 0 ? root : Path.Combine(root, rel);
            if (depth > 16 || (rel.Length > 0 && Paths.IsLink(src))) { skipped++; return; }
            Directory.CreateDirectory(rel.Length == 0 ? to : Path.Combine(to, rel));
            foreach (var f in Directory.GetFiles(src))
            {
                var r = Path.Combine(rel, Path.GetFileName(f));
                if (Skip(Path.GetFileName(f))) continue;
                if (CopyFile(f, Path.Combine(root, r), Path.Combine(to, r))) files++; else skipped++;
            }
            foreach (var d in Directory.GetDirectories(src)) Copy(Path.Combine(rel, Path.GetFileName(d)), depth + 1);
        }
        Copy("", 0);
        return (files, skipped);
    }

    static bool CopyFile(string src, string expected, string dest)
    {
        const uint Read = 0x80000000, OpenReparsePoint = 0x00200000;
        try
        {
            using var h = CreateFileW(src, Read, FileShare.Read, IntPtr.Zero, FileMode.Open, OpenReparsePoint, IntPtr.Zero);
            if (h.IsInvalid) return false;
            if (!GetFileInformationByHandle(h, out var info) || (info.Attributes & 0x410) != 0 || info.Links != 1) return false;   // reparse point or folder
            if (!string.Equals(FinalPath(h, 0), expected, StringComparison.OrdinalIgnoreCase)) return false;
            using var input = new FileStream(h, FileAccess.Read);
            using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            return true;
        }
        catch { return false; }
    }

    /// <summary>A folder opened without following a link (a junction is opened as itself) and without delete sharing; null if it can't be.</summary>
    static SafeFileHandle OpenDir(string path)
    {
        const uint ListDirectory = 0x1, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
        var h = CreateFileW(path, ListDirectory, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    const uint NtVolume = 0x2;   // VOLUME_NAME_NT: \Device\HarddiskVolumeN\..., the form GetMappedFileName gives

    static string FinalPath(SafeFileHandle h, uint flags)
    {
        var sb = new StringBuilder(1024);
        var n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, flags);
        if (n == 0 || n >= sb.Capacity) return null;
        var p = sb.ToString();
        return flags == 0 && p.StartsWith(@"\\?\") && !p.StartsWith(@"\\?\UNC\") ? p[4..] : p;
    }

    static string MappedName(IntPtr address)
    {
        var sb = new StringBuilder(1024);
        var n = K32GetMappedFileNameW((IntPtr)(-1), address, sb, (uint)sb.Capacity);   // this process (pseudo-handle, nothing to close)
        return n == 0 || n >= sb.Capacity ? null : sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation
    {
        // FILETIMEs as two DWORDs each, so no field is moved by alignment
        public uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint K32GetMappedFileNameW(IntPtr process, IntPtr address, StringBuilder name, uint size);
}
