using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Open interest update for an option contract.
/// Open interest represents the total number of outstanding (non-closed) contracts.
/// Typically updated once per day after settlement.
/// </summary>
public sealed record OpenInterestUpdate : MarketEventPayload
{
    /// <summary>
    /// Gets the timestamp of this update (usually end-of-day settlement time).
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the option contract symbol.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the option contract specification.
    /// </summary>
    public OptionContractSpec Contract { get; }

    /// <summary>
    /// Gets the current open interest (total outstanding contracts).
    /// </summary>
    public long OpenInterest { get; }

    /// <summary>
    /// Gets the previous day's open interest, if available.
    /// </summary>
    public long? PreviousOpenInterest { get; }

    /// <summary>
    /// Gets the day's trading volume for this contract.
    /// </summary>
    public long Volume { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the data source identifier.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenInterestUpdate"/> record.
    /// </summary>
    public OpenInterestUpdate(
        DateTimeOffset Timestamp,
        string Symbol,
        OptionContractSpec Contract,
        long OpenInterest,
        long Volume,
        long? PreviousOpenInterest = null,
        long SequenceNumber = 0,
        string Source = "IB")
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        if (OpenInterest < 0)
            throw new ArgumentOutOfRangeException(nameof(OpenInterest), OpenInterest, "Open interest cannot be negative");

        if (Volume < 0)
            throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "Volume cannot be negative");

        this.Timestamp = Timestamp;
        this.Symbol = Symbol;
        this.Contract = Contract ?? throw new ArgumentNullException(nameof(Contract));
        this.OpenInterest = OpenInterest;
        this.PreviousOpenInterest = PreviousOpenInterest;
        this.Volume = Volume;
        this.SequenceNumber = SequenceNumber;
        this.Source = Source;
    }

    /// <summary>
    /// Gets the change in open interest from previous day.
    /// Positive indicates new positions opened; negative indicates positions closed.
    /// </summary>
    public long? OpenInterestChange => PreviousOpenInterest.HasValue
        ? OpenInterest - PreviousOpenInterest.Value
        : null;

    /// <summary>
    /// Gets the volume-to-open-interest ratio.
    /// High ratios may indicate unusual activity or hedging flows.
    /// </summary>
    public decimal VolumeToOpenInterestRatio => OpenInterest > 0
        ? (decimal)Volume / OpenInterest
        : 0m;
}
