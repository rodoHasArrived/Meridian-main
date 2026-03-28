namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Distinguishes the visual chart type for a <see cref="PlotRequest"/>.
/// </summary>
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
