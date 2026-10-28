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
    Dictionary<string, AccountMeta> _accMeta = new();
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
    bool _distrust, _restored, _phoneBusy, _tetherHinted, _cs2WasRunning, _wasProtected, _everProtected, _homeCached, _switching, _reveal, _syncing, _syncAgain, _changingIp, _querying, _refreshingAcc, _uiLock, _closingDone;
    string _adb;
    PhoneInfo _phone;
    DateTime _lastIpCheck = DateTime.MinValue;

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _queryTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _secondTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };

    bool _demo = false;   // only the development build turns it on (--demo)

    CheckItem _cFirewall, _cKill, _cEgress, _cRoutes, _cDiff, _cPhoneData, _cCs2, _cSteam, _cPc;
    string _steamPath;

    static readonly Regex ServerRegex = new(@"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})(?!\d|\.\d)(?:[ \t]*[:,; \t][ \t]*(\d+)(?!\d|\.\d))?", RegexOptions.Compiled);

    public MainWindow(string[] args)
    {
        InitializeComponent();
        ParseDevArgs(args);
        _justUpdated = args.Contains("--updated");
        ShowVersion();

        Log.Added += e => Dispatcher.BeginInvoke(() =>
        {
            _log.Insert(0, e);
            while (_log.Count > 600) _log.RemoveAt(_log.Count - 1);
        });

        _s = _demo ? new Settings() : Store.Load(Paths.File("settings.json"), () => new Settings());
        InitDev();
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

        _checks.Add(_cFirewall = new CheckItem { Title = "Firewall", Help = "Must be on: it enforces the kill switch." });
        _checks.Add(_cKill = new CheckItem { Title = "Kill switch", Help = "Rule that blocks the listed servers on every adapter except the phone." });
        _checks.Add(_cEgress = new CheckItem { Title = "Phone connection", Help = "The phone over a USB cable, which is how the servers are reached." });
        _checks.Add(_cRoutes = new CheckItem { Title = "Routes", Help = "Confirmed by Windows itself: every IP in the list goes through the phone." });
        _checks.Add(_cDiff = new CheckItem { Title = "IP differs from home", Help = "If the phone is sharing your home Wi-Fi instead of mobile data, the IP would be the same." });
        _checks.Add(_cPhoneData = new CheckItem { Title = "Mobile data", Help = "With the phone's Wi-Fi on, tethering may carry your home network instead of mobile data. Read through ADB; without ADB, check it yourself." });
        _checks.Add(_cCs2 = new CheckItem { Title = "CS2 only through the phone", Help = "Firewall rule: cs2.exe only goes out through the phone. Servers not in the list (game browser, invites) never see your home IP either." });
        _checks.Add(_cSteam = new CheckItem { Title = "Steam through the phone", Help = "steam.exe talks to Valve through the phone, like CS2. If Steam is on another network or reconnects mid-game, the server kicks you with \"No Steam logon\"." });
        _checks.Add(_cPc = new CheckItem { Title = "Rest of the PC", Help = "Where the normal PC traffic goes out (Steam, browser, Discord)." });

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
        ContentRendered += async (_, _) => await Startup();
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
        if (_demo) LoadDemo();
        else
        {
            StartupDev();
            ImportStartupFile();
            LoadAccounts();
            ShowLastServer();
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
        }
        _secondTimer.Start();
        AfterStartupDev();
    }

    async Task OnTick()
    {
        WatchSteamLog();
        await SyncNow();
        DiagTick();
        WatchCs2Console();
        if (!_checkingUpdate && _s.CheckUpdates && (_s.LastUpdateCheck == null || (DateTime.Now - _s.LastUpdateCheck.Value).TotalHours >= 24)) _ = CheckForUpdates();
        if (!_changingIp && DateTime.Now.Second % 10 < 5) _ = RefreshPhone();
        if (!_changingIp && (DateTime.Now - _lastIpCheck).TotalSeconds > (Steam.Cs2Running ? 15 : 90)) _ = RefreshPublicIps();
    }

    void OnSecond()
    {
        foreach (var a in _accounts.Where(a => a.Meta.CooldownUntil != null))
        {
            var was = a.InCooldown;
            a.TickCooldown();
            if (a.Meta.CooldownUntil <= DateTime.Now)
            {
                a.Meta.CooldownUntil = null;
                a.RefreshAll();
                SaveAccounts();
                Toast($"The cooldown for {a.Name} has ended.", true);

            }
        }
    }
}
