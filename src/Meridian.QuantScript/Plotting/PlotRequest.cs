using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Immutable description of a single chart produced by a script run.
/// </summary>
public sealed record PlotRequest(
    string Title,
    PlotType Type,
    /// <summary>Primary data series (Line, CumulativeReturn, Drawdown, Bar, Scatter, Histogram).</summary>
    IReadOnlyList<(DateOnly Date, double Value)>? Series = null,
    /// <summary>Multiple named series for overlay line charts.</summary>
    IReadOnlyList<(string Label, IReadOnlyList<(DateOnly Date, double Value)> Values)>? MultiSeries = null,
    /// <summary>OHLCV data for Candlestick charts.</summary>
    IReadOnlyList<PriceBar>? Candlestick = null,
    /// <summary>Row-major 2D data for Heatmap.</summary>
    double[][]? HeatmapData = null,
    string[]? HeatmapLabels = null);
