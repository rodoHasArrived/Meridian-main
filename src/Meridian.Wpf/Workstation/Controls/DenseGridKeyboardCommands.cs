using System.Windows.Input;

namespace Meridian.Wpf.Workstation.Controls;

public static class DenseGridKeyboardCommands
{
    public static readonly RoutedUICommand FocusFilter = new(
        "Focus filter",
        nameof(FocusFilter),
        typeof(DenseGridKeyboardCommands));

    public static readonly RoutedUICommand OpenSelectedDetails = new(
        "Open selected details",
        nameof(OpenSelectedDetails),
        typeof(DenseGridKeyboardCommands));

    public static readonly RoutedUICommand CloseDetails = new(
        "Close details",
        nameof(CloseDetails),
        typeof(DenseGridKeyboardCommands));

    public static readonly RoutedUICommand ClearFilters = new(
        "Clear filters",
        nameof(ClearFilters),
        typeof(DenseGridKeyboardCommands));

    public static readonly RoutedUICommand JumpToRelatedRecords = new(
        "Jump to related records",
        nameof(JumpToRelatedRecords),
        typeof(DenseGridKeyboardCommands));
}
