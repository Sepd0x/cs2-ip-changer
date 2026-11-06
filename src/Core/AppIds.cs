namespace SRT.Core;

/// <summary>
/// The names the app is known by: its exe, its folders and files, its command line, its Firewall rules and its User-Agent.
/// They never change between versions: an older copy, the update, the Install copy, Windows' Uninstall button and
/// tools/restore.cmd all count on them (tests/Untangle12Tests.cs checks the csproj and restore.cmd against these).
/// </summary>
public static class AppIds
{
    /// <summary>The released exe without ".exe": the &lt;AssemblyName&gt; in ServerRouteTool.csproj. The Recording build is named
    /// CS2IPChanger-Recording on purpose and never updates itself (<see cref="Updater.CanSelfUpdate"/>).</summary>
    public const string Name = "CS2IPChanger";
    public const string ExeName = Name + ".exe";
    /// <summary>The update's download, next to the app until it is swapped in.</summary>
    public const string NewExeName = Name + ".new.exe";
    /// <summary>The exe an update replaced, until the next start removes it.</summary>
    public const string OldSuffix = ".old";
    /// <summary>A dev build has it next to the exe; the single-file exe doesn't.</summary>
    public const string DllName = Name + ".dll";

    /// <summary>The install folder's name in Program Files.</summary>
    public const string InstallFolder = "CS2 IP Changer";
    /// <summary>C:\Program Files\CS2 IP Changer, wherever Windows keeps Program Files.</summary>
    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallFolder);
    /// <summary>%ProgramData%\CS2IPChanger: admin-only, holds the ADB copy, the unpacked files, the Install copy and the task's XML.</summary>
    public static string ProgramDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);
    /// <summary>The data folder next to the exe.</summary>
    public const string DataFolder = "SRT_Dados";

    /// <summary>Files in SRT_Dados, and the leftovers of a save (<see cref="Store"/>).</summary>
    public static class Files
    {
        public const string Settings = "settings.json";
        public const string Servers = "servers.json";
        /// <summary>The routes and metric the app changed in Windows (only read from an admin-only folder).</summary>
        public const string Owned = "owned.json";
        /// <summary>Says whether the last start ended normally (<see cref="Crash"/>).</summary>
        public const string RunningFlag = "running.flag";
        /// <summary>A save is written here first, then swapped in.</summary>
        public const string TmpSuffix = ".tmp";
        /// <summary>A file that didn't read as JSON is kept as name.broken-date.</summary>
        public const string BrokenMark = ".broken-";
    }

    /// <summary>The command line. Each is matched as it is written here, except --restore (any case: it's typed by hand).</summary>
    public static class Args
    {
        /// <summary>Undoes the rules and routes without opening the window.</summary>
        public const string Restore = "--restore";
        /// <summary>The demo (screenshots): touches nothing.</summary>
        public const string Demo = "--demo";
        /// <summary>Started by an update; followed by <see cref="On"/> or <see cref="Off"/>.</summary>
        public const string Updated = "--updated";
        /// <summary>After <see cref="Updated"/>: protection was on in the old copy (beta.9 passed nothing when it was protected).</summary>
        public const string On = "--on";
        /// <summary>After <see cref="Updated"/>: protection was off.</summary>
        public const string Off = "--off";
        /// <summary>What beta.9 passed while it waited for the phone; still read, as off.</summary>
        public const string Standby = "--standby";
        /// <summary>The sign-in task: start in the notification area.</summary>
        public const string Tray = "--tray";
        /// <summary>Windows' Uninstall button (the registry's UninstallString).</summary>
        public const string Uninstall = "--uninstall";
        /// <summary>The Install copy: the folder the app was started from comes next.</summary>
        public const string InstallFrom = "--install-from";
        /// <summary>Program.Main's own second start from the admin-only unpack folder; never shown to App or MainWindow.</summary>
        public const string NetMoved = "--net-moved";

        public static bool IsRestore(IEnumerable<string> args) => args.Any(a => a.Equals(Restore, StringComparison.OrdinalIgnoreCase));

        /// <summary>The state an update passes on: the same as now.</summary>
        public static string UpdateState(bool on) => on ? On : Off;

        /// <summary>A state the new copy understands after <see cref="Updated"/>.</summary>
        public static bool IsUpdateState(string state) => state is On or Off or Standby;

        /// <summary>What the new copy is started with after an update.</summary>
        public static string ForUpdate(string state) => IsUpdateState(state) ? Updated + " " + state : Updated;
    }

    /// <summary>Every Firewall rule of the app starts with this (since alpha.1; tools/restore.cmd removes them by it too).</summary>
    public const string RulePrefix = "ServerRouteTool v9";

    /// <summary>The app's own requests (GitHub releases, the public IP check).</summary>
    public const string UserAgent = Name + "/0.1";
}
