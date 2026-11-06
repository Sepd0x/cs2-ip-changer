using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SRT.Core;

/// <summary>
/// Settings > Remove everything and quit, and Windows Settings > Installed apps > Uninstall (<see cref="Arg"/>): what the app put in
/// Windows goes, then the app's files. Keep = SRT_Dados stays (install again and it is all back); everything = SRT_Dados too.
/// What this copy still uses (its exe, its unpacked files) goes a few seconds after it closes: a small PowerShell step waits for the
/// app to end and deletes exactly those paths, never a folder with what's in it. A restart is only the last resort.
/// </summary>
public static class Uninstall
{
    /// <summary>%ProgramData%\CS2IPChanger: the ADB copy, the unpacked files of each version, the Install copy, the task's XML.</summary>
    public static string Root => AppIds.ProgramDataDir;

    /// <summary>What couldn't go yet because this copy still uses it: files, then folders (the deepest first).</summary>
    public sealed class Leftovers
    {
        public List<string> Files { get; } = new();
        public List<string> Dirs { get; } = new();
        public int Count => Files.Count + Dirs.Count;
    }

    /// <summary>
    /// Deletes a folder of the app and everything in it without ever going through a link: a junction or link inside is removed, not
    /// what it points to. What is in use is added to <paramref name="left"/> and the rest still goes. As admin, a folder other programs
    /// can change is left alone (false): it isn't the app's to delete. A folder that isn't there is fine (true).
    /// </summary>
    /// <param name="programData">%ProgramData%\CS2IPChanger, which the app makes admin-only itself (<see cref="Adb.AdminOnly"/>). The app's
    /// folder and SRT_Dados take Program Files' permissions instead, checked as at the start (<see cref="Adb.UsersCanWrite"/>).</param>
    public static bool RemoveTree(string root, Leftovers left, bool programData = true)
    {
        if (!Directory.Exists(root)) return true;
        if (Paths.IsLink(root) || (Adb.Elevated && (programData ? !Adb.AdminOnly(root) : Adb.UsersCanWrite(root)))) return false;
        Empty(root, left, 0);
        return true;
    }

    static void Empty(string dir, Leftovers left, int depth)
    {
        if (depth > 0 && Paths.IsLink(dir)) { Gone(() => Directory.Delete(dir), dir, left.Dirs); return; }   // only the link itself
        if (depth < 32)
        {
            string[] files = [], dirs = [];
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); } catch { }
            foreach (var f in files)
                Gone(() => { if (!Paths.IsLink(f)) File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }, f, left.Files);
            foreach (var d in dirs) Empty(d, left, depth + 1);
        }
        Gone(() => Directory.Delete(dir), dir, left.Dirs);   // after what is inside it
    }

    static void Gone(Action delete, string path, List<string> left)
    {
        try { delete(); } catch { left.Add(path); }
    }

    /// <summary>ProgramDataRefused / DataRefused: left alone, programs without admin rights can change it.</summary>
    public sealed record Files(bool ProgramDataRefused, bool DataRefused, bool AppRefused, Leftovers Left);

    /// <summary>
    /// The app's files: %ProgramData%\CS2IPChanger, SRT_Dados when <paramref name="everything"/>, the app's exe, and the install folder
    /// when it is empty then (with Keep, SRT_Dados is still in it). What is in use goes to Left.
    /// </summary>
    public static Files RemoveFiles(bool everything, string exe, string dataDir, string root = null, string installDir = null)
    {
        var left = new Leftovers();
        var pdOk = RemoveTree(root ?? Root, left);
        var dataOk = !everything || RemoveTree(dataDir, left, programData: false);
        var appOk = true;
        if (exe != null && File.Exists(exe))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(exe))!;
            // The app only runs from a folder others can't change (Program.Main); checked again, as for the other folders
            if (Paths.IsLink(exe) || Paths.IsLink(dir) || (Adb.Elevated && Adb.UsersCanWrite(dir))) appOk = false;
            else
            {
                Gone(() => File.Delete(exe), exe, left.Files);
                // The install folder goes once it is empty: with Keep, SRT_Dados stays in it, so it stays too
                if (Installer.SameDir(dir, installDir ?? Installer.Target) && !(Directory.Exists(dataDir) && Installer.SameDir(Path.GetDirectoryName(Path.GetFullPath(dataDir))!, dir)))
                    Gone(() => Directory.Delete(dir), dir, left.Dirs);
            }
        }
        return new Files(!pdOk, !dataOk, !appOk, left);
    }

    /// <summary>What Remove did. Network, Task, Registry: what couldn't be undone (null = done). Helper: the step after the app closes started.</summary>
    public sealed record Outcome(string Network, string Task, string Registry, Files Files, bool Helper, bool Everything);

    /// <summary>
    /// Everything Remove does, in order: the rules and routes (<paramref name="restoreNetwork"/>, the caller's Guard), the Start with
    /// Windows task, the phone tool, the Installed apps entry, the files, and the step that deletes what is in use once the app closes.
    /// </summary>
    public static async Task<Outcome> Run(bool everything, Func<Task<string>> restoreNetwork)
    {
        string network;
        try { network = await restoreNetwork(); } catch (Exception ex) { network = ex.Message; }
        var task = await Autostart.TurnOff();
        var exe = Environment.ProcessPath;
        var installed = Autostart.Allowed(exe);
        return await System.Threading.Tasks.Task.Run(() =>
        {
            Adb.StopServer();
            // The network wasn't fully restored: the app, its Installed apps entry and SRT_Dados stay, so it can be opened (which takes
            // its rules off at the start) or --restore run, and --restore still finds the servers and owned.json
            if (KeepsApp(network)) return new Outcome(network, task, null, new Files(false, false, false, new Leftovers()), true, everything);
            var registry = installed ? Installer.Unregister() : null;   // a copy elsewhere leaves the installed copy's entry alone
            if (everything) Paths.ReadOnly = true;   // nothing writes to SRT_Dados again on the way out (log, settings, crash marker)
            var files = RemoveFiles(everything, exe, Paths.Data);
            return new Outcome(network, task, registry, files, AfterExit(files.Left), everything);
        });
    }

    /// <summary>With the network not fully restored (<paramref name="network"/> says what stayed), Remove leaves the app and its files.</summary>
    public static bool KeepsApp(string network) => network != null;

    /// <summary>The line in the log (it only reaches the file with Keep: with everything, SRT_Dados is gone).</summary>
    public static string LogLine(Outcome o) =>
        "Remove (" + (o.Everything ? "everything" : "kept SRT_Dados") + "): " + (o.Network == null ? "rules and routes gone" : "couldn't remove " + o.Network)
        + "; task " + (o.Task ?? "gone") + (o.Registry == null ? "" : "; Installed apps entry stayed: " + o.Registry)
        + (o.Files.ProgramDataRefused ? $"; {Root} left alone (others can change it)" : "")
        + (KeepsApp(o.Network) ? "; the app and its files stayed to try again"
            : o.Files.Left.Count == 0 ? "; files gone" : $"; in use: {Fmt.Count(o.Files.Left.Count, "path")}, deleted " + (o.Helper ? "after the app closes" : "at the next restart")) + ".";

    // ------------------------------------------------------------ After the app closes

    /// <summary>
    /// Starts the step that deletes <paramref name="left"/> once this process has ended. False when it couldn't start (or a path can't be
    /// given to it): then Windows deletes them at the next restart instead.
    /// </summary>
    public static bool AfterExit(Leftovers left)
    {
        if (left.Count == 0) return true;
        var script = Script(Environment.ProcessId, left);
        if (script != null)
        {
            try
            {
                var sys = Environment.SystemDirectory;
                var psi = new ProcessStartInfo(Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = sys,   // not one of the folders it deletes (a process holds its working folder)
                };
                foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p != null) return true;
            }
            catch { }
        }
        AtRestart(left);
        return false;
    }

    /// <summary>
    /// The PowerShell step: waits for the app (its process id) to end, deletes each file (tried for about 20 s: an antivirus can hold a
    /// file for a moment), then each folder only if it is empty. Never Remove-Item, never a folder with what's in it. What is still
    /// there goes at the next restart, written the way MoveFileEx writes it. Null when a path isn't a plain full path.
    /// </summary>
    public static string Script(int pid, Leftovers left)
    {
        // PowerShell also ends a '...' string at the curly quotes, so a path with one isn't given to it (it goes at the restart)
        if (left.Files.Concat(left.Dirs).Any(p => !Path.IsPathFullyQualified(p) || p.Any(char.IsControl) || p.StartsWith(@"\\")
            || p.IndexOfAny(['‘', '’', '‚', '‛']) >= 0)) return null;
        static string List(IEnumerable<string> paths) => "@(" + string.Join(",", paths.Select(p => "'" + p.Replace("'", "''") + "'")) + ")";
        return "$ErrorActionPreference='SilentlyContinue'; " +
            $"Wait-Process -Id {pid} -Timeout 600; " +
            $"$f={List(left.Files)}; $d={List(left.Dirs)}; " +
            "for($i=0; $i -lt 40; $i++){ foreach($p in $f){ if([IO.File]::Exists($p)){ [IO.File]::Delete($p) } }; " +
            "if(-not ($f | Where-Object { [IO.File]::Exists($_) })){ break }; Start-Sleep -Milliseconds 500 }; " +
            "foreach($p in $d){ [IO.Directory]::Delete($p, $false) }; " +
            "$left=@($f | Where-Object { [IO.File]::Exists($_) }) + @($d | Where-Object { [IO.Directory]::Exists($_) }); " +
            "if($left.Count -gt 0){ $k='HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager'; " +
            "$v=@((Get-ItemProperty -LiteralPath $k -Name PendingFileRenameOperations).PendingFileRenameOperations | Where-Object { $_ -ne $null }); " +
            "foreach($p in $left){ $v += ('\\??\\' + $p); $v += '' }; " +
            "Set-ItemProperty -LiteralPath $k -Name PendingFileRenameOperations -Value ([string[]]$v) -Type MultiString }";
    }

    /// <summary>The last resort: Windows deletes them at the next restart (files first, then the folders, deepest first).</summary>
    public static int AtRestart(Leftovers left, Func<string, bool> atReboot = null)
    {
        atReboot ??= path => MoveFileExW(path, null, DelayUntilReboot);
        return left.Files.Concat(left.Dirs).Count(atReboot);
    }

    // ------------------------------------------------------------ What the question and the result say

    public const string Title = "Remove CS2 IP Changer?";

    /// <summary>What goes either way, above the two choices.</summary>
    public static string Text() =>
        "Removes the Firewall rules and routes the app made (Regions too), the Start with Windows task, the app's files in " +
        $"{Root} and the app itself, and stops the phone tool. Steam and CS2 keep their own files.";

    /// <summary>The two choices: Keep (first, picked at the start) and Everything.</summary>
    public static string[] Choices(string dataDir, bool installed) =>
    [
        "Keep my settings and accounts\n" + (installed
            ? $"{dataDir} stays, so installing the app again brings everything back."
            : $"{dataDir} stays, next to where the app was."),
        "Delete everything\nSRT_Dados goes too: settings, servers, accounts list, saved Steam sign-ins, config backups and logs.",
    ];

    /// <summary>The window after Remove. Problem = something was left that needs you.</summary>
    public static (string Title, string Text, bool Problem) Result(Outcome o, string dataDir)
    {
        var (network, task, files, everything, helper) = (o.Network, o.Task, o.Files, o.Everything, o.Helper);
        var problem = network != null || task != null || o.Registry != null || files.ProgramDataRefused || files.DataRefused || files.AppRefused;
        var lines = new List<string>
        {
            network == null ? (task == null ? "The Firewall rules, the routes and the Start with Windows task are gone." : "The Firewall rules and routes are gone.") : "Couldn't remove: " + network + ". Restart the PC to clear routes. Rules are in Windows Firewall > Outbound Rules (names start with ServerRouteTool).",
        };
        if (task != null) lines.Add(task);
        if (o.Registry != null) lines.Add("The entry in Windows Settings > Installed apps couldn't be removed: " + o.Registry);
        if (files.ProgramDataRefused) lines.Add($"{Root} was left alone: programs without admin rights can change it.");
        if (files.DataRefused || files.AppRefused) lines.Add("The app's folder was left alone: programs without admin rights can change it.");
        if (files.Left.Count > 0)
            lines.Add(helper ? "The last files go a few seconds after the app closes. If Windows still holds one, it goes at the next restart."
                : "Some files are in use by this copy: Windows deletes them at the next restart.");
        if (KeepsApp(network)) lines.Add($"The app and its files stayed, so you can try again: open the app and Remove again, or run {AppIds.ExeName} {AppIds.Args.Restore}.");
        else if (!everything) lines.Add($"Kept: {dataDir}. Delete that folder if you won't install the app again.");
        return (problem ? "Not everything was removed" : "CS2 IP Changer was removed", string.Join("\n\n", lines), problem);
    }

    const uint DelayUntilReboot = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileExW(string from, string to, uint flags);
}
