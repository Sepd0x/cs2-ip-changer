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
        Loc.Init(Store.Peek<Settings>(Paths.File(AppIds.Files.Settings))?.Language);   // before any window
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
        // The usage stats notice, before the app makes a folder, a rule or a route: Accept or Turn off, and the app opens either way.
        // The notice would become the app's MainWindow and closing it would end the app, so the main window decides that again below.
        var updated = args.Contains(AppIds.Args.Updated);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var settingsFile = Paths.File(AppIds.Files.Settings);
        var stats = StatsGate(args, () => StatsNoticeWindow(StatsNoticeIn(settingsFile)).ShowAndWait(), settingsFile);
        if (!stats.Go) { Shutdown(); return; }
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
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            Crash.Write(a.ExceptionObject as Exception, "fatal");
            // Whole PC's DNS and IPv6 rules would leave the PC without names once the phone is gone (2 s at most; kill switch and CS2 lock stay)
            if (Adb.Elevated && !args.Contains(AppIds.Args.Demo)) Guard.RemoveOnCrash(TimeSpan.FromSeconds(2));
            Telemetry.Fatal(a.ExceptionObject as Exception);
        };
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
            new NoticeWindow(Loc.T("Close CS2 first"), Loc.T("Removing the app's block now would send CS2's traffic through your home connection. Close CS2, then click Uninstall again."), Loc.T("Close"), problem: true).ShowAndWait();
            Shutdown();
            return;
        }
        Uninstall.Outcome o = null;
        var ask = new NoticeWindow(Uninstall.Title, Uninstall.Text(), Loc.T("Remove"), Loc.T("Cancel"), choices: Uninstall.Choices(Paths.Data, Autostart.Allowed(Environment.ProcessPath)), danger: true,
            action: async w =>
            {
                w.Working = true;
                w.Say(Loc.T("Removing the rules, routes, task and the app's files…"));
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
                    w.Say(Loc.F("Remove stopped: {0}", ex.Message) + " " + Loc.F("Run {0} {1} to undo the network changes.", AppIds.ExeName, AppIds.Args.Restore), bad: true);
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
    /// What the usage stats notice gave at this start, passed to the main window. Go: the app can go on (always, since 1.0.0: Turn off
    /// doesn't close it). JustAccepted: the notice was answered in this start (the main window saves it with the rest of the settings).
    /// Off: the answer was Turn off. AskedAgain: the notice showed again because an older one was accepted, so it isn't a first start.
    /// </summary>
    public readonly record struct StatsConsent(bool Go, bool JustAccepted = false, bool AskedAgain = false, bool Off = false)
    {
        /// <summary>The first start on this PC (the notice was never answered before): first_start and the Steam and CS2 check.</summary>
        public bool FirstStart => JustAccepted && !AskedAgain;
    }

    /// <summary>
    /// The notice is answered once, before the app opens (settings.json, read only, says if it was). <paramref name="ask"/> gives true
    /// for Accept, false for Turn off (or Escape): the app opens in both, and Turn off sends nothing. The demo and the recording build
    /// send nothing, so they don't ask. An older notice is shown again once (1.0.0, notice 5: to everyone who accepted in a beta,
    /// when the stats were required), but not to someone who already has the stats off: nothing is sent, so there is nothing to ask.
    /// </summary>
    public static StatsConsent StatsGate(string[] args, Func<bool> ask, string settingsFile)
    {
        if (args.Contains(AppIds.Args.Demo) || Rec.On) return new(true);
        var had = StatsNoticeIn(settingsFile);
        if (had >= Telemetry.NoticeVersion || had > 0 && StatsOffIn(settingsFile)) return new(true);
        var on = ask();
        return new(true, JustAccepted: true, AskedAgain: had > 0, Off: !on);
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

    /// <summary>settings.json says the notice was answered and the stats are off (read like <see cref="StatsNoticeIn"/>, nothing written).</summary>
    public static bool StatsOffIn(string settingsFile) => Store.Peek<Settings>(settingsFile) is { StatsAccepted: true, StatsOff: true };

    /// <summary>
    /// Started by an update that left protection on: --updated with --on, or --updated alone (beta.9). With --off or --standby nothing
    /// was on, so the notice doesn't say it was. The rule of <see cref="Guard.OnAtStart"/> for an update.
    /// </summary>
    public static bool ProtectionLeftOn(string[] args) =>
        args.Contains(AppIds.Args.Updated) && Guard.OnAtStart(true, args, false, false, false, noPhone: false);

    /// <summary>The usage stats notice: the short text, the full list under See the full list, Accept and Turn off.</summary>
    /// <param name="had">The notice accepted before (<see cref="StatsNoticeIn"/>): above 0, what changed since then is added.</param>
    internal static NoticeWindow StatsNoticeWindow(int had = 0) =>
        new(Loc.T(Telemetry.NoticeTitle), StatsNoticeBody(had), Loc.T("Accept"), Loc.T("Turn off"), more: Telemetry.NoticeList);

    /// <summary>The notice's visible text (<see cref="StatsNoticeWindow"/>), without the full list.</summary>
    public static string StatsNoticeBody(int had)
    {
        var changed = Telemetry.NoticeChangedFor(had);
        return Telemetry.NoticeShort + (changed != "" ? "\n\n" + changed : "");
    }

    // The app's own windows for these two messages (NoticeWindow), not Windows' message box. The demo screenshots use them too.
    internal static NoticeWindow AlreadyRunningWindow(bool otherSession = false) =>
        new(Loc.T("CS2 IP Changer is already running"), RunningText(otherSession), Loc.T("Close"));

    /// <summary>
    /// Why this start stops. The lock is for the whole PC (the rules are), but the window can only be brought back in this Windows
    /// session: a copy open in another user's session has nothing in this taskbar.
    /// </summary>
    public static string RunningText(bool otherSession) => otherSession
        ? Loc.T("CS2 IP Changer is open in another Windows user's session. It changes the Firewall for the whole PC, so only one copy can run: close it in that session first.")
        : Loc.T("Close the copy that is open first (look in the taskbar), then start the app again.");

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
        return new(title, text, Loc.T("Close"), problem: left != null || running || task != null);
    }

    /// <summary>--restore's result: what is left of the network (null = all clean), whether another copy still runs, and why the Start with Windows task stayed (null = gone).</summary>
    public static (string Title, string Text) RestoredText(string left, bool running, string task = null) =>
        (left == null && !running ? Loc.T("Network restored") : Loc.T("Network not fully restored"),
            (left == null
                ? Loc.T("The Firewall rules and routes from CS2 IP Changer are gone.")
                : Loc.F("Couldn't remove: {0}.", left) + "\n\n" + Loc.F("Restart the PC to clear routes. Rules can be removed in Windows Firewall > Outbound Rules (names start with {0}).", AppIds.RulePrefix))
            + (task != null ? "\n\n" + Loc.F("The Start with Windows task wasn't removed: {0}. Open the app and turn off Settings > App > Start with Windows, or delete the task \"CS2 IP Changer\" in Task Scheduler.", task.TrimEnd().TrimEnd('.')) : "")
            + (running ? "\n\n" + Loc.T("Another copy of the app is still running: close it, or it puts the rules back.") : ""));

    static void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // An error never closes the app: it goes to the log and the window carries on.
        Log.Error(e.Exception.GetBaseException().Message);
        Crash.Write(e.Exception, "ui");
        Telemetry.Error(e.Exception);
        e.Handled = true;
    }
}
