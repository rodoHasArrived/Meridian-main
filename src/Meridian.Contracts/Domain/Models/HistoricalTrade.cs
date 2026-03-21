using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Historical trade data from Alpaca Markets API.
/// Represents a single executed trade at a specific exchange.
/// </summary>
public sealed record HistoricalTrade : MarketEventPayload
{
    /// <summary>
    /// Gets the ticker symbol for the security.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the timestamp when the trade was executed.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the exchange code where the trade occurred.
    /// </summary>
    public string Exchange { get; }

    /// <summary>
    /// Gets the execution price of the trade.
    /// </summary>
    public decimal Price { get; }

    /// <summary>
    /// Gets the number of shares traded.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the unique identifier for this trade.
    /// </summary>
    public string TradeId { get; }

    /// <summary>
    /// Gets the trade condition codes.
    /// </summary>
    public string[]? Conditions { get; }

    /// <summary>
    /// Gets the tape identifier (A, B, or C).
    /// </summary>
    public string? Tape { get; }

    /// <summary>
    /// Gets the data source identifier (e.g., "alpaca").
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoricalTrade"/> record.
    /// </summary>
    /// <param name="Symbol">The ticker symbol.</param>
    /// <param name="Timestamp">The trade timestamp.</param>
    /// <param name="Exchange">The exchange code.</param>
    /// <param name="Price">The execution price.</param>
    /// <param name="Size">The trade size.</param>
    /// <param name="TradeId">The unique trade identifier.</param>
    /// <param name="Conditions">The trade conditions.</param>
    /// <param name="Tape">The tape identifier.</param>
    /// <param name="Source">The data source identifier.</param>
    /// <param name="SequenceNumber">The sequence number for ordering.</param>
    public HistoricalTrade(
        string Symbol,
        DateTimeOffset Timestamp,
        string Exchange,
        decimal Price,
        long Size,
        string TradeId,
        string[]? Conditions = null,
        string? Tape = null,
        string Source = "alpaca",
        long SequenceNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        if (Price <= 0)
            throw new ArgumentOutOfRangeException(nameof(Price), "Price must be greater than zero.");

        if (Size <= 0)
            throw new ArgumentOutOfRangeException(nameof(Size), "Size must be greater than zero.");

        if (string.IsNullOrWhiteSpace(TradeId))
            throw new ArgumentException("Trade ID is required", nameof(TradeId));

        this.Symbol = Symbol;
        this.Timestamp = Timestamp;
        this.Exchange = Exchange;
        this.Price = Price;
        this.Size = Size;
        this.TradeId = TradeId;
        this.Conditions = Conditions;
        this.Tape = Tape;
        this.Source = Source;
        this.SequenceNumber = SequenceNumber;
    }

    /// <summary>
    /// Notional value of the trade (Price * Size).
    /// </summary>
    public decimal NotionalValue => Price * Size;
}
