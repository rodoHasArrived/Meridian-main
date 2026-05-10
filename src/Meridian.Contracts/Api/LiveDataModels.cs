using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.Api;

/// <summary>
/// Response DTO for recent trade data for a symbol.
/// </summary>
public sealed record TradeDataResponse(
    string Symbol,
    DateTimeOffset Timestamp,
    decimal Price,
    long Size,
    string Aggressor,
    long SequenceNumber,
    string? StreamId,
    string? Venue
);

/// <summary>
/// Response DTO for the trades endpoint, wrapping a list of recent trades.
/// </summary>
public sealed record TradesResponse(
    string Symbol,
    IReadOnlyList<TradeDataResponse> Trades,
    int Count,
    DateTimeOffset Timestamp
);

/// <summary>
/// Per-symbol intraday session statistics surfaced alongside live quotes so the watchlist
/// and quote screens can render day open / high / low / change without an extra round trip.
/// </summary>
public sealed record SessionStatsDto(
    string SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Last,
    long Volume,
    decimal Vwap,
    long TradeCount,
    decimal Change,
    decimal? ChangePercent,
    DateTimeOffset FirstTradeAt,
    DateTimeOffset LastTradeAt
);

/// <summary>
/// Response DTO for a single quote snapshot.
/// </summary>
public sealed record QuoteDataResponse(
    string Symbol,
    DateTimeOffset Timestamp,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    decimal? MidPrice,
    decimal? Spread,
    long SequenceNumber,
    string? StreamId,
    string? Venue,
    SessionStatsDto? Session = null
);

/// <summary>
/// Response DTO wrapping quotes for one or more symbols.
/// </summary>
public sealed record QuotesResponse(
    string Symbol,
    QuoteDataResponse? Quote,
    DateTimeOffset Timestamp
);

/// <summary>
/// One row in the multi-symbol quotes snapshot — the latest BBO plus the last trade
/// (if any) so a watchlist can render bid / ask / last / spread for every symbol from
/// a single request. <see cref="Session"/> carries the intraday day-statistics for the
/// symbol when at least one trade has been observed since the session anchor.
/// </summary>
public sealed record QuotesSnapshotItem(
    string Symbol,
    DateTimeOffset Timestamp,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    decimal? MidPrice,
    decimal? Spread,
    decimal? LastPrice,
    long? LastSize,
    DateTimeOffset? LastTradeTimestamp,
    long SequenceNumber,
    string? StreamId,
    string? Venue,
    SessionStatsDto? Session = null
);

/// <summary>
/// Multi-symbol quotes snapshot response. Used by the watchlist screen to refresh prices for
/// every subscribed symbol from a single endpoint.
/// </summary>
public sealed record QuotesSnapshotResponse(
    DateTimeOffset Timestamp,
    int Count,
    IReadOnlyList<QuotesSnapshotItem> Quotes
);

/// <summary>
/// Response DTO for an order book level.
/// </summary>
public sealed record OrderBookLevelDto(
    string Side,
    int Level,
    decimal Price,
    decimal Size,
    string? MarketMaker
);

/// <summary>
/// Response DTO for a full order book snapshot.
/// </summary>
public sealed record OrderBookResponse(
    string Symbol,
    DateTimeOffset Timestamp,
    IReadOnlyList<OrderBookLevelDto> Bids,
    IReadOnlyList<OrderBookLevelDto> Asks,
    decimal? MidPrice,
    decimal? Imbalance,
    string MarketState,
    long SequenceNumber,
    bool IsStale,
    string? StreamId,
    string? Venue
);

/// <summary>
/// Response DTO for best bid/offer data.
/// </summary>
public sealed record BboResponse(
    string Symbol,
    DateTimeOffset Timestamp,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    decimal? MidPrice,
    decimal? Spread,
    long SequenceNumber,
    string? StreamId,
    string? Venue
);

/// <summary>
/// Response DTO for order flow statistics.
/// </summary>
public sealed record OrderFlowResponse(
    string Symbol,
    DateTimeOffset Timestamp,
    long BuyVolume,
    long SellVolume,
    long UnknownVolume,
    decimal VWAP,
    decimal Imbalance,
    int TradeCount,
    long SequenceNumber,
    string? StreamId,
    string? Venue
);

/// <summary>
/// Per-symbol health status in the live data health response.
/// </summary>
public sealed record SymbolDataHealthDto(
    string Symbol,
    bool HasQuotes,
    bool HasDepth,
    bool IsDepthStale,
    DateTimeOffset? LastQuoteTimestamp,
    DateTimeOffset? LastOrderBookTimestamp
);

/// <summary>
/// Response DTO for live data health.
/// </summary>
public sealed record LiveDataHealthResponse(
    DateTimeOffset Timestamp,
    bool CollectorsAvailable,
    int SymbolsWithQuotes,
    int SymbolsWithDepth,
    long PipelinePublished,
    long PipelineDropped,
    long PipelineConsumed,
    int PipelineQueueSize,
    double PipelineUtilization,
    IReadOnlyList<SymbolDataHealthDto> Symbols
);
