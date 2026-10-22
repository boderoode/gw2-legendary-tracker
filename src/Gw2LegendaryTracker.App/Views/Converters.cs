using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Gw2LegendaryTracker.App.Views;

public sealed class ExpandGlyphConverter : IValueConverter
{
    public static ExpandGlyphConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "▴" : "▾";

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
