using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SRT;

/// <summary>
/// The app's languages (beta.13): English (the source and the fallback), pt-BR, es (neutral, Latin America) and ru.
/// One mechanism: the English text is the key. src/Lang/*.json map each English text to its translation, and a text with no
/// entry shows in English. XAML needs no change: <see cref="Apply"/> swaps the text of a window's elements once, when the window
/// opens (changing the language in Settings > App restarts the window). Texts the code makes go through <see cref="T"/> and
/// <see cref="F"/>. The Log, the telemetry and the internal names stay in English: <see cref="En"/> gives back the English of a
/// text this class translated. tests/LocTests.cs fails when a key is missing in a language or a visible literal skips this class.
/// </summary>
public static class Loc
{
    public static readonly string[] Codes = { "en", "pt-BR", "es", "ru" };

    /// <summary>The language names as each speaker writes them (the Settings > App box).</summary>
    public static readonly string[] Names = { "English", "Português (Brasil)", "Español", "Русский" };

    public static string Lang { get; private set; } = "en";

    static Dictionary<string, string> _t = new();
    static readonly ConditionalWeakTable<string, string> _english = new();
    static readonly bool Pseudo = Environment.GetEnvironmentVariable("SRT_PSEUDO") == "1";

    /// <summary>The language for a saved choice: null or "auto" follows Windows (pt → pt-BR, es, ru, anything else English).</summary>
    public static string Resolve(string saved)
    {
        if (Environment.GetEnvironmentVariable("SRT_LANG") is { Length: > 0 } forced) saved = forced;   // captures and layout checks
        if (!string.IsNullOrEmpty(saved) && saved != "auto")
            foreach (var c in Codes) if (string.Equals(c, saved, StringComparison.OrdinalIgnoreCase)) return c;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() switch { "pt" => "pt-BR", "es" => "es", "ru" => "ru", _ => "en" };
    }

    public static void Init(string saved)
    {
        Lang = Resolve(saved);
        _t = Lang == "en" ? new() : Load(Lang);
        Hook();
    }

    static bool _hooked;

    /// <summary>Translates every element when it loads (rows built later, templates). First thing in Main: WPF caches the class
    /// handlers of a type the first time one of its instances is used, so a handler added later misses it.</summary>
    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => ApplyOne((DependencyObject)s)));
    }

    /// <summary>The table of a language: from the embedded src/Lang/&lt;code&gt;.json (empty values are texts not translated yet: English shows).</summary>
    public static Dictionary<string, string> Load(string code)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lang." + code + ".json");
        if (s == null) return new();
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
        return d.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>The text in the app's language; the English itself when there is no translation.</summary>
    public static string T(string en)
    {
        if (string.IsNullOrEmpty(en)) return en;
        if (!_t.TryGetValue(en, out var r) && !(Lang != "en" && Squash(en) is { } sq && sq != en && _t.TryGetValue(sq, out r))) { if (!Pseudo) return en; r = Stretch(en); }
        else if (Pseudo) r = Stretch(en);
        try { _english.AddOrUpdate(r, en); } catch { }
        return r;
    }

    /// <summary>XAML text as the key has it: runs of white space are one space, no space at the ends (null when it already is).</summary>
    static string Squash(string s) => System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");

    /// <summary>A text with {0}, {1}... in the app's language (the English template is the key).</summary>
    public static string F(string en, params object[] args)
    {
        var r = string.Format(CultureInfo.InvariantCulture, T(en), args);
        var plain = string.Format(CultureInfo.InvariantCulture, en, args);
        try { _english.AddOrUpdate(r, plain); } catch { }
        return r;
    }

    /// <summary>"3 accounts" in the app's language. The key is the singular noun; its entry holds the forms joined by "|":
    /// one|other (pt-BR, es) or one|few|many (ru: 1, 2-4, 5+). Null when the language has no entry (English builds it).</summary>
    public static string Plural(int n, string one)
    {
        if (!_t.TryGetValue(one, out var forms)) return null;
        var f = forms.Split('|');
        int i;
        if (Lang == "ru" && f.Length >= 3)
        {
            var m10 = n % 10; var m100 = n % 100;
            i = m10 == 1 && m100 != 11 ? 0 : m10 is >= 2 and <= 4 && m100 is not (>= 12 and <= 14) ? 1 : 2;
        }
        else i = n == 1 ? 0 : Math.Min(1, f.Length - 1);
        return n + " " + f[i];
    }

    /// <summary>The English of a text <see cref="T"/> or <see cref="F"/> made (for the Log and the telemetry); any other text as it is.</summary>
    public static string En(string s) => s != null && _english.TryGetValue(s, out var e) ? e : s;

    /// <summary>Pseudo-translation for the layout check (SRT_PSEUDO=1): every word 40% longer, placeholders kept.</summary>
    static string Stretch(string s)
    {
        var sb = new StringBuilder();
        foreach (var part in System.Text.RegularExpressions.Regex.Split(s, @"(\s+|\{[^}]*\})"))
        {
            sb.Append(part);
            if (part.Length >= 3 && !part.Contains('{') && part.Any(char.IsLetter) && !char.IsWhiteSpace(part[0]))
                sb.Append('e', (int)Math.Ceiling(part.Length * 0.4));
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- XAML

    static readonly ConditionalWeakTable<object, object> Done = new();

    /// <summary>Translates the texts of a window and everything in it (also what is collapsed now). No-op in English.</summary>
    public static void Apply(DependencyObject root)
    {
        if (Lang == "en" && !Pseudo) return;
        ApplyOne(root);
        foreach (var c in LogicalTreeHelper.GetChildren(root).Cast<object>().ToList())
            if (c is DependencyObject d) Apply(d);
    }

    static void ApplyOne(DependencyObject d)
    {
        if (Lang == "en" && !Pseudo) return;
        if (d == null || Done.TryGetValue(d, out _)) return;
        Done.Add(d, d);
        switch (d)
        {
            case Run r when !System.Windows.Data.BindingOperations.IsDataBound(r, Run.TextProperty): r.Text = T(r.Text); break;
            case TextBlock tb when tb.Inlines.Count == 0 || (tb.Inlines.Count == 1 && tb.Inlines.FirstInline is Run):
                if (!System.Windows.Data.BindingOperations.IsDataBound(tb, TextBlock.TextProperty) && !string.IsNullOrEmpty(tb.Text)) tb.Text = T(tb.Text);
                break;
            case Window w when w.Title != null && !System.Windows.Data.BindingOperations.IsDataBound(w, Window.TitleProperty): w.Title = T(w.Title); break;
            case ContentControl cc when cc.Content is string c && !System.Windows.Data.BindingOperations.IsDataBound(cc, ContentControl.ContentProperty): cc.Content = T(c); break;
        }
        if (d is HeaderedContentControl h && h.Header is string hs) h.Header = T(hs);
        else if (d is HeaderedItemsControl hi && hi.Header is string his) hi.Header = T(his);
        if (d is FrameworkElement fe && fe.ToolTip is string tt) fe.ToolTip = T(tt);
    }
}
