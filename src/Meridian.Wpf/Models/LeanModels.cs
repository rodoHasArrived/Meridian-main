using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>Backtest summary row displayed in the Recent Backtests side panel.</summary>
public sealed class BacktestDisplayItem
{
    public string AlgorithmName { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public string ReturnText { get; set; } = string.Empty;
    public Brush ReturnBrush { get; set; } = Brushes.White;
}

/// <summary>Symbol-to-Lean-ticker mapping row displayed in the Symbol Mapping panel.</summary>
public sealed class LeanSymbolMappingDisplayItem
{
    public string MdcSymbol { get; set; } = string.Empty;
    public string LeanTicker { get; set; } = string.Empty;
    public string SecurityType { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
}
