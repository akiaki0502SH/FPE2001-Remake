using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Fpe2001Remake.UI.Converters;

/// <summary>bool → 危险/次要画刷（动作栏危险动作分组）。</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Theme.DesignTokens.Palette.Danger);

    public Brush FalseBrush { get; set; } = new SolidColorBrush(Theme.DesignTokens.Palette.Text);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
