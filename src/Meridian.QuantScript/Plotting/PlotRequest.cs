namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Describes a single chart to be rendered in the QuantScript Charts tab.
using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Plotting;

public enum PlotType
{
    Line,
    MultiLine,
    CumulativeReturn,
    Drawdown,
    Heatmap,
    Candlestick,
    Bar,
    Scatter,
    Histogram
}

/// <summary>
/// Immutable description of a single chart produced by a script.
/// </summary>
public sealed record PlotRequest(
    string Title,
    PlotType Type,
    IReadOnlyList<(DateTime X, double Y)> Points);
    /// <summary>Primary data series (Line, CumulativeReturn, Drawdown, Bar, Scatter, Histogram).</summary>
    IReadOnlyList<(DateOnly Date, double Value)>? Series = null,
    /// <summary>Multiple named series for overlay line charts.</summary>
    IReadOnlyList<(string Label, IReadOnlyList<(DateOnly Date, double Value)> Values)>? MultiSeries = null,
    /// <summary>OHLCV data for Candlestick charts.</summary>
    IReadOnlyList<PriceBar>? Candlestick = null,
    /// <summary>Row-major 2D data for Heatmap.</summary>
    double[][]? HeatmapData = null,
    string[]? HeatmapLabels = null);
