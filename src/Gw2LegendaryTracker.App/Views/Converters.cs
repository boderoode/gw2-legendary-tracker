using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Gw2LegendaryTracker.App.Views;

public sealed class ExpandGlyphConverter : IValueConverter
{
    public static ExpandGlyphConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        // Glyphs of Segoe Fluent Icons: chevron up (collapse) and chevron down (expand).
        value is true ? "\uE70E" : "\uE70D";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class VisibilityConverter : IValueConverter
{
    public static VisibilityConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible only when every bound value is <c>true</c>.</summary>
public sealed class AllTrueVisibilityConverter : IMultiValueConverter
{
    public static AllTrueVisibilityConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
