namespace Meridian.QuantScript.Plotting;

/// <summary>
/// Describes a single chart to be rendered in the QuantScript Charts tab.
/// </summary>
public sealed record PlotRequest(
    string Title,
    PlotType Type,
    IReadOnlyList<(DateTime X, double Y)> Points);
