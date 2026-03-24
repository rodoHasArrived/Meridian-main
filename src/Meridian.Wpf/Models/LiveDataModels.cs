using System;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Model for live data events in the feed.
/// </summary>
public sealed class LiveDataEventModel
{
    public string Id { get; set; } = string.Empty;
    public DateTime RawTimestamp { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public decimal RawPrice { get; set; }
    public string Size { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public SolidColorBrush TypeColor { get; set; } = new(Colors.Gray);
    public SolidColorBrush PriceColor { get; set; } = new(Colors.Gray);
}
