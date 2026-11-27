using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace SRT;

/// <summary>
/// 27 Nov: a button that shows only an icon (a Segoe Fluent glyph as its content: Minimize, Close, Copy IP, Delete snapshot...)
/// told Narrator and other screen readers the icon's private character, so they said nothing useful. Such a button now takes its
/// tooltip as its name for them, bound so a tooltip the code changes later (rows or cards) follows. A name set in the XAML wins.
/// </summary>
public static class IconNames
{
    static bool _hooked;

    /// <summary>With Loc.Hook, first thing in Main (WPF caches a type's class handlers the first time one of its instances is used).</summary>
    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EventManager.RegisterClassHandler(typeof(ButtonBase), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Name((ButtonBase)s)));
        EventManager.RegisterClassHandler(typeof(ButtonBase), FrameworkElement.SizeChangedEvent, new RoutedEventHandler((s, _) => Name((ButtonBase)s)));
    }

    /// <summary>Only a glyph from the private use area (U+E000 to U+F8FF), alone: what Segoe Fluent Icons draws.</summary>
    public static bool IsIcon(object content) => content is string s && s.Length == 1 && s[0] >= (char)0xE000 && s[0] <= (char)0xF8FF;

    public static void Name(ButtonBase b)
    {
        if (!IsIcon(b.Content) || !string.IsNullOrEmpty(AutomationProperties.GetName(b)) || BindingOperations.IsDataBound(b, AutomationProperties.NameProperty)) return;
        BindingOperations.SetBinding(b, AutomationProperties.NameProperty, new Binding(nameof(FrameworkElement.ToolTip)) { Source = b, Mode = BindingMode.OneWay, Converter = TipText.Instance });
    }

    sealed class TipText : IValueConverter
    {
        public static readonly TipText Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value as string ?? "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
