using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Converts an <see cref="int"/> (or any numeric value) to a <see cref="Visibility"/> value.
/// A value greater than zero maps to <see cref="Visibility.Visible"/>;
/// zero or negative maps to <see cref="Visibility.Collapsed"/>.
/// Pass <c>ConverterParameter="Inverse"</c> (or <c>"Invert"</c>) to reverse the mapping
/// (e.g. show a placeholder when the count is zero).
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

        int intValue;
        try
        {
            intValue = value switch
            {
                int i    => i,
                long l   => (int)l,
                double d => (int)d,
                _        => System.Convert.ToInt32(value ?? 0)
            };
        }
        catch
        {
            intValue = 0;
        }

        var isPositive = intValue > 0;
        return (isPositive ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
