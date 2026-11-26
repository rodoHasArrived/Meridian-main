using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Historical bar with two independently declared OHLC price sets and corporate-action data.
/// The primary fields are not necessarily raw. Optional adjusted fields never prove raw availability.
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
    decimal? DividendAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [property: JsonConverter(typeof(HistoricalPriceBasisJsonConverter))]
    HistoricalPriceBasis PriceBasis = HistoricalPriceBasis.Unknown,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [property: JsonConverter(typeof(HistoricalPriceBasisJsonConverter))]
    HistoricalPriceBasis AdjustedPriceBasis = HistoricalPriceBasis.Unknown
) : MarketEventPayload
{
    /// <summary>
    /// Projects a complete available price set without synthesizing prices. The default prefers
    /// the optional adjusted set, otherwise preserves the primary set and its declared basis.
    /// Setting <paramref name="preferAdjusted"/> to false requires explicitly raw prices.
    /// Partial adjusted OHLC is rejected rather than borrowing individual primary components.
    /// </summary>
    public HistoricalBar ToHistoricalBar(bool preferAdjusted = true)
    {
        if (!preferAdjusted)
            return ToHistoricalBar(HistoricalPriceBasis.Raw);

        return HasAdjustedPrices ? CreateAdjustedBar() : CreatePrimaryBar();
    }

    /// <summary>
    /// Selects a complete price set in exactly the requested basis. This method does not reverse
    /// adjustments, derive raw prices from ratios, or silently substitute a different basis.
    /// </summary>
    public HistoricalBar ToHistoricalBar(HistoricalPriceBasis requiredBasis)
    {
        if (!Enum.IsDefined(requiredBasis) || requiredBasis == HistoricalPriceBasis.Unknown)
            throw new ArgumentOutOfRangeException(nameof(requiredBasis), "A known historical price basis is required.");

        if (PriceBasis == requiredBasis)
            return CreatePrimaryBar();

        if (HasAdjustedPrices && AdjustedPriceBasis == requiredBasis)
            return CreateAdjustedBar();

        throw new InvalidOperationException(
            $"{Source} history for {Symbol} on {SessionDate:yyyy-MM-dd} does not contain a complete {requiredBasis} OHLC set.");
    }

    private bool HasAdjustedPrices => AdjustedOpen.HasValue || AdjustedHigh.HasValue
        || AdjustedLow.HasValue || AdjustedClose.HasValue;

    private HistoricalBar CreatePrimaryBar() => new(
        Symbol, SessionDate, Open, High, Low, Close, Volume, Source, SequenceNumber,
        PriceBasis: PriceBasis);

    private HistoricalBar CreateAdjustedBar()
    {
        if (AdjustedOpen is not { } open || AdjustedHigh is not { } high
            || AdjustedLow is not { } low || AdjustedClose is not { } close)
        {
            throw new InvalidOperationException(
                $"{Source} history for {Symbol} on {SessionDate:yyyy-MM-dd} has incomplete adjusted OHLC prices.");
        }

        if (open <= 0 || high <= 0 || low <= 0 || close <= 0)
            throw new ArgumentOutOfRangeException(nameof(AdjustedOpen), "Adjusted OHLC values must be greater than zero.");

        // Preserve the existing rounding repair, but only within one complete price set.
        high = Math.Max(high, Math.Max(Math.Max(open, close), low));
        low = Math.Min(low, Math.Min(Math.Min(open, close), high));

        return new HistoricalBar(
            Symbol, SessionDate, open, high, low, close, AdjustedVolume ?? Volume, Source, SequenceNumber,
            PriceBasis: AdjustedPriceBasis);
    }
}
