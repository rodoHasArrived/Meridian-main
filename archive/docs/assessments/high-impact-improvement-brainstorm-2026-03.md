# High-Impact Improvement Brainstorm — March 2026

**Date:** 2026-03-01 (initial brainstorm)
**Last Reconciled:** 2026-03-24
**Status:** Archived historical brainstorm; use `docs/status/IMPROVEMENTS.md`, `docs/status/ROADMAP.md`, and `docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md` for active execution tracking
**Author:** Architecture Review

> **Archived location:** `archive/docs/assessments/high-impact-improvement-brainstorm-2026-03.md`
>
> This document is retained for decision history and defect-discovery context, not as the live execution backlog.

> **Scope**: Ideas that meaningfully improve the quality of the codebase and the
> correctness/reliability of the running program. Implementation effort is
> explicitly **not** a filter — only impact matters.

---

## Current-State Reconciliation (2026-03-24)

This document is preserved as a **high-impact ideation artifact**, not the active backlog source.

- The initial 2026-03-01 assessment and ratings remain useful as defect discovery context.
- Delivery ownership has since moved to normalized status docs and delivery waves.
- Core platform improvement themes are now tracked as complete in `docs/status/IMPROVEMENTS.md` (A-G and canonicalization theme J).
- Remaining platform-critical work is now sequenced in `docs/status/ROADMAP.md`, especially provider confidence hardening, paper trading cockpit wiring, promotion workflow, and portfolio depth.

**How to use this file now:**
1. Read it to understand why certain fixes were prioritized in early March 2026.
2. Do **not** treat this file as the source of truth for current delivery status.
3. For sprint planning, prioritize `FULL_IMPLEMENTATION_TODO_2026_03_20.md` + roadmap wave sequencing.

---

## Contents

- [Current-State Reconciliation (2026-03-24)](#current-state-reconciliation-2026-03-24)
- [Executive Assessment](#executive-assessment)
- [Category 1: Data Integrity & Correctness](#category-1-data-integrity--correctness)
- [Category 2: Resource Management & Stability](#category-2-resource-management--stability)
- [Category 3: Architectural Improvements](#category-3-architectural-improvements)
- [Category 4: Observability & Operational Excellence](#category-4-observability--operational-excellence)
- [Category 5: Test Infrastructure](#category-5-test-infrastructure)
- [Category 6: Code Quality & Maintainability](#category-6-code-quality--maintainability)
- [Category 7: Correctness by Construction](#category-7-correctness-by-construction)
- [Priority Matrix](#priority-matrix)
- [Implementation Follow-Up (2026-03-10)](#implementation-follow-up-2026-03-10)
- [Implementation Follow-Up (2026-03-11)](#implementation-follow-up-2026-03-11)
- [Implementation Follow-Up (2026-03-12)](#implementation-follow-up-2026-03-12)
- [Implementation Summary (Historical Snapshot — 2026-03-19)](#implementation-summary-historical-snapshot--2026-03-19)
- [Recommended Next-Step Sources (2026-03-24)](#recommended-next-step-sources-2026-03-24)

---

## Executive Assessment

The codebase is **architecturally sound at the macro level** — provider
abstraction, tiered storage, data quality monitoring, and domain modelling are
all well-designed. But deep analysis reveals **critical implementation-level
defects** hiding behind that good architecture: race conditions in the event
pipeline flush, WAL durability gaps, silent data corruption in provider parsing,
memory leaks in monitoring, and an under-utilized F# integration. The system
looks production-ready from the outside but has subtle failure modes that would
cause silent data loss, incorrect metrics, or resource exhaustion under real
market conditions.

**Overall rating: 6.5/10 — Architecturally Sound, Operationally Risky**

| Component | Design | Implementation | Robustness |
|-----------|--------|----------------|------------|
| Event Pipeline | 9/10 | 5/10 | 5/10 |
| Write-Ahead Log | 8/10 | 4/10 | 4/10 |
| Alpaca Client | 7/10 | 4/10 | 4/10 |
| WebSocket Resilience | 8/10 | 5/10 | 6/10 |
| Data Quality Monitoring | 9/10 | 6/10 | 6/10 |
| Domain Models | 9/10 | 8/10 | 8/10 |
| Configuration System | 8/10 | 7/10 | 7/10 |
| Test Suite | 8/10 | 6/10 | 6/10 |
| F# Integration | 7/10 | 5/10 | 5/10 |

> **Note:** Ratings above reflect the initial assessment (2026-03-01). Follow-up
> implementations since then have improved the Event Pipeline, WebSocket
> Resilience, and Data Quality Monitoring components in particular.

### Progress by Category

| Category | Done | Partial | Open | Total |
|----------|------|---------|------|-------|
| 1 — Data Integrity & Correctness | 3 | 1 | 1 | 5 |
| 2 — Resource Management & Stability | 6 | 0 | 0 | 6 |
| 3 — Architectural Improvements | 1 | 0 | 5 | 6 |
| 4 — Observability & Operational Excellence | 4 | 0 | 0 | 4 |
| 5 — Test Infrastructure | 1 | 0 | 3 | 4 |
| 6 — Code Quality & Maintainability | 0 | 0 | 3 | 3 |
| 7 — Correctness by Construction | 0 | 0 | 3 | 3 |
| **Total** | **15** | **1** | **15** | **31** |

---

## Category 1: Data Integrity & Correctness

These are bugs that cause **silent data loss or corruption** in the running
program. Fixing any one of them directly improves the trustworthiness of every
byte of data the system collects.

### 1.1 EventPipeline Flush Semantics Are Broken — ✅ Done (2026-03-12)

**File**: `src/Meridian.Application/Pipeline/EventPipeline.cs`

The `FlushAsync()` completion condition counts dropped events as "accounted
for":

```csharp
// Current (broken):
if (consumed + dropped >= targetPublished) break;
```

In `DropOldest` mode this breaks **immediately** after publishing because
dropped events count toward the target, even though they were never written to
storage. The caller receives a successful flush even though data was silently
discarded.

**Impact**: Any code that calls `FlushAsync()` and then trusts that all data is
persisted is wrong. This affects shutdown, checkpoint operations, and any
user-facing "data saved" confirmation.

**Fix**: Only break when `consumed >= targetPublished`. Dropped events should be
tracked separately and reported, not conflated with successful persistence.

### 1.2 WAL-to-Sink Transaction Gap — 📝 Open

**File**: `src/Meridian.Storage/Archival/WriteAheadLog.cs` and
`EventPipeline.cs`

The consumer loop appends to the WAL, then writes to the sink, then commits the
WAL:

```
WAL.Append(event) → Sink.Append(event) → Sink.Flush() → WAL.Commit()
```

If the sink write fails **after** WAL append succeeds, the WAL still has the
record. On recovery, those events replay and may duplicate into the sink. Worse:
if `Sink.Flush()` succeeds but the process crashes before `WAL.Commit()`, the
WAL recovery replays already-persisted events — creating duplicates with no
detection mechanism.

**Impact**: After any crash or restart, the stored data may contain duplicate
events. For market data research, this corrupts volume statistics, VWAP
calculations, and trade-flow analysis.

**Fix**: Implement idempotent sink writes (dedup by sequence number) or use a
two-phase commit where the WAL commit only happens after verified sink
persistence.

### 1.3 Alpaca Price Precision Loss — ✅ Done (2026-03-10)

**File**: `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs`

Trade prices are parsed via `GetDouble()` and then cast to `decimal`:

```csharp
var price = el.TryGetProperty("p", out var pProp) ? (decimal)pProp.GetDouble() : 0m;
```

`double` has ~15-17 significant digits; `decimal` has 28-29. But the
**conversion path** `JSON string → double → decimal` introduces floating-point
representation errors. A price of `123.455` might become `123.45499999999999`
after the round-trip.

Additionally, trade size uses `GetInt32()` but large block trades can exceed
`int.MaxValue`. Timestamps silently fall back to `DateTimeOffset.UtcNow` on
parse failure — recording the wrong time rather than flagging the error.

**Impact**: Price corruption on high-precision assets (crypto, forex). Volume
truncation on large block trades. Incorrect timestamps on malformed messages.

**Fix**: Parse prices as `GetDecimal()` or parse the raw string. Use `GetInt64()`
for sizes. Reject events with unparseable timestamps rather than substituting.

### 1.4 No Trade Message Deduplication — ✅ Done (2026-03-10)

**File**: `AlpacaMarketDataClient.cs` (and likely other providers)

Alpaca's WebSocket API is known to send duplicate trade messages. The client has
no deduplication logic — identical trades are published to the pipeline twice.
The pipeline's `PersistentDedupLedger` exists but is sequence-based, not
content-based, so if the same trade arrives with different sequence numbers it
passes through.

**Impact**: Inflated volume metrics, broken VWAP, incorrect order-flow
statistics. Every downstream consumer sees phantom trades.

**Fix**: Implement content-based deduplication using a sliding window of
`(symbol, price, size, exchange_timestamp)` tuples. Bloom filter or LRU hash
set for memory efficiency.

### 1.5 Completeness Score Miscalculation — 🔄 Partial (2026-03-12)

**File**: `src/Meridian.Application/Monitoring/DataQuality/CompletenessScoreCalculator.cs`

Expected events per hour is determined at the moment of the **first event of
the day**. If no liquidity profile has been registered by then, the default
(e.g., 10,000 events/hour) is used. For a symbol that actually trades 100
events/hour, the score calculates as 1% complete — **and this score is locked
for that date forever**.

**Impact**: Completeness dashboards show grossly incorrect scores for any symbol
without a pre-registered liquidity profile. Operators cannot trust the quality
metrics.

**Fix**: Allow dynamic liquidity profile updates mid-day, or recalculate
expected counts based on observed patterns after an initial calibration window.

---

## Category 2: Resource Management & Stability

These issues cause the program to degrade over time through resource exhaustion,
hangs, or cascading failures.

### 2.1 Memory Leaks in Monitoring Services — ✅ Done (2026-03-12)

**Files**: `SequenceErrorTracker.cs`, `GapAnalyzer.cs`,
`CompletenessScoreCalculator.cs`

All three services use `ConcurrentDictionary` keyed by symbol (or
symbol+eventType). Entries are added on first sight of a symbol but **never
removed**. The cleanup timers remove old *data within entries* but never remove
the *entries themselves*.

For a system monitoring 1,000 symbols for a day, all 1,000 dictionary entries
persist in memory forever. Over weeks of operation with symbol rotation (e.g.,
options chains), memory grows unboundedly.

`GapAnalyzer` is particularly bad: it creates separate state for each `symbol ×
eventType` combination (1,000 symbols × 4 types = 4,000 entries, each holding
timestamps, sequences, and pending gap state).

**Impact**: Long-running instances slowly consume more memory until OOM or GC
pressure causes latency spikes.

**Fix**: Implement LRU eviction or time-based expiry on dictionary entries. When
a symbol hasn't been seen for N hours, remove its entry entirely.

### 2.2 Hot-Path Allocations in Data Quality — ✅ Done (2026-03-12)

**File**: `GapAnalyzer.cs`

`GetEffectiveConfig()` is called for **every single event** and creates a new
`GapAnalyzerConfig` record each time via the `with` keyword:

```csharp
return _config with { GapThresholdSeconds = ..., ExpectedEventsPerHour = ... };
```

At 50,000 events/second, this creates 50,000 short-lived objects per second —
pure GC pressure.

**Impact**: Increased GC pause times, higher tail latency, reduced throughput
under load.

**Fix**: Cache computed configs per symbol in a dictionary. Invalidate only when
liquidity profile changes.

### 2.3 Consumer Blocking on Slow Sinks — ✅ Done (2026-03-11)

**File**: `EventPipeline.cs`

The consumer loop calls `Sink.FlushAsync()` synchronously (awaited) in the
consumer loop. If the sink is slow (network storage, disk I/O burst), the
consumer is blocked and the bounded channel fills up, triggering backpressure
and event dropping.

There is no timeout on `Sink.FlushAsync()`. If the sink hangs (e.g., NFS mount
becomes unresponsive), the entire pipeline stalls permanently.

**Impact**: A single slow storage operation cascades into data loss across all
symbols.

**Fix**: Add a configurable timeout to sink operations. Consider double-buffering
(write to buffer while previous buffer flushes). Alert operators when flush
latency exceeds thresholds.

### 2.4 WebSocket Receive Buffer Unbounded — ✅ Done (2026-03-12)

**File**: `WebSocketConnectionManager.cs`

The receive loop uses a fixed 64KB buffer but appends to a `StringBuilder` with
no size limit:

```csharp
var buffer = new byte[64 * 1024];
var messageBuilder = new StringBuilder(128 * 1024);
// Loops until EndOfMessage, no size check
```

A malicious or misbehaving server could send a message that grows the
StringBuilder to gigabytes.

**Impact**: Denial-of-service via memory exhaustion from a single oversized
WebSocket message.

**Fix**: Add a maximum message size limit (e.g., 10MB). Disconnect and log a
warning if exceeded.

### 2.5 WebSocket Reconnection Race Condition — ✅ Done (2026-03-12)

**File**: `WebSocketConnectionManager.cs`

The `TryReconnectAsync` method checks `_isReconnecting` without acquiring the
semaphore gate first:

```csharp
if (_isReconnecting) return false;        // No lock!
if (!await _reconnectGate.WaitAsync(0, ct)) return false;
```

Two threads can both see `_isReconnecting == false` and proceed past the first
check simultaneously, then race on the semaphore. This can cause duplicate
reconnection attempts or — worse — one thread succeeding while the other
corrupts the connection state.

**Impact**: Duplicate WebSocket connections, wasted subscriptions, inconsistent
state.

**Fix**: Remove the fast-path check. Let the semaphore be the sole gating
mechanism.

### 2.6 No Provider Connection Timeout — ✅ Done (2026-03-11)

**File**: `src/Meridian/Program.cs`

The startup flow calls `await dataClient.ConnectAsync()` with no timeout. If a
provider hangs (firewall silently dropping packets, DNS resolution stalling),
the application hangs forever.

**Impact**: Application fails to start with no error message, no timeout, no
recovery.

**Fix**: Wrap connection in a timeout (e.g., 30 seconds). On timeout, log a
clear error and either fall back to an alternative provider or exit with a
meaningful error code.

---

## Category 3: Architectural Improvements

These changes improve the system's fundamental design, making it more
maintainable, extensible, and correct by construction.

### 3.1 End-to-End Trace Context Propagation — ✅ Done (2026-03-24)

**Current state**: Trace context is captured at pipeline ingress, restored in processing/storage spans, and stamped onto `MarketEvent` records (`TraceId`, `ParentSpanId`) so sinks and logs can correlate operations end-to-end.

**What's missing**: When a trade arrives from Alpaca, gets processed through the
pipeline, validated by data quality, and written to storage — there's no single
trace ID linking all of those operations.

**Impact of improvement**: Operators can trace any latency anomaly from ingestion
to storage in one query. Debugging goes from "search 5 log files and correlate
timestamps manually" to "filter by trace ID."

**Approach**: Wire `System.Diagnostics.Activity` through the pipeline. Tag each
`MarketEvent` with its originating activity context. Propagate to sink
operations.

### 3.2 WebSocket Provider Base Class — 📝 Open

**Current state**: Polygon, NYSE, and StockSharp each implement ~200-300 LOC of
duplicate WebSocket lifecycle code (connect, authenticate, receive loop,
reconnect, heartbeat).

**Impact of improvement**: Bug fixes in reconnection logic apply once instead of
3+ times. New WebSocket providers start from a tested base instead of copying
and adapting. Connection resilience becomes uniform across all providers.

**Approach**:

```csharp
public abstract class WebSocketProviderBase : IMarketDataClient
{
    protected abstract Uri BuildConnectionUri();
    protected abstract Task<bool> AuthenticateAsync(CancellationToken ct);
    protected abstract void HandleMessage(JsonElement message);
    // Shared: connect, reconnect, heartbeat, receive loop, state machine
}
```

### 3.3 Decide the F# Strategy: Deepen or Remove — 📝 Open

**Current state**: 12 F# files vs. 652 C# files. F# provides discriminated
unions for market events, validation pipelines, and spread/imbalance
calculations. But the C# domain collectors (`QuoteCollector`,
`TradeDataCollector`) are the *real* domain logic, and they're mutable.

The F# validation pipeline exists but is rarely called from C#. The interop
layer adds ceremony (type conversion at boundaries) without clear payoff. Tests
exist but the coverage is thin.

**The honest assessment**: The F# integration is at a **dead middle ground** — it
adds surface area and complexity without being deep enough to deliver its
inherent safety benefits. Either commitment is valid:

**Option A — Deepen**: Move all validation, canonicalization, and calculations
into F#. Make C# collectors thin adapters that call F# functions. The type
safety then truly protects the hot path.

**Option B — Remove**: Port the useful F# logic (spread calculation, validation
rules) to C# sealed records and pattern matching. Eliminate the interop layer
and the dual-language build complexity.

**Impact**: Either direction reduces cognitive load and maintenance surface area.
The current state is the worst of both worlds.

### 3.4 Idempotent Storage Writes — 📝 Open

**Current state**: If the same event is written twice (crash recovery, provider
duplication, reconnection replay), the sink stores both copies. There's no
content-based or sequence-based deduplication at the storage layer.

**Impact of improvement**: Crash recovery, provider reconnection, and message
deduplication all become safe by default. The WAL-to-sink transaction gap
(section 1.2) becomes non-critical because duplicate writes are harmlessly
absorbed.

**Approach**: Each storage sink maintains a bloom filter or hash set of recent
`(symbol, sequence, timestamp)` tuples. Events matching an existing entry are
silently deduplicated. The bloom filter is rebuilt from the last N minutes of
stored data on startup.

### 3.5 Configuration Fail-Fast vs. Self-Healing Separation — 📝 Open

**Current state**: `ConfigurationPipeline.ApplySelfHealing()` silently fixes
problems like missing symbols, reversed dates, and unavailable providers. This
is helpful for getting started but dangerous in production — operators don't
know their config was modified.

**Impact of improvement**: Clear separation between "fixable cosmetic issues"
(reversed dates → swap them) and "configuration errors that need human
attention" (missing credentials → fail with actionable error). Production
deployments fail fast on real problems; development environments get helpful
auto-fixes.

**Approach**: Introduce severity levels in self-healing: `AutoFix` (apply
silently), `Warn` (apply but log prominently), `Error` (refuse to start). Let
operators configure the threshold via an environment variable
(`MDC_CONFIG_STRICTNESS=production`).

### 3.6 Proper Backpressure Feedback Loop — ✅ Done (2026-03-11)

**Current state**: `EventPipeline.TryPublish()` returns `bool` but provides no
information about *why* it failed or *what to do*. Publishers have no mechanism
to slow down when the pipeline is under pressure.

**Impact of improvement**: Instead of silently dropping events, the system can
signal providers to pause subscriptions, reduce polling frequency, or queue
locally. This turns uncontrolled data loss into managed flow control.

**Approach**: Return a `PublishResult` enum (`Accepted`, `Queued`,
`BackpressureActive`, `Dropped`) and expose a `BackpressureChanged` event that
providers can subscribe to for proactive throttling.

---

## Category 4: Observability & Operational Excellence

### 4.1 Alert-to-Runbook Linkage — ✅ Done (2026-03-12)

**Current state**: Alert rules exist in `deploy/monitoring/alert-rules.yml` but
don't contain runbook URLs or mitigation steps in their annotations. When an
alert fires, the operator has to search documentation manually.

**Impact**: Embed runbook URLs directly in alert annotations so monitoring tools
(Grafana, PagerDuty) display actionable guidance inline.

### 4.2 Backpressure Alerting Is Single-Shot — ✅ Done (2026-03-12)

**Current state**: The pipeline logs one warning at 80% queue utilization, resets
at 50%. During sustained high load (80-100%), hundreds or thousands of events
can be dropped with only that single warning.

**Impact of improvement**: Continuous alerting during backpressure events. Report
drop rate per second, total events dropped in current episode, and estimated
data loss percentage.

### 4.3 WAL Corruption Alerting — ✅ Done (2026-03-10)

**Current state**: During WAL recovery, invalid checksums are logged at Warning
level and the records are silently skipped. There's no operator alert, no
halt-on-corruption option, and no way to know that 1,000 out of 10,000 records
were silently discarded.

**Impact of improvement**: Configurable corruption response: `Skip` (current
behavior), `Alert` (continue but fire alert), `Halt` (refuse to start until
operator reviews). Default to `Alert` in production.

### 4.4 Provider Health Dashboard — ✅ Done (2026-03-11)

**Current state**: Individual provider metrics exist but there's no unified view
of "which providers are healthy, which are degraded, which are failing, and
what's the overall data collection health?"

**Impact**: A single `/api/providers/dashboard` endpoint that returns a traffic-
light summary: green (all providers healthy), yellow (some degraded, failover
active), red (primary providers down, data at risk).

---

## Category 5: Test Infrastructure

### 5.1 Fix Timing-Dependent Tests — ✅ Done (2026-03-11)

**Current state**: 5+ tests are skipped with `[Fact(Skip = "...")]` because they
rely on timing guarantees (`Task.Delay`) that don't hold in CI environments.
Tests like `QueueUtilization_ReflectsQueueFill` fail because the consumer
drains the queue faster than expected.

**Impact of improvement**: Replace `Task.Delay`-based synchronization with
deterministic signaling (`ManualResetEventSlim`, `TaskCompletionSource`,
`SemaphoreSlim`). Every skipped test is a regression that's not being caught.

### 5.2 Error Injection in Mock Sinks — 📝 Open

**Current state**: The mock storage sink used in pipeline tests only supports a
configurable `ProcessingDelay`. There's no way to inject exceptions, simulate
partial writes, or test concurrent failure scenarios.

**Impact of improvement**: Tests can verify that the pipeline handles sink
failures correctly: retries, reports errors, doesn't corrupt state. Currently
this is untested.

### 5.3 Provider Resilience Test Suite — 📝 Open

**Current state**: Provider tests primarily cover message parsing and
subscription management. There are no tests for: rate limit enforcement across
providers, partial data corruption (malformed JSON), authentication failure
handling, reconnection under message loss, or heartbeat timeout behavior.

**Impact of improvement**: These are the exact failure modes that occur in
production. Testing them prevents the "works in dev, fails at 3 AM on a
holiday" class of incidents.

### 5.4 Property-Based Testing for Domain Models — 📝 Open

**Current state**: Domain model tests are primarily example-based (specific
inputs → expected outputs). For types like `MarketEvent`, `Trade`, and
`LOBSnapshot`, property-based testing would be more effective at finding edge
cases.

**Impact of improvement**: Catch edge cases in serialization round-trips, event
ordering, and payload validation that hand-written examples miss. Libraries like
FsCheck or Hedgehog work well with the existing xUnit setup.

---

## Category 6: Code Quality & Maintainability

### 6.1 Eliminate 42-Service Singleton Anti-Pattern — 📝 Open

**Files**: All services in `src/Meridian.Ui.Services/Services/`

42 services use manual `Lazy<T>` singleton patterns:

```csharp
private static readonly Lazy<AlertService> _instance = new(() => new AlertService());
public static AlertService Instance => _instance.Value;
```

This makes services untestable (can't inject mocks), tightly coupled (services
reference each other via static instances), and duplicative (~4,000-5,000 LOC of
boilerplate).

**Impact of improvement**: Proper DI registration. Services become testable,
composable, and have explicit lifetime management. Dependency graphs become
visible and verifiable.

### 6.2 ServiceCompositionRoot Decomposition — 📝 Open

**File**: `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`

This single file registers 50-100+ services with complex dependency wiring. No
validation that the dependency graph is acyclic. No documentation of
registration order (which matters for some services).

**Impact of improvement**: Break into focused registration modules
(`StorageModule`, `MonitoringModule`, `ProviderModule`). Add startup validation
that resolves all registered services eagerly to catch missing registrations at
boot rather than at first use.

### 6.3 Consistent Error Handling Across Providers — 📝 Open

**Current state**: Each provider handles errors differently:
- Alpaca: fire-and-forget subscription, no auth verification
- Polygon: circuit breaker with retry
- IB: conditional connection with simulation fallback
- NYSE: hybrid streaming + historical

**Impact of improvement**: A uniform error handling contract where every provider
reports errors through the same mechanism, with the same severity levels, and
the same recovery semantics. Currently, understanding error behavior requires
reading each provider's implementation individually.

---

## Category 7: Correctness by Construction

These improvements make entire classes of bugs impossible rather than catching
them after the fact.

### 7.1 Typed Symbol Keys — 📝 Open

**Current state**: Symbols are passed as `string` everywhere. Nothing prevents
passing a ticker where a CUSIP is expected, or vice versa. Canonical vs.
raw symbols are distinguished only by convention.

**Impact of improvement**: Introduce `Symbol` (raw), `CanonicalSymbol`, and
`ProviderSymbol` value types. The compiler prevents mixing them. Mapping between
types is explicit and auditable.

### 7.2 Sequence Number Domain Separation — 📝 Open

**Current state**: `MarketEvent.Sequence` is a pipeline-assigned sequence, but
payloads like `Trade.SequenceNumber` carry exchange-assigned sequences. These
two sequence domains are both `long` and easily confused.

**Impact of improvement**: Introduce `PipelineSequence` and `ExchangeSequence`
value types. Code that accidentally compares or conflates them becomes a
compile-time error.

### 7.3 Non-Nullable Event Payloads via Type Specialization — 📝 Open

**Current state**: `MarketEvent.Payload` is `MarketEventPayload?` — nullable for
all event types. Some events (like Heartbeat) have null payloads, but most
require non-null payloads. This is enforced by convention, not the type system.

**Impact of improvement**: Generic specialization
(`MarketEvent<TPayload> where TPayload : MarketEventPayload`) eliminates the
nullability for events that always carry payloads. Pattern matching becomes
exhaustive and the compiler catches missing cases.

---

## Priority Matrix

| # | Improvement | Category | Data Impact | Reliability Impact | Status |
|---|------------|----------|-------------|-------------------|--------|
| 1.1 | Fix flush semantics | Correctness | **Critical** | High | ✅ Done (2026-03-12) |
| 1.2 | WAL-sink transaction | Correctness | **Critical** | High | 📝 Open |
| 1.3 | Price precision fix | Correctness | **Critical** | Medium | ✅ Done (2026-03-10) |
| 1.4 | Trade deduplication | Correctness | **Critical** | Medium | ✅ Done (2026-03-10) |
| 1.5 | Completeness score calibration | Correctness | **Critical** | Medium | 🔄 Partial (2026-03-12) |
| 2.1 | Memory leak fixes | Stability | Low | **Critical** | ✅ Done (2026-03-12) |
| 2.2 | Hot-path GC allocation (GapAnalyzer) | Stability | Low | High | ✅ Done (2026-03-12) |
| 2.3 | Sink timeout/buffering | Stability | High | **Critical** | ✅ Done (2026-03-11) |
| 2.4 | WebSocket buffer size limit | Stability | Low | High | ✅ Done (2026-03-12) |
| 2.5 | Reconnection race fix | Stability | Medium | High | ✅ Done (2026-03-12) |
| 2.6 | Provider connection timeout | Stability | Low | High | ✅ Done (2026-03-11) |
| 3.1 | E2E trace propagation | Architecture | Low | High | ✅ Done (2026-03-24) |
| 3.2 | WebSocket provider base class | Architecture | Low | High | 📝 Open |
| 3.3 | Decide F# strategy | Architecture | Low | Medium | 📝 Open |
| 3.4 | Idempotent writes | Architecture | **Critical** | **Critical** | 📝 Open |
| 3.5 | Config fail-fast vs. self-healing | Architecture | Medium | Medium | 📝 Open |
| 3.6 | Backpressure feedback loop | Architecture | High | High | ✅ Done (2026-03-11) |
| 4.1 | Alert-to-runbook linkage | Observability | Low | Medium | ✅ Done (2026-03-12) |
| 4.2 | Continuous backpressure alerting | Observability | High | High | ✅ Done (2026-03-12) |
| 4.3 | WAL corruption alerting | Observability | High | High | ✅ Done (2026-03-10) |
| 4.4 | Provider health dashboard | Observability | Low | Medium | ✅ Done (2026-03-11) |
| 5.1 | Fix flaky tests | Testing | Low | Medium | ✅ Done (2026-03-11) |
| 5.2 | Error injection in mock sinks | Testing | Low | Medium | 📝 Open |
| 5.3 | Provider resilience test suite | Testing | Medium | High | 📝 Open |
| 5.4 | Property-based testing for domain models | Testing | Low | Medium | 📝 Open |
| 6.1 | Eliminate singletons | Maintainability | Low | Medium | 📝 Open |
| 6.2 | ServiceCompositionRoot decomposition | Maintainability | Low | Medium | 📝 Open |
| 6.3 | Consistent provider error handling | Maintainability | Low | Medium | 📝 Open |
| 7.1 | Typed symbol keys | Type Safety | Medium | Medium | 📝 Open |
| 7.2 | Sequence number domain separation | Type Safety | Medium | Medium | 📝 Open |
| 7.3 | Non-nullable event payloads | Type Safety | Low | Medium | 📝 Open |

---

## Implementation Follow-Up (2026-03-10)

The following items from this brainstorm have been implemented:

| Item | Status | Implementation |
|------|--------|----------------|
| 1.3 — Alpaca price precision & timestamp integrity | ✅ Done | `AlpacaMarketDataClient`: trade sizes now parsed with `GetInt64()` to avoid truncation on block trades exceeding `int.MaxValue`. Both trade and quote messages now reject unparseable timestamps with a `Warning` log instead of silently substituting `UtcNow`, preserving time-series integrity. |
| 1.4 — Trade message deduplication | ✅ Done | `AlpacaMarketDataClient`: content-based deduplication added via a bounded sliding window (`HashSet` + `Queue`) of `(symbol, price, size, timestamp)` tuples (capacity 2,048). Duplicate re-deliveries from Alpaca's WebSocket are suppressed at the `Debug` log level. |
| 4.3 — WAL corruption alerting | ✅ Done | `WriteAheadLog`: new `WalCorruptionMode` enum (`Skip` / `Alert` / `Halt`) added. `WalOptions.CorruptionMode` defaults to `Skip` (backwards-compatible). In `Alert` mode the new `CorruptionDetected` event fires with the corrupted record count so monitoring infrastructure can alert operators. In `Halt` mode an `InvalidDataException` is thrown to force operator review before the application can start. |

Test coverage:
- `AlpacaMessageParsingTests` — 12 tests covering size precision, timestamp rejection, deduplication, and window eviction.
- `WriteAheadLogCorruptionModeTests` — 9 tests covering all three modes and the `WalOptions` default.

---

## Implementation Follow-Up (2026-03-11)

| Item | Status | Implementation |
|------|--------|----------------|
| 2.6 — Provider connection timeout | ✅ Done | `Program.cs`: `dataClient.ConnectAsync()` is now wrapped in a 30-second `CancellationTokenSource` timeout. On timeout an `OperationCanceledException` is caught separately and surfaced as a clear `ErrorCode.ConnectionTimeout` exit code with an actionable log message. |
| 2.3 — Periodic sink flush timeout | ✅ Done | `EventPipeline`: new `sinkFlushTimeout` constructor parameter (default 60 s). Each periodic flush call is wrapped in a `CancellationTokenSource.CreateLinkedTokenSource` that adds the per-flush deadline on top of the pipeline shutdown token. A hung sink now times out and logs a `Warning` instead of stalling the pipeline indefinitely. Pipeline-shutdown cancellation is still distinguished from flush-timeout cancellation via a `when` guard. |
| 3.6 — Backpressure feedback loop | ✅ Done | `EventPipeline.TryPublishWithResult()` added returning a new `PublishResult` enum (`Accepted` / `AcceptedUnderPressure` / `Dropped`). `TryPublish()` is unchanged for backward compatibility. `PublishResult` is defined in `Meridian.Domain.Events` so all provider adapters can reference it without circular dependencies. |
| 4.4 — Provider health dashboard | ✅ Done | New `GET /api/providers/dashboard` endpoint (`UiApiRoutes.ProvidersDashboard`) added to `ProviderExtendedEndpoints`. Returns an `overallTrafficLight` (`green`/`yellow`/`red`), human-readable `summary`, and per-provider detail including latency from stored metrics. |
| 5.1 — Fix timing-dependent skipped tests | ✅ Done | `QueueUtilization_ReflectsQueueFill`: rewritten using `BlockingStorageSink` + `batchSize: 1` so 49 events remain in the channel while the consumer is blocked on the first. `ValidateFileAsync_SupportsCancellation`: fixed by adding `ct.ThrowIfCancellationRequested()` at the top of `ValidateFileAsync` to honour pre-cancelled tokens before the file is opened. Both tests pass deterministically. |

Test coverage added:
- `EventPipelineTests.TryPublishWithResult_WhenAccepted_ReturnsAccepted` — verifies `Accepted` result on normal publish.
- `EventPipelineTests.TryPublishWithResult_WhenQueueFull_ReturnsDropped` — verifies `Dropped` result when pipeline is at capacity (DropWrite mode).
- `EventPipelineTests.QueueUtilization_ReflectsQueueFill` — previously skipped, now enabled and passes deterministically.
- `DataValidatorTests.ValidateFileAsync_SupportsCancellation` — previously skipped, now enabled and passes deterministically.

---

## Implementation Follow-Up (2026-03-12)

| Item | Status | Implementation |
|------|--------|----------------|
| 1.1 — EventPipeline flush semantics | ✅ Done | `EventPipeline.FlushAsync()`: the break condition was changed from `consumed + dropped >= targetPublished` to `consumed + _rejectedCount >= targetPublished`. Dropped events no longer count toward the flush target, so callers can no longer receive a successful flush while data is silently missing. A secondary channel-empty + consumer-idle double-check handles `DropOldest` mode where events are discarded by the channel before reaching the consumer. A post-flush `LogWarning` is emitted when any events were dropped during the flush window so callers are explicitly informed of data loss. |
| 1.5 — Completeness score miscalculation | 🔄 Partial | `CompletenessScoreCalculator.RegisterSymbolLiquidity()` now immediately propagates the new expected-events-per-hour to all existing `SymbolDateState` entries for that symbol via `state.SetExpectedEventsPerHour()`. Liquidity profiles registered after the first event of the day are therefore reflected in the score without waiting until the next day. The public `SetExpectedEventsPerHour(string symbol, long expectedPerHour)` overload allows operators to push ad-hoc corrections at any time. Automatic self-calibration from observed event patterns (the original fix proposal) remains future work. |
| 2.1 — Memory leaks in monitoring services | ✅ Done | All three services now evict stale entries on a background timer. `SequenceErrorTracker`: removes error lists and symbol states that have had no activity within the configured retention window. `GapAnalyzer`: removes both `_detectedGaps` and `_symbolStates` entries for symbols inactive beyond the retention threshold, and also clears the `_effectiveConfigCache` entry. `CompletenessScoreCalculator`: removes `SymbolDateState` entries for dates older than `RetentionDays` (default 7 days). Each service logs a `Debug` message when entries are removed, making cleanup visible in diagnostic traces. |
| 2.2 — Hot-path allocations in GapAnalyzer | ✅ Done | `GapAnalyzer.GetEffectiveConfig()` now returns from a per-symbol `ConcurrentDictionary<string, GapAnalyzerConfig> _effectiveConfigCache`. The `record with` allocation only occurs on first access or when `RegisterLiquidityProfile()` invalidates the cache entry. At 50,000 events/second this eliminates ~50,000 short-lived heap allocations per second, reducing GC pressure on the analysis hot path. |
| 2.4 — WebSocket receive buffer unbounded | ✅ Done | `WebSocketConnectionManager` now enforces `WebSocketConnectionConfig.MaxMessageSizeBytes` inside the receive loop. When the accumulated `StringBuilder` length exceeds the limit the connection is closed with a structured `Warning` log: `"{Provider} WebSocket message exceeds max size {MaxBytes} bytes — discarding"`. This prevents a misbehaving or malicious provider from exhausting heap memory via an oversized message. |
| 2.5 — WebSocket reconnection race condition | ✅ Done | The fast-path `if (_isReconnecting) return false` check-without-lock has been removed from `TryReconnectAsync()`. The `SemaphoreSlim _reconnectGate` is now the sole gating mechanism: `await _reconnectGate.WaitAsync(0, ct)` provides the non-blocking try-acquire. The code comment explicitly documents why the fast path was removed. This closes the race window where two threads could both observe `_isReconnecting == false` and proceed to duplicate the reconnection attempt. |
| 4.1 — Alert-to-runbook linkage | ✅ Done | `deploy/monitoring/alert-rules.yml` now includes a `runbook_url` annotation on every alert rule, pointing to the relevant section of `docs/operations/operator-runbook.md` (e.g., `#application-down`, `#high-drop-rate`, `#pipeline-backpressure`). The in-process `AlertRunbookRegistry` mirrors these mappings so the `AlertDispatcher` can attach runbook URLs to programmatic alert objects at dispatch time. Monitoring tools (Grafana, PagerDuty) that support annotation rendering display the URL inline when an alert fires. |
| 4.2 — Backpressure alerting is single-shot | ✅ Done | `BackpressureAlertService` now maintains a persistent `_isInBackpressureState` flag and `_backpressureStartTime`. The alert fires at every check interval while pressure is sustained, reporting the per-interval drop delta, total drops, current drop-rate percentage (as `DropRate`), and the full episode duration. An `OnBackpressureResolved` event fires when utilization drops below threshold, providing the episode duration and total event loss for post-mortem reporting. Alert de-bounce (`ConsecutiveChecksBeforeAlert`) prevents spurious single-cycle spikes from triggering the onset alert. |

Test coverage added:
- `EventPipelineTests.TryPublish_WhenQueueFull_DropOldestMode_DropsEvents` — verifies `FlushAsync` completes and the sink receives only the latest events when `DropOldest` mode discards older entries.
- `DataQualityTests` (31 tests) — cover `CompletenessScoreCalculator`, `GapAnalyzer`, `SequenceErrorTracker`, and `AnomalyDetector` including liquidity-profile registration and mid-day re-registration behaviour.
- `LiquidityProfileTests` (22 tests) — cover threshold calculation, profile inference, gap classification, completeness grading, and propagation to all monitoring services via `DataQualityMonitoringService.RegisterSymbolLiquidity()`.
- `BackpressureAlertServiceTests` (8 tests) — cover `GetStatus` at normal/warning/critical utilisation, high drop-rate detection, zero-denominator safety, message content, and the fire-and-forget async guard (`CheckBackpressure_IsNotAsyncVoid`).
- `WebSocketConnectionManagerTests` (3 tests) — cover construction validation, `StartReceiveLoop` pre-connect guard, and graceful `DisposeAsync` without a live connection.

---

## Implementation Summary (Historical Snapshot — 2026-03-19)

**Overall Status (at 2026-03-19 snapshot):** 15 of 31 items fully complete; 1 item partial; 15 items open

### Completion Rate by Category

| Category | Complete | Partial | Open | Total | % Done |
|----------|----------|---------|------|-------|--------|
| Data Integrity & Correctness | 3 | 1 | 1 | 5 | 60% |
| Resource Management & Stability | 6 | 0 | 0 | 6 | 100% ✅ |
| Architectural Improvements | 1 | 0 | 5 | 6 | 17% |
| Observability & Operational Excellence | 4 | 0 | 0 | 4 | 100% ✅ |
| Test Infrastructure | 1 | 0 | 3 | 4 | 25% |
| Code Quality & Maintainability | 0 | 0 | 3 | 3 | 0% |
| Correctness by Construction | 0 | 0 | 3 | 3 | 0% |
| **Total** | **15** | **1** | **15** | **31** | **48% + 3% partial** |

### Categories Fully Complete (100%)

1. **Resource Management & Stability** (6/6) ✅
   - All memory leaks, timeout issues, and race conditions addressed
   - Monitoring services evict stale entries; flush timeouts enforced; reconnection race closed

2. **Observability & Operational Excellence** (4/4) ✅
   - Alert-to-runbook linkage wired into rules and registry
   - Backpressure alerting sustained and episodic
   - WAL corruption alerting configurable (Skip/Alert/Halt)
   - Provider health dashboard operational

### High-Value Remaining Work

**Category 3 — Architectural Improvements:**
| Item | Benefit | Effort | Blocked By |
|------|---------|--------|-----------|
| 3.1 — Trace context propagation via `System.Diagnostics.Activity` | Enable E2E tracing across collectors/pipeline/storage | Medium | None |
| 3.2 — WebSocket provider base class consolidation | Reduce 200-300 LOC duplication across Polygon/NYSE/StockSharp | Low-Med | Design phase |
| 3.4 — Idempotent storage writes (bloom filter dedup) | Prevent sink-layer duplicates on replay | Medium | High-performance encoding choice |

**Category 6 & 7 — Code Quality & Correctness by Construction:**
| Item | Benefit | Effort |
|------|---------|--------|
| 6.1 — Replace 42-service `Lazy<T>` singletons with DI | Better testability, no tight coupling | High |
| 7.1 — Typed symbol keys (value-object wrappers) | Eliminate string-based symbol confusion | High |
| 7.2 — Sequence number domain separation | Type-safe pipeline vs exchange sequences | Medium |

### Recommendations for Phase Execution

1. **Next Sprint:** Complete Categories 3-4 items (trace context, WebSocket base class)
2. **High-Priority Debt:** Code Quality items 6.1-6.3 should be roadmapped for Phase 8+
3. **Long-term Architecture:** Correctness items 7.1-7.3 should be considered in Phase 14+ refactoring

**Overall Assessment:** Core improvements (48% + 3% partial = 51% completion) addressing highest-risk areas (data integrity, stability, operations). Remaining work is architectural refinement and technical debt rather than critical defects.

---

*Initial Brainstorm: 2026-03-02 | Follow-ups: 2026-03-10, 2026-03-11, 2026-03-12 | Summary: 2026-03-19*


## Recommended Next-Step Sources (2026-03-24)

To reflect the current project state, continue execution from these documents:

1. `docs/status/IMPROVEMENTS.md` — normalized improvement tracking (current status baseline).
2. `docs/status/ROADMAP.md` — delivery-wave sequencing and dependencies.
3. `docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md` — consolidated backlog for non-assembly execution.

This brainstorm remains a supporting reference for rationale and root-cause framing, not a live status board.

