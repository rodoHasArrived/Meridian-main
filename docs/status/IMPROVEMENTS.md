# Meridian - Improvement Tracking

**Last Updated:** 2026-04-28
**Status:** Active tracking document

This document consolidates **functional improvements** (features, reliability, UX) and **structural improvements** (architecture, modularity, code quality) into an item-level tracking view. For the active wave-structured delivery roadmap and release gates, see [`ROADMAP.md`](ROADMAP.md) and [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md).

Legacy `ROADMAP:` labels below retain their original milestone wording for traceability. Use [`PROGRAM_STATE.md`](PROGRAM_STATE.md) as the canonical source for wave status labels and target dates.

---

## Canonical Program State

Program wave status is canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md). Any wave status wording in this file is explanatory context only.

<!-- program-state:begin -->
| Wave | Owner | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Done | 2026-04-17 | [`production-status.md#provider-evidence-summary`](production-status.md#provider-evidence-summary) |
| W2 | Trading Workstation | In Progress | 2026-05-29 | [`ROADMAP.md#wave-2-workstation-paper-trading-cockpit-completion`](ROADMAP.md#wave-2-workstation-paper-trading-cockpit-completion) |
| W3 | Shared Platform Interop | In Progress | 2026-06-26 | [`ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity`](ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity) |
| W4 | Governance + Fund Ops | In Progress | 2026-07-24 | [`ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline`](ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline) |
| W5 | Research Platform | Planned | 2026-08-21 | [`ROADMAP.md#wave-5-backtest-studio-unification`](ROADMAP.md#wave-5-backtest-studio-unification) |
| W6 | Execution + Brokerage Integrations | Planned | 2026-09-18 | [`ROADMAP.md#wave-6-live-integration-readiness`](ROADMAP.md#wave-6-live-integration-readiness) |
<!-- program-state:end -->

---

## Table of Contents

- [Overview](#overview)
- [Theme A: Reliability & Resilience](#theme-a-reliability--resilience)
- [Theme B: Testing & Quality](#theme-b-testing--quality)
- [Theme C: Architecture & Modularity](#theme-c-architecture--modularity)
- [Theme D: API & Integration](#theme-d-api--integration)
- [Theme E: Performance & Scalability](#theme-e-performance--scalability)
- [Theme F: User Experience](#theme-f-user-experience)
- [Theme G: Operations & Monitoring](#theme-g-operations--monitoring)
- [Priority Matrix](#priority-matrix)
- [Execution Strategy](#execution-strategy)
- [Delivery Operating Model](#delivery-operating-model)
- [Dependency Map](#dependency-map)
- [Definition of Done Checklist](#definition-of-done-checklist)
- [Review Cadence & Reporting](#review-cadence--reporting)
- [Theme K: Trading Workstation Migration](#theme-k-trading-workstation-migration)

---

## Overview

### Progress Summary

| Status | Count | Items |
| -------- | ------- | ------- |
| ✅ **Completed** | 35 | A1, A2, A3, A4, A5, A6, A7, B1, B2, B3, B4, B5, C1, C2, C3, C4, C5, C6, C7, D1, D2, D3, D4, D5, D6, D7, E1, E2, E3, F1, F2, F3, G1, G2, G3 |
| 🔄 **Partially Complete** | 0 | None |
| 📝 **Open** | 0 | None |
| **Total** | 35 | All improvement items (core) |

### By Theme

| Theme | Completed | Partial | Open | Total |
| ------- | ----------- | --------- | ------ | ------- |
| A: Reliability & Resilience | 7 | 0 | 0 | 7 |
| B: Testing & Quality | 5 | 0 | 0 | 5 |
| C: Architecture & Modularity | 7 | 0 | 0 | 7 |
| D: API & Integration | 7 | 0 | 0 | 7 |
| E: Performance & Scalability | 3 | 0 | 0 | 3 |
| F: User Experience | 3 | 0 | 0 | 3 |
| G: Operations & Monitoring | 3 | 0 | 0 | 3 |
| J: Data Canonicalization | 8 | 0 | 0 | 8 |

### Portfolio Health Snapshot

- **Completion ratio:** 100% complete (35/35), 0% partial (0/35), 0% open (0/35).
- **Core improvement themes A-G are closed** for the current platform baseline.
- **Theme J canonicalization is closed** through J8, including drift reporting and fixture-maintenance workflow support.
- **Kernel migration parity program initiated:** blueprint and status tracking are now defined for fixture-driven C# ↔ F# boundary parity (`score`/`severity`/`reason`) with expected-divergence controls and CI gating for kernel-related PRs. Track rollout in [`docs/plans/kernel-parity-migration-blueprint.md`](../plans/kernel-parity-migration-blueprint.md) and live coverage in [`KERNEL_PARITY_STATUS.md`](KERNEL_PARITY_STATUS.md).
- **Theme K workstation delivery active:** K0 (WPF Desktop Shell Modernization) and K2A (Security Master Productization Baseline) are complete. K1, K2, and K3 remain active, and the shell-first workstation baseline is now validated in code through metadata-driven navigation, workspace-shell pages, shared deep-page shell hosting, Trading/Research/Data Operations desk briefing heroes, Trading Hours session briefing, OrderBook order-flow posture, Welcome readiness progress, Storage archive posture plus preview scope/guidance, compact hosted-page command chrome, actionable shell-context attention detail, Watchlist pinned-first staging, Fund Accounts balance-evidence briefing, route-aware operator queue attention with actionable run review-packet items, DI fixes, registered-page sweep coverage, and hardened scheduled/manual WPF screenshot evidence capture.
- **Recommended focus:** keep the closed Wave 1 trust gate synchronized around Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet proof; harden the existing paper-trading cockpit and shared operator-inbox handoffs (Wave 2); deepen workflow-native inspectors and page-body harmonization on top of the delivered shell host; and continue governance/fund-operations productization on top of the delivered Security Master and Fund Accounts baselines (K2).

### Backlog Inputs

The active improvement tracker now absorbs the current-value outputs from the brainstorm/proposal set instead of treating every evaluation as an equal live backlog source.

- Active backlog inputs:
  - `docs/evaluations/high-value-low-cost-improvements-brainstorm.md`
  - `docs/evaluations/nautilus-inspired-restructuring-proposal.md`
  - `docs/evaluations/2026-03-brainstorm-next-frontier.md`
- Historical backlog input:
  - `archive/docs/assessments/high-impact-improvement-brainstorm-2026-03.md`
  - `archive/docs/assessments/high-impact-improvements-brainstorm.md`

Use this document and `FULL_IMPLEMENTATION_TODO_2026_03_20.md` as the active normalized backlog, and treat brainstorm documents as supporting analysis unless they are explicitly promoted here.

### Next Sprint Backlog (Recommended)

| Sprint | Primary Goals | Exit Criteria | Status |
| -------- | --------------- | --------------- | -------- |
| 1 | C4, C5 | `EventPipeline` no longer depends on static metrics; config validation pipeline in place | ✅ Done |
| 2 | D4, B1 remainder | `/api/quality/drops` and `/api/quality/drops/{symbol}` are live and documented | ✅ Done |
| 3 | C6, A7 | Multi-sink fan-out merged; error handling convention documented and enforced in startup path | ✅ Done |
| 4 | B3 tranche 1, G2 partial, D7 partial | Provider tests for Polygon + StockSharp; OTel pipeline metrics; typed OpenAPI annotations | ✅ Done |
| 5 | B2 tranche 1, D7 remainder | Negative-path + schema validation tests; typed annotations across all endpoint families | ✅ Done |
| 6 | C1/C2, H1, H4, I1 | Provider registration unified under DI; per-provider backfill rate limiting; degradation scoring; test harness | ✅ Done |
| 7 | C3 remainder, B3 tranche 2 | NYSE shared-lifecycle bridge lands; IB + Alpaca provider tests expand | ✅ Done |
| 8 | G2 remainder, J8 canary | Full trace propagation and canonicalization drift detection/reporting land | ✅ Done |
| 9 | K0, route/health reliability | WPF Fluent theme, SVG icons, LiveCharts2 charting, Synthetic provider default, workflow guide, CI screenshots, duplicate route/registration fixes | ✅ Done |
| 10 | K1 shell consolidation, page-level redesign, Wave 1 provider confidence | `ShellNavigationCatalog`, workspace shell pages, and current WPF shell consolidation plus high-traffic page redesign (Live Data, Provider, Backfill, Data Quality); active Wave 1 gate closed around Alpaca/Robinhood/Yahoo plus checkpoint and Parquet evidence closure | ✅ Done |
| 11 | Wave 2 cockpit hardening, governance/reporting follow-ons | Existing web paper-trading cockpit hardened across positions, orders, fills, P&L, risk, replay, sessions, and promotion; governance cash-flow/report-pack follow-ons on top of delivered Security Master productization | 🔄 Active |

---

## Theme A: Reliability & Resilience

### A1. ✅ WebSocket Automatic Resubscription (COMPLETED)

**Impact:** Critical | **Effort:** Low | **Priority:** P0 | **Status:** ✅ DONE

**Problem:** WebSocket providers lost all subscriptions on reconnect, requiring manual intervention.

**Solution Implemented:**

- `SubscriptionManager` tracks subscriptions by kind with `GetSymbolsByKind()` for recovery
- `AlpacaMarketDataClient.OnConnectionLostAsync()` passes `onReconnected` callback for re-auth + resubscribe
- `PolygonMarketDataClient.ResubscribeAllAsync()` replays trades, quotes, aggregates after reconnect
- All providers log successful resubscription events

**Files:**

- `Infrastructure/Resilience/WebSocketConnectionManager.cs`
- `Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs`
- `Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`
- `Infrastructure/Adapters/Shared/SubscriptionManager.cs`

**ROADMAP:** Phase 0 (Critical Fixes)

---

### A2. ✅ Storage Sink Disposal Race Condition Fix (COMPLETED)

**Impact:** High | **Effort:** Low | **Priority:** P0 | **Status:** ✅ DONE

**Problem:** `JsonlStorageSink` and `ParquetStorageSink` could lose data during shutdown if flush timer fired during disposal.

**Solution Implemented:**

- Cancel disposal token first to stop new writes
- Dispose flush timer (waiting for pending callbacks)
- Execute guaranteed final flush under a non-reentrant semaphore-gated core flush path
- Then dispose writers and remaining resources

**Files:**

- `Storage/Sinks/JsonlStorageSink.cs`
- `Storage/Sinks/ParquetStorageSink.cs`

**Remaining Work (Low Priority):** Extract shared buffering/flushing logic into `BufferedSinkBase` to prevent future divergence.

**ROADMAP:** Phase 0 (Critical Fixes)

---

### A3. ✅ Backfill Rate Limit Exponential Backoff (COMPLETED)

**Impact:** High | **Effort:** Low | **Priority:** P0 | **Status:** ✅ DONE

**Problem:** Backfill workers retry rate-limited requests without proper backoff, wasting time and API quota.

**Solution Implemented:**

- Exponential backoff (2s base, 60s cap) with jitter in `BackfillWorkerService`
- Retry budget enforced at 3 attempts per request
- `RateLimitException` includes `RetryAfter` property for provider-specified cooldown periods
- `Retry-After` response header parsing implemented in `ProviderHttpUtilities` and `SharedResiliencePolicies`
- `ProviderRateLimitTracker` tracks per-provider rate limit state with sliding window `RateLimiter`
- Providers honor `Retry-After` values from HTTP 429 responses

**Files:**

- `Infrastructure/Adapters/Queue/BackfillWorkerService.cs`
- `Core/Exceptions/RateLimitException.cs`
- `ProviderSdk/ProviderHttpUtilities.cs`
- `Infrastructure/Http/SharedResiliencePolicies.cs`
- `Infrastructure/Adapters/Core/ProviderRateLimitTracker.cs`

**ROADMAP:** Phase 1 (Core Stability)

---

### A4. ✅ Subscription Memory Leak Fix (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P0 | **Status:** ✅ DONE

**Problem:** `SubscriptionManager` never removed entries from internal dictionaries, causing memory leak over time.

**Solution Implemented:**

- `Unsubscribe()` and `UnsubscribeSymbol()` properly remove entries
- Added `Count` property for monitoring active subscriptions
- All lifecycle events logged at Debug level

**Files:**

- `Infrastructure/Adapters/Shared/SubscriptionManager.cs`

**ROADMAP:** Phase 0 (Critical Fixes)

---

### A5. ✅ Provider Factory with Runtime Switching (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** Provider selection was hardcoded in `Program.cs` with switch statement, no runtime switching.

**Solution Implemented:**

- `IMarketDataClientFactory` and `MarketDataClientFactory` replace switch statement
- Supports IB, Alpaca, Polygon, StockSharp, NYSE providers
- Runtime provider switching via `/api/config/data-source` POST endpoint
- Failover chain creates client instances dynamically from factory

**Files:**

- `Infrastructure/Adapters/MarketDataClientFactory.cs`
- `Program.cs`
- `Ui.Shared/Endpoints/ConfigEndpoints.cs`

**ROADMAP:** Phase 2 (Architecture)

---

### A6. ✅ Write-Ahead Log Recovery Hardening (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** WAL recovery could fail or hang on large uncommitted files.

**Solution Implemented:**

- `GetUncommittedRecordsAsync()` uses `IAsyncEnumerable<WalRecord>` with streaming reads
- Processes records in batches of 10,000
- Configurable `UncommittedSizeWarningThreshold` (default 50MB) logs warnings
- Full SHA256 checksums replace previous truncated 8-byte variant

**Files:**

- `Storage/Archival/WriteAheadLog.cs`

**ROADMAP:** Phase 5 (Operational Readiness)

---

### A7. ✅ Standardize Error Handling Strategy (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** Codebase uses three concurrent error handling approaches inconsistently:

1. **Exceptions** - 9 custom exception types in `Core/Exceptions/`
2. **Result<T, TError>** - functional result type in `Application/Results/Result.cs`
3. **Hard-coded `return 1`** - all error paths in `Program.cs` returned exit code 1 regardless of error category

**Solution Implemented:**

- Added `ErrorCodeExtensions.FromException(Exception)` method that maps domain exceptions and standard .NET exceptions to the correct `ErrorCode` enum value
- Replaced all hard-coded `return 1` in `Program.cs` with category-accurate exit codes via `ErrorCode.ToExitCode()`:
  - Configuration errors → exit code 3 (via `ErrorCode.ConfigurationInvalid`)
  - File permission errors → exit code 7 (via `ErrorCode.FileAccessDenied`)
  - Schema validation errors → exit code 6 (via `ErrorCode.SchemaMismatch`)
  - Backfill failures → exit code 5 (via `ErrorCode.ProviderError`)
  - Connection failures → exit code 4 (via `ErrorCode.ConnectionFailed`)
  - Fatal catch-all → dynamically mapped from exception type
- Connection failure handler now returns error code instead of re-throwing
- All error log messages include `ErrorCode` and `ExitCode` for diagnostics
- **NEW**: 22 comprehensive tests covering exception-to-ErrorCode mapping, exit code ranges, category names, and transient error identification

**Files:**

- `Application/Results/ErrorCode.cs` (`FromException` method added)
- `Program.cs` (6 exit code locations updated)
- `tests/.../Application/Services/ErrorCodeMappingTests.cs` (22 tests)

**Benefit:** Process exit codes now reflect the actual error category, enabling operators and CI/CD to distinguish configuration errors (3), connection failures (4), provider errors (5), schema issues (6), and storage problems (7) from generic failures.

**ROADMAP:** Phase 2 (Architecture)

---

## Theme B: Testing & Quality

### B1. ✅ Dropped Event Audit Trail (COMPLETED)

**Impact:** Medium-High | **Effort:** Low | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** Events dropped due to backpressure were not tracked, making data quality assessment impossible.

**Solution Implemented:**

- `DroppedEventAuditTrail` logs dropped events to `_audit/dropped_events.jsonl`
- JSONL format with timestamp, event type, symbol, sequence, source, drop reason
- Integrated with `EventPipeline`
- Tracks drop counts per symbol via `ConcurrentDictionary`
- Dropped-event and dead-letter audit JSONL writes now append through `AtomicFileWriter.AppendLinesAsync`, so crash recovery never leaves partial audit records behind
- **NEW**: `/api/quality/drops` HTTP endpoint exposing `DroppedEventStatistics`
- **NEW**: `/api/quality/drops/{symbol}` for per-symbol drill-down
- **NEW**: 10 comprehensive integration tests covering all scenarios

**Files:**

- `Application/Pipeline/DroppedEventAuditTrail.cs`
- `Application/Pipeline/EventPipeline.cs`
- `Ui.Shared/Endpoints/QualityDropsEndpoints.cs` (implemented)
- `tests/.../EndpointTests/QualityDropsEndpointTests.cs` (10 tests)

**ROADMAP:** Phase 3 (API Completeness)

---

### B2. ✅ HTTP Endpoint Integration Tests (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** The HTTP API layer (136 implemented endpoints) had no integration tests using `WebApplicationFactory<T>`. Only `EndpointStubDetectionTests.cs` validated route format.

**Solution Implemented:**

- `EndpointTestFixture` base class with shared `WebApplicationFactory<T>` setup
- 16 endpoint test files covering core API surface
- Tests assert status codes, content types, response schema shapes
- Negative cases (invalid input, missing config, auth failures) included
- Coverage spans status, health, config, backfill, providers, quality, SLA, maintenance, packaging, and more
- **Sprint 5 additions:**
  - `NegativePathEndpointTests.cs` — 40+ tests for negative-path and edge-case behavior across all endpoint families (404s, invalid POST bodies, path traversal rejection, reversed date ranges, symbol count limits, non-existent providers, method-not-allowed)
  - `ResponseSchemaValidationTests.cs` — 15+ tests validating JSON response schemas for core endpoints (field presence, types, structural contracts for /api/status, /api/health, /api/health/summary, /api/config, /api/config/data-sources, /api/providers/comparison, /api/backpressure)

**Files:**

- `tests/Meridian.Tests/Integration/EndpointTests/EndpointTestFixture.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/StatusEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/HealthEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/ConfigEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/BackfillEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/ProviderEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/QualityEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/QualityDropsEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/SlaEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/MaintenanceEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/PackagingEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/FailoverEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/SymbolEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/SubscriptionEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/LiveDataEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/DiagnosticsEndpointTests.cs`
- `tests/Meridian.Tests/Integration/EndpointTests/NegativePathEndpointTests.cs` (new, Sprint 5)
- `tests/Meridian.Tests/Integration/EndpointTests/ResponseSchemaValidationTests.cs` (new, Sprint 5)

**ROADMAP:** Phase 1 (Core Stability) - Item 1A

---

### B3. ✅ Infrastructure Provider Unit Tests (COMPLETED)

**Impact:** High | **Effort:** High | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** 55 provider implementation files but only 8 test files. Major streaming providers had no dedicated unit tests.

**Solution Implemented:**

- 12 provider test files now covering all core providers:
  - Polygon: subscription, message parsing, main client logic
  - StockSharp: subscription, message conversion
  - Alpaca: credential validation, reconnect behavior, quote routing
  - IB: simulation client tests (15 tests)
  - NYSE: message parsing
  - Failover: client switching, health check, service coordination
  - Backfill: retry-after header handling

**Files:**

- `tests/.../Infrastructure/Adapters/AlpacaCredentialAndReconnectTests.cs`
- `tests/.../Infrastructure/Adapters/AlpacaQuoteRoutingTests.cs`
- `tests/.../Infrastructure/Adapters/BackfillRetryAfterTests.cs`
- `tests/.../Infrastructure/Adapters/FailoverAwareMarketDataClientTests.cs`
- `tests/.../Infrastructure/Adapters/IBSimulationClientTests.cs`
- `tests/.../Infrastructure/Adapters/NYSEMessageParsingTests.cs`
- `tests/.../Infrastructure/Adapters/PolygonMarketDataClientTests.cs`
- `tests/.../Infrastructure/Adapters/PolygonMessageParsingTests.cs`
- `tests/.../Infrastructure/Adapters/PolygonSubscriptionTests.cs`
- `tests/.../Infrastructure/Adapters/StockSharpMessageConversionTests.cs`
- `tests/.../Infrastructure/Adapters/StockSharpSubscriptionTests.cs`
- `tests/.../Infrastructure/Adapters/StreamingFailoverServiceTests.cs`

**ROADMAP:** Sprint 4 (tranche 1), Sprint 7 (tranche 2) — both complete

---

### B4. ✅ Application Service Tests (COMPLETED)

**Impact:** Medium-High | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** 19 application services had zero test coverage, including critical ones like `TradingCalendar` and data quality services.

**Solution Implemented:**

- 13 core application service test files covering critical paths:
  - `TradingCalendar` — 50+ tests (market hours, holidays, half-days, pre/post sessions)
  - Data quality services — 62 tests across 4 services (GapAnalyzer, AnomalyDetector, CompletenessScoreCalculator, SequenceErrorTracker)
  - `ConfigurationService` — config loading, validation, self-healing
  - `CliModeResolver` — CLI mode detection
  - `CronExpressionParser` — cron expression parsing
  - `ErrorCodeMapping` — error code resolution
  - `GracefulShutdown` — shutdown coordination
  - `OperationalScheduler` — backfill scheduling
  - `OptionsChainService` — options data handling
  - `PreflightChecker` — startup validation
- Additional 293 UI service tests and 142 WPF desktop service tests

**Files:**

- `tests/.../Application/Services/TradingCalendarTests.cs`
- `tests/.../Application/Services/DataQuality/GapAnalyzerTests.cs`
- `tests/.../Application/Services/DataQuality/AnomalyDetectorTests.cs`
- `tests/.../Application/Services/DataQuality/CompletenessScoreCalculatorTests.cs`
- `tests/.../Application/Services/DataQuality/SequenceErrorTrackerTests.cs`
- `tests/.../Application/Services/ConfigurationServiceTests.cs`
- `tests/.../Application/Services/CliModeResolverTests.cs`
- `tests/.../Application/Services/ErrorCodeMappingTests.cs`
- `tests/.../Application/Services/GracefulShutdownTests.cs`
- `tests/.../Application/Services/OperationalSchedulerTests.cs`
- `tests/.../Application/Services/OptionsChainServiceTests.cs`
- `tests/.../Application/Services/PreflightCheckerTests.cs`
- `tests/.../Application/Services/CronExpressionParserTests.cs`

**ROADMAP:** Phase 1 (Core Stability) - Item 1C

---

### B5. ✅ Provider SDK Tests (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** Provider SDK classes (`DataSourceRegistry`, `CredentialValidator`) used by all providers had minimal test coverage.

**Solution Implemented:**

- `DataSourceRegistryTests` (14 tests): assembly discovery, deduplication, metadata validation, service registration
- `CredentialValidatorTests` (16 tests): API key validation, key-secret pairs, throw helpers, env var retrieval
- `ExceptionTypeTests` (24 tests): all 8 custom exception types tested for properties, hierarchy, sealed checks
- `DataSourceAttributeTests` (14 tests): attribute construction, metadata mapping, IsRealtime/IsHistorical properties

**Files:**

- `tests/Meridian.Tests/ProviderSdk/DataSourceRegistryTests.cs` (14 tests)
- `tests/Meridian.Tests/ProviderSdk/CredentialValidatorTests.cs` (16 tests)
- `tests/Meridian.Tests/ProviderSdk/ExceptionTypeTests.cs` (24 tests)
- `tests/Meridian.Tests/ProviderSdk/DataSourceAttributeTests.cs` (14 tests)

**ROADMAP:** Phase 1 (Core Stability) - Item 1D

---

## Theme C: Architecture & Modularity

### C1. ✅ Unified Provider Registry (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** Three separate provider creation mechanisms existed and competed:

1. `MarketDataClientFactory` - switch-based factory
2. `ProviderFactory` - parallel factory system
3. Direct instantiation in `Program.cs`

**Solution Implemented:**

- `ProviderFactory` is the single creation mechanism for backfill and symbol search providers
- `ProviderRegistry` is the unified entry point used by `Program.cs` via DI (`hostStartup.GetRequiredService<ProviderRegistry>()`)
- `[DataSource]` attribute scanning wired into startup — all 5 streaming providers use `[DataSource]` attributes
- `ServiceCompositionRoot` orchestrates all registrations centrally (lines 69-133)
- `Program.cs` resolves providers exclusively through DI; no direct `new` instantiation of providers

**Files:**

- `Infrastructure/Adapters/Core/ProviderFactory.cs` (unified creation)
- `Infrastructure/Adapters/Core/ProviderRegistry.cs` (single entry point)
- `Application/Composition/ServiceCompositionRoot.cs` (centralized DI registration)
- `Program.cs` (DI-only resolution)
- `ProviderSdk/DataSourceAttribute.cs`
- `ProviderSdk/DataSourceRegistry.cs`

**Benefit:** Adding new provider becomes: implement interface, add attribute, done.

**ROADMAP:** Phase 2 (Architecture) - Item 2A

---

### C2. ✅ Single DI Composition Path (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** `ServiceCompositionRoot.cs` registered services in DI, but `Program.cs` bypassed DI for critical components — collectors created via `new`, storage pipeline created via `new`, configuration loaded twice.

**Solution Implemented:**

- `Program.cs` now delegates to the shared startup layer in `Application/Composition/Startup/`, keeping the entry point thin while reusing the same startup helpers across hosts.
- All collectors resolved from DI: `hostStartup.GetRequiredService<QuoteCollector>()`, `GetRequiredService<TradeDataCollector>()`, `GetRequiredService<MarketDepthCollector>()`
- Storage pipeline resolved from DI via `hostStartup.Pipeline`
- `HostStartupFactory` maps deployment modes onto canonical `CompositionOptions` presets, and `ServiceCompositionRoot` registers everything centrally:
  - `AddCoreConfigurationServices()` — ConfigStore, ConfigurationService
  - `AddStorageServices()` — Storage sinks, file services
  - `AddProviderServices()` — ProviderRegistry, ProviderFactory
  - `AddBackfillServices()` — Backfill coordinator, scheduling
  - `AddPipelineServices()` — EventPipeline, Publisher
  - `AddCollectorServices()` — All collectors
- No orphaned registrations — all registered services are actively consumed

**Files:**

- `Program.cs` (DI-only resolution throughout)
- `Application/Composition/Startup/SharedStartupBootstrapper.cs` (shared startup helpers and mode orchestration)
- `Application/Composition/ServiceCompositionRoot.cs` (centralized registration)
- `Application/Composition/HostStartup.cs` (composition host)

**Benefit:** One composition path. DI registrations are source of truth. Testable via service replacement.

**ROADMAP:** Phase 2 (Architecture) - Item 2B

---

### C3. 🔄 WebSocket Provider Lifecycle Consolidation (PARTIALLY COMPLETE)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** 🔄 PARTIAL

**Problem:** WebSocket lifecycle management was historically duplicated across streaming providers, increasing maintenance cost around reconnect, heartbeat, and subscription recovery behavior.

**Current State:**

- ✅ `PolygonMarketDataClient` now extends `WebSocketProviderBase`, removing large amounts of bespoke connection-management code.
- 📝 `NYSEDataSource` still carries its own lifecycle path and remains the primary remaining C3 follow-up.
- ℹ️ `StockSharpMarketDataClient` is now treated separately: it wraps a third-party connector rather than a raw WebSocket, so direct `WebSocketProviderBase` adoption is no longer the right target.

**Remaining Work:**

- Refactor NYSE streaming lifecycle onto the shared provider base or an equivalent shared abstraction after interface alignment.
- Keep Polygon as the reference implementation/template for future raw WebSocket providers.
- Reassess whether StockSharp needs a connector-oriented base abstraction instead of participating in C3 directly.

**Files:**

- `src/Meridian.Infrastructure/Adapters/Core/WebSocketProviderBase.cs`
- `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs`
- `docs/development/refactor-map.md`

**ROADMAP:** Phase 2 (Architecture)

---

### C4. ✅ Injectable Metrics Interface (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** `EventPipeline` called `Metrics.IncPublished()` and `Metrics.IncDropped()` via static methods. This prevented substitution in tests and coupled pipeline to specific metrics backend.

**Solution Implemented:**

- Extracted `IEventMetrics` interface: `IncPublished()`, `IncDropped()`, `IncConsumed()`, `IncRecovered()`, etc.
- Injected `IEventMetrics` into `EventPipeline` via constructor parameter
- `DefaultEventMetrics` implementation delegates to existing `Metrics`/`PrometheusMetrics` static classes
- ServiceCompositionRoot registers `IEventMetrics` as singleton
- BackfillCoordinator now accepts and passes IEventMetrics to EventPipeline
- **NEW**: 7 comprehensive tests for injectable metrics behavior

**Files:**

- `Application/Monitoring/IEventMetrics.cs` (interface + DefaultEventMetrics)
- `Application/Pipeline/EventPipeline.cs:98` (accepts metrics parameter)
- `Application/Http/BackfillCoordinator.cs:42, 155` (injection)
- `Application/Composition/ServiceCompositionRoot.cs` (DI registration)
- `tests/.../Pipeline/EventPipelineMetricsTests.cs` (7 tests)

**Benefit:** Pipeline testable without side effects. Opens door to alternative metrics backends.

**ROADMAP:** Phase 2 (Architecture) - Item 2D

---

### C5. ✅ Consolidated Configuration Validation (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** Configuration validation was spread across three classes with overlapping responsibilities:

1. `ConfigValidationHelper` - field-level validation
2. `ConfigValidatorCli` - CLI-oriented validation with output formatting
3. `PreflightChecker` - pre-startup validation including connectivity

No clear contract for what each validates or when it runs.

**Solution Implemented:**

- Defined `IConfigValidator` with `Validate(AppConfig) -> ConfigValidationResult[]`
- Implemented `ConfigValidationPipeline` with composable stages: FieldValidationStage → SemanticValidationStage
- ConfigurationPipeline migrated to use ConfigValidationPipeline
- ConfigurationService.ValidateConfig migrated to use ConfigValidationPipeline
- ConfigValidationHelper methods marked obsolete with migration guidance
- **NEW**: 11 comprehensive tests for validation pipeline including error cases, warnings, and edge conditions

**Files:**

- `Application/Config/IConfigValidator.cs` (interface + pipeline + stages)
- `Application/Config/ConfigurationPipeline.cs:224-231` (migrated)
- `Application/Services/ConfigurationService.cs:327-346` (migrated)
- `Application/Config/ConfigValidationHelper.cs` (marked obsolete)
- `tests/.../Config/ConfigValidationPipelineTests.cs` (11 tests)

**Benefit:** Single validation pipeline. Clear ordering. Easy to add new rules. Better testability.

**ROADMAP:** Phase 2 (Architecture) - Item 2E

---

### C6. ✅ Composite Storage Sink Plugin Architecture (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** `EventPipeline` accepts single `IStorageSink`. Multi-sink scenarios (JSONL + Parquet simultaneously, or JSONL + analytics sink) require external composition. No built-in fan-out.

**Solution Implemented:**

- `CompositeSink : IStorageSink` wraps `IReadOnlyList<IStorageSink>` with per-sink fault isolation
- `AppendAsync` fans out to all sinks; individual sink failures are logged but don't block other sinks
- `FlushAsync` collects exceptions and throws `AggregateException` for visibility
- `DisposeAsync` gracefully disposes all sinks, logging per-sink failures
- `ServiceCompositionRoot` conditionally creates `CompositeSink` when `EnableParquetSink` is enabled
- Default mode uses single `JsonlStorageSink`; Parquet mode creates `CompositeSink` wrapping both
- **8 comprehensive tests** covering fan-out, fault isolation, flush aggregation, disposal, and constructor guards

**Files:**

- `Storage/Sinks/CompositeSink.cs` (87 lines)
- `Application/Composition/ServiceCompositionRoot.cs:636-666` (conditional composition)
- `tests/.../Storage/CompositeSinkTests.cs` (8 tests)

**Benefit:** Multi-format storage without pipeline changes. New sinks (CSV, database, cloud) can be added independently. Per-sink fault isolation prevents one failing sink from blocking others.

**ROADMAP:** Phase 2 (Architecture) - Item 2F

---

### C7. ✅ WPF/UWP Service Deduplication (COMPLETED)

**Impact:** High | **Effort:** High | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** 25-30 services were nearly identical between WPF and UWP desktop projects.

**Solution Implemented:**

- UWP project (`src/Meridian.Uwp/`) fully removed from the codebase
- WPF is the sole desktop client; no duplicate services remain
- Shared service interfaces and base classes consolidated in `Meridian.Ui.Services`
- Platform-specific adapters exist only in `Meridian.Wpf/Services/`

**Files:**

- `Meridian.Wpf/Services/*.cs` (51 service files)
- `Meridian.Ui.Services/` (shared base classes and interfaces)

**Benefit:** UWP removal eliminated all service duplication. Single desktop platform simplifies maintenance.

**ROADMAP:** Phase 6 (Cleanup) - Phase 6C

---

## Theme D: API & Integration

### D1. ✅ API Route Implementation Gap Closure (COMPLETED - Phase 1)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE (Phase 1)

**Problem:** Many declared routes returned no response or generic error, breaking web dashboard.

**Solution Implemented:**

- All unimplemented routes return `501 Not Implemented` with structured JSON
- `StubEndpoints.cs` registers 180 stub routes with clear messaging
- Core endpoints fully functional: status, config, backfill, failover, providers

**Remaining Work (Phase 2-3):** Implement handler logic for highest-value stub groups.

**Files:**

- `Ui.Shared/Endpoints/StubEndpoints.cs`
- `Contracts/Api/UiApiRoutes.cs`

**ROADMAP:** Phase 3 (API Completeness)

---

### D2. ✅ Real-Time Dashboard Updates via SSE (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** Dashboard required manual refresh to see updated metrics.

**Solution Implemented:**

- SSE endpoint at `/api/events/stream` pushes status every 2 seconds
- Includes event throughput, active subscriptions, provider health, backpressure, recent errors
- JavaScript `EventSource` client with automatic fallback to polling
- Reconnects after 10 seconds on connection drop

**Files:**

- `Ui.Shared/Endpoints/StatusEndpoints.cs`
- `Ui.Shared/HtmlTemplates.cs`

**ROADMAP:** Phase 3 (API Completeness)

---

### D3. ✅ Backfill Progress Reporting (COMPLETED)

**Impact:** Medium-High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** No visibility into backfill progress - users couldn't tell if job was stuck or progressing.

**Solution Implemented:**

- `BackfillProgressTracker` tracks per-symbol progress with date ranges
- Calculates percentage complete per symbol and overall
- `BackfillProgressSnapshot` with detailed metrics (completed, failed, errors)
- Exposed via `/api/backfill/progress` endpoint

**Files:**

- `Infrastructure/Adapters/Core/BackfillProgressTracker.cs`
- `Infrastructure/Adapters/Queue/BackfillWorkerService.cs`
- `Ui.Shared/Endpoints/BackfillEndpoints.cs`

**ROADMAP:** Phase 3 (API Completeness)

---

### D4. ✅ Quality Metrics API Endpoints (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** `DataQualityMonitoringService` computes completeness, gap, anomaly metrics internally but quality endpoints remained stubs. `DroppedEventAuditTrail` had no HTTP exposure.

**Solution Implemented:**

- Implemented `GET /api/quality/drops` returning `DroppedEventStatistics`
- Implemented `GET /api/quality/drops/{symbol}` for per-symbol drill-down
- Endpoints handle case normalization (symbols converted to uppercase)
- Graceful handling when audit trail is not configured
- Wired into UiServer and UiEndpoints
- **NEW**: Expanded from 2 baseline tests to 10 comprehensive integration tests

**Files:**

- `Ui.Shared/Endpoints/QualityDropsEndpoints.cs` (fully implemented)
- `Application/Pipeline/DroppedEventAuditTrail.cs` (provides statistics)
- `tests/.../EndpointTests/QualityDropsEndpointTests.cs` (10 tests)

**Benefit:** Completes observability story. Dashboards can display data quality in real time. Endpoints tested for edge cases (case handling, special characters, empty symbols, missing audit trail).

**ROADMAP:** Phase 3 (API Completeness)

---

### D5. ✅ OpenAPI/Swagger Documentation (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** No API documentation for external integrations or third-party developers.

**Solution Implemented:**

- `Swashbuckle.AspNetCore` and `Microsoft.AspNetCore.OpenApi` integrated
- Swagger UI served at `/swagger` in development mode
- OpenAPI spec at `/swagger/v1/swagger.json`
- `ApiDocumentationService` provides additional documentation generation

**Remaining Work:** Add `[ProducesResponseType]` annotations for complete schema documentation.

**Files:**

- `Ui.Shared/Endpoints/UiEndpoints.cs`
- `Meridian.Ui.Shared.csproj`

**ROADMAP:** Phase 3 (API Completeness)

---

### D6. ✅ API Authentication and Rate Limiting (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** HTTP endpoints had no authentication, allowing unrestricted access.

**Solution Implemented:**

- `ApiKeyMiddleware` enforces API key via `X-Api-Key` header or `api_key` query param
- Reads from `MDC_API_KEY` environment variable
- Constant-time comparison prevents timing attacks
- `ApiKeyRateLimitMiddleware` enforces 120 req/min per key with sliding window
- Returns `429 Too Many Requests` with `Retry-After` header
- Health endpoints (`/healthz`, `/readyz`, `/livez`) exempt
- If `MDC_API_KEY` not set, all requests allowed (backward compatible)

**Files:**

- `Ui.Shared/Endpoints/ApiKeyMiddleware.cs`
- `Ui.Shared/Endpoints/UiEndpoints.cs`

**ROADMAP:** Phase 5 (Operational Readiness)

---

### D7. ✅ OpenAPI Response Type Annotations (COMPLETED)

**Impact:** Low-Medium | **Effort:** Medium | **Priority:** P3 | **Status:** ✅ DONE

**Problem:** Swagger infrastructure exists but generated OpenAPI spec lacks response type documentation. Shows generic `200 OK` for all endpoints with no schema information.

**Solution Implemented:**

- Added typed `Produces<T>()` annotations to core health and status endpoints (`StatusEndpoints.cs`, `HealthEndpoints.cs`)
- Added `WithDescription()` metadata for endpoint documentation
- Created typed `HealthSummaryResponse` and `HealthSummaryProviders` models in `StatusModels.cs`
- Typed annotations for `HealthCheckResponse`, `StatusResponse` on corresponding endpoints
- **Sprint 5**: Extended typed `Produces<T>()` and `.WithDescription()` annotations across all remaining endpoint families:
  - `BackfillEndpoints.cs` — 5 endpoints annotated with `Produces<BackfillProviderInfo[]>`, `Produces<BackfillResult>`
  - `BackfillScheduleEndpoints.cs` — 15 endpoints annotated with descriptions and typed produces
  - `ConfigEndpoints.cs` — 8 endpoints annotated with descriptions
  - `ProviderEndpoints.cs` — 12 endpoints annotated with `Produces<ProviderComparisonResponse>`, `Produces<ProviderStatusResponse[]>`, `Produces<ProviderMetricsResponse[]>`, `Produces<ProviderCatalogEntry>`
  - `ProviderExtendedEndpoints.cs` — 11 endpoints annotated with descriptions and typed produces
  - `HealthEndpoints.cs` — 7 remaining endpoints annotated with descriptions
  - `StatusEndpoints.cs` — remaining endpoints annotated with `Produces<ErrorsResponseDto>`, `Produces<BackpressureStatusDto>`, `Produces<ProviderLatencySummaryDto>`, `Produces<ConnectionHealthSnapshotDto>`

**Files:**

- `Ui.Shared/Endpoints/StatusEndpoints.cs`
- `Ui.Shared/Endpoints/HealthEndpoints.cs`
- `Ui.Shared/Endpoints/BackfillEndpoints.cs`
- `Ui.Shared/Endpoints/BackfillScheduleEndpoints.cs`
- `Ui.Shared/Endpoints/ConfigEndpoints.cs`
- `Ui.Shared/Endpoints/ProviderEndpoints.cs`
- `Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs`
- `Contracts/Api/StatusModels.cs`
- `Contracts/Api/StatusEndpointModels.cs`

**ROADMAP:** Sprint 4 (core endpoints), Sprint 5 (all endpoint families)

---

## Theme E: Performance & Scalability

### E1. ✅ CLI Argument Parser Extraction (COMPLETED)

**Impact:** Low-Medium | **Effort:** Low | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** Each command in `Application/Commands/` re-implemented argument parsing inline. Pattern like `GetArgValue(args, "--flag")` duplicated across 9 files.

**Solution Implemented:**

- `CliArgumentParser` utility class created
- Methods: `HasFlag(args, flag)`, `GetValue(args, flag)`, `GetValues(args, flag)`, `GetDateValue(args, flag)`
- All `ICliCommand` implementations refactored to use it

**Files:**

- `Application/Commands/CliArgumentParser.cs`
- `Application/Commands/*.cs` (9 files)

**Benefit:** Eliminates parsing duplication. Consistent error messages. Easier to add new commands.

**ROADMAP:** Phase 2 (Architecture)

---

### E2. ✅ UiServer Endpoint Extraction (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** `UiServer.cs` was 3,030 lines with all endpoint logic inline, making it unmaintainable.

**Solution Implemented:**

- Extracted all inline endpoint definitions to 30+ dedicated endpoint modules
- `UiServer.cs` reduced from 3,030 to 191 lines (93.7% reduction)
- Removed all legacy `Configure*Routes()` methods
- Delegates to modules in `Ui.Shared/Endpoints/`

**Files:**

- `Application/Http/UiServer.cs` (3,030 → 191 lines)
- `Ui.Shared/Endpoints/` (30+ modules)

**Benefit:** Maintainable code structure. Clear separation of concerns. Easy to add new endpoints.

**ROADMAP:** Phase 6 (Cleanup) - Phase 6A

---

### E3. ✅ Reduce GC Pressure in Hot Paths (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P3 | **Status:** ✅ DONE

**Problem:** High-frequency message parsing allocated per-message via `JsonDocument.Parse()`, `Encoding.UTF8.GetString()`, `List<T>` construction at ~100 Hz.

**Solution Implemented:**

- StockSharp `MessageConverter` uses `ObjectPool<List<OrderBookLevel>>` with pre-sized lists and try/finally return-to-pool patterns
- Polygon `PolygonMarketDataClient` uses `ArrayPool<byte>.Shared` throughout WebSocket receive loop:
  - Buffers rented before use, returned immediately after in `try/finally` blocks
  - 4KB initial buffer and 65KB large buffer both pool-managed
  - Zero per-message allocation after warmup
  - Proper exception-safe return-to-pool patterns

**Files:**

- `Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs` (ArrayPool)
- `Infrastructure/Adapters/StockSharp/StockSharpMessageConversion.cs` (ObjectPool)

**ROADMAP:** Phase 7 (Extended Capabilities)

---

## Theme F: User Experience

### F1. ✅ Desktop Navigation Consolidation (COMPLETED)

**Impact:** Medium | **Effort:** High | **Priority:** P3 | **Status:** ✅ DONE

**Problem:** WPF consolidated into 5 workspaces (~15 navigation items) with command palette (Ctrl+K). UWP had 40+ pages in flat navigation list.

**Solution Implemented:**

- WPF has a first-stage workspace model (Monitor, Collect, Storage, Quality, Settings)
- WPF command palette functional (Ctrl+K)
- UWP project removed — no remaining flat navigation to consolidate

**Follow-on migration:** The next UX phase upgrades this first-stage consolidation into the workflow-centric Trading Workstation model (`Research`, `Trading`, `Data Operations`, `Governance`). See Theme K.

**Files:**

- `Meridian.Wpf/Views/MainPage.xaml`
- `Meridian.Wpf/Services/NavigationService.cs`

**ROADMAP:** Phase 4 (Desktop App Maturity)

---

### F2. ✅ Contextual CLI Help System (COMPLETED)

**Impact:** Low-Medium | **Effort:** Low | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** `HelpCommand` displayed wall of flags. Users had to read full output to find what they need. No contextual help, no `--help backfill` sub-command support.

**Solution Implemented:**

- `--help <topic>` support for focused help across 7 topics
- Available topics: `backfill`, `symbols`, `config`, `storage`, `providers`, `packaging`, `diagnostics`
- Each topic shows description, available flags, and copy-paste examples
- Default `--help` (no topic) shows summary with topic list
- Topic content drawn from existing `docs/HELP.md` documentation

**Files:**

- `Application/Commands/HelpCommand.cs`
- `Application/Commands/CliArguments.cs`

**Benefit:** Users find relevant help faster. Reduces support burden and documentation lookups.

**ROADMAP:** Phase 4 (Desktop App Maturity)

---

### F3. ✅ First-Run Onboarding Experience (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** New users faced blank dashboard with no guidance on next steps.

**Solution Implemented:**

- `ConfigurationWizard` provides an 8-step interactive onboarding process:
  1. Provider detection (async)
  2. Use case selection
  3. Data source configuration (credentials)
  4. Symbol configuration
  5. Storage configuration (paths, compression)
  6. Backfill configuration
  7. Review and confirmation
  8. Save to file
- `AutoConfigurationService` detects providers from environment variables (Alpaca, Polygon, Tiingo, Finnhub, AlphaVantage, IB, NYSE, Yahoo) with credential resolution, recommended priorities, and structured `AutoConfigResult`
- CLI flags: `--wizard` for interactive setup, `--auto-config` for env-var auto-detection, `--detect-providers` for available providers
- Desktop `SetupWizardPage` and `OnboardingTourService` provide GUI onboarding
- `FirstRunService` (WPF) detects first-run state and triggers setup flow

**Files:**

- `Application/Services/ConfigurationWizard.cs`
- `Application/Services/AutoConfigurationService.cs`
- `Meridian.Wpf/Services/FirstRunService.cs`
- `Meridian.Ui.Services/Services/SetupWizardService.cs`
- `Meridian.Ui.Services/Services/OnboardingTourService.cs`

**ROADMAP:** Phase 4 (Desktop App Maturity)

---

## Theme G: Operations & Monitoring

### G1. ✅ Prometheus Metrics Export (COMPLETED)

**Impact:** High | **Effort:** Low | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** No standardized metrics export for production monitoring.

**Solution Implemented:**

- `PrometheusMetrics` class exposes standard Prometheus metrics
- `/api/metrics` endpoint for scraping
- Metrics for event throughput, provider health, backpressure, error rates
- Histograms for latency tracking

**Files:**

- `Application/Monitoring/PrometheusMetrics.cs`
- `Ui.Shared/Endpoints/StatusEndpoints.cs`

**ROADMAP:** Phase 5 (Operational Readiness)

---

### G2. 🔄 Observability Tracing with OpenTelemetry (PARTIALLY COMPLETE)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** 🔄 PARTIAL

**Problem:** No distributed tracing for request flows across services. Hard to diagnose latency issues.

**Solution Implemented (Partial):**

- `TracedEventMetrics` decorator wraps `IEventMetrics` with `System.Diagnostics.Metrics` counters and histograms
- Pipeline meter (`Meridian.Pipeline`) exports published/dropped/trade/depth/quote/integrity/historical counters via OTLP
- Latency histogram (`meridian.pipeline.latency`) tracks event processing time in milliseconds
- `OpenTelemetrySetup` updated to register pipeline meter alongside existing application meters
- `CompositionOptions.EnableOpenTelemetry` flag gates decorator registration in DI
- `MarketDataTracing` extended with `StartBatchConsumeActivity`, `StartBackfillActivity`, `StartWalRecoveryActivity`

**Remaining Work:**

- Wire trace context propagation from provider receive through pipeline to storage write
- Add correlation IDs to structured log messages
- Integrate distributed tracing for backfill worker service
- Export traces to Jaeger/Zipkin for visualization

**Files:**

- `Application/Tracing/TracedEventMetrics.cs` (new)
- `Application/Tracing/OpenTelemetrySetup.cs` (updated)
- `Application/Composition/ServiceCompositionRoot.cs` (updated)
- `tests/Meridian.Tests/Application/Monitoring/TracedEventMetricsTests.cs` (new)

**ROADMAP:** Sprint 4 (partial), Sprint 8 (full trace propagation)

---

### G3. ✅ Scheduled Maintenance and Archive Management (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** No automated maintenance for old files, index rebuilding, or archive optimization.

**Solution Implemented:**

- `ScheduledArchiveMaintenanceService` runs maintenance tasks on schedule
- `ArchiveMaintenanceScheduleManager` manages CRON-based schedules
- Tasks: file integrity validation, orphan cleanup, index rebuild, compression optimization
- `/api/maintenance/*` endpoints for manual triggering

**Files:**

- `Storage/Maintenance/ScheduledArchiveMaintenanceService.cs`
- `Storage/Maintenance/ArchiveMaintenanceScheduleManager.cs`
- `Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs`

**ROADMAP:** Phase 5 (Operational Readiness)

---

## Theme J: Data Canonicalization

### J1. ✅ Deterministic Canonicalization Design (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** Equivalent market events from different providers for the same instrument produce structurally incomparable records. The same instrument appears as different symbol strings, condition codes use different encoding systems, and venue identifiers are inconsistent across providers.

**Solution Implemented:**

- Comprehensive design document with provider field audit covering timestamp formats, aggressor side determination, venue identifiers, condition codes, and sequence numbers across Alpaca, Polygon, IB, and StockSharp
- Detailed canonicalization stage design with `IEventCanonicalizer` interface and `EventCanonicalizer` class
- Condition code mapping registry specification (CTA plan codes, SEC numeric codes, IB field codes → canonical enum)
- Venue normalization to ISO 10383 MIC codes with complete Polygon exchange mapping
- 3-phase rollout plan: Contract + Mapping Inventory → Dual-Write Validation → Default Canonical Read Path
- Acceptance criteria, operational metrics, risk mitigations, and test strategy

**Files:**

- `docs/architecture/deterministic-canonicalization.md`

**ROADMAP:** Theme J (Data Canonicalization)

---

### J2. ✅ MarketEvent Canonical Fields (COMPLETED)

**Impact:** High | **Effort:** Low | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** `MarketEvent` envelope lacks fields to distinguish raw vs. canonicalized events and carry resolved identifiers.

**Solution Implemented:**

- Added `CanonicalSymbol` (`string?`), `CanonicalizationVersion` (`int`), `CanonicalVenue` (`string?`) fields to `MarketEvent` sealed record
- Updated `MarketDataJsonContext` source generator attributes for new fields and canonicalization types
- New fields use `WhenWritingNull`/default omission for backward compatibility with existing JSONL files
- Added `EffectiveSymbol` property (`CanonicalSymbol ?? Symbol`) for downstream consumers
- Both Domain and Contracts `MarketEvent` records updated in sync

**Files:**

- `src/Meridian.Domain/Events/MarketEvent.cs`
- `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`

**Dependencies:** None (additive change)

---

### J3. ✅ EventCanonicalizer Implementation (COMPLETED)

**Impact:** High | **Effort:** Medium | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** No canonicalization step exists between provider adapters and `EventPipeline`.

**Solution Implemented:**

- `IEventCanonicalizer` interface with `Canonicalize(MarketEvent raw, CancellationToken ct)` method
- `EventCanonicalizer` class using `with` expression pattern (same as `StampReceiveTime()`)
- Resolves symbols via `CanonicalSymbolRegistry.ResolveToCanonical()`, maps venues via `VenueMicMapper`, extracts venue from typed payloads (Trade, BboQuote, LOBSnapshot, L2Snapshot, OrderFlowStatistics, IntegrityEvent)
- Skips heartbeats and already-canonicalized events (idempotent)
- Sets `Tier = Enriched` on canonicalized events
- 12+ unit tests covering symbol resolution, venue normalization, idempotency, and edge cases

**Files:**

- `src/Meridian.Application/Canonicalization/IEventCanonicalizer.cs`
- `src/Meridian.Application/Canonicalization/EventCanonicalizer.cs`
- `tests/Meridian.Tests/Application/Services/EventCanonicalizerTests.cs`

**Dependencies:** J2 (canonical fields on MarketEvent)

---

### J4. ✅ Condition Code Mapping Registry (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** Trade condition codes stored as raw `string[]?` with no cross-provider normalization.

**Solution Implemented:**

- `CanonicalTradeCondition` enum with 16+ canonical values (Regular, FormT_ExtendedHours, OddLot, Intermarket_Sweep, OpeningPrint, ClosingPrint, etc.)
- `ConditionCodeMapper` class with `FrozenDictionary<(provider, raw_code), CanonicalTradeCondition>` for zero-allocation hot-path lookups
- Mapping table in `config/condition-codes.json` covering 17 Alpaca CTA plan codes, 19 Polygon SEC numeric codes, 8 IB field codes
- `MapConditions()` returns both canonical and raw arrays for auditability; `MapSingle()` for individual lookups
- Loaded from JSON at startup with graceful fallback if file missing

**Files:**

- `src/Meridian.Application/Canonicalization/ConditionCodeMapper.cs`
- `config/condition-codes.json`
- `tests/Meridian.Tests/Application/Services/ConditionCodeMapperTests.cs`

**Dependencies:** J3 (EventCanonicalizer to invoke mapper)

---

### J5. ✅ Venue Normalization to ISO 10383 MIC (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** Venue identifiers differ across providers for the same exchange.

**Solution Implemented:**

- `VenueMicMapper` class with `FrozenDictionary<(provider, rawVenue), string?>` for zero-allocation lookups
- Mapping table in `config/venue-mapping.json`: 29 Alpaca text mappings, 17 Polygon numeric ID mappings, 17 IB routing name mappings (including `SMART → null` for unmappable IB meta-venues)
- Case-insensitive venue matching with provider-scoped lookups
- `CanonicalVenue` field populated on `MarketEvent` envelope via `EventCanonicalizer`
- Loaded from JSON at startup with graceful fallback if file missing

**Files:**

- `src/Meridian.Application/Canonicalization/VenueMicMapper.cs`
- `config/venue-mapping.json`
- `tests/Meridian.Tests/Application/Services/VenueMicMapperTests.cs`

**Dependencies:** J3 (EventCanonicalizer to invoke mapper)

---

### J6. ✅ Provider Adapter Wiring (COMPLETED)

**Impact:** High | **Effort:** High | **Priority:** P1 | **Status:** ✅ DONE

**Problem:** Provider adapters publish raw events without canonicalization.

**Solution Implemented:**

- `CanonicalizingPublisher` decorator wraps `IMarketEventPublisher` with transparent canonicalization
- DI wiring in `ServiceCompositionRoot.AddCanonicalizationServices()` — decorates the existing pipeline publisher
- Configurable pilot symbol list for phased rollout (clear `PilotSymbols` for all-symbol canonicalization)
- Dual-write mode: publishes both raw and canonicalized events for parity validation
- Lock-free metrics tracking (canonicalized count, skipped count, unresolved count, average duration)
- `CanonicalizationConfig` in `appsettings.json` controls `Enabled`, `PilotSymbols`, `DualWriteRawAndCanonical`, `ConditionCodesPath`, `VenueMappingPath`, and `Version`
- 17+ unit tests covering pilot filtering, dual-write, metrics, and edge cases

**Files:**

- `src/Meridian.Application/Canonicalization/CanonicalizingPublisher.cs`
- `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`
- `src/Meridian.Core/Config/CanonicalizationConfig.cs`
- `tests/Meridian.Tests/Application/Services/CanonicalizingPublisherTests.cs`

**Dependencies:** J3 (EventCanonicalizer implementation)

---

### J7. ✅ Canonicalization Metrics and Monitoring (COMPLETED)

**Impact:** Medium | **Effort:** Low | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** No observability into canonicalization success rates, latency, or unresolved mappings.

**Solution Implemented:**

- `CanonicalizationMetrics` static class with thread-safe counters: success, soft-fail, hard-fail, dual-write totals
- Per-provider parity statistics (`ProviderParityStats`) tracking match rates, unresolved breakdowns (symbol/venue/condition)
- `CanonicalizationSnapshot` immutable record for point-in-time metric export
- API endpoints via `CanonicalizationEndpoints`:
  - `GET /api/canonicalization/status` — overall canonicalization metrics
  - `GET /api/canonicalization/parity` — per-provider parity breakdown
  - `GET /api/canonicalization/parity/{provider}` — single provider detail with unresolved field breakdown
  - `GET /api/canonicalization/config` — current canonicalization configuration
- `CanonicalizingPublisher` also exposes per-instance metrics (canonicalized count, average duration)

**Files:**

- `src/Meridian.Application/Canonicalization/CanonicalizationMetrics.cs`
- `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs`
- `src/Meridian.Contracts/Api/UiApiRoutes.cs` (route constants)

**Dependencies:** J3 (EventCanonicalizer must emit metrics)

---

### J8. ✅ Golden Fixture Test Suite (COMPLETED)

**Impact:** Medium | **Effort:** Medium | **Priority:** P2 | **Status:** ✅ DONE

**Problem:** No test fixtures for verifying canonicalization correctness across providers.

**Solution Implemented:**

- Unit tests for `EventCanonicalizer`, `ConditionCodeMapper`, `VenueMicMapper`, and `CanonicalizingPublisher` cover core correctness, idempotency, and edge cases
- Property tests for idempotency (canonicalize twice = same result), raw symbol preservation, and tier progression are covered in `EventCanonicalizerTests`
- **8 curated fixture JSON files** in `tests/Meridian.Tests/Application/Canonicalization/Fixtures/` covering Alpaca and Polygon regular, extended-hours, and odd-lot trade scenarios plus cross-provider XNAS identity checks
- **`CanonicalizationGoldenFixtureTests`** loads all `.json` fixture files at runtime using `[Theory][MemberData]`, constructs `MarketEvent` from fixture inputs, applies production symbol and venue canonicalization (via `venue-mapping.json`), and asserts canonical symbol, venue, tier, and version fields match expected values
- PR checks now emit an actionable canonicalization drift report artifact/summary for unmapped condition codes and venues
- A manual fixture-maintenance workflow supports curated suite upkeep

**Files:**

- `tests/Meridian.Tests/Application/Canonicalization/Fixtures/*.json` (8 fixture files)
- `tests/Meridian.Tests/Application/Canonicalization/CanonicalizationGoldenFixtureTests.cs` (new)

**Dependencies:** J3 (EventCanonicalizer to test against)

---

## Priority Matrix

### By Impact and Effort

| Priority | Items | Description |
| ---------- | ------- | ------------- |
| **P0** | A1-A4 | Critical reliability fixes - ALL DONE ✅ |
| **P1** | A3, A5, B1-B2, C1-C2, C4-C6, D4, G1 | High impact, low-medium effort - A3, B2 DONE ✅ |
| **P2** | A6-A7, B3-B5, D5-D6, E1, F2-F3, G3 | Medium impact or higher effort - core work complete; remaining focus shifts to roadmap execution |
| **P3** | D7, E3 | Lower priority or high effort - C7, F1 DONE ✅ |

### Recommended Execution Order

**Phase 1 — Quick Wins (4-6 weeks):**

1. C4 — Injectable metrics (unblocks testability)
2. C5 — Consolidated config validation (cleaner startup)
3. C6 — Composite storage sink (new capability)
4. D4 — Quality metrics API (immediate UX value)
5. B1 — Complete dropped event audit with API endpoint

**Phase 2 — Core Architecture (6-8 weeks):**
6. C1 — Unified provider registry
7. C2 — Single DI composition path
8. B2 — HTTP endpoint integration tests
9. A7 — Standardized error handling

**Phase 3 — Testing Foundation (8-10 weeks):**
10. B3 — Infrastructure provider tests
11. B4 — Application service tests
12. B5 — Provider SDK tests

**Phase 4 — Larger Refactors (12-16 weeks):**
13. ~~C3 — NYSE-side WebSocket lifecycle consolidation~~ ✅ Done
14. ~~C7 — WPF/UWP service deduplication~~ ✅ Done (UWP removed)
15. E3 — GC pressure reduction (Polygon optimization)
16. ~~F1 — UWP navigation consolidation~~ ✅ Done (UWP removed)

---

## Execution Strategy

### Parallel Tracks

1. **Testing Track** — Can run parallel to architecture work
   - B2-B5: Build test suite incrementally
   - Target 80% coverage for production readiness

2. **Architecture Track** — Core refactoring
   - C1-C6: Improve modularity and maintainability
   - A7: Standardize error handling

3. **API Track** — Complete HTTP API surface
   - D4, D7: Finish quality endpoints and OpenAPI annotations

4. **Performance Track** — Optimization (lower priority)
   - E3: Polygon zero-alloc parsing
   - Theme K: trading workstation delivery

### Success Metrics

| Metric | Current | Target | Phase |
| -------- | --------- | -------- | ------- |
| Completed Improvements | 35/35 | 35/35 | All |
| Test Files | 219 | 250+ | Phase 1-3 |
| Test Methods | ~3,444 | 4,000+ | Phase 1-3 |
| Route Constants | 283 | 283 | Phase 3 |
| Provider Test Files | 12 | 15+ | Phase 3 |

### Risk Mitigation

- **Break Large Refactors** — Theme K workstation slices and large readability refactors should be split into smaller PRs
- **Test First** — B2-B5 should precede major refactoring
- **Incremental Rollout** — F1 (UWP consolidation) can be phased
- **Benchmark Performance** — E3 must include before/after metrics

---

## Delivery Operating Model

### Workstream Ownership

| Workstream | Scope | Suggested Owner | Supporting Roles |
| ------------ | ------- | ----------------- | ------------------ |
| Reliability | A1-A7 | Platform/Core lead | Provider maintainers, SRE |
| Test Foundation | B1-B5 | QA automation lead | Service owners, API maintainers |
| Architecture | C1-C7 | Principal engineer | App architecture guild |
| API & UX | D1-D7, F1-F3 | Full-stack lead | Frontend + API contributors |
| Ops/Observability | G1-G3 | DevOps lead | Infra + on-call engineers |

### PR Sizing Guidance

- **Small PR (preferred):** 1 improvement item or one coherent subset (<500 LOC net change).
- **Medium PR:** 1 item with migration shims + tests (<1,200 LOC net).
- **Large PR (exception):** Theme K or readability-level refactors; require a design document and staged rollout plan.

### Quality Gates per Improvement Item

Each item should not be marked complete until all gates are met:

1. Code merged behind existing CI checks.
2. Test coverage added or updated for touched behavior.
3. Operational visibility updated (logs/metrics/traces where applicable).
4. Documentation updated in `ROADMAP.md`, this file, and endpoint docs (if API-facing).

---

## Dependency Map

| Item | Depends On | Why Dependency Exists |
| ------ | ------------ | ----------------------- |
| B2 | C2 | Stable DI composition needed to host test server predictably |
| C1 | C2 | Registry unification should land after single composition path |
| D4 | B1 | Drop statistics model from audit trail is prerequisite for API exposure |
| B3 | C3 (optional) | Tests can start now, but base-class refactor will reduce fixture duplication |
| D7 | D4 | Response annotations are easier once quality endpoints are concrete |
| G2 | C4 | Injectable metrics/tracing abstractions reduce instrumentation coupling |

### Critical Path (Shortest Path to Production Readiness Lift)

1. ~~C4 → C5 → D4/B1 completion~~ ✅ Done
2. ~~C6, A7~~ ✅ Done
3. ~~B2 → B3 (provider confidence)~~ ✅ Done
4. ~~C1/C2 (composition + provider extensibility)~~ ✅ Done
5. provider/runtime hardening → Theme K workstation delivery → flagship capability implementation

---

## Definition of Done Checklist

Use this checklist before changing any item status from 📝/🔄 to ✅:

- [ ] Acceptance criteria met for the item’s “Proposed Solution.”
- [ ] Unit/integration tests included and passing in CI.
- [ ] No new open task markers left without a linked backlog issue.
- [ ] Telemetry impact evaluated (log/metric/trace).
- [ ] Backward compatibility validated (config, endpoints, file formats).
- [ ] Documentation and status tables updated in this file.

---

## Review Cadence & Reporting

- **Weekly (engineering sync):** Update item-level status and blockers.
- **Bi-weekly (architecture review):** Re-score priorities P0-P3 based on new risk and customer impact.
- **Per release:** Recompute completion metrics and validate roadmap phase alignment.

### Suggested Reporting Snippet (for release notes / standups)

```md
Improvements Tracker Update
- Completed this period: <items>
- Partially complete: <items>
- Newly opened risks: <items>
- Next period focus: <top 3 items>
```

---

## Reference Documents

- **[ROADMAP.md](ROADMAP.md)** — Wave-structured delivery roadmap and release gates
- **[EVALUATIONS_AND_AUDITS.md](EVALUATIONS_AND_AUDITS.md)** — Consolidated architecture evaluations, code audits, and assessments
- **[CHANGELOG.md](CHANGELOG.md)** — Historical changes and version history
- **[TODO.md](TODO.md)** — Auto-generated task marker tracking from code comments
- **[DEPENDENCIES.md](../DEPENDENCIES.md)** — NuGet package dependencies
- **[production-status.md](production-status.md)** — Current production readiness assessment
- **[deterministic-canonicalization.md](../architecture/deterministic-canonicalization.md)** — Cross-provider canonicalization design (Theme J)

### Archived Improvement Documents

These documents are superseded by this consolidated tracker:

- `../../archive/docs/summaries/IMPROVEMENTS_2026-02.md` — Original functional improvements (10 completed)
- `../../archive/docs/summaries/STRUCTURAL_IMPROVEMENTS_2026-02.md` — Original structural improvements (15 items)
- `../../archive/docs/summaries/REDESIGN_IMPROVEMENTS.md` — UI redesign quality summary
- `../../archive/docs/summaries/2026-02_UI_IMPROVEMENTS_SUMMARY.md` — Visual UI improvements summary
- `../../archive/docs/summaries/CHANGES_SUMMARY.md` — Historical changes (v1.5.0 and earlier)
- `../../archive/docs/summaries/ROADMAP_UPDATE_SUMMARY.md` — Roadmap expansion summary (archived)

See [`https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/INDEX.md`](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/INDEX.md) for context on archived documents.

---

**Last Updated:** 2026-04-28
**Maintainer:** Project Team
**Status:** ✅ Active tracking document — 100% of core improvements complete (35/35), Theme J canonicalization complete (8/8), Theme K delivery active
**Next Review:** Weekly engineering sync (or immediately after any status change)


---

## Theme K: Trading Workstation Migration

### K1. 📝 Workflow-Centric Workspace Migration

**Impact:** High | **Effort:** High | **Priority:** P1 | **Status:** 🔄 IN PROGRESS

**Problem:** Meridian functionality is broad but still exposed through too many page-centric entry points. The vocabulary and workspace/session model now align on `Research`, `Trading`, `Data Operations`, and `Governance`, but the product still needs deeper workspace-native shells and quick-action flows.

**Planned Solution:**

- Consolidate top-level UX into `Research`, `Trading`, `Data Operations`, and `Governance` workspaces
- Ensure all major capabilities are reachable from primary navigation and command palette
- Add workflow-level entry points instead of orphan feature pages

**Current signal:**

- `ShellNavigationCatalog`, workspace shell pages, richer command-palette routing, governance aliases, and shell smoke/full-sweep coverage are now in the validated baseline
- duplicate title chrome on many legacy deep pages now compacts away automatically when those pages are hosted inside `WorkspaceDeepPageHostPage`
- action-heavy legacy headers on `MessagingHub`, `NotificationCenter`, `SecurityMaster`, `ServiceManager`, and `PositionBlotter` now compact correctly inside the shared host while keeping their page-specific command and trust bands
- `PositionBlotter`, `SecurityMaster`, and `ServiceManager` now expose richer page-body workbenches and workflow-native inspector rails instead of only inheriting the shared host chrome
- the Trading shell now keeps portfolio drill-ins inside the cockpit by routing operators to the active run portfolio when a run is selected and to the account portfolio when no active run is bound, reinforcing Wave 3 shared-model continuity without bouncing back to `Research`
- the Trading shell now includes a desk briefing hero that derives focus, readiness tone, and next handoff from active-run, workflow-summary, and shared operator-readiness inputs instead of a separate shell-local readiness model
- the Research shell now includes a desk briefing hero that derives focus, selected-run posture, run-detail / portfolio drill-ins, and paper-promotion handoff state from shared workstation run data instead of a separate shell-local research model
- the current shell support slice also includes Trading Hours session-specific briefings, OrderBook order-flow posture, RunCashFlow empty-state guidance, Welcome readiness progress, Storage archive posture plus preview scope/guidance, actionable shell-context attention detail, Watchlist pinned-first staging, run review-packet operator-inbox items, and Fund Accounts balance-evidence briefing as workflow-support evidence rather than separate readiness gates
- WPF screenshot refresh now supports scheduled/push/manual catalog and manual capture groups, publishes diagnostic artifacts, and commits screenshot PNG changes once after the capture matrix; keep this in the validation-evidence lane rather than treating it as Wave 2-4 acceptance
- RunCashFlow now shows selected-run, missing-run, no-event, and loaded cash-flow guidance from retained run summaries; keep this in the shared-run continuity lane rather than treating it as completed governance cash-flow modeling
- the mixed `MainPageUiWorkflowTests` bundle is stable again through isolated workspace persistence in the automation facade and shell-contract assertions that avoid unrelated singleton drift
- `MainPageSmokeTests`, `MainPageUiWorkflowTests`, `RunMatUiSmokeTests`, `NavigationPageSmokeTests`, `WorkstationPageSmokeTests`, `NavigationServiceTests`, and `FullNavigationSweepTests` now run under `NavigationServiceSerialCollection`, keeping the mixed shell bundle deterministic while still validating full registered-page reachability
- remaining K1 work is now concentrated in broader high-traffic page-body harmonization and workstation refinements. `OrderBook` has started this path with order-flow posture, while pages such as `DataQuality` and `LiveDataViewer` still need deeper treatment rather than more shell-foundation plumbing.

**ROADMAP:** Phase 11

---

### K2. 📝 Shared Run / Portfolio / Ledger Read Models

**Impact:** High | **Effort:** High | **Priority:** P1 | **Status:** 🔄 IN PROGRESS

**Problem:** Meridian now has the beginnings of one operator-facing run model, but it is still backtest-first and not yet expanded across paper/live history or richer governance workflows.

**Planned Solution:**

- Introduce shared `StrategyRun` contracts
- Add shared portfolio summaries and ledger read services
- Make run history, portfolio state, and ledger drill-down reusable across engines and modes

**Current baseline:**

- shared workstation DTOs exist for run summaries/details, portfolio summaries/positions, ledger summaries, journal rows, trial balance rows, and run comparison views
- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` are in code
- WPF now exposes a first `StrategyRuns` browser plus `RunDetail`, `RunPortfolio`, and `RunLedger` drill-ins
- governance fund operations now exposes explicit fund cash-flow projection ladders/events plus consolidated and account-linked multi-ledger views

**Next expansion:**

- deepen per-entity / per-sleeve / per-vehicle ledger posting fidelity beyond the current account-linked multi-ledger filters
- integrate Security Master metadata so portfolio, ledger, and governance surfaces use one authoritative instrument layer
- deepen the delivered run-scoped reconciliation engine and file-backed break queue into governed exception handling
- add report generation tools for fund-ops, investor, compliance, and board-style reporting packs
- connect the shared run/portfolio/ledger model to account, entity, and trade-management workflows so Meridian can function as a full fund-management platform

**ROADMAP:** Phase 12

---

### K2A. ✅ Security Master Productization Baseline

**Impact:** High | **Effort:** High | **Priority:** P1 | **Status:** ✅ COMPLETE (baseline delivered; downstream governance follow-ons remain active)

**Problem solved:** Security Master needed to move from a backend capability into an explicit product/platform seam for workstation workflows.

**Delivered baseline:**

- Security Master now acts as the authoritative instrument-definition layer across research, trading, portfolio, ledger, reconciliation, governance, and WPF drill-ins
- workstation/read-model propagation is in place through shared `WorkstationSecurityReference` coverage and provenance
- WPF, shared DTOs, conflict handling, corporate actions, trading parameters, and ingest seams are materially in code

**Remaining follow-on work:**

- deepen governance and fund-operations workflows built on top of the delivered baseline through K2 and Wave 4 work
- reuse Security Master metadata in account/entity, cash-flow, multi-ledger, reconciliation, and reporting workflows instead of creating a parallel governance seam
- enforce PR/review validation that governance DTOs/services introducing instrument metadata carry Security Master identity/provenance fields, with no governance-local instrument definitions except adapter-only mapped intermediates
- reviewer search guidance: scan governance DTO/service changes for instrument-term fields (`Symbol`, `Cusip`, `Isin`, `Coupon`, `Maturity`, `Issuer`, `Venue`, `AssetClass`) lacking Security Master references

**ROADMAP:** Phase 12A baseline delivered; follow-ons continue in Phase 12 / Wave 4

---

### K3. 📝 Backtest + Paper-Trading Experience Unification

**Impact:** High | **Effort:** High | **Priority:** P1 | **Status:** 📝 PLANNED

**Problem:** Native backtesting, Lean integration, and paper-trading infrastructure exist, but the user experience is still split across separate surfaces and does not yet feel like one strategy lifecycle on top of the new shared run model.

**Planned Solution:**

- Build a unified Backtest Studio with engine selection and run comparison
- Harden the existing paper-trading infrastructure into a real trading cockpit
- Add explicit promotion flow from Backtest → Paper → Live with safety guardrails

**Current baseline:**

- Paper trading gateway, risk rules, order abstractions, and brokerage gateway framework are implemented
- REST endpoints for account, orders, sessions, health, and promotion are wired
- Brokerage gateway adapters for Alpaca, Robinhood, IB, and StockSharp are in place

**Remaining scope (Wave 2):**

- Web dashboard: live positions, open orders, fills, P&L, and risk state panels wired to brokerage gateways
- `Backtest → Paper` promotion: explicit lifecycle step, audit trail, and safety gate
- Paper session persistence and replay
**ROADMAP:** Phase 13
