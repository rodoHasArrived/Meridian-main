namespace Meridian.Wpf.Models;

/// <summary>
/// Display model for a completed time-series alignment run shown in the recent history list.
/// </summary>
public sealed class AlignmentHistoryEntry
{
    public string Name { get; init; } = string.Empty;
    public string DetailsText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
}
