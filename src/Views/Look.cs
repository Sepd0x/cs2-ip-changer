using System.Windows;

namespace SRT;

/// <summary>
/// 26 Nov (the App v3 design): a page that sets <c>local:Look.Round="True"</c> draws the shared buttons round (Btn, Primary
/// and a plain Button as 28 px pills, Primary filled with the accent). The code keeps picking the styles by the same keys
/// ("Primary", "Btn", the plain Button), so the page changes look without the code that picks the main button changing.
/// Inherited, so it is set once on the page.
/// </summary>
public static class Look
{
    public static readonly DependencyProperty RoundProperty = DependencyProperty.RegisterAttached("Round", typeof(bool), typeof(Look),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetRound(DependencyObject d) => (bool)d.GetValue(RoundProperty);
    public static void SetRound(DependencyObject d, bool v) => d.SetValue(RoundProperty, v);
}

/// <summary>
/// Log (26 Nov, App v3): a line's lead ("Config (Sync now):") in SemiBold and the rest after it. ConverterParameter "head" gives the
/// lead with its colon, anything else the rest. A line with no short lead before its first ": " is all rest.
/// </summary>
public sealed class LogPart : System.Windows.Data.IValueConverter
{
    /// <summary>Where the lead ends (after the colon), or 0 when the line has none: a colon and a space within the first 60 characters,
    /// with no sentence ending before it.</summary>
    public static int LeadEnd(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var i = s.IndexOf(": ", System.StringComparison.Ordinal);
        if (i <= 0 || i > 60) return 0;
        var lead = s[..i];
        return lead.Contains(". ") || lead.Contains('\n') ? 0 : i + 1;
    }

    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var s = value as string ?? "";
        var end = LeadEnd(s);
        return (parameter as string) == "head" ? s[..end] : s[end..];
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        System.Windows.DependencyProperty.UnsetValue;
}

/// <summary>
/// Settings > Player flags (26 Nov, App v3): the name of "kestrel · Staff" in SemiBold, apart from its label. Values: the flag's Title
/// (which follows the Steam name) and its Label; the Title without " · Label" at its end.
/// </summary>
public sealed class FlagName : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var title = values.Length > 0 ? values[0] as string ?? "" : "";
        var tail = " · " + (values.Length > 1 ? values[1] as string ?? "" : "");
        return title.EndsWith(tail, System.StringComparison.Ordinal) ? title[..^tail.Length] : title;
    }

    public object[] ConvertBack(object value, System.Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => null;
}

/// <summary>A small label in capitals ("IP SERVERS SEE"): the text stays the translated one, only shown in upper case.</summary>
public sealed class UpperText : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        (value as string)?.ToUpper(System.Globalization.CultureInfo.GetCultureInfo(Loc.Lang)) ?? "";

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        System.Windows.DependencyProperty.UnsetValue;
}
