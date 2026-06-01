# Real-Time Streaming Architecture Evaluation

## Meridian — Data Pipeline Assessment

**Date:** 2026-03-11
**Status:** Re-evaluation Complete (supersedes 2026-02-22 assessment)
**Author:** Architecture Review

---

## Executive Summary

This document re-evaluates the real-time streaming architecture of the Meridian system. Since the prior assessment (2026-02-22), several significant new capabilities have been added, notably end-to-end event validation, persistent deduplication, and a comprehensive data quality monitoring subsystem.

**Key changes since last review:**

| Recommendation / Addition (Mar 2026) | Status |
|--------------------------------------|--------|
| Implement micro-batching | **Retained** — 100-event batches with 5 s periodic flush |
| Add tiered backpressure | **Retained** — `BackpressureAlertService` with 70 %/90 % thresholds |
| Add provider failover | **Retained** — `FailoverAwareMarketDataClient` with automatic provider switching |
| Add latency histograms | **Retained** — `ProviderLatencyService` with P50/P95/P99 per provider |
| Improve reconnection with jitter | **Retained** — `WebSocketReconnectionHelper` with exponential backoff + jitter |
| Provider-specific resilience policies | **Retained** — Three `WebSocketConnectionConfig` profiles (Default, HighFrequency, Resilient) |
| Event validation + dead-letter sink | **Implemented** — `IEventValidator` + `DeadLetterSink` routes invalid events to JSONL dead-letter file |
| Persistent deduplication ledger | **Implemented** — `PersistentDedupLedger` with TTL-based JSONL cache survives restarts |
| Data quality monitoring subsystem | **Implemented** — `DataQualityMonitoringService` orchestrates 7 quality analyzers with liquidity-aware thresholds |
| SLA freshness monitoring | **Implemented** — `DataFreshnessSlaMonitor` with configurable warning (60 s) and critical (300 s) thresholds |
| Centralized alert infrastructure | **Implemented** — `AlertDispatcher`, `HealthCheckAggregator`, `AlertRunbookRegistry`, `SloDefinitionRegistry` |
| Add parallel channel consumers | Deferred — single consumer batching meets current throughput needs |
| Connection pooling | Deferred — not required at current symbol counts |

**Overall finding:** The streaming architecture now spans data quality monitoring, persistent deduplication, event-level validation with dead-letter routing, and a formal SLO registry alongside all previously implemented capabilities. The system is production-grade with clear observability, resilience, and data integrity guarantees. Remaining improvements are incremental optimisations.

---

## A. Architecture Overview

### Streaming Data Flow

```
External Sources                    Internal Processing
┌─────────────┐     ┌──────────────────────────────────────────────────────────┐
│   Alpaca    │────▶│                                                          │
│  WebSocket  │     │  ┌─────────────┐    ┌─────────────┐   ┌─────────────┐   │
├─────────────┤     │  │  Provider   │    │   Domain    │   │   Event     │   │
│   Polygon   │────▶│  │  Adapters   │───▶│  Collectors │──▶│  Pipeline   │   │
│  WebSocket  │     │  │             │    │             │   │ (Channels)  │   │
├─────────────┤     │  └─────────────┘    └─────────────┘   └──────┬──────┘   │
│     IB      │────▶│         │                                     │          │
│   Gateway   │     │         │ ┌───────────────────────────────────┤          │
├─────────────┤     │         │ │                                   │          │
│  StockSharp │────▶│         │ ▼                                   ▼          │
│  Connectors │     │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐   │
├─────────────┤     │  │   Failover   │  │  WAL (crash  │  │   Storage    │   │
│    NYSE     │────▶│  │   Client     │  │  safe log)   │  │   Sinks      │   │
│    Feed     │     │  └──────────────┘  └──────────────┘  └──────────────┘   │
└─────────────┘     │         │                                   │            │
                    │         ▼                                   ▼            │
                    │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐   │
                    │  │  Degradation │  │ Backpressure │  │  Latency &   │   │
                    │  │  Scorer      │  │ Alerts       │  │  Skew Mon.   │   │
                    │  └──────────────┘  └──────────────┘  └──────────────┘   │
                    └──────────────────────────────────────────────────────────┘
```

### Core Components

| Component | Location | Responsibility |
|-----------|----------|----------------|
| `IMarketDataClient` | `ProviderSdk/` | Provider abstraction (ADR-001) |
| `EventPipeline` | `Application/Pipeline/` | Bounded channel routing with WAL |
| `EventPipelinePolicy` | `Core/Pipeline/` | Preset channel configurations (ADR-013) |
| `IEventValidator` | `Application/Pipeline/` | Pre-persistence event validation gate (ADR-007) |
| `DeadLetterSink` | `Application/Pipeline/` | JSONL dead-letter file for rejected events (ADR-007) |
| `PersistentDedupLedger` | `Application/Pipeline/` | TTL-based persistent deduplication with JSONL backing |
| `SchemaValidationService` | `Application/Monitoring/` | Consolidated schema version validation for ingestion path |
| `TradeDataCollector` | `Domain/Collectors/` | Trade event processing with sequence validation |
| `MarketDepthCollector` | `Domain/Collectors/` | L2 order book maintenance |
| `QuoteCollector` | `Domain/Collectors/` | BBO state tracking |
| `FailoverAwareMarketDataClient` | `Infrastructure/Adapters/Failover/` | Automatic provider switching |
| `StreamingFailoverRegistry` | `Infrastructure/Adapters/Failover/` | Singleton registry for API endpoint access to failover service |
| `WebSocketConnectionManager` | `Infrastructure/Resilience/` | Unified WebSocket lifecycle |
| `WebSocketConnectionConfig` | `Infrastructure/Resilience/` | Profile-based resilience tuning |
| `WebSocketReconnectionHelper` | `Infrastructure/Shared/` | Standardised reconnection with jitter |
| `WebSocketResiliencePolicy` | `Infrastructure/Resilience/` | Polly retry + circuit breaker + timeout |
| `ConnectionHealthMonitor` | `Application/Monitoring/` | Connection state tracking |
| `ProviderLatencyService` | `Application/Monitoring/` | Per-provider P50/P95/P99 histograms |
| `ProviderDegradationScorer` | `Application/Monitoring/` | Composite health scoring |
| `ClockSkewEstimator` | `Application/Monitoring/` | EWMA clock-drift detection |
| `SpreadMonitor` | `Application/Monitoring/` | Bid-ask spread alerting |
| `BackpressureAlertService` | `Application/Monitoring/` | Tiered backpressure alerting |
| `DroppedEventAuditTrail` | `Application/Pipeline/` | Dropped event accounting |
| `SubscriptionOrchestrator` | `Application/Subscriptions/` | Hot-reloadable subscription management |
| `DataQualityMonitoringService` | `Application/Monitoring/DataQuality/` | Central orchestrator for 7 quality analyzers |
| `LiquidityProfileProvider` | `Application/Monitoring/DataQuality/` | Liquidity-aware monitoring thresholds (High/Normal/Low/VeryLow/Minimal) |
| `DataFreshnessSlaMonitor` | `Application/Monitoring/DataQuality/` | Configurable data freshness SLA enforcement |
| `AlertDispatcher` | `Application/Monitoring/Core/` | Centralized alert publishing and subscription management |
| `HealthCheckAggregator` | `Application/Monitoring/Core/` | Parallel health check aggregation across components |
| `SloDefinitionRegistry` | `Application/Monitoring/Core/` | Runtime SLO definitions per subsystem |
| `AlertRunbookRegistry` | `Application/Monitoring/Core/` | Alert-to-runbook mapping for incident triage |

---

## B. Provider Connectivity Evaluation

### Subscription ID Ranges

Each provider operates in a non-overlapping subscription ID range to prevent conflicts:

| Provider | Starting ID | Range |
|----------|-------------|-------|
| Alpaca | 100,000 | 100 K – 200 K |
| Polygon | 200,000 | 200 K – 300 K |
| StockSharp | 300,000 | 300 K – 400 K |
| Interactive Brokers | 400,000 | 400 K – 500 K |
| NYSE | 500,000 | 500 K – 600 K |

---

### Provider 1: Alpaca Markets

**Connection Type:** WebSocket (`wss://stream.data.alpaca.markets/v2/{feed}`)
**Authentication:** JSON `{"action": "auth", "key": "<KEY_ID>", "secret": "<SECRET_KEY>"}`
**Resilience Profile:** `WebSocketConnectionConfig.Default`

**Strengths:**

| Strength | Detail |
|----------|--------|
| Stable connection | `WebSocketConnectionManager` with automatic reconnect |
| Re-authentication | Automatic re-auth and re-subscription on reconnect |
| Heartbeat | Ping/pong via `WebSocketConnectionManager` |
| Feed selection | IEX (free) and SIP (paid) feeds supported |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| US markets only | No international coverage |
| No L2 depth | `SubscribeMarketDepth` returns -1 |
| Symbol limits | Max ~200 symbols per connection |

**Implementation Assessment:**
- Location: `Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs`
- Reconnection: `OnConnectionLostAsync()` re-authenticates, restarts receive loop, re-subscribes
- Data: Trades → `TradeDataCollector`, Quotes → `QuoteCollector`

---

### Provider 2: Polygon.io

**Connection Type:** WebSocket (`wss://socket.polygon.io/{feed}`)
**Authentication:** `{"action":"auth","params":"{apiKey}"}`
**Resilience Profile:** `WebSocketConnectionConfig.Default` + `WebSocketReconnectionHelper`

**Strengths:**

| Strength | Detail |
|----------|--------|
| Full tick data | Trade ("T"), quote ("Q"), and aggregate ("A"/"AM") streams |
| Gated reconnection | `SemaphoreSlim` prevents reconnection storms |
| Sequence tracking | `_messageSequence` tracks message ordering |
| Stub mode | Operates in degraded mode if API key missing (< 20 chars) |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Cost | Professional tier for real-time data; free tier is 15-min delayed |
| Rate limits | Connection and request limits per tier |
| Complexity | Multiple subscription channels (stocks, options, forex, crypto) |

**Implementation Assessment:**
- Location: `Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`
- Reconnection: Exponential backoff (base 2 s → max 60 s), max 10 attempts
- Data: Trades, quotes, second/minute aggregates

---

### Provider 3: Interactive Brokers

**Connection Type:** TCP Socket via TWS/IB Gateway (not WebSocket)
**Resilience Profile:** `WebSocketConnectionConfig.Resilient`

**Strengths:**

| Strength | Detail |
|----------|--------|
| Global coverage | 150+ markets worldwide |
| Full depth | L2 market depth via `reqMarketDepth()` |
| Multi-asset | Stocks, futures, forex, options |
| Enhanced connection manager | `EnhancedIBConnectionManager` with pacing compliance |
| Simulation fallback | `IBSimulationClient` when IBAPI library unavailable |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Gateway dependency | Requires TWS or IB Gateway running locally |
| Connection limits | Max 3 simultaneous connections |
| Pacing rules | Complex IB-specific rate limiting (`IBApiLimits`) |
| Conditional compilation | `#if IBAPI` — falls back to simulation if library absent |

**Implementation Assessment:**
- Location: `Infrastructure/Adapters/InteractiveBrokers/`
- Components: `IBMarketDataClient`, `IBCallbackRouter`, `EnhancedIBConnectionManager`, `ContractFactory`
- Data: Trades, quotes (L1), depth (L2, up to 10 levels) via IB API callbacks

---

### Provider 4: StockSharp

**Connection Type:** Framework-managed (varies by underlying connector)
**Resilience Profile:** `WebSocketConnectionConfig.Resilient`

**Strengths:**

| Strength | Detail |
|----------|--------|
| 90+ connectors | Massive exchange coverage (Rithmic, IQFeed, CQG, etc.) |
| Message buffering | Bounded channel (50 K capacity, `EventPipelinePolicy.MessageBuffer`) prevents blocking |
| Async message processor | Dedicated task drains buffer asynchronously |
| Heartbeat timer | Long-running liveness detection |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Framework overhead | Heavy dependency footprint |
| Learning curve | Complex connector configuration |
| Licensing | Commercial features require license |
| Conditional compilation | `#if STOCKSHARP` — stub otherwise |

**Implementation Assessment:**
- Location: `Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs`
- Reconnection: Automatic with exponential backoff + jitter, connector recreation if needed
- Data: Trades → `TradeDataCollector`, Quotes → `QuoteCollector`, Depth → `MarketDepthCollector`

---

### Provider 5: NYSE

**Connection Type:** Hybrid (WebSocket + REST, OAuth authenticated)
**Resilience Profile:** `WebSocketConnectionConfig.Default` + `WebSocketReconnectionHelper`

**Strengths:**

| Strength | Detail |
|----------|--------|
| Official source | Direct from exchange |
| L1/L2 data | Both quote levels (Premium/Professional tier) |
| Historical + real-time | Combined provider |
| Corporate actions | Dividends, splits, trade conditions |
| Extended hours | Pre/after-hours data |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| NYSE only | Single exchange |
| Cost | Enterprise pricing for L2 |
| Rate limits | 100 req/min, 5 K req/hour, 50 K req/day |
| Token management | OAuth token refresh with expiry tracking |

**Implementation Assessment:**
- Location: `Infrastructure/Adapters/NYSE/NYSEDataSource.cs`
- Features: Hybrid streaming + historical backfill, participant IDs, consolidated tape

---

### Provider Comparison Matrix

| Provider | Latency | Throughput | Reliability | Coverage | L2 Depth | Resilience Profile | Cost |
|----------|---------|------------|-------------|----------|----------|-------------------|------|
| Alpaca | ★★★★☆ | ★★★★☆ | ★★★★★ | US Only | No | Default | Free |
| Polygon | ★★★★★ | ★★★★★ | ★★★★★ | US + Crypto | Yes | Default | $$$ |
| IB | ★★★★☆ | ★★★☆☆ | ★★★★☆ | Global | Yes | Resilient | $ |
| StockSharp | ★★★☆☆ | ★★★★☆ | ★★★★☆ | Global | Yes | Resilient | $$ |
| NYSE | ★★★★★ | ★★★★★ | ★★★★★ | NYSE Only | Yes | Default | $$$$ |

---

## C. Event Pipeline Evaluation

### Current Implementation

Location: `Application/Pipeline/EventPipeline.cs`

**Architecture:** Bounded `System.Threading.Channels` with dedicated consumer task, micro-batching, WAL-backed durability, and backpressure alerting.

#### Pipeline Policy Presets (ADR-013)

| Preset | Capacity | Full Mode | Metrics | Use Case |
|--------|----------|-----------|---------|----------|
| `Default` | 100,000 | DropOldest | Yes | General-purpose event pipelines |
| `HighThroughput` | 50,000 | DropOldest | Yes | Streaming data pipelines |
| `MessageBuffer` | 50,000 | DropOldest | No | Internal message buffering (StockSharp) |
| `MaintenanceQueue` | 100 | Wait | No | Background tasks (no drops allowed) |
| `Logging` | 1,000 | DropOldest | No | Log channels |
| `CompletionQueue` | 500 | Wait | No | Completion notifications |

#### Publishing Paths

| Method | Behaviour | Use Case |
|--------|-----------|----------|
| `TryPublish(in MarketEvent)` | Non-blocking O(1); returns `false` if full | Hot path for providers |
| `PublishAsync(MarketEvent, CancellationToken)` | Blocks until space available | Backfill / maintenance |

#### Consumer Processing

```
Events arrive → TryPublish → Bounded Channel → Consumer Task
                                                    │
                                    ┌───────────────┤
                                    ▼               ▼
                               WAL Append      Sink Append
                                    │               │
                                    ▼               ▼
                              WAL Commit    (batch of 100)
                                                    │
                                    ┌───────────────┤
                                    ▼               ▼
                            Periodic Flush    Final Flush
                             (every 5 s)     (30 s timeout)
```

#### Pipeline Statistics Exposed

| Statistic | Description |
|-----------|-------------|
| `PublishedCount` | Total events published |
| `DroppedCount` | Events dropped due to backpressure |
| `ConsumedCount` | Events consumed by sinks |
| `RecoveredCount` | Events recovered from WAL on startup |
| `RejectedCount` | Events rejected by the validator and routed to the dead-letter sink |
| `DeduplicatedCount` | Duplicate events filtered by `PersistentDedupLedger` |
| `PeakQueueSize` | Maximum observed queue depth |
| `CurrentQueueSize` | Current queue depth |
| `QueueUtilization` | 0 – 100 % current utilisation |
| `AverageProcessingTimeUs` | Average per-event processing time (microseconds) |
| `TimeSinceLastFlush` | Time since last sink flush |
| `IsWalEnabled` | Whether a WAL is configured |
| `IsValidationEnabled` | Whether event validation is configured |
| `IsDeduplicationEnabled` | Whether deduplication is configured |

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| WAL durability | Events survive crashes — uncommitted records replayed on startup via `RecoverAsync()` |
| Micro-batching | 100-event batches reduce per-event I/O overhead (was single-event in prior review) |
| Periodic flush | 5 s timer ensures data reaches disk even during low-activity periods |
| DropOldest default | Providers never block; oldest data sacrificed under extreme load |
| Rich statistics | Queue utilisation, drop count, reject count, dedup count, processing time all exposed for monitoring |
| Dropped event audit | `DroppedEventAuditTrail` records every dropped event with reason |
| High water mark alerts | Warning logged at 80 % utilisation; recovery at 50 % |
| Consistent policies | `EventPipelinePolicy` presets enforce uniform channel configuration (ADR-013) |
| Event validation gate | `IEventValidator` intercepts bad events before WAL/sink; rejected events routed to dead-letter |
| Dead-letter sink | `DeadLetterSink` persists rejected events to `_dead_letter/rejected_events.jsonl` for replay/inspection |
| Persistent deduplication | `PersistentDedupLedger` uses a TTL-keyed JSONL cache that survives restarts; supports compaction |
| Schema validation | `SchemaValidationService` bridges `EventSchemaValidator` and `SchemaVersionManager` for a single ingestion-path entrypoint |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Single consumer | One consumer task per pipeline (sufficient for current load, limits future scale) |
| No priority queues | All event types treated equally in the channel |
| Batch size fixed | 100-event batches are not adaptive to load |

### Throughput Benchmarks

Benchmarked in `benchmarks/Meridian.Benchmarks/EventPipelineBenchmarks.cs`:

| Scenario | Configuration | Events Tested |
|----------|---------------|---------------|
| Bounded 50 K (Wait) | SingleReader, SingleWriter | 1 K / 10 K / 100 K |
| Bounded 10 K (Wait) | SingleReader, SingleWriter | 1 K / 10 K / 100 K |
| Bounded (DropOldest) | TryWrite, non-blocking | 1 K / 10 K / 100 K |
| Unbounded | Baseline comparison | 1 K / 10 K / 100 K |
| TryPublish sync | Multi-producer, 50 K DropOldest | Latency |
| PublishAsync | Multi-producer, async write | Latency |

### Remaining Recommendations

1. **Adaptive batch sizing** — Scale batch size based on queue utilisation (smaller at low load, larger at high load)
2. **Parallel consumers** — Add when single consumer saturates (not currently a bottleneck)
3. **Priority channels** — Separate channels for quotes vs. trades if latency-sensitive use cases emerge
4. **Dedup ledger compaction scheduling** — Schedule periodic `CompactAsync()` calls (e.g., daily) to prevent unbounded growth of `dedup_ledger.jsonl`

---

## D. Resilience Pattern Evaluation

### Current Implementation

Location: `Infrastructure/Resilience/`

#### WebSocket Connection Profiles

Three pre-defined profiles match provider characteristics:

| Profile | Max Retries | Base Delay | Max Delay | Circuit Breaker Threshold | CB Duration | Heartbeat Interval | Heartbeat Timeout | Max Reconnect |
|---------|-------------|------------|-----------|---------------------------|-------------|--------------------|-------------------|---------------|
| **Default** | 5 | 2 s | 30 s | 5 failures | 30 s | 30 s | 10 s | 10 |
| **HighFrequency** | 5 | 1 s | 15 s | 5 failures | 15 s | 15 s | 5 s | 10 |
| **Resilient** | 10 | 3 s | 60 s | 5 failures | 60 s | 30 s | 10 s | 20 |

**Provider assignments:**
- Alpaca, Polygon, NYSE → `Default`
- Interactive Brokers, StockSharp → `Resilient`

#### Polly Comprehensive Pipeline

`WebSocketResiliencePolicy.CreateComprehensivePipeline()` creates a three-layer resilience pipeline:

```
┌──────────────────────────────┐
│  Layer 1: Timeout            │  5-minute total operation timeout
│  ┌────────────────────────┐  │
│  │ Layer 2: Circuit Breaker│  │  50 % failure ratio, min throughput = threshold
│  │ ┌──────────────────┐   │  │
│  │ │ Layer 3: Retry   │   │  │  Exponential backoff + ±20 % jitter
│  │ │                  │   │  │
│  │ └──────────────────┘   │  │
│  └────────────────────────┘  │
└──────────────────────────────┘
```

**Retry formula:** `delay = min(baseDelay * 2^(attempt - 1), maxDelay) ± 20 % jitter`

#### WebSocket Connection Manager

`WebSocketConnectionManager` provides unified lifecycle management:

| Feature | Detail |
|---------|--------|
| Gated reconnection | `SemaphoreSlim(1, 1)` prevents reconnection storms |
| Receive buffer | 64 KB per message |
| Message assembly | `StringBuilder` for multi-frame messages |
| Heartbeat integration | Automatic pong recording on data received |
| State events | `ConnectionLost`, `Reconnected`, `StateChanged` callbacks |
| Cleanup | Proper disposal of WebSocket, CTS, heartbeat |

#### WebSocket Reconnection Helper

`WebSocketReconnectionHelper` standardises reconnection across Polygon, NYSE, and StockSharp:

| Parameter | Default |
|-----------|---------|
| Max attempts | 10 |
| Base delay | 2 s |
| Max delay | 60 s |
| Jitter | ±20 % |
| Gating | `SemaphoreSlim` |

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| Profile-based configuration | Provider-specific tuning without code changes |
| Comprehensive pipeline | Timeout + circuit breaker + retry in correct order |
| Jitter on all retries | Prevents thundering herd across providers |
| Gated reconnection | SemaphoreSlim eliminates reconnection storms |
| Unified manager | `WebSocketConnectionManager` consolidates lifecycle |
| Observable state | Events for connection lost, recovered, state changes |

**Improvements since prior review:**

| Prior Weakness | Resolution |
|----------------|------------|
| Generic policies for all providers | Three profiles (Default / HighFrequency / Resilient) |
| No adaptive tuning | Profiles tuned per provider type |
| IB pacing needs special handling | `Resilient` profile + `EnhancedIBConnectionManager` |
| Reconnection without jitter | ±20 % jitter on all backoff calculations |

### Failure Scenarios Handled

| Scenario | Response | Recovery Time |
|----------|----------|---------------|
| Network blip | Retry with exponential backoff + jitter | 2 – 4 s |
| Server unavailable | Circuit breaker opens | 30 – 60 s (profile-dependent) |
| Authentication failure | Fail fast (no retry) | Immediate |
| Rate limit | Backoff with jitter | Variable |
| Connection timeout | Cancel and retry (30 s timeout) | 30 + s |
| Sustained provider failure | Failover to backup provider | 5 – 15 s |
| Silent connection loss | Heartbeat timeout triggers reconnect | 10 – 40 s |

### Remaining Recommendations

1. **Adaptive circuit breaker** — Auto-adjust thresholds based on rolling error rates
2. **Per-failure-type retry policies** — Different strategies for timeout vs. rate-limit vs. server error

---

## E. Backpressure Handling Evaluation

### Current Implementation

Backpressure handling has been substantially enhanced since the prior review with the addition of `BackpressureAlertService` and `DroppedEventAuditTrail`.

#### Pipeline Backpressure

| Full Mode | Behaviour | Current Usage |
|-----------|-----------|---------------|
| DropOldest | Drop oldest queued event | Default, HighThroughput, MessageBuffer, Logging |
| Wait | Block producer until space | MaintenanceQueue, CompletionQueue |

#### Tiered Alerting (BackpressureAlertService)

| Metric | Warning Threshold | Critical Threshold |
|--------|-------------------|--------------------|
| Queue utilisation | 70 % | 90 % |
| Drop rate | 1 % | 5 % |
| Check interval | 5 s | — |
| Consecutive checks before alert | 3 | — |
| Warning alert interval | 300 s (5 min) | — |
| Critical alert interval | 60 s | — |

**Alert actions:**
- Raises `OnBackpressureDetected` event with `BackpressureAlert` record
- Raises `OnBackpressureResolved` when pressure subsides
- Optionally sends webhook notification via `DailySummaryWebhook`

#### EventPipeline Internal Thresholds

| Threshold | Level | Action |
|-----------|-------|--------|
| 80 % queue utilisation | Warning | One-time log: "Events may be dropped if queue fills" |
| < 50 % queue utilisation | Recovery | Warning flag reset |
| Queue full | Drop | Event dropped; `DroppedEventAuditTrail.RecordDroppedEventAsync()` called |

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| Tiered alerting | Warning (70 %) and critical (90 %) thresholds with configurable cooldowns |
| Drop rate tracking | Alerts on 1 %+ drop rate, not just absolute queue depth |
| Debounced alerts | 3 consecutive checks required before triggering (prevents flapping) |
| Webhook integration | External notification on sustained backpressure |
| Audit trail | Every dropped event recorded with reason ("backpressure_queue_full") |
| Non-blocking default | `TryPublish` never blocks providers; DropOldest prevents cascade disconnects |

**Improvements since prior review:**

| Prior State | Current State |
|-------------|---------------|
| Wait mode (blocks producers, risks disconnects) | DropOldest mode (non-blocking, prevents cascading failures) |
| No overflow metrics | Full `BackpressureAlertService` with tiered thresholds |
| No dropped event tracking | `DroppedEventAuditTrail` records all drops |
| No external alerting | Webhook notification on sustained backpressure |

### Remaining Recommendations

1. **Load shedding** — Prioritise essential symbols during sustained overload
2. **Adaptive capacity** — Dynamically resize channel capacity based on sustained utilisation patterns
3. **Upstream signaling** — Notify providers to reduce subscription scope before drops start

---

## F. Connection Management & Failover Evaluation

### Failover Implementation

Location: `Infrastructure/Adapters/Failover/FailoverAwareMarketDataClient.cs`

The `FailoverAwareMarketDataClient` wraps multiple `IMarketDataClient` instances and transparently switches on failure:

```
┌────────────────────────────────────────────┐
│       FailoverAwareMarketDataClient        │
│                                            │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐ │
│  │ Primary  │  │ Backup 1 │  │ Backup 2 │ │
│  │ (active) │  │ (standby)│  │ (standby)│ │
│  └──────────┘  └──────────┘  └──────────┘ │
│       │                                    │
│       ▼                                    │
│  ┌──────────────────────────────────────┐  │
│  │    Subscription Tracking             │  │
│  │  - Active depth subs (per symbol)    │  │
│  │  - Active trade subs (per symbol)    │  │
│  └──────────────────────────────────────┘  │
│       │                                    │
│       ▼                                    │
│  ┌──────────────────────────────────────┐  │
│  │    StreamingFailoverService          │  │
│  │  - OnFailoverTriggered event         │  │
│  │  - OnFailoverRecovered event         │  │
│  └──────────────────────────────────────┘  │
└────────────────────────────────────────────┘
```

**Failover process:**

| Step | Action |
|------|--------|
| 1 | Acquire `SemaphoreSlim` switch lock |
| 2 | Connect new provider |
| 3 | Re-subscribe all active depth and trade subscriptions |
| 4 | Swap `_activeClient` (volatile) |
| 5 | Gracefully disconnect old provider |
| 6 | Record metrics to `StreamingFailoverService` |

**Immediate failover on connect:** `TryFailoverConnectAsync()` iterates through backup providers if the primary fails during initial `ConnectAsync()`.

### Provider Degradation Scoring

Location: `Application/Monitoring/ProviderDegradationScorer.cs`

Composite health score per provider (0.0 = healthy, 1.0 = degraded):

| Component | Weight | Threshold | Max |
|-----------|--------|-----------|-----|
| Connection (missed heartbeats) | 35 % | — | 5 missed |
| Latency (P95) | 25 % | 200 ms | 2,000 ms |
| Error rate | 25 % | 5 % | — |
| Reconnects per hour | 15 % | — | 10/hour |

**Degradation threshold:** composite score ≥ 0.6 triggers degradation event.
**Evaluation interval:** every 30 s.

### Connection Health Monitor

Location: `Application/Monitoring/ConnectionHealthMonitor.cs`

| Feature | Configuration |
|---------|---------------|
| Heartbeat interval | 30 s |
| Heartbeat timeout | 60 s |
| High latency threshold | 500 ms |
| Events | `OnConnectionLost`, `OnConnectionRecovered`, `OnHeartbeatMissed`, `OnHighLatency` |

### Connection State Machine

```
┌─────────────┐
│ Disconnected│◀──────────────────────────────┐
└──────┬──────┘                               │
       │ Connect()                            │
       ▼                                      │
┌─────────────┐                               │
│ Connecting  │                               │
└──────┬──────┘                               │
       │ Success          Failure             │
       ▼                    │                 │
┌─────────────┐            ▼                  │
│  Connected  │     ┌─────────────┐           │
└──────┬──────┘     │  Retrying   │───────────┤
       │            └─────────────┘           │
       │ Error               │ Failover       │
       ▼                     ▼                │
┌─────────────┐     ┌─────────────┐           │
│Reconnecting │     │  Failover   │───────────┘
└──────┬──────┘     │  Switch     │
       │            └─────────────┘
       │ Max retries exceeded
       ▼
┌─────────────┐
│  Degraded   │ (scored by ProviderDegradationScorer)
└─────────────┘
```

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| Automatic failover | Transparent provider switching with subscription recovery |
| Composite scoring | Multi-factor degradation detection (connection, latency, errors, reconnects) |
| Weighted scoring | Connection health (35 %) weighted highest as most critical signal |
| Lock-protected switching | SemaphoreSlim prevents concurrent failover attempts |
| Subscription persistence | All active subscriptions re-established on backup provider |

**Improvements since prior review:**

| Prior State | Current State |
|-------------|---------------|
| No failover between providers | `FailoverAwareMarketDataClient` with automatic switching |
| Manual recovery for some scenarios | Fully automated failover + re-subscription |
| No degradation detection | `ProviderDegradationScorer` with composite scoring |
| No standardised reconnection | `WebSocketReconnectionHelper` with gated jittered backoff |

### Remaining Recommendations

1. **Warm standby** — Pre-connect backup providers to reduce failover latency
2. **Connection pooling** — Multiple connections per provider for high symbol counts
3. **Failover testing** — Chaos engineering hooks to validate failover paths

---

## G. Latency & Observability Analysis

### Per-Provider Latency Histograms

Location: `Application/Monitoring/ProviderLatencyService.cs`

| Metric | Per Provider |
|--------|-------------|
| Sample count | Yes |
| Mean latency | Yes |
| P50 | Yes |
| P95 | Yes |
| P99 | Yes |
| Min / Max | Yes |
| Per-symbol breakdown | Optional |
| Last update time | Yes |
| Hourly cleanup | Automatic |

**Recording methods:**
- `RecordLatency(provider, latencyMs, symbol?)` — direct millisecond value
- `RecordLatency(provider, eventTime, receiveTime, symbol?)` — computed from timestamps

### Clock Skew Estimation

Location: `Application/Monitoring/ClockSkewEstimator.cs`

| Parameter | Value |
|-----------|-------|
| Algorithm | Exponentially Weighted Moving Average (EWMA) |
| Smoothing factor (α) | 0.05 |
| Formula | `ewma = α × skewMs + (1 - α) × ewma` |
| Per-provider tracking | Yes |
| Snapshot data | Estimated skew, sample count, min/max |

Positive skew indicates provider clock is behind local time. Detects NTP jumps and gradual drift.

### Spread Monitoring

Location: `Application/Monitoring/SpreadMonitor.cs`

| Preset | Wide Spread Threshold (bps) | Percentage | Alert Cooldown |
|--------|----------------------------|------------|----------------|
| Default | 100 bps | 1.0 % | 10 s |
| LargeCap | 10 bps | 0.1 % | 5 s |
| SmallCap | 500 bps | 5.0 % | 30 s |

**Tracks per symbol:** current spread, average, min, max, wide spread count, consecutive wide spreads.
**Cleanup:** Removes inactive symbols after 24 hours.

### End-to-End Latency Components

| Stage | Typical Latency | Variance |
|-------|-----------------|----------|
| Network (provider → app) | 1 – 50 ms | High (network dependent) |
| WebSocket parsing | 0.1 – 1 ms | Low |
| Channel enqueue (TryPublish) | 0.01 – 0.1 ms | Very low |
| Channel dequeue | 0.01 – 0.1 ms | Very low |
| Collector processing | 0.1 – 1 ms | Low |
| WAL append | 0.1 – 1 ms | Low |
| Storage write (batched) | 1 – 5 ms | Medium |
| **Total** | **3 – 60 ms** | **Medium** |

### Evaluation

**Improvements since prior review:**

| Prior State | Current State |
|-------------|---------------|
| No latency histograms | `ProviderLatencyService` with P50/P95/P99 per provider |
| No clock skew detection | `ClockSkewEstimator` with EWMA |
| No spread monitoring | `SpreadMonitor` with preset configurations |
| Single-event storage writes | Batched writes (100 events) reduce I/O overhead |
| No SLO definitions at runtime | `SloDefinitionRegistry` with P95 latency, drop-rate, and availability SLOs |
| No centralized alert dispatch | `AlertDispatcher` with per-severity and per-category routing |

### Remaining Recommendations

1. **Object pooling** — Reduce GC pressure for `MarketEvent` allocations in hot path
2. **SIMD-accelerated parsing** — Faster JSON deserialization for high-throughput providers
3. **Latency-based routing** — Use `ProviderLatencyService` data to prefer lower-latency providers

---

## H. Data Quality Monitoring

### Overview

Location: `Application/Monitoring/DataQuality/`

A comprehensive data quality monitoring subsystem was introduced after the prior review. `DataQualityMonitoringService` is the central orchestrator that initialises and wires together seven specialised analyzers.

### Sub-Services

| Component | Responsibility |
|-----------|----------------|
| `CompletenessScoreCalculator` | Calculates data completeness scores (A–F grade) per symbol per day |
| `GapAnalyzer` | Detects and classifies data gaps (Minor/Moderate/Significant/Major/Critical) |
| `SequenceErrorTracker` | Tracks out-of-order and duplicate sequence numbers |
| `AnomalyDetector` | Detects price spikes, volume outliers, and stale data using statistical methods |
| `LatencyHistogram` | Per-symbol latency distribution for fine-grained quality analysis |
| `CrossProviderComparisonService` | Identifies price/volume discrepancies across providers |
| `DataQualityReportGenerator` | Generates structured quality reports from all analyzers |

### Liquidity-Aware Thresholds

`LiquidityProfileProvider` maps a symbol's liquidity tier to monitoring parameters, preventing false positives for illiquid instruments:

| Profile | Gap Threshold | Expected Events/Hour | Freshness SLA | Spread Threshold |
|---------|--------------|----------------------|---------------|-----------------|
| High | 60 s | 1,000 | 60 s | 10 bps |
| Normal | 120 s | 200 | 120 s | 50 bps |
| Low | 600 s | 20 | 600 s | 500 bps |
| VeryLow | 1,800 s | 5 | 1,800 s | 1,000 bps |
| Minimal | 3,600 s | 1 | 3,600 s | 2,000 bps |

Profiles are registered per symbol via `DataQualityMonitoringService.RegisterSymbolLiquidity(symbol, profile)`. All sub-services automatically adopt the relevant thresholds.

### Data Freshness SLA Monitor

`DataFreshnessSlaMonitor` enforces data freshness SLAs with two tiers:

| Parameter | Default |
|-----------|---------|
| Warning threshold | 60 s |
| Critical threshold | 300 s |
| Check interval | 10 s |
| Alert cooldown | 300 s |
| Market hours only | Yes (13:30 – 20:00 UTC) |
| Per-symbol overrides | Supported |

### Gap Classification

Gaps are classified based on duration relative to the symbol's liquidity profile:

| Severity | Description |
|----------|-------------|
| Minor | < 1 minute (or proportional equivalent for illiquid symbols) |
| Moderate | 1 – 5 minutes |
| Significant | 5 – 30 minutes |
| Major | 30 – 60 minutes |
| Critical | > 60 minutes |

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| Unified orchestration | Single `DataQualityMonitoringService` with consistent event wiring |
| Liquidity awareness | Thresholds scale with symbol liquidity — no false alerts on illiquid instruments |
| Cross-provider comparison | Detects price discrepancies when multiple providers are active |
| Report generation | Structured quality reports available for API consumption |
| Statistical anomaly detection | Price spike and volume outlier detection using rolling statistics |
| SLA freshness enforcement | Per-symbol thresholds with market-hours awareness |

**Remaining Recommendations:**

1. **Completeness back-fill** — Feed historical data through `CompletenessScoreCalculator` on startup to populate initial baselines
2. **Cross-provider auto-routing** — Use `CrossProviderComparisonService` discrepancy detection to automatically prefer higher-quality provider
3. **Quality-gated backfill** — Trigger gap-fill backfill automatically when `GapAnalyzer` detects a significant gap

---

## I. Monitoring Core Infrastructure

### Overview

Location: `Application/Monitoring/Core/`

A centralized monitoring infrastructure layer was added to provide structured alert dispatch, health aggregation, SLO tracking, and runbook integration.

### AlertDispatcher

`AlertDispatcher` implements the central alert bus:

| Feature | Detail |
|---------|--------|
| Severity routing | Info / Warning / Error / Critical |
| Category tracking | Per-category alert counts |
| Recent alert buffer | Configurable ring buffer (default: 1,000 entries) |
| Subscription model | Subscribe/unsubscribe handlers via GUID |
| Statistics | Total alerts, alerts by severity, by category, by source |

### HealthCheckAggregator

`HealthCheckAggregator` runs all registered `IHealthCheckProvider` implementations in parallel:

| Feature | Detail |
|---------|--------|
| Parallel execution | All checks run concurrently |
| Per-check timeout | 5 s default; configurable |
| Aggregated report | `AggregatedHealthReport` with worst-case composite state |
| Dynamic registration | `Register()` / `Unregister()` at runtime |

### SloDefinitionRegistry

`SloDefinitionRegistry` holds runtime SLO definitions with metric linkage:

| SLO ID | Subsystem | Target | Critical Threshold |
|--------|-----------|--------|-------------------|
| SLO-ING-001 | Ingestion | P95 latency ≤ 2 s | 5 s |
| SLO-ING-002 | Ingestion | Drop rate ≤ 0.1 % | — |
| SLO-AV-001 | Availability | — (via MeridianDown alert) | — |

Each SLO entry links to the relevant Prometheus metric, alert rule, and runbook section.

### AlertRunbookRegistry

`AlertRunbookRegistry` maps alert rule names to operator runbook sections:

| Feature | Detail |
|---------|--------|
| Runbook URL | Direct link to operator runbook section |
| Probable causes | Pre-populated list for faster triage |
| Immediate actions | Step-by-step mitigation instructions |
| SLO linkage | Each alert maps to its SLO ID |
| Incident priority | P1 / P2 / P3 classification |

**Key benefit:** When `AlertDispatcher` fires an alert, the runbook entry is immediately available — eliminating the need for engineers to search documentation during incidents.

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| Centralized dispatch | Single `AlertDispatcher` for all subsystem alerts |
| Parallel health checks | `HealthCheckAggregator` avoids serial health check delays |
| Formal SLO definitions | `SloDefinitionRegistry` makes compliance measurable at runtime |
| Runbook linkage | `AlertRunbookRegistry` reduces MTTR by embedding remediation guidance |

**Remaining Recommendations:**

1. **Webhook integration** — Route `AlertDispatcher` alerts to PagerDuty or Slack via webhook
2. **SLO burn-rate alerts** — Track error budget consumption rate, not just point-in-time violations
3. **Health check caching** — Cache last-known-good state to avoid thundering-herd on health endpoints during incidents

---

## J. Scalability Assessment

### Current Limits

| Resource | Practical Limit | Bottleneck |
|----------|-----------------|------------|
| Symbols per provider | 100 – 500 | Provider limits, memory |
| Events per second | 100,000+ | Channel throughput, single consumer |
| Concurrent providers | 5 (+ failover) | Connection management |
| Memory usage | 1 – 2 GB | Event buffering, depth data |
| Subscription IDs | 100 K per provider | Non-overlapping ranges |

### Message Buffering (StockSharp)

StockSharp uses a dedicated `EventPipelinePolicy.MessageBuffer` channel (50 K capacity, DropOldest) to absorb high-frequency bursts from the Hydra connector pattern without blocking the connector thread.

### Scaling Patterns

**Vertical Scaling (Current):**
- Add CPU cores → More parallel processing
- Add RAM → Larger channel buffers
- Faster disk → Higher write throughput
- SSD → Lower WAL latency

**Horizontal Scaling (Future):**
- Multiple collector instances (partitioned by symbol)
- Load balancer for provider connections
- Distributed storage (Kafka, cloud)

### Recommendations for Scale

| Scale | Recommendation |
|-------|----------------|
| 100 symbols | Current architecture sufficient |
| 500 symbols | Monitor backpressure alerts; tune channel capacity if needed |
| 1,000+ symbols | Add parallel consumers; consider symbol-partitioned instances |
| 10,000+ symbols | Distributed architecture with Kafka or equivalent |

---

## K. Alternative Architecture Patterns

### Pattern 1: Actor Model (Akka.NET / Proto.Actor)

**Pros:**
- Natural fit for per-symbol state
- Built-in supervision and recovery
- Location transparency for scaling

**Cons:**
- Learning curve
- Debugging complexity
- Framework overhead

**Verdict:** Consider for future horizontal scaling

---

### Pattern 2: Reactive Extensions (Rx.NET)

**Pros:**
- Powerful composition operators
- Built-in backpressure (IObservable)
- Time-windowing, throttling built-in

**Cons:**
- Steep learning curve
- Debugging reactive chains difficult
- Memory overhead for complex pipelines

**Verdict:** Good for specific use cases (e.g., aggregations), not wholesale replacement

---

### Pattern 3: Dataflow (TPL Dataflow)

**Pros:**
- Built into .NET
- Good for pipeline composition
- Bounded blocks with backpressure

**Cons:**
- Less flexible than Channels
- Heavier weight
- More complex configuration

**Verdict:** Current Channel-based approach is simpler and sufficient

---

### Pattern 4: Message Broker (Kafka / RabbitMQ)

**Pros:**
- Distributed by design
- Persistence and replay
- Multiple consumers
- Proven at massive scale

**Cons:**
- Operational complexity
- Additional infrastructure
- Latency overhead

**Verdict:** Consider when horizontal scaling required

---

## L. Summary & Recommendations

### Architecture Maturity Assessment

The streaming architecture has continued to evolve since the February 2026 re-evaluation:

| Capability | Feb 2026 Status | Mar 2026 Status |
|------------|-----------------|-----------------|
| Provider abstraction | Mature | Mature |
| Channel-based pipeline | Mature (WAL, batching, audit trail) | Mature + event validation + dedup ledger |
| Resilience policies | Profile-based (Default / HighFrequency / Resilient) | Unchanged |
| Backpressure handling | Tiered alerting (70 % / 90 %) + DropOldest | Unchanged |
| Failover | Automatic with subscription recovery | Unchanged + `StreamingFailoverRegistry` |
| Latency observability | P50/P95/P99 histograms + clock skew detection | Unchanged |
| Spread monitoring | Per-symbol with preset configurations | Unchanged |
| Degradation scoring | Composite 4-factor scoring | Unchanged |
| Connection management | Centralised manager with heartbeat + jitter | Unchanged |
| Event validation | Not implemented | `IEventValidator` + `DeadLetterSink` (ADR-007) |
| Deduplication | Not implemented | `PersistentDedupLedger` with TTL cache + compaction |
| Data quality monitoring | Not implemented | `DataQualityMonitoringService` with 7 analyzers |
| SLA freshness | Not implemented | `DataFreshnessSlaMonitor` with configurable tiers |
| Liquidity-aware thresholds | Not implemented | `LiquidityProfileProvider` (5 tiers) |
| Alert infrastructure | Ad-hoc | `AlertDispatcher` + `AlertRunbookRegistry` |
| SLO definitions | Not implemented | `SloDefinitionRegistry` with metric + runbook linkage |
| Health aggregation | Not implemented | `HealthCheckAggregator` with parallel execution |

### Retain (Proven Components)

1. **Channel-based pipeline** — Efficient, WAL-backed, now with validation and dedup
2. **Polly resilience** — Industry-standard, profile-based per provider
3. **Provider abstraction** — Clean separation via `IMarketDataClient`
4. **Micro-batching** — 100-event batches balance throughput and latency
5. **Failover client** — Transparent provider switching with subscription recovery
6. **Degradation scorer** — Multi-factor health assessment
7. **Data quality monitoring** — Comprehensive liquidity-aware quality subsystem

### Remaining Improvements

| Priority | Improvement | Benefit |
|----------|-------------|---------|
| Medium | Parallel channel consumers | 2 – 4x throughput when single consumer saturates |
| Medium | Object pooling for hot path | Reduce GC pauses under sustained load |
| Medium | Warm standby for failover | Reduce failover latency from seconds to milliseconds |
| Medium | Dedup ledger compaction scheduling | Prevent unbounded `dedup_ledger.jsonl` growth |
| Low | Adaptive batch sizing | Better throughput/latency balance across load levels |
| Low | Priority channels per event type | Latency-sensitive quote processing |
| Low | Kafka integration | Horizontal scaling for 1,000+ symbol deployments |
| Low | Chaos engineering hooks | Validate failover paths in staging |
| Low | AlertDispatcher webhook integration | Route alerts to PagerDuty or Slack |
| Low | SLO burn-rate tracking | Measure error budget consumption rate |

### Performance Targets

| Metric | Prior Target | Current Estimate | Notes |
|--------|-------------|-----------------|-------|
| Events/second | 250 K | 100 K+ (single consumer) | Sufficient for current use cases |
| P99 latency | 20 ms | 30 – 60 ms (end-to-end) | Dominated by network + storage |
| Recovery time | 10 s | 5 – 15 s (failover) | Automatic with re-subscription |
| Memory under load | 300 MB | 200 – 500 MB | Depends on symbol count and depth |

---

## Key Insight

Since the February 2026 assessment, the streaming architecture has added end-to-end event integrity guarantees (validation + dead-letter routing, persistent deduplication, schema validation), a comprehensive data quality monitoring subsystem with liquidity-aware thresholds and SLA enforcement, and a formal monitoring infrastructure with centralized alert dispatch, SLO definitions, and runbook linkage.

The system is now production-grade across all primary dimensions: data integrity, resilience, observability, quality, and operational readiness. Remaining improvements — parallel consumers, object pooling, warm standby, dedup compaction scheduling — are operational refinements rather than architectural gaps, and should be driven by observed production bottlenecks.

---

*Evaluation Date: 2026-03-11*
*Prior Evaluation: 2026-02-22*
