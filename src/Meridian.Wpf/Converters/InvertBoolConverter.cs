using System;
using System.Globalization;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Converts a <see cref="bool"/> value to its logical inverse.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool boolValue ? !boolValue : false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool boolValue ? !boolValue : false;
    }
}
