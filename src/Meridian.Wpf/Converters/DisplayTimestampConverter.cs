using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Formats a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> instant in the operator's
/// display time zone with <see cref="FormatHelpers.FormatDisplayTimestamp(DateTimeOffset, string)"/>,
/// e.g. "2026-07-05 13:00 UTC+01:00". The converter parameter, when given, is the date and time
/// pattern. Any other value passes through unchanged.
/// </summary>
[ValueConversion(typeof(DateTimeOffset), typeof(string))]
public sealed class DisplayTimestampConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pattern = parameter as string is { Length: > 0 } custom ? custom : FormatHelpers.DisplayTimestampFormat;
        return value switch
        {
            DateTimeOffset offset => FormatHelpers.FormatDisplayTimestamp(offset, pattern),
            DateTime dateTime => FormatHelpers.FormatDisplayTimestamp(dateTime, pattern),
            _ => value
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
