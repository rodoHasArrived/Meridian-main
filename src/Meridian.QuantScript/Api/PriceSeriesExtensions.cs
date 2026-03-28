namespace Meridian.QuantScript.Api;

/// <summary>
/// Extension methods on <see cref="PriceSeries"/> to produce return series.
/// </summary>
public static class PriceSeriesExtensions
{
    /// <summary>Day-over-day arithmetic returns: (Close[t] - Close[t-1]) / Close[t-1].</summary>
    public static ReturnSeries DailyReturns(this PriceSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count < 2)
            return new ReturnSeries(series.Symbol, ReturnKind.Arithmetic, []);

        var pts = new List<ReturnPoint>(series.Count - 1);
        for (var i = 1; i < series.Bars.Count; i++)
        {
            var prev = (double)series.Bars[i - 1].Close;
            var curr = (double)series.Bars[i].Close;
            if (prev == 0) continue;
            pts.Add(new ReturnPoint(series.Bars[i].Date, (curr - prev) / prev));
        }
        return new ReturnSeries(series.Symbol, ReturnKind.Arithmetic, pts);
    }

    /// <summary>Day-over-day log returns: ln(Close[t] / Close[t-1]).</summary>
    public static ReturnSeries LogReturns(this PriceSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count < 2)
            return new ReturnSeries(series.Symbol, ReturnKind.Log, []);

        var pts = new List<ReturnPoint>(series.Count - 1);
        for (var i = 1; i < series.Bars.Count; i++)
        {
            var prev = (double)series.Bars[i - 1].Close;
            var curr = (double)series.Bars[i].Close;
            if (prev <= 0 || curr <= 0) continue;
            pts.Add(new ReturnPoint(series.Bars[i].Date, Math.Log(curr / prev)));
        }
        return new ReturnSeries(series.Symbol, ReturnKind.Log, pts);
    }

    /// <summary>Compounded cumulative return starting from 1.0.</summary>
    public static ReturnSeries CumulativeReturns(this PriceSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count < 2)
            return new ReturnSeries(series.Symbol, ReturnKind.Cumulative, []);

        double running = 1.0;
        var pts = new List<ReturnPoint>(series.Count - 1);
        for (var i = 1; i < series.Bars.Count; i++)
        {
            var prev = (double)series.Bars[i - 1].Close;
            var curr = (double)series.Bars[i].Close;
            if (prev == 0) continue;
            running *= curr / prev;
            pts.Add(new ReturnPoint(series.Bars[i].Date, running - 1.0));
        }
        return new ReturnSeries(series.Symbol, ReturnKind.Cumulative, pts);
    }

    /// <summary>Non-overlapping rolling returns over <paramref name="window"/> days.</summary>
    public static ReturnSeries RollingReturns(this PriceSeries series, int window)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (window < 1) throw new ArgumentOutOfRangeException(nameof(window), "Window must be >= 1");
        if (series.Count <= window)
            return new ReturnSeries(series.Symbol, ReturnKind.Rolling, []);

        var pts = new List<ReturnPoint>();
        for (var i = window; i < series.Bars.Count; i += window)
        {
            var start = (double)series.Bars[i - window].Close;
            var end = (double)series.Bars[i].Close;
            if (start == 0) continue;
            pts.Add(new ReturnPoint(series.Bars[i].Date, (end - start) / start));
        }
        return new ReturnSeries(series.Symbol, ReturnKind.Rolling, pts);
    }
}
