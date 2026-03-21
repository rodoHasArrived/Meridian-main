# Meridian - Consolidated Evaluations & Audits

**Version:** 1.7.0
**Last Updated:** 2026-03-20
**Status:** Consolidated reference document (aligned with current roadmap/status docs)

This document consolidates all architecture evaluations, code audits, desktop assessments, improvement brainstorms, and architecture proposals into a single navigable reference. It replaces the need to read 20+ individual files across `docs/evaluations/`, `docs/audits/`, and `docs/development/` for a complete project health picture.

**Canonical tracking documents (not merged here):**
- [`ROADMAP.md`](ROADMAP.md) — phased execution timeline (Phases 0-10)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) — item-level improvement tracking (35 items, 7 themes)
- [`production-status.md`](production-status.md) — provider readiness and production checklist

---

## Table of Contents

- [Project Health Summary](#project-health-summary)
- [Refresh Highlights](#refresh-highlights)
- [Architecture Evaluations](#architecture-evaluations)
  - [Real-Time Streaming Architecture](#real-time-streaming-architecture)
  - [Storage Architecture](#storage-architecture)
  - [Data Quality Monitoring](#data-quality-monitoring)
  - [Historical Data Providers](#historical-data-providers)
  - [Ingestion Orchestration](#ingestion-orchestration)
  - [Operational Readiness](#operational-readiness)
- [Desktop Assessments](#desktop-assessments)
  - [Desktop UX Assessment](#desktop-ux-assessment)
  - [Desktop Provider Configurability](#desktop-provider-configurability)
  - [Desktop Platform Improvements Guide](#desktop-platform-improvements-guide)
  - [Desktop Improvements Executive Summary](#desktop-improvements-executive-summary)
- [Code Audits](#code-audits)
  - [Repository Hygiene (H1-H3)](#repository-hygiene-h1-h3)
  - [Debug Code Analysis (H3)](#debug-code-analysis-h3)
  - [Platform Cleanup (UWP Removal)](#platform-cleanup-uwp-removal)
  - [Further Simplification Opportunities](#further-simplification-opportunities)
- [Repository Cleanup](#repository-cleanup)
  - [Cleanup Action Plan Status](#cleanup-action-plan-status)
  - [Config Consolidation](#config-consolidation)
- [Improvement Brainstorms](#improvement-brainstorms)
  - [High-Impact Improvement Brainstorm (March 2026)](#high-impact-improvement-brainstorm-march-2026)
  - [High-Impact Improvements Brainstorm (Feb/Mar 2026)](#high-impact-improvements-brainstorm-febmar-2026)
  - [High-Value Low-Cost Improvements](#high-value-low-cost-improvements)
- [Architecture Proposals](#architecture-proposals)
  - [Nautilus-Inspired Restructuring](#nautilus-inspired-restructuring)
  - [Assembly-Level Performance Opportunities](#assembly-level-performance-opportunities)
  - [Next Frontier Brainstorm (March 2026)](#next-frontier-brainstorm-march-2026)
- [Cross-Cutting Findings](#cross-cutting-findings)
- [Archived Evaluations](#archived-evaluations)

---

## Project Health Summary

| Domain | Assessment | Key Finding | Source |
|--------|-----------|-------------|--------|
| Streaming Architecture | Sound | Polly resilience patterns good; optimize pipeline throughput and backpressure signaling | [Evaluation](#real-time-streaming-architecture) |
| Storage Architecture | Well-designed | JSONL + Parquet dual-format provides excellent write/query balance; WAL ensures durability | [Evaluation](#storage-architecture) |
| Data Quality Monitoring | Comprehensive | 12+ specialized services; improve automated remediation and ML anomaly detection | [Evaluation](#data-quality-monitoring) |
| Historical Providers | Well-designed | Alpaca + Polygon primary; Stooq + Yahoo free fallback; validate Polygon rate limits | [Evaluation](#historical-data-providers) |
| Ingestion Orchestration | Uneven maturity | Strong building blocks; needs unified job model for realtime + backfill workloads | [Evaluation](#ingestion-orchestration) |
| Operational Readiness | Pilot Ready | Good monitoring baseline; needs standardized SLOs and runbook-linked alerts | [Evaluation](#operational-readiness) |
| Desktop UX | Partial parity | 49 pages, 104 services; key features implemented; remaining gaps in live backend integration | [Assessment](#desktop-ux-assessment) |
| Code Quality | Excellent | Debug code is intentional; no cleanup required; repository hygiene complete | [Audit](#debug-code-analysis-h3) |
| Repository Cleanup | Complete | Phases 1-6 done; generated docs refresh and HtmlTemplateGenerator CSS/JS extraction remain | [Audit](#cleanup-action-plan-status) |
| Simplification Backlog | ~2,800-3,400 LOC removable | 12 categories identified; highest priority: bare catches, dead code, Task.Run misuse | [Audit](#further-simplification-opportunities) |
| Critical Defects (March 2026) | 6.5/10 — Architecturally Sound, Operationally Risky | Silent data-loss paths in flush semantics, WAL transactions, price precision, and deduplication | [Brainstorm](#high-impact-improvement-brainstorm-march-2026) |
| Code Generalization | Strong domain types needed | Stringly-typed identifiers, thin singletons, and duplicated WebSocket lifecycle code are top risks | [Brainstorm](#high-impact-improvements-brainstorm-febmar-2026) |
| Nautilus-Inspired Restructuring | Partially Implemented | 5/12 proposals implemented; co-located provider configs, parsing layer, FSM base still open | [Proposal](#nautilus-inspired-restructuring) |
| Next Frontier (March 2026) | 94%+ core complete | 11 capabilities shipped; correlation engine, ML anomaly detection, cloud sinks remain future work | [Brainstorm](#next-frontier-brainstorm-march-2026) |

## Refresh Highlights

**This 2026-03-20 refresh aligns the consolidated document with the current planning set:**

- C1/C2 provider registration and DI composition are now reflected as **completed**, matching `IMPROVEMENTS.md` and `ROADMAP.md`.
- Operational readiness now reflects the shipped SLO registry, alert-to-runbook linkage, and current remaining work (release gates, rollback drills).
- Desktop coverage now includes the implementation guide and executive summary alongside the historical UX assessment and configurability review.
- Cross-cutting findings now distinguish between **completed foundations** and **still-open follow-through work**, instead of pointing at superseded statuses.
- Repository cleanup notes now call out that generated documentation has been refreshed since the original cleanup audit, leaving HtmlTemplateGenerator asset extraction as the main residual cleanup idea.

---

## Architecture Evaluations

### Real-Time Streaming Architecture

> Source: `docs/evaluations/realtime-streaming-architecture-evaluation.md`
> Date: 2026-02-03 | Status: Evaluation Complete

**Verdict:** Fundamentally sound with good resilience patterns.

**Strengths:**
- 5 streaming providers behind `IMarketDataClient` abstraction (Alpaca, Polygon, IB, StockSharp, NYSE)
- `EventPipeline` uses `System.Threading.Channels` with bounded capacity (100,000 events) and configurable backpressure
- Polly-based retry and circuit breaker policies for WebSocket connections
- Domain collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`) separate parsing from routing
- `SubscriptionManager` tracks active subscriptions for recovery on reconnect

**Improvement Opportunities:**
| Priority | Opportunity | Status |
|----------|-------------|--------|
| P1 | Optimize EventPipeline throughput for >100 Hz scenarios | Partially addressed (C4 injectable metrics done) |
| P1 | Enhance backpressure signaling to prevent data loss under load | `DroppedEventAuditTrail` implemented (B1 done) |
| P2 | Unify WebSocket lifecycle across providers via base class | Open (C3 in IMPROVEMENTS.md) |
| P2 | Provider degradation scoring for intelligent failover | Done (H4) |

---

### Storage Architecture

> Source: `docs/evaluations/storage-architecture-evaluation.md`
> Date: 2026-02-03 | Status: Evaluation Complete

**Verdict:** Well-designed for archival-first market data collection.

**Strengths:**
- Dual-format: JSONL (human-readable, append-only) + Parquet (columnar, compressed)
- Write-Ahead Log (WAL) with SHA256 checksums for crash-safe persistence
- 4 naming conventions (BySymbol, ByDate, ByType, Flat) for flexible organization
- 3 compression profiles: RealTime (LZ4), Standard (Gzip), Archive (ZSTD-19)
- Tiered storage (Hot/Warm/Cold) with configurable retention and automatic migration

**Improvement Opportunities:**
| Priority | Opportunity | Status |
|----------|-------------|--------|
| P1 | CompositeSink for multi-format writes with fault isolation | Done (C6) |
| P2 | Optimize Parquet write batching for reduced memory pressure | Open |
| P2 | Add storage space forecasting and quota warnings | QuotaEnforcementService exists |
| P3 | Crystallized storage format for ultra-compressed long-term archival | Documented in `docs/architecture/` |

---

### Data Quality Monitoring

> Source: `docs/evaluations/data-quality-monitoring-evaluation.md`
> Date: 2026-02-03 | Updated: 2026-03-19 | Status: Evaluation Complete

**Verdict:** Comprehensive and well-designed; 12+ specialized services.

**Latest Status (2026-03-19):** All core services operational and integrated with alerting pipeline. SLA monitoring fully implemented. Remaining recommendations (automated remediation, ML-based detection) roadmapped for Phase 15+.

**Service Inventory:**

| Service | Responsibility |
|---------|----------------|
| `DataQualityMonitoringService` | Orchestrates all quality checks |
| `CompletenessScoreCalculator` | Data completeness percentage |
| `GapAnalyzer` | Missing data period detection |
| `SequenceErrorTracker` | Out-of-order / duplicate events |
| `AnomalyDetector` | Price spike / crossed market detection |
| `LatencyHistogram` | End-to-end latency distribution |
| `CrossProviderComparisonService` | Data consistency across providers |
| `PriceContinuityChecker` | Price gap and continuity validation |
| `DataFreshnessSlaMonitor` | SLA compliance for data freshness |
| `DataQualityReportGenerator` | Daily quality reports |
| `BadTickFilter` | Invalid tick data filtering |
| `SpreadMonitor` | Bid-ask spread monitoring |

**Improvement Opportunities:**
| Priority | Opportunity | Status |
|----------|-------------|--------|
| P1 | Automated gap remediation (trigger backfill on detected gaps) | Open |
| P2 | ML-based anomaly detection (move beyond threshold-based rules) | Open |
| P2 | Quality scoring per provider for intelligent routing | Partial (degradation scoring done via H4) |
| P3 | Historical quality trend visualization | Open |

---

### Historical Data Providers

> Source: `docs/evaluations/historical-data-providers-evaluation.md`
> Date: 2026-02-20 | Status: Updated

**Verdict:** Multi-provider architecture is well-designed.

**Provider Recommendations:**

| Tier | Provider | Best For | Free Tier |
|------|----------|----------|-----------|
| Primary | Alpaca | US equities, intraday bars | Yes (with account) |
| Primary | Polygon | Professional tick data, aggregates | Limited |
| Secondary | Interactive Brokers | Comprehensive coverage, all types | Yes (with account) |
| Fallback | Tiingo | Cost-effective daily bars | Yes |
| Fallback | Stooq | International coverage, daily bars | Yes |
| Fallback | Yahoo Finance | Free daily bars (unofficial API) | Yes |
| Supplementary | Finnhub, Alpha Vantage, Nasdaq Data Link | Specialized use cases | Limited/Yes |

**Key Architecture Features:**
- `CompositeHistoricalDataProvider` with priority-based fallback chain
- `ProviderRateLimitTracker` enforces per-provider rate limits (H1 done)
- Health monitoring and automatic provider deprioritization
- Symbol resolution across providers

**Improvement Opportunities:**
| Priority | Opportunity | Status |
|----------|-------------|--------|
| P1 | Validate Polygon rate-limit assumptions against current docs | Open |
| P2 | Add tick-level historical data from IB for verification | Requires IBAPI build flag |
| P3 | Provider SDK auto-documentation from attributes | Open (I4 in ROADMAP) |

---

### Ingestion Orchestration

> Source: `docs/evaluations/ingestion-orchestration-evaluation.md`
> Date: 2026-02-12 | Status: Evaluation Complete

**Verdict:** Strong building blocks; orchestration maturity is uneven.

**Strengths:**
- Clean provider and storage abstractions enable scheduler-independent execution
- WAL + tiered storage reduces data-loss risk for long-running jobs
- Existing health and quality monitoring can be reused for orchestration signals
- Desktop UI already exposes backfill and status concepts

**Gap Analysis:**

| Gap | Risk | Priority |
|-----|------|----------|
| No unified job contract across realtime/backfill flows | Inconsistent behaviors | P0 — **Resolved**: `IngestionJobService` manages unified `IngestionJob` lifecycle with state machine; API endpoints at `/api/ingestion/jobs` |
| Limited checkpoint semantics exposed to users | Manual reruns after partial failures | P0 — **Resolved**: `IngestionJobService.UpdateCheckpointAsync()` + `/api/ingestion/jobs/resumable` endpoint exposes checkpoint semantics |
| Retry policy lacks workload-level intent | Over-retry, provider throttling | P1 |
| Missing explicit idempotency strategy | Duplicate records or unnecessary rewrites | P1 |
| Weak operator timeline/audit view | Harder post-incident analysis | P1 |

**Target Capabilities:**
1. **Unified Ingestion Job State Machine** — `Draft → Queued → Running → Paused → Completed | Failed | Cancelled`
2. **Policy-Driven Scheduler** — Cron, session-aware, signal-triggered
3. **Deterministic Resumability** — Resume from last committed checkpoint
4. **Idempotent Writes** — Dedupe keys: `(provider, symbol, timestamp, event_type, sequence)`

---

### Operational Readiness

> Source: `docs/evaluations/operational-readiness-evaluation.md`
> Date: 2026-02-12 | Updated: 2026-03-19 | Status: P0 Substantially Complete, P1 In Progress

**Verdict:** Production-ready with good observability and incident response foundations.

**Latest Status (2026-03-19):** SLO framework operational (7 SLO definitions), alert-to-runbook linkage implemented (`AlertRunbookRegistry`), health checks operational, operator runbook comprehensive. Remaining: formal post-incident review process, standardized rollback testing per release.

**Strengths:**
- Docker and systemd deployment artifacts present
- Extensive GitHub Actions workflows (22 workflows)
- Prometheus metrics and alert rule definitions available
- Active documentation for status, roadmap, and architecture

**Risk Matrix:**

| Risk | Impact | Priority |
|------|--------|----------|
| SLOs not consistently documented per subsystem | Hard to calibrate alerts | P0 — **Resolved**: `SloDefinitionRegistry` provides 7 runtime SLO definitions across 6 subsystems, each linked to alert rules and runbook sections. Full docs in `service-level-objectives.md` |
| Alert-to-runbook linkage is implicit | Slower incident triage | P0 — **Resolved**: `AlertRunbookRegistry` maps all 11 Prometheus alerts to runbook sections with probable causes, immediate actions, and SLO references. `EnrichWithRunbook()` augments dispatched alerts |
| Release readiness criteria are dispersed | Regressions reaching production | P1 |
| Rollback playbooks not standardized | Longer MTTR | P1 |
| Capacity thresholds under-specified | Late scaling detection | P2 |

**60-Day Plan:**
1. Weeks 1-2: Document SLOs for ingestion, storage, and export paths
2. Weeks 3-4: Embed runbook URLs into critical alert annotations
3. Weeks 5-6: Introduce consolidated release gate checklist in CI
4. Weeks 7-8: Review alert precision/recall; publish reliability scorecard

---

## Desktop Assessments

### Desktop UX Assessment

> Source: `docs/archived/desktop-end-user-improvements.md`
> Date: 2026-03-20 | Status: Archived historical assessment

**Scope:** 49 XAML pages, 104 services (72 shared + 32 WPF-specific), 1,266 tests

**Implemented Features:**

| Feature | Status | Details |
|---------|--------|---------|
| Command Palette (Ctrl+K) | Done | 47 commands, fuzzy search, recent tracking |
| Onboarding Tours | Done | 5 built-in tours with progress persistence |
| Workspace Persistence | Done | 4 default workspaces, session state, window bounds |
| Backfill Checkpoints | Done | Per-symbol progress, resume/retry, disk persistence |
| Keyboard Shortcuts | Done | 35 shortcuts with full service and tests |
| Fixture Mode | Done | Explicit `--fixture` / `MDC_FIXTURE_MODE` activation with visual warning |

**Remaining Priorities:**

| Priority | Gap | Impact |
|----------|-----|--------|
| P0 | Replace demo/simulated values with live backend state | Users can trust what they see — **Resolved**: `StatusServiceBase` now populates `DataProvenance` field ("live"/"fixture"/"offline") on `SimpleStatus`; `FixtureModeDetector` integration drives the banner |
| P0 | Resumable jobs with crash recovery for backfill/exports | Long-running work not lost — **Resolved**: `IngestionJobService` with checkpoint persistence + `/api/ingestion/jobs/resumable` endpoint |
| P0 | Explicit staleness + source provenance on key metrics | Prevents decisions on stale data — **Resolved**: `SimpleStatus` now includes `RetrievedAtUtc`, `SourceProvider`, `IsStale`, `AgeSeconds`, `DataProvenance` fields |
| P1 | Actionable error diagnostics with root-cause hints | Faster debugging for data engineers |
| P1 | Bulk symbol management (import/validate/fix workflows) | Faster portfolio setup |
| P2 | Alert intelligence (suppress duplicates, smart recommendations) | Reduced alert fatigue |

---

### Desktop Provider Configurability

> Source: `docs/evaluations/windows-desktop-provider-configurability-assessment.md`
> Date: 2026-02-13 | Updated: 2026-03-19 | Status: Infrastructure Complete, UI In Progress

**Key Finding:** Provider abstraction is solid (`IHistoricalDataProvider`, `CompositeHistoricalDataProvider`), but WPF Backfill page is mostly demo/static and lacks per-provider operational settings UI.

**Implementation Status (2026-03-19):**
- ✅ Type-safe DTOs: `BackfillConfigDto.Providers`, `BackfillProvidersConfigDto`, `BackfillProviderOptionsDto`
- ✅ Config service methods: `GetBackfillProvidersConfigAsync()`, `SetBackfillProviderOptionsAsync()`
- ✅ WPF ViewModels: `BackfillProviderSettingsViewModel` with ObservableCollection binding
- ✅ Provider health dashboard: `/api/providers/dashboard` for runtime state visibility
- 🔄 UI implementation: Provider list and reordering partial; full settings workflow in Phase 11

### Desktop Platform Improvements Guide

> Source: `docs/evaluations/desktop-platform-improvements-implementation-guide.md`
> Date: 2026-02-22 | Status: Implementation guide retained for active migration work

**Purpose:** Turns the desktop evaluations into concrete delivery work across testing, fixture mode, DI cleanup, and workflow-focused operator UX.

**Most relevant active guidance:**
- Establish deterministic desktop testing paths before expanding workflow surfaces further
- Prefer fixture/live provenance cues over simulated placeholder values
- Continue moving page-specific logic into testable services/view models
- Use the guide as the implementation bridge between historical desktop audits and Phases 11-13 in `ROADMAP.md`

### Desktop Improvements Executive Summary

> Source: `docs/evaluations/desktop-improvements-executive-summary.md`
> Date: 2026-02-22 | Updated: 2026-03-19 | Status: Current summary for desktop modernization

**Executive Takeaway:** The desktop stack has broad functional coverage already, but the highest-value remaining work is UX consolidation, stronger live-state trust signals, and a workflow-centric trading workstation structure.

**Current planning alignment:**
- Phase 11 — Reorganize the UX around Research, Trading, Data Operations, and Governance workspaces
- Phase 12 — Standardize shared run / portfolio / ledger read models
- Phase 13 — Unify native backtesting, Lean integration, and paper-trading workflows

---

## Code Audits

### Repository Hygiene (H1-H3)

> Source: `docs/archived/CLEANUP_SUMMARY.md`
> Date: 2026-02-10 | Status: Complete

| Item | Issue | Resolution |
|------|-------|------------|
| H1 | Accidental artifact file tracked in repo | Removed; `.gitignore` patterns added |
| H2 | `build-output.log` (93,549 bytes) in version control | Removed; `*.log` pattern prevents recurrence |
| H3 | Root-level narrative docs diluting discoverability | Moved to `docs/archived/` with date prefixes |

**Outcome:** Repository root is clean. All hygiene items resolved.

---

### Debug Code Analysis (H3)

> Source: `docs/archived/H3_DEBUG_CODE_ANALYSIS.md`
> Date: 2026-02-10 | Status: Complete

| Category | Instances | Verdict |
|----------|-----------|---------|
| `Console.WriteLine` | 20 | All intentional (CLI output, user-facing diagnostics) |
| `Debug.WriteLine` | 20 | All properly conditional (`#if DEBUG` or diagnostic context) |
| Skipped Tests | Reviewed | All have documented rationale |

**Conclusion:** Excellent code quality. No cleanup required.

---

### Platform Cleanup (UWP Removal)

> Source: `docs/archived/CLEANUP_OPPORTUNITIES.md`
> Date: 2026-02-10 (updated 2026-02-20) | Status: Complete

**Summary of Completed Work:**

| Category | Status |
|----------|--------|
| Repository Hygiene (H1-H3) | Done |
| UiServer Endpoint Extraction (3,030 → 260 LOC, 91.4% reduction) | Done |
| UWP Platform Removal (project deleted, solution cleaned, tests removed) | Done |
| UWP Service Migration (WPF is sole desktop client) | Done |
| Residual UWP References (R1-R9, all 9 items cleaned) | Done |
| HtmlTemplates Split (3 partial class files, 2,533 LOC) | Done |
| Storage Services Split (PortableDataPackager: 5 files, AnalysisExportService: 6 files) | Done |
| Architecture Debt (DataGapRepair DI, SubscriptionManager rename) | Done |

**Remaining:**
- Historical note: the original audit flagged generated docs as stale, but `docs/generated/` has since been refreshed and expanded
- HtmlTemplateGenerator still embeds CSS/JS inline (2,533 LOC) — could move to `wwwroot/`

---

### Further Simplification Opportunities

> Source: `docs/audits/FURTHER_SIMPLIFICATION_OPPORTUNITIES.md`
> Date: 2026-02-20 | Status: Documented for future consideration

**Total estimated removable/simplifiable code: ~2,800-3,400 lines**

| # | Category | Est. Lines | Risk | Priority |
|---|----------|-----------|------|----------|
| 1 | Thin WPF service wrappers | 800-950 | Low | Medium |
| 2 | Manual double-checked locking → `Lazy<T>` | 350-430 | Low | Medium |
| 3 | Endpoint boilerplate helpers | 400-600 | Low | Medium |
| 4 | ConfigStore/BackfillCoordinator wrappers | ~250 | Medium | Low |
| 5 | Orphaned ServiceBase abstractions | 500-700 | Medium | Medium |
| 6 | `Task.Run` wrapping async I/O | ~50 | Low-Med | **High** |
| 7 | Remaining bare catch blocks | ~30 | Low | **High** |
| 8 | FormatBytes/date format duplication | ~30 | Very Low | Low |
| 9 | Dead code and empty stubs | ~370 | Low | **High** |
| 10 | Stale UWP references in source comments | ~20 | None | Low |
| 11 | Endpoint file organization | ~0 (reorg) | Low | Low |
| 12 | Duplicate model definitions across layers | TBD | Medium | Low |

**Recommended Execution Order:**
1. Quick wins (High priority, Low risk): Items 7, 9, 6
2. Mechanical refactors (Medium priority, Low risk): Items 2, 8, 10
3. Structural simplification (Medium priority, Medium risk): Items 1, 3, 5
4. Architecture evaluation (Low priority, Medium risk): Items 4, 11, 12

---

## Repository Cleanup

### Cleanup Action Plan Status

> Source: `docs/archived/repository-cleanup-action-plan.md`
> Version: 1.2 | Date: 2026-02-16 | Status: Phases 1-6 Complete

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Duplicate Code | ~3,000 lines | ~500 lines | 83% reduction |
| Unused Files | ~260 KB | 0 KB | 100% removed |
| Duplicate Interfaces | 9 | 0 | 100% consolidated |
| Files >2,000 LOC | 4 | 0 | 100% decomposed |
| Orphaned Tests | ~15 files | 0 files | 100% aligned |

**Phase Status:**
1. Phase 1 (Immediate Wins): Done
2. Phase 2 (Interface Consolidation): Done
3. Phase 3 (Service Deduplication): Done
4. Phase 4 (Large File Decomposition): Done
5. Phase 5 (Documentation Consolidation): Done
6. Phase 6 (Build and CI Optimization): Done

---

### Config Consolidation

> Source: `CONFIG_CONSOLIDATION_REPORT.md` (root)
> Date: 2026-02-10 | Status: Complete

**Findings:**
- Props/targets files: No duplicates (3 files, each distinct purpose)
- Global config: Removed duplicate diagnostic suppressions
- Application config: No duplication found

---

## Improvement Brainstorms

### High-Impact Improvement Brainstorm (March 2026)

> Source: `docs/evaluations/high-impact-improvement-brainstorm-2026-03.md`
> Date: 2026-03-01 (follow-ups: 2026-03-10, 2026-03-11) | Status: Active — several items implemented

**Overall Rating:** 6.5/10 — Architecturally Sound, Operationally Risky

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

**Category 1 — Data Integrity & Correctness:**

| # | Issue | Status |
|---|-------|--------|
| 1.1 | EventPipeline flush semantics count dropped events as persisted — silent data loss | Open |
| 1.2 | WAL-to-Sink transaction gap — crash recovery may produce duplicates | Open |
| 1.3 | Alpaca price precision loss (`double → decimal` round-trip) and large trade size truncation | ✅ Fixed (2026-03-10): `GetInt64()` for sizes; timestamps reject on parse failure |
| 1.4 | No trade message deduplication — phantom trades inflate volume and VWAP | ✅ Fixed (2026-03-10): bounded sliding-window content deduplication |
| 1.5 | Completeness score locked at first-event rate; wrong for most symbols | Open |

**Category 2 — Resource Management & Stability:**

| # | Issue | Status |
|---|-------|--------|
| 2.1 | Memory leaks in `SequenceErrorTracker`, `GapAnalyzer`, `CompletenessScoreCalculator` — entries never evicted | Open |
| 2.2 | `GapAnalyzer.GetEffectiveConfig()` allocates a new record per event on the hot path | Open |
| 2.3 | `EventPipeline` sink flush has no timeout — hung sink stalls the entire pipeline | ✅ Fixed (2026-03-11): 60 s configurable flush timeout |
| 2.4 | `WebSocketConnectionManager` receive buffer has no size limit — OOM via oversized message | Open |
| 2.5 | `TryReconnectAsync` reconnection race condition — `_isReconnecting` check not inside semaphore | Open |
| 2.6 | `dataClient.ConnectAsync()` at startup has no timeout — application hangs forever on firewall drop | ✅ Fixed (2026-03-11): 30 s connection timeout with `ErrorCode.ConnectionTimeout` exit |

**Category 3 — Architectural Improvements:**

| # | Improvement | Status |
|---|-------------|--------|
| 3.1 | End-to-end trace context propagation via `System.Diagnostics.Activity` | Open |
| 3.2 | WebSocket provider base class (Polygon, NYSE, StockSharp share ~200-300 LOC lifecycle code) | Open |
| 3.3 | Decide F# strategy: deepen validation/calculation coverage or remove interop overhead | Open |
| 3.4 | Idempotent storage writes (bloom filter / hash-set dedup at sink layer) | Open |
| 3.5 | Config fail-fast vs. self-healing separation with severity levels | Open |
| 3.6 | Backpressure feedback loop — `TryPublish()` returns `bool`; providers can't throttle proactively | ✅ Fixed (2026-03-11): `TryPublishWithResult()` returning `PublishResult` enum |

**Category 4 — Observability & Operational Excellence:**

| # | Improvement | Status |
|---|-------------|--------|
| 4.1 | Alert-to-runbook linkage in Prometheus alert rule annotations | Open |
| 4.2 | Backpressure alerting is single-shot; sustained load produces only one warning | Open |
| 4.3 | WAL corruption alerting — invalid checksums are silently skipped | ✅ Fixed (2026-03-10): `WalCorruptionMode` enum (Skip/Alert/Halt) |
| 4.4 | Provider health dashboard — no unified traffic-light view across providers | ✅ Fixed (2026-03-11): `GET /api/providers/dashboard` with green/yellow/red status |

**Category 5 — Test Infrastructure:**

| # | Improvement | Status |
|---|-------------|--------|
| 5.1 | Timing-dependent skipped tests (`Task.Delay`-based synchronization) | ✅ Fixed (2026-03-11): deterministic `BlockingStorageSink` synchronization |
| 5.2 | Mock sinks support no error injection — pipeline failure modes untested | Open |
| 5.3 | No provider resilience tests (rate limits, malformed JSON, reconnection under loss) | Open |
| 5.4 | Property-based testing absent for domain models (serialization round-trips, ordering) | Open |

**Category 6-7 — Code Quality & Correctness by Construction:**

| # | Improvement | Status |
|---|-------------|--------|
| 6.1 | 42-service `Lazy<T>` singleton anti-pattern — untestable, tightly coupled | Open |
| 6.2 | `ServiceCompositionRoot` 50-100+ service registrations in one file | Open |
| 6.3 | Inconsistent error handling across providers | Open |
| 7.1 | Typed symbol keys (`Symbol`, `CanonicalSymbol`, `ProviderSymbol` value types) | Open |
| 7.2 | Sequence number domain separation (`PipelineSequence` vs. `ExchangeSequence`) | Open |
| 7.3 | Non-nullable event payloads via generic specialization | Open |

---

### High-Impact Improvements Brainstorm (Feb/Mar 2026)

> Source: `docs/evaluations/high-impact-improvements-brainstorm.md`
> Date: 2026-03-02 | Status: Active — Improvements Identified

**Top themes from deep codebase analysis:**

| Theme | Summary |
|-------|---------|
| Stringly-typed identifiers | `string Symbol`, `string Source`, provider IDs are bare strings; introduce value-object wrappers (`Symbol`, `CanonicalSymbol`, `ProviderId`, `Venue`, `StreamId`) |
| Provider registration unification | 3 separate creation mechanisms; consolidate via `ProviderFactory` + `DataSourceRegistry` |
| Endpoint response contract | HTTP endpoints lack versioned OpenAPI schemas; response shapes evolve silently |
| F# integration depth | 12 F# files vs. 652 C#; validation pipeline rarely called; interop overhead without coverage depth |
| WPF ViewModel extraction | All pages except Dashboard perform business logic in code-behind; untestable without UI thread |
| Test doubles | Mock sinks, fake providers, and stub calendars are not shared; each test reinvents infrastructure |

> For the full item-by-item analysis see `docs/evaluations/high-impact-improvements-brainstorm.md`.

---

### High-Value Low-Cost Improvements

> Source: `docs/evaluations/high-value-low-cost-improvements-brainstorm.md`
> Date: 2026-02-23 | Status: Active — Improvements Identified

**Context:** Identified after 94.3% of core improvements were complete (33/35 items). Focus on high-ROI, low-effort changes.

**Top categories:**

| Category | Key Improvements |
|----------|-----------------|
| Startup & Configuration Hardening | Credential validation in `PreflightChecker`; deprecation warning for legacy `DataSource` string config |
| Provider Resilience | Per-provider retry budget; explicit provider degradation thresholds configurable per environment |
| Developer Experience | Shared test double library; `[Theory]`-based provider message parsing tests |
| Operations | Prometheus alert annotations with runbook URLs; structured log correlation IDs |
| Code Simplification | Replace `Lazy<T>` singletons with DI; remove thin wrapper services |

> For the full item-by-item analysis see `docs/evaluations/high-value-low-cost-improvements-brainstorm.md`.

---

## Architecture Proposals

### Nautilus-Inspired Restructuring

> Source: `docs/evaluations/nautilus-inspired-restructuring-proposal.md`
> Date: 2026-03-01 | Last Reviewed: 2026-03-11 | Status: Partially Implemented

**Scope:** 7 structural changes + 5 procedural/code enhancements inspired by [nautechsystems/nautilus_trader](https://github.com/nautechsystems/nautilus_trader). All changes are backward-compatible.

**Implementation Status:**

| # | Proposal | Status |
|---|----------|--------|
| 1.1 | Unified Per-Provider Directories | ✅ Implemented — reorganized under `Adapters/` by vendor |
| 1.2 | Provider Template Scaffold | ⚠️ Partial — `ProviderTemplate.cs` factory exists; no `_Template/` scaffold dir |
| 1.3 | Co-located Provider Configuration | ❌ Not Started — configs remain in `Application/Config/` |
| 1.4 | Explicit Parsing Layer Per Provider | ❌ Not Started — parsing is still inline within provider clients |
| 1.5 | Per-Provider Factory Classes | ❌ Not Started — central `ProviderFactory.cs` unchanged |
| 1.6 | Consolidated Domain Enums | ✅ Implemented — 14 enums in `Contracts/Domain/Enums/` |
| 1.7 | Persistence Read/Write/Transform Separation | ⚠️ Partial — functional separation via `Archival/`, `Replay/`, `Export/`; no literal dirs |
| 2.1 | Component Lifecycle FSM Base Class | ❌ Not Started — no `ComponentBase`; abstract bases exist without FSM |
| 2.2 | Provider-Local Common Types | ⚠️ Partial — some providers have internal files; not universal |
| 2.3 | Module-Scoped Message Types | ⚠️ Partial — `Application/Commands/` exists; no pipeline/backfill command types |
| 2.4 | Credential Isolation at Provider Boundary | ⚠️ Partial — hybrid: `ICredentialResolver` + `ProviderCredentialResolver` |
| 2.5 | ArchUnitNET Dependency Rules | ❌ Not Started — no architecture boundary tests |

**Remaining work:** Items 1.3, 1.4, 1.5, 2.1, 2.5 are all open. The highest-value unopened items are the explicit parsing layer (1.4) and the component FSM base class (2.1).

---

### Assembly-Level Performance Opportunities

> Source: `docs/evaluations/assembly-performance-opportunities.md`
> Date: 2026-03-01 (viability assessment added 2026-03-17) | Status: **Roadmapped — Phase 16**

**Scope:** Identifies where .NET hardware intrinsics / SIMD can materially improve hot-path
performance. Assembly is **not** recommended for orchestration, I/O-bound, or framework-heavy
paths.

**Implementation roadmap:** See [Phase 16 in `ROADMAP.md`](ROADMAP.md#phase-16-assembly-level-performance-optimizations) for the full viability assessment, per-item delivery tasks, and exit criteria.

**Highest-potential candidates (7 evaluated; 6 viable for immediate delivery):**

| # | Area | Opportunity | Viability | Priority |
|---|------|-------------|-----------|----------|
| 1 | `MemoryMappedJsonlReader` — newline scan | `SearchValues<byte>` / AVX2 vectorized `\n` search | High | 1 |
| 2 | `MemoryMappedJsonlReader` — UTF-16 deferral | Pass `ReadOnlyMemory<byte>` slices; defer `GetString` | Medium | 2 |
| 3 | `DataQualityScoringService` — sequence extraction | `TryExtractSequenceUtf8` byte-span helper; `PipeReader` input | High | 3 |
| 4 | `LatencyHistogram` — bucket scan + sample ring | Binary search + fixed-size circular `LatencySample[]` | High | 4 |
| 5 | `EventBuffer.Drain(maxCount)` | Ring-buffer with head/tail indices replacing `GetRange+RemoveRange` | Medium | 5 |
| 6 | `DrainBySymbol` in-place partition | Symbol-interning via `SymbolTable`; int comparison; single-pass partition | High | 6 |
| 7 | `AnomalyDetector.SymbolStatistics` rolling stats | `Vector<double>` SIMD accumulation — **profile first** | Low–Medium | 7 |

> For the full analysis, implementation sketches, and correctness guidance see
> `docs/evaluations/assembly-performance-opportunities.md`.

---

### Next Frontier Brainstorm (March 2026)

> Source: `docs/evaluations/2026-03-brainstorm-next-frontier.md`
> Date: 2026-03-03 (updated 2026-03-12) | Status: Living Document

**Context:** Core platform at v1.6.2 with 94.3%+ of improvement items complete (33/35 core items, 6/8 extended items). This brainstorm targets new capabilities not yet covered by existing evaluations.

**Implementation Status by Area:**

**Area 1 — Data Intelligence & Analytics:**

| # | Proposal | Status |
|---|----------|--------|
| 1.1 | Cross-Symbol Correlation Engine (rolling correlation matrices, lead-lag) | 📝 Future |
| 1.2 | Microstructure Event Annotations (sweep, block, halt, spread spike) | 📝 Future |
| 1.3 | Cost-Per-Query Estimator for Backfill | ✅ Implemented — `BackfillCostEstimator`, `/api/backfill/cost-estimate` |

**Area 2 — Resilience & Operational Maturity:**

| # | Proposal | Status |
|---|----------|--------|
| 2.1 | Replay-Based Regression Testing (golden snapshot diffs) | 🔄 Partial — infrastructure in place; drift-canary CI job pending |
| 2.2 | Provider Health Scorecard with Trend Analysis | 🔄 Partial — current-state metrics implemented; historical snapshot + trend pending |
| 2.3 | Circuit Breaker Dashboard | ✅ Implemented — `CircuitBreakerStatusService`, `/api/resilience/circuit-breakers` |

**Area 3 — Developer & User Experience:**

| # | Proposal | Status |
|---|----------|--------|
| 3.1 | Data Catalog with Search & Discovery | 🔄 Partial — `StorageSearchService`, `DataBrowserPage` done; CLI shortcut + Gantt timeline pending |
| 3.2 | Provider Credential Rotation Automation | 🔄 Partial — expiry states and OAuth refresh implemented; proactive rotation pending |
| 3.3 | Interactive Backfill Planner | 🔄 Partial — cost estimator, calendar, checkpoints done; pause/resume and conflict UI pending |

**Area 4 — Data Integrity & Governance:**

| # | Proposal | Status |
|---|----------|--------|
| 4.1 | Data Lineage Visualization | ✅ Implemented — `DataLineageService` |
| 4.2 | Automated Data Retention Compliance Reports | ✅ Implemented — `RetentionComplianceReporter`, `/api/resilience/compliance-report` |
| 4.3 | Schema Evolution & Migration Toolkit | 🔄 Partial — `ISchemaUpcaster`, `SchemaVersionManager`, `SchemaUpcasterRegistry` done; lazy upcasting + migration CLI pending |

**Area 5 — Ecosystem & Integration:**

| # | Proposal | Status |
|---|----------|--------|
| 5.1 | Webhook & Notification Framework | 🔄 Partial — `AlertDispatcher`, webhooks implemented; user-defined JSON rule engine pending |
| 5.2 | Data Export to Cloud Storage (S3, GCS, Azure Blob) | 📝 Future |
| 5.3 | QuantConnect Lean Tight Integration | ✅ Implemented — `LeanIntegrationService`, `LeanAutoExportService`, `LeanSymbolMapper` |

**Area 6 — Performance & Scale:**

| # | Proposal | Status |
|---|----------|--------|
| 6.1 | Tiered Memory Buffer with Spill-to-Disk | 📝 Future |
| 6.2 | Parallel Backfill Orchestration | ✅ Implemented — `PriorityBackfillQueue`, `BackfillJobManager`, `BackfillWorkerService` |

**Area 7 — Architecture & Technical Debt (added 2026-03-12):**

| # | Proposal | Status |
|---|----------|--------|
| 7.1 | WebSocket Provider Base Class Consolidation | 📝 Future |
| 7.2 | End-to-End OpenTelemetry Trace Propagation | 📝 Future |
| 7.3 | WPF MVVM Full Migration (all pages beyond `DashboardViewModel`) | 📝 Future |

**Area 8 — New Capabilities (added 2026-03-12):**

| # | Proposal | Status |
|---|----------|--------|
| 8.1 | ML-Based Anomaly Detection (Isolation Forest / LSTM) | 📝 Future |
| 8.2 | Reference Data Integration (corporate actions, earnings, economic calendar) | 📝 Future |
| 8.3 | Multi-Instance Coordination (horizontal scaling, symbol partitioning) | 📝 Future |
| 8.4 | FIX Protocol / Drop-Copy Integration | 📝 Future |

---

## Cross-Cutting Findings

These themes recur across multiple evaluations and are now aligned to the current planning set.

### 1. Provider Registration Unification (Theme C)

Multiple evaluations (streaming, historical, desktop configurability) identified fragmented provider registration as a friction point. That foundation work is now substantially complete.

**Current state:** C1 (Unified Provider Registry) and C2 (Single DI Composition Path) are completed; remaining provider-architecture follow-through is concentrated in C3 (`WebSocketProviderBase` adoption for NYSE and StockSharp) and adjacent provider-local cleanup.

### 2. Operational SLO Standardization

Operational readiness and data quality evaluations both called for formal SLO definitions and tighter alert/runbook linkage.

**Current state:** The SLO registry and alert-to-runbook linkage are implemented. Remaining work is operational process hardening: release gates, rollback drills, and post-incident review discipline.

### 3. Unified Job / Orchestration Model

The ingestion orchestration and desktop UX evaluations both highlighted the need for a single job model spanning realtime and backfill workloads.

**Current state:** The core lifecycle is in place through `IngestionJobService`, checkpoint semantics, and resumable job endpoints. The remaining work is operator-facing UX consolidation and broader workflow integration in roadmap Phases 11-13.

### 4. Code Simplification Backlog

The simplification audit still identifies ~2,800-3,400 lines of removable or reducible code, especially around bare catches, dead code, and unnecessary async wrappers.

**Current state:** This remains active technical debt. It is best treated as rolling Phase 8 maintenance and should be pulled forward whenever adjacent feature work touches those areas.

### 5. Desktop Modernization Throughline

The desktop assessment set is consistent: infrastructure and coverage are much stronger than the remaining operator experience.

**Current state:** The strongest path forward is not net-new page sprawl; it is workflow consolidation, view-model/service extraction, stronger provenance/staleness signals, and alignment to the trading workstation migration blueprint.

---

## Archived Evaluations

These evaluations are superseded or no longer applicable:

| Document | Location | Reason Archived |
|----------|----------|----------------|
| `desktop-end-user-improvements-shortlist.md` | `docs/archived/` | All P0 items resolved; superseded by current desktop assessment |
| `desktop-ui-alternatives-evaluation.md` | `docs/archived/` | Decision made: WPF is sole desktop platform |
| `UWP_COMPREHENSIVE_AUDIT.md` | `docs/archived/` | UWP fully removed from codebase |
| `uwp-development-roadmap.md` | `docs/archived/` | UWP deprecated; WPF is sole client |
| `DUPLICATE_CODE_ANALYSIS.md` | `docs/archived/` | Analysis complete; most items resolved |
| `IMPROVEMENTS_2026-02.md` | `docs/archived/` | Consolidated into `IMPROVEMENTS.md` |
| `STRUCTURAL_IMPROVEMENTS_2026-02.md` | `docs/archived/` | Consolidated into `IMPROVEMENTS.md` |
| `REDESIGN_IMPROVEMENTS.md` | `docs/archived/` | Content merged into current docs |
| `CLEANUP_SUMMARY.md` | `docs/archived/` | All hygiene phases complete; historical reference only |
| `H3_DEBUG_CODE_ANALYSIS.md` | `docs/archived/` | Complete — no action required; historical reference only |
| `CLEANUP_OPPORTUNITIES.md` | `docs/archived/` | All platform cleanup items fully completed; historical reference only |
| `repository-cleanup-action-plan.md` | `docs/archived/` | All phases (1–6) complete; historical record of completed cleanup work |

See [`docs/archived/INDEX.md`](../archived/INDEX.md) for the full archive index.

---

## Source Document Index

All source documents that feed into this consolidation:

| Document | Path | Category | Status |
|----------|------|----------|--------|
| Real-Time Streaming Evaluation | `docs/evaluations/realtime-streaming-architecture-evaluation.md` | Evaluation | Current |
| Storage Architecture Evaluation | `docs/evaluations/storage-architecture-evaluation.md` | Evaluation | Current |
| Data Quality Monitoring Evaluation | `docs/evaluations/data-quality-monitoring-evaluation.md` | Evaluation | Current |
| Historical Data Providers Evaluation | `docs/evaluations/historical-data-providers-evaluation.md` | Evaluation | Current |
| Ingestion Orchestration Evaluation | `docs/evaluations/ingestion-orchestration-evaluation.md` | Evaluation | Current |
| Operational Readiness Evaluation | `docs/evaluations/operational-readiness-evaluation.md` | Evaluation | Current |
| Desktop End-User Improvements | `docs/archived/desktop-end-user-improvements.md` | Assessment | Archived |
| Desktop Provider Configurability | `docs/evaluations/windows-desktop-provider-configurability-assessment.md` | Assessment | Current |
| High-Impact Improvement Brainstorm (Mar 2026) | `docs/evaluations/high-impact-improvement-brainstorm-2026-03.md` | Brainstorm | Active |
| High-Impact Improvements Brainstorm | `docs/evaluations/high-impact-improvements-brainstorm.md` | Brainstorm | Active |
| High-Value Low-Cost Improvements | `docs/evaluations/high-value-low-cost-improvements-brainstorm.md` | Brainstorm | Active |
| Nautilus-Inspired Restructuring Proposal | `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | Proposal | Partially Implemented |
| Assembly-Level Performance Opportunities | `docs/evaluations/assembly-performance-opportunities.md` | Proposal | Proposal |
| Next Frontier Brainstorm (Mar 2026) | `docs/evaluations/2026-03-brainstorm-next-frontier.md` | Brainstorm | Living Document |
| Cleanup Summary | `docs/archived/CLEANUP_SUMMARY.md` | Audit | Archived |
| Cleanup Opportunities | `docs/archived/CLEANUP_OPPORTUNITIES.md` | Audit | Archived |
| Debug Code Analysis | `docs/archived/H3_DEBUG_CODE_ANALYSIS.md` | Audit | Archived |
| Further Simplification | `docs/audits/FURTHER_SIMPLIFICATION_OPPORTUNITIES.md` | Audit | Documented |
| UWP Comprehensive Audit | `docs/archived/UWP_COMPREHENSIVE_AUDIT.md` | Audit | Archived |
| Repository Cleanup Plan | `docs/archived/repository-cleanup-action-plan.md` | Plan | Archived |
| Config Consolidation Report | `CONFIG_CONSOLIDATION_REPORT.md` | Report | Complete |
| Desktop Improvements Exec Summary | `docs/evaluations/desktop-improvements-executive-summary.md` | Summary | Current |
| Desktop Platform Improvements Guide | `docs/evaluations/desktop-platform-improvements-implementation-guide.md` | Guide | Current |
| Desktop Improvement Shortlist | `docs/archived/desktop-end-user-improvements-shortlist.md` | Assessment | Archived |

---

*Last Updated: 2026-03-20*
