using System;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Display model for a single activity log entry.
/// </summary>
public sealed class LogEntryModel
{
    public DateTime RawTimestamp { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public SolidColorBrush LevelBackground { get; set; } = new(Colors.Transparent);
    public SolidColorBrush LevelForeground { get; set; } = new(Colors.Gray);
}
