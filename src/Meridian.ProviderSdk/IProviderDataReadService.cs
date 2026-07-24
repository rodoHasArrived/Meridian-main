using System.Runtime.CompilerServices;
using Meridian.Contracts.Operations;

namespace Meridian.ProviderSdk;

/// <summary>Immutable evidence describing the origin and availability posture of provider data.</summary>
public sealed record ProviderDataProvenance(
    string ProviderId,
    string ProviderConnectionId,
    DateTimeOffset SourceTimestamp,
    DateTimeOffset ReceiptTimestamp,
    string Entitlement,
    string Feed,
    string MarketDataAvailability,
    string RequestOrSubscriptionDescriptor,
    string ProviderNativeId,
    string CorrelationId,
    string StableDeduplicationKey)
{
    /// <summary>
    /// Explicitly identifies whether the data is real, simulated, seeded, or sample. Calendar
    /// responses require this value so placeholder evidence cannot be presented as real data.
    /// </summary>
    public DataProvenance? DataProvenance { get; init; }

    /// <summary>Creates explicit placeholder provenance for callback bridges before their request context is known.</summary>
    public static ProviderDataProvenance Unattributed(DateTimeOffset sourceTimestamp) => new(
        "unknown", "unknown", sourceTimestamp, DateTimeOffset.UtcNow, "unknown", "unknown", "unknown",
        "unknown", "unknown", "unknown", "unknown")
    {
        DataProvenance = Meridian.Contracts.Operations.DataProvenance.Sample
    };
}


/// <summary>Provider-neutral contract-definition result.</summary>
public sealed record ProviderContractDetails(
    string ProviderContractId, string Symbol, string? LocalSymbol, string? SecurityType,
    string? Exchange, string? PrimaryExchange, string? Currency, string? TradingClass,
    string? Multiplier, DateOnly? Expiration, decimal? Strike, string? Right,
    string? MarketRuleIds, decimal? MinimumTick, string? LongName, string? Industry,
    string? Category, string? Subcategory, string? TimeZoneId, string? TradingHours, string? LiquidHours);

/// <summary>Provider-neutral option-chain definition returned for an underlying instrument.</summary>
public sealed record ProviderOptionChainDefinition(
    string Exchange, string UnderlyingProviderContractId, string TradingClass, string? Multiplier,
    IReadOnlyList<DateOnly> Expirations, IReadOnlyList<decimal> Strikes);

/// <summary>Provider-neutral historical-news headline.</summary>
public sealed record ProviderNewsHeadline(DateTimeOffset Timestamp, string ProviderCode, string ArticleId, string Headline);

/// <summary>Provider-neutral news-article payload.</summary>
public sealed record ProviderNewsArticle(int ArticleType, string Content);

/// <summary>Provider-neutral fundamental report payload.</summary>
public sealed record ProviderFundamentalReport(string Content);

/// <summary>Provider-neutral tick-by-tick trade, quote, or midpoint observation.</summary>
public sealed record ProviderTickByTickObservation(
    DateTimeOffset Timestamp, string Kind, decimal? Price = null, decimal? Size = null,
    decimal? Bid = null, decimal? Ask = null, decimal? BidSize = null, decimal? AskSize = null,
    string? Exchange = null, string? SpecialConditions = null);

/// <summary>Provider-neutral market-depth exchange capability.</summary>
public sealed record ProviderDepthExchangeDescription(string Exchange, string SecurityType, string ListingExchange, string Service, bool IsAggregator);

/// <summary>Provider-neutral dividend and earnings evidence supplied by a market-data callback.</summary>
public sealed record ProviderDividendEarnings(
    decimal? TrailingTwelveMonthDividend, decimal? ForwardTwelveMonthDividend, DateOnly? NextDividendDate,
    decimal? NextDividendAmount, decimal? EarningsPerShare, decimal? PriceEarningsRatio);

/// <summary>Provider-neutral evidence for a discovered option contract.</summary>
public sealed record ProviderOptionContract(string Symbol, string UnderlyingSymbol, DateOnly Expiration, decimal Strike, string Right, string Exchange, string? TradingClass, string? Multiplier, string? ProviderContractId, ProviderDataProvenance Provenance);

/// <summary>Provider-neutral scanner row, retaining the source rank and optional metrics.</summary>
public sealed record ProviderScannerResult(int Rank, string Symbol, string? Exchange, string? ProviderContractId, string? Distance, string? Benchmark, string? Projection, string? Legs, ProviderDataProvenance Provenance);

/// <summary>Provider-neutral real-time bar observation.</summary>
public sealed record ProviderRealTimeBar(DateTimeOffset Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal WeightedAveragePrice, int TradeCount, ProviderDataProvenance Provenance);

/// <summary>Provider-neutral historical tick observation.</summary>
public sealed record ProviderHistoricalTick(DateTimeOffset Timestamp, decimal Price, decimal Size, string TickKind, decimal? Bid, decimal? Ask, string? Exchange, ProviderDataProvenance Provenance);

/// <summary>Provider-neutral account or model-account P&amp;L observation.</summary>
public sealed record ProviderAccountPnl(string AccountId, string? ModelAccountId, decimal Daily, decimal Unrealized, decimal Realized, decimal? Position, decimal? Value, ProviderDataProvenance Provenance);

/// <summary>One price-band increment in a provider market rule.</summary>
public sealed record ProviderMarketRuleIncrement(decimal LowEdge, decimal Increment, ProviderDataProvenance Provenance);

/// <summary>A correlated, presentation-safe snapshot of provider data.</summary>
public sealed record ProviderDataRequestReadModel(
    int RequestId,
    string ProviderFamily,
    string Capability,
    ProviderDataRequestStatus Status,
    DateTimeOffset UpdatedAt,
    string? AccountId = null,
    string? ModelAccountId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyList<ProviderOptionContract>? OptionContracts = null,
    IReadOnlyList<ProviderScannerResult>? ScannerResults = null,
    IReadOnlyList<ProviderRealTimeBar>? RealTimeBars = null,
    IReadOnlyList<ProviderHistoricalTick>? HistoricalTicks = null,
    ProviderAccountPnl? Pnl = null,
    IReadOnlyList<ProviderMarketRuleIncrement>? MarketRuleIncrements = null,
    IReadOnlyList<ProviderContractDetails>? ContractDetails = null,
    IReadOnlyList<ProviderOptionChainDefinition>? OptionChainDefinitions = null,
    IReadOnlyList<ProviderNewsHeadline>? NewsHeadlines = null,
    ProviderNewsArticle? NewsArticle = null,
    ProviderFundamentalReport? FundamentalReport = null,
    IReadOnlyList<ProviderTickByTickObservation>? TickByTickObservations = null,
    IReadOnlyList<ProviderDepthExchangeDescription>? DepthExchanges = null,
    ProviderDividendEarnings? DividendEarnings = null);

/// <summary>Shared read-model seam for rich provider data requested by an operator workflow.</summary>
public interface IProviderDataReadService
{
    IReadOnlyList<ProviderDataRequestReadModel> GetRequests();
    IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(CancellationToken cancellationToken = default);
}

/// <summary>Presentation-safe provider news item supplied by providers that support news.</summary>
public sealed record ProviderNewsItem(
    string NewsId,
    string Headline,
    DateTimeOffset PublishedAt,
    string? Symbol,
    string? Source,
    string? Url,
    ProviderDataProvenance Provenance)
{
    public ProviderNewsItem(string newsId, string headline, DateTimeOffset publishedAt, string? symbol = null, string? source = null, string? url = null)
        : this(newsId, headline, publishedAt, symbol, source, url, ProviderDataProvenance.Unattributed(publishedAt)) { }
}

/// <summary>Presentation-safe trading-calendar event supplied by providers that support calendars.</summary>
public sealed record ProviderCalendarEvent(
    string EventId,
    string Market,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string EventType,
    string? Description,
    ProviderDataProvenance Provenance)
{
    public ProviderCalendarEvent(string eventId, string market, DateTimeOffset startsAt, DateTimeOffset endsAt, string eventType, string? description = null)
        : this(eventId, market, startsAt, endsAt, eventType, description, ProviderDataProvenance.Unattributed(startsAt)) { }
}

/// <summary>Presentation-safe instrument discovery result supplied by providers that support search.</summary>
public sealed record ProviderInstrumentDiscoveryResult(
    string InstrumentId,
    string Symbol,
    string DisplayName,
    string? Exchange,
    string? AssetClass,
    ProviderDataProvenance Provenance)
{
    public ProviderInstrumentDiscoveryResult(string instrumentId, string symbol, string displayName, string? exchange = null, string? assetClass = null)
        : this(instrumentId, symbol, displayName, exchange, assetClass, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow)) { }
}

/// <summary>
/// Provider availability and entitlement evidence. Implement this optional interface instead of
/// leaking adapter connection objects into workstation projections.
/// </summary>
public sealed record ProviderDataAvailability(
    string ProviderFamily,
    bool IsAvailable,
    string ConnectionState,
    DateTimeOffset ObservedAt,
    string? Entitlement,
    string? Detail,
    ProviderDataProvenance Provenance)
{
    public ProviderDataAvailability(string providerFamily, bool isAvailable, string connectionState, DateTimeOffset observedAt, string? entitlement = null, string? detail = null)
        : this(providerFamily, isAvailable, connectionState, observedAt, entitlement, detail, ProviderDataProvenance.Unattributed(observedAt)) { }
}

public interface IProviderDataAvailabilityReadService
{
    IReadOnlyList<ProviderDataAvailability> GetAvailability();
}

public interface IProviderNewsReadService
{
    string ProviderFamily { get; }
    IReadOnlyList<ProviderNewsItem> GetNews();
    IAsyncEnumerable<ProviderNewsItem> WatchNewsAsync(CancellationToken cancellationToken = default);
}

public interface IProviderCalendarReadService
{
    string ProviderFamily { get; }
    IReadOnlyList<ProviderCalendarEvent> GetCalendarEvents();
    IAsyncEnumerable<ProviderCalendarEvent> WatchCalendarEventsAsync(CancellationToken cancellationToken = default);
}

public interface IProviderInstrumentDiscoveryReadService
{
    string ProviderFamily { get; }
    IReadOnlyList<ProviderInstrumentDiscoveryResult> GetInstruments();
    IAsyncEnumerable<ProviderInstrumentDiscoveryResult> WatchInstrumentsAsync(CancellationToken cancellationToken = default);
}
