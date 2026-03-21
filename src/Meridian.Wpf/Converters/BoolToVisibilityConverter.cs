using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Converts a <see cref="bool"/> value to a <see cref="Visibility"/> value.
/// Pass <c>ConverterParameter="Invert"</c> to reverse the mapping
/// (i.e. <c>true</c> → <see cref="Visibility.Collapsed"/>, <c>false</c> → <see cref="Visibility.Visible"/>).
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var boolValue = value is true;
        return (boolValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
