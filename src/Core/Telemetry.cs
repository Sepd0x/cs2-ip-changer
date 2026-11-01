using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SRT.Core;

/// <summary>
/// Anonymous usage stats, required to use the app: accepted once at the first start, before the app opens (App.StatsGate;
/// README > Telemetry). Until then nothing is sent and no ID is made. Once at start, at close (Exit) and once a day it sends:
/// a random ID that is new every day (UTC; never made from the PC or the user, so it never links one day to the next),
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
        /// <summary>Today's random ID, and the day (UTC) it is for. On another day it is replaced, never sent again.</summary>
        public string Id { get; set; }
        public string Day { get; set; }
        /// <summary>Set by hand in SRT_Dados/telemetry.json on the developer's own PCs, so their reports count as dev and stay out of the numbers.</summary>
        public bool Mine { get; set; }
        public DateTime LastSent { get; set; }
        public double Minutes { get; set; }
        public Dictionary<string, int> Clicks { get; set; } = new();
        public Dictionary<string, int> Counts { get; set; } = new();
        public Dictionary<string, ErrorItem> Errors { get; set; } = new();
    }
    public sealed class ErrorItem { public string Type { get; set; } public string At { get; set; } public int N { get; set; } }

    static readonly object Gate = new();
    static readonly Regex Name = new(@"^[A-Za-z0-9_]{1,48}$", RegexOptions.Compiled);
    static State _st;
    static DateTime _since;
    static bool _on, _sending;
    static Func<(string Mode, string Link, Dictionary<string, int> Sizes)> _now;

    static string File_ => _file ?? Paths.File("telemetry.json");

    static string _file;
    static bool _hooked;
    static DispatcherTimer _hourly, _first;

    public const string NoticeTitle = "Anonymous usage stats";

    /// <summary>The notice at the first start (Accept or Quit). README > Telemetry has the same list.</summary>
    public const string NoticeText =
        "While CS2 IP Changer is in beta, using it means it sends anonymous usage stats when it opens and closes, so bugs get found and fixed:\n\n"
        + "•  a random ID that is new every day, never made from your PC or from you, so it never links one day to the next\n"
        + "•  the app and Windows versions, and your country as two letters\n"
        + "•  the mode, how the phone is connected and minutes the app was open\n"
        + "•  how often each button and hotkey is used, a few counters (IP changes, kicks seen) and how long your lists are, in rough bands\n"
        + "•  errors, as their type and place in the app's code\n\n"
        + "Never IPs, servers, accounts, Steam names or IDs, your carrier or anything you type.\n\n"
        + "Quit closes the app without changing anything on your PC.";

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
    /// The ID for <paramref name="utcNow"/>'s day (UTC): the same all day, so restarting the app doesn't count twice, and a new
    /// random one once the day is over. Random only (never from the PC, Windows or the user), so nothing links one day to the next.
    /// </summary>
    public static (string Id, string Day) DailyId(string id, string day, DateTime utcNow)
    {
        var today = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return day == today && IdShape.IsMatch(id ?? "") ? (id, day) : (Guid.NewGuid().ToString("N"), today);
    }

    /// <summary>Gives <paramref name="st"/> the ID for today. True when it changed (an earlier day's ID, or none yet).</summary>
    static bool NewDay(State st, DateTime utcNow)
    {
        var (id, day) = DailyId(st.Id, st.Day, utcNow);
        if (id == st.Id && day == st.Day) return false;
        st.Id = id;
        st.Day = day;
        return true;
    }

    /// <summary>
    /// telemetry.json gets today's ID: an earlier day's ID is saved over at once, so it doesn't stay on the disk. The app does
    /// this at start and every hour while it runs; the tests call it with a fixed clock.
    /// </summary>
    public static void Today(string file, DateTime utcNow)
    {
        var st = Store.Load(file, () => new State());
        if (NewDay(st, utcNow)) Write(file, st, true);
    }

    static void TurnOn()
    {
        if (_on) return;
        _st = Store.Load(File_, () => new State());
        _st.Clicks ??= new(); _st.Counts ??= new(); _st.Errors ??= new();
        _since = DateTime.UtcNow;
        _on = true;
        if (NewDay(_st, DateTime.UtcNow)) Save(newDay: true);
        if (!_hooked)
        {
            _hooked = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler(OnClick));
            Log.Added += e => { if (e.Level == LogLevel.Error) Count("log_error"); };
            if (Application.Current != null) Application.Current.Exit += (_, _) => SendAtExit();
            _hourly = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _hourly.Tick += (_, _) => { bool nd; lock (Gate) nd = _st != null && NewDay(_st, DateTime.UtcNow); Save(nd); if (DateTime.UtcNow.Date != _st.LastSent.Date || DateTime.UtcNow - _st.LastSent > TimeSpan.FromHours(24)) _ = Send(); };
            // The first report waits until the window has loaded its lists (and never slows the start).
            _first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _first.Tick += (_, _) => { _first.Stop(); _ = Send(); };
        }
        _hourly.Start();
        _first.Start();
    }

    public static void Click(string name) => Add(s => s.Clicks, name);
    public static void Count(string name, int by = 1) => Add(s => s.Counts, name, by);

    static void Add(Func<State, Dictionary<string, int>> pick, string name, int by = 1)
    {
        if (!_on || name == null || !Name.IsMatch(name)) return;
        lock (Gate) { if (_st == null) return; var d = pick(_st); d[name] = d.GetValueOrDefault(name) + by; }
    }

    /// <summary>The exception's type and the first place in the app's own code, never its message (it can hold an IP or a name).</summary>
    public static void Error(Exception ex)
    {
        if (!_on || ex == null) return;
        ex = ex.GetBaseException();
        var at = new StackTrace(ex).GetFrames()?.Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType?.Namespace?.StartsWith("SRT") == true);
        var where = at == null ? "unknown" : Regex.Replace($"{at.DeclaringType!.Name}.{at.Name}", @"[^A-Za-z0-9_.<>`|+-]", "");
        var type = Regex.Replace(ex.GetType().Name, @"[^A-Za-z0-9_]", "");
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
        nowUtc - lastSentUtc >= TimeSpan.FromMinutes(10) && (minutes >= 1 || clicks > 0 || counts > 0);

    /// <summary>Same as worker/stats MAX_MINUTES.</summary>
    public const int MaxMinutes = 24 * 60;

    sealed record Snapshot(string Body, Dictionary<string, int> Clicks, Dictionary<string, int> Counts, List<ErrorItem> Errors, int Minutes);

    /// <summary>On the window's thread (the mode and list sizes come from it): today's ID, the minutes so far and the body.</summary>
    static Snapshot Build()
    {
        // never an earlier day's ID, even if the app stayed open past midnight (UTC)
        bool newDay;
        lock (Gate) newDay = NewDay(_st, DateTime.UtcNow);
        Save(newDay);
        (string mode, string link, Dictionary<string, int> sizes) = (null, null, null);
        try { if (_now != null) (mode, link, sizes) = _now(); } catch { }
        lock (Gate)
        {
            // The server takes at most a day of minutes in one report (more is refused, not stored). More than that waiting
            // here is from days without a report: the rest is dropped instead of piling up.
            if (_st.Minutes > MaxMinutes) _st.Minutes = MaxMinutes;
            var clicks = new Dictionary<string, int>(_st.Clicks);
            var counts = new Dictionary<string, int>(_st.Counts);
            var errors = _st.Errors.Values.Select(x => new ErrorItem { Type = x.Type, At = x.At, N = x.N }).ToList();
            var minutes = (int)Math.Round(_st.Minutes);
            var all = new Dictionary<string, int>(counts);
            foreach (var (k, v) in sizes ?? new()) all[k] = Band(v);
            var body = JsonSerializer.Serialize(new
            {
                v = 1, id = _st.Id, app = Updater.Clean(AppInfo.Version.Split('+')[0]), os = Environment.OSVersion.Version.ToString(),
                mode, link, minutes, dev = Dev || _st.Mine, clicks, counts = all,
                errors = errors.Select(x => new { type = x.Type, at = x.At, n = x.N }),
            });
            return new Snapshot(body, clicks, counts, errors, minutes);
        }
    }

    /// <summary>No await comes back to the window's thread, so the close can wait for it.</summary>
    static async Task<bool> Post(string body, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2IPChanger");
        using var resp = await http.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;   // otherwise kept for the next try
    }

    /// <summary>Only what went out is taken away: anything counted while sending stays for next time.</summary>
    static void Sent(Snapshot s)
    {
        lock (Gate)
        {
            foreach (var (k, v) in s.Clicks) { _st.Clicks[k] -= v; if (_st.Clicks[k] <= 0) _st.Clicks.Remove(k); }
            foreach (var (k, v) in s.Counts) { _st.Counts[k] -= v; if (_st.Counts[k] <= 0) _st.Counts.Remove(k); }
            foreach (var x in s.Errors) { var key = x.Type + "@" + x.At; if (_st.Errors.TryGetValue(key, out var e) && (e.N -= x.N) <= 0) _st.Errors.Remove(key); }
            _st.Minutes = Math.Max(0, _st.Minutes - s.Minutes);
            _st.LastSent = DateTime.UtcNow;
            try { Store.Save(File_, _st); } catch { }
        }
    }

    static async Task Send()
    {
        if (!_on || _sending) return;
        _sending = true;
        try
        {
            var s = Build();
            if (await Post(s.Body, TimeSpan.FromSeconds(8))) Sent(s);
        }
        catch { }
        finally { _sending = false; }
    }

    /// <summary>At close: saved first, then at most 3 s for the report, so closing never hangs on the network.</summary>
    static void SendAtExit()
    {
        Save();
        if (!_on || _sending) return;
        try
        {
            bool go;
            lock (Gate) go = _st != null && SendAtClose(_st.LastSent, DateTime.UtcNow, _st.Minutes, _st.Clicks.Count, _st.Counts.Count);
            if (!go) return;
            _sending = true;
            var s = Build();
            // The request gives up before the wait does, so a report the server got is (almost always) also seen as sent and
            // not sent again at the next start. A report still in flight from the timer when the app closes may still go twice:
            // the stats can count one session's clicks twice, never lose them.
            var post = Task.Run(() => Post(s.Body, TimeSpan.FromSeconds(2.5)));
            if (post.Wait(TimeSpan.FromSeconds(3)) && post.Result) Sent(s);
        }
        catch { }
        finally { _sending = false; }
    }
}
