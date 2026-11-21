namespace Meridian.Backtesting.Metrics;

/// <summary>
/// Converts return figures computed in <see cref="double"/> into the <see cref="decimal"/> values
/// carried by backtest results. Compounding a short run over a year can exceed the decimal range
/// (a 25% one-day gain annualises to roughly 2e35) or reach infinity, and an unchecked cast then
/// throws <see cref="OverflowException"/> and aborts the whole metrics build. These figures are
/// reported, not traded on, so they are capped instead. The cap matches the live-run tracker.
/// </summary>
internal static class ReportedReturn
{
    /// <summary>Largest magnitude reported for a return figure.</summary>
    internal const double MaxMagnitude = 1e12;

    internal static decimal FromDouble(double value)
    {
        if (double.IsNaN(value))
            return 0m;

        return (decimal)Math.Clamp(value, -MaxMagnitude, MaxMagnitude);
    }
}
