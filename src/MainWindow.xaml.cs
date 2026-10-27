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

/// <summary>Janela principal. Cada área vive no seu ficheiro MainWindow.*.cs.</summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------ Estado
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
    bool _distrust, _cs2WasRunning, _wasProtected, _everProtected, _homeCached, _switching, _reveal, _syncing, _syncAgain, _changingIp, _querying, _refreshingAcc, _uiLock, _closingDone;
    string _adb;
    PhoneInfo _phone;
    DateTime _lastIpCheck = DateTime.MinValue;

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _queryTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _secondTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };

    bool _demo = false;   // só a build de desenvolvimento o liga (--demo)

    CheckItem _cFirewall, _cKill, _cEgress, _cRoutes, _cDiff, _cPhoneData, _cCs2, _cSteam, _cPc;
    string _steamPath;

    static readonly Regex ServerRegex = new(@"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})(?!\d|\.\d)(?:[ \t]*[:,; \t][ \t]*(\d+)(?!\d|\.\d))?", RegexOptions.Compiled);

    public MainWindow(string[] args)
    {
        InitializeComponent();
        ParseDevArgs(args);
        ShowVersion();

        Log.Added += e => Dispatcher.BeginInvoke(() =>
        {
            _log.Insert(0, e);
            while (_log.Count > 600) _log.RemoveAt(_log.Count - 1);
        });

        _s = _demo ? new Settings() : Store.Load(Paths.File("settings.json"), () => new Settings());
        InitDev();
        LoadServers();
        _serverView = CollectionViewSourceFor(_servers);
        ServerList.ItemsSource = _serverView;
        LogList.ItemsSource = _log;
        History.ItemsSource = _history;
        AccountsPanel.ItemsSource = _accShown;
        Checks.ItemsSource = _checks;
        foreach (var h in _s.IpHistory.OrderByDescending(h => h.Time).Take(30)) _history.Add(h);

        _checks.Add(_cFirewall = new CheckItem { Title = "Firewall", Help = "Tem de estar ligado: é ele que aplica o kill switch." });
        _checks.Add(_cKill = new CheckItem { Title = "Bloqueio (kill switch)", Help = "Regra que bloqueia os servidores da lista em todas as placas menos no telemóvel." });
        _checks.Add(_cEgress = new CheckItem { Title = "Ligação ao telemóvel", Help = "O telemóvel por cabo USB, por onde os servidores são contactados." });
        _checks.Add(_cRoutes = new CheckItem { Title = "Rotas", Help = "Confirmado pelo próprio Windows: cada IP da lista vai pelo telemóvel." });
        _checks.Add(_cDiff = new CheckItem { Title = "IP diferente de casa", Help = "Se o telemóvel estiver a partilhar o Wi-Fi de casa em vez dos dados móveis, o IP seria o mesmo." });
        _checks.Add(_cPhoneData = new CheckItem { Title = "Dados móveis", Help = "Com o Wi-Fi do telemóvel ligado, a partilha pode levar a rede de casa em vez dos dados móveis. Lido por ADB; sem ADB, confirma à mão." });
        _checks.Add(_cCs2 = new CheckItem { Title = "CS2 só pelo telemóvel", Help = "Regra na Firewall: o cs2.exe só sai pelo telemóvel. Servidores fora da lista (browser do jogo, convites) também nunca veem o IP de casa." });
        _checks.Add(_cSteam = new CheckItem { Title = "Steam pelo telemóvel", Help = "O steam.exe fala com a Valve pelo telemóvel, como o CS2. Se o Steam estiver noutra rede ou se religar a meio do jogo, o servidor dá kick com \"No Steam logon\"." });
        _checks.Add(_cPc = new CheckItem { Title = "Resto do PC", Help = "Por onde sai o tráfego normal do PC (Steam, browser, Discord)." });

        ApplySettingsToUi();
        UpdateHistoryEmpty();

        _tick.Tick += async (_, _) => await OnTick();
        _queryTimer.Tick += async (_, _) =>
        {
            foreach (var srv in _servers.Where(x => x.IsLast)) srv.Raise(nameof(ServerItem.LastText));
            if (PageLog.IsVisible) UpdateDiagInfo();
            if (AutoQueryBox.IsChecked == true && PageServers.IsVisible) await QueryServers(true);
        };
        _secondTimer.Tick += (_, _) => OnSecond();
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
        Log.Info($"ServerRouteTool {AppInfo.Version} aberto{(_demo ? " (demonstração)" : "")}.");
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
            // Uma mudança de rede (cabo, Wi-Fi a saltar para outra rede) sincroniza logo, sem esperar pelos 5 s.
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.BeginInvoke(async () =>
            {
                await SyncNow();
                _ = RefreshPublicIps();
            });
            _queryTimer.Start();
            if (_servers.Count > 0) _ = QueryServers(true);
        }
        _secondTimer.Start();
        AfterStartupDev();
    }

    async Task OnTick()
    {
        WatchSteamLog();
        await SyncNow();
        DiagTick();
        if (!_changingIp && DateTime.Now.Second % 10 < 5) _ = RefreshPhone();
        if (!_changingIp && (DateTime.Now - _lastIpCheck).TotalSeconds > 90) _ = RefreshPublicIps();
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
                Toast($"O cooldown de {a.Name} terminou.", true);
            }
        }
    }
}
