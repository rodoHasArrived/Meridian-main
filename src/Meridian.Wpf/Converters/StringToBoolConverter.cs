using System.Globalization;
using System.Windows.Data;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Converts a <see cref="string"/> to a nullable <see cref="bool"/> so that string-valued
/// model properties (e.g. <c>ParameterViewModel.RawValue</c>) can round-trip through a
/// WPF <c>CheckBox.IsChecked</c> binding without using a Visibility converter.
/// </summary>
[ValueConversion(typeof(string), typeof(bool?))]
public sealed class StringToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        bool.TryParse(value as string, out var b) ? b : (bool?)null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? b.ToString() : "False";
}
