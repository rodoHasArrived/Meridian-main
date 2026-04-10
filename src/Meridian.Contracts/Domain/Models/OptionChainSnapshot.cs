using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Point-in-time snapshot of an option chain for a single underlying and expiration.
/// Contains quotes for all tracked strikes (calls and puts).
/// </summary>
public sealed record OptionChainSnapshot : MarketEventPayload
{
    /// <summary>
    /// Gets the timestamp of this chain snapshot.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the underlying symbol (e.g., "SPX", "AAPL").
    /// </summary>
    public string UnderlyingSymbol { get; }

    /// <summary>
    /// Gets the underlying price at the time of this snapshot.
    /// </summary>
    public decimal UnderlyingPrice { get; }

    /// <summary>
    /// Gets the expiration date for this chain.
    /// </summary>
    public DateOnly Expiration { get; }

    /// <summary>
    /// Gets the instrument type for this chain (EquityOption or IndexOption).
    /// </summary>
    public InstrumentType InstrumentType { get; }

    /// <summary>
    /// Gets the available strike prices in this chain.
    /// </summary>
    public IReadOnlyList<decimal> Strikes { get; }

    /// <summary>
    /// Gets the call option quotes keyed by strike price.
    /// </summary>
    public IReadOnlyList<OptionQuote> Calls { get; }

    /// <summary>
    /// Gets the put option quotes keyed by strike price.
    /// </summary>
    public IReadOnlyList<OptionQuote> Puts { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the data source identifier.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionChainSnapshot"/> record.
    /// </summary>
    public OptionChainSnapshot(
        DateTimeOffset Timestamp,
        string UnderlyingSymbol,
        decimal UnderlyingPrice,
        DateOnly Expiration,
        IReadOnlyList<decimal> Strikes,
        IReadOnlyList<OptionQuote> Calls,
        IReadOnlyList<OptionQuote> Puts,
        InstrumentType InstrumentType = InstrumentType.EquityOption,
        long SequenceNumber = 0,
        string Source = "IB")
    {
        if (string.IsNullOrWhiteSpace(UnderlyingSymbol))
            throw new ArgumentException("Underlying symbol is required", nameof(UnderlyingSymbol));

        if (UnderlyingPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(UnderlyingPrice), UnderlyingPrice, "Underlying price must be greater than 0");

        if (InstrumentType is not (InstrumentType.EquityOption or InstrumentType.IndexOption))
            throw new ArgumentException("InstrumentType must be EquityOption or IndexOption", nameof(InstrumentType));

        this.Timestamp = Timestamp;
        this.UnderlyingSymbol = UnderlyingSymbol;
        this.UnderlyingPrice = UnderlyingPrice;
        this.Expiration = Expiration;
        this.Strikes = Strikes ?? throw new ArgumentNullException(nameof(Strikes));
        this.Calls = Calls ?? throw new ArgumentNullException(nameof(Calls));
        this.Puts = Puts ?? throw new ArgumentNullException(nameof(Puts));
        this.InstrumentType = InstrumentType;
        this.SequenceNumber = SequenceNumber;
        this.Source = Source;
    }

    /// <summary>
    /// Gets the number of calendar days until expiration from the snapshot timestamp.
    /// </summary>
    public int DaysToExpiration => Expiration.DayNumber - DateOnly.FromDateTime(Timestamp.UtcDateTime).DayNumber;

    /// <summary>
    /// Gets the total number of contracts in this chain (calls + puts).
    /// </summary>
    public int TotalContracts => Calls.Count + Puts.Count;

    /// <summary>
    /// Gets the at-the-money strike (strike closest to the underlying price).
    /// </summary>
    public decimal? AtTheMoneyStrike => Strikes.Count > 0
        ? Strikes.OrderBy(s => Math.Abs(s - UnderlyingPrice)).First()
        : null;

    /// <summary>
    /// Gets the aggregate put/call ratio by volume (if volume data is available).
    /// Returns null if no volume data is present.
    /// </summary>
    public decimal? PutCallVolumeRatio
    {
        get
        {
            var callVolume = Calls.Sum(c => c.Volume ?? 0);
            var putVolume = Puts.Sum(p => p.Volume ?? 0);
            return callVolume > 0 ? (decimal)putVolume / callVolume : null;
        }
    }

    /// <summary>
    /// Gets the aggregate put/call ratio by open interest (if OI data is available).
    /// Returns null if no open interest data is present.
    /// </summary>
    public decimal? PutCallOpenInterestRatio
    {
        get
        {
            var callOI = Calls.Sum(c => c.OpenInterest ?? 0);
            var putOI = Puts.Sum(p => p.OpenInterest ?? 0);
            return callOI > 0 ? (decimal)putOI / callOI : null;
        }
    }
}
