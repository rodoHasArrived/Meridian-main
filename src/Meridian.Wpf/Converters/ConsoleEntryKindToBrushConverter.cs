using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Converters;

/// <summary>
/// Maps <see cref="ConsoleEntryKind"/> to a WPF <see cref="Brush"/> for console line colouring.
/// </summary>
public sealed class ConsoleEntryKindToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush _errorBrush = new(Color.FromRgb(245, 101, 101));
    private static readonly SolidColorBrush _warningBrush = new(Color.FromRgb(237, 137, 54));
    private static readonly SolidColorBrush _separatorBrush = new(Color.FromRgb(100, 116, 139));
    private static readonly SolidColorBrush _outputBrush = new(Color.FromRgb(226, 232, 240));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConsoleEntryKind kind)
        {
            return kind switch
            {
                ConsoleEntryKind.Error => _errorBrush,
                ConsoleEntryKind.Warning => _warningBrush,
                ConsoleEntryKind.Separator => _separatorBrush,
                _ => _outputBrush
            };
        }
        return _outputBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
