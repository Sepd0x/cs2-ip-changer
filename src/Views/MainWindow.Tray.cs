using System.Windows;
using System.Windows.Controls;
using SRT.Core;

namespace SRT;

// Living in the background: the notification area icon, Start with Windows, the mobile data counter, Remove everything and quit
public partial class MainWindow
{
    Tray _tray;
    bool _quitting, _startHidden;

    // ------------------------------------------------------------ Notification area

    void StartTray()
    {
        try
        {
            _tray = new Tray(TrayMenu, ShowFromTray) { Tip = TrayTip(SideState?.Text) };
        }
        catch (Exception ex) { Log.Warn("The icon in the notification area couldn't be made: " + ex.Message); }
    }

    /// <summary>"CS2 IP Changer: Protected" (what the top bar says).</summary>
    public static string TrayTip(string state) => Tray.TrimTip(string.IsNullOrWhiteSpace(state) ? "CS2 IP Changer" : "CS2 IP Changer: " + state);

    /// <summary>The menu, top to bottom: the state (not clickable), Open, the commands, Quit. Key null = separator.</summary>
    public static List<(string Text, string Key, bool Enabled)> TrayLayout(string state, bool armed, bool phone) =>
    [
        (string.IsNullOrWhiteSpace(state) ? "CS2 IP Changer" : state, "state", false),
        (null, null, false),
        ("Open", "open", true),
        ("New IP + next account", "switch", true),
        ("Change IP", "changeip", phone),
        ("Regions", "regions", true),
        ("Turn off protection", "off", armed),
        (null, null, false),
        ("Quit", "quit", true),
    ];

    IReadOnlyList<Tray.Item> TrayMenu() => TrayLayout(SideState.Text, ProtectionArmed, !_s.NoPhone).Select(l => l.Text == null ? new Tray.Item(null)
        : new Tray.Item(l.Text, l.Key == "state" ? null : () => TrayCommand(l.Key), l.Enabled, l.Key == "open")).ToList();

    void TrayCommand(string key)
    {
        Telemetry.Click("Tray_" + key);
        switch (key)
        {
            case "open": ShowFromTray(); break;
            case "switch": _ = QuickSwitch("Tray"); break;
            case "changeip": _ = RunCommand("Change IP", "starting…", () => ChangeIp(confirm: true)); break;
            case "regions": ShowFromTray(); NavRegions.IsChecked = true; break;
            case "off": ShowFromTray(); Restore_Click(this, new RoutedEventArgs()); break;
            case "quit": _quitting = true; Close(); break;   // a question on the way (Keep and close) opens the window
        }
    }

    void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void HideToTray()
    {
        Hide();
        if (_s.TrayHinted) return;
        _s.TrayHinted = true;
        SaveSettings();
        _tray?.Balloon("CS2 IP Changer is still running", "Protection carries on. Click the icon to open the app; right-click it for New IP, Regions or Quit.");
    }

    /// <summary>The X goes to the notification area: only with the option on and the icon there, never to quit, for an update or a Windows sign-out.</summary>
    public static bool GoesToTray(bool closeToTray, bool hasIcon, bool quitting, bool sessionEnding, bool closingDone, bool demo) =>
        closeToTray && hasIcon && !quitting && !sessionEnding && !closingDone && !demo;

    void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        if (_uiLock) return;
        _s.CloseToTray = CloseToTrayBox.IsChecked == true;
        SaveSettings();
    }

    /// <summary>Started with --tray (Start with Windows): no window until you click the icon.</summary>
    void StartHiddenIfAsked(string[] args)
    {
        _startHidden = !_demo && args.Contains(Autostart.TrayArg);
        if (!_startHidden) return;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        Loaded += async (_, _) =>
        {
            if (_tray != null) Hide();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            await Startup();
        };
    }

    /// <summary>A second start of the app (double click while it runs) brings this window back instead of "already running".</summary>
    void ListenForShow()
    {
        try
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, App.ShowEventName, out var created);
            if (!created)
            {
                // Made by someone else before the app: whoever made it could pop the window up mid-game. Not used.
                ev.Dispose();
                Log.Warn($"Another program already made {App.ShowEventName}: a second start of the app won't bring this window back.");
                return;
            }
            ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => Dispatcher.BeginInvoke(ShowFromTray), null, -1, false);
        }
        catch (Exception ex) { Log.Warn("A second start won't bring this window back: " + ex.Message); }
    }

    // ------------------------------------------------------------ Start with Windows (scheduled task, only for the installed copy)

    async Task ShowAutostart()
    {
        var allowed = Autostart.Allowed(Environment.ProcessPath);
        StartWithWindowsBox.IsEnabled = allowed;
        AutostartHint.Visibility = allowed ? Visibility.Collapsed : Visibility.Visible;
        var on = allowed && await Autostart.IsOn(Environment.ProcessPath) == true;
        _uiLock = true;
        try { StartWithWindowsBox.IsChecked = on; } finally { _uiLock = false; }
    }

    async void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        if (_uiLock || _demo) return;
        var on = StartWithWindowsBox.IsChecked == true;
        StartWithWindowsBox.IsEnabled = false;
        var problem = on ? await Autostart.TurnOn(Environment.ProcessPath) : await Autostart.TurnOff();
        StartWithWindowsBox.IsEnabled = true;
        if (problem != null)
        {
            Toast((on ? "Start with Windows didn't turn on: " : "Start with Windows didn't turn off: ") + problem, Tone.Error);
            await ShowAutostart();
            return;
        }
        Log.Info(on ? $"Start with Windows on: the scheduled task \"{Autostart.TaskName}\" opens the app in the notification area when you sign in."
            : $"Start with Windows off: the scheduled task \"{Autostart.TaskName}\" was deleted.");
        Toast(on ? "The app now opens in the notification area when you sign in to Windows." : "The app no longer starts with Windows.", true);
    }

    // ------------------------------------------------------------ Mobile data counter

    readonly DataMeter _data = new();
    DateTime _dataSaved = DateTime.Now;

    /// <summary>Every tick: what went through the phone's adapter since the last one, for this session and this month.</summary>
    void CountData()
    {
        var a = _s.NoPhone ? null : _last?.Egress ?? _last?.Candidate;
        var added = _data.Add(a?.Id, DataMeter.Read(a?.Id));
        if (added > 0)
        {
            var now = DateTime.Now;
            var before = _s.DataMonth == DataMeter.Month(now) ? _s.DataMonthBytes : 0;
            (_s.DataMonth, _s.DataMonthBytes) = DataMeter.AddToMonth(_s.DataMonth, _s.DataMonthBytes, added, now);
            if (DataMeter.Crossed(before, _s.DataMonthBytes, _s.DataWarnGb) && _s.DataWarnedMonth != _s.DataMonth)
            {
                _s.DataWarnedMonth = _s.DataMonth;
                SaveSettings();
                Toast($"{DataMeter.Text(_s.DataMonthBytes)} of mobile data used through the phone this month: your warning is at {_s.DataWarnGb:0.#} GB (Settings > Background and data).", Tone.Warn);
            }
            if ((now - _dataSaved).TotalMinutes >= 2) { _dataSaved = now; SaveSettings(); }
        }
        ShowData();
    }

    void ShowData()
    {
        var month = _s.DataMonth == DataMeter.Month(DateTime.Now) ? _s.DataMonthBytes : 0;
        DataUsage.Text = DataText(_data.Session, month, DateTime.Now);
        DataUsage.Visibility = _s.NoPhone || (month == 0 && _data.Session == 0) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>"Mobile data: 85 MB since the app opened, 1.24 GB in November."</summary>
    public static string DataText(long session, long month, DateTime now) =>
        $"Mobile data: {DataMeter.Text(session)} since the app opened, {DataMeter.Text(month)} in {now.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)}.";

    void DataWarn_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiLock) return;
        var t = DataWarnBox.Text.Trim().Replace(',', '.');
        _s.DataWarnGb = double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gb) ? Math.Clamp(gb, 0, 10000) : 0;
        DataWarnBox.Text = _s.DataWarnGb > 0 ? _s.DataWarnGb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "0";
        SaveSettings();
    }

    // ------------------------------------------------------------ Remove everything and quit

    async void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (Steam.Cs2Running) { Toast("Close CS2 first: removing the block now would send CS2's traffic through your home connection.", Tone.Warn); return; }
        var installed = Autostart.Allowed(Environment.ProcessPath);
        if (!await Ask("Remove everything and quit?", Uninstall.Text(Paths.Data, installed), "Remove and quit", danger: true)) return;
        _closingDone = true;
        _restored = true;
        _tick.Stop();
        ShowProgress("Removing the rules, routes, task and the app's files…");
        var got = await _regionGate.WaitAsync(TimeSpan.FromSeconds(10));
        string left, task;
        int later;
        try
        {
            left = await _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: false);
            task = await Autostart.TurnOff();
            later = await Task.Run(() => { Adb.StopServer(); return Uninstall.RemoveData(); });
        }
        finally { if (got) _regionGate.Release(); }
        Log.Info("Remove everything: " + (left == null ? "rules and routes gone" : "couldn't remove " + left) + "; task " + (task ?? "gone") +
            "; " + (later < 0 ? Uninstall.Root + " left alone (others can change it)" : later > 0 ? $"{later} files in {Uninstall.Root} go at the next restart" : Uninstall.Root + " gone") + ".");
        var problem = left != null || task != null || later < 0;
        var text = (left == null ? "The Firewall rules and routes are gone." : "Couldn't remove: " + left + ". Restart the PC to clear routes, or run the app with --restore.")
            + (task == null ? "" : "\n\n" + task)
            + (later < 0 ? $"\n\n{Uninstall.Root} was left alone: programs without admin rights can change it. Delete it yourself." :
                later > 0 ? $"\n\nSome files in {Uninstall.Root} are in use by this copy: Windows deletes them at the next restart." : "")
            + $"\n\nStill here: {Paths.Data} and the app itself. " + (installed ? $"Delete the folder {Installer.Target} to remove them." : "Delete the SRT_Dados folder and CS2IPChanger.exe to remove them.");
        HideProgress();
        new NoticeWindow(problem ? "Not everything was removed" : "Everything was removed", text, "Close", problem: problem).ShowAndWait();
        Close();
    }
}
