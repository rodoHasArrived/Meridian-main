using System;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Model for live data events in the feed.
/// </summary>
public sealed class LiveDataEventModel
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Event time used for ordering and display. When the payload omits a timestamp this falls
    /// back to receipt time so ordering still works, which is why it must not be read as an
    /// observation time without checking <see cref="HasObservedTimestamp"/> first.
    /// </summary>
    public DateTime RawTimestamp { get; set; }

    /// <summary>
    /// True only when the payload actually carried a timestamp. Distinguishes an observed market
    /// time from the receipt-time fallback, so surfaces that claim to show when the market was
    /// observed can render "unknown" instead of the local clock.
    /// </summary>
    public bool HasObservedTimestamp { get; set; }

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
