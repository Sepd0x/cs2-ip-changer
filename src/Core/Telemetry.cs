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
/// Anonymous usage stats while the app is in beta (always on, README > Telemetry). Once at start and then once a day it sends:
/// a random install ID made on this PC, the app and Windows versions, the mode, how the phone is connected, minutes open,
/// how many times each button and hotkey was used (by the name of its code, never its text), a few counters, and errors as
/// type + place in the code. Never: IPs, servers, accounts, Steam names or IDs, the carrier, anything you typed.
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
        public string Id { get; set; }
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

    static string File_ => Paths.File("telemetry.json");

    /// <summary>At start. <paramref name="now"/> gives the mode, the phone link and list sizes at send time.</summary>
    public static void Start(bool demo, Func<(string, string, Dictionary<string, int>)> now)
    {
        if (demo || Rec.On) return;
        _on = true;
        _now = now;
        _st = Store.Load(File_, () => new State());
        _since = DateTime.UtcNow;
        if (string.IsNullOrEmpty(_st.Id)) { _st.Id = Guid.NewGuid().ToString("N"); Save(); }
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler(OnClick));
        Log.Added += e => { if (e.Level == LogLevel.Error) Count("log_error"); };
        if (Application.Current != null) Application.Current.Exit += (_, _) => Save();
        var t = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        t.Tick += (_, _) => { Save(); if (DateTime.UtcNow.Date != _st.LastSent.Date || DateTime.UtcNow - _st.LastSent > TimeSpan.FromHours(24)) _ = Send(); };
        t.Start();
        // The first report waits until the window has loaded its lists (and never slows the start).
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        first.Tick += (_, _) => { first.Stop(); _ = Send(); };
        first.Start();
    }

    public static void Click(string name) => Add(s => s.Clicks, name);
    public static void Count(string name, int by = 1) => Add(s => s.Counts, name, by);

    static void Add(Func<State, Dictionary<string, int>> pick, string name, int by = 1)
    {
        if (!_on || name == null || !Name.IsMatch(name)) return;
        lock (Gate) { var d = pick(_st); d[name] = d.GetValueOrDefault(name) + by; }
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

    static void Save()
    {
        if (!_on) return;
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            _st.Minutes += (now - _since).TotalMinutes;
            _since = now;
            Store.Save(File_, _st);
        }
    }

    static async Task Send()
    {
        if (!_on || _sending) return;
        _sending = true;
        try
        {
            Save();
            string body;
            Dictionary<string, int> clicks, counts;
            List<ErrorItem> errors;
            int minutes;
            (string mode, string link, Dictionary<string, int> sizes) = (null, null, null);
            try { if (_now != null) (mode, link, sizes) = _now(); } catch { }
            lock (Gate)
            {
                clicks = new(_st.Clicks); counts = new(_st.Counts); errors = _st.Errors.Values.Select(x => new ErrorItem { Type = x.Type, At = x.At, N = x.N }).ToList(); minutes = (int)Math.Round(_st.Minutes);
                var all = new Dictionary<string, int>(counts);
                foreach (var (k, v) in sizes ?? new()) all[k] = Math.Min(v, 9999);
                body = JsonSerializer.Serialize(new
                {
                    v = 1, id = _st.Id, app = Updater.Clean(AppInfo.Version.Split('+')[0]), os = Environment.OSVersion.Version.ToString(),
                    mode, link, minutes, dev = Dev, clicks, counts = all,
                    errors = errors.Select(x => new { type = x.Type, at = x.At, n = x.N }),
                });
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2IPChanger");
            using var resp = await http.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) return;   // kept for the next try
            lock (Gate)
            {
                // Only what went out is taken away: anything counted while sending stays for next time.
                foreach (var (k, v) in clicks) { _st.Clicks[k] -= v; if (_st.Clicks[k] <= 0) _st.Clicks.Remove(k); }
                foreach (var (k, v) in counts) { _st.Counts[k] -= v; if (_st.Counts[k] <= 0) _st.Counts.Remove(k); }
                foreach (var x in errors) { var key = x.Type + "@" + x.At; if (_st.Errors.TryGetValue(key, out var e) && (e.N -= x.N) <= 0) _st.Errors.Remove(key); }
                _st.Minutes = Math.Max(0, _st.Minutes - minutes);
                _st.LastSent = DateTime.UtcNow;
                Store.Save(File_, _st);
            }
        }
        catch { }
        finally { _sending = false; }
    }
}
