using System.Windows;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Paths.Ensure();
        DispatcherUnhandledException += OnUiError;
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Task: " + a.Exception?.GetBaseException().Message); a.SetObserved(); };
        base.OnStartup(e);
        var w = new MainWindow(e.Args);
        MainWindow = w;
        w.Show();
    }

    static void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // An error never closes the app: it goes to the log and the window carries on.
        Log.Error(e.Exception.GetBaseException().Message);
        e.Handled = true;
    }
}
