using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// 27 nov. (the owner: "a user may not know there is a setting for what he wants"). Two ways in: Find a setting over the list of
// groups (Ctrl+F on the page), which reads the page itself (names, what each one does, its tip) in the app's language plus a few
// words people type for it; and a notification that names a group of Settings, or a setting in it, gets Open Settings, which
// goes straight there. Nothing in the page moves or goes away.
public partial class MainWindow
{
    /// <summary>One thing on the Settings page you can change: its name, its group, every word around it, and where it is.</summary>
    public sealed record SettingEntry(string Title, string Group, string Text, string Words, FrameworkElement Target);

    List<SettingEntry> _setIndex;

    /// <summary>
    /// Words people type that the page doesn't use, by the setting's English name (also its group's name). Only English: the
    /// page's own words already match in every language.
    /// </summary>
    public static readonly Dictionary<string, string> SettingWords = new()
    {
        ["Start with Windows"] = "startup boot autostart login sign in open automatically launch",
        ["Close to the notification area"] = "tray minimize system tray background hide close x quit",
        ["Check for updates"] = "update upgrade new version",
        ["Get test versions"] = "beta preview test update",
        ["Language"] = "translate english portuguese spanish russian turkish idioma",
        ["Hotkeys and sounds"] = "shortcut keyboard key hotkey sound audio volume beep mute",
        ["Privacy"] = "stats telemetry tracking data streamer obs hide usernames",
        ["Mobile data"] = "data usage limit gb mb plan cap",
        ["Change IP"] = "airplane mode new ip range ban rotate",
        ["Remove the app"] = "uninstall delete remove",
        ["Maintenance"] = "logs folder firewall reset",
        ["Steam"] = "web api key launch options",
        ["Protection"] = "kill switch block home ip leak vpn",
        ["Open with CS2"] = "autostart cs2 launch game",
    };

    /// <summary>Lower case without accents, so "Atualizações" matches "atualizacoes".</summary>
    public static string Fold(string s)
    {
        var d = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// The settings that have every word of <paramref name="query"/>, best first: the name starting with it, then in the name, then
    /// in its extra words (<see cref="SettingWords"/>: "tray" is Close to the notification area before any line that says tray), in
    /// the group's name, then anywhere around it (what it does, the tip). Up to <paramref name="max"/>.
    /// </summary>
    public static List<int> FindSettings(string query, IReadOnlyList<(string Title, string Group, string Text, string Words)> items, int max = 8)
    {
        var q = Fold(query).Trim();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new();
        var found = new List<(int Rank, int Index)>();
        for (var i = 0; i < items.Count; i++)
        {
            var (title, group, text, extra) = (Fold(items[i].Title), Fold(items[i].Group), Fold(items[i].Text), Fold(items[i].Words));
            var all = $"{title} {group} {text} {extra}";
            if (!words.All(w => Starts(all, w))) continue;
            var rank = title.StartsWith(q, StringComparison.Ordinal) ? 0 : Starts(title, q) ? 1 : words.All(w => Starts(title, w)) ? 2
                : words.All(w => Starts($"{title} {extra}", w)) ? 3 : Starts(group, q) ? 4 : 5;
            found.Add((rank, i));
        }
        return found.OrderBy(f => f.Rank).ThenBy(f => f.Index).Take(max).Select(f => f.Index).ToList();

        // a word of the query must start a word of the text ("sound" finds "sounds", "ban" doesn't find "urban")
        static bool Starts(string hay, string w)
        {
            for (var at = hay.IndexOf(w, StringComparison.Ordinal); at >= 0; at = hay.IndexOf(w, at + 1, StringComparison.Ordinal))
                if (at == 0 || !char.IsLetterOrDigit(hay[at - 1])) return true;
            return false;
        }
    }

    /// <summary>
    /// What a notification's "Settings > ..." points at, if it is one of the app's own groups or settings (Windows' Settings and the
    /// phone's are left alone: "Privacy &amp; security" is not Privacy). The longest name that fits wins; null when none.
    /// </summary>
    public static string SettingsLinkIn(string english, IEnumerable<string> names)
    {
        if (string.IsNullOrEmpty(english)) return null;
        string best = null;
        for (var at = english.IndexOf("Settings > ", StringComparison.Ordinal); at >= 0; at = english.IndexOf("Settings > ", at + 1, StringComparison.Ordinal))
        {
            if (at > 0 && english[..at].TrimEnd().EndsWith("Windows", StringComparison.Ordinal)) continue;
            var rest = english[(at + "Settings > ".Length)..];
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n) || !rest.StartsWith(n, StringComparison.Ordinal)) continue;
                var after = rest[n.Length..];
                var ends = after.Length == 0 || ".,:;)\"'".Contains(after[0]) || (after.Length > 1 && after[0] == ' ' && (char.IsLower(after[1]) || after[1] is '(' or '>'));
                if (ends && (best == null || n.Length > best.Length)) best = n;
            }
        }
        return best;
    }

    /// <summary>Reads the Settings page once it is translated: each switch, button, list and box with its name and the words around it.</summary>
    List<SettingEntry> SettingsIndex()
    {
        if (_setIndex != null) return _setIndex;
        var list = new List<SettingEntry>();
        foreach (var g in SetGroups.Children.OfType<HeaderedContentControl>())
        {
            var group = g.Header as string ?? "";
            foreach (var c in Below(g).OfType<Control>().Where(c => c is CheckBox or Button or ComboBox or TextBox or PasswordBox or Slider))
            {
                if (c is Button && Ancestors(c).Any(a => a is CheckBox)) continue;
                // a button that serves a field next to it (the API key's Show and Get key) is part of that field, not an option
                if (c is Button && LogicalTreeHelper.GetParent(c) is DependencyObject row && Below(row).Any(x => x is TextBox or PasswordBox or ComboBox)) continue;
                var title = Title(c);
                if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;
                var around = c is CheckBox or Button ? Words(c) : LogicalTreeHelper.GetParent(c) is DependencyObject p ? Words(p) : "";
                var extra = SettingWords.TryGetValue(Loc.En(title), out var w) ? w : "";
                if (SettingWords.TryGetValue(Loc.En(group), out var gw)) extra += " " + gw;
                list.Add(new SettingEntry(title, group, $"{around} {c.ToolTip as string}", $"{Loc.En(title)} {extra}", c));
            }
        }
        return _setIndex = list;

        static IEnumerable<DependencyObject> Below(DependencyObject d)
        {
            foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            {
                yield return c;
                foreach (var x in Below(c)) yield return x;
            }
        }
        static IEnumerable<DependencyObject> Ancestors(DependencyObject d)
        {
            for (var p = LogicalTreeHelper.GetParent(d); p != null; p = LogicalTreeHelper.GetParent(p)) yield return p;
        }
        static string Words(DependencyObject d) => string.Join(" ", Below(d).OfType<TextBlock>().Select(t => t.Text));
        // a switch or button: its own first line (not a step's number, "1" to "4" in New IP + next account); a list or box: the
        // first text beside it in its row
        static string Title(Control c)
        {
            if (c is ContentControl cc)
                return cc.Content as string ?? Below(cc).OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && !t.All(char.IsDigit));
            for (var p = LogicalTreeHelper.GetParent(c); p != null && p is not HeaderedContentControl; p = LogicalTreeHelper.GetParent(p))
                if (Below(p).OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text) && !Below(c).Contains(t)) is { } label) return label.Text;
            return c.ToolTip as string;
        }
    }

    void SetFind_Changed(object sender, TextChangedEventArgs e)
    {
        var q = SetFind.Text;
        SetFound.Children.Clear();
        var on = q.Trim().Length > 0;
        SetFound.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SetNav.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (!on) return;
        var index = SettingsIndex().Where(s => s.Target.IsVisible).ToList();
        var hits = FindSettings(q, index.Select(s => (s.Title, s.Group, s.Text, s.Words)).ToList());
        if (hits.Count == 0)
        {
            SetFound.Children.Add(new TextBlock { Text = Loc.T("No setting has these words. Try another, or look through the groups."), Foreground = Ui.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 4, 6, 0) });
            return;
        }
        foreach (var i in hits)
        {
            var s = index[i];
            var b = new Button { Style = (Style)SetFound.FindResource("SetFoundItem"), Tag = s };
            System.Windows.Automation.AutomationProperties.SetName(b, $"{s.Title}, {s.Group}");
            b.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = s.Title, FontSize = 12.5, TextWrapping = TextWrapping.Wrap },   // the whole name, on two lines if it needs
                    new TextBlock { Text = s.Group, FontSize = 11.5, Foreground = Ui.Muted, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
                },
            };
            b.Click += (_, _) => ShowSetting(s);
            SetFound.Children.Add(b);
        }
    }

    void SetFind_Key(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SetFind.Text.Length > 0) { SetFind.Clear(); e.Handled = true; }
        else if (e.Key == Key.Enter && SetFound.Children.OfType<Button>().FirstOrDefault() is { Tag: SettingEntry s }) { ShowSetting(s); e.Handled = true; }
        else if (e.Key == Key.Down && SetFound.Children.OfType<Button>().FirstOrDefault() is { } first) { FocusRingShown(true); first.Focus(); e.Handled = true; }
    }

    /// <summary>
    /// Scrolls the setting to a third of the way down and blinks it twice (1.2 s), so the eye lands on it; with the keyboard the
    /// focus goes to it too. WPF draws no focus ring for focus given after a click, so the blink is what shows it.
    /// </summary>
    void ShowSetting(SettingEntry s)
    {
        var g = SetGroups.Children.IndexOf(SetGroups.Children.OfType<HeaderedContentControl>().FirstOrDefault(h => h.Header as string == s.Group));
        if (g >= 0 && g < SetNav.Children.Count) ((RadioButton)SetNav.Children[g]).IsChecked = true;
        var y = s.Target.TranslatePoint(new Point(0, 0), SetContent).Y;
        _setNavTarget = Math.Max(0, y - SetScroll.ViewportHeight / 3);
        SetScroll.ScrollToVerticalOffset(_setNavTarget.Value);
        if (s.Target.Focusable && s.Target.IsEnabled) s.Target.Focus();
        var blink = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1200) };
        foreach (var (t, o) in new[] { (0, 1.0), (300, 0.3), (600, 1.0), (900, 0.3), (1200, 1.0) })
            blink.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(o, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
        blink.Completed += (_, _) => s.Target.BeginAnimation(OpacityProperty, null);
        s.Target.BeginAnimation(OpacityProperty, blink);
        Log.Info($"Find a setting: {Loc.En(s.Title)} ({Loc.En(s.Group)}).");
    }

    /// <summary>Opens Settings at a group or a setting named in English (a notification's "Settings > ...").</summary>
    void OpenSettingsAt(string english)
    {
        NavSettings.IsChecked = true;
        Dispatcher.BeginInvoke(() =>
        {
            var g = SetGroups.Children.OfType<HeaderedContentControl>().ToList().FindIndex(h => Loc.En(h.Header as string) == english);
            if (g >= 0) { SettingsGo(SetGroups.Children.IndexOf(SetGroups.Children.OfType<HeaderedContentControl>().ElementAt(g))); return; }
            if (SettingsIndex().FirstOrDefault(s => Loc.En(s.Title) == english && s.Target.IsVisible) is { } hit) ShowSetting(hit);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 27 nov. (the owner: "how does someone know the option exists when they want to do something?"): the Settings groups that
    /// change what each page does, by the page's Tag (the page title's Settings for this page link). In the order they show.
    /// </summary>
    public static readonly Dictionary<string, string[]> PageSettingsGroups = new()
    {
        ["net"] = ["Protection", "Mobile data", "Change IP", "Hotkeys and sounds"],
        ["servers"] = ["Protection", "Player flags", "Open with CS2", "Steam"],
        ["regions"] = ["Protection"],
        ["accounts"] = ["New IP + next account", "Hotkeys and sounds", "Steam", "Privacy"],
        ["configs"] = ["Steam", "Open with CS2"],
        ["profiles"] = ["Steam", "Privacy"],
    };

    static readonly Dictionary<string, string> PageTitles = new()
    {
        ["net"] = "Connection", ["servers"] = "Servers", ["regions"] = "Regions", ["accounts"] = "Accounts", ["configs"] = "Configs", ["profiles"] = "Profiles",
    };

    void PageSettingsLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string page || !PageSettingsGroups.TryGetValue(page, out var groups)) return;
        NavSettings.IsChecked = true;
        Dispatcher.BeginInvoke(() => ShowPageSettings(PageTitles[page], groups), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Settings with one page's options listed where the groups are (as Find a setting lists its results): a line saying for
    /// which page, every option of those groups (a click goes to it and blinks it), and All groups to go back. The page scrolls
    /// to the first of them.
    /// </summary>
    void ShowPageSettings(string page, string[] groups)
    {
        if (SetFind.Text.Length > 0) SetFind.Clear();
        SetFound.Children.Clear();
        // on top: for which page, and All groups to go back
        SetFound.Children.Add(new TextBlock { Text = Loc.F("Settings for {0}", Loc.T(page)), Foreground = Ui.Muted, FontSize = 12.5, Margin = new Thickness(10, 0, 6, 0) });
        var all = new Button { Style = (Style)FindResource("LinkBtn"), Content = Loc.T("All groups"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 2) };
        all.Click += (_, _) => { SetFound.Children.Clear(); SetFound.Visibility = Visibility.Collapsed; SetNav.Visibility = Visibility.Visible; };
        SetFound.Children.Add(all);
        // then each group once, in capitals like START AND CLOSE on the page, with its options under it
        var index = SettingsIndex().Where(s => s.Target.IsVisible).ToList();
        foreach (var g in groups)
        {
            var mine = index.Where(s => Loc.En(s.Group) == g).ToList();
            if (mine.Count == 0) continue;
            SetFound.Children.Add(new TextBlock { Text = mine[0].Group.ToUpper(System.Globalization.CultureInfo.CurrentUICulture), Foreground = Ui.Muted, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 10, 6, 2) });
            foreach (var s in mine)
            {
                var b = new Button { Style = (Style)SetFound.FindResource("SetFoundItem"), Tag = s, Content = new TextBlock { Text = s.Title, FontSize = 12.5, TextWrapping = TextWrapping.Wrap } };
                System.Windows.Automation.AutomationProperties.SetName(b, $"{s.Title}, {s.Group}");
                b.Click += (_, _) => ShowSetting(s);
                SetFound.Children.Add(b);
            }
        }
        SetFound.Visibility = Visibility.Visible;
        SetNav.Visibility = Visibility.Collapsed;
        SetSide.ScrollToTop();
        var first = SetGroups.Children.OfType<HeaderedContentControl>().ToList().FindIndex(h => Loc.En(h.Header as string) == groups[0]);
        if (first >= 0) SettingsGo(SetGroups.Children.IndexOf(SetGroups.Children.OfType<HeaderedContentControl>().ElementAt(first)));
        Log.Info($"Settings for {page}: {string.Join(", ", groups)}.");
    }

    /// <summary>Leaving Settings drops a page's list (Settings from the sidebar shows the groups again); a search you typed stays.</summary>
    void PageSettings_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (PageSettings.IsVisible || SetFind.Text.Length > 0) return;
        SetFound.Children.Clear();
        SetFound.Visibility = Visibility.Collapsed;
        SetNav.Visibility = Visibility.Visible;
    }

    /// <summary>The English names a notification can point at: the groups and every setting on the page.</summary>
    IEnumerable<string> SettingsNames() =>
        SetGroups.Children.OfType<HeaderedContentControl>().Select(h => Loc.En(h.Header as string)).Concat(SettingsIndex().Select(s => Loc.En(s.Title)));

    /// <summary>A notification that says where in Settings something is gets Open Settings (unless it already has a button).</summary>
    void LinkToSettings(string text)
    {
        if (ToastActionBtn.Visibility == Visibility.Visible || !IsLoaded) return;
        if (SettingsLinkIn(Loc.En(text), SettingsNames()) is not string target) return;
        _toastAction = () => { OpenSettingsAt(target); return Task.CompletedTask; };
        ToastActionBtn.Content = Loc.T("Open Settings");
        ToastActionBtn.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(12);
        _toastTimer.Start();
    }
}
