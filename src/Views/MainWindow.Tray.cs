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

    /// <summary>
    /// The menu, top to bottom: the state (not clickable), Open, the commands, Quit. Key null = separator. <paramref name="on"/>: protection
    /// is on, so the line turns it off; off, it turns it on (greyed in No phone mode, where there is nothing to turn on). <paramref name="quick"/>:
    /// the switch's name as its button says it now ("Log in to atlas + rejoin" when the IP doesn't change).
    /// </summary>
    public static List<(string Text, string Key, bool Enabled)> TrayLayout(string state, bool on, bool phone, string quick = "New IP + next account") =>
    [
        (string.IsNullOrWhiteSpace(state) ? "CS2 IP Changer" : state, "state", false),
        (null, null, false),
        ("Open", "open", true),
        on ? ("Turn off protection", "off", true) : ("Turn on protection", "on", phone),
        (string.IsNullOrWhiteSpace(quick) ? "New IP + next account" : quick, "switch", true),
        ("Change IP", "changeip", phone),
        ("Regions", "regions", true),
        (null, null, false),
        ("Quit", "quit", true),
    ];

    /// <summary>Built each time the menu opens, from the state of that moment. The state line has no command, so it is greyed.</summary>
    public static List<Tray.Item> TrayItems(string state, bool on, bool phone, Action<string> command, string quick = "New IP + next account") =>
        TrayLayout(state, on, phone, quick).Select(l => l.Text == null ? new Tray.Item(null)
            : new Tray.Item(l.Text, l.Key == "state" ? null : () => command(l.Key), l.Enabled, l.Key == "open")).ToList();

    IReadOnlyList<Tray.Item> TrayMenu()
    {
        UpdateQuickInfo();   // the switch's name as of now
        return TrayItems(SideState.Text, _protOn && !_s.NoPhone, !_s.NoPhone, TrayCommand, _quickName);
    }

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
            case "on": _ = TurnProtectionOn(ask: true); break;   // a question (CS2 open) opens the window
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
        _tray?.Balloon("CS2 IP Changer is still running", TrayHintText);
    }

    /// <summary>The first X to the notification area, shown once: it says nothing about protection, which may be off or in No phone mode.</summary>
    public const string TrayHintText = "Click the icon to open the app; right-click it for Regions or Quit.";

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
        _startHidden = !_demo && args.Contains(AppIds.Args.Tray);
        if (!_startHidden) return;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        Loaded += async (_, _) =>
        {
            if (_tray?.Shown == true) Hide();   // only with the icon really there (Explorer busy at sign-in can refuse it)
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            await Startup();
        };
    }

    /// <summary>A second start of the app (double click while it runs) brings this window back instead of "already running".</summary>
    async void ListenForShow()
    {
        try
        {
            // Made by someone else before the app: whoever made it could pop the window up mid-game. Not used.
            if (!await ListenFor(App.ShowEventName, () => Dispatcher.BeginInvoke(ShowFromTray)))
                Log.Warn($"Another program already made {App.ShowEventName}: a second start of the app won't bring this window back.");
        }
        catch (Exception ex) { Log.Warn("A second start won't bring this window back: " + ex.Message); }
    }

    /// <summary>
    /// After an update the copy before still holds the app's events for a few seconds while it closes (its stats, the phone tool),
    /// so the new copy tries again once a second, up to 15 s, before it gives up on them. Without an update, once.
    /// </summary>
    public static bool EventRetry(bool justUpdated, int tries) => justUpdated && tries < 15;

    /// <summary>Makes the event <paramref name="name"/> and runs <paramref name="onSet"/> each time it is set. False when another process holds it.</summary>
    async Task<bool> ListenFor(string name, Action onSet)
    {
        for (var tries = 0; ; tries++)
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name, out var created);
            if (created)
            {
                ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => onSet(), null, -1, false);
                return true;
            }
            ev.Dispose();
            if (!EventRetry(_justUpdated, tries)) return false;
            await Task.Delay(1000);
        }
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
            Telemetry.Data(added);
            var now = DateTime.Now;
            (_s.DataMonth, _s.DataMonthBytes) = DataMeter.AddToMonth(_s.DataMonth, _s.DataMonthBytes, added, now);
            if (WarnNow(_s.DataMonthBytes, _s.DataWarnGb, _s.DataWarnedMonth, _s.DataMonth))
            {
                _s.DataWarnedMonth = _s.DataMonth;
                SaveSettings();
                Toast($"{DataMeter.Text(_s.DataMonthBytes)} of mobile data used through the phone this month: your warning is at {_s.DataWarnGb:0.#} GB (Settings > Mobile data).", Tone.Warn);
            }
            if ((now - _dataSaved).TotalMinutes >= 2) { _dataSaved = now; SaveSettings(); }
        }
        ShowData();
    }

    /// <summary>
    /// The monthly warning is due: this month is at or past the limit and wasn't warned yet. Not only on the crossing: a limit set
    /// below what was already used this month warns on the next tick with data.
    /// </summary>
    public static bool WarnNow(long monthBytes, double gb, string warnedMonth, string month) =>
        gb > 0 && monthBytes >= DataMeter.Bytes(gb) && warnedMonth != month;

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


    // ------------------------------------------------------------ Remove everything and quit (also Uninstall in Windows Settings)

    bool _removing;

    async void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_demo || _removing) return;
        if (!IsVisible) ShowFromTray();
        if (Steam.Cs2Running) { Toast("Close CS2 first: removing the block now would send CS2's traffic through your home connection.", Tone.Warn); return; }
        if (!await Ask(Uninstall.Title, Uninstall.Text(), "Remove", danger: true, choices: Uninstall.Choices(Paths.Data, Autostart.Allowed(Environment.ProcessPath)))) return;
        var everything = _dialogChoice == 1;
        _removing = _closingDone = _restored = _closeBusy = true;   // a second X waits for the removal (CloseWaits)
        _tick.Stop();
        ShowProgress("Removing the rules, routes, task and the app's files…");
        var got = await _regionGate.WaitAsync(TimeSpan.FromSeconds(10));
        Uninstall.Outcome o;
        try { o = await Uninstall.Run(everything, () => _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList(), keepRegions: false)); }
        catch (Exception ex)
        {
            // Never left half-way with the window stuck: say it, and the app closes (opening it again puts its protection back)
            Log.Error("Remove stopped: " + ex.Message);
            HideProgress();
            new NoticeWindow("Not everything was removed", "Remove stopped: " + ex.Message + $"\n\nOpen the app and try again, or run {AppIds.ExeName} {AppIds.Args.Restore}.", "Close", problem: true) { Owner = this }.ShowAndWait();
            _closeBusy = false;
            Close();
            return;
        }
        finally { if (got) _regionGate.Release(); }
        Log.Info(Uninstall.LogLine(o));
        HideProgress();
        var (title, text, problem) = Uninstall.Result(o, Paths.Data);
        new NoticeWindow(title, text, "Close", problem: problem) { Owner = this }.ShowAndWait();
        _closeBusy = false;
        Close();
    }

    /// <summary>Uninstall in Windows Settings while the app runs: the copy started by Windows asks this one to open the same question.</summary>
    async void ListenForUninstall()
    {
        // Made by someone else first: not used (it could open the question at any moment)
        try { await ListenFor(App.UninstallEventName, () => Dispatcher.BeginInvoke(() => RemoveAll_Click(this, new RoutedEventArgs()))); }
        catch { }
    }
}
