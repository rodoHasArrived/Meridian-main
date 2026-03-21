using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Immutable tick-by-tick trade record for an option contract.
/// </summary>
public sealed record OptionTrade : MarketEventPayload
{
    /// <summary>
    /// Gets the timestamp when the trade was executed.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the option contract symbol (OCC or provider-specific).
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the option contract specification.
    /// </summary>
    public OptionContractSpec Contract { get; }

    /// <summary>
    /// Gets the trade price (premium per share).
    /// </summary>
    public decimal Price { get; }

    /// <summary>
    /// Gets the number of contracts traded.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the side that initiated the trade (buyer or seller).
    /// </summary>
    public AggressorSide Aggressor { get; }

    /// <summary>
    /// Gets the underlying asset price at the time of this trade.
    /// </summary>
    public decimal UnderlyingPrice { get; }

    /// <summary>
    /// Gets the implied volatility at the time of the trade, if available.
    /// </summary>
    public decimal? ImpliedVolatility { get; }

    /// <summary>
    /// Gets the exchange where the trade was executed.
    /// </summary>
    public string? TradeExchange { get; }

    /// <summary>
    /// Gets trade condition codes (e.g., "AutoExecution", "IntermarketSweep").
    /// </summary>
    public string[]? Conditions { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the data source identifier.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionTrade"/> record.
    /// </summary>
    public OptionTrade(
        DateTimeOffset Timestamp,
        string Symbol,
        OptionContractSpec Contract,
        decimal Price,
        long Size,
        AggressorSide Aggressor,
        decimal UnderlyingPrice,
        decimal? ImpliedVolatility = null,
        string? TradeExchange = null,
        string[]? Conditions = null,
        long SequenceNumber = 0,
        string Source = "IB")
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        if (Price < 0)
            throw new ArgumentOutOfRangeException(nameof(Price), Price, "Price cannot be negative");

        if (Size <= 0)
            throw new ArgumentOutOfRangeException(nameof(Size), Size, "Size must be greater than 0");

        if (UnderlyingPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(UnderlyingPrice), UnderlyingPrice, "Underlying price must be greater than 0");

        this.Timestamp = Timestamp;
        this.Symbol = Symbol;
        this.Contract = Contract ?? throw new ArgumentNullException(nameof(Contract));
        this.Price = Price;
        this.Size = Size;
        this.Aggressor = Aggressor;
        this.UnderlyingPrice = UnderlyingPrice;
        this.ImpliedVolatility = ImpliedVolatility;
        this.TradeExchange = TradeExchange;
        this.Conditions = Conditions;
        this.SequenceNumber = SequenceNumber;
        this.Source = Source;
    }

    /// <summary>
    /// Gets the total premium for this trade (Price * Size * Multiplier).
    /// </summary>
    public decimal NotionalValue => Price * Size * Contract.Multiplier;

    /// <summary>
    /// Returns true if the option is in-the-money based on the underlying price.
    /// </summary>
    public bool IsInTheMoney => Contract.Right switch
    {
        OptionRight.Call => UnderlyingPrice > Contract.Strike,
        OptionRight.Put => UnderlyingPrice < Contract.Strike,
        _ => false
    };
}
