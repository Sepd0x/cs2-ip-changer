using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

/// <summary>Main window. Each area lives in its own MainWindow.*.cs file.</summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------ State
    Settings _s;
    readonly ObservableCollection<ServerItem> _servers = new();
    ICollectionView _serverView;
    readonly List<AccountItem> _accounts = new();
    /// <summary>accounts.json, read once by the first LoadAccounts (null before): the AccountItems share these objects.</summary>
    Dictionary<string, AccountMeta> _accMeta;
    readonly ObservableCollection<AccountItem> _accShown = new();
    readonly ObservableCollection<LogEntry> _log = new();
    readonly ObservableCollection<IpHistoryEntry> _history = new();
    readonly ObservableCollection<CheckItem> _checks = new();

    readonly Guard _guard = new();
    GuardReport _last;
    string _egressPublic, _homePublic;
    Net.IpInfo _egressInfo, _homeInfo;
    string _lastEgressKey;
    string _verifiedKey, _measuredKey;
    int _ipSeq;
    /// <summary>
    /// Protection is on: only when you turned it on (Guard.OnAtStart has the rules). _restored: the app is closing or removing
    /// everything, so no sync puts the rules back.
    /// </summary>
    bool _protOn, _turningOff;
    string[] _startArgs;
    bool _distrust, _restored, _phoneBusy, _tetherHinted, _cs2WasRunning, _wasProtected, _everProtected, _homeCached, _switching, _reveal, _syncing, _syncAgain, _changingIp, _querying, _refreshingAcc, _uiLock, _closingDone;
    string _adb;
    PhoneInfo _phone;
    DateTime _lastIpCheck = DateTime.MinValue;

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _queryTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _secondTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };

    bool _demo = false;   // only the development build turns it on (--demo)

    CheckItem _cFirewall, _cKill, _cEgress, _cRoutes, _cDiff, _cPhoneData, _cQuality, _cCs2, _cSteam, _cPc, _cRegions;
    string _steamPath;

    // After ":" any number is the port; after a space, comma or ";" only a 4 or 5 digit one that ends the word (review C5:
    // "1.2.3.4 35ms" from a server browser became port 35)
    static readonly Regex ServerRegex = new(@"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})(?!\d|\.\d)(?:[ \t]*:[ \t]*(?<port>\d+)(?!\d|\.\d)|[ \t]*[,; \t][ \t]*(?<port>\d{4,5})(?![\w.]))?", RegexOptions.Compiled);

    /// <param name="stats">What the usage stats notice gave before the window (App.StatsGate).</param>
    public MainWindow(string[] args, App.StatsConsent stats)
    {
        InitializeComponent();
        Rec.Install(this, PersonalWords);
        MarkPublicLists();
        ClampToWorkArea();   // before ParseDevArgs: a demo --size still wins
        ParseDevArgs(args);
        StartHiddenIfAsked(args);
        _justUpdated = args.Contains(AppIds.Args.Updated);
        _startArgs = args;   // on or off is decided in Startup (Guard.OnAtStart), with the Firewall and CS2 read
        ShowVersion();

        Log.Added += e => Dispatcher.BeginInvoke(() =>
        {
            MaskLog(e);
            _log.Insert(0, e);
            while (_log.Count > 600) _log.RemoveAt(_log.Count - 1);
        });

        _s = _demo ? new Settings() : Store.Load(Paths.File(AppIds.Files.Settings), () => new Settings());
        // Before beta.6 there was no PhoneSeen: an IP history means a phone was already checked on this PC
        if (!_s.PhoneSeen && _s.IpHistory.Count > 0) _s.PhoneSeen = true;
        // beta.7 and before kept the Steam Web API key in plain text: saved twice, so the .prev copy loses it too
        if (_s.ApiKeyMigrated)
        {
            try { SaveSettings(); SaveSettings(); Log.Info("Your Steam Web API key is now saved encrypted for your Windows user."); }
            catch (Exception ex) { Log.Warn("Couldn't save the Steam Web API key encrypted yet (" + ex.Message + "); the app tries again at the next save."); }
        }
        if (_s.ApiKeyLost) Log.Warn("The Steam Web API key in settings.json was saved by another Windows user or on another PC and can't be read here. Paste it again in Settings > Steam.");
        InitDev();
        if (stats.JustAccepted && !_demo)
        {
            _s.StatsAccepted = true;
            _s.StatsNotice = Telemetry.NoticeVersion;
            Log.Info("Usage stats accepted. README > Telemetry lists what is sent.");
            try { SaveSettings(); }
            catch (Exception ex) { Log.Warn("Couldn't save that the usage stats notice was accepted (" + ex.Message + "); it shows again at the next start."); }
        }
        Telemetry.Start(_demo, TelemetryNow, _s.StatsAccepted);
        TelemetryHook();
        // The day the notice was accepted: the dashboard's "new users" (also once for someone coming from beta.7, who sees it once;
        // not for an older notice accepted again, that's no first start)
        if (stats.FirstStart && !_demo) Telemetry.Count("first_start");
        if (stats.FirstStart && !_demo && !Rec.On) Loaded += (_, _) => FirstStartCheck();   // Steam and CS2 found? (4 nov.)
        LoadFlags();
        LoadOpenWith();
        LoadServers();
        _serverView = CollectionViewSourceFor(_servers);
        ServerList.ItemsSource = _serverView;
        LogList.ItemsSource = _log;
        History.ItemsSource = _history;
        AccountsPanel.ItemsSource = _accShown;
        Checks.ItemsSource = _checks;
        foreach (var h in _s.IpHistory.OrderByDescending(h => h.Time).Take(30)) _history.Add(h);

        // The three rows that decide whether you are protected come first.
        _checks.Add(_cEgress = new CheckItem { Title = "Phone connection", Help = "The phone over a USB cable, which is how the servers are reached." });
        _checks.Add(_cRoutes = new CheckItem { Title = "Servers through the phone", Help = "Windows confirms that every server in your list goes through the phone." });
        _checks.Add(_cCs2 = new CheckItem { Title = "CS2 only through the phone", Help = "Firewall rule: cs2.exe's game traffic (UDP) only goes out through the phone. Servers not in the list (game browser, invites) never see your home IP either. Web content that CS2 loads uses the normal connection." });
        _checks.Add(_cKill = new CheckItem { Title = "Block if the phone drops", Help = "The kill switch: a Firewall rule that blocks the listed servers on every adapter except the phone, so a dropped phone never falls back to your home IP." });
        _checks.Add(_cFirewall = new CheckItem { Title = "Windows Firewall", Help = "Must be on: it enforces the block." });
        _checks.Add(_cDiff = new CheckItem { Title = "Phone IP is not your home IP", Help = "If the phone is sharing your home Wi-Fi instead of mobile data, the IP would be the same." });
        _checks.Add(_cPhoneData = new CheckItem { Title = "Mobile data", Help = "With the phone's Wi-Fi on, tethering may carry your home network instead of mobile data. Read through ADB; without ADB, check it yourself." });
        _checks.Add(_cQuality = new CheckItem { Title = "Connection quality", Help = "Ping, jitter and loss through the phone, measured every 30 s. Weak means lag in game: the app warns, and can drop Whole PC to Game only while it lasts (Settings > Mobile data)." });
        _checks.Add(_cSteam = new CheckItem { Title = "Steam through the phone", Help = "steam.exe talks to Valve through the phone, like CS2. If Steam is on another network or reconnects mid-game, the server kicks you with \"No Steam logon\"." });
        _checks.Add(_cPc = new CheckItem { Title = "Other apps", Help = "Where the rest of the PC's traffic goes out (browser, Discord, downloads)." });
        _checks.Add(_cRegions = new CheckItem { Title = "Matchmaking regions", Chosen = true, Help = "Regions you turned off on the Regions page stay blocked for CS2 matchmaking until you allow them again (Regions > Allow all (block off)). Turning protection off, closing the app and No phone mode don't change them." });
        UpdateRegionsCheck();
        HookChecksView();

        ApplySettingsToUi();
        UpdateHistoryEmpty();

        _tick.Tick += async (_, _) => await OnTick();
        _queryTimer.Tick += async (_, _) =>
        {
            foreach (var srv in _servers.Where(x => x.IsLast)) srv.Raise(nameof(ServerItem.LastText));
            if (PageLog.IsVisible) UpdateDiagInfo();
            // Following a player keeps the list fresh on every page, also with the window minimized.
            if ((AutoQueryBox.IsChecked == true && PageServers.IsVisible) || _flags.Any(f => f.Follow)) await QueryServers(true);
        };
        _secondTimer.Tick += (_, _) =>
        {
            OnSecond();
            // In a match, the phone's Wi-Fi coming back on is checked every 2 s (Android moves tethering to Wi-Fi at once).
            if (!_demo && !_changingIp && _adb != null && DateTime.Now.Second % 2 == 0 && Steam.Cs2Running) _ = RefreshPhone();
        };
        _toastTimer.Tick += (_, _) => HideToast();

        StateChanged += (_, _) =>
        {
            RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        };
        Closing += OnClosing;
        if (!_startHidden) ContentRendered += async (_, _) => await Startup();   // hidden (--tray): Startup runs when it's loaded
    }

    static ICollectionView CollectionViewSourceFor(ObservableCollection<ServerItem> src)
    {
        var v = System.Windows.Data.CollectionViewSource.GetDefaultView(src);
        v.SortDescriptions.Add(new SortDescription(nameof(ServerItem.Favorite), ListSortDirection.Descending));
        v.SortDescriptions.Add(new SortDescription(nameof(ServerItem.IsLast), ListSortDirection.Descending));
        return v;
    }

    async Task Startup()
    {
        Log.Info($"CS2 IP Changer {AppInfo.Version} started{(_demo ? " (demo)" : "")}.");
        if (_demo) { _protOn = true; LoadDemo(); }
        else
        {
            var leftOn = false;
            try
            {
                if (Steam.Cs2Running)
                {
                    // A Firewall that couldn't be read counts as the block still there: the match isn't moved to the home IP on a guess
                    var rules = Firewall.Remaining();
                    leftOn = Firewall.BlockLeft(rules);
                    if (rules.Error != null) Log.Warn($"Couldn't read Windows Firewall ({rules.Error}) with CS2 open: protection stays on in case the block from the last run is still there.");
                }
            }
            catch { }
            _protOn = Guard.OnAtStart(_justUpdated, _startArgs, _s.ProtectOnStart, leftOn, Steam.Cs2Running, _s.NoPhone);
            if (_s.NoPhone) { }   // No phone mode says so itself (Guard.HomeMode)
            else if (!_protOn) Log.Info("Protection is off. Turn it on in Status (or the tray icon) when you want to play through the phone.");
            else Log.Info(_justUpdated ? "Protection stays on after the update." : leftOn ? "CS2 is open with the block from the last run still on: protection stays on, so the match isn't moved to your home IP."
                : "Protection turns on by itself (Settings > Protection > Turn on protection when the app starts).");
            StartupDev();
            ImportStartupFile();
            WatchStreaming();   // OBS already open: usernames hidden before the list shows them
            LoadAccounts();
            ShowLastServer();
            InitLaunchOptions();
            WatchSteamLog();
            await SyncNow(force: true);
            _ = RefreshPhone();
            _ = RefreshPublicIps();
            _tick.Start();
            // A network change (cable, Wi-Fi jumping to another network) syncs right away, without waiting for the 5 s.
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.BeginInvoke(async () =>
            {
                await SyncNow();
                _ = RefreshPublicIps();
            });
            _queryTimer.Start();
            if (_servers.Count > 0) _ = QueryServers(true);
            AfterUpdateStart();
            _ = CheckForUpdates();
            _ = ShowAutostart();
            ShowData();
        }
        _secondTimer.Start();
        AfterStartupDev();
    }

    async Task OnTick()
    {
        WatchSteamLog();
        WatchSteamAccounts();
        await SyncNow();
        DiagTick();
        WatchCs2Console();
        CountData();
        WatchStreaming();
        _ = WatchQuality();
        if (!_checkingUpdate && _s.CheckUpdates && (_s.LastUpdateCheck == null || Fmt.Stale(_s.LastUpdateCheck.Value, TimeSpan.FromHours(24)))) _ = CheckForUpdates();
        if (!_changingIp && DateTime.Now.Second % 10 < 5) _ = RefreshPhone();
        if (!_changingIp && _ipRunning == 0 && (DateTime.Now - _lastIpCheck).TotalSeconds > (Steam.Cs2Running ? 15 : 90)) _ = RefreshPublicIps();
    }

    void OnSecond()
    {
        foreach (var a in _accounts.Where(a => a.Meta.CooldownUntil != null))
        {
            var was = a.InCooldown;
            a.TickCooldown();
            if (a.Meta.CooldownUntil <= AppClock.Now)
            {
                a.Meta.CooldownUntil = null;
                a.RefreshAll();
                SaveAccounts();
                Toast($"The cooldown for {a.Name} has ended.", true);
                UpdateQuickInfo();   // it is back in the rotation: New IP + next account may name it now
            }
        }
    }
}
