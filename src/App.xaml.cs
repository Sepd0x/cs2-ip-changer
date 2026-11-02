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
        var args = e.Args.Where(a => a != Program.Moved).ToArray();   // Program.Main's own marker, not an option
        // Before the single-instance lock: it's the way out when a stuck copy holds the lock and its window won't open.
        if (args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase))) { Paths.Ensure(); RestoreAndExit(); return; }
        // One instance only: two would fight over the same Firewall rules (the demo touches nothing).
        var first = true;
        if (!args.Contains("--demo")) _single = new Mutex(true, InstanceName, out first);
        // Started by an update: the old copy is still closing, give it a few seconds.
        if (!first && args.Contains("--updated"))
        {
            try { first = _single.WaitOne(10000); }
            catch (AbandonedMutexException) { first = true; }
        }
        var uninstall = args.Contains(Uninstall.Arg);
        if (!first)
        {
            // The copy that runs (maybe only in the notification area) brings its window back; the message only if it can't.
            // Not for an update or the sign-in task (--tray): those start a copy while one already runs, nobody asked for the window.
            if (args.Contains(Autostart.TrayArg)) { Shutdown(); return; }
            // Uninstall in Windows Settings while the app runs: the running copy opens the Remove question
            if (uninstall && Signal(UninstallEventName)) { Shutdown(); return; }
            if (!args.Contains("--updated") && ShowRunning()) { Shutdown(); return; }
            AlreadyRunningWindow().ShowAndWait();
            Shutdown();
            return;
        }
        // Uninstall in Windows Settings, no copy running: only the Remove question, without the main window (nothing is set up first)
        if (uninstall) { UninstallAndExit(); return; }
        // The usage stats notice, before the app makes a folder, a rule or a route: Quit (or Escape) leaves the PC as it was.
        // The notice would become the app's MainWindow and closing it would end the app, so the main window decides that again below.
        var updated = args.Contains("--updated");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!StatsGate(args, () => StatsNoticeWindow(updated).ShowAndWait(), Paths.File("settings.json")))
        {
            if (updated) UndoAfterUpdateQuit();
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Paths.Ensure();
        // This copy holds the lock, so no other copy runs from the older unpacked folders (the demo touches nothing).
        // The installed copy keeps its entry in Windows Settings > Installed apps (after an Install or an update: the version)
        if (!args.Contains("--demo")) Task.Run(() =>
        {
            Program.CleanOldUnpacked();
            Installer.CleanStage();
            if (Installer.KeepRegistered(Environment.ProcessPath, Updater.Clean(AppInfo.Version)) is string why) Log.Warn("The entry in Windows Settings > Installed apps couldn't be written: " + why);
        });
        DispatcherUnhandledException += OnUiError;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Crash.Write(a.ExceptionObject as Exception, "fatal");
        if (!args.Contains("--demo")) Crash.Started();
        Exit += (_, _) => { Crash.Ended(); Adb.KillOwnServer(); };
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Task: " + a.Exception?.GetBaseException().Message); Crash.Write(a.Exception, "task"); Telemetry.Error(a.Exception); a.SetObserved(); };
        base.OnStartup(e);
        var w = new MainWindow(args);
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
            var servers = Store.Load<List<ServerItem>>(Paths.File("servers.json"), () => new());
            left = new Guard().RestoreAll(servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: false).GetAwaiter().GetResult();
        }
        catch (Exception ex) { left = ex.Message; }
        // Start with Windows goes too: --restore is the way out, the task would start the app (and its rules) again at sign-in.
        // Off the UI thread: Proc.Run's awaits would come back to this thread, which is blocked waiting here.
        try { if (Task.Run(Autostart.TurnOff).GetAwaiter().GetResult() is string task) left = left == null ? task : left + "; " + task; }
        catch (Exception ex) { left = (left == null ? "" : left + "; ") + ex.Message; }
        var running = false;
        try { if (Mutex.TryOpenExisting(InstanceName, out var other)) { running = true; other.Dispose(); } } catch (UnauthorizedAccessException) { running = true; }
        RestoredWindow(left, running).ShowAndWait();
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
                var servers = Store.Load<List<ServerItem>>(Paths.File("servers.json"), () => new());
                o = await Uninstall.Run(w.Choice == 1, () => new Guard().RestoreAll(servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: false));
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

    /// <summary>Accept at the notice in this start: the main window saves it with the rest of the settings.</summary>
    public static bool StatsJustAccepted { get; private set; }

    /// <summary>
    /// True when the app can go on: the usage stats notice was accepted before (settings.json, read only) or is now.
    /// Quit or Escape returns false before anything is made, changed or sent. The demo and the recording build send nothing,
    /// so they don't ask.
    /// </summary>
    public static bool StatsGate(string[] args, Func<bool> ask, string settingsFile)
    {
        if (args.Contains("--demo") || Rec.On || StatsAcceptedIn(settingsFile)) return true;
        if (!ask()) return false;
        StatsJustAccepted = true;
        return true;
    }

    /// <summary>
    /// "StatsAccepted": true in settings.json, read the way Store.Load reads it (a file locked for a moment is tried again for
    /// about 2 s, a damaged one falls back to .prev) but only read: nothing is written, not even the .broken copy.
    /// A missing or unreadable file is "not yet".
    /// </summary>
    public static bool StatsAcceptedIn(string settingsFile)
    {
        if (!System.IO.File.Exists(settingsFile)) return false;
        string raw = null;
        for (var i = 0; raw == null; i++)
        {
            try { raw = System.IO.File.ReadAllText(settingsFile); }
            catch (System.IO.IOException) when (i < 20) { Thread.Sleep(100); }
            catch { return false; }
        }
        if (Accepted(raw) is bool b) return b;
        try { return System.IO.File.Exists(settingsFile + ".prev") && Accepted(System.IO.File.ReadAllText(settingsFile + ".prev")) == true; }
        catch { return false; }

        // null: not JSON (damaged)
        static bool? Accepted(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json.TrimStart('﻿'));
                return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(nameof(Settings.StatsAccepted), out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
            }
            catch { return null; }
        }
    }

    internal static NoticeWindow StatsNoticeWindow(bool updated = false) =>
        new(Telemetry.NoticeTitle, Telemetry.NoticeText + (updated ? "\n\n" + Telemetry.NoticeAfterUpdate : ""), "Accept", "Quit");

    /// <summary>
    /// Quit at the notice right after an update: the version before left its protection on for this copy to take over. With no
    /// app left to turn it off, it's undone like Turn off protection (the Regions rule stays, as it does there).
    /// Not while CS2 runs: it would carry on with your home IP.
    /// </summary>
    static void UndoAfterUpdateQuit()
    {
        if (Steam.Cs2Running) return;
        try
        {
            Paths.Ensure();
            var servers = Store.Load<List<ServerItem>>(Paths.File("servers.json"), () => new());
            var left = new Guard().RestoreAll(servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: true).GetAwaiter().GetResult();
            Log.Info("Usage stats notice: Quit after an update. " + (left == null ? "The protection the version before left on was turned off." : "Couldn't remove: " + left));
        }
        catch (Exception ex) { Log.Warn("Quit after an update: couldn't turn off the protection the version before left on: " + ex.Message); }
    }

    // The app's own windows for these two messages (NoticeWindow), not Windows' message box. The demo screenshots use them too.
    internal static NoticeWindow AlreadyRunningWindow() =>
        new("CS2 IP Changer is already running", "Close the copy that is open first (look in the taskbar), then start the app again.", "Close");

    internal static NoticeWindow RestoredWindow(string left, bool running)
    {
        var (title, text) = RestoredText(left, running);
        return new(title, text, "Close", problem: left != null || running);
    }

    /// <summary>--restore's result: what is left (null = all clean) and whether another copy still runs.</summary>
    public static (string Title, string Text) RestoredText(string left, bool running) =>
        (left == null && !running ? "Network restored" : "Network not fully restored",
            (left == null
                ? "The Firewall rules and routes from CS2 IP Changer are gone."
                : "Couldn't remove: " + left + ".\n\nRestart the PC to clear routes. Rules can be removed in Windows Firewall > Outbound Rules (names start with ServerRouteTool v9).")
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
