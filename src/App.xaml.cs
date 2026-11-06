using System.Windows;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

public partial class App : Application
{
    static Mutex _single;
    const string InstanceName = @"Global\CS2IPChanger.Instance";

    protected override void OnStartup(StartupEventArgs e)
    {
        var args = e.Args.Where(a => a != AppIds.Args.NetMoved).ToArray();   // Program.Main's own marker, not an option
        // Before the single-instance lock: it's the way out when a stuck copy holds the lock and its window won't open.
        if (AppIds.Args.IsRestore(args)) { Paths.Ensure(); RestoreAndExit(); return; }
        // One instance only: two would fight over the same Firewall rules (the demo touches nothing).
        var first = true;
        if (!args.Contains(AppIds.Args.Demo)) _single = new Mutex(true, InstanceName, out first);
        // Started by an update: the old copy is still closing, give it a few seconds.
        if (!first && args.Contains(AppIds.Args.Updated))
        {
            try { first = _single.WaitOne(10000); }
            catch (AbandonedMutexException) { first = true; }
        }
        var uninstall = args.Contains(AppIds.Args.Uninstall);
        if (!first)
        {
            // The copy that runs (maybe only in the notification area) brings its window back; the message only if it can't.
            // Not for an update or the sign-in task (--tray): those start a copy while one already runs, nobody asked for the window.
            if (args.Contains(AppIds.Args.Tray)) { Shutdown(); return; }
            // Uninstall in Windows Settings while the app runs: the running copy opens the Remove question
            if (uninstall && Signal(UninstallEventName)) { Shutdown(); return; }
            if (!args.Contains(AppIds.Args.Updated) && ShowRunning()) { Shutdown(); return; }
            AlreadyRunningWindow(OpenInOtherSession()).ShowAndWait();
            Shutdown();
            return;
        }
        // Uninstall in Windows Settings, no copy running: only the Remove question, without the main window (nothing is set up first)
        if (uninstall) { UninstallAndExit(); return; }
        // The usage stats notice, before the app makes a folder, a rule or a route: Quit (or Escape) leaves the PC as it was.
        // The notice would become the app's MainWindow and closing it would end the app, so the main window decides that again below.
        var updated = args.Contains(AppIds.Args.Updated);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var settingsFile = Paths.File(AppIds.Files.Settings);
        var stats = StatsGate(args, () => StatsNoticeWindow(ProtectionLeftOn(args), StatsNoticeIn(settingsFile)).ShowAndWait(), settingsFile);
        if (!stats.Go)
        {
            if (updated) UndoAfterUpdateQuit();
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Paths.Ensure();
        // This copy holds the lock, so no other copy runs from the older unpacked folders (the demo touches nothing).
        // The installed copy keeps its entry in Windows Settings > Installed apps (after an Install or an update: the version)
        if (!args.Contains(AppIds.Args.Demo)) Task.Run(() =>
        {
            Program.CleanOldUnpacked();
            Installer.CleanStage();
            if (Installer.KeepRegistered(Environment.ProcessPath, Updater.Clean(AppInfo.Version)) is string why) Log.Warn("The entry in Windows Settings > Installed apps couldn't be written: " + why);
        });
        DispatcherUnhandledException += OnUiError;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { Crash.Write(a.ExceptionObject as Exception, "fatal"); Telemetry.Fatal(a.ExceptionObject as Exception); };
        if (!args.Contains(AppIds.Args.Demo)) Crash.Started();
        Exit += (_, _) => { Crash.Ended(); Adb.SendPendingOn(); Adb.KillOwnServer(); };
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Task: " + a.Exception?.GetBaseException().Message); Crash.Write(a.Exception, "task"); Telemetry.Error(a.Exception); a.SetObserved(); };
        base.OnStartup(e);
        var w = new MainWindow(args, stats);
        MainWindow = w;
        w.Show();
    }

    /// <summary>Set by a second start: the running copy shows its window (MainWindow.ListenForShow).</summary>
    public const string ShowEventName = @"Local\CS2IPChanger.Show";

    /// <summary>Set by Uninstall in Windows Settings while a copy runs: that copy opens the Remove question (MainWindow.ListenForUninstall).</summary>
    public const string UninstallEventName = @"Local\CS2IPChanger.Uninstall";

    static bool ShowRunning() => Signal(ShowEventName);

    static bool Signal(string name)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out var ev)) return false;
            using (ev)
            {
                // Only the running copy may come to the front (not any process).
                var me = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
                    using (p) if (p.Id != me.Id) AllowSetForegroundWindow(p.Id);
                return ev.Set();
            }
        }
        catch { return false; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    static bool _released;

    /// <summary>Lets the updated copy start while this one closes.</summary>
    public static void ReleaseSingleInstance()
    {
        if (_single == null) return;
        try { _single.ReleaseMutex(); } catch { }
        _single.Dispose();
        _single = null;
        _released = true;
    }

    /// <summary>The update couldn't start the new copy and this one keeps running: it takes the single-instance lock back.</summary>
    public static void ReacquireSingleInstance()
    {
        if (!_released || _single != null) return;
        _released = false;
        try
        {
            var m = new Mutex(true, InstanceName, out var first);
            if (first) _single = m; else m.Dispose();   // another copy got it meanwhile: nothing else changes
        }
        catch (Exception ex) { Log.Warn("Could not take the single-instance lock back: " + ex.Message); }
    }

    /// <summary>
    /// "CS2IPChanger.exe --restore": undoes the rules and routes without opening the window
    /// (for when the window won't open, or before deleting the folder). The way out: the Regions rule goes too.
    /// </summary>
    void RestoreAndExit()
    {
        string left;
        try
        {
            var servers = Store.Load<List<ServerItem>>(Paths.File(AppIds.Files.Servers), () => new());
            left = new Guard().RestoreAll(servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: false).GetAwaiter().GetResult();
        }
        catch (Exception ex) { left = ex.Message; }
        // Start with Windows goes too: --restore is the way out, the task would start the app (and its rules) again at sign-in.
        // Off the UI thread: Proc.Run's awaits would come back to this thread, which is blocked waiting here.
        // Said on its own: "restart the PC to clear routes" is no help for a task.
        string task;
        try { task = Task.Run(Autostart.TurnOff).GetAwaiter().GetResult(); }
        catch (Exception ex) { task = ex.Message; }
        var running = false;
        try { if (Mutex.TryOpenExisting(InstanceName, out var other)) { running = true; other.Dispose(); } } catch (UnauthorizedAccessException) { running = true; }
        RestoredWindow(left, running, task).ShowAndWait();
        Shutdown();
    }

    /// <summary>
    /// "CS2IPChanger.exe --uninstall" (Uninstall in Windows Settings > Installed apps) with no copy running: the same question as
    /// Settings > Remove everything, then the same removal, without opening the main window or setting anything up first.
    /// </summary>
    void UninstallAndExit()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (Steam.Cs2Running)
        {
            new NoticeWindow("Close CS2 first", "Removing the app's block now would send CS2's traffic through your home connection. Close CS2, then click Uninstall again.", "Close", problem: true).ShowAndWait();
            Shutdown();
            return;
        }
        Uninstall.Outcome o = null;
        var ask = new NoticeWindow(Uninstall.Title, Uninstall.Text(), "Remove", "Cancel", choices: Uninstall.Choices(Paths.Data, Autostart.Allowed(Environment.ProcessPath)), danger: true,
            action: async w =>
            {
                w.Working = true;
                w.Say("Removing the rules, routes, task and the app's files…");
                try
                {
                    var servers = Store.Load<List<ServerItem>>(Paths.File(AppIds.Files.Servers), () => new());
                    o = await Uninstall.Run(w.Choice == 1, () => new Guard().RestoreAll(servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: false));
                }
                catch (Exception ex)
                {
                    // As the Remove inside the app: never left in Working with no word (the UI error handler isn't set up on this path)
                    Log.Error("Remove stopped: " + ex.Message);
                    w.Working = false;
                    w.Finish();
                    w.Say("Remove stopped: " + ex.Message + $" Run {AppIds.ExeName} {AppIds.Args.Restore} to undo the network changes.", bad: true);
                    return;
                }
                Log.Info(Uninstall.LogLine(o));
                w.Working = false;
                w.Close();
            });
        ask.ShowAndWait();
        if (o != null)
        {
            var (title, text, problem) = Uninstall.Result(o, Paths.Data);
            new NoticeWindow(title, text, "Close", problem: problem).ShowAndWait();
        }
        Shutdown();
    }

    /// <summary>
    /// What the usage stats notice gave at this start, passed to the main window. Go: the app can go on. JustAccepted: Accept in this
    /// start (the main window saves it with the rest of the settings). AskedAgain: the notice showed again because an older one was
    /// accepted, so it isn't a first start.
    /// </summary>
    public readonly record struct StatsConsent(bool Go, bool JustAccepted = false, bool AskedAgain = false)
    {
        /// <summary>The first start on this PC (the notice was never accepted before): first_start and the Steam and CS2 check.</summary>
        public bool FirstStart => JustAccepted && !AskedAgain;
    }

    /// <summary>
    /// Go when the app can go on: the usage stats notice was accepted before (settings.json, read only) or is now.
    /// Quit or Escape gives no Go before anything is made, changed or sent. The demo and the recording build send nothing,
    /// so they don't ask.
    /// </summary>
    public static StatsConsent StatsGate(string[] args, Func<bool> ask, string settingsFile)
    {
        if (args.Contains(AppIds.Args.Demo) || Rec.On) return new(true);
        var had = StatsNoticeIn(settingsFile);
        if (had >= Telemetry.NoticeVersion) return new(true);
        if (!ask()) return new(false);
        return new(true, JustAccepted: true, AskedAgain: had > 0);
    }

    /// <summary>The current notice was accepted before (<see cref="StatsNoticeIn"/>).</summary>
    public static bool StatsAcceptedIn(string settingsFile) => StatsNoticeIn(settingsFile) >= Telemetry.NoticeVersion;

    /// <summary>
    /// Which notice settings.json says was accepted: 0 none, 1 one from before beta.9 ("StatsAccepted": true alone), else "StatsNotice".
    /// Read by Store.Peek, as Store.Load reads it (a file locked for a moment is tried again for about 2 s, a damaged one falls back to
    /// .prev, names in any case) but only read: nothing is written, not even the .broken copy. A missing or unreadable file is "not yet".
    /// </summary>
    public static int StatsNoticeIn(string settingsFile) =>
        Store.Peek<Settings>(settingsFile) is { StatsAccepted: true } s ? Math.Max(s.StatsNotice, 1) : 0;

    /// <summary>
    /// Started by an update that left protection on: --updated with --on, or --updated alone (beta.9). With --off or --standby nothing
    /// was on, so the notice doesn't say it was. The rule of <see cref="Guard.OnAtStart"/> for an update.
    /// </summary>
    public static bool ProtectionLeftOn(string[] args) =>
        args.Contains(AppIds.Args.Updated) && Guard.OnAtStart(true, args, false, false, false, noPhone: false);

    /// <summary>The usage stats notice: the short text, the full list under See the full list, Accept and Quit.</summary>
    /// <param name="leftOn">Started by an update that left protection on (<see cref="ProtectionLeftOn"/>): the notice says Quit turns it off.</param>
    /// <param name="had">The notice accepted before (<see cref="StatsNoticeIn"/>): above 0, what changed since then is added.</param>
    internal static NoticeWindow StatsNoticeWindow(bool leftOn = false, int had = 0) =>
        new(Telemetry.NoticeTitle, StatsNoticeBody(leftOn, had), "Accept", "Quit", more: Telemetry.NoticeList);

    /// <summary>The notice's visible text (<see cref="StatsNoticeWindow"/>), without the full list.</summary>
    public static string StatsNoticeBody(bool leftOn, int had)
    {
        var changed = Telemetry.NoticeChangedFor(had);
        return Telemetry.NoticeShort + (changed != "" ? "\n\n" + changed : "") + (leftOn ? "\n\n" + Telemetry.NoticeAfterUpdate : "");
    }

    /// <summary>
    /// Quit at the notice right after an update: the version before left its protection on for this copy to take over. With no
    /// app left to turn it off, it's undone like Turn off protection (the Regions rule stays, as it does there).
    /// Not while CS2 runs: it would carry on with your home IP. Then it goes as closing with Keep (Guard.KeepOnClose): the phone gets
    /// its default route back and the Whole PC rules go, so the PC isn't left without internet once the phone is gone.
    /// </summary>
    static void UndoAfterUpdateQuit()
    {
        try
        {
            Paths.Ensure();
            if (Steam.Cs2Running)
            {
                new Guard().KeepOnClose(cs2Running: true).GetAwaiter().GetResult();
                Log.Info("Usage stats notice: Quit after an update with CS2 open. The block stays, the phone got its default route back.");
                return;
            }
            var servers = Store.Load<List<ServerItem>>(Paths.File(AppIds.Files.Servers), () => new());
            var left = new Guard().RestoreAll(servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: true).GetAwaiter().GetResult();
            Log.Info("Usage stats notice: Quit after an update. " + (left == null ? "The protection the version before left on was turned off." : "Couldn't remove: " + left));
        }
        catch (Exception ex) { Log.Warn("Quit after an update: couldn't turn off the protection the version before left on: " + ex.Message); }
    }

    // The app's own windows for these two messages (NoticeWindow), not Windows' message box. The demo screenshots use them too.
    internal static NoticeWindow AlreadyRunningWindow(bool otherSession = false) =>
        new("CS2 IP Changer is already running", RunningText(otherSession), "Close");

    /// <summary>
    /// Why this start stops. The lock is for the whole PC (the rules are), but the window can only be brought back in this Windows
    /// session: a copy open in another user's session has nothing in this taskbar.
    /// </summary>
    public static string RunningText(bool otherSession) => otherSession
        ? "CS2 IP Changer is open in another Windows user's session. It changes the Firewall for the whole PC, so only one copy can run: close it in that session first."
        : "Close the copy that is open first (look in the taskbar), then start the app again.";

    /// <summary>Another copy of the app runs in another Windows session (fast user switching). Read as admin; false when it can't be told.</summary>
    static bool OpenInOtherSession()
    {
        try
        {
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            var others = System.Diagnostics.Process.GetProcessesByName(me.ProcessName);
            try { return others.Any(p => p.Id != me.Id && p.SessionId != me.SessionId); }
            finally { foreach (var p in others) p.Dispose(); }
        }
        catch { return false; }
    }

    internal static NoticeWindow RestoredWindow(string left, bool running, string task = null)
    {
        var (title, text) = RestoredText(left, running, task);
        return new(title, text, "Close", problem: left != null || running || task != null);
    }

    /// <summary>--restore's result: what is left of the network (null = all clean), whether another copy still runs, and why the Start with Windows task stayed (null = gone).</summary>
    public static (string Title, string Text) RestoredText(string left, bool running, string task = null) =>
        (left == null && !running ? "Network restored" : "Network not fully restored",
            (left == null
                ? "The Firewall rules and routes from CS2 IP Changer are gone."
                : "Couldn't remove: " + left + ".\n\nRestart the PC to clear routes. Rules can be removed in Windows Firewall > Outbound Rules (names start with " + AppIds.RulePrefix + ").")
            + (task != null ? "\n\nThe Start with Windows task wasn't removed: " + task.TrimEnd().TrimEnd('.') + ". Open the app and turn off Settings > App > Start with Windows, or delete the task \"CS2 IP Changer\" in Task Scheduler." : "")
            + (running ? "\n\nAnother copy of the app is still running: close it, or it puts the rules back." : ""));

    static void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // An error never closes the app: it goes to the log and the window carries on.
        Log.Error(e.Exception.GetBaseException().Message);
        Crash.Write(e.Exception, "ui");
        Telemetry.Error(e.Exception);
        e.Handled = true;
    }
}
