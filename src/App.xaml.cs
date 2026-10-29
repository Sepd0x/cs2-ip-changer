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
        Paths.Ensure();
        // Before the single-instance lock: it's the way out when a stuck copy holds the lock and its window won't open.
        if (args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase))) { RestoreAndExit(); return; }
        // One instance only: two would fight over the same Firewall rules (the demo touches nothing).
        var first = true;
        if (!args.Contains("--demo")) _single = new Mutex(true, InstanceName, out first);
        // Started by an update: the old copy is still closing, give it a few seconds.
        if (!first && args.Contains("--updated"))
        {
            try { first = _single.WaitOne(10000); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            MessageBox.Show("CS2 IP Changer is already running. Close it first.","CS2 IP Changer", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        // This copy holds the lock, so no other copy runs from the older unpacked folders (the demo touches nothing).
        if (!args.Contains("--demo")) Task.Run(Program.CleanOldUnpacked);
        DispatcherUnhandledException += OnUiError;
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Task: " + a.Exception?.GetBaseException().Message); Telemetry.Error(a.Exception); a.SetObserved(); };
        base.OnStartup(e);
        var w = new MainWindow(args);
        MainWindow = w;
        w.Show();
    }

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
        var running = false;
        try { if (Mutex.TryOpenExisting(InstanceName, out var other)) { running = true; other.Dispose(); } } catch (UnauthorizedAccessException) { running = true; }
        MessageBox.Show((left == null
                ? "Network restored: the Firewall rules and routes from CS2 IP Changer are gone."
                : "Couldn't remove: " + left + ".\nRestart the PC to clear routes. Rules can be removed in Windows Firewall > Outbound Rules (names start with ServerRouteTool v9).")
                + (running ? "\n\nAnother copy of the app is still running: close it, or it puts the rules back." : ""),
            "CS2 IP Changer", MessageBoxButton.OK, left == null && !running ? MessageBoxImage.Information : MessageBoxImage.Warning);
        Shutdown();
    }

    static void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // An error never closes the app: it goes to the log and the window carries on.
        Log.Error(e.Exception.GetBaseException().Message);
        Telemetry.Error(e.Exception);
        e.Handled = true;
    }
}
