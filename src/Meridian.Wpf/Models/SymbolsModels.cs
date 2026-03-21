using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Symbol view model for the symbols page list and edit form.
/// </summary>
public sealed class SymbolViewModel
{
    public bool IsSelected { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public bool SubscribeTrades { get; set; }
    public bool SubscribeDepth { get; set; }
    public int DepthLevels { get; set; } = 10;
    public string Exchange { get; set; } = "SMART";
    public string? LocalSymbol { get; set; }
    public string SecurityType { get; set; } = "STK";
    public decimal? Strike { get; set; }
    public string? Right { get; set; }
    public string? LastTradeDateOrContractMonth { get; set; }
    public string? OptionStyle { get; set; }
    public int? Multiplier { get; set; }

    public string TradesText => SubscribeTrades ? "On" : "Off";
    public string DepthText => SubscribeDepth ? "On" : "Off";
    public string StatusText => SubscribeTrades || SubscribeDepth ? "Active" : "Inactive";

    public SolidColorBrush TradesStatusColor => SubscribeTrades
        ? new SolidColorBrush(Color.FromRgb(63, 185, 80))
        : new SolidColorBrush(Color.FromRgb(139, 148, 158));

    public SolidColorBrush DepthStatusColor => SubscribeDepth
        ? new SolidColorBrush(Color.FromRgb(63, 185, 80))
        : new SolidColorBrush(Color.FromRgb(139, 148, 158));

    public SolidColorBrush StatusBackground => SubscribeTrades || SubscribeDepth
        ? new SolidColorBrush(Color.FromArgb(40, 63, 185, 80))
        : new SolidColorBrush(Color.FromArgb(40, 139, 148, 158));
}

/// <summary>
/// Display model for a watchlist entry in the watchlists sidebar.
/// </summary>
public sealed class WatchlistInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SymbolCount { get; set; } = string.Empty;
    public string? Color { get; set; } = "#58A6FF";
    public bool IsPinned { get; set; }

    public SolidColorBrush ColorBrush
    {
        get
        {
            if (string.IsNullOrEmpty(Color))
                return new SolidColorBrush(Colors.Gray);
            try
            {
                var wc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Color);
                return new SolidColorBrush(wc);
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }
    }
}
