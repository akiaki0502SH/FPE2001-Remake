using System.Windows;

namespace Fpe2001Remake.UI.Converters;

/// <summary>bool → Visibility。</summary>
public sealed class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new System.NotSupportedException();
}
