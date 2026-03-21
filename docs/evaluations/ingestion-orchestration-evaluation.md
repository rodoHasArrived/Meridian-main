# Ingestion Orchestration Evaluation

## Meridian — Scheduler & Backfill Control Assessment

**Date:** 2026-02-12
**Status:** Evaluation Complete — All P0/P1 Recommendations Implemented
**Last Updated:** 2026-03-11
**Author:** Architecture Review

---

## Executive Summary

This document evaluates ingestion orchestration capabilities for the Meridian, with emphasis on scheduling, backfill execution, resumability, idempotency, and operational controls.

**Original Finding (2026-02-12):** The architecture had strong building blocks (provider abstraction, background services, and storage durability patterns), but orchestration maturity was uneven. The highest-value next step was a unified job model treating realtime collection and historical backfills as first-class managed workloads.

**Current State (2026-03-11):** All P0/P1 recommendations have been implemented. The ingestion pipeline is now a production-grade system with a unified job state machine, cron/session-aware scheduling, deterministic checkpointing, idempotent writes, backpressure alerting, dead-letter handling, schema evolution, and quantified SLOs.

---

## A. Evaluation Scope

The assessment focused on:

1. Job lifecycle management (create/start/pause/resume/cancel).
2. Scheduler policy quality (interval, market-session-aware, and event-triggered).
3. Failure handling and retry behavior.
4. Backfill checkpointing and partial progress recovery.
5. Multi-provider coordination and deduplication safeguards.
6. Operator ergonomics and auditability.

---

## B. Current-State Assessment

### Strengths

| Area | Observation | Impact |
|------|-------------|--------|
| Modularity | Providers and storage sinks are cleanly abstracted | Enables scheduler-independent execution strategy |
| Durability primitives | WAL + tiered storage reduce data-loss risk | Supports long-running ingestion jobs |
| Monitoring baseline | Health and quality monitoring reused for orchestration signals | Job telemetry at low implementation cost |
| Desktop workflow hooks | UI exposes backfill, schedule management, and status | Full operator control-plane in WPF and web dashboard |
| Bounded channel pipeline | `EventPipelinePolicy` presets enforce consistent backpressure | Prevents memory exhaustion and deadlocks across all channels |
| Backpressure alerting | `BackpressureAlertService` monitors queue utilization and drop rate | Ops-visible signals before data loss escalates |
| Audit trails | `DeadLetterSink` + `DroppedEventAuditTrail` capture rejected and dropped events | Enables post-incident replay and root-cause analysis |
| Schema evolution | `SchemaUpcasterRegistry` chains upcasters for transparent migration | Forward compatibility for stored events |
| Hot-reload subscriptions | `SubscriptionOrchestrator` applies config changes at runtime without restart | Zero-downtime symbol management |
| Quantified SLOs | `SloDefinitionRegistry` defines P95 latency and drop-rate thresholds | Measurable reliability targets for ingestion |

### Resolved Historical Gaps

All gaps identified in the original evaluation have been addressed:

| Gap (Original) | Resolution |
|----------------|------------|
| No unified job contract across realtime/backfill flows | `IngestionJob` with 7-state machine; `IngestionJobService` with disk persistence |
| Limited checkpoint semantics | `IngestionCheckpointToken` persisted per job; `BackfillCheckpointService` tracks per-symbol cursor |
| Retry policy lacks workload-level intent | `IngestionJob.RetryEnvelope` with configurable max retries and exponential backoff |
| Missing idempotency strategy for repeated backfills | `PersistentDedupLedger` with SHA256-based event keys, TTL expiry, and JSONL-backed compaction |
| Weak operator timeline/audit view | Desktop UI job timeline, `DroppedEventAuditTrail`, `DeadLetterSink`, and centralized `AlertDispatcher` |

---

## C. Target Capability Model

### Capability 1: Unified Ingestion Job State Machine ✅ Implemented

`Draft → Queued → Running → Paused → Completed | Failed | Cancelled`

Implemented metadata:
- JobId, workload type (`Realtime`, `Historical`, `GapFill`, `ScheduledBackfill`), symbols, provider, timeframe.
- Checkpoint token (symbol/date cursor, last durable offset).
- Retry envelope (attempt count, base delay, exponential backoff).
- SLA expectations (freshness target, completion deadline) via `IngestionSla`.

### Capability 2: Policy-Driven Scheduler ✅ Implemented

All three policy classes implemented:
- **Cron/time-based**: `CronExpressionParser` (5-field cron with timezone support).
- **Session-aware**: `ScheduledBackfillService` pauses during non-market hours; `TradingCalendar` drives open/close windows.
- **Signal-triggered**: `GapBackfillService` repairs detected data gaps; `DataGapAnalyzer` triggers gap-fill runs.

### Capability 3: Deterministic Resumability ✅ Implemented

Resume behavior by workload type:
- **Realtime**: restart from latest stream + gap-fill window via `GapBackfillService`.
- **Backfill**: resume from last committed bar cursor stored in `BackfillCheckpointService`.

`IngestionJobService.GetResumableJobs()` surfaces all Failed/Paused jobs with valid checkpoints.

### Capability 4: Idempotent Writes by Design ✅ Implemented

Consistent dedupe keys and merge semantics:
- Key shape per event type: trades hash `(timestamp|price|size|aggressor|venue)`; quotes hash `(timestamp|bidPrice|askPrice|bidSize|askSize)`; L2/default use sequence number.
- `PersistentDedupLedger`: in-memory `ConcurrentDictionary` (max 500k entries, 24-hour TTL) backed by JSONL file with automatic compaction.
- Backfill reruns report `DeduplicatedCount` metrics to distinguish reconciliation runs.

---

## D. 90-Day Implementation Roadmap

### Month 1 (P0) — Job Foundation ✅ Complete

- ✅ `IngestionJob` model and persisted state transitions.
- ✅ Normalized start/pause/resume/cancel APIs across workloads.
- ✅ Checkpoints persisted at symbol and batch boundaries.

### Month 2 (P1) — Scheduler Policies + Retry Classes ✅ Complete

- ✅ Scheduler policy engine (cron + session-aware via `BackfillSchedule` types: GapFill, FullBackfill, RollingWindow, EndOfDay).
- ✅ Retry classes via `IngestionJob.RetryEnvelope` with jittered exponential backoff.
- ✅ Provider throttling guards in `BackfillWorkerService`.

### Month 3 (P1/P2) — UX + Operability ✅ Complete

- ✅ Job timeline and event trail in WPF desktop app.
- ✅ "Resume from checkpoint" action surfaced via `GetResumableJobs()` API.
- ✅ Orchestration KPIs emitted: `BackpressureAlertService`, `SloDefinitionRegistry`, Prometheus metrics.

---

## E. Success Metrics

Track the following KPIs:

- **Backfill completion reliability:** % jobs completed without full restart.
- **Mean recovery time:** interruption-to-resume duration.
- **Duplicate write rate:** duplicates detected per million events (tracked by `PersistentDedupLedger.TotalDuplicates`).
- **Operator efficiency:** mean clicks/time to recover failed job.
- **Provider safety:** throttling incidents per week.

Quantified SLO baselines (from `SloDefinitionRegistry`):
- **SLO-ING-001**: End-to-end ingestion latency P95 < 2 seconds.
- **SLO-ING-002**: Event drop rate < 0.1% (critical threshold: 1%).
- **SLO-DC-001**: Daily data completeness ≥ 95%.
- **SLO-DC-002**: Maximum data gap duration ≤ 5 minutes.

---

## Recommendation

All original recommendations have been implemented. Future work should focus on:

1. **Operator runbook completeness** — wire `AlertRunbookRegistry` entries to actionable dashboards.
2. **Cross-provider reconciliation** — extend `PersistentDedupLedger` merge semantics for multi-source backfills.
3. **Dynamic SLO tuning** — expose `SloDefinitionRegistry` thresholds as runtime-configurable settings.

---

## F. Implementation Follow-Up (2026-02-25)

All P0 and P1 recommendations from this evaluation have been implemented:

| Capability | Status | Implementation |
|------------|--------|----------------|
| Unified Job State Machine | ✅ Done | `IngestionJob` contract with 7-state machine: Draft → Queued → Running → Paused → Completed / Failed / Cancelled. Supports `Realtime`, `Historical`, `GapFill`, `ScheduledBackfill` workload types. |
| Job Lifecycle Service | ✅ Done | `IngestionJobService` with disk persistence, concurrent job registry, create/transition/cancel/delete APIs. Events: `JobStateChanged`, `CheckpointUpdated`. |
| Checkpoint & Resume | ✅ Done | `IngestionCheckpointToken` (symbol/date cursor, last offset) persisted per job. `BackfillCheckpointService` tracks per-symbol progress with resume from last committed cursor. |
| Cron/Session-Aware Scheduler | ✅ Done | `CronExpressionParser` (5-field cron with timezone support), `BackfillSchedule` (GapFill, FullBackfill, RollingWindow, EndOfDay types), `BackfillScheduleManager` with CRUD + preset templates. |
| Scheduled Execution Service | ✅ Done | `ScheduledBackfillService` with dual-loop architecture (scheduler + executor), priority queue, catch-up logic for missed schedules, and market-hours-aware pausing. |
| Retry & Throttling | ✅ Done | `IngestionJob.RetryEnvelope` with configurable max retries, base delay, and exponential backoff. Provider rate-limit guards in `BackfillWorkerService`. |
| Idempotent Writes | ✅ Done | `PersistentDedupLedger` with SHA256-based event keys, TTL expiry, JSONL-backed persistence, and automatic compaction. |
| Gap Detection & Repair | ✅ Done | `DataGapAnalyzer` detects missing data periods. `GapBackfillService` and `RunImmediateGapFillAsync()` for automatic repair. |
| Desktop UX | ✅ Done | Backfill UI pages, checkpoint resume actions, and schedule management in WPF desktop app. API endpoints exposed via `/api/backfill/schedules/*` and `/api/backfill/gap-fill`. |

---

## G. Additional Pipeline Infrastructure (2026-03-11)

The following capabilities were implemented beyond the original P0/P1 roadmap, completing the ingestion pipeline architecture:

### G.1 EventPipeline — High-Throughput Async Channel

`src/Meridian.Application/Pipeline/EventPipeline.cs`

The central orchestrator that decouples producers from storage sinks:

- **Channel type**: `BoundedChannel<MarketEvent>` (capacity governed by `EventPipelinePolicy`).
- **Batching**: accumulates 100-event batches before writing to storage sinks.
- **Periodic flush**: flushes every 5 seconds or on explicit `FlushAsync()`.
- **WAL integration**: optionally writes to `WriteAheadLog` before sink; commits after batch flush.
- **Validation**: routes invalid events to `DeadLetterSink` via `IEventValidator`.
- **Deduplication**: checks every event against `PersistentDedupLedger` before writing.
- **Recovery**: `RecoverAsync()` replays uncommitted WAL entries on startup.

**Pipeline statistics** (`GetStatistics()`): `PublishedCount`, `DroppedCount`, `ConsumedCount`, `RejectedCount`, `DeduplicatedCount`, `RecoveredCount`, `PeakQueueSize`, `AverageProcessingTimeUs`.

### G.2 EventPipelinePolicy — Bounded Channel Presets (ADR-013)

`src/Meridian.Core/Pipeline/EventPipelinePolicy.cs`

Six static presets enforce consistent backpressure across all channels:

| Preset | Capacity | Full Mode | Metrics | Use Case |
|--------|----------|-----------|---------|----------|
| `Default` | 100,000 | DropOldest | Yes | General event pipelines |
| `HighThroughput` | 50,000 | DropOldest | Yes | Market data streaming clients |
| `MessageBuffer` | 50,000 | DropOldest | No | Internal WebSocket message parsing |
| `MaintenanceQueue` | 100 | Wait | No | Background tasks (no drops) |
| `Logging` | 1,000 | DropOldest | No | Structured log channels |
| `CompletionQueue` | 500 | Wait | No | Backfill completion notifications |

### G.3 Backpressure Alerting

`src/Meridian.Application/Monitoring/BackpressureAlertService.cs`

Monitors pipeline queue utilization and drop rate, emitting debounced alerts:

- Warning at 70% utilization or 1% drop rate (throttled to one alert per 5 minutes).
- Critical at 90% utilization or 5% drop rate (throttled to one alert per minute).
- Requires 3 consecutive threshold breaches before alerting (debounce).
- Resolves when utilization drops below 50%.
- Implements `IBackpressureSignal` (`IsUnderPressure`, `QueueUtilization`) observed by producers.

### G.4 Dead-Letter and Audit Trail

`src/Meridian.Application/Pipeline/DeadLetterSink.cs`  
`src/Meridian.Application/Pipeline/DroppedEventAuditTrail.cs`

- **DeadLetterSink**: records events that fail `IEventValidator` to `_dead_letter/rejected_events.jsonl` with error details. Enables post-fix replay.
- **DroppedEventAuditTrail**: records events dropped by backpressure to `_audit/dropped_events.jsonl` with per-symbol statistics for gap detection.

### G.5 Schema Upcasting

`src/Meridian.Application/Pipeline/SchemaUpcasterRegistry.cs`

Transparent schema migration chain for stored events:

- Chains multiple `ISchemaUpcaster<MarketEvent>` implementations for multi-step evolution (e.g., v0→v1→v2).
- Integrated into `EventPipeline` replay path so older JSONL files are automatically migrated on read.
- Tracks `TotalUpcastAttempts`, `SuccessfulUpcasts`, `FailedUpcasts`.

### G.6 SubscriptionOrchestrator — Hot-Reload Symbol Management

`src/Meridian.Application/Subscriptions/SubscriptionOrchestrator.cs`

Applies `AppConfig` symbol changes at runtime without restarting the service:

- Registers/unregisters symbols with `MarketDepthCollector`, `TradeDataCollector`, and `OptionDataCollector`.
- Calls `IMarketDataClient.SubscribeMarketDepth()` / `SubscribeTrades()` and tracks returned subscription IDs per symbol.
- Thread-safe (`_gate` lock) to prevent races during concurrent `Apply()` calls.
- Exposes `DepthSubscriptions`, `TradeSubscriptions`, `OptionSubscriptions` dictionaries for introspection.

### G.7 Monitoring/Core — SLOs and Centralized Alerting

`src/Meridian.Application/Monitoring/Core/`

- **`SloDefinitionRegistry`**: pre-defined SLOs (SLO-ING-001 P95 latency, SLO-ING-002 drop rate, SLO-DC-001 completeness, SLO-DC-002 max gap). Maps each SLO to alert rules and runbooks.
- **`AlertDispatcher`**: centralized alert publishing with severity/category/source filtering; maintains a recent-alerts buffer and tracks statistics by severity and category.
- **`AlertRunbookRegistry`**: maps alert codes to operational runbook entries with remediation guidance.
- **`HealthCheckAggregator`**: aggregates health checks from all subsystems into a single composite health status.

### G.8 Subscriptions Supporting Services

`src/Meridian.Application/Subscriptions/Services/`

Eleven supporting services complete the subscription management surface:

| Service | Purpose |
|---------|---------|
| `SymbolManagementService` | Add/remove symbols; list monitored and archived |
| `PortfolioImportService` | Bulk import symbols from portfolio files |
| `IndexSubscriptionService` | Manage index-level subscriptions |
| `SchedulingService` | Schedule and trigger ingestion jobs |
| `MetadataEnrichmentService` | Enrich symbols with market metadata |
| `AutoResubscribePolicy` | Automatic resubscription on provider failure |
| `SymbolImportExportService` | CSV/JSON import/export of symbol sets |
| `TemplateService` | Manage subscription templates |
| `WatchlistService` | Create and manage symbol watchlists |
| `SymbolSearchService` | Search and discover symbols across providers |
| `BatchOperationsService` | Bulk add/remove/update symbols |

### G.9 Test Coverage

`tests/Meridian.Tests/Application/Pipeline/` — 11 test files covering:

| Test File | Coverage |
|-----------|----------|
| `EventPipelineTests.cs` | Single/multi-event publish, WAL recovery, backpressure, statistics |
| `BackpressureSignalTests.cs` | `IBackpressureSignal` contract, utilization tracking, pressure state transitions |
| `IngestionJobServiceTests.cs` | Job creation, state transitions, checkpoints, per-symbol progress |
| `DroppedEventAuditTrailTests.cs` | Drop counting, per-symbol stats, JSONL file generation |
| `CompositePublisherTests.cs` | Multi-sink scenarios |
| `WalEventPipelineTests.cs` | Write-Ahead Log integration and recovery |
| `EventPipelineMetricsTests.cs` | Prometheus metrics emission |
| `GoldenMasterPipelineReplayTests.cs` | Regression/replay scenario coverage |
| `BackfillProgressTrackerTests.cs` | Historical data progress tracking |
| `IngestionJobTests.cs` | Job contract model validation |
| `MarketDataClientFactoryTests.cs` | Provider factory creation |
