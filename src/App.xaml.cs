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
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Tarefa: " + a.Exception?.GetBaseException().Message); a.SetObserved(); };
        base.OnStartup(e);
        var w = new MainWindow(e.Args);
        MainWindow = w;
        w.Show();
    }

    static void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Um erro nunca fecha a app: fica no registo e a janela continua.
        Log.Error(e.Exception.GetBaseException().Message);
        e.Handled = true;
    }
}
