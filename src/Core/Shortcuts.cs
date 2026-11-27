using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace SRT.Core;

/// <summary>
/// 27 nov. (the owner: "after closing the app it's hard to find again without the install .exe"). The installed copy is in the
/// Start menu for everyone on the PC (and so in Windows search), made again at each start of that copy when it is missing or points
/// elsewhere (installs from before this get it with the update). Install also puts one on your desktop, once: delete it and it stays
/// deleted. Remove takes both away. Only links named Pickroute (or the old name) that point at the installed exe are touched.
/// </summary>
public static class Shortcuts
{
    public const string Name = "Pickroute";
    /// <summary>The old name: a link with it that points at the app is taken away too (none was ever made, but one made by hand is ours).</summary>
    public const string OldName = "CS2 IP Changer";
    // The Start menu tooltip: no old name in sight (27 nov., the owner)
    public const string Description = "Pickroute: CS2 through your phone's mobile data";

    public static string StartMenu(string programs = null) =>
        Path.Combine(programs ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Name + ".lnk");

    public static string Desktop(string desktop = null) =>
        Path.Combine(desktop ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk");

    /// <summary>Only the copy in the install folder gets links (the same rule as its Installed apps entry).</summary>
    public static bool ForThisCopy(string exe, string target = null) =>
        exe != null && Installer.SameDir(Path.GetDirectoryName(exe)!, target ?? Installer.Target);

    /// <summary>
    /// At each start of the installed copy: the Start menu link is there and opens this exe. Null = fine (or nothing to do);
    /// otherwise why it couldn't be made.
    /// </summary>
    public static string KeepStartMenu(string exe, string programs = null, string target = null)
    {
        if (!ForThisCopy(exe, target)) return null;
        try
        {
            var link = StartMenu(programs);
            if (File.Exists(link) && SameFile(Target(link), exe)) return null;
            if (!Ours(link, exe)) return null;
            Make(link, exe);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Install: the desktop link, once. Null = made or already there; otherwise why not.</summary>
    public static string AddDesktop(string exe, string desktop = null)
    {
        try
        {
            var link = Desktop(desktop);
            if (File.Exists(link) && SameFile(Target(link), exe)) return null;
            if (!Ours(link, exe)) return null;
            Make(link, exe);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Remove: the links named Pickroute or CS2 IP Changer in the Start menu and on the desktops that open <paramref name="exe"/>.
    /// Returns how many went. A link someone else made with that name for another program stays.</summary>
    public static int RemoveAll(string exe, IEnumerable<string> folders = null)
    {
        folders ??= new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        var n = 0;
        foreach (var dir in folders.Where(d => !string.IsNullOrEmpty(d)))
            foreach (var name in new[] { Name, OldName })
            {
                var link = Path.Combine(dir, name + ".lnk");
                try
                {
                    if (!File.Exists(link) || !SameFile(Target(link), exe)) continue;
                    File.Delete(link);
                    n++;
                }
                catch { }
            }
        return n;
    }

    /// <summary>
    /// The link may be written: there is none yet, or it opens a program in the app's own folder (a link of ours to an older exe).
    /// Second Codex pass, B3 (27 nov.): a Pickroute.lnk that opens another program, or one that can't be read, is someone else's and
    /// stays as it is. It is never replaced, so Remove (which only takes links that open the app) never takes it either.
    /// </summary>
    public static bool Ours(string link, string exe)
    {
        if (!File.Exists(link)) return true;
        var target = Target(link);
        if (string.IsNullOrEmpty(target)) return false;
        try { return Installer.SameDir(Path.GetDirectoryName(Path.GetFullPath(target))!, Path.GetDirectoryName(Path.GetFullPath(exe))!); }
        catch { return false; }
    }

    public static bool SameFile(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes a .lnk that opens <paramref name="exe"/> in its folder, with its icon and the description (through a .tmp, then moved).</summary>
    public static void Make(string link, string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var sl = (IShellLinkW)new ShellLink();
        try
        {
            sl.SetPath(exe);
            sl.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            sl.SetIconLocation(exe, 0);
            sl.SetDescription(Description);
            var tmp = link + ".tmp";
            ((IPersistFile)sl).Save(tmp, true);
            File.Move(tmp, link, true);
        }
        finally { Marshal.FinalReleaseComObject(sl); }
    }

    /// <summary>The program a .lnk opens (null when it can't be read).</summary>
    public static string Target(string link)
    {
        var sl = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)sl).Load(link, 0);
            var sb = new StringBuilder(1024);
            sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4);   // SLGP_RAWPATH
            return sb.Length > 0 ? Environment.ExpandEnvironmentVariables(sb.ToString()) : null;
        }
        catch { return null; }
        finally { Marshal.FinalReleaseComObject(sl); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
