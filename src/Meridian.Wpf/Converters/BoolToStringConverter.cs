using System;
using System.Globalization;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Converts a <see cref="bool"/> value to one of two strings based on the value.
/// Parameter format: "TrueString|FalseString" (e.g., "Edit Security|Create Security")
/// </summary>
[ValueConversion(typeof(bool), typeof(string))]
public sealed class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is true;
        var paramStr = parameter as string ?? "True|False";
        var parts = paramStr.Split('|');

        if (parts.Length == 2)
        {
            return boolValue ? parts[0] : parts[1];
        }

        return boolValue ? "True" : "False";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
