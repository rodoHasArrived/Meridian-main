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
    string? Symbol = null,
    string? Source = null,
    string? Url = null,
    ProviderDataProvenance? Provenance = null);

/// <summary>Presentation-safe trading-calendar event supplied by providers that support calendars.</summary>
public sealed record ProviderCalendarEvent(
    string EventId,
    string Market,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string EventType,
    string? Description = null,
    ProviderDataProvenance? Provenance = null);

/// <summary>Presentation-safe instrument discovery result supplied by providers that support search.</summary>
public sealed record ProviderInstrumentDiscoveryResult(
    string InstrumentId,
    string Symbol,
    string DisplayName,
    string? Exchange = null,
    string? AssetClass = null,
    ProviderDataProvenance? Provenance = null);

/// <summary>
/// Provider availability and entitlement evidence. Implement this optional interface instead of
/// leaking adapter connection objects into workstation projections.
/// </summary>
public sealed record ProviderDataAvailability(
    string ProviderFamily,
    bool IsAvailable,
    string ConnectionState,
    DateTimeOffset ObservedAt,
    string? Entitlement = null,
    string? Detail = null);

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
