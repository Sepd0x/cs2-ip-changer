using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SRT.Core;

/// <summary>
/// Writes and deletes that work on the file's own handle and check where it really is, for folders that programs without admin
/// rights can change (Steam's userdata, Desktop, Downloads). Checking a path and then opening it again by name leaves a gap: in it,
/// an emptied folder can become a link to somewhere else, and the app, as admin, would write or delete there.
/// <list type="bullet">
/// <item>The temporary file is made with CREATE_NEW without following links, and its real place (GetFinalPathNameByHandle) must be
/// the expected one. From then on the folder has a file the app holds open without delete sharing, so it can't be emptied and turned
/// into a link.</item>
/// <item>It is renamed into place through its handle, and its real place is checked once more.</item>
/// <item>A delete opens the file itself (a link is opened as the link, never what it points to), checks its real place, and deletes
/// through that handle.</item>
/// </list>
/// </summary>
public static class SafeFile
{
    /// <summary>Writes <paramref name="data"/> to <paramref name="path"/>; the folder is resolved once, here.</summary>
    public static void Write(string path, byte[] data)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        using var hd = OpenDir(dir);
        Write(FinalPath(hd) ?? throw new IOException("can't read the folder's real path"), Path.GetFileName(path), data);
    }

    /// <summary>
    /// Writes to <paramref name="rel"/> under <paramref name="realDir"/>, a folder's real path (from GetFinalPathNameByHandle, held
    /// open by the caller). Replaces a file that is there; never writes anywhere else.
    /// </summary>
    public static void Write(string realDir, string rel, byte[] data)
    {
        var path = Path.Combine(realDir, rel);
        var name = Path.GetFileName(path);
        var tmp = path + ".srt-tmp";
        Delete(realDir, rel + ".srt-tmp");   // a leftover (or something put there) is deleted as itself, never opened through a link
        using var h = CreateFileW(tmp, GenericWrite | DeleteAccess | ReadAttributes, 0, IntPtr.Zero, CreateNew, OpenReparsePoint, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException("couldn't create " + Path.GetFileName(tmp), Marshal.GetHRForLastWin32Error());
        var done = false;
        try
        {
            if (!Same(FinalPath(h), tmp)) throw new IOException(Moved);
            RandomAccess.Write(h, data, 0);
            Rename(h, path);
            if (!Same(FinalPath(h), path)) throw new IOException(Moved);
            done = true;
        }
        finally { if (!done) Dispose(h); }
    }

    /// <summary>Deletes <paramref name="rel"/> under the real folder <paramref name="realDir"/> if it is there, as itself. False if not.</summary>
    public static bool Delete(string realDir, string rel)
    {
        var path = Path.Combine(realDir, rel);
        using var h = CreateFileW(path, DeleteAccess | ReadAttributes, 7, IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
        if (h.IsInvalid) return false;
        if (!Same(FinalPath(h), path)) return false;   // reached through a link: not this folder's file
        return Dispose(h);
    }

    /// <summary>A folder held open (nobody can rename or delete it meanwhile) and its real path, for <see cref="Delete(string, string)"/>.</summary>
    public sealed class Held : IDisposable
    {
        readonly SafeFileHandle _h;
        public string Real { get; }
        internal Held(SafeFileHandle h, string real) { _h = h; Real = real; }
        public void Dispose() => _h.Dispose();
    }

    /// <summary>Holds a folder that must not be a link itself (a link is refused, not followed).</summary>
    public static Held Hold(string dir)
    {
        var h = CreateFileW(dir, ListDirectory | ReadAttributes, 3, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); throw new IOException("can't open the folder " + Path.GetFileName(dir)); }
        if (!GetFileInformationByHandle(h, out var info) || (info.Attributes & ReparsePointAttr) != 0 || FinalPath(h) is not string real || !Same(real, dir))
        {
            h.Dispose();
            throw new IOException(Path.GetFileName(dir) + " is a link, or reached through one");
        }
        return new Held(h, real);
    }

    /// <summary>Deletes <paramref name="path"/> as itself; the folder is resolved once, here.</summary>
    public static bool Delete(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Directory.Exists(dir)) return false;
        using var hd = OpenDir(dir);
        return FinalPath(hd) is string real && Delete(real, Path.GetFileName(path));
    }

    /// <summary>
    /// Opens a file in a folder others can change, to read it as admin: the folder is resolved once (it may be a link, like a Steam
    /// folder moved to another drive), then the file must really be in that folder, not be a link and have only one name. Otherwise a
    /// file only admins can read could be put behind that name and end up somewhere you can read it (a bug report on the Desktop).
    /// </summary>
    public static FileStream OpenRead(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        using var hd = OpenDir(dir);
        var p = Path.Combine(FinalPath(hd) ?? throw new IOException("can't read the folder's real path"), Path.GetFileName(path));
        var h = CreateFileW(p, GenericRead, 7, IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); throw new FileNotFoundException("not found: " + Path.GetFileName(path)); }
        if (!Same(FinalPath(h), p) || !GetFileInformationByHandle(h, out var info) || (info.Attributes & ReparsePointAttr) != 0 || (info.Attributes & DirectoryAttr) != 0 || info.Links != 1)
        {
            h.Dispose();
            throw new IOException(Path.GetFileName(path) + " was not read: it is a link, or reached through one");
        }
        return new FileStream(h, FileAccess.Read);
    }

    const string Moved = "the folder was changed into a link while the app was writing; nothing was written there";

    /// <summary>Where an open file or folder really is, every link on the way resolved, or null.</summary>
    public static string FinalPath(SafeFileHandle h)
    {
        var sb = new StringBuilder(1024);
        var n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
        if (n == 0 || n >= sb.Capacity) return null;
        var p = sb.ToString();
        return p.StartsWith(@"\\?\UNC\") ? @"\\" + p[8..] : p.StartsWith(@"\\?\") ? p[4..] : p;
    }

    static bool Same(string a, string b) => a != null && b != null &&
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder held open without delete sharing: while the handle lives, nobody can rename or delete it.</summary>
    static SafeFileHandle OpenDir(string path)
    {
        var h = CreateFileW(path, ListDirectory, 3, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        throw new IOException("can't open the folder " + Path.GetFileName(path));
    }

    /// <summary>
    /// Renames the open file to <paramref name="target"/> (a full path in its own folder), replacing a file of that name. The path is
    /// looked up again, but that folder holds this open file, which nobody can move or delete, so it can't be emptied and turned into
    /// a link. (A bare name is not used: Windows reads it from the current folder, not the file's.)
    /// </summary>
    static void Rename(SafeFileHandle h, string target)
    {
        var name = Path.GetFileName(target);
        var chars = Encoding.Unicode.GetBytes(target);
        // FILE_RENAME_INFO: ReplaceIfExists (4 bytes, padded to a pointer), RootDirectory (null), FileNameLength, FileName
        var off = 2 * IntPtr.Size;
        var buf = new byte[off + 4 + chars.Length + 2];
        buf[0] = 1;
        BitConverter.GetBytes(chars.Length).CopyTo(buf, off);
        chars.CopyTo(buf, off + 4);
        if (!SetFileInformationByHandle(h, FileRenameInfo, buf, (uint)buf.Length))
            throw new IOException("couldn't put " + name + " in place", Marshal.GetHRForLastWin32Error());
    }

    /// <summary>Marks the open file for deletion (it goes when the handle closes).</summary>
    static bool Dispose(SafeFileHandle h)
    {
        var yes = new byte[] { 1 };
        return SetFileInformationByHandle(h, FileDispositionInfo, yes, 1);
    }

    // BY_HANDLE_FILE_INFORMATION (FILETIMEs are two DWORDs: 4-byte packing)
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct FileInfo
    {
        public uint Attributes;
        public long Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    const uint ReparsePointAttr = 0x400, DirectoryAttr = 0x10;
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, DeleteAccess = 0x00010000, ReadAttributes = 0x80, ListDirectory = 0x1;
    const uint CreateNew = 1, OpenExisting = 3;
    const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    const int FileRenameInfo = 3, FileDispositionInfo = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle h, out FileInfo info);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, byte[] info, uint size);
}
