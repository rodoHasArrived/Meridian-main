using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.API;

/// <summary>
/// A time-indexed list of return observations with plotting helpers.
/// </summary>
public sealed class ReturnSeries
{
    private readonly IReadOnlyList<ReturnPoint> _points;

    internal ReturnSeries(IReadOnlyList<ReturnPoint> points)
    {
        _points = points ?? throw new ArgumentNullException(nameof(points));
    }

    /// <summary>Number of return observations.</summary>
    public int Count => _points.Count;

    /// <summary>Returns the underlying list of observations.</summary>
    public IReadOnlyList<ReturnPoint> ToList() => _points;

    /// <summary>
    /// Enqueues a line plot of the raw return series into the active <see cref="PlotQueue"/>.
    /// </summary>
    public ReturnSeries Plot(string? title = null)
    {
        var points = _points.Select(p => (p.Date, p.Value)).ToList();
        PlotQueue.Current?.Enqueue(new PlotRequest(title ?? "Returns", PlotType.Line, points));
        return this;
    }

    /// <summary>
    /// Enqueues a cumulative return line plot into the active <see cref="PlotQueue"/>.
    /// </summary>
    public ReturnSeries PlotCumulative(string? title = null)
    {
        double cumulative = 0.0;
        var points = new List<(DateTime, double)>(_points.Count);
        foreach (var p in _points)
        {
            cumulative += p.Value;
            points.Add((p.Date, cumulative));
        }
        PlotQueue.Current?.Enqueue(new PlotRequest(title ?? "Cumulative Returns", PlotType.Line, points));
        return this;
    }
}
