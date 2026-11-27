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
/// The app's languages (beta.13): English (the source and the fallback), pt-BR, es (neutral, Latin America), ru and tr.
/// One mechanism: the English text is the key. src/Lang/*.json map each English text to its translation, and a text with no
/// entry shows in English. XAML needs no change: <see cref="Apply"/> swaps the text of a window's elements once, when the window
/// opens (changing the language in Settings > App restarts the window). Texts the code makes go through <see cref="T"/> and
/// <see cref="F"/>. The Log, the telemetry and the internal names stay in English: <see cref="En"/> gives back the English of a
/// text this class translated. tests/LocTests.cs fails when a key is missing in a language or a visible literal skips this class.
/// </summary>
public static class Loc
{
    public static readonly string[] Codes = { "en", "pt-BR", "es", "ru", "tr" };

    /// <summary>The language names as each speaker writes them (the Settings > App box).</summary>
    public static readonly string[] Names = { "English", "Português (Brasil)", "Español", "Русский", "Türkçe" };

    public static string Lang { get; private set; } = "en";

    static Dictionary<string, string> _t = new();
    static readonly ConditionalWeakTable<string, string> _english = new();
    static readonly bool Pseudo = Environment.GetEnvironmentVariable("SRT_PSEUDO") == "1";

    /// <summary>The language for a saved choice: null or "auto" follows Windows (pt → pt-BR, es, ru, tr, anything else English).</summary>
    public static string Resolve(string saved)
    {
        if (Environment.GetEnvironmentVariable("SRT_LANG") is { Length: > 0 } forced) saved = forced;   // captures and layout checks
        if (!string.IsNullOrEmpty(saved) && saved != "auto")
            foreach (var c in Codes) if (string.Equals(c, saved, StringComparison.OrdinalIgnoreCase)) return c;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() switch { "pt" => "pt-BR", "es" => "es", "ru" => "ru", "tr" => "tr", _ => "en" };
    }

    /// <summary>The app language's culture for dates ("6 Nov", "6 нояб.", "6 Kas"); invariant in English or when Windows lacks it.</summary>
    public static System.Globalization.CultureInfo Culture
    {
        get
        {
            if (Lang == "en") return CultureInfo.InvariantCulture;
            try { return CultureInfo.GetCultureInfo(Lang); } catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
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
        // Loaded is only raised in the branches of the tree where an element has its own Loaded handler, so the rows of a list, the
        // contents of a template and the like never got it (15 nov.: "As admin", "Follow" and the player list stayed English in pt-BR,
        // es and ru). SizeChanged is raised for every element at its first layout, before it is drawn.
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent, new RoutedEventHandler((s, _) => ApplyOne((DependencyObject)s)));
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
    /// one|other (pt-BR, es; tr writes the same noun twice, Turkish keeps it singular after a number) or one|few|many (ru: 1, 2-4, 5+). Null when the language has no entry (English builds it).</summary>
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

    /// <summary>The noun alone, in the singular ("game ban" -> "игровой бан"): its entry holds all the forms joined by "|", see <see cref="Plural"/>.</summary>
    public static string One(string one) => _t.TryGetValue(one, out var forms) ? forms.Split('|')[0] : one;

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

    /// <summary>
    /// The value comes from a Binding or a TemplateBinding: the code gives it already translated and changes it later, so a translated
    /// copy written over it as a local value would hide every later change (audit 26 nov., X1 and X3: the Profiles menu kept "Sign in"
    /// after signing in, a Servers row's tip showed another server, the dialog box kept the first hint).
    /// </summary>
    public static bool Bound(DependencyObject d, DependencyProperty p) =>
        System.Windows.Data.BindingOperations.IsDataBound(d, p) || DependencyPropertyHelper.GetValueSource(d, p).IsExpression;

    static bool Changes(string s, out string t) { t = T(s); return t != s; }

    static void ApplyOne(DependencyObject d)
    {
        if (Lang == "en" && !Pseudo) return;
        if (d == null || Done.TryGetValue(d, out _)) return;
        Done.Add(d, d);
        // 26 nov. (X3): only a text that changes is written. A TemplateBinding inside a template is not an expression for WPF
        // (its source is ParentTemplate), so Bound can't see it; the box's hint comes already translated from the Tag, and writing
        // the same text over it as a local value cut the TemplateBinding, so the next dialog kept the first hint.
        switch (d)
        {
            case Run r when !Bound(r, Run.TextProperty) && Changes(r.Text, out var rt): r.Text = rt; break;
            case TextBlock tb when tb.Inlines.Count == 0 || (tb.Inlines.Count == 1 && tb.Inlines.FirstInline is Run):
                if (!Bound(tb, TextBlock.TextProperty) && !string.IsNullOrEmpty(tb.Text) && Changes(tb.Text, out var tbt)) tb.Text = tbt;
                break;
            case Window w when w.Title != null && !Bound(w, Window.TitleProperty) && Changes(w.Title, out var wt): w.Title = wt; break;
            case ContentControl cc when cc.Content is string c && !Bound(cc, ContentControl.ContentProperty) && Changes(c, out var ct): cc.Content = ct; break;
        }
        if (d is HeaderedContentControl h && h.Header is string hs && !Bound(h, HeaderedContentControl.HeaderProperty)) { if (Changes(hs, out var ht)) h.Header = ht; }
        else if (d is HeaderedItemsControl hi && hi.Header is string his && !Bound(hi, HeaderedItemsControl.HeaderProperty) && Changes(his, out var hit)) hi.Header = hit;
        if (d is FrameworkElement fe && fe.ToolTip is string tt && !Bound(fe, FrameworkElement.ToolTipProperty) && Changes(tt, out var ttt)) fe.ToolTip = ttt;
        // 27 nov. (Codex C13): the name a screen reader reads, when the XAML writes it ("Find a setting" stayed English)
        if (d is UIElement && System.Windows.Automation.AutomationProperties.GetName(d) is { Length: > 0 } an
            && !Bound(d, System.Windows.Automation.AutomationProperties.NameProperty) && Changes(an, out var ant)) System.Windows.Automation.AutomationProperties.SetName(d, ant);
        // the grey hint inside an empty box is its Tag (the style draws it)
        if (d is TextBox or PasswordBox && ((FrameworkElement)d).Tag is string hint && hint.Length > 0) ((FrameworkElement)d).Tag = T(hint);
    }
}
