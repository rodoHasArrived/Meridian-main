using System;
using System.Windows.Media;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Models;

/// <summary>
/// Display model for a single notification item in the list.
/// </summary>
public sealed class NotificationItem
{
    public string Icon { get; set; } = string.Empty;
    public Brush IconColor { get; set; } = Brushes.Transparent;
    public Brush IconBackground { get; set; } = Brushes.Transparent;
    public Brush TypeBackground { get; set; } = Brushes.Transparent;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime RawTimestamp { get; set; }
    public NotificationType NotificationType { get; set; }
    public bool IsRead { get; set; }
}
