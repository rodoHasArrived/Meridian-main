# Provider Management Architecture

**Version:** 3.8 | **Last Updated:** 2026-08-05

This document describes the provider management architecture used by Meridian. It covers provider contracts, discovery, lifecycle management, failover, health monitoring, degradation scoring, and data quality operations.

See also:

- [ADR-001: Provider Abstraction](../../archive/docs/adr/001-provider-abstraction.md)
- [ADR-004: Async Streaming Patterns](../../archive/docs/adr/004-async-streaming-patterns.md)
- [ADR-005: Attribute-Based Discovery](../../archive/docs/adr/005-attribute-based-discovery.md)
- [Provider Implementation Guide](../development/provider-implementation.md)
- [Provider Capability Matrix](../reference/provider-capability-matrix.md)
- [Provider Backfill Operations](../operators/provider-backfill-operations.md)

---

## Overview

The provider stack is built on two complementary contract systems:

1. **`IProviderMetadata`** — Unified metadata interface shared by all provider types (streaming, historical, symbol search). Provides identity, priority, capability flags, and UI metadata.
2. **`IDataSource`** — Unified base interface for data sources with lifecycle management (`InitializeAsync`, `ValidateCredentialsAsync`, `TestConnectivityAsync`), health tracking, and rate-limit state.

At runtime, provider registration and routing are handled by `DataSourceRegistry`, while resilience is managed by `StreamingFailoverService` (streaming), `CompositeHistoricalDataProvider` (historical), and monitoring services (`ConnectionHealthMonitor`, `ProviderDegradationScorer`, `ProviderLatencyService`).

---

## Layered Architecture

```text
UI / API Layer
└── ProviderManagementService / ProviderHealthService / Endpoint handlers
    └── Monitoring Layer
        ├── ConnectionHealthMonitor (heartbeat, reconnect, latency)
        ├── ProviderDegradationScorer (composite health scoring)
        └── ProviderLatencyService (percentile histograms)
            └── Runtime Layer
                ├── StreamingFailoverService + FailoverAwareMarketDataClient
                ├── CompositeHistoricalDataProvider (priority chain + rate-limit rotation)
                └── ProviderRateLimitTracker + RateLimiter
                    └── Discovery & Registration
                        ├── DataSourceRegistry (assembly scanning)
                        ├── [DataSource] attribute (ADR-005)
                        └── IProviderModule (DI registration)
                            └── Concrete Providers
                                ├── Streaming: Alpaca, Polygon, IB, NYSE, StockSharp
                                │   (base: WebSocketProviderBase)
                                ├── Historical: Alpaca, Polygon, Tiingo, Yahoo, Stooq,
                                │              Finnhub, AlphaVantage, NasdaqDataLink,
                                │              IB, StockSharp
                                │   (base: BaseHistoricalDataProvider)
                                └── Symbol Search: Alpaca, AlphaVantage,
                                                   Finnhub, Polygon, Tiingo,
                                                   TwelveData, OpenFIGI, StockSharp
                                    (base: BaseSymbolSearchProvider)
```

---

## Core Contracts

### IProviderMetadata

**Location:** `src/Meridian.ProviderSdk/IProviderMetadata.cs`

Unified metadata interface implemented by all three provider categories. Enables consistent discovery, routing, and UI presentation without provider-specific code paths.

| Property | Type | Purpose |
|----------|------|---------|
| `ProviderId` | `string` | Unique identifier (e.g., `"alpaca"`, `"polygon"`) |
| `ProviderDisplayName` | `string` | Human-readable name |
| `ProviderDescription` | `string` | Capability description |
| `ProviderPriority` | `int` | Routing priority (lower = preferred) |
| `ProviderCapabilities` | `ProviderCapabilities` | Unified capability record |
| `RequiresCredentials` | `bool` | Derived from credential fields |
| `ProviderCredentialFields` | `ProviderCredentialField[]` | Credential requirements for UI form generation |
| `ProviderNotes` | `string[]` | Informational notes |
| `ProviderWarnings` | `string[]` | Limitation warnings |
| `SupportedDataTypes` | `string[]` | Auto-derived from capabilities |

### ProviderCapabilities

**Location:** `src/Meridian.ProviderSdk/IProviderMetadata.cs`

Strongly-typed record that consolidates capabilities across all provider types. Replaces the previous bit-flag enum style with boolean properties and optional metadata.

**Capability groups:**

- **Provider type:** `SupportsStreaming`, `SupportsBackfill`, `SupportsSymbolSearch`
- **Streaming:** `SupportsRealtimeTrades`, `SupportsRealtimeQuotes`, `SupportsMarketDepth`, `MaxDepthLevels`, `MaxSymbolsPerSubscription`
- **Backfill:** `SupportsAdjustedPrices`, `SupportsIntraday`, `SupportsDividends`, `SupportsSplits`, `SupportsHistoricalQuotes`, `SupportsHistoricalTrades`, `SupportsHistoricalAuctions`, `MinBarResolution`, `SupportedBarIntervals`
- **Symbol search:** `SupportsAssetTypeFilter`, `SupportsExchangeFilter`, `SupportedAssetTypes`, `SupportedExchanges`
- **Market coverage:** `SupportedMarkets`
- **Rate limiting:** `MaxRequestsPerWindow`, `RateLimitWindow`, `MinRequestDelay`

**Factory methods:** `Streaming(...)`, `BackfillBarsOnly`, `BackfillFullFeatured`, `SymbolSearch`, `SymbolSearchFilterable(...)`, `Hybrid(...)`, `FromHistoricalCapabilities(...)`

### IDataSource

**Location:** `src/Meridian.ProviderSdk/IDataSource.cs`

Unified base interface for all data sources with lifecycle and health support.

```csharp
public interface IDataSource : IAsyncDisposable
{
    // Identity
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }

    // Classification
    DataSourceType Type { get; }        // Realtime | Historical | Hybrid
    DataSourceCategory Category { get; } // Exchange | Broker | Aggregator | Free | Premium
    int Priority { get; }

    // Capabilities
    DataSourceCapabilities Capabilities { get; }  // Bitwise flags (35+ capabilities)
    DataSourceCapabilityInfo CapabilityInfo { get; }
    IReadOnlySet<string> SupportedMarkets { get; }
    IReadOnlySet<AssetClass> SupportedAssetClasses { get; }

    // Operational State
    DataSourceHealth Health { get; }
    DataSourceStatus Status { get; }
    RateLimitState RateLimitState { get; }
    IObservable<DataSourceHealthChanged> HealthChanges { get; }

    // Lifecycle
    Task InitializeAsync(CancellationToken ct = default);
    Task<bool> ValidateCredentialsAsync(CancellationToken ct = default);
    Task<bool> TestConnectivityAsync(CancellationToken ct = default);
}
```

**`DataSourceCapabilities`** provides fine-grained bitwise flags organized in groups:

| Group | Bits | Examples |
|-------|------|---------|
| Real-time (0-9) | Trades, Quotes, Depth L1/L2/L3, Aggregate bars | `RealtimeTrades`, `RealtimeDepthL2` |
| Historical (10-19) | Daily bars, Intraday, Ticks, Adjusted, Dividends, Splits, Earnings | `HistoricalIntradayBars`, `HistoricalDividends` |
| Operational (20-29) | Backfill, Streaming, Polling, WebSocket, Batch, Symbol search | `SupportsWebSocket`, `SupportsBatchRequests` |
| Quality (30-39) | Exchange timestamps, Sequence numbers, Trade conditions, Participant IDs | `ExchangeTimestamps`, `ConsolidatedTape` |

### Provider-Category Interfaces

| Interface | Location | Extends | Purpose |
|-----------|----------|---------|---------|
| `IMarketDataClient` | `ProviderSdk/IMarketDataClient.cs` | `IProviderMetadata`, `IAsyncDisposable` | Streaming: `ConnectAsync`, `DisconnectAsync`, `SubscribeMarketDepth`, `SubscribeTrades` |
| `IHistoricalDataProvider` | `Infrastructure/Adapters/Core/IHistoricalDataProvider.cs` | `IProviderMetadata`, `IDisposable` | Backfill: `GetDailyBarsAsync`, `GetHistoricalQuotesAsync`, `GetHistoricalTradesAsync`, `GetHistoricalAuctionsAsync` |
| `IRealtimeDataSource` | `ProviderSdk/IRealtimeDataSource.cs` | `IDataSource` | Real-time with `IObservable<T>` streams for trades, quotes, depth |
| `IHistoricalDataSource` | `ProviderSdk/IHistoricalDataSource.cs` | `IDataSource` | Historical bars, intraday bars, dividends, splits |
| `ISymbolSearchProvider` | `Infrastructure/Adapters/Core/ISymbolSearchProvider.cs` | `IProviderMetadata` | Symbol lookup: `SearchAsync`, `GetDetailsAsync` |
| `IFilterableSymbolSearchProvider` | `Infrastructure/Adapters/Core/ISymbolSearchProvider.cs` | `ISymbolSearchProvider` | Adds asset type and exchange filtering |

---

## Discovery & Registration

### Attribute-based discovery (ADR-005)

The `[DataSource]` attribute marks classes for automatic discovery:

```csharp
[DataSource("alpaca", "Alpaca Markets", DataSourceType.Hybrid, DataSourceCategory.Broker, Priority = 10)]
public sealed class AlpacaDataSource : IRealtimeDataSource, IHistoricalDataSource
{
    // ...
}
```

The attribute captures: `Id`, `DisplayName`, `Type`, `Category`, `Priority`, `EnabledByDefault`, `Description`, and `ConfigSection`.

### DataSourceRegistry

**Location:** `src/Meridian.ProviderSdk/DataSourceRegistry.cs`

Central registry responsible for:

1. **Assembly scanning** — `DiscoverFromAssemblies(params Assembly[])` finds all types decorated with `[DataSource]` that implement `IDataSource`.
2. **DI registration** — `RegisterServices(IServiceCollection)` registers discovered types as singletons (both concrete type and `IDataSource`).
3. **Module registration** — `RegisterModules(IServiceCollection, params Assembly[])` discovers and executes `IProviderModule` implementations for custom registration logic.
4. **Metadata** — Exposes `Sources` as `IReadOnlyList<DataSourceMetadata>` for UI catalog generation.

### IProviderModule

**Location:** `src/Meridian.ProviderSdk/IProviderModule.cs`

Providers that require custom DI registration (e.g., `HttpClient` configuration, options binding) implement `IProviderModule`:

```csharp
public interface IProviderModule
{
    void Register(IServiceCollection services, DataSourceRegistry registry);
}
```

---

## Streaming Provider Implementations

| Provider | Class | Location | Trades | Quotes | Depth | Transport |
|----------|-------|----------|--------|--------|-------|-----------|
| Alpaca | `AlpacaMarketDataClient` | `Streaming/Alpaca/` | Yes | Yes | No | WebSocket |
| Polygon | `PolygonMarketDataClient` | `Streaming/Polygon/` | Yes | Yes | Yes | WebSocket |
| Interactive Brokers | `IBMarketDataClient` | `Streaming/InteractiveBrokers/` | Yes | Yes | Yes | TWS/Gateway |
| NYSE | `NYSEDataSource` | `Streaming/NYSE/` | Yes | Yes | L1/L2 | Hybrid (streaming + historical) |
| StockSharp | `StockSharpMarketDataClient` | `Streaming/StockSharp/` | Yes | Yes | Yes | 90+ connectors |
| IB Simulation | `IBSimulationClient` | `Streaming/InteractiveBrokers/` | N/A | N/A | N/A | Testing only |
| Failover | `FailoverAwareMarketDataClient` | `Streaming/Failover/` | Delegated | Delegated | Delegated | Composite wrapper |
| NoOp | `NoOpMarketDataClient` | `Infrastructure/` | N/A | N/A | N/A | Placeholder |

All locations relative to `src/Meridian.Infrastructure/Adapters/`.

---

## Historical Provider Implementations

| Provider | Class | Free Tier | Rate Limits | Key Capabilities |
|----------|-------|-----------|-------------|------------------|
| Alpaca | `AlpacaHistoricalDataProvider` | With account | 200/min | Bars, trades, quotes, auctions, intraday |
| Polygon | `PolygonHistoricalDataProvider` | Limited | Varies | Bars, trades, quotes, aggregates |
| Tiingo | `TiingoHistoricalDataProvider`, `TiingoSymbolSearchProvider`, `TiingoCorporateActionProvider` | Yes | 50/hour | Daily bars, adjusted prices, utilities symbol search, adjusted-EOD dividend/split extraction |
| Yahoo Finance | `YahooFinanceHistoricalDataProvider` | Yes | Unofficial | Daily bars |
| Stooq | `StooqHistoricalDataProvider` | Yes | Low | Daily bars |
| Finnhub | `FinnhubHistoricalDataProvider` | Yes | 60/min | Daily and intraday bars |
| FRED | `FredHistoricalDataProvider`, `FredSymbolSearchProvider` | Yes | 120/min | Economic series observations mapped to synthetic daily bars, credential-gated series search |
| Alpha Vantage | `AlphaVantageHistoricalDataProvider`, `AlphaVantageSymbolSearchProvider`, `AlphaVantageCorporateActionProvider` | Yes | 5/min | Daily bars, intraday bars, keyword symbol search, adjusted-daily dividend/split extraction |
| Twelve Data | `TwelveDataHistoricalDataProvider`, `TwelveDataSymbolSearchProvider`, `TwelveDataCorporateActionProvider` | Yes | 8/min | Daily bars, credential-gated `/symbol_search` discovery, paid-plan `/dividends` and `/splits` corporate actions |
| Nasdaq Data Link | `NasdaqDataLinkHistoricalDataProvider`, `NasdaqDataLinkSymbolSearchProvider`, `NasdaqDataLinkCorporateActionProvider` | Limited | Varies | Time-series datasets, credential-gated dataset-code search, adjusted dataset dividend/split extraction |
| Interactive Brokers | `IBHistoricalDataProvider` | With account | IB pacing | All types |
| StockSharp | `StockSharpHistoricalDataProvider` | With account | Varies | Multi-exchange |

All located under `src/Meridian.Infrastructure/Adapters/`.

---

## Symbol Search Provider Implementations

| Provider | Class | Filterable | Exchanges | Rate Limit |
|----------|-------|------------|-----------|------------|
| Alpaca | `AlpacaSymbolSearchProvider` | Yes | US, Crypto | 200/min |
| Alpha Vantage | `AlphaVantageSymbolSearchProvider` | Yes | Region-scoped global keyword results | 5/min |
| Tiingo | `TiingoSymbolSearchProvider` | Yes | Tiingo utilities search results with client-side asset/exchange filtering | 50/hour |
| Twelve Data | `TwelveDataSymbolSearchProvider` | Yes | `/symbol_search` results with client-side asset/exchange filtering | 8/min |
| Finnhub | `FinnhubSymbolSearchProvider` | Yes | US, International | 60/min |
| Polygon | `PolygonSymbolSearchProvider` | Yes | US | 5/min (free) |
| OpenFIGI | `OpenFigiClient` | No | Global (ID mapping) | Varies |
| StockSharp | `StockSharpSymbolSearchProvider` | No | Multi-exchange | Varies |

Located under `src/Meridian.Infrastructure/Adapters/` provider folders.

`SymbolSearchService` remains the shared aggregation boundary for UI and workflow consumers. It
normalizes provider symbols, drops malformed blank-symbol rows, attributes sparse rows to the
provider that returned them, promotes exact ticker matches ahead of fuzzy rows, and uses
multi-provider agreement plus provider priority as deterministic tie-breakers. These rules reduce
secondary-provider ambiguity without promoting any inventory provider to full search-quality parity.

---

## Streaming Failover

### StreamingFailoverService

**Location:** `src/Meridian.Infrastructure/Adapters/Failover/StreamingFailoverService.cs`

Orchestrates automatic failover between streaming providers. Monitors provider health via `IConnectionHealthMonitor`, evaluates configured failover rules, and triggers switchover when failures exceed the threshold.

**Failover flow:**

1. Rules are loaded from `DataSourcesConfig.FailoverRules` at startup.
2. A periodic timer evaluates each rule against provider health state.
3. When a provider's consecutive failures exceed the rule's `FailoverThreshold`, the service finds the next healthy backup provider.
4. `OnFailoverTriggered` event signals `FailoverAwareMarketDataClient` to switch.
5. Auto-recovery occurs when the primary provider's consecutive successes reach
   `RecoveryThreshold` and its recent latency window is within `MaxLatencyMs`, when configured.

**Failover triggers:**

- Consecutive operation failures exceeding the threshold
- Recent-window average latency exceeding `MaxLatencyMs` configured on the rule
- Connection lost events from `ConnectionHealthMonitor`
- Missed heartbeats (2+ consecutive)

**Manual control:** `ForceFailover(ruleId, targetProviderId)` allows API-driven provider switches via the `/api/failover/force/{ruleId}` endpoint.

### FailoverAwareMarketDataClient

**Location:** `src/Meridian.Infrastructure/Adapters/Failover/FailoverAwareMarketDataClient.cs`

A composite `IMarketDataClient` that wraps multiple provider clients and delegates to the currently active one. Transparent to callers.

**Key behaviors:**

- Tracks all active subscriptions (depth and trades) in concurrent dictionaries
- Returns existing per-symbol subscription IDs for duplicate requests so the active provider is not subscribed twice for the same stream
- On failover: connects new provider, re-subscribes all active symbols, then disconnects the old provider
- On connect failure: iterates backup providers until one succeeds
- Thread-safe switching via `SemaphoreSlim`
- Reports capabilities of the currently active underlying provider

Provider health snapshots keep a bounded recent latency window for streaming failover decisions.
This makes `MaxLatencyMs` routing responsive to current SLA breaches while allowing stale latency
spikes to decay after sustained healthy samples. When a latency breach triggers failover, backup
selection also applies the same `MaxLatencyMs` health rule so routing skips backups whose current
recent-latency window is already outside the rule threshold. Recovery uses that same threshold so
success pings alone do not route back to a primary provider that is still breaching the current
latency SLA.

### StreamingFailoverRegistry

**Location:** `src/Meridian.Infrastructure/Adapters/Failover/StreamingFailoverRegistry.cs`

Singleton that holds the runtime `StreamingFailoverService` instance, allowing API endpoint handlers to query failover state without direct project references.

---

## Streaming Lifecycle Diagnostics

`WebSocketConnectionManager` owns the shared raw-WebSocket lifecycle used by streaming providers that derive from `WebSocketProviderBase`.
It now exposes a secret-safe `WebSocketConnectionDiagnostics` snapshot for health surfaces, logs, and tests.

The snapshot includes:

- Provider name
- Lifecycle state: `Configured`, `Connecting`, `Connected`, `Degraded`, `Reconnecting`, `Disconnecting`, `Disconnected`, or `Failed`
- Current `ClientWebSocket` state
- Reconnect attempt count and latest reconnect attempt timestamp
- Last connected, disconnected, and message timestamps
- Current connection age and idle duration
- Last error message

The diagnostics record intentionally excludes endpoint URIs, headers, credentials, account IDs, and payload data. Provider-specific code should use this snapshot for operator status and diagnostic exports instead of logging connection internals directly.

---

## Subscription Recovery Diagnostics

`SubscriptionManager` is the shared subscription ledger used by streaming provider bases and provider test doubles.
It records enough metadata for reconnect, failover, and diagnostic flows to explain active subscription posture without logging payloads or secrets.

Tracked subscription fields include:

- Subscription ID
- Normalized symbol and subscription kind
- Created timestamp
- Status: `Active`, `Recovering`, or `Failed`
- Last accepted message timestamp
- Last subscription-specific error
- Recovery attempt count
- Optional source of request, such as a watchlist, strategy run, or workspace flow

Existing providers can keep using `Subscribe(...)` when they need independent subscription IDs for separate callers. Reconnect and failover code should prefer `SubscribeOrGetExisting(...)` when duplicate upstream subscriptions are unsafe. `ValidateSubscriptionRequest(...)` returns normalized fields, duplicate detection, the existing subscription ID when present, and a non-secret failure reason for missing symbol/kind inputs.

`GetSnapshot()` now includes total, failed, and recovering subscription counts for health surfaces. `GetStaleSubscriptions(...)` identifies active streams whose last accepted message is older than the caller's threshold so provider diagnostics can distinguish connected-but-stale streams from fully failed connections.

---

## Provider Boundary Data Validation

Streaming adapters should validate normalized provider messages before they enter collectors or the event pipeline. `ProviderDataQualityValidator` provides the shared boundary validator for common market-data invariants:

- Required symbol and timestamp
- Timestamp not beyond the configured future-skew window
- Non-negative trade prices and sizes
- Non-negative quote prices and sizes
- No crossed BBO quotes unless a provider-specific adapter has documented and explicitly handled that condition

Validation failures return `ProviderDataQualityIssue` records with severity, provider name, field path, entity context, message, and suggested recovery. Adapters must not silently discard invalid provider data; they should reject or flag it with structured log context and avoid high-frequency per-tick success logging.

Alpaca streaming now applies this validator to trade and quote messages after provider-specific JSON parsing and before forwarding to `TradeDataCollector` or `QuoteCollector`.

---

## Historical Provider Routing

### CompositeHistoricalDataProvider

**Location:** `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs`

Multi-provider router for historical data with automatic failover. Chains providers ordered by priority and routes requests with:

- **Priority-based fallback:** Tries providers in priority order; skips providers in backoff.
- **Rate-limit aware rotation:** When a provider nears its rate limit (configurable threshold), the next provider in the chain is used.
- **Provider health tracking:** Tracks `ProviderHealthStatus` per provider; backs off failed providers for a configurable duration.
- **Symbol resolution:** Optional `ISymbolResolver` (e.g., `OpenFigiSymbolResolver`) maps symbols across providers.
- **Cross-provider validation:** Optionally validates results from multiple providers for data consistency.
- **Aggregated capabilities:** Reports the union of all child provider capabilities.

### PriorityBackfillQueue

**Location:** `src/Meridian.Infrastructure/Adapters/Queue/PriorityBackfillQueue.cs`

Backfill job queue with priority-based ordering:

- **Priorities:** Critical, High, Normal, Low, Deferred
- **Batch enqueue** for bulk job submission
- **Dependency chains** for orchestrated multi-step workflows
- **Completion tracking** with retry support

### BackfillWorkerService

**Location:** `src/Meridian.Infrastructure/Adapters/Queue/BackfillWorkerService.cs`

Background service that dequeues jobs from `PriorityBackfillQueue` (or `BackfillRequestQueue`) and executes them through `CompositeHistoricalDataProvider`.

### Backfill Ordering and Gap-Remediation Contract

The operator-facing backfill contract is documented in
[Provider Backfill Operations](../operators/provider-backfill-operations.md). The current
architecture supports the following deterministic semantics:

- `HistoricalBackfillService` caps per-request symbol concurrency with `MaxConcurrentSymbols`; when
  unset, it uses the configured backfill job concurrency.
- `SymbolPriorities` sort symbols by lower numeric priority first, with case-insensitive keys.
  Without priorities, serial execution (`MaxConcurrentSymbols=1`) preserves input order.
- Cost preview and execution trim symbols, drop blanks, and de-duplicate symbols case-insensitively
  while preserving the first-seen order and spelling, preventing duplicate provider fetches from
  casing or whitespace variants in the same request.
- Concurrent multi-symbol runs do not guarantee completion order. Treat per-symbol validation
  signals, execution history, and checkpoints as the evidence boundary instead of task completion
  sequence.
- `ResumeFromCheckpoint=true` skips fully covered symbols and advances partially covered symbols to
  the day after the recorded checkpoint. Fresh non-resume runs clear only matching-granularity
  checkpoints, so intraday and daily lanes do not wipe each other.
- `PriorityBackfillQueue` orders jobs by `Critical`, `High`, `Normal`, `Low`, then `Deferred`, and
  keeps dependency-blocked jobs paused until all dependencies complete.

`AutoGapRemediationService` adds guardrails plus retained SLA classification and status projection,
not a complete cross-provider SLA engine. It requires minimum gap duration/size, applies symbol and
provider cooldowns, limits concurrent remediation attempts, suppresses duplicate completed attempts
by idempotency key, and allows transient failures to retry. Same-provider, same-window gap-analyzer
scan results are batched into a single deterministic remediation execution with uppercase, sorted
symbols and retained execution history. Each system-triggered execution now retains SLA tier,
due-time, owner-assignment, downstream-workflow, and reason-code warnings so critical paper,
reconciliation, accounting, or reporting blockers are distinguishable from standard repairs.
`EvaluateRemediationSla` projects those retained execution-history fields into overdue, due-soon,
failed, open, and completed status items with owner-assignment counts for operator read models.
Cross-provider fallback and promotion-grade closure remain operator-governed: the run is not
considered remediated until execution history, per-symbol validation signals, and a follow-up gap or
quality check prove the affected interval acceptable for the blocked downstream workflow.
For bounded daily backfill checks, `CrossSourceBackfillReconciliationService` can compare a
baseline provider against one or more comparison providers and retain structured price/volume drift,
missing-session, and provider-error evidence. Batch reconciliation normalizes symbols to uppercase,
de-duplicates them, preserves first-seen request order, and returns per-symbol evidence so operators
can audit a multi-symbol remediation without inferring order from provider completion timing. The
result also emits a closure decision and ordered review-symbol list so clean evidence is explicitly
separated from provider-error or discrepancy blockers. It is proof material for the
operator-governed closure flow, not autonomous SLA enforcement.

### Rate Limiting

**`ProviderRateLimitTracker`** and **`RateLimiter`** (`Infrastructure/Adapters/RateLimiting/`) enforce per-provider request pacing derived from provider metadata. The `IRateLimitAwareProvider` interface allows providers to report real-time rate limit status and emit `OnRateLimitHit` events.

---

## Gap Detection & Repair

### DataGapAnalyzer

**Location:** `src/Meridian.Infrastructure/Adapters/GapAnalysis/DataGapAnalyzer.cs`

Analyzes expected vs. stored data periods and classifies gaps:

- **Missing** — No data for expected trading periods
- **Partial** — Incomplete data coverage
- **Holiday** — Expected market closures
- **Suspicious** — Unusually low data volume

### DataGapRepair

**Location:** `src/Meridian.Infrastructure/Adapters/GapAnalysis/DataGapRepair.cs`

Attempts automated repair using the preferred provider chain. Writes repaired bars through `IHistoricalBarWriter` (defined in `ProviderSdk` to break the Infrastructure-Storage circular dependency).

### DataQualityMonitor

**Location:** `src/Meridian.Infrastructure/Adapters/GapAnalysis/DataQualityMonitor.cs`

Computes weighted quality scores across dimensions: completeness, accuracy, timeliness, consistency, and validity. Scores drive follow-up repair flows and operational dashboards.

---

## Health Monitoring

### ConnectionHealthMonitor

**Location:** `src/Meridian.DataIntegration/Monitoring/ConnectionHealthMonitor.cs`

Monitors connection health for streaming providers:

- **Heartbeat tracking** — Periodic heartbeat checks with configurable interval and timeout
- **Reconnection tracking** — Counts reconnects and computes uptime duration
- **Latency measurement** — Tracks round-trip latency per connection
- **Auto-reconnect support** — Optional `PingSender` delegate for active health probing
- **Serialized scans** — Time-provider-backed `PeriodicTimer` loops prevent overlapping heartbeat
  scans while allowing each scan to probe eligible providers concurrently
- **Bounded probes** — At most one ping is in flight for a connection generation, and
  `MaxConcurrentPingSenderInvocations` limits how many provider callbacks can execute at once
- **Generation isolation** — Unregister, stale cleanup, and disposal retire the current connection
  generation; disconnect also cancels its active ping and advances state so a late result cannot
  recover or credit a replacement with the same ID

`PingTimeoutSeconds` bounds how long a heartbeat scan awaits a provider callback and requests
cancellation when that limit expires. A callback that ignores cancellation may outlive the scan;
the monitor continues observing its eventual fault without holding shutdown open indefinitely.
`ConnectionHealthMonitor` implements both `IDisposable` and `IAsyncDisposable`: disposal stops the
periodic loops, cancels active probes, drains cooperative work, and avoids self-deadlock when a ping
or heartbeat event callback initiates disposal. These are lifecycle safeguards, not evidence that a
specific provider has completed production certification.

**Events:** `OnConnectionLost`, `OnConnectionRecovered`, `OnHeartbeatMissed`, `OnHighLatency`

### ProviderLatencyService

**Location:** `src/Meridian.DataIntegration/Monitoring/ProviderLatencyService.cs`

Tracks per-provider latency with full percentile histograms:

- **Histogram buckets:** 1ms, 5ms, 10ms, 25ms, 50ms, 100ms, 250ms, 500ms, 1s, 2.5s, 5s
- **Percentiles:** P50, P75, P90, P95, P99
- **Per-symbol tracking** within each provider
- **Configurable retention:** Default 24 hours, max 50,000 samples per provider
- **Summary API:** `GetSummary()` returns fastest/slowest provider, global percentiles
- **High latency detection:** `GetHighLatencyProviders(thresholdMs)` for alerting

### ProviderDegradationScorer

**Location:** `src/Meridian.DataIntegration/Monitoring/ProviderDegradationScorer.cs`

Computes a composite health/degradation score (0.0 = fully healthy, 1.0 = fully degraded) per provider by combining four weighted components:

| Component | Weight | Score Inputs |
|-----------|--------|-------------|
| Connection health | 0.35 | Disconnected state, missed heartbeats |
| Latency | 0.25 | P95 latency vs. threshold (default: 200ms-2000ms range) |
| Error rate | 0.25 | Sliding-window error rate (default: 300s window, 5% threshold) |
| Reconnect frequency | 0.15 | Reconnects per hour (default: max 10/hour) |

**Configurable thresholds (`ProviderDegradationConfig`):**

**Config/calibration location:** `src/Meridian.DataIntegration/Monitoring/ProviderDegradationConfig.cs`,
`src/Meridian.DataIntegration/Monitoring/ProviderDegradationCalibration.cs`

- `DegradationThreshold` — Composite score at which a provider is considered degraded (default: 0.6)
- `EvaluationIntervalSeconds` — Scoring frequency (default: 30s)
- All component weights and thresholds are configurable

**Events:** `OnProviderDegraded`, `OnProviderRecovered`

**Key APIs:** `GetScore(providerName)`, `GetAllScores()`, `GetProvidersByHealth()`, `IsDegraded(providerName)`.
Score discovery includes providers seen through connection health, latency histograms, or error
tracking so latency-only secondary-provider evidence is visible in health rankings.
When a provider has no streaming connection signal, scoring switches to a request-only mode and
rebalances across available latency and error-rate evidence. This keeps severe historical-provider
latency or request-failure telemetry from being capped below the degradation threshold simply
because the provider is not a streaming adapter.

Provider degradation calibration datasets, incident windows, kernel profiles, calibration
snapshots, promotion gate policy, governance workflow, and calibration report generation also live
in `Meridian.DataIntegration.Monitoring`. The Application-owned calibration command and endpoints
are runtime adapters that consume that Data Integration-owned provider trust model.

### ProviderMetricsStatus

**Location:** `src/Meridian.DataIntegration/Monitoring/ProviderMetricsStatus.cs`

Aggregates provider metrics for the `/api/providers/metrics` endpoint.

---

## Credential Management

### Shared encrypted provider store

**Locations:** `src/Meridian.Application/Config/Credentials/IProviderCredentialStore.cs`,
`src/Meridian.Application/Config/Credentials/FileProviderCredentialStore.cs`

Provider credentials are managed through `IProviderCredentialStore`. The default implementation
stores encrypted per-user provider records below the resolved Meridian data root:

```text
<DataRoot>/.mdc/provider-credentials.vault
<DataRoot>/.mdc/provider-credentials.audit.jsonl
```

The vault uses the current Windows user profile on Windows and a local profile key on non-Windows
hosts. Audit records include provider id, action, actor, state, source, verification posture, field
names, environment, and external account id. Audit records do not include raw credential values.

Runtime provider construction resolves through `StoredProviderCredentialResolver` first and then
falls back to the legacy config/environment resolver. Environment variables remain read-only
compatibility fallback; new browser flows must not write process, user, or machine environment
variables.

### ProviderSetupService

**Location:** `src/Meridian.Application/ProviderRouting/ProviderSetupService.cs`

`ProviderSetupService` is the application-layer boundary for the legacy
`POST /api/providers/configure` setup flow. It normalizes the provider family through the shared
credential catalog, saves submitted credentials only through `IProviderCredentialStore`, creates a
redacted legacy `DataSourceConfig` entry for backward compatibility, and seeds
`ProviderConnectionsConfig` connections and bindings for the requested setup capabilities.

The setup service never stores submitted API keys or API secrets in `DataSourceConfig`, endpoint
responses, or routing DTOs. Responses expose only safe setup metadata: provider id, connection id,
binding ids, credential state/source, credential reference, environment, and warnings. New setup
connections start with `ProductionReady = false` until certification or verification promotes the
connection.

Capability mapping for this compatibility flow is intentionally narrow:

| Setup capability | Provider routing capability |
|------------------|-----------------------------|
| `streaming` | `RealtimeMarketData` |
| `backfill` | `HistoricalBars` |
| `reference` | `ReferenceData` |

### CredentialValidator

**Location:** `src/Meridian.ProviderSdk/CredentialValidator.cs`

Centralized credential validation utilities:

- `ValidateApiKey(apiKey, providerName)` — Single API key validation
- `ValidateKeySecretPair(keyId, secretKey, providerName)` — Key/secret pair validation
- `ThrowIfApiKeyMissing(...)` / `ThrowIfCredentialsMissing(...)` — Throwing variants
- `GetCredential(paramValue, envVarName)` — Resolves credentials from parameters or environment variables with fallback

### ProviderCredentialField

Metadata for UI form generation:

```csharp
public sealed record ProviderCredentialField(
    string Name,              // e.g., "ApiKey"
    string? EnvironmentVariable,  // e.g., "ALPACA__KEYID"
    string DisplayName,       // e.g., "API Key ID"
    bool Required,
    string? DefaultValue = null);
```

Providers can either override `IProviderMetadata.ProviderCredentialFields` directly or declare `[RequiresCredential]` attributes. Runtime catalog generation falls back to the attribute metadata when explicit credential fields are not overridden, which keeps UI credential forms self-describing without a second provider-specific registry.

At runtime, `ProviderFactory`, the built-in Alpaca streaming registration, `ConfigurationService`, and `ConfigurationPipeline` now resolve credentials through a generic `ICredentialContext` created from provider attributes plus config overrides. `IProviderCredentialResolver` itself now exposes only that generic context factory, so new providers no longer require either a new interface method or a new application-layer helper method to participate in runtime credential resolution.

---

## UI Services

### ProviderManagementService

**Location:** `src/Meridian.Ui.Services/Services/ProviderManagementService.cs`

Desktop/UI service for managing multi-provider configurations:

- Provider status retrieval (`/api/providers/status`)
- Provider detail lookup
- Provider switching and configuration
- Failover rule management
- Rate limit monitoring

### ProviderHealthService

**Location:** `src/Meridian.Ui.Services/Services/ProviderHealthService.cs`

UI-side health monitoring service:

- Periodic health polling (5-second interval)
- Health history tracking per provider
- `HealthUpdated` and `HealthAlert` events for UI updates

---

## API Endpoints

### Provider API (`/api/providers/`)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/providers/configure` | POST | Compatibility setup flow that stores credentials through the encrypted provider store, creates a redacted legacy data source, and seeds provider-routing connections/bindings |
| `/api/providers/connections` | GET | Data and brokerage provider rows with credential state, verification state, continuity health, fallback posture, masked key preview, affected workflows, and repair action |
| `/api/providers/{providerId}/credentials` | PUT/DELETE | Save or delete provider credentials in the encrypted provider store |
| `/api/providers/{providerId}/verify` | POST | Verify stored provider credentials without returning secrets |
| `/api/providers/status` | GET | All provider status including active provider |
| `/api/providers/metrics` | GET | Provider metrics (latency, error rates) |
| `/api/providers/latency` | GET | Latency histograms per provider |
| `/api/providers/catalog` | GET | Provider catalog with capabilities metadata |
| `/api/providers/comparison` | GET | Feature comparison across providers |
| `/api/connections` | GET | Connection health status |

### Provider Routing API (`/api/provider-routing/`)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/provider-routing/connections` | GET | Configured provider-routing connections with safe credential references and production-ready state |
| `/api/provider-routing/bindings` | GET | Capability bindings used by the routing engine |
| `/api/provider-routing/trust-snapshots` | GET | Trust-score snapshots for configured routing connections |
| `/api/provider-routing/preview` | POST | Deterministic route preview for a requested provider capability |

### Failover API (`/api/failover/`)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/failover/config` | GET/POST | Failover configuration |
| `/api/failover/rules` | GET/POST | Failover rule management |
| `/api/failover/health` | GET | Provider health for failover decisions |
| `/api/failover/force/{ruleId}` | POST | Force manual failover |

---

## Configuration

### Connection health

`ConnectionHealthConfig` is supplied when constructing `ConnectionHealthMonitor`. Its defaults are:

| Property | Default | Purpose |
|----------|---------|---------|
| `HeartbeatIntervalSeconds` | `30` | Interval between serialized heartbeat scans |
| `HeartbeatTimeoutSeconds` | `60` | Inactivity threshold before a heartbeat is missed |
| `MaxMissedHeartbeats` | `3` | Miss count that transitions a connection to lost |
| `PingTimeoutSeconds` | `10` | Maximum time a scan awaits an active ping before requesting cancellation |
| `MaxConcurrentPingSenderInvocations` | `32` | Per-monitor bound on concurrently executing `PingSender` callbacks |
| `HighLatencyThresholdMs` | `500` | Threshold for `OnHighLatency` warnings |

Heartbeat interval, heartbeat timeout, ping timeout, and ping-callback concurrency values must be
positive; invalid values fail during monitor construction.

### Failover rules

```json
{
  "DataSources": {
    "EnableFailover": true,
    "HealthCheckIntervalSeconds": 10,
    "FailoverRules": [
      {
        "Id": "primary-streaming",
        "PrimaryProviderId": "alpaca",
        "BackupProviderIds": ["polygon", "stocksharp"],
        "FailoverThreshold": 3,
        "RecoveryThreshold": 5,
        "MaxLatencyMs": 1000
      }
    ]
  }
}
```

### Degradation scoring

```json
{
  "ProviderDegradation": {
    "EvaluationIntervalSeconds": 30,
    "DegradationThreshold": 0.6,
    "LatencyThresholdMs": 200,
    "LatencyMaxMs": 2000,
    "ErrorRateThreshold": 0.05,
    "ErrorWindowSeconds": 300,
    "MaxReconnectsPerHour": 10,
    "ConnectionWeight": 0.35,
    "LatencyWeight": 0.25,
    "ErrorRateWeight": 0.25,
    "ReconnectWeight": 0.15
  }
}
```

### Credentials

Credentials are resolved from environment variables using double-underscore nesting:

```bash
export ALPACA__KEYID=your-key-id
export ALPACA__SECRETKEY=your-secret-key
export POLYGON__APIKEY=your-api-key
export TIINGO__TOKEN=your-token
```

---

## Best Practices

1. **Select providers by capability, then by priority and health**
   - Filter by required features using `ProviderCapabilities`.
   - Exclude providers that are degraded (`ProviderDegradationScorer.IsDegraded`).
   - Prefer lower priority value when multiple providers qualify.

2. **Use the composite pattern for failover**
   - `FailoverAwareMarketDataClient` for streaming: transparent, automatic provider switching.
   - `CompositeHistoricalDataProvider` for backfill: priority chain with rate-limit rotation.
   - Both re-subscribe/re-route automatically on provider failure.

3. **Monitor provider health at multiple levels**
   - `ConnectionHealthMonitor` for connection-level health (heartbeats, connectivity).
   - `ProviderLatencyService` for latency distribution tracking.
   - `ProviderDegradationScorer` for composite health scoring combining all signals.

4. **Implement new providers using the ProviderSdk contracts**
   - Implement `IMarketDataClient` (streaming), `IHistoricalDataProvider` (backfill), or `ISymbolSearchProvider` (search).
   - Add `[DataSource]` and `[ImplementsAdr]` attributes.
   - Override `IProviderMetadata` default implementations for accurate capability reporting.
   - Implement `IProviderModule` for custom DI registration.

5. **Close the loop on data quality**
   - Validate streaming adapter inputs with `ProviderDataQualityValidator` before collector ingress.
   - Use `DataGapAnalyzer` to detect missing data periods.
   - Trigger `DataGapRepair` for automated gap filling.
   - Score quality with `DataQualityMonitor` and emit metrics.
   - Re-score after repair and feed results into dashboards.

6. **Publish catalog metadata for UI/ops**
   - Use `DataSourceRegistry.Sources` and `IProviderMetadata` so UI components consume normalized provider metadata.
   - `ProviderCapabilities.ToDictionary()` converts capabilities to JSON for API responses.

---

## Migration Notes (v2.1 -> v3.0)

- Added documentation of concrete streaming, historical, and symbol search provider implementations.
- Documented the `StreamingFailoverService` and `FailoverAwareMarketDataClient` failover architecture with event-driven switching.
- Added `ProviderDegradationScorer` composite health scoring (weighted: connection 0.35, latency 0.25, errors 0.25, reconnects 0.15).
- Added `ProviderLatencyService` percentile histogram tracking.
- Documented the `IDataSource` / `DataSourceCapabilities` contract system alongside `IProviderMetadata` / `ProviderCapabilities`.
- Added `IProviderModule` and credential management (`CredentialValidator`, `ProviderCredentialField`).
- Added configuration examples for failover rules and degradation scoring.
- Added UI services (`ProviderManagementService`, `ProviderHealthService`) and API endpoint reference.
- Documented gap detection/repair pipeline (`DataGapAnalyzer`, `DataGapRepair`, `DataQualityMonitor`).
- Documented `PriorityBackfillQueue` and `BackfillWorkerService` for historical backfill job management.

## Migration Notes (v3.0 -> v3.1)

- Added provider base classes to the layered architecture diagram: `WebSocketProviderBase` (streaming), `BaseHistoricalDataProvider` (historical), `BaseSymbolSearchProvider` (symbol search).
- Added `BackfillProgressTracker` – real-time ETA and progress tracking for backfill jobs; available via `/api/backfill/status` endpoint.
- Added `ProviderSubscriptionRanges` utility for splitting large symbol lists into provider-compatible batches.
- Updated version and date.

## Migration Notes (v3.1 -> v3.2)

- Added the encrypted `IProviderCredentialStore` foundation and runtime `StoredProviderCredentialResolver`.
- Added the canonical provider connection API for listing, saving, verifying, and deleting provider credentials.
- Kept legacy `/api/credentials/*`, provider-credential validation/test, and Alpaca brokerage routes as compatibility wrappers over the shared store.
- Updated Alpaca paper verification to use the explicit Trading API endpoint `https://paper-api.alpaca.markets/v2/account`.

## Migration Notes (v3.2 -> v3.3)

- Moved legacy provider setup DTOs into shared contracts and routed `/api/providers/configure`
  through `ProviderSetupService`.
- Hardened provider setup so newly submitted secrets are written only to `IProviderCredentialStore`
  and never into legacy `DataSourceConfig` entries.
- Added `/api/provider-routing/connections`, `/api/provider-routing/bindings`,
  `/api/provider-routing/trust-snapshots`, and `/api/provider-routing/preview` endpoint mappings.
- Seeded provider-routing connections and bindings from setup so configured providers are visible
  to route previews and trust snapshots immediately.

## Migration Notes (v3.3 -> v3.4)

- Added secret-safe `WebSocketConnectionDiagnostics` and explicit streaming lifecycle states to `WebSocketConnectionManager`.
- Added `ProviderDataQualityValidator` for provider-boundary trade and quote checks.
- Routed Alpaca streaming trade and quote messages through the boundary validator before collector ingress.
- Hardened `FailoverAwareMarketDataClient` so duplicate per-symbol trade/depth subscription requests return the existing subscription ID instead of creating duplicate upstream provider subscriptions.

## Migration Notes (v3.4 -> v3.5)

- Extended `SubscriptionManager` with subscription status, last-message timestamps, last-error text, recovery-attempt counts, and optional request-source metadata.
- Added `ValidateSubscriptionRequest(...)`, `SubscribeOrGetExisting(...)`, `RecordMessageReceived(...)`, `RecordSubscriptionError(...)`, `RecordRecoveryAttempt(...)`, and `GetStaleSubscriptions(...)` for reconnect/failover-safe subscription supervision.
- Kept the existing `Subscribe(...)` behavior intact for providers that intentionally maintain separate per-caller subscription IDs.

## Migration Notes (v3.5 -> v3.6)

- Moved `ProviderLatencyService`, provider latency summary records, `ProviderMetricsStatus`, and
  `ProviderMetrics` from Application monitoring into `Meridian.DataIntegration.Monitoring`.
- Moved `ConnectionHealthMonitor`, connection-health config/snapshot/status records,
  `ProviderDegradationScorer`, provider degradation score/event/reason records, and degradation
  delta helpers into `Meridian.DataIntegration.Monitoring`.
- Moved `ProviderDegradationConfig`, provider degradation calibration datasets/snapshots, kernel
  calibration policies, governance workflow service, and calibration report writer into
  `Meridian.DataIntegration.Monitoring`.
- Moved `ClockSkewEstimator`, `SpreadMonitor`, `DataLossAccounting`, and their provider
  data-quality evidence records into `Meridian.DataIntegration.Monitoring`.
- Moved `SchemaValidationService` and stored market-event schema compatibility result records into
  `Meridian.DataIntegration.Monitoring`.
- Kept provider calibration command and status endpoint composition in Application as consumers of
  the Data Integration-owned provider telemetry, health, scoring, calibration, and data-quality
  evidence models.

## Migration Notes (v3.6 -> v3.7)

- Added the historical backfill ordering, checkpoint/resume, priority queue, and gap-remediation
  contract that links architecture guidance to the canonical provider backfill operator runbook.
- Clarified that current auto-remediation is guardrail automation with retained SLA classification
  metadata and status projection, while cross-provider SLA closure still requires retained execution,
  validation-signal, and follow-up quality evidence plus later governance timer enforcement.

## Migration Notes (v3.7 -> v3.8)

- Replaced callback-based connection-health timers with time-provider-backed `PeriodicTimer` loops
  and serialized heartbeat scans.
- Added generation-safe active probes, a per-connection in-flight guard, configurable bounded
  `PingSender` callback concurrency, and timeout/cancellation handling that observes late faults.
- Added coordinated synchronous/asynchronous disposal so cooperative probes are drained while
  non-cooperative provider callbacks cannot hold monitor shutdown open indefinitely.
