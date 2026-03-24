using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Display model for a single order book price level (bid or ask).
/// </summary>
public sealed class OrderBookDisplayLevel
{
    public decimal RawPrice { get; set; }
    public string Price { get; set; } = string.Empty;
    public int RawSize { get; set; }
    public string Size { get; set; } = string.Empty;
    public decimal RawTotal { get; set; }
    public string Total { get; set; } = string.Empty;
    public double DepthWidth { get; set; }
}

/// <summary>
/// Display model for a single recent trade entry.
/// </summary>
public sealed class RecentTradeModel
{
    public string Time { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public SolidColorBrush PriceColor { get; set; } = new(System.Windows.Media.Colors.Gray);
}
