using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Historical auction data from Alpaca Markets API.
/// Contains opening and closing auction information for a trading session.
/// Useful for VWAP calculations and understanding market open/close dynamics.
/// </summary>
public sealed record HistoricalAuction : MarketEventPayload
{
    /// <summary>
    /// Gets the ticker symbol for the security.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the trading session date.
    /// </summary>
    public DateOnly SessionDate { get; }

    /// <summary>
    /// Gets the collection of opening auction prices.
    /// </summary>
    public IReadOnlyList<AuctionPrice> OpeningAuctions { get; }

    /// <summary>
    /// Gets the collection of closing auction prices.
    /// </summary>
    public IReadOnlyList<AuctionPrice> ClosingAuctions { get; }

    /// <summary>
    /// Gets the data source identifier (e.g., "alpaca").
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoricalAuction"/> record.
    /// </summary>
    /// <param name="Symbol">The ticker symbol.</param>
    /// <param name="SessionDate">The trading session date.</param>
    /// <param name="OpeningAuctions">The opening auction prices.</param>
    /// <param name="ClosingAuctions">The closing auction prices.</param>
    /// <param name="Source">The data source identifier.</param>
    /// <param name="SequenceNumber">The sequence number for ordering.</param>
    public HistoricalAuction(
        string Symbol,
        DateOnly SessionDate,
        IReadOnlyList<AuctionPrice>? OpeningAuctions,
        IReadOnlyList<AuctionPrice>? ClosingAuctions,
        string Source = "alpaca",
        long SequenceNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        this.Symbol = Symbol;
        this.SessionDate = SessionDate;
        this.OpeningAuctions = OpeningAuctions ?? Array.Empty<AuctionPrice>();
        this.ClosingAuctions = ClosingAuctions ?? Array.Empty<AuctionPrice>();
        this.Source = Source;
        this.SequenceNumber = SequenceNumber;
    }

    /// <summary>
    /// Gets the primary opening auction price (first valid opening auction).
    /// </summary>
    public decimal? PrimaryOpenPrice => OpeningAuctions.FirstOrDefault()?.Price;

    /// <summary>
    /// Gets the primary opening auction volume.
    /// </summary>
    public long? PrimaryOpenVolume => OpeningAuctions.FirstOrDefault()?.Size;

    /// <summary>
    /// Gets the primary closing auction price (first valid closing auction).
    /// </summary>
    public decimal? PrimaryClosePrice => ClosingAuctions.FirstOrDefault()?.Price;

    /// <summary>
    /// Gets the primary closing auction volume.
    /// </summary>
    public long? PrimaryCloseVolume => ClosingAuctions.FirstOrDefault()?.Size;

    /// <summary>
    /// Total volume from all opening auctions.
    /// </summary>
    public long TotalOpeningVolume => OpeningAuctions.Sum(a => a.Size);

    /// <summary>
    /// Total volume from all closing auctions.
    /// </summary>
    public long TotalClosingVolume => ClosingAuctions.Sum(a => a.Size);
}

/// <summary>
/// Individual auction price record within an opening or closing auction.
/// </summary>
public sealed record AuctionPrice
{
    /// <summary>
    /// Gets the timestamp of the auction price.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the auction price.
    /// </summary>
    public decimal Price { get; }

    /// <summary>
    /// Gets the auction size (number of shares).
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the exchange code where the auction occurred.
    /// </summary>
    public string? Exchange { get; }

    /// <summary>
    /// Gets the auction condition code.
    /// </summary>
    public string? Condition { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuctionPrice"/> record.
    /// </summary>
    /// <param name="Timestamp">The timestamp of the auction.</param>
    /// <param name="Price">The auction price.</param>
    /// <param name="Size">The auction size.</param>
    /// <param name="Exchange">The exchange code.</param>
    /// <param name="Condition">The auction condition.</param>
    public AuctionPrice(
        DateTimeOffset Timestamp,
        decimal Price,
        long Size,
        string? Exchange = null,
        string? Condition = null)
    {
        if (Price <= 0)
            throw new ArgumentOutOfRangeException(nameof(Price), "Price must be greater than zero.");

        if (Size < 0)
            throw new ArgumentOutOfRangeException(nameof(Size), "Size cannot be negative.");

        this.Timestamp = Timestamp;
        this.Price = Price;
        this.Size = Size;
        this.Exchange = Exchange;
        this.Condition = Condition;
    }

    /// <summary>
    /// Notional value of the auction (Price * Size).
    /// </summary>
    public decimal NotionalValue => Price * Size;
}
