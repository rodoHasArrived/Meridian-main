using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Converts a <see cref="string"/> value to a <see cref="Visibility"/> value.
/// A non-null, non-empty string maps to <see cref="Visibility.Visible"/>;
/// a null or empty string maps to <see cref="Visibility.Collapsed"/>.
/// Pass <c>ConverterParameter="Invert"</c> to reverse the mapping.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var hasValue = !string.IsNullOrEmpty(value as string);
        return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
