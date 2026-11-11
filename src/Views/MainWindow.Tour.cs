using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// The first-run tour (10 Nov 2026, asked by the owner): 5 short stops on the real controls, Skip at any time, once by itself,
// again from Settings > App. Texts in Texts.Tour.
public partial class MainWindow
{
    int _tourAt = -1;
    DispatcherTimer _tourWait;

    /// <summary>
    /// The tour opens by itself once: never seen, no phone ever checked on this PC (someone who already uses the phone knows the app),
    /// not in the demo. Seen is saved when it opens, so closing the app half way doesn't bring it back.
    /// </summary>
    public static bool TourDue(bool seen, bool phoneSeen, bool demo) => !seen && !phoneSeen && !demo;

    /// <summary>
    /// The tour waits for the window to be on screen and nothing else open on top (the first start's Steam and CS2 question, Phone
    /// setup, a running command), up to 2 minutes; then it waits for the next start.
    /// </summary>
    void StartTourWhenFree()
    {
        if (!TourDue(_s.TourSeen, _s.PhoneSeen, _demo) || Rec.On) return;
        var since = DateTime.UtcNow;
        _tourWait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _tourWait.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - since > TimeSpan.FromMinutes(2)) { _tourWait.Stop(); return; }
            if (!IsVisible || WindowState == WindowState.Minimized || Dialog.Visibility == Visibility.Visible || SetupPane.Visibility == Visibility.Visible
                || _command != null || PhoneKnown) return;
            _tourWait.Stop();
            OpenTour(auto: true);
        };
        _tourWait.Start();
    }

    void OpenTour(bool auto)
    {
        if (!_demo)
        {
            _s.TourSeen = true;
            try { SaveSettings(); } catch (Exception ex) { Log.Warn("Couldn't save that the tour was seen: " + ex.Message); }
            Telemetry.Count(auto ? "tour_shown" : "tour_again");
        }
        TourPane.Visibility = Visibility.Visible;
        ShowTourStop(0);
        TourNextBtn.Focus();
    }

    void ShowTourStop(int i)
    {
        _tourAt = i;
        var stop = Texts.Tour[i];
        var last = i == Texts.Tour.Length - 1;
        // The stops on Connection need it open; the sidebar ones point at the sidebar from wherever you are
        if (FindName(stop.Target) is FrameworkElement t && !IsNav(t)) NavNet.IsChecked = true;
        TourTitle.Text = Loc.T(stop.Title);
        TourText.Text = Loc.T(stop.Text);
        TourHint.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
        TourCount.Text = Texts.TourCount(i + 1, Texts.Tour.Length);
        TourBackBtn.Visibility = i == 0 ? Visibility.Collapsed : Visibility.Visible;
        TourSkipBtn.Content = Loc.T(last ? Texts.TourLater : Texts.TourSkip);
        TourNextBtn.Content = Loc.T(last ? Texts.TourOpenSetup : Texts.TourNext);
        // After the layout (the page may have just switched), so the hole lands on the control
        Dispatcher.BeginInvoke(PlaceTour, DispatcherPriority.Loaded);
    }

    static bool IsNav(FrameworkElement e) => e is RadioButton;

    /// <summary>The control a stop points at, as it is on screen now: a hidden Change IP falls back to Status.</summary>
    FrameworkElement TourTarget(int i)
    {
        var t = FindName(Texts.Tour[i].Target) as FrameworkElement;
        return t is { IsVisible: true } ? t : Hero;
    }

    void PlaceTour()
    {
        if (_tourAt < 0 || TourPane.Visibility != Visibility.Visible) return;
        var target = TourTarget(_tourAt);
        var all = new Rect(0, 0, TourPane.ActualWidth, TourPane.ActualHeight);
        Rect hole;
        try { hole = target.TransformToVisual(TourPane).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight)); }
        catch (InvalidOperationException) { hole = Rect.Empty; }
        if (!hole.IsEmpty) hole.Inflate(6, 6);
        var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
        shade.Children.Add(new RectangleGeometry(all));
        if (!hole.IsEmpty) shade.Children.Add(new RectangleGeometry(hole));
        TourShade.Data = shade;
        TourRing.Visibility = hole.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (!hole.IsEmpty) { TourRing.Margin = new Thickness(hole.X, hole.Y, 0, 0); TourRing.Width = hole.Width; TourRing.Height = hole.Height; }
        TourBox.Measure(new Size(TourBox.Width, double.PositiveInfinity));
        var (x, y) = TourBoxAt(hole, TourBox.DesiredSize, all.Size);
        Canvas.SetLeft(TourBox, x);
        Canvas.SetTop(TourBox, y);
    }

    /// <summary>
    /// Where the box goes: to the right of the control when it fits, else under it, else above it, else to its left; always inside
    /// the window with 12 px to spare.
    /// </summary>
    public static (double X, double Y) TourBoxAt(Rect hole, Size box, Size window)
    {
        const double gap = 14, edge = 12;
        if (hole.IsEmpty) return ((window.Width - box.Width) / 2, (window.Height - box.Height) / 2);
        double cx(double x) => Math.Clamp(x, edge, Math.Max(edge, window.Width - box.Width - edge));
        double cy(double y) => Math.Clamp(y, edge, Math.Max(edge, window.Height - box.Height - edge));
        if (hole.Right + gap + box.Width + edge <= window.Width) return (hole.Right + gap, cy(hole.Top));
        if (hole.Bottom + gap + box.Height + edge <= window.Height) return (cx(hole.Left), hole.Bottom + gap);
        if (hole.Top - gap - box.Height >= edge) return (cx(hole.Left), hole.Top - gap - box.Height);
        return (cx(hole.Left - gap - box.Width), cy(hole.Top));
    }

    void TourNext_Click(object sender, RoutedEventArgs e)
    {
        if (_tourAt < Texts.Tour.Length - 1) { ShowTourStop(_tourAt + 1); return; }
        CloseTour(TourEnd.Setup);
        _ = RunPhoneSetup();
    }

    void TourBack_Click(object sender, RoutedEventArgs e) { if (_tourAt > 0) ShowTourStop(_tourAt - 1); }

    void TourSkip_Click(object sender, RoutedEventArgs e) => CloseTour(_tourAt == Texts.Tour.Length - 1 ? TourEnd.Later : TourEnd.Skip);

    public enum TourEnd { Skip, Later, Setup }

    /// <summary>
    /// The usage count when the tour closes: skipped at stop n (tour_skip_1..5), read to the end (tour_done), and Open Phone setup
    /// pressed at the end (tour_setup too).
    /// </summary>
    public static string[] TourCounts(TourEnd end, int stop) => end switch
    {
        TourEnd.Skip => new[] { $"tour_skip_{stop + 1}" },
        TourEnd.Later => new[] { "tour_done" },
        _ => new[] { "tour_done", "tour_setup" },
    };

    void CloseTour(TourEnd end)
    {
        if (_tourAt < 0) return;
        if (!_demo) foreach (var n in TourCounts(end, _tourAt)) Telemetry.Count(n);
        Log.Info(end == TourEnd.Skip ? $"Tour skipped at {_tourAt + 1} of {Texts.Tour.Length}." : "Tour done.");
        _tourAt = -1;
        TourPane.Visibility = Visibility.Collapsed;
    }

    void TourAgain_Click(object sender, RoutedEventArgs e) => OpenTour(auto: false);

    // ------------------------------------------------------------ Regions: what the phone adds, said once

    /// <summary>
    /// Regions says once what the phone adds: only to someone who never had a phone checked and isn't in No phone mode (they chose
    /// to play at home). Seen is saved the first time it shows; it stays until Hide or the app closes.
    /// </summary>
    public static bool RegionsPitchDue(bool seen, bool phoneKnown, bool noPhone, bool demo) => !seen && !phoneKnown && !noPhone && !demo;

    void ShowRegionsPitch()
    {
        var due = RegionsPitchDue(_s.RegionsPitchSeen, PhoneKnown, _s.NoPhone, _demo);
        DemoRegionsPitch(ref due);
        if (RegionsPitch.Visibility == Visibility.Visible || !due) return;
        RegionsPitch.Visibility = Visibility.Visible;
        if (_demo) return;
        _s.RegionsPitchSeen = true;
        try { SaveSettings(); } catch (Exception ex) { Log.Warn("Couldn't save that the Regions note was seen: " + ex.Message); }
        Telemetry.Count("pitch_shown");
    }

    /// <summary>Demo only: the Regions note shows in the demo too (to capture it).</summary>
    partial void DemoRegionsPitch(ref bool due);

    async void RegionsPitchSetup_Click(object sender, RoutedEventArgs e)
    {
        RegionsPitch.Visibility = Visibility.Collapsed;
        NavNet.IsChecked = true;
        await RunPhoneSetup();
    }

    void RegionsPitchHide_Click(object sender, RoutedEventArgs e) => RegionsPitch.Visibility = Visibility.Collapsed;

    /// <summary>Esc skips the tour, the arrows move through it; the window moving or resizing moves the hole with it.</summary>
    bool TourKey(Key key)
    {
        if (_tourAt < 0) return false;
        if (key == Key.Escape) TourSkip_Click(this, null);
        else if (key == Key.Right && _tourAt < Texts.Tour.Length - 1) ShowTourStop(_tourAt + 1);
        else if (key == Key.Left) TourBack_Click(this, null);
        else return false;
        return true;
    }
}
