using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
        @"|steamcommunity\.com/(?:id|profiles)/[^\s/]+" +
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
        var t = Patterns.Replace(text, Mask);
        t = V6.Replace(t, m => m.Value.Contains("::") || m.Value.Count(c => c == ':') >= 4 || m.Value.Any(c => char.IsLetter(c) && c != '%') ? Mask : m.Value);
        return wordRx == null ? t : wordRx.Replace(t, Mask);
    }

    /// <summary>For Export diagnostics: who you are goes (IDs, logins, names, SSIDs, your Windows user) but the IPs stay, a bug report needs them.</summary>
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

    sealed class Seen { public string Original, Shown; public int Sig; public bool Hid; }
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
        if (d is TextBlock tb) Filter(tb);
        else if (d is TextBox box) Hide(box);
        var n = VisualTreeHelper.GetChildrenCount(d);
        for (var i = 0; i < n; i++) Visit(VisualTreeHelper.GetChild(d, i));
    }

    static void Filter(TextBlock tb)
    {
        var text = tb.Text;
        var s = _seen.GetValue(tb, _ => new Seen { Sig = -1 });
        if (text == s.Shown && s.Sig == _sig) return;
        if (text != s.Shown) s.Original = text;
        var shown = Scrub(s.Original, _wordRx);
        s.Shown = shown; s.Sig = _sig;
        if (shown != text) tb.SetCurrentValue(TextBlock.TextProperty, shown);   // keeps the binding: a new value comes back here
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
