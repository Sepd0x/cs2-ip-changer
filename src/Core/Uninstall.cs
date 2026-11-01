using System.Runtime.InteropServices;

namespace SRT.Core;

/// <summary>Settings > Remove everything and quit: what the app put outside its own folder goes, SRT_Dados stays (it is yours).</summary>
public static class Uninstall
{
    /// <summary>%ProgramData%\CS2IPChanger: the ADB copy, the unpacked files of each version, the Install copy, the task's XML.</summary>
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger");

    /// <summary>
    /// Deletes the folder without ever going through a link (a link inside is removed, not what it points to). What is in use (this
    /// copy's own unpacked files) is handed to <paramref name="atReboot"/> (Windows deletes it at the next restart). As admin, a folder
    /// other programs can change is left alone: it isn't the app's to delete. Returns how many paths wait for the restart, or -1.
    /// </summary>
    public static int RemoveData(string root = null, Func<string, bool> atReboot = null)
    {
        root ??= Root;
        atReboot ??= DeleteAtReboot;
        if (!Directory.Exists(root)) return 0;
        if (Paths.IsLink(root) || (Adb.Elevated && !Adb.AdminOnly(root))) return -1;
        foreach (var d in Directory.GetDirectories(root)) SRT.Program.DeleteTree(d);
        foreach (var f in Directory.GetFiles(root)) { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { } }
        try { Directory.Delete(root); return 0; } catch { }
        return Later(root, atReboot);
    }

    static int Later(string dir, Func<string, bool> atReboot)
    {
        if (Paths.IsLink(dir)) return atReboot(dir) ? 1 : 0;
        var n = 0;
        foreach (var f in Directory.GetFiles(dir)) if (atReboot(f)) n++;
        foreach (var d in Directory.GetDirectories(dir)) n += Later(d, atReboot);
        if (atReboot(dir)) n++;   // after what is inside it: Windows deletes them in this order
        return n;
    }

    static bool DeleteAtReboot(string path) => MoveFileExW(path, null, DelayUntilReboot);

    /// <summary>What the question says before anything is removed.</summary>
    public static string Text(string dataDir, bool installed) =>
        "Removes every Firewall rule and route the app made (protection and Regions), the Start with Windows task, and the app's folder " +
        $"{Root} (its copy of ADB and its unpacked files), and stops the phone tool. Then the app closes.\n\n" +
        $"What stays: {dataDir} (your settings, servers, accounts list, saved Steam sign-ins, config backups and logs) and the app itself. " +
        (installed
            ? $"To remove them too, delete the folder {Installer.Target}."
            : "To remove them too, delete the SRT_Dados folder and CS2IPChanger.exe.") +
        " Steam and CS2 keep their own files.";

    const uint DelayUntilReboot = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileExW(string from, string to, uint flags);
}
