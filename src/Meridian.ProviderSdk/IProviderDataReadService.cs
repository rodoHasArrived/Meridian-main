using System.Runtime.CompilerServices;

namespace Meridian.ProviderSdk;

/// <summary>Provider-neutral lifecycle state for a correlated data request.</summary>
public enum ProviderDataRequestStatus
{
    Requested,
    Streaming,
    Completed,
    Cancelled,
    TimedOut,
    Rejected,
    Failed
}

/// <summary>IB's reported market-data availability; unknown is never treated as live data.</summary>
public enum IBMarketDataAvailability { Unknown = 0, Live = 1, Frozen = 2, Delayed = 3, DelayedFrozen = 4 }

/// <summary>Complete, entitlement-aware lineage for an Interactive Brokers request or subscription.</summary>
public sealed record IBDataLineage(
    int RequestId, string Service, string Symbol, string? Exchange, string? MarketRuleIds,
    string? MinimumIncrements, string? Subscription, IBMarketDataAvailability Availability,
    bool IsDelayed, string Status, DateTimeOffset ObservedAt);

/// <summary>Provider-neutral evidence for a discovered option contract.</summary>
public sealed record ProviderOptionContract(
    string Symbol,
    string UnderlyingSymbol,
    DateOnly Expiration,
    decimal Strike,
    string Right,
    string Exchange,
    string? TradingClass = null,
    string? Multiplier = null,
    string? ProviderContractId = null);

/// <summary>Provider-neutral scanner row, retaining the source rank and optional metrics.</summary>
public sealed record ProviderScannerResult(
    int Rank,
    string Symbol,
    string? Exchange,
    string? ProviderContractId,
    string? Distance,
    string? Benchmark,
    string? Projection,
    string? Legs);

/// <summary>Provider-neutral real-time bar observation.</summary>
public sealed record ProviderRealTimeBar(DateTimeOffset Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal WeightedAveragePrice, int TradeCount);

/// <summary>Provider-neutral historical tick observation.</summary>
public sealed record ProviderHistoricalTick(DateTimeOffset Timestamp, decimal Price, decimal Size, string TickKind, decimal? Bid = null, decimal? Ask = null, string? Exchange = null);

/// <summary>Provider-neutral account or model-account P&amp;L observation.</summary>
public sealed record ProviderAccountPnl(string AccountId, string? ModelAccountId, decimal Daily, decimal Unrealized, decimal Realized, decimal? Position = null, decimal? Value = null);

/// <summary>One price-band increment in a provider market rule.</summary>
public sealed record ProviderMarketRuleIncrement(decimal LowEdge, decimal Increment);

/// <summary>
/// A correlated, presentation-safe snapshot of provider data. It deliberately contains no vendor
/// transport objects so browser and desktop workstation services can consume the same seam.
/// </summary>
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
    IBDataLineage? Lineage = null);

/// <summary>
/// Storage-facing, normalized provider result. Snapshot identities are stable across retries and
/// restarts; <see cref="NormalizedPayload"/> is the portable serialized read model and lineage is
/// retained separately so consumers need not recover it from an opaque payload.
/// </summary>
public sealed record IBDataResult(
    string ResultIdentity,
    string ProviderFamily,
    string Capability,
    string RequestIdentity,
    string? SubscriptionIdentity,
    string? Symbol,
    string? AccountId,
    DateTimeOffset CapturedAt,
    ProviderDataRequestStatus LifecycleStatus,
    string NormalizedPayload,
    IBDataLineage Lineage);

/// <summary>Bounded filter for durable IB materialized results.</summary>
public sealed record IBDataResultQuery(
    string? Capability = null,
    string? RequestIdentity = null,
    string? Symbol = null,
    string? AccountId = null,
    DateTimeOffset? CapturedFrom = null,
    DateTimeOffset? CapturedTo = null,
    int Limit = 500);

/// <summary>Durable read/write seam for materialized IB observations and snapshots.</summary>
public interface IIBDataResultStore
{
    ValueTask UpsertAsync(IBDataResult result, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<IBDataResult>> QueryAsync(IBDataResultQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Shared read-model seam for rich provider data requested by an operator workflow.</summary>
public interface IProviderDataReadService
{
    IReadOnlyList<ProviderDataRequestReadModel> GetRequests();

    IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(CancellationToken cancellationToken = default);
}
