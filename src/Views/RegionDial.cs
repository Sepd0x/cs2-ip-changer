using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SRT;

/// <summary>The Regions dial (26 Nov, the App v3 design): every region of the list as one spoke round a circle, the continents
/// one after the other with an empty slot before each one for its name. The spoke's length is the ping (0 to 200 ms), the ring is
/// the "Ping under" limit, and a thin arc, blue to magenta, runs over the regions CS2 may play in, with a yellow point on the
/// slowest. Drawn on a 576 px square (the design's), scaled to the room it has. Only draws what the list already holds; a click
/// on a spoke toggles that region like its checkbox.</summary>
public sealed class RegionDial : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource),
        typeof(IEnumerable), typeof(RegionDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));
    public static readonly DependencyProperty LimitProperty = DependencyProperty.Register(nameof(Limit),
        typeof(double), typeof(RegionDial), new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HoverIdProperty = DependencyProperty.Register(nameof(HoverId),
        typeof(string), typeof(RegionDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnHoverChanged));

    /// <summary>Only the region the mouse is on here (not a continent, not a row the mouse is already on) lights its row.</summary>
    static void OnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var dial = (RegionDial)d;
        MarkHot(dial.ItemsSource, dial.IsMouseOver ? e.NewValue as string : null);
    }

    /// <summary>Lights the row of region <paramref name="id"/> and only it; null lights none.</summary>
    public static void MarkHot(IEnumerable items, string id)
    {
        if (items == null) return;
        foreach (var r in items.OfType<RegionItem>()) r.Hot = id != null && r.Id == id;
    }

    public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public double Limit { get => (double)GetValue(LimitProperty); set => SetValue(LimitProperty, value); }
    /// <summary>The region (its Id) or the continent (its English name) under the mouse, here or in the list.</summary>
    public string HoverId { get => (string)GetValue(HoverIdProperty); set => SetValue(HoverIdProperty, value); }

    /// <summary>A spoke was clicked: the page toggles that region the same way as its checkbox.</summary>
    public event Action<RegionItem> SpokeClicked;

    // The design's numbers: a 576 px square, the centre at 288, spokes from 50 px, 0.62 px a millisecond up to 200 ms,
    // the arc at 180 px, the labels from 190 px.
    const double Size = 576, C = 288, R0 = 50, K = 0.62, ArcR = 180, LabelR = 190, MaxPing = 200;
    /// <summary>Round the dial clockwise from the right, as the world lies east to west.</summary>
    static readonly string[] Around = { "Europe", "Middle East", "Asia", "Oceania", "Africa", "South America", "North America" };

    readonly List<(RegionItem Item, double Deg)> _spokes = new();
    double _scale = 1, _ox, _oy;

    static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var dial = (RegionDial)d;
        if (e.OldValue is INotifyCollectionChanged o) o.CollectionChanged -= dial.Collection_Changed;
        if (e.NewValue is INotifyCollectionChanged n) n.CollectionChanged += dial.Collection_Changed;
        dial.Watch(e.OldValue as IEnumerable, false);
        dial.Watch(e.NewValue as IEnumerable, true);
    }

    void Watch(IEnumerable items, bool on)
    {
        if (items == null) return;
        foreach (var i in items.OfType<INotifyPropertyChanged>())
        {
            i.PropertyChanged -= Item_Changed;
            if (on) i.PropertyChanged += Item_Changed;
        }
    }

    void Collection_Changed(object s, NotifyCollectionChangedEventArgs e)
    {
        Watch(e.OldItems, false);
        Watch(e.NewItems, true);
        if (e.Action == NotifyCollectionChangedAction.Reset) Watch(ItemsSource, true);
        InvalidateVisual();
    }

    void Item_Changed(object s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RegionItem.Allowed) or nameof(RegionItem.PingMs)) InvalidateVisual();
    }

    /// <summary>Square: as big as the room allows (576 when the room has no end).</summary>
    protected override Size MeasureOverride(Size available)
    {
        var s = Math.Min(available.Width, available.Height);
        if (double.IsInfinity(s)) s = double.IsInfinity(available.Width) ? (double.IsInfinity(available.Height) ? Size : available.Height) : available.Width;
        return new Size(s, s);
    }

    Brush B(string key, Color fallback) => TryFindResource(key) switch
    {
        Brush b => b,
        Color c => new SolidColorBrush(c),
        _ => new SolidColorBrush(fallback),
    };

    Color Col(string key, Color fallback) => TryFindResource(key) switch
    {
        Color c => c,
        SolidColorBrush b => b.Color,
        _ => fallback,
    };

    /// <summary>
    /// The amber dot, on the slowest region CS2 may play in: only among the regions with a ping (audit 26 nov., X8: one not measured
    /// yet or with no answer was drawn as 200 ms and took the dot). None while no allowed region has a ping.
    /// </summary>
    public static RegionItem Slowest(IEnumerable<RegionItem> allowed) =>
        allowed.Where(r => r.PingMs >= 0).OrderByDescending(r => r.PingMs).FirstOrDefault();

    static double Len(int ping) => Math.Min(ping < 0 ? MaxPing : ping, MaxPing) * K;

    /// <summary>The colour of a ping against the limit: green up to it, light grey up to a quarter more, amber after.</summary>
    public static string PingTone(int ping, double limit) => ping < 0 ? "Faint" : ping <= limit ? "Good" : ping <= limit * 1.25 ? "Info" : "Warn";

    /// <summary>The order round the dial and the angle (degrees, 0 = right, clockwise) of each region and of each continent's name.</summary>
    public static (List<(RegionItem Item, double Deg)> Spokes, List<(string Continent, double Deg)> Names) Layout(IEnumerable<RegionItem> rows)
    {
        var groups = rows.GroupBy(r => r.Continent)
            .OrderBy(g => { var i = Array.IndexOf(Around, g.Key); return i < 0 ? 99 : i; })
            .Select(g => g.OrderBy(r => r.SortPing).ThenBy(r => r.City, StringComparer.CurrentCultureIgnoreCase).ToList()).ToList();
        var spokes = new List<(RegionItem, double)>();
        var names = new List<(string, double)>();
        var total = groups.Sum(g => g.Count) + groups.Count;
        if (total == 0) return (spokes, names);
        var step = 360.0 / total;
        var first = groups[0].Count;
        var slot = 0;
        foreach (var g in groups)
        {
            // the empty slot before the continent holds its name (the first continent's is the last slot of the circle)
            names.Add((g[0].Continent, (slot == 0 ? total - 1 - (first - 1) / 2.0 : slot - 1 - (first - 1) / 2.0) * step));
            foreach (var r in g) spokes.Add((r, (slot++ - (first - 1) / 2.0) * step));
            slot++;
        }
        return (spokes, names);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w < 40 || h < 40) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // so the whole square takes the mouse
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _scale = Math.Min(w, h) / Size;
        _ox = 0; _oy = 0;
        dc.PushTransform(new ScaleTransform(_scale, _scale));

        var items = ItemsSource?.OfType<RegionItem>().ToList() ?? new List<RegionItem>();
        var bg = B("Bg", Color.FromRgb(0x0E, 0x0C, 0x14));
        var line = B("Line", Color.FromRgb(0x1F, 0x1D, 0x24));
        var lineStrong = B("LineStrong", Color.FromRgb(0x30, 0x2E, 0x35));
        var surface2 = B("Surface2", Color.FromRgb(0x1C, 0x1A, 0x26));
        var surface3 = B("Surface3", Color.FromRgb(0x26, 0x23, 0x33));
        var text = B("Text", Color.FromRgb(0xEE, 0xEC, 0xF3));
        var muted = B("Muted", Color.FromRgb(0xA8, 0xAE, 0xBB));
        var faint = B("Faint", Color.FromRgb(0x8C, 0x92, 0xA0));
        var info = B("Info", Color.FromRgb(0xCD, 0xD1, 0xDA));
        var dim = faint.Clone(); dim.Opacity = 0.6; dim.Freeze();   // the design's #5a566b: Faint at 60 % on the ground
        var accent = B("Accent", Color.FromRgb(0x5B, 0xA8, 0xF0));
        var top1 = Col("Top1", Color.FromRgb(0x3B, 0xB4, 0xE5));
        var top2 = Col("Top2", Color.FromRgb(0xC6, 0x4F, 0xC6));
        var top3 = B("Top3", Color.FromRgb(0xD8, 0xE0, 0x3A));
        var sans = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var sansBold = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var mono = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var monoBold = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var big = new Typeface(new FontFamily("Bahnschrift, Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        FormattedText T(string s, Typeface f, double size, Brush b) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, f, size, b, dpi);

        // The ping scale: thin rings at 50, 100, 150 and 200 ms, their numbers on the way down to the right.
        var ringPen = new Pen(line, 1); ringPen.Freeze();
        foreach (var ms in new[] { 50, 100, 150, 200 }) dc.DrawEllipse(null, ringPen, new Point(C, C), R0 + ms * K, R0 + ms * K);
        foreach (var ms in new[] { 50, 100, 150, 200 })
        {
            var t = T(ms == 200 ? Loc.F("{0} ms", 200) : ms.ToString(CultureInfo.InvariantCulture), mono, 11, faint);
            var p = C + (R0 + ms * K) * 0.7071;
            dc.DrawRectangle(bg, null, new Rect(p - t.Width / 2 - 3, p - t.Height / 2, t.Width + 6, t.Height));
            dc.DrawText(t, new Point(p - t.Width / 2, p - t.Height / 2));
        }

        var (spokes, names) = Layout(items);
        _spokes.Clear();
        _spokes.AddRange(spokes);
        var allowed = spokes.Where(s => s.Item.Allowed).ToList();
        var lastAllowed = Slowest(allowed.Select(s => s.Item));
        var hover = HoverId;

        for (var i = 0; i < spokes.Count; i++)
        {
            var (r, deg) = spokes[i];
            var on = r.Allowed;
            var hot = hover != null && (r.Id == hover || r.Continent == hover);
            var len = Len(r.PingMs);
            dc.PushTransform(new RotateTransform(deg, C, C));
            if (on)
            {
                // the spoke: 4 px (6 under the mouse), round at its end
                var th = hot ? 6 : 4;
                var pen = new Pen(accent, th) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(C + R0, C), new Point(C + R0 + Math.Max(0, len - th / 2.0), C));
                // its piece of the arc, blue to magenta in the order round the dial
                var k = allowed.FindIndex(s => s.Item == r);
                var n = Math.Max(1, allowed.Count);
                var g = new LinearGradientBrush(Mix(top1, top2, k / (double)n), Mix(top1, top2, (k + 1) / (double)n), new Point(0, 0), new Point(0, 1));
                DrawSegment(dc, new Pen(g, 2), 14);
            }
            else
            {
                var lc = hot ? muted : surface3;
                dc.DrawLine(new Pen(lc, 2), new Point(C + R0, C), new Point(C + R0 + len, C));
                if (r.PingMs >= 0 && r.PingMs <= MaxPing)
                {
                    dc.DrawEllipse(bg, null, new Point(C + R0 + len, C), 4, 4);
                    dc.DrawEllipse(null, new Pen(hot ? info : dim, 1.5), new Point(C + R0 + len, C), 3.25, 3.25);
                }
                DrawSegment(dc, new Pen(surface3, 2), 14);
            }
            if (r == lastAllowed)
            {
                dc.DrawEllipse(bg, null, new Point(C + ArcR, C), 4, 4);
                dc.DrawEllipse(top3, null, new Point(C + ArcR, C), 2.5, 2.5);
            }
            dc.Pop();

            // The label: the ping then the city, read the right way up on both halves.
            var ping = r.PingMs >= 0 ? r.PingMs.ToString(CultureInfo.InvariantCulture) : "—";
            var pt = T(ping, mono, 11, on ? B(PingTone(r.PingMs, Limit), Color.FromRgb(0x46, 0xC5, 0x8A)) : hot ? muted : faint);
            var ct = T(r.City, on ? sansBold : sans, 11, on ? Brushes.White : hot ? text : faint);
            var flip = Flip(deg);
            dc.PushTransform(new RotateTransform(flip ? deg + 180 : deg, C, C));
            var y = C - 9;
            if (!flip)
            {
                dc.DrawText(pt, new Point(C + LabelR + 22 - pt.Width, C - pt.Height / 2));
                if (hot) dc.DrawRoundedRectangle(surface2, null, new Rect(C + LabelR + 27, y, ct.Width + 6, 18), 3, 3);
                dc.DrawText(ct, new Point(C + LabelR + 30, C - ct.Height / 2));
            }
            else
            {
                dc.DrawText(pt, new Point(C - LabelR - 22, C - pt.Height / 2));
                if (hot) dc.DrawRoundedRectangle(surface2, null, new Rect(C - LabelR - 27 - ct.Width - 6, y, ct.Width + 6, 18), 3, 3);
                dc.DrawText(ct, new Point(C - LabelR - 30 - ct.Width, C - ct.Height / 2));
            }
            dc.Pop();
        }

        // The continents' names in their empty slots: 11 px SemiBold, spaced, Muted.
        foreach (var (continent, deg) in names)
        {
            var flip = Flip(deg);
            var word = Loc.T(continent).ToUpper(CultureInfo.GetCultureInfo(Loc.Lang));   // X5: the app's language, not Windows' region ("AFRİCA")
            var glyphs = word.Select(ch => T(ch.ToString(), sansBold, 11, muted)).ToList();
            var width = glyphs.Sum(g => g.WidthIncludingTrailingWhitespace) + 0.66 * (glyphs.Count - 1);
            dc.PushTransform(new RotateTransform(flip ? deg + 180 : deg, C, C));
            var x = flip ? C - LabelR - width : C + LabelR;
            foreach (var g in glyphs)
            {
                dc.DrawText(g, new Point(x, C - g.Height / 2));
                x += g.WidthIncludingTrailingWhitespace + 0.66;
            }
            dc.Pop();
        }

        // The limit: its ring, the knob up on the right, and its value in a small pill.
        var lr = R0 + Math.Clamp(Limit, 0, MaxPing) * K;
        var ringBrush = text.Clone(); ringBrush.Opacity = 0.7; ringBrush.Freeze();
        dc.DrawEllipse(null, new Pen(ringBrush, 1.5), new Point(C, C), lr - 0.75, lr - 0.75);
        var q = lr * 0.7071;
        dc.DrawEllipse(text, null, new Point(C + q, C - q), 5, 5);
        var lt = T(Loc.F("{0} ms", (int)Limit), monoBold, 11, text);
        var pill = new Rect(C + q + 9, C - q - 24, lt.Width + 14, lt.Height + 4);
        dc.DrawRoundedRectangle(surface2, new Pen(lineStrong, 1), pill, 9, 9);
        dc.DrawText(lt, new Point(pill.X + 7, pill.Y + 2));

        // The centre: how many are allowed, of how many.
        dc.DrawEllipse(bg, new Pen(lineStrong, 1), new Point(C, C), 51.5, 51.5);
        var nt = T(allowed.Count.ToString(CultureInfo.InvariantCulture), big, 44, text);
        var of = T(Loc.F("of {0}", items.Count), sans, 12, muted);
        var top = C - (44 + of.Height) / 2;
        dc.DrawText(nt, new Point(C - nt.Width / 2, top - (nt.Height - 44) / 2));
        dc.DrawText(of, new Point(C - of.Width / 2, top + 44));
        dc.Pop();
    }

    static bool Flip(double deg)
    {
        var n = ((deg + 180) % 360 + 360) % 360 - 180;
        return Math.Abs(n) > 90;
    }

    /// <summary>A 2 px piece of the arc at 180 px, 28 px long, across the spoke (drawn in the spoke's turned frame).</summary>
    static void DrawSegment(DrawingContext dc, Pen pen, double half)
    {
        var a = half / ArcR;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(C + ArcR * Math.Cos(-a), C + ArcR * Math.Sin(-a)), false, false);
            c.ArcTo(new Point(C + ArcR * Math.Cos(a), C + ArcR * Math.Sin(a)), new Size(ArcR, ArcR), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    /// <summary>The design's blend: blue to magenta, slow at first (t^1.5).</summary>
    static Color Mix(Color a, Color b, double t)
    {
        var e = Math.Pow(Math.Clamp(t, 0, 1), 1.5);
        return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * e), (byte)Math.Round(a.G + (b.G - a.G) * e), (byte)Math.Round(a.B + (b.B - a.B) * e));
    }

    RegionItem Hit(Point p)
    {
        if (_spokes.Count == 0 || _scale <= 0) return null;
        var dx = (p.X - _ox) / _scale - C; var dy = (p.Y - _oy) / _scale - C;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < R0 || dist > 284) return null;
        var deg = Math.Atan2(dy, dx) * 180 / Math.PI;
        return _spokes.OrderBy(s => Math.Abs(Math.IEEERemainder(s.Deg - deg, 360))).First().Item;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var r = Hit(e.GetPosition(this));
        HoverId = r?.Id;
        Cursor = r != null ? Cursors.Hand : null;
    }

    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); HoverId = null; }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Hit(e.GetPosition(this)) is { } r) SpokeClicked?.Invoke(r);
    }
}

/// <summary>Regions list: a ping's colour against the "Ping under" limit (values: PingMs, the limit).</summary>
public sealed class RegionPingBrush : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var ping = values.Length > 0 && values[0] is int p ? p : -2;
        var limit = values.Length > 1 && values[1] is double l ? l : 60;
        return Application.Current?.TryFindResource(RegionDial.PingTone(ping, limit)) ?? Brushes.Gray;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Regions list: the length of a row's ping bar, 104 px for 200 ms; the limit's tick as a left margin on the same scale.</summary>
public sealed class RegionBarLength : IValueConverter
{
    public const double Width = 104;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value switch { int i => i, double d => d, _ => -1.0 };
        var px = v < 0 ? 0 : Math.Min(v, 200) / 200 * Width;
        return targetType == typeof(Thickness) ? new Thickness(px, 0, 0, 0) : px;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A continent in the Regions list: open when one of its regions is allowed (only when the list is built).</summary>
public sealed class RegionGroupOpen : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IEnumerable items && items.OfType<RegionItem>().Any(r => r.Allowed);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A continent's line on the right: "all allowed", "none allowed · 112 to 172 ms", "3 of 8 allowed" (values: its items,
/// and the list's Tag, which the page bumps on every change so the line is read again).</summary>
public sealed class RegionGroupNote : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var rows = (values.Length > 0 ? values[0] as IEnumerable : null)?.OfType<RegionItem>().ToList() ?? new List<RegionItem>();
        if (rows.Count == 0) return "";
        var on = rows.Count(r => r.Allowed);
        if (on == rows.Count) return Loc.T("all allowed");
        if (on > 0) return Loc.F("{0} of {1} allowed", on, rows.Count);
        var pings = rows.Where(r => r.PingMs >= 0).Select(r => r.PingMs).ToList();
        if (pings.Count == 0) return Loc.T("none allowed");
        var lo = pings.Min(); var hi = pings.Max();
        return lo == hi ? Loc.F("none allowed · {0} ms", lo) : Loc.F("none allowed · {0} to {1} ms", lo, hi);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
