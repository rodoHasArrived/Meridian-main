using System.Windows.Input;

namespace Meridian.Wpf.Models;

/// <summary>
/// Represents a single action (button) in the PageActionBar.
/// </summary>
public sealed record ActionEntry(
    string Label,
    ICommand Command,
    string? Icon = null,
    string? Tooltip = null,
    bool IsPrimary = false);
