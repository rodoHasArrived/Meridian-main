# High-Impact Repository Improvements Brainstorm

> **TO BE UPDATED:** This brainstorm remains useful as idea history, but it predates newer March 2026 planning artifacts and should be reconciled with the active roadmap before being used as a current priority source.

**Date:** 2026-03-02 (Part 1) / 2026-03-03 (Part 2)
**Status:** Active — Improvements Identified
**Author:** Architecture Review

> This document contains two separate deep-analysis sessions.  
> **Part 1** (2026-03-02) focuses on code generalization, architectural soundness, and extensibility.  
> **Part 2** (2026-03-03) focuses on output quality, correctness, and production robustness.

---

## Table of Contents

### Part 1 — Code Generalization & Architecture (2026-03-02)
1. [Replace Stringly-Typed Identifiers with Strong Domain Types](#1-replace-stringly-typed-identifiers-with-strong-domain-types)
2. [Make the Event Pipeline Generic and Composable](#2-make-the-event-pipeline-generic-and-composable-middleware-pipeline)
3. [Unify the Dual Domain Model](#3-unify-the-dual-domain-model-c-records--f-types)
4. [Introduce a Proper Event Sourcing / CQRS Backbone](#4-introduce-a-proper-event-sourcing--cqrs-backbone)
5. [Extract a Provider Contract Test Suite](#5-extract-a-provider-contract-test-suite-consumer-driven-contracts)
6. [Implement Structural Typing for MarketEventPayload](#6-implement-structural-typing-for-marketeventpayload-eliminate-polymorphic-null)
7. [Implement Backpressure Propagation Across the Stack](#7-implement-backpressure-propagation-across-provider--collector--pipeline)
8. [Implement a Plugin Architecture for Storage Sinks](#8-implement-a-plugin-architecture-for-storage-sinks)
9. [Introduce Deterministic Replay Testing](#9-introduce-deterministic-replay-testing-golden-master-tests)
10. [Implement Zero-Allocation Hot Path](#10-implement-zero-allocation-hot-path-struct-based-event-pipeline)
11. [Implement a Formal State Machine for Provider Connection Lifecycle](#11-implement-a-formal-state-machine-for-provider-connection-lifecycle)
12. [Compile-Time Architectural Boundary Enforcement](#12-compile-time-architectural-boundary-enforcement)
13. [Implement Data Lineage as a First-Class Pipeline Concept](#13-implement-data-lineage-as-a-first-class-pipeline-concept)
14. [Replace Runtime Provider Discovery with Source-Generated Registration](#14-replace-runtime-provider-discovery-with-source-generated-registration)
15. [Implement Comprehensive Schema Evolution for Stored Data](#15-implement-comprehensive-schema-evolution-for-stored-data)
- [Part 1 Summary: Impact Ranking](#summary-impact-ranking)

### Part 2 — Output Quality & Correctness (2026-03-03)
1. [Unified Event Schema Evolution & Versioning](#1-unified-event-schema-evolution--versioning)
2. [Replace Stringly-Typed Provider/Symbol/Venue Identifiers with Strong Types](#2-replace-stringly-typed-providersymbolvenueidentifiers-with-strong-types)
3. [Make the F# Validation Pipeline a First-Class Citizen](#3-make-the-f-validation-pipeline-a-first-class-citizen-in-the-event-flow)
4. [Implement Proper WAL Corruption Recovery](#4-implement-proper-wal-corruption-recovery)
5. [Introduce a Query/Replay Abstraction Over Stored Data](#5-introduce-a-queryreplay-abstraction-over-stored-data)
6. [End-to-End Contract Testing for the Provider → Storage Round-Trip](#6-end-to-end-contract-testing-for-the-provider--storage-round-trip)
7. [Make the Composite Sink Failure Model Explicit and Configurable](#7-make-the-composite-sink-failure-model-explicit-and-configurable)
8. [Formalize the Provider Capability Model for Smarter Orchestration](#8-formalize-the-provider-capability-model-for-smarter-orchestration)
9. [Enforce Structural Consistency via Roslyn Analyzers](#9-enforce-structural-consistency-via-roslyn-analyzers)
10. [Build a Deterministic Data Replay System](#10-build-a-deterministic-data-replay-system)
11. [Eliminate the Endpoint Stub Problem](#11-eliminate-the-endpoint-stub-problem)
12. [Unify and Harden the Reconnection Model](#12-unify-and-harden-the-reconnection-model)
13. [Add Property-Based / Fuzz Testing for the Data Path](#13-add-property-based--fuzz-testing-for-the-data-path)
14. [Implement Cross-Provider Data Reconciliation](#14-implement-cross-provider-data-reconciliation)
15. [Decouple the HTTP API from the Collection Engine](#15-decouple-the-http-api-from-the-collection-engine)
16. [Implement Structured Concurrency for Provider Lifecycle](#16-implement-structured-concurrency-for-provider-lifecycle)
- [Part 2 Summary: Impact vs. Risk Matrix](#summary-impact-vs-risk-matrix)

---

## 1. Replace Stringly-Typed Identifiers with Strong Domain Types

**Current state:** Symbols, provider IDs, stream IDs, venue codes, and subscription IDs are all bare `string` or `int` throughout the codebase. The `MarketEvent` record uses `string Symbol`, `string Source`, `string? CanonicalSymbol`, `string? CanonicalVenue`. Collectors key state on `string`. The entire pipeline, storage, and API layer pass raw strings around.

**Problem:** Nothing prevents mixing a symbol with a venue code, a provider ID with a stream ID, or passing an un-normalized symbol where a canonical one is expected. The compiler cannot help. Bugs like "passed venue where symbol expected" are silent runtime errors. The `EffectiveSymbol` property on `MarketEvent` is a band-aid for what should be a type-level distinction.

**Improvement:** Introduce value-object wrappers:

```csharp
public readonly record struct Symbol(string Value) : IComparable<Symbol>;
public readonly record struct CanonicalSymbol(string Value);
public readonly record struct ProviderId(string Value);
public readonly record struct Venue(string Value);
public readonly record struct StreamId(string Value);
public readonly record struct SubscriptionId(int Value);
```

These are zero-cost at runtime (single-field readonly structs) but eliminate entire categories of bugs at compile time. The `ConcurrentDictionary<string, SymbolTradeState>` in `TradeDataCollector` becomes `ConcurrentDictionary<Symbol, SymbolTradeState>`, making the key semantics explicit. Storage paths, dedup keys, metrics labels, and quality monitoring all benefit from type-safe symbols vs canonical symbols.

**Impact:** Eliminates a class of subtle runtime bugs. Makes API contracts self-documenting. Enables compile-time enforcement of "canonical vs raw" symbol distinction that currently relies on developer discipline.

---

## 2. Make the Event Pipeline Generic and Composable (Middleware Pipeline)

**Current state:** `EventPipeline` is a monolithic 677-line class that hardcodes: channel-based backpressure, WAL integration, batch consumption, periodic flushing, metrics tracking, and audit trail logging. All concerns are interleaved in `ConsumeAsync()`. Adding a new cross-cutting concern (e.g., deduplication, filtering, transformation, sampling) requires modifying this class directly.

**Problem:** The pipeline is not composable. Every new behavior (canonicalization, validation, filtering, enrichment) must be wired externally or bolted onto the monolith. The `CanonicalizingPublisher` wraps `IMarketEventPublisher` — but this only works at the publish boundary, not within the pipeline. There is no way to express "validate, then canonicalize, then deduplicate, then persist" as a pipeline of independent stages.

**Improvement:** Introduce a middleware-based pipeline architecture:

```csharp
public delegate ValueTask EventPipelineDelegate(MarketEvent evt, CancellationToken ct);

public interface IEventPipelineMiddleware
{
    ValueTask InvokeAsync(MarketEvent evt, EventPipelineDelegate next, CancellationToken ct);
}
```

Each concern becomes a composable middleware:
- `WalMiddleware` — WAL append before forwarding
- `DeduplicationMiddleware` — drop duplicate sequences
- `CanonicalizationMiddleware` — normalize symbols/venues
- `ValidationMiddleware` — run F# validators, emit integrity events
- `MetricsMiddleware` — track throughput and latency
- `FilterMiddleware` — configurable event type filtering
- `StorageSinkMiddleware` — terminal middleware that writes to sink

The pipeline builder composes them:
```csharp
pipeline.Use<MetricsMiddleware>()
        .Use<DeduplicationMiddleware>()
        .Use<CanonicalizationMiddleware>()
        .Use<ValidationMiddleware>()
        .Use<WalMiddleware>()
        .Use<StorageSinkMiddleware>();
```

**Impact:** Transforms the pipeline from a closed system into an open, extensible one. New behaviors are additive, not invasive. Each middleware is independently testable. Pipeline composition becomes a configuration concern rather than a code change.

---

## 3. Unify the Dual Domain Model (C# Records + F# Types)

**Current state:** The domain is split across two type systems:
- C# records in `Contracts/Domain/` and `Domain/Events/` (`Trade`, `BboQuotePayload`, `LOBSnapshot`, `MarketEvent`)
- F# records in `Meridian.FSharp/Domain/` (`TradeEvent`, `QuoteEvent`, `OrderBookSnapshot`)
- The `Interop.fs` file provides manual wrappers (`TradeEventWrapper`, `QuoteEventWrapper`) to bridge between them.

**Problem:** There are two parallel representations of every core domain concept. A trade is both a C# `Trade` record and an F# `TradeEvent` record. Conversion between them is manual and fragile. The F# validation library (`TradeValidator`, `QuoteValidator`) operates on F# types, so C# code must convert to F# types, validate, then convert back. This dual model increases surface area for bugs and makes it unclear which representation is canonical.

**Improvement:** Choose one canonical representation and derive the other:

**Option A: F# as the canonical domain, generate C# projections.** F# discriminated unions and record types are more expressive for domain modeling. Use the existing `FSharpInteropGenerator` (in `build/dotnet/`) to auto-generate C# wrappers from F# types, eliminating hand-written `Interop.fs`.

**Option B: C# as the canonical domain, use F# computation expressions over C# types.** Since the C# types are already used everywhere, make the F# validators operate directly on C# record types via extension modules. Eliminate the parallel F# domain types entirely.

Either way, the goal is: **one source of truth for each domain concept**, with the other language consuming it directly.

**Impact:** Eliminates an entire layer of conversion code, reduces bug surface for type mismatches, makes the F# validation pipeline zero-friction to use from C#.

---

## 4. Introduce a Proper Event Sourcing / CQRS Backbone

**Current state:** `MarketEvent` is a sealed record with 16+ factory methods that acts as both a domain event and a persistence envelope. The `MarketEventPayload` base class uses polymorphic dispatch (nullable base type) with runtime type checks. The WAL stores serialized `MarketEvent` blobs. Storage sinks receive events one at a time via `AppendAsync`. There is no event store abstraction — only storage sinks.

**Problem:** The system has the shape of event sourcing (immutable events, WAL, replay) but lacks the formal guarantees. There is no event versioning strategy (the `SchemaVersion` field exists but is always `1`). There is no projection/replay capability beyond WAL recovery. Querying historical data requires reading JSONL files. The system cannot answer "replay all trades for SPY from 10:30 to 11:00 through the pipeline" without building ad-hoc infrastructure each time.

**Improvement:** Formalize the event store abstraction:

```csharp
public interface IEventStore
{
    ValueTask AppendAsync(MarketEvent evt, CancellationToken ct);
    IAsyncEnumerable<MarketEvent> ReadForwardAsync(Symbol symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    IAsyncEnumerable<MarketEvent> ReadForwardAsync(EventStreamPosition position, CancellationToken ct);
    Task<EventStreamPosition> GetCurrentPositionAsync(CancellationToken ct);
}
```

Add event schema evolution:
- Each payload type gets an explicit schema version
- Upcasters transform old versions to current
- The WAL and storage layer both use the event store interface

Add projection support:
- Projections are stateful consumers of the event stream
- VWAP, order flow stats, spread monitoring become projections
- Projections can be rebuilt from the event store at any time

**Impact:** Enables time-travel debugging, replay-based backtesting, schema evolution without data migration, and decouples read-side queries from write-side storage.

---

## 5. Extract a Provider Contract Test Suite (Consumer-Driven Contracts)

**Current state:** Each provider (Alpaca, Polygon, IB, StockSharp, NYSE, etc.) is tested in isolation with mocks. There is no shared test suite that verifies all providers satisfy the `IMarketDataClient` and `IHistoricalDataProvider` contracts identically. The `ContractVerificationService` in `Infrastructure/Contracts/` exists but is a runtime service, not a test harness.

**Problem:** Provider implementations can drift. One provider might emit trades with negative sequence numbers (which `TradeDataCollector` rejects), another might emit timestamps in local time instead of UTC, another might not handle cancellation tokens properly. These discrepancies are only discoverable at integration time or in production.

**Improvement:** Create a shared contract test base class:

```csharp
public abstract class MarketDataClientContractTests<T> where T : IMarketDataClient
{
    protected abstract T CreateClient();

    [Fact] public async Task Connect_Then_Disconnect_Should_Not_Throw() { ... }
    [Fact] public async Task Subscribe_Trades_Should_Return_Positive_SubscriptionId() { ... }
    [Fact] public async Task Events_Should_Have_Monotonic_Sequences() { ... }
    [Fact] public async Task Events_Should_Have_UTC_Timestamps() { ... }
    [Fact] public async Task Cancellation_Should_Be_Respected() { ... }
    [Fact] public async Task Dispose_Should_Disconnect_Gracefully() { ... }
}

// Each provider inherits and provides its implementation:
public class AlpacaContractTests : MarketDataClientContractTests<AlpacaMarketDataClient>
{
    protected override AlpacaMarketDataClient CreateClient() => ...;
}
```

Similarly for `IHistoricalDataProvider`:

```csharp
public abstract class HistoricalProviderContractTests<T> where T : IHistoricalDataProvider
{
    [Fact] public async Task GetDailyBars_Should_Return_Sorted_By_Date() { ... }
    [Fact] public async Task GetDailyBars_Should_Have_Positive_OHLC_Values() { ... }
    [Fact] public async Task GetDailyBars_Should_Respect_Date_Range() { ... }
    [Fact] public async Task Rate_Limit_Should_Not_Throw_But_Wait() { ... }
}
```

**Impact:** Guarantees behavioral consistency across all providers. New providers automatically inherit the full contract test suite. Regression detection is immediate.

---

## 6. Implement Structural Typing for MarketEventPayload (Eliminate Polymorphic Null)

**Current state:** `MarketEvent` has a `Payload` property of type `MarketEventPayload?`. This is a nullable base class that the consumer must downcast at runtime. The 16 factory methods on `MarketEvent` create events with different payload types, but the type information is lost in the record's signature. Consumers must pattern-match on `Type` and then cast `Payload`:

```csharp
if (evt.Type == MarketEventType.Trade && evt.Payload is Trade trade) { ... }
```

**Problem:** The compiler cannot prove exhaustiveness. Nothing prevents accessing `evt.Payload` as a `Trade` when it's actually an `LOBSnapshot`. The nullable payload means `Heartbeat` events have `null` payloads — a special case that every consumer must handle. Adding a new event type requires updating every consumer manually.

**Improvement:** Use a discriminated union pattern (C# 13 supports this well):

```csharp
public abstract record MarketEventPayload
{
    public sealed record TradePayload(Trade Trade) : MarketEventPayload;
    public sealed record L2SnapshotPayload(LOBSnapshot Snapshot) : MarketEventPayload;
    public sealed record BboQuotePayload(BboQuote Quote) : MarketEventPayload;
    public sealed record OrderFlowPayload(OrderFlowStatistics Stats) : MarketEventPayload;
    public sealed record IntegrityPayload(IntegrityEvent Integrity) : MarketEventPayload;
    public sealed record HeartbeatPayload() : MarketEventPayload;
    public sealed record HistoricalBarPayload(HistoricalBar Bar) : MarketEventPayload;
    // ... etc
}
```

Now `MarketEvent.Payload` is non-nullable (every event has a payload, even heartbeats). Consumers use exhaustive pattern matching:

```csharp
var result = evt.Payload switch
{
    MarketEventPayload.TradePayload t => HandleTrade(t.Trade),
    MarketEventPayload.BboQuotePayload q => HandleQuote(q.Quote),
    // compiler warns if cases are missing
};
```

**Impact:** Eliminates null-payload special cases, enables compiler-verified exhaustive handling, makes adding new event types a compile-error-driven process.

---

## 7. Implement Backpressure Propagation Across Provider → Collector → Pipeline

**Current state:** The `EventPipeline` has backpressure (bounded channel, drop-oldest). But the `TradeDataCollector.OnTrade()` method is synchronous and void — it calls `TryPublish` and silently drops if the pipeline is full. Providers push data into collectors with no feedback mechanism. The `DroppedEventAuditTrail` records drops, but nothing uses this signal to slow down the source.

**Problem:** In a sustained overload scenario, the system silently drops data while providers continue pushing at full speed. There is no feedback loop. The pipeline drops events, the audit trail logs them, but the providers don't know and don't slow down. This means the system's behavior under load is "lose data silently" rather than "slow down gracefully."

**Improvement:** Introduce backpressure propagation:

1. **Make collectors async-aware:** `OnTrade` returns a `ValueTask<bool>` indicating whether the event was accepted. When the pipeline is full, collectors can signal back to the provider.

2. **Add provider-side flow control:** `IMarketDataClient` gets a `PauseAsync()`/`ResumeAsync()` contract. When backpressure is detected, the subscription orchestrator pauses the provider's data stream.

3. **Implement adaptive rate limiting:** Instead of binary pause/resume, use a token-bucket or leaky-bucket pattern. The pipeline's utilization percentage drives the token refill rate, creating smooth degradation.

4. **Expose backpressure as a metric dimension:** Current metrics track "dropped events" as a count. Instead, expose `pipeline_backpressure_ratio` as a gauge (0.0 = no pressure, 1.0 = fully saturated). This feeds into alerting and auto-scaling decisions.

**Impact:** Transforms the system from "lossy under load" to "gracefully degrading under load." Prevents silent data loss in production. Enables auto-scaling decisions.

---

## 8. Implement a Plugin Architecture for Storage Sinks

**Current state:** Storage sinks are registered at compile time via DI. The `CompositeSink` hardcodes JSONL + optional Parquet. Adding a new sink (e.g., ClickHouse, TimescaleDB, Apache Kafka, S3) requires modifying `ServiceCompositionRoot.cs` and the sink registration logic.

**Problem:** Storage is the most likely extension point for users. Different deployments want different storage backends. But adding a new sink requires rebuilding the application. There is no plugin discovery mechanism for sinks.

**Improvement:** Implement a plugin-based sink architecture:

```csharp
[StorageSink("clickhouse")]
public sealed class ClickHouseSink : IStorageSink { ... }

[StorageSink("kafka")]
public sealed class KafkaSink : IStorageSink { ... }
```

At startup, the composition root scans for `[StorageSink]` attributes (similar to how `[DataSource]` works for providers). Configuration drives which sinks are active:

```json
{
  "Storage": {
    "Sinks": ["jsonl", "parquet", "clickhouse"],
    "ClickHouse": { "ConnectionString": "..." }
  }
}
```

The `CompositeSink` becomes dynamically composed from the configured sink list.

**Impact:** Makes storage extensible without code changes. Users can add new storage backends as plugins. The existing JSONL/Parquet sinks become just two instances of the plugin pattern.

---

## 9. Introduce Deterministic Replay Testing (Golden Master Tests)

**Current state:** Tests mock individual services and assert specific behaviors. The `JsonlReplayer` and `MemoryMappedJsonlReader` exist for replay, but there are no tests that replay a known input sequence and compare the full output against a golden master.

**Problem:** The system transforms input data through many stages (provider → collector → canonicalization → validation → pipeline → storage). End-to-end correctness is only testable in production. A subtle change in trade aggregation, sequence validation, or VWAP calculation could pass all unit tests but produce different output data.

**Improvement:** Create deterministic replay tests:

1. **Capture golden datasets:** Record a sequence of raw provider messages (e.g., 1000 trades + quotes for SPY over 5 minutes from the Alpaca adapter).

2. **Replay through the full pipeline:** Feed the golden input through the real collector → pipeline → storage chain (with in-memory sinks).

3. **Compare output against golden master:** The storage sink's output (JSONL lines) is compared byte-for-byte against a committed golden file.

4. **Detect regressions automatically:** Any change to the pipeline that alters output data causes the golden master test to fail. The developer must explicitly update the golden file, which forces them to review the delta.

```csharp
[Fact]
public async Task Replay_SPY_GoldenDataset_ProducesExpectedOutput()
{
    var input = LoadGoldenInput("testdata/spy-1000-trades.jsonl");
    var sink = new InMemoryStorageSink();
    var pipeline = BuildFullPipeline(sink);

    foreach (var evt in input)
        await pipeline.PublishAsync(evt);
    await pipeline.FlushAsync();

    var output = sink.GetAllEvents();
    await Verify(output); // Verify library for snapshot testing
}
```

**Impact:** Catches subtle behavioral regressions that unit tests miss. Provides confidence that pipeline changes don't silently alter output data. Creates a reproducible baseline for performance benchmarking.

---

## 10. Implement Zero-Allocation Hot Path (Struct-Based Event Pipeline)

**Current state:** `MarketEvent` is a `sealed record` (reference type). Every event allocation goes through the heap. The `ConsumeAsync()` loop creates a `List<MarketEvent>` batch buffer per iteration. The channel stores reference types. In the hot path (high-frequency trade data), this generates significant GC pressure.

**Problem:** For market data at scale (thousands of events per second per symbol, across hundreds of symbols), GC pauses introduce latency spikes. The current architecture allocates on every event: the `MarketEvent` record, the payload record, the `List<T>` buffer resize, and potentially the JSONL serialization string.

**Improvement:** Introduce a struct-based fast path for the highest-volume event types:

1. **Struct event representation for hot path:**
```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct RawTradeEvent
{
    public readonly long TimestampTicks;
    public readonly int SymbolHash;  // Pre-computed, lookup in symbol table
    public readonly decimal Price;
    public readonly long Size;
    public readonly byte Aggressor;
    public readonly long Sequence;
}
```

2. **Ring buffer instead of channel:** For the ultra-hot path, use a `SingleProducerSingleConsumer` ring buffer backed by pre-allocated memory. No allocation per event.

3. **Batch serialization:** Instead of serializing events one at a time, batch-serialize to a pre-allocated `Span<byte>` buffer using `Utf8JsonWriter`.

4. **Dual path:** Keep the current `MarketEvent` record-based pipeline for low-volume event types (integrity, heartbeat, historical bars). Use the struct-based path only for trades and quotes.

**Impact:** Eliminates GC pressure on the hot path. Reduces p99 latency. Enables the system to handle 10-100x more events per second before degrading.

---

## 11. Implement a Formal State Machine for Provider Connection Lifecycle

**Current state:** Provider connection state is tracked via `bool IsConnected`, `bool IsReconnecting`, and various `volatile` flags in `WebSocketConnectionManager`. State transitions (disconnected → connecting → connected → reconnecting → disconnected) are implicit in the control flow of `ConnectAsync()`, `ReconnectInternalAsync()`, and event handlers.

**Problem:** Invalid state transitions are possible. For example, calling `SubscribeTrades()` while `IsReconnecting` is true could produce undefined behavior. The reconnection logic uses `SemaphoreSlim` gates to prevent storms, but the allowed transitions are not formally modeled. Race conditions between heartbeat timeout detection and manual disconnect are possible.

**Improvement:** Model the connection lifecycle as a formal state machine:

```csharp
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Subscribing,
    Active,        // Connected + subscriptions active
    Reconnecting,
    Draining,      // Shutting down, processing remaining events
    Disposed
}
```

Use a state machine library or hand-rolled transition table:

```csharp
public sealed class ConnectionStateMachine
{
    private static readonly Dictionary<(ConnectionState, ConnectionTrigger), ConnectionState> Transitions = new()
    {
        { (Disconnected, Connect), Connecting },
        { (Connecting, Connected), Connected },
        { (Connected, Subscribe), Subscribing },
        { (Subscribing, AllSubscribed), Active },
        { (Active, ConnectionLost), Reconnecting },
        { (Reconnecting, Connected), Subscribing },
        { (Active, Disconnect), Draining },
        { (Draining, Drained), Disconnected },
        // ... etc
    };
}
```

**Impact:** Eliminates race conditions in connection management. Makes invalid state transitions compile-time or throw-immediately-time errors. Simplifies reconnection logic. State transitions become observable (logging, metrics, testing).

---

## 12. Compile-Time Architectural Boundary Enforcement

**Current state:** The `BannedReferences.txt` in the Domain project documents that Domain should not reference Application, Infrastructure, Storage, or Core. But this is documentation only — the `.csproj` project reference structure provides the actual enforcement. If someone adds a `<ProjectReference>` to Infrastructure from Domain, it compiles fine.

**Problem:** Architectural boundaries are enforced by convention and code review, not by tooling. As the team grows or AI agents make changes, layer violations can be introduced silently. The current enforcement is project-level (no reference means no access), but within projects, internal coupling is unchecked.

**Improvement:**

1. **Roslyn Analyzer for Layer Boundaries:** Create a custom analyzer that reads `BannedReferences.txt` and emits compiler errors for any `using` statement that references a banned namespace.

2. **ArchUnit-style tests:** Add architectural fitness tests:
```csharp
[Fact]
public void Domain_Should_Not_Reference_Infrastructure()
{
    var result = Types.InAssembly(typeof(MarketEvent).Assembly)
        .Should().NotHaveDependencyOn("Meridian.Infrastructure")
        .GetResult();
    result.IsSuccessful.Should().BeTrue();
}
```

3. **Module-level access control:** Use `[InternalsVisibleTo]` more deliberately. Currently, internal types in one project are sometimes visible to test projects but also accidentally to other production projects.

**Impact:** Prevents architectural erosion. Makes layer violations impossible to commit. Provides fast feedback during development rather than in code review.

---

## 13. Implement Data Lineage as a First-Class Pipeline Concept

**Current state:** `DataLineageService` exists in Storage but is a separate service that must be called explicitly. Events carry `Source` and `CanonicalizationVersion` fields, but there is no systematic tracking of which transformations an event has passed through.

**Problem:** When debugging data quality issues in production, the question "how did this event get here and what happened to it along the way?" is hard to answer. Was it canonicalized? By which version? Was it validated? Did it pass the bad-tick filter? Was it deduplicated? None of this is recorded on the event itself.

**Improvement:** Add a lineage chain to every event:

```csharp
public sealed record EventLineage(
    ImmutableArray<LineageEntry> Entries
)
{
    public EventLineage Append(string stage, string detail) =>
        new(Entries.Add(new LineageEntry(stage, detail, DateTimeOffset.UtcNow)));
}

public sealed record LineageEntry(
    string Stage,       // "ingestion", "canonicalization", "validation", "dedup", "storage"
    string Detail,      // "alpaca-ws", "v3", "passed", "duplicate-dropped", "jsonl-written"
    DateTimeOffset Timestamp
);
```

Each pipeline middleware appends to the lineage. The storage sink writes the lineage alongside the event data. Query APIs can filter by lineage stage.

**Impact:** Full observability into the event transformation chain. Dramatically simplifies debugging data quality issues. Enables "what-if" analysis (replay with different pipeline configuration).

---

## 14. Replace Runtime Provider Discovery with Source-Generated Registration

**Current state:** `ServiceCompositionRoot` uses reflection (`GetCustomAttribute<DataSourceAttribute>()`, `Activator.CreateInstance()`) to discover and register providers at runtime. This is in `RegisterStreamingFactoriesFromAttributes()`.

**Problem:** Reflection-based discovery is:
- Silent on failure (if a provider's constructor signature changes, `Activator.CreateInstance` throws at runtime, not compile time)
- Not trimming-compatible (breaks with .NET AOT/trimming)
- Not debuggable (hard to tell which providers were actually registered)
- Slow (reflection on startup)

**Improvement:** Use a C# source generator:

```csharp
[GenerateProviderRegistry]
public partial class ProviderRegistry
{
    // Source generator scans for [DataSource] attributes and generates:
    // partial void RegisterAllProviders(IServiceCollection services) { ... }
}
```

The source generator emits explicit registration code at compile time:
```csharp
// Auto-generated
partial void RegisterAllProviders(IServiceCollection services)
{
    services.AddTransient<IMarketDataClient, AlpacaMarketDataClient>();
    services.AddTransient<IMarketDataClient, PolygonMarketDataClient>();
    // ...
}
```

**Impact:** Provider registration becomes compile-time verified. AOT-compatible. Debugging is trivial (generated code is readable). Startup is faster.

---

## 15. Implement Comprehensive Schema Evolution for Stored Data

**Current state:** `MarketEvent` has `SchemaVersion = 1` hardcoded as a default. The `SchemaVersionManager` exists in Storage/Archival but primarily handles versioning at the file level. There is no mechanism to evolve the JSON schema of stored events over time.

**Problem:** The current JSONL storage format will break if any field is renamed, retyped, or restructured. Old data files become unreadable if the C# record changes. There is no upcasting (old schema → new schema) or downcasting capability. This makes the storage format brittle and prevents safe evolution of the domain model.

**Improvement:**

1. **Schema registry:** Register each event type's JSON schema with a version number. Store the schema alongside the data.

2. **Upcasters:** For each schema version transition, define a transformation:
```csharp
public interface IEventUpcaster
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonElement Upcast(JsonElement oldEvent);
}
```

3. **Read-side adaptation:** When reading old JSONL files, the reader applies upcasters in sequence to bring events to the current schema version.

4. **Write-side stamping:** Every written event includes its schema version, making the data self-describing.

**Impact:** Stored data survives domain model evolution. Old datasets remain queryable forever. Schema changes become safe operations rather than migration nightmares.

---

## Summary: Impact Ranking

| # | Improvement | Impact Area |
|---|-------------|-------------|
| 1 | Strong domain types | Bug prevention, API clarity |
| 2 | Middleware pipeline | Extensibility, testability |
| 3 | Unified domain model | Simplicity, reduced bugs |
| 4 | Event sourcing backbone | Replay, time-travel, querying |
| 5 | Contract test suite | Provider reliability |
| 6 | Discriminated union payloads | Type safety, exhaustiveness |
| 7 | Backpressure propagation | Reliability under load |
| 8 | Plugin storage sinks | Extensibility |
| 9 | Golden master replay tests | Regression detection |
| 10 | Zero-allocation hot path | Performance at scale |
| 11 | Connection state machine | Reliability, debuggability |
| 12 | Compile-time boundary enforcement | Architectural integrity |
| 13 | First-class data lineage | Observability, debugging |
| 14 | Source-generated provider registry | Correctness, AOT compat |
| 15 | Schema evolution for storage | Data longevity |

---

## Part 2 — Output Quality & Correctness (2026-03-03)

> **Date:** 2026-03-03
> **Scope:** Code generalization and output program quality — effort is not a factor.

### Executive Summary

After a deep analysis of the entire codebase — domain modeling, provider infrastructure, event pipeline, storage, testing, CI/CD, and F# integration — the following are the highest-impact improvements ranked by how much they would improve the **generality**, **correctness**, **robustness**, and **output quality** of the system.

---

## 1. Unified Event Schema Evolution & Versioning

**Current state:** `MarketEvent` carries a `SchemaVersion = 1` field, but there is no actual schema evolution mechanism. The `ISchemaUpcaster` interface exists in Contracts but has no implementations. If the event shape changes, all stored JSONL becomes silently incompatible.

**Improvement:** Implement a real schema evolution pipeline:
- Register upcasters per (fromVersion → toVersion) pair.
- On deserialization (replay, WAL recovery, package import), automatically apply the upcaster chain.
- Add a `--migrate-storage` CLI command that rewrites files to the latest schema version.
- Store the schema version in JSONL file headers (first line metadata) and Parquet file metadata.
- Add a CI check that fails if a MarketEventPayload-derived type changes shape without incrementing SchemaVersion and adding an upcaster.

**Why it matters:** Without this, any domain model evolution silently breaks downstream consumers, replay, backfill comparison, and portable data packages. This is the single most dangerous gap for a system whose purpose is long-term data archival.

---

## 2. Replace Stringly-Typed Provider/Symbol/Venue Identifiers with Strong Types

**Current state:** Provider names, symbols, venues, and stream IDs are all `string`. The codebase normalizes these in scattered ad-hoc ways (`ToLowerInvariant()`, `ToUpperInvariant()`, trim). `ProviderId`, `SymbolId`, and `VenueCode` strong types exist in `Contracts/Domain/` but are not used consistently — most code still passes raw strings.

**Improvement:** Enforce strong types throughout the hot path:
- Make `ProviderId`, `SymbolId`, and `VenueCode` the **only** way to reference these concepts.
- These types should enforce normalization at construction (e.g., `SymbolId` always uppercases, `ProviderId` always lowercases).
- Eliminate all `string symbol` parameters in favor of `SymbolId symbol` in provider interfaces, pipeline, storage, and monitoring.
- Add implicit conversions from string for ergonomics but validate on construction.
- Use these types as dictionary keys to eliminate case-sensitivity bugs.

**Why it matters:** String-typed identifiers are the #1 source of subtle bugs in market data systems. A symbol that is "AAPL" in the provider, "aapl" in storage, and "Aapl" in the dashboard creates three separate data streams that look identical to the user. The existing `EffectiveSymbol` property is a band-aid. Strong types make this class of bug structurally impossible.

---

## 3. Make the F# Validation Pipeline a First-Class Citizen in the Event Flow

**Current state:** The F# validation pipeline (`Transforms.fs`, `ValidationPipeline.fs`) is sophisticated — railway-oriented, applicative, composable — but it sits *beside* the C# pipeline rather than *inside* it. Events flow from collectors → EventPipeline → storage without passing through F# validation. The F# layer is used for enrichment and analysis but not for gating.

**Improvement:** Integrate F# validation as an optional, configurable stage in the EventPipeline:
- Add a `IEventValidator` interface that the pipeline calls between receive and persist.
- The default implementation delegates to the F# `ValidationPipeline`.
- Events that fail validation are routed to a dead-letter sink (separate JSONL file) with the full `ValidationError` list, rather than being silently dropped or persisted with bad data.
- Expose validation metrics (pass rate, error distribution by type) via Prometheus.
- Allow per-symbol validation config (e.g., relaxed thresholds for illiquid symbols).

**Why it matters:** The system currently persists all events regardless of validity. A bad tick from a provider (negative price, zero quantity, future timestamp) gets stored and pollutes downstream analysis. The F# validation code is already written and excellent — it just isn't wired into the hot path.

---

## 4. Implement Proper WAL Corruption Recovery

**Current state:** The WAL (`WriteAheadLog`) has append, commit, truncate, and recovery. But there is no handling for partial writes or corrupted records. If the process crashes mid-write, the WAL file may contain a truncated JSON line. The recovery code (`GetUncommittedRecordsAsync`) will throw a deserialization exception and skip the record, logging a warning — but it doesn't know whether the *next* record is also corrupt (shifted bytes).

**Improvement:**
- Add length-prefixed framing to WAL records: `[4-byte length][payload][checksum]`.
- On recovery, if a record fails checksum validation, scan forward to the next valid frame boundary.
- Track and report the number of corrupted vs. recovered records.
- Add a `--wal-repair` CLI command that dumps corrupted records for manual inspection.
- Add a fuzz test that writes partial records and verifies recovery correctness.

**Why it matters:** A WAL that silently loses data on crash is worse than no WAL at all, because it gives a false sense of durability. The current checksum field is populated but never validated during recovery.

---

## 5. Introduce a Query/Replay Abstraction Over Stored Data

**Current state:** Querying stored data is fragmented across `HistoricalDataQueryService`, `MemoryMappedJsonlReader`, `JsonlReplayer`, `StorageSearchService`, and `StorageCatalogService`. Each has different capabilities, different APIs, and different assumptions about file layout. There is no unified way to say "give me all trades for AAPL between 10:00 and 10:05 on 2026-01-15."

**Improvement:** Create a unified `IMarketDataStore` read abstraction:
```csharp
public interface IMarketDataStore
{
    IAsyncEnumerable<MarketEvent> QueryAsync(
        MarketDataQuery query,
        CancellationToken ct = default);
}

public sealed record MarketDataQuery(
    SymbolId? Symbol = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    MarketEventType? EventType = null,
    string? Source = null,
    int? Limit = null);
```
- Implement it for JSONL, Parquet, and composite (tiered) storage.
- Use this single interface for replay, export, the HTTP query endpoints, quality monitoring, and the desktop data browser.
- Add predicate pushdown for Parquet (column pruning, row group filtering).
- This unifies the 5+ current query paths into one tested, optimized implementation.

**Why it matters:** The current fragmentation means each consumer reimplements filtering, date parsing, and file discovery. Bugs fixed in one path don't propagate. The export service, the replay endpoint, the data browser, and the quality monitor all independently enumerate files — and may produce different results for the same query.

---

## 6. End-to-End Contract Testing for the Provider → Storage Round-Trip

**Current state:** Provider tests mock the publisher. Pipeline tests mock the sink. Storage tests mock the events. There is no test that verifies: "If Alpaca sends this WebSocket message, the correct MarketEvent ends up in JSONL with the right fields." The closest tests are `AlpacaQuoteRoutingTests`, which test collector → publisher but skip deserialization and storage.

**Improvement:** Add contract tests that exercise the full pipeline per provider:
- Given: a recorded WebSocket message (or HTTP response) from each provider.
- When: processed through the actual adapter → collector → pipeline → storage sink.
- Then: the stored JSONL line deserializes to a `MarketEvent` with exactly the expected fields.
- Store the recorded messages as test fixtures (golden files).
- Run these tests in CI on every PR.
- This catches: deserialization regressions, field mapping errors, sequence number drift, timestamp timezone bugs, and symbol normalization inconsistencies.

**Why it matters:** The most common class of bugs in market data systems is "the field was there but had the wrong value." Unit tests with mocks can't catch these because they mock away the transformation layer. A golden-file contract test catches the exact bugs that matter most.

---

## 7. Make the Composite Sink Failure Model Explicit and Configurable

**Current state:** `CompositeSink` writes to multiple sinks (JSONL + Parquet). If one sink throws, it logs a warning and continues. There is no tracking of per-sink failure state, no circuit breaker, and no way for callers to know that data is being written to JSONL but not Parquet.

**Improvement:**
- Add a per-sink health state (Healthy, Degraded, Failed) with automatic circuit breaking.
- Expose sink health as a Prometheus metric and health endpoint.
- Add a configurable failure policy: `ContinueOnPartialFailure` (current default), `FailOnAnyFailure`, `RequireQuorum(n)`.
- When a sink recovers from failure, backfill the missed events from the healthy sink(s) if the data window is within the hot tier.
- Log a structured event (`SinkDegradation`) that the monitoring pipeline can alert on.

**Why it matters:** Silent partial failure is the worst failure mode for a data collection system. Users believe they have Parquet files but they're missing hours of data because Parquet conversion hit an edge case. The current behavior optimizes for availability (keep writing to at least one sink) but sacrifices visibility.

---

## 8. Formalize the Provider Capability Model for Smarter Orchestration

**Current state:** `ProviderCapabilities` is a flat record with boolean flags (`SupportsTrades`, `SupportsQuotes`, `SupportsDepth`). The `CompositeHistoricalDataProvider` uses provider priority (an integer) for fallback ordering. There is no structured way to express: "Polygon supports 1-minute bars for US equities but not for crypto" or "Alpaca's free tier is delayed 15 minutes."

**Improvement:** Replace the flat capability model with a structured one:
```csharp
public sealed record ProviderCapabilities(
    IReadOnlySet<AssetClass> SupportedAssetClasses,
    IReadOnlySet<MarketEventType> SupportedEventTypes,
    IReadOnlySet<string> SupportedBarIntervals,
    IReadOnlySet<string> SupportedExchanges,
    DataFreshness Freshness,         // Realtime, Delayed15Min, EndOfDay
    DateOnly? EarliestHistoricalDate,
    RateLimitProfile RateLimit,
    bool RequiresPaidSubscription);
```
- Use this to auto-select the best provider for a given request (symbol + date range + data type).
- Replace the linear priority integer with constraint-based routing.
- Enable the backfill system to automatically split requests across providers: "Use Alpaca for 2024 intraday, Stooq for 2020 daily."
- Surface capability gaps in the UI: "No provider supports L2 data for this exchange."

**Why it matters:** The current priority-based fallback is simple but wasteful. It tries providers in order until one succeeds, even if the first 3 providers are known not to support the requested asset class. Structured capabilities turn O(n) fallback into O(1) routing.

---

## 9. Enforce Structural Consistency via Roslyn Analyzers

**Current state:** The CLAUDE.md documents many rules (sealed classes, CancellationToken, no Task.Run for I/O, structured logging, [ImplementsAdr] attributes). These rules are enforced by convention and AI agent review, not by the compiler. The `ai-repo-updater.py` script detects violations after the fact.

**Improvement:** Write custom Roslyn analyzers for the project's critical invariants:
- **MDC001:** All public classes must be sealed unless they have `[AllowInheritance]`.
- **MDC002:** All async methods must accept `CancellationToken`.
- **MDC003:** Logger calls must not use string interpolation (detect `$"` inside `Log*` calls).
- **MDC004:** Classes implementing `IMarketDataClient` or `IHistoricalDataProvider` must have `[ImplementsAdr]` and `[DataSource]` attributes.
- **MDC005:** `new HttpClient()` is forbidden — use `IHttpClientFactory`.
- **MDC006:** `Task.Run` calls must have a justifying comment.
- **MDC007:** `PackageReference` items must not have `Version` attributes (CPM enforcement).
- Ship these as a local analyzer project referenced by `Directory.Build.props`.
- Set them to `Error` severity so they fail the build.

**Why it matters:** Convention-based rules decay over time. Every new contributor (human or AI) must rediscover them. Analyzer-based rules are permanent, produce IDE squiggles during development, and fail CI before code review. This converts ~687 bare `catch (Exception)` blocks and ~8 `Task.Run` I/O violations from "known technical debt" to "compilation errors."

---

## 10. Build a Deterministic Data Replay System

**Current state:** `JsonlReplayer` can replay stored JSONL files, but it does not replay at the original event rate. There is no way to simulate "what would have happened if I ran my strategy against the 2025-Q3 data at production speed." The replay is batch (as fast as possible), not temporal.

**Improvement:** Build a deterministic replay engine:
- Read stored events and emit them to a `Channel<MarketEvent>` at the original inter-event timing.
- Support speed multipliers (1x real-time, 10x, 100x, max-speed).
- Allow injecting faults (dropped events, delayed events, provider failover) to test resilience.
- Wire this into the existing EventPipeline so that downstream consumers (monitoring, indicators, storage) behave identically to live operation.
- Add a `--replay` CLI mode: `--replay --replay-source ./data/live/alpaca/2025-07-15/ --replay-speed 10x`.
- Enable the QuantConnect Lean integration to consume replay data as if it were live.

**Why it matters:** Market data collection systems are notoriously hard to test in production-like conditions because markets are only open during trading hours. A deterministic replay system lets developers validate changes against real historical data at any time of day, with exactly reproducible results.

---

## 11. Eliminate the Endpoint Stub Problem

**Current state:** The API declares 287 route constants. Approximately 15-20 endpoints return hardcoded stub data (e.g., `MessagingEndpoints.cs` — all 7 endpoints return `queued=0, running=false`). `ReplayEndpoints.cs` returns empty arrays. These stubs are indistinguishable from working endpoints to API consumers.

**Improvement:**
- **Remove or gate stub endpoints.** Endpoints that don't work should not be discoverable. Options:
  - Return `501 Not Implemented` with a structured body: `{ "status": "not_implemented", "planned_version": "1.8.0" }`.
  - Gate them behind a `--enable-experimental-endpoints` flag.
  - Remove them from the route table entirely until implemented.
- **Add an endpoint health matrix** to the `/api/status` response that reports per-endpoint implementation status.
- **Add integration tests** that verify every registered endpoint returns something other than 501 — this prevents regressions where a working endpoint accidentally becomes a stub.

**Why it matters:** Stub endpoints that return `200 OK` with empty data are API contract violations. Downstream consumers (dashboards, scripts, other services) will silently behave incorrectly. A 501 is honest; a 200 with fake data is a lie.

---

## 12. Unify and Harden the Reconnection Model

**Current state:** WebSocket reconnection is handled differently by different providers:
- `AlpacaMarketDataClient` uses `WebSocketConnectionManager` (centralized).
- `PolygonMarketDataClient` uses both `WebSocketConnectionManager` and `WebSocketReconnectionHelper`.
- `StockSharpMarketDataClient` has its own reconnection logic.
- `IBMarketDataClient` delegates to `EnhancedIBConnectionManager` with a different retry model.

**Improvement:**
- All providers should use `WebSocketConnectionManager` (or a higher-level `IConnectionManager` interface).
- Extract the reconnection strategy into a pluggable policy:
  - `ExponentialBackoffReconnectionPolicy` (current default).
  - `MarketHoursAwareReconnectionPolicy` (don't reconnect outside trading hours).
  - `CircuitBreakerReconnectionPolicy` (stop after N consecutive failures).
- Add reconnection event telemetry: time-to-reconnect, subscription re-registration duration, events missed during downtime.
- Implement **subscription state journaling**: on reconnect, automatically re-subscribe to the exact set of symbols that were active, without the provider adapter needing to track this.

**Why it matters:** Reconnection bugs are the #1 operational issue in streaming systems. If each provider handles reconnection differently, each provider has different reconnection bugs. A unified model means one implementation to get right and one set of tests to validate.

---

## 13. Add Property-Based / Fuzz Testing for the Data Path

**Current state:** Tests use hand-crafted fixtures with specific values. This validates known scenarios but misses edge cases that humans don't think of (e.g., `decimal.MaxValue` prices, symbols with unicode characters, timestamps at `DateTimeOffset.MinValue`).

**Improvement:**
- Add [FsCheck](https://github.com/fscheck/FsCheck) or Hedgehog for property-based testing of the core data path:
  - **MarketEvent round-trip:** For any valid MarketEvent, serializing to JSON and deserializing produces an identical event.
  - **WAL durability:** For any sequence of append/commit/crash/recover operations, no committed event is lost.
  - **Validation completeness:** For any TradeEvent, the F# validator either accepts it or returns at least one specific error (never silently passes invalid data).
  - **Storage path determinism:** For any MarketEvent, the storage policy produces the same path regardless of call order.
- Add fuzzing for the WebSocket message parsers (Alpaca JSON, Polygon JSON, IB binary).

**Why it matters:** The F# codebase is already structurally suited for property-based testing. The `ValidationResult<T>` type, the pure calculation functions, and the immutable records are ideal inputs for QuickCheck-style generators. This would catch the class of bugs that hand-crafted tests systematically miss.

---

## 14. Implement Cross-Provider Data Reconciliation

**Current state:** `CrossProviderComparisonService` exists and can compare data across providers, but it's a monitoring tool — it reports differences, it doesn't resolve them. When Alpaca says AAPL traded at 150.25 and Polygon says 150.26, the system stores both without reconciliation.

**Improvement:**
- Add a reconciliation pipeline that runs after collection:
  - For each symbol, compare events from all active providers within a time window.
  - Identify discrepancies (price differences, missing events, timestamp disagreements).
  - Apply a configurable reconciliation strategy:
    - **Majority vote:** If 2 of 3 providers agree, use that value.
    - **Authority source:** Always prefer the exchange-direct feed.
    - **Conservative:** Flag discrepancies for manual review.
  - Produce a reconciled output stream alongside the raw streams.
  - Store reconciliation decisions as metadata (which provider won, what the delta was).

**Why it matters:** Multi-provider collection is only valuable if you can merge the streams into a single source of truth. Without reconciliation, downstream consumers must pick a provider and hope it was correct. This is especially critical for backtesting, where a 1-cent price difference on a high-volume trade can flip a strategy's P&L.

---

## 15. Decouple the HTTP API from the Collection Engine

**Current state:** The HTTP API (endpoints, dashboard) runs in-process with the collection engine. `Program.cs` starts both the collector and the web server in the same process. This means:
- Restarting the API restarts collection (dropping data).
- A buggy endpoint handler can crash the collector.
- Scaling the API independently of the collector is impossible.

**Improvement:**
- Separate the API into a standalone project that reads from the same storage (JSONL/Parquet files, WAL) but does not own the collection process.
- The collector exposes a minimal status/health endpoint but delegates all API functionality to the separate process.
- Communication between the two processes happens via:
  - Shared filesystem (for stored data).
  - A lightweight IPC channel (Unix domain socket or named pipe) for live status.
  - The existing WAL for crash-consistent reads.
- This enables: independent deployment, independent scaling, independent restarts, and cleaner testing.

**Why it matters:** The current architecture couples the most latency-sensitive component (real-time data collection) with the least latency-sensitive component (HTTP API for dashboards). A single slow API request handler can cause backpressure in the event pipeline. Decoupling these is the single highest-impact architectural change for production reliability.

---

## 16. Implement Structured Concurrency for Provider Lifecycle

**Current state:** Provider lifecycle (connect, subscribe, receive, reconnect, disconnect) is managed through individual async methods and event handlers. There is no structured cancellation scope — if `ConnectAsync` succeeds but `SubscribeMarketDepth` throws, the connection may leak.

**Improvement:**
- Adopt a structured concurrency model where each provider's lifecycle is a single cancellation scope:
  ```
  Provider Scope (CancellationToken)
  ├── Connection Task
  │   ├── Authentication
  │   └── Heartbeat Loop
  ├── Subscription Tasks (one per symbol)
  │   ├── Subscribe
  │   └── Receive Loop
  └── Reconnection Supervisor
  ```
- If any child task fails fatally, the entire scope is cancelled and the reconnection supervisor handles restart.
- If the parent scope is cancelled (shutdown), all child tasks are cancelled in reverse order with configurable drain timeouts.
- This prevents resource leaks, orphaned connections, and the subtle bugs where a provider is "connected" but not "subscribed."

**Why it matters:** The current provider lifecycle code is correct but fragile — it depends on developers remembering to handle every failure path. Structured concurrency makes correct cleanup automatic.

---

## Summary: Impact vs. Risk Matrix

| # | Improvement | Impact on Data Quality | Impact on Code Quality | Risk |
|---|------------|----------------------|----------------------|------|
| 1 | Schema evolution | **Critical** | Medium | Low |
| 2 | Strong-typed identifiers | High | **Critical** | Medium |
| 3 | F# validation in pipeline | **Critical** | High | Low |
| 4 | WAL corruption recovery | **Critical** | Medium | Medium |
| 5 | Unified query abstraction | High | **Critical** | Low |
| 6 | End-to-end contract tests | **Critical** | High | Low |
| 7 | Composite sink failure model | High | Medium | Low |
| 8 | Structured capabilities | High | High | Low |
| 9 | Roslyn analyzers | Medium | **Critical** | Low |
| 10 | Deterministic replay | High | Medium | Medium |
| 11 | Eliminate stub endpoints | Medium | High | Low |
| 12 | Unified reconnection | High | High | Medium |
| 13 | Property-based testing | High | High | Low |
| 14 | Cross-provider reconciliation | **Critical** | Medium | High |
| 15 | Decouple API from collector | High | High | High |
| 16 | Structured concurrency | Medium | High | Medium |

**Top 5 by pure output quality improvement:** #1 (schema), #3 (validation), #4 (WAL), #6 (contract tests), #14 (reconciliation)

**Top 5 by code generalization improvement:** #2 (strong types), #5 (query abstraction), #9 (analyzers), #8 (capabilities), #12 (reconnection)

---

## Implementation Status Summary (2026-03-19)

This brainstorm identified 16 high-impact improvements that have been incorporated into the ongoing ROADMAP and quarterly planning:

### Implemented (Complete or Substantial Progress)

| # | Improvement | Status | Details |
|---|------------|--------|---------|
| 1 | Schema evolution | ✅ Complete | `SchemaUpcasterRegistry` + transparent migration on read |
| 3 | F# validation in pipeline | 🔄 Partial | Validation layer exists; deeper integration deferred |
| 4 | WAL corruption recovery | ✅ Complete | `WalCorruptionMode` enum (Skip/Alert/Halt); configurable behavior |
| 6 | End-to-end contract tests | 🔄 Partial | Provider contract tests framework; not all providers covered |
| 10 | Deterministic replay testing | 🔄 Partial | `EventReplayService` + infrastructure; golden master tests pending |
| 12 | Unified reconnection | 🔄 Partial | `WebSocketReconnectionHelper` standardized; consolidation planned |

### Roadmapped for Future Phases

| # | Improvement | Roadmap Phase | Timeframe |
|---|------------|---------------|-----------|
| 2 | Strong-typed identifiers | Phase 13 | Q3-Q4 2026 |
| 5 | Unified query abstraction | Phase 14 | Q4 2026+ |
| 7 | Composite sink failure model | Phase 10 | Q2 2026 |
| 8 | Structured capabilities | Phase 12 | Q3 2026 |
| 9 | Roslyn analyzers for validation | Phase 12 | Q3 2026 |
| 11 | Eliminate stub endpoints | Phase 11 | Q2-Q3 2026 |
| 13 | Property-based testing | Phase 10 | Q2 2026 |
| 14 | Cross-provider reconciliation | Phase 15 | Q4 2026+ |
| 15 | Decouple API from collector | Phase 10 | Q2 2026 |
| 16 | Structured concurrency for providers | Phase 9 | Q2 2026 |

**Verdict:** Original brainstorm provided valuable input into long-term architectural planning. 6 improvements complete or substantially progressed. 10 improvements roadmapped across Phases 9-15 with clear delivery targets. Most critical output quality improvements (schema, validation, WAL) already complete.
