using System.Runtime.CompilerServices;

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
    /// <summary>Creates explicit placeholder provenance for callback bridges before their request context is known.</summary>
    public static ProviderDataProvenance Unattributed(DateTimeOffset sourceTimestamp) => new(
        "unknown", "unknown", sourceTimestamp, DateTimeOffset.UtcNow, "unknown", "unknown", "unknown",
        "unknown", "unknown", "unknown", "unknown");
}

/// <summary>Provider-neutral lifecycle state for a correlated data request.</summary>
public enum ProviderDataRequestStatus { Requested, Streaming, Completed, Cancelled, TimedOut, Rejected, Failed }

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
    int RequestId, string ProviderFamily, string Capability, ProviderDataRequestStatus Status, DateTimeOffset UpdatedAt,
    ProviderDataProvenance Provenance, string? AccountId = null, string? ModelAccountId = null, string? ErrorCode = null,
    string? ErrorMessage = null, IReadOnlyList<ProviderOptionContract>? OptionContracts = null,
    IReadOnlyList<ProviderScannerResult>? ScannerResults = null, IReadOnlyList<ProviderRealTimeBar>? RealTimeBars = null,
    IReadOnlyList<ProviderHistoricalTick>? HistoricalTicks = null, ProviderAccountPnl? Pnl = null,
    IReadOnlyList<ProviderMarketRuleIncrement>? MarketRuleIncrements = null);

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
