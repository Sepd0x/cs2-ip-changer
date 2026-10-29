using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SRT.Core;

/// <summary>
/// Recording build ("dotnet build -p:Recording=true", exe CS2IPChanger-Recording.exe): nothing that identifies you reaches the screen.
/// Before each frame is drawn, every TextBlock in every window, tooltip and menu is filtered: any IP (the phone's and made-up ones too),
/// MAC, SteamID, e-mail, API key, the Windows user in paths, and the words the window knows are personal (logins, Steam names,
/// players, carrier, city, phone, SSIDs). A TextBox whose text would need filtering is drawn invisible.
/// The normal build compiles this but never turns it on.
/// </summary>
public static class Rec
{
#if RECORDING
    public static readonly bool On = true;
#else
    public static readonly bool On = false;
#endif
    public const string Mask = "•••";

    const string IdPatterns =
        @"(?<![\w:-])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![\w:-])" +                                     // MAC
        @"|7656119\d{10}|\[U:1:\d+\]|STEAM_[0-5]:[01]:\d+" +                                                 // SteamIDs
        @"|[\w.+-]+@[\w-]+(?:\.[\w-]+)+" +                                                                    // e-mail
        @"|(?<![0-9A-Fa-f])[0-9A-Fa-f]{32}(?![0-9A-Fa-f])" +                                                 // Steam Web API key
        @"|eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}" +                                   // Steam sign-in tokens (JWT)
        @"|steamcommunity\.com/(?:id|profiles)/[^\s/]+" +
        @"|(?<=[\\/]userdata[\\/])\d{3,}|(?<=[\\/]configs[\\/]backups[\\/])\d{3,}" +                   // account folder (id32 = SteamID64 - 76561197960265728)
        @"|(?<=\\Users\\)[^\\/""\r\n]+";                                                                      // Windows user in paths
    static readonly Regex Ids = new(IdPatterns, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Patterns = new(
        @"(?<![\w.])\d{1,3}\.\d{1,3}\.(?:\d{1,3}|\*|•+)\.(?:\d{1,3}|\*|•+)(?:/\d{1,2})?(?::\d{1,5})?(?!\w)" +   // IPv4, ranges, half-masked IPs, :port
        "|" + IdPatterns,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // IPv6 needs "::", a hex letter or 4+ colons, so clock times (12:30:45) stay
    static readonly Regex V6 = new(@"(?<![\w:.])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?:%\w+)?(?![\w:])", RegexOptions.Compiled);

    /// <summary>Masks the patterns above and the given personal words (whole words, any case).</summary>
    public static string Scrub(string text, IEnumerable<string> words = null) => Scrub(text, words == null ? null : WordRegex(words));

    static string Scrub(string text, Regex wordRx)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder();
        foreach (var (part, hide) in Split(text, wordRx)) sb.Append(hide ? Mask : part);
        return sb.ToString();
    }

    /// <summary>The text in pieces, each one personal (hide) or not, in order.</summary>
    static List<(string Part, bool Hide)> Split(string text, Regex wordRx)
    {
        var spans = new List<(int Start, int End)>();
        foreach (Match m in Patterns.Matches(text)) spans.Add((m.Index, m.Index + m.Length));
        foreach (Match m in V6.Matches(text))
            if (m.Value.Contains("::") || m.Value.Count(c => c == ':') >= 4 || m.Value.Any(char.IsLetter)) spans.Add((m.Index, m.Index + m.Length));
        if (wordRx != null) foreach (Match m in wordRx.Matches(text)) spans.Add((m.Index, m.Index + m.Length));
        spans.Sort();
        var parts = new List<(string, bool)>();
        var at = 0;
        for (var i = 0; i < spans.Count; i++)
        {
            var (start, end) = spans[i];
            while (i + 1 < spans.Count && spans[i + 1].Start <= end) end = Math.Max(end, spans[++i].End);   // overlapping or touching: one piece
            start = Math.Max(start, at);
            if (start > at) parts.Add((text[at..start], false));
            if (end > start) parts.Add((text[start..end], true));
            at = Math.Max(at, end);
        }
        if (at < text.Length || parts.Count == 0) parts.Add((text[at..], false));
        return parts;
    }

    /// <summary>For Save bug report: who you are goes (IDs, logins, names, SSIDs, your Windows user) but the IPs stay, a bug report needs them.</summary>
    public static string ScrubIds(string text, Regex wordRx)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var t = Ids.Replace(text, "(hidden)");
        return wordRx == null ? t : wordRx.Replace(t, "(hidden)");
    }

    public static Regex WordRegex(IEnumerable<string> words)
    {
        var list = words.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).Where(w => w.Length >= 2 && w != Mask)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(w => w.Length).Select(Regex.Escape).ToList();
        return list.Count == 0 ? null : new Regex(@"(?<![\p{L}\p{N}_])(?:" + string.Join("|", list) + @")(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ------------------------------------------------------------ The filter on screen

    /// <summary>
    /// Public lists (Valve's regions, the list of countries): your words are not masked there, only IPs and IDs. One blurred row
    /// among readable ones would give away your country or city. Inherited, so setting it on the list covers its rows and popup.
    /// </summary>
    public static readonly DependencyProperty PublicProperty = DependencyProperty.RegisterAttached("Public", typeof(bool), typeof(Rec),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetPublic(DependencyObject d) => (bool)d.GetValue(PublicProperty);
    public static void SetPublic(DependencyObject d, bool value) => d.SetValue(PublicProperty, value);

    /// <summary>
    /// A TextBox whose placeholder (its Tag, a fixed hint written in the XAML) holds a made-up example like "e.g. 1.2.3.4:27015".
    /// Only that placeholder is shown as is; what is typed in the box is still hidden.
    /// </summary>
    public static readonly DependencyProperty ExampleHintProperty = DependencyProperty.RegisterAttached("ExampleHint", typeof(bool), typeof(Rec),
        new FrameworkPropertyMetadata(false));
    public static bool GetExampleHint(DependencyObject d) => (bool)d.GetValue(ExampleHintProperty);
    public static void SetExampleHint(DependencyObject d, bool value) => d.SetValue(ExampleHintProperty, value);

    sealed class Seen { public string Original, Shown; public int Sig; public bool Hid, Built; }

    /// <summary>The censor bar: made-up text of the same shape, blurred, so it looks like an edit and not like the app.</summary>
    sealed class Censored : TextBlock { }
    static readonly ConditionalWeakTable<DependencyObject, Seen> _seen = new();
    static Func<IEnumerable<string>> _words;
    static Regex _wordRx;
    static int _sig = -1;
    static bool _walking;

    /// <summary>Before the first window is shown. Does nothing in the normal build.</summary>
    public static void Install(FrameworkElement anyElement, Func<IEnumerable<string>> personalWords)
    {
        if (!On) return;
        _words = personalWords;
        // LayoutUpdated fires for the whole dispatcher, after layout and before the frame is drawn: new or changed text never shows unfiltered.
        anyElement.LayoutUpdated += (_, _) => Walk();
        // Words that change with no layout (a carrier or a login learnt in the background) still re-filter what is already on screen.
        var t = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) => { if (Signature() != _sig) Walk(); };
        t.Start();
    }

    static int Signature()
    {
        var h = new HashCode();
        try { foreach (var w in _words?.Invoke() ?? Enumerable.Empty<string>()) h.Add(w, StringComparer.OrdinalIgnoreCase); }
        catch (InvalidOperationException) { return _sig; }   // a list changed while it was read: the next pass gets it
        return h.ToHashCode();
    }

    static void Walk()
    {
        if (_walking) return;
        _walking = true;
        try
        {
            var sig = Signature();
            if (sig != _sig)
            {
                try { _wordRx = WordRegex(_words?.Invoke() ?? Enumerable.Empty<string>()); _sig = sig; }
                catch (InvalidOperationException) { }
            }
            foreach (var src in PresentationSource.CurrentSources.OfType<PresentationSource>().ToList())
                if (src.RootVisual is DependencyObject root) Visit(root);
        }
        finally { _walking = false; }
    }

    static void Visit(DependencyObject d)
    {
        if (d is Censored) return;
        if (d is TextBlock tb) Filter(tb);
        else if (d is TextBox box) Hide(box);
        var n = VisualTreeHelper.GetChildrenCount(d);
        for (var i = 0; i < n; i++) Visit(VisualTreeHelper.GetChild(d, i));
    }

    static void Filter(TextBlock tb)
    {
        var text = tb.Text;
        if (tb.TemplatedParent is TextBox owner && GetExampleHint(owner) && owner.Tag is string hint && text == hint) return;
        var s = _seen.GetValue(tb, _ => new Seen { Sig = -1 });
        if (text == s.Shown && s.Sig == _sig) return;
        if (text != s.Shown) s.Original = text;
        s.Sig = _sig;
        var parts = string.IsNullOrEmpty(s.Original) ? null : Split(s.Original, GetPublic(tb) ? null : _wordRx);
        if (parts == null || !parts.Any(p => p.Hide))
        {
            if (s.Built) { s.Built = false; tb.SetCurrentValue(TextBlock.TextProperty, s.Original); }   // nothing personal any more: plain text back
            s.Shown = tb.Text;
            return;
        }
        // Only the content changes, the binding stays: a new value from it comes back here and is cut again.
        tb.Inlines.Clear();
        foreach (var (part, hide) in parts) tb.Inlines.Add(hide ? new InlineUIContainer(Censor(part, tb)) { BaselineAlignment = BaselineAlignment.Bottom } : new Run(part));
        s.Built = true;
        s.Shown = tb.Text;
    }

    static UIElement Censor(string real, TextBlock host)
    {
        // Same length and kind of characters, never the real ones: even a sharpened frame shows nothing. Same text, same fake (no flicker).
        var rnd = new Random(StringComparer.Ordinal.GetHashCode(real));
        var fake = new string(real.Select(c => char.IsDigit(c) ? (char)('0' + rnd.Next(10))
            : char.IsUpper(c) ? (char)('A' + rnd.Next(26)) : char.IsLetter(c) ? (char)('a' + rnd.Next(26)) : c).ToArray());
        return new Censored { Text = fake, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = Math.Max(9, host.FontSize * (host.FontWeight.ToOpenTypeWeight() >= 600 ? 1.0 : 0.8)) }, IsHitTestVisible = false };
    }

    /// <summary>A TextBox can't show a filtered copy without saving it: its text goes invisible instead.</summary>
    static void Hide(TextBox box)
    {
        var s = _seen.GetValue(box, _ => new Seen());
        var hide = Scrub(box.Text, _wordRx) != box.Text;
        if (hide == s.Hid) return;
        s.Hid = hide;
        if (hide) box.Foreground = Brushes.Transparent;
        else box.ClearValue(Control.ForegroundProperty);
    }
}
