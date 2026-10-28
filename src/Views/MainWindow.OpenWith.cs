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
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program to open with CS2" };
        if (dlg.ShowDialog(this) != true) return;
        var path = dlg.FileName;
        if (!ShellLaunch.IsLaunchable(path, out var why)) { Toast($"Can't add that file: {why}.", false); return; }
        if (_openWith.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) { Toast("That program is already in the list.", false); return; }
        var item = new OpenWithItem { Path = path };
        _openWith.Add(item);
        SaveOpenWith();
        Log.Info($"Open with CS2: added {path}.");
        Toast($"{item.Name} opens when the app opens CS2.", true);
    }

    void OpenWithToggle_Click(object sender, RoutedEventArgs e) => SaveOpenWith();

    void OpenWithRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OpenWithItem item) return;
        _openWith.Remove(item);
        SaveOpenWith();
        Toast($"{item.Name} removed from Open with CS2.", true);
    }

    /// <summary>Right when the app opens CS2: each program that is on and not running yet, with no arguments.</summary>
    void OpenWithCs2()
    {
        var list = _openWith.Where(x => x.Enabled).ToList();
        if (list.Count == 0) return;
        if (_demo) { Toast("Demo: opening " + string.Join(", ", list.Select(x => x.Name)) + " with CS2.", true); return; }
        foreach (var p in list)
        {
            if (ShellLaunch.IsRunning(p.Path)) continue;
            var err = ShellLaunch.Open(p.Path, p.AsAdmin);
            if (err == null) Log.Info(p.AsAdmin ? $"Opened {p.Name} with CS2, as administrator (Windows asks first)." : $"Opened {p.Name} with CS2, without admin rights.");
            else Log.Warn($"Couldn't open {p.Name} with CS2: {err}. The app never starts it with its own admin rights.");
        }
    }
}
