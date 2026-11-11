using System.Collections.ObjectModel;
using System.Windows;
using SRT.Core;

namespace SRT;

// Settings > Open with CS2: programs you picked, opened as you (never with the app's admin rights) when the app opens CS2
public partial class MainWindow
{
    ObservableCollection<OpenWithItem> _openWith;

    void LoadOpenWith()
    {
        _openWith = new ObservableCollection<OpenWithItem>(_s.OpenWithCs2.Where(x => !string.IsNullOrWhiteSpace(x.Path)));
        OpenWithList.ItemsSource = _openWith;
    }

    void SaveOpenWith()
    {
        _s.OpenWithCs2 = _openWith.ToList();
        SaveSettings();
    }

    void OpenWithAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Programs (*.exe)") + "|*.exe", Title = Loc.T("Choose a program to open with CS2") };
        if (dlg.ShowDialog(this) != true) return;
        var path = dlg.FileName;
        if (!ShellLaunch.IsLaunchable(path, out var why)) { Toast(Loc.F("Can't add that file: {0}.", why), false); return; }
        if (_openWith.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) { Toast(Loc.T("That program is already in the list."), false); return; }
        var item = new OpenWithItem { Path = path };
        _openWith.Add(item);
        SaveOpenWith();
        Log.Info($"Open with CS2: added {path}.");
        Toast(Loc.F("{0} opens when the app opens CS2.", item.Name), true);
    }

    void OpenWithToggle_Click(object sender, RoutedEventArgs e) => SaveOpenWith();

    void OpenWithRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OpenWithItem item) return;
        _openWith.Remove(item);
        SaveOpenWith();
        Toast(Loc.F("{0} removed from Open with CS2.", item.Name), true);
    }

    /// <summary>Right when the app opens CS2: each program that is on and not running yet, with no arguments.</summary>
    void OpenWithCs2()
    {
        var list = _openWith.Where(x => x.Enabled).ToList();
        if (list.Count == 0) return;
        // In Whole PC these programs go out through the phone too: a new IP or a phone hiccup cuts their connections (logins, licences).
        if (_s.WholePc && _protOn) Log.Info("Whole PC is on, so the programs opened with CS2 also go through the phone. If one loses its connection after Change IP, use Game only: they stay on your home network.");
        if (_demo) { Toast(Loc.F("Demo: opening {0} with CS2.", string.Join(", ", list.Select(x => x.Name))), true); return; }
        foreach (var p in list)
        {
            if (ShellLaunch.IsRunning(p.Path)) continue;
            var err = ShellLaunch.Open(p.Path, p.AsAdmin);
            if (err == null) Log.Info(p.AsAdmin ? $"Opened {p.Name} with CS2, as administrator (Windows asks first)." : $"Opened {p.Name} with CS2, without admin rights.");
            else Log.Warn($"Couldn't open {p.Name} with CS2: {err}. The app never starts it with its own admin rights.");
        }
    }
}
