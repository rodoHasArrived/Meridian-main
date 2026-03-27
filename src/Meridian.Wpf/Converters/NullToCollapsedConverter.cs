using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Converts null or empty strings to <see cref="Visibility.Collapsed"/>,
/// and non-null/non-empty strings to <see cref="Visibility.Visible"/>.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
