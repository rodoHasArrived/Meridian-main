using System.Text.Json.Serialization;

namespace Meridian.Contracts.Domain;

/// <summary>
/// Trade data transfer object.
/// </summary>
public sealed class TradeDto
{
    /// <summary>
    /// Gets or sets the trade timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the trade symbol.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trade price.
    /// </summary>
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    /// <summary>
    /// Gets or sets the trade size.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the aggressor side indicator.
    /// </summary>
    [JsonPropertyName("aggressor")]
    public string Aggressor { get; set; } = "Unknown";

    /// <summary>
    /// Gets or sets the sequence number associated with the trade.
    /// </summary>
    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; set; }

    /// <summary>
    /// Gets or sets the stream identifier for the trade.
    /// </summary>
    [JsonPropertyName("streamId")]
    public string? StreamId { get; set; }

    /// <summary>
    /// Gets or sets the venue for the trade.
    /// </summary>
    [JsonPropertyName("venue")]
    public string? Venue { get; set; }

    /// <summary>
    /// Gets or sets the provider-specific trade identifier.
    /// </summary>
    [JsonPropertyName("tradeId")]
    public string? TradeId { get; set; }

    /// <summary>
    /// Gets or sets any trade conditions supplied by the provider.
    /// </summary>
    [JsonPropertyName("conditions")]
    public string[]? Conditions { get; set; }
}

/// <summary>
/// Quote (BBO) data transfer object.
/// </summary>
public sealed class QuoteDto
{
    /// <summary>
    /// Gets or sets the quote timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the quoted symbol.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bid price.
    /// </summary>
    [JsonPropertyName("bidPrice")]
    public decimal BidPrice { get; set; }

    /// <summary>
    /// Gets or sets the bid size.
    /// </summary>
    [JsonPropertyName("bidSize")]
    public long BidSize { get; set; }

    /// <summary>
    /// Gets or sets the ask price.
    /// </summary>
    [JsonPropertyName("askPrice")]
    public decimal AskPrice { get; set; }

    /// <summary>
    /// Gets or sets the ask size.
    /// </summary>
    [JsonPropertyName("askSize")]
    public long AskSize { get; set; }

    /// <summary>
    /// Gets or sets the derived mid price, if available.
    /// </summary>
    [JsonPropertyName("midPrice")]
    public decimal? MidPrice { get; set; }

    /// <summary>
    /// Gets or sets the quoted spread, if available.
    /// </summary>
    [JsonPropertyName("spread")]
    public decimal? Spread { get; set; }

    /// <summary>
    /// Gets or sets the sequence number associated with the quote.
    /// </summary>
    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; set; }

    /// <summary>
    /// Gets or sets the stream identifier for the quote.
    /// </summary>
    [JsonPropertyName("streamId")]
    public string? StreamId { get; set; }

    /// <summary>
    /// Gets or sets the venue for the quote.
    /// </summary>
    [JsonPropertyName("venue")]
    public string? Venue { get; set; }
}

/// <summary>
/// Order book level data transfer object.
/// </summary>
public sealed class OrderBookLevelDto
{
    /// <summary>
    /// Gets or sets the order book side.
    /// </summary>
    [JsonPropertyName("side")]
    public string Side { get; set; } = "Bid";

    /// <summary>
    /// Gets or sets the depth level.
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the price at this level.
    /// </summary>
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    /// <summary>
    /// Gets or sets the size at this level.
    /// </summary>
    [JsonPropertyName("size")]
    public decimal Size { get; set; }

    /// <summary>
    /// Gets or sets the market maker identifier, if available.
    /// </summary>
    [JsonPropertyName("marketMaker")]
    public string? MarketMaker { get; set; }

    /// <summary>
    /// Gets or sets the order count at this level, if available.
    /// </summary>
    [JsonPropertyName("orderCount")]
    public int? OrderCount { get; set; }
}

/// <summary>
/// Order book snapshot data transfer object.
/// </summary>
public sealed class OrderBookSnapshotDto
{
    /// <summary>
    /// Gets or sets the snapshot timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the symbol for the snapshot.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bid levels for the order book.
    /// </summary>
    [JsonPropertyName("bids")]
    public OrderBookLevelDto[] Bids { get; set; } = Array.Empty<OrderBookLevelDto>();

    /// <summary>
    /// Gets or sets the ask levels for the order book.
    /// </summary>
    [JsonPropertyName("asks")]
    public OrderBookLevelDto[] Asks { get; set; } = Array.Empty<OrderBookLevelDto>();

    /// <summary>
    /// Gets or sets the mid price at the snapshot time.
    /// </summary>
    [JsonPropertyName("midPrice")]
    public decimal? MidPrice { get; set; }

    /// <summary>
    /// Gets or sets the micro price at the snapshot time.
    /// </summary>
    [JsonPropertyName("microPrice")]
    public decimal? MicroPrice { get; set; }

    /// <summary>
    /// Gets or sets the order book imbalance, if available.
    /// </summary>
    [JsonPropertyName("imbalance")]
    public decimal? Imbalance { get; set; }

    /// <summary>
    /// Gets or sets the market state indicator.
    /// </summary>
    [JsonPropertyName("marketState")]
    public string MarketState { get; set; } = "Normal";

    /// <summary>
    /// Gets or sets the sequence number associated with the snapshot.
    /// </summary>
    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; set; }
}

/// <summary>
/// Historical bar data transfer object.
/// </summary>
public sealed class HistoricalBarDto
{
    /// <summary>
    /// Gets or sets the bar timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the symbol for the bar.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the open price.
    /// </summary>
    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    /// <summary>
    /// Gets or sets the high price.
    /// </summary>
    [JsonPropertyName("high")]
    public decimal High { get; set; }

    /// <summary>
    /// Gets or sets the low price.
    /// </summary>
    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    /// <summary>
    /// Gets or sets the close price.
    /// </summary>
    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    /// <summary>
    /// Gets or sets the traded volume.
    /// </summary>
    [JsonPropertyName("volume")]
    public long Volume { get; set; }

    /// <summary>
    /// Gets or sets the volume-weighted average price, if available.
    /// </summary>
    [JsonPropertyName("vwap")]
    public decimal? Vwap { get; set; }

    /// <summary>
    /// Gets or sets the trade count, if available.
    /// </summary>
    [JsonPropertyName("tradeCount")]
    public int? TradeCount { get; set; }

    /// <summary>
    /// Gets or sets the bar interval label.
    /// </summary>
    [JsonPropertyName("interval")]
    public string Interval { get; set; } = "Daily";

    /// <summary>
    /// Gets or sets the data source identifier.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether prices are adjusted.
    /// </summary>
    [JsonPropertyName("isAdjusted")]
    public bool IsAdjusted { get; set; }
}

/// <summary>
/// Aggressor side enumeration values.
/// </summary>
public static class AggressorSideValues
{
    /// <summary>
    /// Indicates a buy-side aggressor.
    /// </summary>
    public const string Buy = "Buy";
    /// <summary>
    /// Indicates a sell-side aggressor.
    /// </summary>
    public const string Sell = "Sell";
    /// <summary>
    /// Indicates an unknown aggressor side.
    /// </summary>
    public const string Unknown = "Unknown";
}

/// <summary>
/// Order book side enumeration values.
/// </summary>
public static class OrderBookSideValues
{
    /// <summary>
    /// Indicates the bid side of the book.
    /// </summary>
    public const string Bid = "Bid";
    /// <summary>
    /// Indicates the ask side of the book.
    /// </summary>
    public const string Ask = "Ask";
}

/// <summary>
/// Market state enumeration values.
/// </summary>
public static class MarketStateValues
{
    /// <summary>
    /// Indicates a normal trading state.
    /// </summary>
    public const string Normal = "Normal";
    /// <summary>
    /// Indicates a pre-market trading state.
    /// </summary>
    public const string PreMarket = "PreMarket";
    /// <summary>
    /// Indicates an after-hours trading state.
    /// </summary>
    public const string AfterHours = "AfterHours";
    /// <summary>
    /// Indicates the market is closed.
    /// </summary>
    public const string Closed = "Closed";
    /// <summary>
    /// Indicates trading is halted.
    /// </summary>
    public const string Halted = "Halted";
    /// <summary>
    /// Indicates the market is in an auction state.
    /// </summary>
    public const string Auction = "Auction";
}

/// <summary>
/// Bar interval enumeration values.
/// </summary>
public static class BarIntervalValues
{
    /// <summary>
    /// Indicates a one-minute bar interval.
    /// </summary>
    public const string Minute1 = "Minute1";
    /// <summary>
    /// Indicates a five-minute bar interval.
    /// </summary>
    public const string Minute5 = "Minute5";
    /// <summary>
    /// Indicates a fifteen-minute bar interval.
    /// </summary>
    public const string Minute15 = "Minute15";
    /// <summary>
    /// Indicates a thirty-minute bar interval.
    /// </summary>
    public const string Minute30 = "Minute30";
    /// <summary>
    /// Indicates a one-hour bar interval.
    /// </summary>
    public const string Hour1 = "Hour1";
    /// <summary>
    /// Indicates a four-hour bar interval.
    /// </summary>
    public const string Hour4 = "Hour4";
    /// <summary>
    /// Indicates a daily bar interval.
    /// </summary>
    public const string Daily = "Daily";
    /// <summary>
    /// Indicates a weekly bar interval.
    /// </summary>
    public const string Weekly = "Weekly";
    /// <summary>
    /// Indicates a monthly bar interval.
    /// </summary>
    public const string Monthly = "Monthly";
}

/// <summary>
/// Integrity event data transfer object.
/// </summary>
public sealed class IntegrityEventDto
{
    /// <summary>
    /// Gets or sets the event timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the symbol associated with the event.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event kind.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the severity level of the event.
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "Warning";

    /// <summary>
    /// Gets or sets the event description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the expected value for the event, if applicable.
    /// </summary>
    [JsonPropertyName("expectedValue")]
    public string? ExpectedValue { get; set; }

    /// <summary>
    /// Gets or sets the actual value observed for the event, if applicable.
    /// </summary>
    [JsonPropertyName("actualValue")]
    public string? ActualValue { get; set; }
}
