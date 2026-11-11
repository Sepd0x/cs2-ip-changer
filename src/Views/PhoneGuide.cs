using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using SRT.Core;

namespace SRT;

// Phone setup's USB debugging guide (10 Nov 2026): the three steps drawn as small phone screens, for the phone's brand
public partial class MainWindow
{
    /// <summary>The brand pressed in the guide (null = the one the PC sees on USB).</summary>
    string _guidePicked;
    string _guideShown;

    /// <summary>Shows the guide under Phone setup's steps while USB debugging is the step to do (null hides it).</summary>
    void ShowGuide(PhoneSetupState st)
    {
        if (st == null) { SetupGuide.Visibility = Visibility.Collapsed; _guideShown = null; return; }
        var model = _phone?.Model;
        var guide = PhoneSetup.GuideFor(st.Usb.Vendor, model, _guidePicked);
        var seen = PhoneSetup.SeenBrand(st.Usb.Vendor, model);
        // Redrawn only when it changes: the panel reads the phone every 2 s
        var key = guide.Brand + "|" + seen;
        SetupGuide.Visibility = Visibility.Visible;
        if (key == _guideShown) return;
        _guideShown = key;
        SetupGuide.Content = PhoneSketch.Guide(guide, seen, b => { _guidePicked = b; _guideShown = null; ShowGuide(st); });
    }
}

/// <summary>
/// Draws the guide: a row to pick the brand, then three small phone screens with what to tap, and the text under each. The screens
/// show only the line to tap (grey bars for the rest, which differ from model to model) and use the theme's brushes, so they follow
/// the app's look and every word comes from <see cref="Texts"/>.
/// </summary>
public static class PhoneSketch
{
    const double W = 132, H = 150;

    public static FrameworkElement Guide(Texts.UsbGuide guide, string seen, Action<string> pick)
    {
        var root = new StackPanel();
        var brands = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        brands.Children.Add(new TextBlock { Text = Texts.GuideYourPhone, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) });
        foreach (var g in Texts.Guides)
        {
            var b = new Button
            {
                Content = g.Brand == seen ? $"{g.Name} · {Texts.GuideSeen}" : g.Name,
                Style = (Style)Application.Current.FindResource(g.Brand == guide.Brand ? "Primary" : "Btn"),
                Margin = new Thickness(0, 0, 6, 6), Tag = g.Brand,
            };
            b.Click += (_, _) => pick(g.Brand);
            System.Windows.Automation.AutomationProperties.SetName(b, g.Name);
            brands.Children.Add(b);
        }
        root.Children.Add(brands);

        var steps = new UniformGrid { Columns = guide.Steps.Length };
        for (var i = 0; i < guide.Steps.Length; i++)
        {
            var s = guide.Steps[i];
            var col = new StackPanel { Margin = new Thickness(0, 0, i < guide.Steps.Length - 1 ? 14 : 0, 0) };
            col.Children.Add(Screen(s.Screen));
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.Text, Margin = new Thickness(0, 8, 0, 0) };
            text.Inlines.Add(new System.Windows.Documents.Run($"{i + 1}. ") { Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.Run(s.Text));
            col.Children.Add(text);
            steps.Children.Add(col);
        }
        root.Children.Add(steps);

        var foot = (guide.Checked ? "" : Texts.GuideNotChecked + "\n") + Texts.DebugSafe;
        root.Children.Add(new TextBlock { Text = foot, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.Faint, Margin = new Thickness(0, 10, 0, 0) });
        return root;
    }

    /// <summary>One phone screen: square like the rest of the app, the line to tap in the accent with its mark.</summary>
    public static FrameworkElement Screen(Texts.GuideScreen s)
    {
        var body = new StackPanel();
        // the earpiece, so it reads as a phone
        body.Children.Add(new Rectangle { Width = 22, Height = 3, Fill = Ui.Line, Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Center });
        if (s.Prompt) body.Children.Add(Prompt(s));
        else
        {
            body.Children.Add(new TextBlock { Text = s.Title, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text, Margin = new Thickness(8, 0, 8, 8), TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(Bar(0.72));
            body.Children.Add(Bar(0.55));
            body.Children.Add(Bar(0.64));
            body.Children.Add(Target(s));
            body.Children.Add(Bar(0.48));
            body.Children.Add(Bar(0.6));
        }
        return new Border
        {
            Width = W, Height = H, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true,
            Background = (Brush)Application.Current.FindResource("Bg"), BorderBrush = Ui.LineStrong, BorderThickness = new Thickness(1),
            Child = body,
        };
    }

    static FrameworkElement Bar(double width) => new Rectangle
    {
        Width = (W - 16) * width, Height = 5, Fill = (Brush)Application.Current.FindResource("Surface3"),
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 4, 8, 6),
    };

    /// <summary>The line to tap: its name and "tap 7×", or a switch that is on.</summary>
    static FrameworkElement Target(Texts.GuideScreen s)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = s.Row, FontSize = 10, Foreground = Ui.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        FrameworkElement mark = s.Switch
            ? new Border
            {
                Width = 18, Height = 10, Background = Ui.Accent, VerticalAlignment = VerticalAlignment.Center, ToolTip = s.Mark,
                Child = new Rectangle { Width = 6, Height = 6, Fill = Ui.Text, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0) },
            }
            : new TextBlock { Text = s.Mark, FontSize = 9.5, Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        Grid.SetColumn(mark, 1);
        g.Children.Add(mark);
        return new Border
        {
            Background = (Brush)Application.Current.FindResource("AccentSoft"), BorderBrush = Ui.Accent, BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 2, 0, 4), Child = g,
        };
    }

    /// <summary>Android's question when the cable goes in: the box ticked and Allow marked.</summary>
    static FrameworkElement Prompt(Texts.GuideScreen s)
    {
        var p = new StackPanel { Margin = new Thickness(8, 14, 8, 0) };
        p.Children.Add(new TextBlock { Text = s.Title, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap });
        var tick = new DockPanel { Margin = new Thickness(0, 8, 0, 10) };
        var box = new Border { Width = 9, Height = 9, Background = Ui.Accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 6, 0) };
        DockPanel.SetDock(box, Dock.Left);
        tick.Children.Add(box);
        tick.Children.Add(new TextBlock { Text = s.Row, FontSize = 9.5, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap });
        p.Children.Add(tick);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new TextBlock { Text = Texts.PhoneCancelButton, FontSize = 9.5, Foreground = Ui.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        buttons.Children.Add(new Border
        {
            BorderBrush = Ui.Accent, BorderThickness = new Thickness(1), Background = (Brush)Application.Current.FindResource("AccentSoft"), Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock { Text = Texts.PhoneAllowButton, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Accent },
        });
        p.Children.Add(buttons);
        return new Border { Background = (Brush)Application.Current.FindResource("Surface2"), Margin = new Thickness(6, 10, 6, 0), Padding = new Thickness(0, 0, 0, 8), Child = p };
    }
}
