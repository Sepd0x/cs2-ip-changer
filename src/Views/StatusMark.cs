using System.Windows;
using System.Windows.Media;

namespace SRT;

/// <summary>
/// The 7 px status marker, with a shape per state so the state doesn't rest on the hue alone (colour-blind users):
/// filled square = OK, hollow square = waiting or off, diamond = warning, red filled square = error.
/// The state comes from the brush the code already sets (Good, Warn, Bad, Faint), so callers keep passing brushes.
/// </summary>
public sealed class StatusMark : FrameworkElement
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(StatusMark),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    public StatusMark()
    {
        Width = 9;
        Height = 9;
        SnapsToDevicePixels = true;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public enum Kind { Ok, Off, Warn, Bad }

    static Color? ColorOf(Brush b) => (b as SolidColorBrush)?.Color;

    static bool Same(Brush b, string key) => ColorOf(b) is Color c && ColorOf(Application.Current?.Resources[key] as Brush) == c;

    /// <summary>Which shape a brush means. Anything that isn't Good, Warn or Bad (Faint, Muted, null) is "off".</summary>
    public static Kind KindOf(Brush b) =>
        b == null ? Kind.Off
        : Same(b, "Warn") ? Kind.Warn
        : Same(b, "Bad") ? Kind.Bad
        : Same(b, "Good") || Same(b, "Accent") ? Kind.Ok
        : Kind.Off;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var cx = Math.Round(w / 2);
        var cy = Math.Round(h / 2);
        var b = Brush;
        switch (KindOf(b))
        {
            case Kind.Ok:
            case Kind.Bad:
                dc.DrawRectangle(b, null, new Rect(cx - 3.5, cy - 3.5, 7, 7));
                break;
            case Kind.Warn:
            {
                // a 6 px square turned 45 degrees
                var r = 4.2;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(cx, cy - r), true, true);
                    ctx.LineTo(new Point(cx + r, cy), false, false);
                    ctx.LineTo(new Point(cx, cy + r), false, false);
                    ctx.LineTo(new Point(cx - r, cy), false, false);
                }
                g.Freeze();
                dc.DrawGeometry(b, null, g);
                break;
            }
            default:
            {
                var pen = new Pen(Application.Current?.Resources["Faint"] as Brush ?? Brushes.Gray, 1);
                pen.Freeze();
                dc.DrawRectangle(null, pen, new Rect(cx - 3.5 + 0.5, cy - 3.5 + 0.5, 6, 6));
                break;
            }
        }
    }
}
