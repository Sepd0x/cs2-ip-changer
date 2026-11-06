using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SRT.Core;

/// <summary>
/// Usage stats (pseudonymous: a kept random install ID), required to use the app: accepted once at the first start, before the app opens (App.StatsGate;
/// README > Telemetry). Until then nothing is sent and no ID is made. 20 s after the start, every 30 minutes while the app is open
/// and something is new, when the PC goes to sleep, when Windows signs out or shuts down, and at close (Exit) it sends:
/// a random ID for the install (since beta.9 kept, before new every day; never made from the PC or the user),
/// the app and Windows versions, the mode, how the phone is connected, minutes open, how many times each button and hotkey
/// was used (by the name of its code, never its text), a few counters, list sizes in rough bands, and errors as type + place in the code.
/// Never: IPs, servers, accounts, Steam names or IDs, the carrier, anything you typed.
/// Off in the recording build and in the demo. The server (worker/stats) keeps no IP and has its logs off.
/// </summary>
public static class Telemetry
{
    const string DefaultUrl = "https://stats.cs2ipchanger.workers.dev/v1";   // the project's own Cloudflare account
#if PRIVADO
    const bool Dev = true;   // the development build is marked, so it stays out of the numbers
    static readonly string Url = Environment.GetEnvironmentVariable("SRT_TELEMETRY_URL") ?? DefaultUrl;   // dev only: a local Worker for tests
#else
    const bool Dev = false;
    const string Url = DefaultUrl;
#endif

    sealed class State
    {
        /// <summary>The install's random ID (InstallId). Install is true once it is a beta.9 one; Day is what beta.8 kept and is cleared.</summary>
        public string Id { get; set; }
        public bool Install { get; set; }
        public string Day { get; set; }
        /// <summary>Set by hand in SRT_Dados/telemetry.json on the developer's own PCs, so their reports count as dev and stay out of the numbers.</summary>
        public bool Mine { get; set; }
        public DateTime LastSent { get; set; }
        public double Minutes { get; set; }
        public Dictionary<string, int> Clicks { get; set; } = new();
        public Dictionary<string, int> Counts { get; set; } = new();
        public Dictionary<string, ErrorItem> Errors { get; set; } = new();
        /// <summary>beta.9. Clicks on the screens: "screen|kind|col,row" (kind c, d dead, r rage) to how many. Goes out without the ID's day link: the server adds them up.</summary>
        public Dictionary<string, int> Heat { get; set; } = new();
        /// <summary>beta.9. Minutes with each screen open and the window on screen (not in the tray).</summary>
        public Dictionary<string, double> Screens { get; set; } = new();
        /// <summary>beta.9. Moves between screens, "from>to".</summary>
        public Dictionary<string, int> Paths { get; set; } = new();
        /// <summary>beta.9. The day (UTC) the stats were first on here. Only its band goes out (AgeBand), never the date.</summary>
        public string FirstDay { get; set; }
        /// <summary>beta.9. The UTC days the app was open, only the last 7: just how many goes out (ActiveDays).</summary>
        public List<string> Active { get; set; } = new();
        /// <summary>beta.9. The UTC day the environment report went out (once a day, SendEnv).</summary>
        public string EnvDay { get; set; }
        /// <summary>beta.9. Mobile data through the phone on DataDay (UTC); only its band goes out.</summary>
        public string DataDay { get; set; }
        public long DataBytes { get; set; }
        /// <summary>beta.9. The version that last ran here: after an update, the one it came from is counted once (upd_from).</summary>
        public string LastApp { get; set; }
        /// <summary>Nov. 2026. The best phone link seen since the last report (usb, then hotspot, other, none), so a report made before the phone was up doesn't say "none".</summary>
        public string BestLink { get; set; }
        /// <summary>Nov. 2026. The UTC day and the content of the list sizes, regions and settings last sent: they only go again on a new day or when they change.</summary>
        public string ExtrasDay { get; set; }
        public string ExtrasSig { get; set; }
    }
    public sealed class ErrorItem { public string Type { get; set; } public string At { get; set; } public int N { get; set; } }

    static readonly object Gate = new();
    static readonly Regex Name = new(@"^[A-Za-z0-9_]{1,48}$", RegexOptions.Compiled);
    static State _st;
    static DateTime _since;
    static bool _on;
    static Func<(string Mode, string Link, Dictionary<string, int> Sizes)> _now;

    static string File_ => _file ?? Paths.File("telemetry.json");

    static string _file;
    static bool _hooked;
    static DispatcherTimer _tick, _first;
    static int _ticks;

    // Not "anonymous": the report carries a kept install ID, so under the GDPR it is pseudonymous. Only the heat map and the
    // environment report (no ID) are anonymous.
    public const string NoticeTitle = "Usage stats";

    /// <summary>
    /// The notice at the first start (Accept or Quit), since beta.11 (NoticeVersion 4): short, with every kind of stat named, and the
    /// full list (<see cref="NoticeList"/>) under See the full list. The "Kept by" line says who, why, the legal basis, where, how long
    /// and how to ask or complain (GDPR art. 13), like README > Telemetry and docs/PRIVACY-AND-SECURITY.md.
    /// </summary>
    public const string NoticeShort =
        "This beta sends usage stats when it opens and closes and every 30 minutes while it's open, so bugs get found and fixed. They are required while it is in beta.\n\n"
        + "Sent: a random ID made for this install, app and Windows versions, country, which screens, buttons and settings you use, ping and lag in rough bands, errors, and your phone's brand and Android version.\n"
        + "Never sent: IPs, servers, accounts, Steam names or IDs, your carrier, anything you type.\n"
        + "Kept by the author (Sepd0x on GitHub) on Cloudflare, only to fix bugs and improve the app, because you accept this notice, for 12 months. "
        + "Delete them any time: Settings > Delete my stats. Questions: GitHub issues. You can also complain to your data protection authority.\n\n"
        + "Quit closes the app. Nothing on your PC changes.";

    /// <summary>The full list (the notice until beta.10), shown under See the full list. README > Telemetry has the same list.</summary>
    public const string NoticeText =
        "While CS2 IP Changer is in beta, using it means it sends usage stats when it opens and closes (and every 30 minutes while it's open), so bugs get found and fixed:\n\n"
        + NoticeList + "\n\n"
        + "Never IPs, servers, accounts, Steam names or IDs, your carrier or anything you type.\n\n"
        + "Quit closes the app without changing anything on your PC.";

    /// <summary>The 7 items of the full list, unchanged since beta.10.</summary>
    public const string NoticeList =
        "•  a random ID made for this install, with no name, never from your PC or from you (Settings > Delete my stats deletes everything kept under it), and how long ago the app was set up, only in rough bands (this week, 2 to 4 weeks…)\n"
        + "•  the app and Windows versions, and your country as two letters\n"
        + "•  the mode, how the phone is connected, minutes the app was open and on each screen, and every few minutes while it's open what is on (screen, CS2 open, protection on)\n"
        + "•  how often each button and hotkey is used, where on each screen you click (a coarse grid, added up with everyone's, anonymous), a few counters (IP changes, kicks seen, leak tests passed or failed, which Checks rows are red) and how long your lists are, in rough bands\n"
        + "•  in rough bands: each CS2 match's ping, jitter and lag (Valve or community, never the server), how long sessions last and the start takes, how far you scroll, and which on/off settings you changed\n"
        + "•  errors and crashes, as their type and place in the app's code\n"
        + "•  in a separate anonymous daily report with no ID or country: Windows language and build, window size and scale, the phone's brand (never the model), Android version and 4G or 5G";

    /// <summary>
    /// The notice's version (Settings.StatsNotice). 2 since beta.9: the ID is kept for the install. 3 since beta.10: the stats also go
    /// out every 30 minutes while the app is open. 4 since beta.11: the short notice, which says who keeps the stats, for how long and
    /// how to ask. An older accept is asked again once.
    /// </summary>
    public const int NoticeVersion = 4;

    /// <summary>Added to the notice for someone who accepted an older one, from any version before (<see cref="NoticeChangedFor"/>).</summary>
    public static string NoticeChanged => NoticeChangedFor(1);

    /// <summary>
    /// What changed since the notice <paramref name="had"/> accepted (Settings.StatsNotice, 1 = before beta.9), only what is new to
    /// them: beta.8 said the ID was new every day, beta.9 said open and close only, beta.10 had the long notice. Empty for 0 (never accepted).
    /// </summary>
    public static string NoticeChangedFor(int had)
    {
        if (had <= 0 || had >= NoticeVersion) return "";
        var s = "Changed since you last accepted: this notice is shorter, with every item still under See the full list, and it now says "
            + "who keeps the stats, for how long and how to ask about them or complain.";
        if (had < 3) s += " The stats now also go out every 30 minutes while the app is open (and before the PC sleeps), not only when it opens and closes.";
        if (had < 2) s += " The ID is now kept for this install instead of new every day, so the stats can tell people who come back from new installs. "
            + "It is still random and has no name.";
        return s;
    }

    /// <summary>Added to the notice when it shows right after an update (--updated): Quit undoes what the version before left on.</summary>
    public const string NoticeAfterUpdate =
        "You just updated: the version before left its protection on. Quit also turns it off, like Turn off protection, so your servers "
        + "aren't left blocked outside the phone without the app (not while CS2 is open).";

    /// <summary>Stats are on (the notice was accepted, and this isn't the demo or the recording build).</summary>
    public static bool Running => _on;

    /// <summary>
    /// At start. <paramref name="now"/> gives the mode, the phone link and list sizes at send time. <paramref name="accepted"/> is
    /// Settings.StatsAccepted: until it's true nothing is sent and no ID is made.
    /// </summary>
    public static void Start(bool demo, Func<(string, string, Dictionary<string, int>)> now, bool accepted, string file = null)
    {
        if (demo || Rec.On) return;
        _now = now;
        _file = file;
        if (accepted) TurnOn();
    }

    static readonly Regex IdShape = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    /// <summary>
    /// The install's ID (beta.9): random, made once and kept, so the stats know a return from a new install (retention, people now).
    /// Never made from the PC, Windows, Steam or the user, so it says nothing about who you are. An ID from before beta.9 (beta.8's
    /// daily ones, beta.7's) is not carried on: <paramref name="install"/> false gets a new one. Delete my stats (Forget) makes another.
    /// </summary>
    public static string InstallId(string id, bool install) => install && IdShape.IsMatch(id ?? "") ? id : Guid.NewGuid().ToString("N");

    /// <summary>Gives <paramref name="st"/> its install ID. True when it changed (none yet, or one from before beta.9).</summary>
    static bool NewId(State st)
    {
        var id = InstallId(st.Id, st.Install);
        if (id == st.Id) return false;
        st.Id = id;
        st.Install = true;
        st.Day = null;
        return true;
    }

    /// <summary>telemetry.json gets the install ID: one from before beta.9 is saved over at once, so it doesn't stay on the disk (nor in the .prev).</summary>
    public static void EnsureId(string file)
    {
        var st = Store.Load(file, () => new State());
        if (NewId(st)) Write(file, st, true);
    }

    static void TurnOn()
    {
        if (_on) return;
        _st = Store.Load(File_, () => new State());
        _st.Clicks ??= new(); _st.Counts ??= new(); _st.Errors ??= new();
        _st.Heat ??= new(); _st.Screens ??= new(); _st.Paths ??= new();
        _st.FirstDay ??= DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        _since = DateTime.UtcNow;
        _on = true;
        MarkActive(_st, DateTime.UtcNow);
        var appNow = Updater.Clean(AppInfo.Version.Split('+')[0]);
        if (_st.LastApp != null && _st.LastApp != appNow && VersionName(_st.LastApp) is string from && ("updfrom_" + from).Length <= 48)
            _st.Counts["updfrom_" + from] = _st.Counts.GetValueOrDefault("updfrom_" + from) + 1;
        _st.LastApp = appNow;
        if (NewId(_st)) Save(newDay: true);
        if (!_hooked)
        {
            _hooked = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler(OnClick));
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(MouseDown), true);
            EventManager.RegisterClassHandler(typeof(Window), ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScroll), true);
            // The live signal: every 5 minutes while the app is open, and once half a minute after the start
            _live = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _live.Tick += (_, _) => { if (_live.Interval != TimeSpan.FromMinutes(5)) _live.Interval = TimeSpan.FromMinutes(5); _ = SendLive(); };
            Log.Added += e => { if (e.Level == LogLevel.Error) Count("log_error"); };
            if (Application.Current != null) Application.Current.Exit += (_, _) => SendAtExit();
            // Nov. 2026 (audit D1, D5): before, a report went out only at the start, on a new UTC day and at close (and at close only
            // 10 minutes after the last), so an app left in the tray for hours was counted with 1 minute. Now: every 30 minutes when
            // something is new (Periodic), before the PC sleeps and when Windows signs out or shuts down. The dev build does the same.
            _tick = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _tick.Tick += (_, _) => OnTick();
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
                {
                    if (e.Mode == Microsoft.Win32.PowerModes.Suspend) SendBeforeSleep();
                    else if (e.Mode == Microsoft.Win32.PowerModes.Resume) Resumed();
                };
                Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => SendAtExit();
            }
            catch (Exception ex) { Log.Warn("Usage stats: no sleep and sign-out events (" + ex.GetType().Name + ")."); }
            // The first report waits until the window has loaded its lists (and never slows the start).
            _first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _first.Tick += (_, _) => { _first.Stop(); _ = Send(); _ = SendEnv(late: false); };
        }
        _tick.Start();
        _live.Interval = TimeSpan.FromSeconds(30);
        _live.Start();
        _first.Start();
    }

    // ------------------------------------------------------------------ beta.9: screens, heat map, live signal

    /// <summary>Width of the side bar (MainWindow.xaml): the heat map grid covers the rest of the window, as in the dashboard's ui-map.js.</summary>
    public const double SideBar = 80;
    public const int Cols = 24, Rows = 16;
    static readonly Regex ScreenId = new("^[a-z]{1,12}$", RegexOptions.Compiled);
    static string _screen;
    static DateTime _screenSince;
    static readonly List<(DateTime T, string Cell)> _recent = new();
    static Func<(string Screen, bool Cs2, bool Prot, bool Tray)> _liveNow;
    static DispatcherTimer _live;

    /// <summary>The grid cell ("col,row") of a click at x, y in a window of w by h, or null on the side bar or outside.</summary>
    public static string Cell(double x, double y, double w, double h)
    {
        if (w <= SideBar || h <= 0 || x < SideBar || x >= w || y < 0 || y >= h) return null;
        var c = Math.Clamp((int)((x - SideBar) / (w - SideBar) * Cols), 0, Cols - 1);
        var r = Math.Clamp((int)(y / h * Rows), 0, Rows - 1);
        return $"{c},{r}";
    }

    /// <summary>A rage click: this one and 2 more in the same cell within the last second.</summary>
    public static bool IsRage(IEnumerable<(DateTime T, string Cell)> recent, DateTime now, string cell) =>
        recent.Count(x => x.Cell == cell && now - x.T <= TimeSpan.FromSeconds(1)) >= 2;

    /// <summary>The install's age in days, in bands: 0, 1, 2-3, 4-6, 7-13, 14-29, 30+. Never the day itself.</summary>
    public static string AgeBand(int days) =>
        days <= 0 ? "0" : days == 1 ? "1" : days <= 3 ? "2-3" : days <= 6 ? "4-6" : days <= 13 ? "7-13" : days <= 29 ? "14-29" : "30+";

    /// <summary>The screen now open (Nav_Checked), or null when the window is in the tray. Counts its minutes and the move.</summary>
    public static void Screen(string id)
    {
        if (!_on || (id != null && !ScreenId.IsMatch(id))) return;
        string was;
        lock (Gate) was = _visible ? _screen : null;
        if (was != id) ScrollDone(was);
        Save();
        lock (Gate)
        {
            if (_st == null) return;
            if (_screen != null && id != null && _screen != id)
            {
                var k = _screen + ">" + id; _st.Paths[k] = _st.Paths.GetValueOrDefault(k) + 1;
                // the first screen picked after the start (the one it opens on isn't a choice)
                if (!_firstMove) { _firstMove = true; _st.Counts["fs_" + id] = _st.Counts.GetValueOrDefault("fs_" + id) + 1; }
            }
            if (id != null || _screen != null) _screen = id ?? _screen;
            _visible = id != null;
            _screenSince = DateTime.UtcNow;
        }
    }
    static bool _visible;

    /// <summary>What the live signal says besides the mode and the link (MainWindow.TelemetryLive).</summary>
    public static void LiveFrom(Func<(string, bool, bool, bool)> now) => _liveNow = now;

    static DateTime _activatedAt;
    /// <summary>The main window came to the front (MainWindow.Activated): the click that does that isn't counted.</summary>
    public static void Activated() => _activatedAt = DateTime.UtcNow;

    /// <summary>
    /// A click that counts on the heat map: the window has the focus and wasn't brought to the front by this click. 3 nov.: false
    /// rage clicks came from clicks back into the app from a game or another window (Alt+Tab, the app behind).
    /// </summary>
    public static bool Counts(bool active, DateTime activatedAt, DateTime now) => active && now - activatedAt >= TimeSpan.FromMilliseconds(500);

    /// <summary>A click anywhere in the main window: its cell on the screen open, and whether it was dead or rage.</summary>
    public static void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_on || sender is not Window w || !ReferenceEquals(w, Application.Current?.MainWindow) || w.Content is not FrameworkElement root) return;
        if (!Counts(w.IsActive, _activatedAt, DateTime.UtcNow)) return;
        try
        {
            var p = e.GetPosition(root);
            var cell = Cell(p.X, p.Y, root.ActualWidth, root.ActualHeight);
            string screen;
            lock (Gate) screen = _screen;
            if (cell == null || screen == null) return;
            var now = DateTime.UtcNow;
            bool rage;
            lock (Gate)
            {
                _recent.RemoveAll(x => now - x.T > TimeSpan.FromSeconds(1));
                rage = IsRage(_recent, now, cell) && !_recent.Any(x => x.Cell == "!" + cell);
                _recent.Add((now, cell));
                if (rage) _recent.Add((now, "!" + cell));   // one rage per burst
            }
            AddHeat(screen, "c", cell);
            if (!Interactive(e.OriginalSource as DependencyObject, w)) AddHeat(screen, "d", cell);
            if (rage) AddHeat(screen, "r", cell);
        }
        catch { }
    }

    static void AddHeat(string screen, string kind, string cell)
    {
        lock (Gate)
        {
            if (_st == null) return;
            var k = $"{screen}|{kind}|{cell}";
            if (!_st.Heat.ContainsKey(k) && _st.Heat.Count >= 600) return;
            _st.Heat[k] = _st.Heat.GetValueOrDefault(k) + 1;
        }
    }

    /// <summary>Something under the click that does something (a button, a box to type in, a list row, a scroll bar). A disabled button doesn't count: that click is dead.</summary>
    static bool Interactive(DependencyObject d, Window stop)
    {
        for (var i = 0; d != null && i < 60 && !ReferenceEquals(d, stop); i++)
        {
            if (d is UIElement { IsEnabled: false }) return false;
            if (d is ButtonBase or MenuItem or TextBoxBase or PasswordBox or ComboBox or ComboBoxItem or ListBoxItem or DataGridRow or DataGridCell
                or ScrollBar or Thumb or Slider or Hyperlink or Expander or TabItem) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    static bool _cs2Was;

    static async Task SendLive()
    {
        if (!_on || _liveNow == null) return;
        try
        {
            (string screen, bool cs2, bool prot, bool tray) = _liveNow();
            // the daily report's minutes with CS2 open and with protection on, and CS2 starts, from this 5-minute signal
            if (cs2 && !_cs2Was) Count("cs2_start");
            _cs2Was = cs2;
            if (cs2) Count("min_cs2", 5);
            if (prot) Count("min_prot", 5);
            (string mode, string link, _) = _now != null ? _now() : (null, null, null);
            SeenLink(link);
            string id;
            lock (Gate) { if (_st == null) return; id = _st.Id; }
            var body = JsonSerializer.Serialize(new
            {
                v = 1, id, app = Updater.Clean(AppInfo.Version.Split('+')[0]), mode, link,
                screen = screen != null && ScreenId.IsMatch(screen) ? screen : null, cs2, prot, tray, dev = Dev || _st.Mine,
            });
            await Post(body, TimeSpan.FromSeconds(8), Url + "/live");
        }
        catch (Exception ex) { NotSent(Url + "/live", Kind(ex)); }
    }

    // ------------------------------------------------------------------ beta.9 part 2: matches, sessions, scroll, environment

    /// <summary>Bands for one CS2 match (NetReport), so no number can tie reports together. Same names as worker/stats BANDS.</summary>
    public static string PingBand(int v) => v < 30 ? "lt30" : v < 50 ? "30_50" : v < 80 ? "50_80" : v < 120 ? "80_120" : "gt120";
    public static string JitterBand(int v) => v < 5 ? "lt5" : v < 10 ? "5_10" : v < 20 ? "10_20" : v < 40 ? "20_40" : "gt40";
    public static string LateBand(double v) => v <= 0 ? "0" : v < 1 ? "lt1" : v < 3 ? "1_3" : v < 10 ? "3_10" : "gt10";
    public static string LossBand(int packets) => packets <= 0 ? "0" : packets <= 10 ? "1_10" : packets <= 100 ? "11_100" : "gt100";
    public static string SessionBand(TimeSpan t) => t.TotalMinutes < 5 ? "lt5" : t.TotalMinutes < 15 ? "5_15" : t.TotalMinutes < 30 ? "15_30" : t.TotalMinutes < 60 ? "30_60" : t.TotalMinutes < 120 ? "1_2h" : "gt2h";
    public static string StartupBand(TimeSpan t) => t.TotalSeconds < 1 ? "lt1" : t.TotalSeconds < 2 ? "1_2" : t.TotalSeconds < 4 ? "2_4" : t.TotalSeconds < 8 ? "4_8" : "gt8";
    public static string ProtectedBand(TimeSpan t) => t.TotalSeconds < 10 ? "lt10" : t.TotalSeconds < 30 ? "10_30" : t.TotalSeconds < 60 ? "30_60" : t.TotalSeconds < 180 ? "1_3m" : "gt3m";
    /// <summary>How far down a screen was scrolled (0 top, 1 bottom), in bands of 25%.</summary>
    public static string ScrollBand(double f) => f >= 0.99 ? "100" : f >= 0.75 ? "75" : f >= 0.5 ? "50" : f >= 0.25 ? "25" : "0";

    /// <summary>The counters for one match: Valve or community, lagged or not, and its numbers in bands. Never the server or the time.</summary>
    public static List<string> MatchCounts(NetReport r, bool valve)
    {
        var k = valve ? "valve" : "comm";
        var list = new List<string> { "m_" + k };
        if (r.Laggy) list.Add("m_" + k + "_lag");
        if (r.PingP50 is int p) list.Add("ping_" + PingBand(p));
        if (r.JitterUp is int ju) list.Add("jit_" + JitterBand(ju));
        if (r.JitterDown is int jd) list.Add("jitd_" + JitterBand(jd));
        if (r.UpLate is double l) list.Add("late_" + LateBand(l));
        list.Add("loss_" + LossBand(r.Dropped));
        return list;
    }

    // IP changes, phone drops, mobile data, Rejoin, the first screen, blocked regions and the version updated from
    public static string ChangeBand(TimeSpan t) => t.TotalSeconds < 5 ? "lt5" : t.TotalSeconds < 10 ? "5_10" : t.TotalSeconds < 20 ? "10_20" : t.TotalSeconds < 40 ? "20_40" : "gt40";
    public static string AttemptsBand(int n) => n <= 1 ? "1" : n == 2 ? "2" : n == 3 ? "3" : "4p";
    public static string DataBand(long bytes) => bytes < 250_000_000 ? "lt250m" : bytes < 500_000_000 ? "250_500m" : bytes < 1_000_000_000 ? "500m_1g" : bytes < 2_000_000_000 ? "1_2g" : "gt2g";
    public static string DropsBand(int n) => n <= 0 ? "0" : n == 1 ? "1" : n == 2 ? "2" : "3p";

    /// <summary>The counters for one Change IP that ended with a new IP: the method, how long, attempts, the quality measured, jitter retries.</summary>
    public static List<string> ChangeCounts(bool airplane, TimeSpan took, int attempts, QualityLevel? quality, int jitterRetries)
    {
        var list = new List<string> { airplane ? "chg_air" : "chg_data", "chgt_" + ChangeBand(took), "chga_" + AttemptsBand(attempts) };
        if (quality is QualityLevel.Good or QualityLevel.Fair or QualityLevel.Bad) list.Add("chgq_" + quality.ToString().ToLowerInvariant());
        if (jitterRetries > 0) list.Add("chg_retry");
        return list;
    }

    public static void IpChange(bool airplane, TimeSpan took, int attempts, QualityLevel? quality, int jitterRetries)
    {
        foreach (var n in ChangeCounts(airplane, took, attempts, quality, jitterRetries)) Count(n);
    }

    static int _drops;
    /// <summary>The phone's connection went away while it was up (not during Change IP). Counted per session, in bands, at close.</summary>
    public static void PhoneDrop() { lock (Gate) _drops++; }

    /// <summary>Mobile data through the phone (Tray's data meter): added up per UTC day; only the day's band goes out.</summary>
    public static void Data(long added)
    {
        if (!_on || added <= 0) return;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        lock (Gate)
        {
            if (_st == null) return;
            if (_st.DataDay != today) { _st.DataDay = today; _st.DataBytes = 0; }
            _st.DataBytes += added;
        }
    }

    /// <summary>Rejoin: tried, back on the server, or the server was full and the app waits for a slot.</summary>
    public static void Rejoin(string what) { if (what is "try" or "back" or "wait") Count("rej_" + what); }

    /// <summary>
    /// The rows of the Checks panel (their titles in MainWindow) to the short names sent (chk_, a closed family in worker/stats).
    /// Each report says which rows are red when it is built, never their detail text.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CheckKeys = new Dictionary<string, string>
    {
        ["Phone connection"] = "egress", ["Servers through the phone"] = "routes", ["CS2 only through the phone"] = "cs2",
        ["Block if the phone drops"] = "kill", ["Windows Firewall"] = "firewall", ["Phone IP is not your home IP"] = "diff",
        ["Mobile data"] = "data", ["Connection quality"] = "quality", ["Steam through the phone"] = "steam", ["Other apps"] = "pc",
        ["Matchmaking regions"] = "regions",
    };
    static Func<IEnumerable<string>> _redChecks;
    public static void ChecksFrom(Func<IEnumerable<string>> redTitles) => _redChecks = redTitles;

    /// <summary>The chk_ counters for the red rows: one each, only the known ones.</summary>
    public static List<string> RedChecks(IEnumerable<string> titles) =>
        titles?.Select(t => t != null && CheckKeys.TryGetValue(t, out var k) ? "chk_" + k : null).Where(k => k != null).Distinct().ToList() ?? new();

    /// <summary>Run leak test: passed or failed (leak_ok, leak_bad). No phone mode has nothing to test and isn't counted.</summary>
    public static void LeakTest(bool ok) => Count(ok ? "leak_ok" : "leak_bad");

    static bool _firstMove;
    static Func<IEnumerable<string>> _regions;
    /// <summary>The turned-off regions as Valve's public codes (RegionCodes), what the server keeps (reg_fra).</summary>
    public static void RegionsFrom(Func<IEnumerable<string>> blockedCodes) => _regions = blockedCodes;
    static readonly Regex RegionCodeShape = new("^[a-z]{3,4}[0-9]?$", RegexOptions.Compiled);

    /// <summary>
    /// A region's Valve code for the stats: its shortest pop code ("fra" for Frankfurt's "fra" and "dfra"), or null. Nov. 2026
    /// (audit D4): the app sent the Regions page's ids ("frankfurt-germany"), which the server refuses, so no region ever arrived.
    /// </summary>
    public static string RegionCode(RegionInfo r) =>
        r?.Codes?.Where(c => c != null && RegionCodeShape.IsMatch(c)).OrderBy(c => c.Length).ThenBy(c => c, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The Valve codes of the turned-off regions (<paramref name="blockedIds"/> are RegionInfo ids), once each. Unknown ids are left out.</summary>
    public static List<string> RegionCodes(IEnumerable<RegionInfo> regions, IEnumerable<string> blockedIds)
    {
        if (regions == null || blockedIds == null) return new();
        var blocked = blockedIds.Where(x => x != null).ToHashSet();
        return regions.Where(r => r != null && blocked.Contains(r.Id)).Select(RegionCode).Where(c => c != null).Distinct().ToList();
    }

    // ------------------------------------------------------------------ Nov. 2026: how the phone was connected

    static readonly string[] LinkRank = { "none", "other", "hotspot", "usb" };
    /// <summary>The better of two phone links (usb, then hotspot, other, none); an unknown one never wins.</summary>
    public static string BestLink(string a, string b) => Array.IndexOf(LinkRank, b) > Array.IndexOf(LinkRank, a) ? b : a;

    /// <summary>A phone link seen now (the window's guard report, the live signal): the report says the best one since the last report.</summary>
    public static void SeenLink(string link)
    {
        if (!_on || Array.IndexOf(LinkRank, link) < 0) return;
        lock (Gate) if (_st != null) _st.BestLink = BestLink(_st.BestLink, link);
    }

    /// <summary>A version as a counter name ("0.1.0-beta.8" is "0_1_0_beta_8"), or null.</summary>
    public static string VersionName(string v) =>
        v != null && Regex.IsMatch(v, @"^\d+\.\d+\.\d+(-[0-9A-Za-z.]{1,20})?$") ? v.Replace('.', '_').Replace('-', '_') : null;

    /// <summary>At the end of each CS2 match (MainWindow.LogNetReport).</summary>
    public static void Match(NetReport r, bool valve) { if (r != null) foreach (var n in MatchCounts(r, valve)) Count(n); }

    static DateTime _started = DateTime.UtcNow;
    static bool _readyCounted, _protCounted;
    static DateTime ProcessStart { get { try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch { return _started; } } }

    /// <summary>The start: with Windows (--tray) or by hand, and whether the last session ended without closing (Crash.LastUnclean).</summary>
    public static void Started(bool withWindows)
    {
        _started = DateTime.UtcNow;
        Count("app_start");
        Count(withWindows ? "start_auto" : "start_hand");
        if (Crash.LastUnclean) Count("unclean_exit");
    }

    /// <summary>The window is ready: the time since the exe started, in bands (once per start).</summary>
    public static void Ready()
    {
        if (!_on || _readyCounted) return;
        _readyCounted = true;
        Count("st_" + StartupBand(DateTime.UtcNow - ProcessStart));
    }

    /// <summary>The Status box first says Protected: the time since the start, in bands (once per start).</summary>
    public static void ProtectedNow()
    {
        if (!_on || _protCounted) return;
        _protCounted = true;
        Count("tp_" + ProtectedBand(DateTime.UtcNow - ProcessStart));
    }

    /// <summary>
    /// A crash that closes the app (AppDomain.UnhandledException): counted and saved at once, so the next start sends it. Its type and
    /// place go with the errors as "Fatal_" + type, never the message.
    /// </summary>
    public static void Fatal(Exception ex)
    {
        if (!_on || ex == null) return;
        Count("crash");
        Error(ex, "Fatal_");
        Save();
    }

    // Scroll: the furthest each visit to a screen got, counted when the screen closes
    static double _scrollMax = -1;

    static void OnScroll(object sender, ScrollChangedEventArgs e)
    {
        if (!_on || !ReferenceEquals(sender, Application.Current?.MainWindow) || e.OriginalSource is not ScrollViewer sv || sv.ScrollableHeight < 20) return;
        lock (Gate) _scrollMax = Math.Max(_scrollMax, Math.Clamp(sv.VerticalOffset / sv.ScrollableHeight, 0, 1));
    }

    static void ScrollDone(string screen)
    {
        double max;
        lock (Gate) { max = _scrollMax; _scrollMax = -1; }
        if (screen != null && max >= 0) Count($"scl_{screen}_{ScrollBand(max)}");
    }

    /// <summary>
    /// The settings changed from their factory value, by name (README > Telemetry lists them): only on/off settings and the mode,
    /// never a text, a path or a list. The tests check it against worker/stats/contract.json, which the Worker reads.
    /// </summary>
    public static readonly string[] SettingNames =
    {
        "GameMode", "SteamOnEgress", "KeepPcOffPhone", "RequireGoodLink", "CheckUpdates", "SteamSilent", "SteamOffline", "StreamerMode",
        "AutoStreamer", "AutoQuery", "CleanupOnExit", "CloseToTray", "AutoJoinFollowed", "SwitchIp", "SwitchAccount", "SwitchRejoin",
        "SwitchOpenWith", "CommandSounds", "AutoModeByQuality", "NoPhone", "Mode", "ProtectOnStart", "UseWithProtectionOff",
    };

    /// <summary>The names in SettingNames whose value in <paramref name="now"/> differs from <paramref name="factory"/> (a new Settings).</summary>
    public static List<string> Changed(object now, object factory)
    {
        var list = new List<string>();
        if (now == null || factory == null) return list;
        foreach (var n in SettingNames)
        {
            var p = now.GetType().GetProperty(n);
            if (p == null || (p.PropertyType != typeof(bool) && p.PropertyType != typeof(string))) continue;
            if (!Equals(p.GetValue(now), p.GetValue(factory))) list.Add(n);
        }
        return list;
    }

    static Func<List<string>> _settings;
    public static void SettingsFrom(Func<List<string>> changed) => _settings = changed;

    /// <summary>Days the app was open in the last 7 (today too), from the days kept in telemetry.json (only the last 7, never sent).</summary>
    public static int ActiveDays(IEnumerable<string> days, DateTime utcNow)
    {
        var from = utcNow.Date.AddDays(-6);
        return days?.Select(d => DateTime.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var x) ? x.Date : DateTime.MinValue)
            .Where(x => x >= from && x <= utcNow.Date).Distinct().Count() ?? 0;
    }

    static void MarkActive(State st, DateTime utcNow)
    {
        st.Active ??= new();
        var today = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (!st.Active.Contains(today)) st.Active.Add(today);
        st.Active = st.Active.Where(d => ActiveDays(new[] { d }, utcNow) == 1).TakeLast(7).ToList();
    }

    // The environment: once a day, in its own report with no ID (worker/stats /v1/env keeps only counts per day)
    static PhoneInfo _phoneEnv;
    static Func<(double Width, double Scale)> _window;
    public static void Phone(PhoneInfo p) { if (p?.Ready == true) _phoneEnv = p; }
    public static void WindowFrom(Func<(double, double)> w) => _window = w;

    static readonly Dictionary<string, string> Brands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["samsung"] = "samsung", ["google"] = "google", ["xiaomi"] = "xiaomi", ["redmi"] = "xiaomi", ["poco"] = "xiaomi", ["oneplus"] = "oneplus",
        ["motorola"] = "motorola", ["oppo"] = "oppo", ["realme"] = "realme", ["vivo"] = "vivo", ["huawei"] = "huawei", ["honor"] = "honor",
        ["nothing"] = "nothing", ["sony"] = "sony", ["asus"] = "asus", ["nokia"] = "nokia", ["hmd"] = "nokia", ["zte"] = "zte", ["tcl"] = "tcl",
    };

    /// <summary>The phone's brand from a short list (never the model); anything else is "other".</summary>
    public static string BrandOf(string b) => string.IsNullOrWhiteSpace(b) ? null : Brands.GetValueOrDefault(b.Trim(), "other");
    /// <summary>Android's main version ("14", "8.1.0" is 8), or null.</summary>
    public static string AndroidOf(string v) => int.TryParse((v ?? "").Split('.')[0].Trim(), out var n) && n >= 4 && n <= 40 ? n.ToString() : null;
    public static string WindowBand(double w) => w <= 0 ? null : w < 1100 ? "s" : w < 1500 ? "m" : "l";
    public static string ScaleBand(double s) => s <= 0 ? null : s < 1.13 ? "100" : s < 1.38 ? "125" : s < 1.63 ? "150" : "175p";
    public static string NetOf(string n) => n is "5G" or "4G" or "3G" or "2G" ? n.ToLowerInvariant() : null;

    /// <summary>The environment report's body: no ID, no country, nothing that is in the daily report.</summary>
    public static string EnvBody(string lang, double width, double scale, int build, PhoneInfo phone, bool dev)
    {
        var l = (lang ?? "").ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            v = 1, app = Updater.Clean(AppInfo.Version.Split('+')[0]), lang = Regex.IsMatch(l, "^[a-z]{2,3}$") ? l : null,
            win = WindowBand(width), scale = ScaleBand(scale), build = build is >= 10000 and <= 99999 ? build : (int?)null,
            brand = BrandOf(phone?.Brand), android = AndroidOf(phone?.Android), net = NetOf(phone?.NetType), dev,
        });
    }

    /// <summary>Once a UTC day. <paramref name="late"/> (the hourly tick): even before the phone is read, so a day without a phone still counts.</summary>
    static async Task SendEnv(bool late)
    {
        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            bool dev;
            lock (Gate) { if (_st == null || _st.EnvDay == today) return; dev = Dev || _st.Mine; }
            if (_phoneEnv == null && !late) return;
            var (w, s) = _window != null ? _window() : (0, 0);
            var body = EnvBody(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, w, s, Environment.OSVersion.Version.Build, _phoneEnv, dev);
            if (await Post(body, TimeSpan.FromSeconds(8), Url + "/env")) lock (Gate) { _st.EnvDay = today; try { Store.Save(File_, _st); } catch { } }
        }
        catch (Exception ex) { NotSent(Url + "/env", Kind(ex)); }
    }

    /// <summary>
    /// Settings > Delete my stats: the server deletes everything kept under this install's ID (POST /v1/forget), then the app
    /// starts over with a new ID and nothing waiting. False when the server couldn't be reached (nothing changes here then).
    /// </summary>
    public static async Task<bool> Forget()
    {
        if (!_on) return false;
        string id;
        lock (Gate) { if (_st == null) return false; id = _st.Id; }
        try
        {
            if (!await Post(JsonSerializer.Serialize(new { v = 1, id, app = Updater.Clean(AppInfo.Version.Split('+')[0]) }), TimeSpan.FromSeconds(8), Url + "/forget")) return false;
        }
        catch { return false; }
        lock (Gate)
        {
            _st.Id = null; _st.Install = false;
            NewId(_st);
            _st.Clicks.Clear(); _st.Counts.Clear(); _st.Errors.Clear(); _st.Heat.Clear(); _st.Screens.Clear(); _st.Paths.Clear(); _st.Active?.Clear();
            _st.Minutes = 0;
            // Nov. 2026: nothing from the old ID's reports carries on (the best link seen, the extras already sent today)
            _st.BestLink = null; _st.ExtrasDay = null; _st.ExtrasSig = null;
            _st.FirstDay = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            try { Write(File_, _st, true); } catch { }
        }
        return true;
    }

    public static void Click(string name) => Add(s => s.Clicks, name);
    public static void Count(string name, int by = 1) => Add(s => s.Counts, name, by);

    static void Add(Func<State, Dictionary<string, int>> pick, string name, int by = 1)
    {
        if (!_on || name == null || !Name.IsMatch(name)) return;
        lock (Gate) { if (_st == null) return; var d = pick(_st); d[name] = d.GetValueOrDefault(name) + by; }
    }

    /// <summary>The exception's type and the first place in the app's own code, never its message (it can hold an IP or a name).</summary>
    public static void Error(Exception ex) => Error(ex, "");

    static void Error(Exception ex, string prefix)
    {
        if (!_on || ex == null) return;
        ex = ex.GetBaseException();
        var at = new StackTrace(ex).GetFrames()?.Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType?.Namespace?.StartsWith("SRT") == true);
        var where = at == null ? "unknown" : Regex.Replace($"{at.DeclaringType!.Name}.{at.Name}", @"[^A-Za-z0-9_.<>`|+-]", "");
        var type = prefix + Regex.Replace(ex.GetType().Name, @"[^A-Za-z0-9_]", "");
        if (type.Length > 48) type = type[..48];
        if (where.Length > 120) where = where[..120];
        lock (Gate)
        {
            if (_st == null) return;
            var key = type + "@" + where;
            if (!_st.Errors.TryGetValue(key, out var e)) { if (_st.Errors.Count >= 30) return; _st.Errors[key] = e = new ErrorItem { Type = type, At = where }; }
            e.N++;
        }
    }

    // A button's or menu item's Click handler is a method name in the code ("ChangeIp_Click"): that is what gets counted.
    static readonly PropertyInfo Handlers = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic);

    static void OnClick(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.Source) || sender is not UIElement el) return;
        try
        {
            var store = Handlers?.GetValue(el);
            var get = store?.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public);
            var list = get?.Invoke(store, new object[] { e.RoutedEvent }) as RoutedEventHandlerInfo[];
            var name = list?.Select(h => h.Handler.Method.Name).FirstOrDefault(n => Name.IsMatch(n))
                       ?? (el is FrameworkElement fe && Name.IsMatch(fe.Name ?? "") ? fe.Name : null);
            if (name != null) Click(name);
        }
        catch { }
    }

    /// <summary>A file that can't be written (locked by an antivirus, a link) never stops the app: the stats wait for the next save.</summary>
    static void Save(bool newDay = false)
    {
        if (!_on) return;
        lock (Gate)
        {
            if (_st == null) return;
            var now = DateTime.UtcNow;
            _st.Minutes += (now - _since).TotalMinutes;
            if (_visible && _screen != null && _screenSince != default) _st.Screens[_screen] = _st.Screens.GetValueOrDefault(_screen) + (now - _screenSince).TotalMinutes;
            _screenSince = now;
            _since = now;
            try { Write(File_, _st, newDay); }
            catch (Exception ex) { Log.Warn("Could not save telemetry.json: " + ex.Message); }
        }
    }

    /// <summary>On a new day it's saved twice, so telemetry.json.prev doesn't keep the day before's ID either.</summary>
    static void Write(string file, State st, bool newDay)
    {
        Store.Save(file, st);
        if (newDay) Store.Save(file, st);
    }

    /// <summary>
    /// List sizes go out in rough bands (0, 1, 2 to 5, 6 to 20, 21 to 100, more), never the exact number, so a list's size
    /// can't tie one day's report to the next.
    /// </summary>
    public static int Band(int n) => n <= 1 ? Math.Max(0, n) : n <= 5 ? 2 : n <= 20 ? 6 : n <= 100 ? 21 : 101;

    /// <summary>
    /// The report when the app closes. Without it a session's minutes and clicks only went out at the next start, so someone who
    /// opened the app once was counted with 0 minutes (all 5 real install-days of late Oct. 2026 had 0). Not within 10 minutes of
    /// the last report (the server refuses those; that little waits for the next start), and not with nothing new.
    /// </summary>
    public static bool SendAtClose(DateTime lastSentUtc, DateTime nowUtc, double minutes, int clicks, int counts) =>
        nowUtc - lastSentUtc >= Gap && (minutes >= 1 || clicks > 0 || counts > 0);

    /// <summary>The server's limit: one report per install every 10 minutes, and one more inside them marked exit.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);
    /// <summary>Nov. 2026: how often a report goes out while the app is open (and something is new).</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The report while the app is open (the 5-minute tick): every 30 minutes when something is new, and on a new UTC day or after
    /// 24 hours anyway. Never inside the server's 10 minutes.
    /// </summary>
    public static bool Periodic(DateTime lastSentUtc, DateTime nowUtc, double minutes, int clicks, int counts) =>
        nowUtc - lastSentUtc >= Gap && (nowUtc.Date != lastSentUtc.Date || nowUtc - lastSentUtc >= TimeSpan.FromHours(24)
            || (nowUtc - lastSentUtc >= Every && (minutes >= 1 || clicks > 0 || counts > 0)));

    /// <summary>
    /// At close, sleep or sign-out: a normal report 10 minutes or more after the last, or one marked exit inside them (the server takes
    /// one), so a 5-minute session isn't lost (audit D5). Nothing when nothing is new.
    /// </summary>
    public static (bool Go, bool Exit) AtClose(DateTime lastSentUtc, DateTime nowUtc, double minutes, int clicks, int counts)
    {
        var news = minutes >= 1 || clicks > 0 || counts > 0;
        if (!news) return (false, false);
        return nowUtc - lastSentUtc >= Gap ? (true, false) : (true, true);
    }

    /// <summary>Same as worker/stats MAX_MINUTES.</summary>
    public const int MaxMinutes = 24 * 60;

    /// <summary>
    /// Nov. 2026 (audit D12): what one report takes, under the server's caps (150 buttons, 100 counters, 30 errors, 300 heat cells,
    /// 16 KB), so the server never cuts what the app then takes as sent. The rest waits for the next report.
    /// </summary>
    public const int MaxClicks = 140, MaxCounts = 90, MaxErrors = 30, MaxHeat = 250, MaxBody = 14_000;

    /// <summary>One report's fields (Body). The same names as worker/stats/src/index.js reads.</summary>
    public sealed class Report
    {
        public string Id, App, Os, Mode, Link, Age, Data;
        public int Minutes;
        public bool Dev, Exit, Install;
        public int? Active7;
        public Dictionary<string, int> Clicks = new(), Counts = new(), Heat = new(), Screens = new(), Paths = new();
        public List<ErrorItem> Errors = new();
        public List<string> Settings, Regions;
    }

    static readonly JsonSerializerOptions BodyJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The JSON the server gets (tests/TelemetryTests.cs checks it against worker/stats/test/app-report.json, which the Worker's tests send through).</summary>
    public static string Body(Report r) => JsonSerializer.Serialize(new
    {
        v = 1, id = r.Id, idk = r.Install ? "install" : null, app = r.App, os = r.Os,
        mode = r.Mode, link = r.Link, minutes = r.Minutes, dev = r.Dev, exit = r.Exit ? true : (bool?)null, clicks = r.Clicks, counts = r.Counts,
        errors = r.Errors.Select(x => new { type = x.Type, at = x.At, n = x.N }),
        heat = r.Heat.Select(x => { var k = x.Key.Split('|'); return new object[] { k[0], k[1], k[2], x.Value }; }),
        screens = r.Screens, paths = r.Paths, age = r.Age, active7 = r.Active7, settings = r.Settings, regions = r.Regions, data = r.Data,
    }, BodyJson);

    /// <summary>
    /// The body within MaxBody bytes: the heat map is halved first (its biggest part), then the buttons and the counters. What is
    /// left out of <paramref name="r"/> stays on the PC for the next report (Sent only takes away what went).
    /// </summary>
    public static string Fit(Report r, params string[] keepCounts)
    {
        var body = Body(r);
        var keep = new HashSet<string>(keepCounts ?? Array.Empty<string>());
        for (var i = 0; i < 40 && Encoding.UTF8.GetByteCount(body) > MaxBody; i++)
        {
            if (r.Heat.Count > 0) r.Heat = r.Heat.OrderByDescending(x => x.Value).Take(r.Heat.Count / 2).ToDictionary(x => x.Key, x => x.Value);
            else if (r.Clicks.Count > 10) r.Clicks = r.Clicks.Take(r.Clicks.Count / 2).ToDictionary(x => x.Key, x => x.Value);
            else if (r.Counts.Count > keep.Count + 10) r.Counts = r.Counts.Where(x => keep.Contains(x.Key)).Concat(r.Counts.Where(x => !keep.Contains(x.Key)).Take((r.Counts.Count - keep.Count) / 2)).ToDictionary(x => x.Key, x => x.Value);
            else if (r.Errors.Count > 5) r.Errors = r.Errors.Take(r.Errors.Count / 2).ToList();
            else break;
            body = Body(r);
        }
        return body;
    }

    /// <summary>The list sizes, regions, settings, age, days active and data band: sent on a new UTC day or when they changed (audit D6).</summary>
    public static bool ExtrasDue(string lastDay, string lastSig, string today, string sig) => lastDay != today || lastSig != sig;

    /// <summary>
    /// What ExtrasDue compares, kept only in telemetry.json. The install ID is part of it, so after Delete my stats the new ID's
    /// first report carries the extras (and its install mark, idk) again the same day; without it the new ID had none until midnight.
    /// </summary>
    public static string ExtrasSig(string id, IDictionary<string, int> bands, IEnumerable<string> regions, IEnumerable<string> settings, string age, int? active7, string data) =>
        id + "|" + string.Join(";", (bands ?? new Dictionary<string, int>()).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"))
        + "|" + string.Join(",", (regions ?? Enumerable.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal))
        + "|" + string.Join(",", (settings ?? Enumerable.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal))
        + $"|{age}|{active7}|{data}";

    sealed record Snapshot(string Body, Dictionary<string, int> Clicks, Dictionary<string, int> Counts, List<ErrorItem> Errors, int Minutes,
        Dictionary<string, int> Heat, Dictionary<string, int> Screens, Dictionary<string, int> Paths, string ExtrasDay, string ExtrasSig);

    /// <summary>The window's thread or not (the mode and list sizes are plain fields): the install ID, the minutes so far and the body.</summary>
    static Snapshot Build(bool exit = false)
    {
        lock (Gate) MarkActive(_st, DateTime.UtcNow);   // the app can stay open past midnight (UTC)
        Save();
        (string mode, string link, Dictionary<string, int> sizes) = (null, null, null);
        try { if (_now != null) (mode, link, sizes) = _now(); } catch { }
        List<string> changed = null, regions = null, red = null;
        try { changed = _settings?.Invoke()?.Where(SettingNames.Contains).ToList(); } catch { }
        try { red = RedChecks(_redChecks?.Invoke()); } catch { }
        try { regions = _regions?.Invoke()?.Where(r => r != null && RegionCodeShape.IsMatch(r)).Distinct().Take(60).ToList(); } catch { }
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        lock (Gate)
        {
            // The server takes at most a day of minutes in one report (more is refused, not stored). More than that waiting
            // here is from days without a report: the rest is dropped instead of piling up.
            if (_st.Minutes > MaxMinutes) _st.Minutes = MaxMinutes;
            var first = DateTime.TryParse(_st.FirstDay, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var fd) ? fd : DateTime.UtcNow;
            var r = new Report
            {
                Id = _st.Id, App = Updater.Clean(AppInfo.Version.Split('+')[0]), Os = Environment.OSVersion.Version.ToString(), Mode = mode,
                Link = BestLink(_st.BestLink, link), Minutes = (int)Math.Round(_st.Minutes), Dev = Dev || _st.Mine, Exit = exit,
                Errors = _st.Errors.Values.Take(MaxErrors).Select(x => new ErrorItem { Type = x.Type, At = x.At, N = x.N }).ToList(),
                // beta.9: the server takes 300 heat cells, 12 screens and 60 moves a report; the rest waits for the next one
                Heat = _st.Heat.OrderByDescending(x => x.Value).Take(MaxHeat).ToDictionary(x => x.Key, x => x.Value),
                Screens = _st.Screens.Where(x => x.Value >= 1).Take(12).ToDictionary(x => x.Key, x => (int)Math.Min(MaxMinutes, Math.Floor(x.Value))),
                Paths = _st.Paths.Take(60).ToDictionary(x => x.Key, x => x.Value),
                Clicks = _st.Clicks.Take(MaxClicks).ToDictionary(x => x.Key, x => x.Value),
            };
            // A state at send time, first in the counters so they always fit: the red Checks rows (every report) and, once a day or
            // when they change, the list sizes in bands with the regions, settings, age, days active and data band
            var states = new Dictionary<string, int>();
            foreach (var k in red ?? new()) states[k] = 1;
            var bands = (sizes ?? new()).ToDictionary(x => x.Key, x => Band(x.Value));
            var age = AgeBand((int)(DateTime.UtcNow.Date - first.Date).TotalDays);
            var active7 = ActiveDays(_st.Active, DateTime.UtcNow);
            var data = _st.DataBytes > 0 && _st.DataDay == today ? DataBand(_st.DataBytes) : null;
            var sig = ExtrasSig(_st.Id, bands, regions, changed, age, active7, data);
            var extras = ExtrasDue(_st.ExtrasDay, _st.ExtrasSig, today, sig);
            if (extras)
            {
                foreach (var (k, v) in bands) states[k] = v;
                r.Install = true; r.Age = age; r.Active7 = active7; r.Settings = changed; r.Regions = regions; r.Data = data;
            }
            var counts = _st.Counts.Where(x => !states.ContainsKey(x.Key)).Take(Math.Max(0, MaxCounts - states.Count)).ToDictionary(x => x.Key, x => x.Value);
            r.Counts = states.Concat(counts).ToDictionary(x => x.Key, x => x.Value);
            var body = Fit(r, states.Keys.ToArray());
            // Sent takes away only what went: the counters without the states, and what Fit kept
            var sentCounts = r.Counts.Where(x => counts.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => x.Value);
            return new Snapshot(body, r.Clicks, sentCounts, r.Errors, r.Minutes, r.Heat, r.Screens, r.Paths, extras ? today : null, extras ? sig : null);
        }
    }

    /// <summary>No await comes back to the window's thread, so the close can wait for it.</summary>
    static async Task<bool> Post(string body, TimeSpan timeout, string url = null)
    {
        url ??= Url;
        using var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppIds.Name);
        using var resp = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode) { SentOk(url); return true; }
        if (!NotYet((int)resp.StatusCode)) NotSent(url, "the server answered " + (int)resp.StatusCode);
        return false;   // kept for the next try
    }

    /// <summary>
    /// 429 is the stats server's own "not yet": a report within 10 minutes of the last one, or the 5-minute signal sooner than
    /// that, as when the app is opened twice in a row. Nothing is wrong and the data waits for the next send, so the Log stays quiet.
    /// </summary>
    public static bool NotYet(int status) => status == 429;

    const string NotSentKey = "stats-not-sent ";

    /// <summary>A send to <paramref name="url"/> went through: its failures are written again if they come back.</summary>
    public static void SentOk(string url) => Log.Again(NotSentKey + url + "|");

    /// <summary>What each address is, for the Log.</summary>
    static string What(string url) => url == Url ? "report" : url.EndsWith("/live") ? "5-minute signal" : url.EndsWith("/env") ? "environment report" : "request";

    /// <summary>
    /// Why a send to <paramref name="url"/> didn't go, in the Log: once per kind of failure until one goes through again, so stats
    /// that stop leave a trace without a line every 5 minutes. Only the Log: nothing more is sent.
    /// </summary>
    public static void NotSent(string url, string why) =>
        Log.WarnOnce(NotSentKey + url + "|" + why, $"Usage stats: the {What(url)} wasn't sent ({why}). Tried again later; said once until one goes through.");

    /// <summary>A failure as a kind, never with a path or an address in it.</summary>
    public static string Kind(Exception ex) => ex switch
    {
        TaskCanceledException or TimeoutException => "no answer in time",
        HttpRequestException h => "no connection" + (h.HttpRequestError == HttpRequestError.Unknown ? "" : ": " + h.HttpRequestError),
        _ => ex.GetType().Name,
    };

    /// <summary>Only what went out is taken away: anything counted while sending stays for next time.</summary>
    static void Sent(Snapshot s)
    {
        lock (Gate)
        {
            foreach (var (k, v) in s.Clicks) { _st.Clicks[k] -= v; if (_st.Clicks[k] <= 0) _st.Clicks.Remove(k); }
            foreach (var (k, v) in s.Counts) { _st.Counts[k] -= v; if (_st.Counts[k] <= 0) _st.Counts.Remove(k); }
            foreach (var x in s.Errors) { var key = x.Type + "@" + x.At; if (_st.Errors.TryGetValue(key, out var e) && (e.N -= x.N) <= 0) _st.Errors.Remove(key); }
            _st.Minutes = Math.Max(0, _st.Minutes - s.Minutes);
            foreach (var (k, v) in s.Heat ?? new()) if (_st.Heat.TryGetValue(k, out var h) && (_st.Heat[k] = h - v) <= 0) _st.Heat.Remove(k);
            foreach (var (k, v) in s.Screens ?? new()) if (_st.Screens.TryGetValue(k, out var m) && (_st.Screens[k] = m - v) < 1e-9) _st.Screens.Remove(k);
            foreach (var (k, v) in s.Paths ?? new()) if (_st.Paths.TryGetValue(k, out var p) && (_st.Paths[k] = p - v) <= 0) _st.Paths.Remove(k);
            _st.LastSent = DateTime.UtcNow;
            _st.BestLink = null;   // the next report says the best link from now on
            if (s.ExtrasDay != null) { _st.ExtrasDay = s.ExtrasDay; _st.ExtrasSig = s.ExtrasSig; }
            try { Store.Save(File_, _st); } catch { }
        }
    }

    // 1 while a report is on its way: the timer, the sleep and the close (other threads) never send two at once
    static int _busy;
    static bool Begin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    static void End() => Interlocked.Exchange(ref _busy, 0);

    static async Task Send()
    {
        if (!_on || !Begin()) return;
        try
        {
            var s = Build();
            if (await Post(s.Body, TimeSpan.FromSeconds(8))) Sent(s);
        }
        catch (Exception ex) { NotSent(Url, Kind(ex)); }
        finally { End(); }
    }

    /// <summary>The minutes waiting to go: those saved and the ones since the last save.</summary>
    static double PendingMinutes() => _st.Minutes + (DateTime.UtcNow - _since).TotalMinutes;

    /// <summary>Every 5 minutes: the periodic report when due; every hour the save and the environment report as before.</summary>
    static void OnTick()
    {
        bool due;
        lock (Gate)
        {
            if (_st == null) return;
            MarkActive(_st, DateTime.UtcNow);
            due = Periodic(_st.LastSent, DateTime.UtcNow, PendingMinutes(), _st.Clicks.Count, _st.Counts.Count);
        }
        if (due) _ = Send();
        if (++_ticks % 12 == 0) { if (!due) Save(); _ = SendEnv(late: true); }
    }

    /// <summary>A report with at most <paramref name="wait"/> for the network, for when the PC or the app is about to stop.</summary>
    static void SendNow(TimeSpan wait)
    {
        if (!_on || !Begin()) return;
        try
        {
            (bool go, bool exit) when;
            lock (Gate) when = _st == null ? (false, false) : AtClose(_st.LastSent, DateTime.UtcNow, PendingMinutes(), _st.Clicks.Count, _st.Counts.Count);
            if (!when.go) return;
            var s = Build(when.exit);
            // The request gives up before the wait does, so a report the server got is (almost always) also seen as sent and
            // not sent again at the next start. A report still in flight from the timer when the app closes may still go twice:
            // the stats can count one session's clicks twice, never lose them.
            var post = Task.Run(() => Post(s.Body, wait - TimeSpan.FromSeconds(0.5)));
            if (post.Wait(wait) && post.Result) Sent(s);
        }
        catch { }
        finally { End(); }
    }

    static bool _closed;
    static Action _atExit;

    /// <summary>Counts that wait for a close (e.g. Phone setup still open when the app closes): run once at exit, before the last report.</summary>
    public static void AtExit(Action count) => _atExit += count;

    /// <summary>
    /// At close (Exit) and when Windows signs out or shuts down (SessionEnding): saved first, then at most 3 s for the report, so
    /// closing never hangs on the network. The session's length and drops are counted once, whichever comes first.
    /// </summary>
    static void SendAtExit()
    {
        if (_on && !_closed)
        {
            _closed = true;
            try { _atExit?.Invoke(); } catch { }
            Count("sess_" + SessionBand(DateTime.UtcNow - _started));
            int drops; lock (Gate) drops = _drops;
            Count("drop_" + DropsBand(drops));
            ScrollDone(_visible ? _screen : null);
        }
        Save();
        SendNow(TimeSpan.FromSeconds(3));
    }

    /// <summary>The PC is going to sleep: the minutes so far go out now (sleep isn't a close, so no session length).</summary>
    static void SendBeforeSleep()
    {
        Save();
        SendNow(TimeSpan.FromSeconds(2.5));
    }

    /// <summary>Back from sleep: the time asleep is not counted as minutes open (before, the next save added it all).</summary>
    static void Resumed()
    {
        lock (Gate) { _since = DateTime.UtcNow; _screenSince = DateTime.UtcNow; }
    }
}
