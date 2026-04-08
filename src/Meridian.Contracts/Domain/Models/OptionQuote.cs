using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Best bid/offer quote for an option contract, including greeks and implied volatility.
/// </summary>
public sealed record OptionQuote : MarketEventPayload
{
    /// <summary>
    /// Gets the timestamp of this quote.
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
    /// Gets the best bid price.
    /// </summary>
    public decimal BidPrice { get; }

    /// <summary>
    /// Gets the bid size in contracts.
    /// </summary>
    public long BidSize { get; }

    /// <summary>
    /// Gets the best ask price.
    /// </summary>
    public decimal AskPrice { get; }

    /// <summary>
    /// Gets the ask size in contracts.
    /// </summary>
    public long AskSize { get; }

    /// <summary>
    /// Gets the last trade price, if available.
    /// </summary>
    public decimal? LastPrice { get; }

    /// <summary>
    /// Gets the underlying asset price at the time of this quote.
    /// A value of <c>0</c> means the underlying price was not available from the data source
    /// (e.g., Alpaca's option snapshot endpoint does not include a spot price field).
    /// Computed properties such as <see cref="IsInTheMoney"/> and <see cref="Moneyness"/>
    /// return neutral values when this is <c>0</c>.
    /// </summary>
    public decimal UnderlyingPrice { get; }

    /// <summary>
    /// Gets the implied volatility as a decimal (e.g., 0.25 = 25%).
    /// </summary>
    public decimal? ImpliedVolatility { get; }

    /// <summary>
    /// Gets the delta of the option at the time of this quote.
    /// </summary>
    public decimal? Delta { get; }

    /// <summary>
    /// Gets the gamma of the option at the time of this quote.
    /// </summary>
    public decimal? Gamma { get; }

    /// <summary>
    /// Gets the theta of the option at the time of this quote.
    /// </summary>
    public decimal? Theta { get; }

    /// <summary>
    /// Gets the vega of the option at the time of this quote.
    /// </summary>
    public decimal? Vega { get; }

    /// <summary>
    /// Gets the current open interest for this contract.
    /// </summary>
    public long? OpenInterest { get; }

    /// <summary>
    /// Gets the current day's trading volume for this contract.
    /// </summary>
    public long? Volume { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the data source identifier.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionQuote"/> record.
    /// </summary>
    public OptionQuote(
        DateTimeOffset Timestamp,
        string Symbol,
        OptionContractSpec Contract,
        decimal BidPrice,
        long BidSize,
        decimal AskPrice,
        long AskSize,
        decimal UnderlyingPrice,
        decimal? LastPrice = null,
        decimal? ImpliedVolatility = null,
        decimal? Delta = null,
        decimal? Gamma = null,
        decimal? Theta = null,
        decimal? Vega = null,
        long? OpenInterest = null,
        long? Volume = null,
        long SequenceNumber = 0,
        string Source = "IB")
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        if (BidPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(BidPrice), BidPrice, "Bid price cannot be negative");

        if (AskPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(AskPrice), AskPrice, "Ask price cannot be negative");

        if (UnderlyingPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(UnderlyingPrice), UnderlyingPrice, "Underlying price must be non-negative (use 0 when not available)");

        this.Timestamp = Timestamp;
        this.Symbol = Symbol;
        this.Contract = Contract ?? throw new ArgumentNullException(nameof(Contract));
        this.BidPrice = BidPrice;
        this.BidSize = BidSize;
        this.AskPrice = AskPrice;
        this.AskSize = AskSize;
        this.UnderlyingPrice = UnderlyingPrice;
        this.LastPrice = LastPrice;
        this.ImpliedVolatility = ImpliedVolatility;
        this.Delta = Delta;
        this.Gamma = Gamma;
        this.Theta = Theta;
        this.Vega = Vega;
        this.OpenInterest = OpenInterest;
        this.Volume = Volume;
        this.SequenceNumber = SequenceNumber;
        this.Source = Source;
    }

    /// <summary>
    /// Gets the bid-ask spread.
    /// </summary>
    public decimal Spread => AskPrice - BidPrice;

    /// <summary>
    /// Gets the mid-price (average of bid and ask).
    /// Returns null if either bid or ask is zero.
    /// </summary>
    public decimal? MidPrice => BidPrice > 0 && AskPrice > 0 && BidPrice <= AskPrice
        ? (BidPrice + AskPrice) / 2m
        : null;

    /// <summary>
    /// Gets the notional value per contract (mid-price * multiplier).
    /// </summary>
    public decimal? NotionalValue => MidPrice.HasValue
        ? MidPrice.Value * Contract.Multiplier
        : null;

    /// <summary>
    /// Returns true if the option is in-the-money based on the underlying price.
    /// Returns false when <see cref="UnderlyingPrice"/> is 0 (underlying price not available).
    /// </summary>
    public bool IsInTheMoney => UnderlyingPrice > 0 && Contract.Right switch
    {
        OptionRight.Call => UnderlyingPrice > Contract.Strike,
        OptionRight.Put => UnderlyingPrice < Contract.Strike,
        _ => false
    };

    /// <summary>
    /// Gets the moneyness ratio (underlying / strike).
    /// Values &gt; 1 indicate in-the-money for calls; &lt; 1 for puts.
    /// Returns 0 when <see cref="UnderlyingPrice"/> is 0 (underlying price not available).
    /// </summary>
    public decimal Moneyness => Contract.Strike > 0 && UnderlyingPrice > 0 ? UnderlyingPrice / Contract.Strike : 0m;
}
