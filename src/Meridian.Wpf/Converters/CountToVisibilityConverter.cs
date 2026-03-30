using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound integer value is greater than zero,
/// and <see cref="Visibility.Collapsed"/> when zero or negative.
/// Use this instead of <see cref="BooleanToVisibilityConverter"/> on <c>Count</c> properties.
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
