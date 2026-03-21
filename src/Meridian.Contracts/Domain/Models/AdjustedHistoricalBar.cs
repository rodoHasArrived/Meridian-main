using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Extended historical bar with adjustment factors and corporate action data.
/// </summary>
public sealed record AdjustedHistoricalBar(
    string Symbol,
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    string Source = "unknown",
    long SequenceNumber = 0,
    decimal? AdjustedOpen = null,
    decimal? AdjustedHigh = null,
    decimal? AdjustedLow = null,
    decimal? AdjustedClose = null,
    long? AdjustedVolume = null,
    decimal? SplitFactor = null,
    decimal? DividendAmount = null
) : MarketEventPayload
{
    /// <summary>
    /// Convert to standard HistoricalBar (uses adjusted values if available).
    /// When using adjusted values, ensures OHLC relationships are maintained by clamping
    /// values to valid ranges. This handles rounding errors in split/dividend adjustments.
    /// </summary>
    public HistoricalBar ToHistoricalBar(bool preferAdjusted = true)
    {
        if (preferAdjusted && AdjustedClose.HasValue)
        {
            // Use adjusted values, but ensure they maintain valid OHLC relationships
            var adjOpen = AdjustedOpen ?? Open;
            var adjHigh = AdjustedHigh ?? High;
            var adjLow = AdjustedLow ?? Low;
            var adjClose = AdjustedClose ?? Close;

            // Clamp values to ensure valid OHLC relationships
            // High must be >= max(Open, Close, Low)
            // Low must be <= min(Open, Close, High)
            var maxPrice = Math.Max(Math.Max(adjOpen, adjClose), adjLow);
            var minPrice = Math.Min(Math.Min(adjOpen, adjClose), adjHigh);

            if (adjHigh < maxPrice)
                adjHigh = maxPrice;

            if (adjLow > minPrice)
                adjLow = minPrice;

            // Final clamp to ensure Open and Close are within [Low, High]
            adjOpen = Math.Max(adjLow, Math.Min(adjHigh, adjOpen));
            adjClose = Math.Max(adjLow, Math.Min(adjHigh, adjClose));

            return new HistoricalBar(
                Symbol,
                SessionDate,
                adjOpen,
                adjHigh,
                adjLow,
                adjClose,
                AdjustedVolume ?? Volume,
                Source,
                SequenceNumber
            );
        }

        return new HistoricalBar(Symbol, SessionDate, Open, High, Low, Close, Volume, Source, SequenceNumber);
    }
}
