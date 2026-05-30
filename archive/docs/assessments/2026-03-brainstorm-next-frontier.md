# Brainstorm: Next Frontier Improvements

**Date:** 2026-03-12
**Status:** Living Document — updated to reflect v1.6.2 state
**Author:** Architecture Review
**Context:** Core platform is now at v1.6.2 with 94.3%+ of improvement items complete (33/35 core items, plus 6/8 extended items). This document has been updated to track the implementation status of each original proposal and to introduce a new set of ideas targeting the capabilities that remain genuinely unexplored. Items marked ✅ have been shipped; items marked 🔄 are partially implemented; items marked 📝 remain future work.

**Original context (2026-03-03):** Core platform is 94%+ complete. This brainstorm focuses on *new capabilities and systemic improvements* not yet covered by existing evaluations. The goal is to identify work that would meaningfully expand the project's value rather than polish existing features.

**Scoring:**
- **Impact**: How much this changes what users can do or how reliably the system operates
- **Effort**: T-shirt sizing (S = days, M = 1-2 weeks, L = 3+ weeks)
- **Risk**: Likelihood of destabilizing existing functionality

**Implementation status key:**
- ✅ **Implemented** — shipped and tested
- 🔄 **Partial** — framework or foundation in place; full capability pending
- 📝 **Future** — not yet started

---

## Area 1: Data Intelligence & Analytics

### 1.1 Cross-Symbol Correlation Engine — 📝 Future

**Problem:** The system collects data for many symbols independently but provides no way to analyze relationships between them. Quantitative researchers need correlation matrices, lead-lag analysis, and co-movement detection — they currently must export data and compute these externally.

**Proposal:** Add a `CorrelationService` that computes rolling correlation matrices across collected symbols using streaming updates. Expose via `/api/analytics/correlations` and surface in the desktop dashboard as a heatmap. Use the existing `TechnicalIndicatorService` pattern — compute on buffered data, no new storage required.

**Key deliverables:**
- Rolling Pearson correlation (configurable window: 5m, 1h, 1d)
- Lead-lag detection (which symbol moves first)
- Cluster detection (groups of correlated symbols)
- API endpoint + optional dashboard panel

**Impact:** High — turns the collector from a "data hose" into an analytical platform.
**Effort:** M
**Risk:** Low — purely additive, reads existing stored data.

---

### 1.2 Microstructure Event Annotations — 📝 Future

**Problem:** Raw trades and quotes are stored without higher-level annotations. A researcher looking at the data needs to manually identify events like: sweep orders, block trades, halt/resume sequences, NBBO changes, or unusual spread widening events.

**Proposal:** Add an `EventAnnotator` stage in the pipeline (after canonicalization, before storage) that tags events with microstructure annotations as metadata fields. These annotations are non-destructive — they add optional fields to the stored JSONL without altering the core schema.

**Candidate annotations:**
- `sweep`: trade that walks through multiple price levels
- `block`: trade above a configurable size threshold (e.g., 10,000 shares)
- `halt_related`: trade/quote near a known halt/resume boundary
- `spread_spike`: BBO spread exceeds N× its rolling average
- `imbalance_flip`: order book imbalance changes sign

**Impact:** High — significantly increases data value for downstream consumers.
**Effort:** M
**Risk:** Low — pipeline decorator pattern already proven by `CanonicalizingPublisher`.

---

### 1.3 Cost-Per-Query Estimator for Backfill — ✅ Implemented

**Status (2026-03-12):** `BackfillCostEstimator` is fully implemented in `src/Meridian.Application/Backfill/BackfillCostEstimator.cs`. It estimates API call counts, wall-clock time, and provider routing given a backfill request. Exposed at `/api/backfill/cost-estimate` and integrated with `--backfill --dry-run`. The `/api/backfill/run/preview` endpoint is also live.

**Problem:** Users run backfill operations without understanding the API cost implications. A `--backfill-symbols SPY,AAPL,MSFT,GOOGL --backfill-from 2020-01-01` command could consume thousands of API calls against rate-limited providers, and there's no visibility into this until the operation runs.

**Proposal:** Add a `BackfillCostEstimator` that, given a backfill request, estimates:
- Number of API calls required per provider
- Estimated wall-clock time (given rate limits)
- Whether the request will exhaust free-tier quotas
- Recommended provider ordering to minimize cost

Surface this via `--backfill --dry-run` (extend existing dry-run) and via the `/api/backfill/run/preview` endpoint (already exists as a stub).

**Impact:** Medium — prevents wasted API quota and user frustration.
**Effort:** S
**Risk:** Low — extends existing dry-run infrastructure.

---

## Area 2: Resilience & Operational Maturity

### 2.1 Replay-Based Regression Testing — 🔄 Partial

**Problem:** The system has 3,400+ unit tests but no way to validate that pipeline changes produce *identical output* for the same input data. A subtle change to canonicalization or filtering could silently alter stored data, and only careful manual inspection would catch it.

**Proposal:** Build a `RegressionTestHarness` that:
1. Replays a recorded market data fixture through the full pipeline (provider mock → EventPipeline → storage sink)
2. Compares output against a "golden" snapshot (stored as committed test data)
3. Diffs any changes and fails the test with a clear report

Use the existing `JsonlReplayer` and `MemoryMappedJsonlReader` as building blocks. Store golden fixtures in `tests/fixtures/golden/`.

**Status (2026-03-12):** Infrastructure is partially in place. `JsonlReplayer`, `MemoryMappedJsonlReader`, and `EventReplayService` are all implemented. The canonicalization golden fixture test suite (`CanonicalizationGoldenFixtureTests`) has 8 curated `.json` fixtures. **Remaining:** drift-canary CI job (J8) to automatically fail when provider feed schemas diverge from golden fixtures.

**Impact:** High — catches data-altering regressions that unit tests miss.
**Effort:** M
**Risk:** Low — test-only infrastructure, no production code changes.

---

### 2.2 Provider Health Scorecard with Trend Analysis — 🔄 Partial

**Problem:** `ProviderDegradationScorer` computes a point-in-time degradation score, but there's no historical tracking. Operators can't answer: "Has Polygon been getting worse this week?" or "Which provider has the best uptime this month?"

**Proposal:** Add a `ProviderHealthHistory` service that:
- Persists hourly snapshots of provider health metrics (latency p50/p95/p99, error rate, data completeness)
- Computes trend lines (improving/degrading/stable) using simple linear regression over configurable windows
- Surfaces via `/api/providers/health/trends` and a dashboard sparkline per provider
- Optionally emits alerts when a trend crosses a configurable threshold ("Polygon latency increasing 15% week-over-week")

**Status (2026-03-12):** `ProviderDegradationScorer` and `ProviderLatencyService` are both implemented and track current-state metrics (latency histograms, degradation scores). The `HealthCheckAggregator`, `AlertDispatcher`, and `AlertRunbookRegistry` provide centralized alert routing. **Remaining:** historical snapshot persistence and trend regression computation; dashboard sparklines.

**Impact:** High — moves from reactive to proactive provider management.
**Effort:** M
**Risk:** Low — purely additive, stores its own data.

---

### 2.3 Circuit Breaker Dashboard — ✅ Implemented

**Status (2026-03-12):** `CircuitBreakerStatusService` is implemented in `src/Meridian.Application/Monitoring/CircuitBreakerStatusService.cs`. It tracks state transitions (Open/Closed/HalfOpen), trip counts, last trip times, and cooldown state for each named circuit breaker. Circuit breaker state is surfaced via `/api/resilience/circuit-breakers` through `ResilienceEndpoints`. Structured log events are emitted on every state transition. The `CircuitBreakerCallbackRouter` wires Polly policy callbacks into `CircuitBreakerStatusService`.

**Original problem:** The `SharedResiliencePolicies` implement circuit breakers, but their state (open/closed/half-open) isn't visible to operators. When a provider hits a circuit breaker, the only evidence is buried in debug logs.

**Impact:** Medium — critical for operational debugging.
**Effort:** S *(completed)*
**Risk:** Low — read-only visibility into existing Polly policies.

---

## Area 3: Developer & User Experience

### 3.1 Data Catalog with Search & Discovery — 🔄 Partial

**Problem:** Stored data grows over time but there's no unified way to discover what's available. "Do I have AAPL trades from January 2025?" requires manually checking file paths or running CLI commands. The `StorageCatalogService` maintains an index, but it's not searchable or browsable in a user-friendly way.

**Proposal:** Build a `DataCatalog` experience:
- **CLI:** `--catalog search "AAPL trades 2025"` — natural-language-ish search over stored data
- **API:** `/api/catalog/search?q=AAPL&type=trades&from=2025-01-01` — structured search
- **Dashboard:** A dedicated "Data Browser" panel showing timeline bars per symbol (visual representation of data coverage)

The timeline visualization would immediately show gaps, coverage periods, and which providers contributed data for each symbol — like a Gantt chart of data availability.

**Status (2026-03-12):** `StorageSearchService` provides multi-level indexed search with faceted query support (symbol, date, data type, provider). `StorageCatalogService` maintains the file inventory. `DataBrowserPage` (WPF) provides a browse UI. **Remaining:** CLI `--catalog search` shortcut; Gantt-style timeline visualization in the web dashboard.

**Impact:** High — transforms data discoverability.
**Effort:** M
**Risk:** Low — reads existing catalog metadata.

---

### 3.2 Provider Credential Rotation Automation — 🔄 Partial

**Problem:** API keys expire or get rotated, requiring manual config updates and app restarts. The `ConfigWatcher` supports hot-reload but doesn't specifically handle credential rotation.

**Proposal:** Add credential lifecycle management:
- **Expiration tracking:** Parse expiration hints from provider responses (HTTP headers, error codes) and track days-until-expiry
- **Rotation alerts:** Emit warnings N days before suspected expiry: `"Alpaca API key may expire in ~7 days based on token metadata"`
- **Zero-downtime rotation:** Support a `ALPACA__SECRETKEY_NEW` env var pattern — when set, the system tests the new credential, and if valid, atomically switches over and clears the old reference
- **Audit trail:** Log all credential changes with timestamps (never log the credential values themselves)

**Status (2026-03-12):** `CredentialAuthStatus` enum includes `ExpiringSoon` and `Expired` states. `OAuthRefreshService` handles OAuth token refresh flows. `CredentialValidationService` tests credentials on startup. **Remaining:** Proactive expiry tracking from HTTP response headers; zero-downtime env-var rotation pattern; audit trail for credential changes.

**Impact:** Medium — reduces operational toil for production deployments.
**Effort:** M
**Risk:** Medium — credential handling requires careful security review.

---

### 3.3 Interactive Backfill Planner — 🔄 Partial

**Problem:** Backfill is a "fire and pray" operation. Users specify parameters and wait, with no visibility into progress until completion. The existing progress display helps, but there's no upfront planning step.

**Proposal:** Add an interactive backfill planner (CLI and web):
1. **Scope visualization:** Show calendar grid of what data already exists vs. what the backfill would fill
2. **Provider routing preview:** Show which provider handles which date range (based on capabilities and priority)
3. **Estimated duration:** Based on historical API response times and rate limits
4. **Incremental execution:** Allow starting a backfill and pausing/resuming it across sessions (checkpoint-based)
5. **Conflict resolution:** When backfilled data overlaps existing data, show a diff and let the user choose (keep existing, overwrite, merge)

**Status (2026-03-12):** Significant groundwork is complete. `BackfillCostEstimator` provides cost/duration estimates. `BackfillProgressTracker` reports per-symbol progress. `DataCalendarPage` (WPF) shows a calendar heat-map of collected dates. `BackfillScheduleManager` supports CRUD scheduling. `PriorityBackfillQueue` enables priority-ordered execution. **Remaining:** calendar scope visualization in the backfill planner (data-exists vs. to-be-filled overlay); checkpoint-based pause/resume across process restarts; interactive conflict resolution UI.

**Impact:** High — transforms backfill from "advanced CLI operation" to "guided workflow."
**Effort:** L
**Risk:** Low — extends existing backfill infrastructure without changing core logic.

---

## Area 4: Data Integrity & Governance

### 4.1 Data Lineage Visualization — ✅ Implemented

**Status (2026-03-12):** `DataLineageService` is implemented in `src/Meridian.Storage/Services/DataLineageService.cs` and tracks provenance chain per stored data file. Lineage information is accessible through the storage API.

**Original problem:** `DataLineageService` tracks provenance metadata but there's no way to visualize the lineage. When debugging a data quality issue, operators need to trace: "This AAPL trade at 14:32:05 came from Alpaca, was canonicalized at 14:32:05.003, passed bad-tick filter, and was stored at 14:32:05.012."

**Impact:** Medium — essential for debugging production data quality issues.
**Effort:** M *(completed)*
**Risk:** Low — uses existing lineage service as foundation.

---

### 4.2 Automated Data Retention Compliance Reports — ✅ Implemented

**Status (2026-03-12):** `RetentionComplianceReporter` is fully implemented in `src/Meridian.Storage/Services/RetentionComplianceReporter.cs`. It scans stored data against configured policies and generates JSON, Markdown, and CSV reports. Accessible via `/api/resilience/compliance-report` and `--retention-report` CLI flag. Integrated into `ScheduledArchiveMaintenanceService` for monthly automated runs.

**Original problem:** Organizations using this tool for regulated trading need proof that data retention policies are being followed. Currently there's no automated way to generate a compliance report.

**Impact:** Medium — critical for regulated environments, nice-to-have otherwise.
**Effort:** S *(completed)*
**Risk:** Low — read-only analysis of existing storage.

---

### 4.3 Schema Evolution & Migration Toolkit — 🔄 Partial

**Problem:** As the event schema evolves (new fields, renamed fields, type changes), historical data becomes incompatible. The `SchemaVersionManager` tracks versions but there's no automated migration path. Old JSONL files with schema v2 can't be seamlessly read by code expecting schema v5.

**Proposal:** Build a schema migration toolkit:
- **Upcasters:** Register `ISchemaUpcaster` implementations (interface already exists) that transform old events to current schema
- **Migration CLI:** `--migrate-schema --from v2 --to v5 --symbols AAPL` — rewrite stored data with schema upgrades
- **Lazy migration:** When reading old data (replay, export), apply upcasters on-the-fly without rewriting files
- **Compatibility matrix:** `/api/schema/compatibility` — show which stored data files are on which schema version

**Status (2026-03-12):** `ISchemaUpcaster` interface is defined in `src/Meridian.Contracts/Schema/`. `SchemaVersionManager` tracks versions in `src/Meridian.Storage/Archival/`. `SchemaUpcasterRegistry` exists in the pipeline layer for runtime upcaster registration. **Remaining:** lazy on-the-fly upcasting during replay/export reads; `/api/schema/compatibility` matrix endpoint; `--migrate-schema` CLI command.

**Impact:** High — prevents "data rot" as the project evolves.
**Effort:** M
**Risk:** Medium — rewriting stored data requires careful validation and backup.

---

## Area 5: Ecosystem & Integration

### 5.1 Webhook & Notification Framework — 🔄 Partial

**Problem:** `ConnectionStatusWebhook` and `DailySummaryWebhook` exist but are hardcoded patterns. Users can't define custom alerts like "notify me on Slack when AAPL spread exceeds 5 cents" or "email me when backfill completes."

**Proposal:** Add a general-purpose notification framework:
- **Rule engine:** Define conditions as JSON rules: `{ "event": "spread_exceeded", "symbol": "AAPL", "threshold": 0.05, "action": "webhook" }`
- **Channels:** Webhook (generic), Slack (formatted), Email (SMTP), Desktop notification (WPF toast)
- **Templates:** Customizable message templates with variable substitution
- **Endpoint:** `/api/notifications/rules` — CRUD for notification rules
- **CLI:** `--add-alert "AAPL spread > 0.05" --notify slack`

**Status (2026-03-12):** Strong foundation is in place. `AlertDispatcher` provides centralized alert publishing and subscription management. `AlertRunbookRegistry` maps alert rules to runbook references. `SloDefinitionRegistry` handles SLO-based alert thresholds. `ConnectionStatusWebhook` and `DailySummaryWebhook` provide webhook delivery. **Remaining:** user-defined JSON rule engine; Slack-formatted message templates; CRUD `/api/notifications/rules` endpoint; CLI `--add-alert` shortcut.

**Impact:** High — transforms passive data collection into active monitoring.
**Effort:** L
**Risk:** Low — additive system, no changes to core pipeline.

---

### 5.2 Data Export to Cloud Storage — 📝 Future

**Problem:** All data is stored locally. Users who want to feed data into cloud-based analytics (S3 + Athena, GCS + BigQuery, Azure Blob + Synapse) must manually copy files.

**Proposal:** Add cloud storage sink support:
- Implement `IStorageSink` for S3, GCS, and Azure Blob
- Use the existing `CompositeSink` to write simultaneously to local + cloud
- Support sync mode (near-real-time upload) and batch mode (periodic upload of completed files)
- Config: `"Storage": { "CloudSync": { "Provider": "S3", "Bucket": "my-market-data", "Prefix": "live/", "Mode": "batch" } }`

**Impact:** High — enables cloud-native analytics workflows.
**Effort:** L
**Risk:** Medium — cloud SDK dependencies, network failure handling.

---

### 5.3 QuantConnect Lean Tight Integration — ✅ Implemented

**Status (2026-03-12):** `LeanIntegrationService` is implemented in `src/Meridian.Ui.Services/` and wires into `LeanIntegrationPage` (WPF). `LeanAutoExportService` continuously exports collected data in Lean-compatible format. `LeanSymbolMapper` handles automatic symbol format translation. Custom Lean data types (`LeanDataTypes.cs`) are defined in `src/Meridian/Integrations/Lean/`. The `IDataProvider` implementation reads stored JSONL/Parquet as Lean data feeds.

**Original problem:** Lean integration exists (`Integrations/Lean/`) but the data flow is one-directional and manual. Users must export data, convert formats, and configure Lean separately.

**Impact:** Medium — high value for the quant research use case specifically.
**Effort:** L *(completed)*
**Risk:** Medium — depends on Lean Engine availability and version compatibility.

---

## Area 6: Performance & Scale

### 6.1 Tiered Memory Buffer with Spill-to-Disk — 📝 Future

**Problem:** The `EventPipeline` uses bounded channels with a fixed capacity. Under burst load (market open, news events), the pipeline applies backpressure and may drop events. The current approach is "size the buffer large enough" which wastes memory during quiet periods.

**Proposal:** Implement a tiered buffer:
1. **Hot tier:** In-memory bounded channel (existing, fast, ~10K events)
2. **Warm tier:** Memory-mapped file buffer (~100K events, disk-backed but fast)
3. **Spill policy:** When the hot tier is 80% full, start spilling to warm tier; drain warm tier during quiet periods

This extends the existing `EventPipelinePolicy` presets — add a `BurstTolerant` preset that enables the warm tier.

**Impact:** Medium — prevents data loss during burst periods without wasting memory.
**Effort:** M
**Risk:** Medium — changes to the critical path require careful benchmarking.

---

### 6.2 Parallel Backfill Orchestration — ✅ Implemented

**Status (2026-03-12):** Parallel backfill is fully implemented. `PriorityBackfillQueue` provides priority-ordered job execution. `BackfillJobManager` orchestrates concurrent symbol processing. `BackfillWorkerService` runs as a background service with configurable concurrency. `ProviderRateLimitTracker` enforces per-provider rate limits with exponential backoff and `Retry-After` header parsing. The `GapBackfillService` triggers automatic parallel repair jobs on reconnect.

**Original problem:** Backfill runs sequentially — one symbol at a time, one provider at a time. A 50-symbol backfill across 5 years takes hours even when rate limits would allow parallelism.

**Impact:** High — dramatically reduces backfill time for multi-symbol operations.
**Effort:** M *(completed)*
**Risk:** Medium — must respect rate limits carefully to avoid bans.

---

## Area 7: Architecture & Technical Debt (New — 2026-03-12)

### 7.1 WebSocket Provider Base Class Consolidation — 📝 Future

**Problem (C3, historical framing):** This brainstorm originally grouped Polygon, NYSE, and StockSharp together as if they were all raw WebSocket lifecycle candidates. The current repository state has since narrowed that problem: Polygon and NYSE were the real shared-lifecycle targets, while StockSharp is now treated as a connector-runtime exception rather than a direct `WebSocketProviderBase` migration candidate.

**Proposal:** Extract a `WebSocketProviderBase` abstract class that handles:
- Reconnect loop with exponential backoff
- Heartbeat sending and timeout detection
- Subscription restoration on reconnect
- Structured log events for connection lifecycle (connected, reconnecting, disconnected)

Polygon and NYSE inherit from this base; StockSharp instead keeps its connector-runtime path and any future consolidation there should be connector-oriented rather than a forced raw-WebSocket inheritance model.

**Key deliverables:**
- `WebSocketProviderBase` in `Infrastructure/Adapters/Core/`
- Migrate at least Polygon and NYSE to use it
- Existing `WebSocketConnectionManager` and `WebSocketResiliencePolicy` remain as utilities

**Impact:** Medium — eliminates reconnect-semantics drift across providers.
**Effort:** M
**Risk:** Medium — refactoring live connection code; requires thorough testing.

---

### 7.2 End-to-End OpenTelemetry Trace Propagation — 📝 Future

**Problem (G2 remainder):** The OpenTelemetry framework is wired (`TracedEventMetrics`, `Meridian.Pipeline` meter, OTLP registration), but `Activity` context is not propagated from provider receive loops through the `EventPipeline` consumer to the storage write call. Distributed traces appear as disconnected spans with no parent-child relationship across the pipeline boundaries.

**Proposal:** Wire explicit `Activity` propagation:
1. Provider receive loop creates a root span per batch: `Activity("provider.receive", tags: { provider, symbol })`
2. `EventPipeline` consumer propagates the span context through the channel write
3. Storage sink completes the span with outcome and bytes-written tags
4. Add correlation ID (derived from the trace ID) to all `ILogger` structured log entries

**Key deliverables:**
- Correlation IDs in every log entry (searchable in Grafana Loki, Seq, etc.)
- Jaeger/Zipkin export documentation for visual trace exploration
- End-to-end latency breakdowns by pipeline stage visible in Grafana

**Impact:** High — enables root-cause diagnosis of latency regressions and data-loss events.
**Effort:** M
**Risk:** Low — framework already in place; this is wiring, not new dependencies.

---

### 7.3 WPF MVVM Full Migration — 📝 Future

**Problem:** While `DashboardViewModel` has been extracted following the `BindableBase`/MVVM pattern, all other WPF pages still perform business logic directly in code-behind (event handlers call services, update UI properties, manage timers). This violates the MVVM principle documented in ADR-017 and makes pages untestable without a UI thread.

**Proposal:** Extract ViewModels for all remaining pages, following the `DashboardViewModel` pattern:
- Create `*ViewModel` class implementing `BindableBase`
- Move all `LoadDataAsync`, service calls, and timer management into the ViewModel
- Keep code-behind to pure UI wiring (`ViewModel = new FooViewModel(services...)`)
- Add ViewModel unit tests (no WPF thread dependency)

**Priority order (by complexity):** `BackfillPage`, `SymbolsPage`, `SettingsPage`, `DataQualityPage`, then remaining pages.

**Impact:** Medium — improves testability and separates concerns.
**Effort:** L
**Risk:** Low — incremental page-by-page migration; no runtime behavior changes.

---

## Area 8: New Capabilities (New — 2026-03-12)

### 8.1 Machine Learning-Based Anomaly Detection — 📝 Future

**Problem:** The current `AnomalyDetector` uses rule-based thresholds (price/volume outlier beyond N standard deviations). This approach misses regime changes (what's "normal" for AAPL at market open differs from midday), generates false positives during high-volatility events, and requires manual threshold tuning per symbol.

**Proposal:** Integrate an online learning anomaly detector:
- Use an Isolation Forest or LSTM-based model trained on rolling windows of each symbol's microstructure features (spread, volume, trade rate, imbalance)
- Score each incoming event in real-time (<1 ms budget) without blocking the pipeline
- Expose anomaly scores as an optional annotation field (complements 1.2 Event Annotations)
- Provide a calibration API: `/api/quality/anomaly-calibrate/{symbol}` — trigger retraining on a baseline window

Use `Microsoft.ML` (already available in the .NET ecosystem) to keep the dependency tree minimal.

**Key deliverables:**
- `MlAnomalyDetector` as a drop-in replacement for `AnomalyDetector`
- Per-symbol model files persisted in the storage tier
- A/B comparison mode: run both detectors and log disagreements

**Impact:** High — dramatically reduces false positives and catches subtle structural regime breaks.
**Effort:** L
**Risk:** Medium — ML inference latency must stay under hot-path budget; model drift requires monitoring.

---

### 8.2 Reference Data Integration (Corporate Actions & Fundamentals) — 📝 Future

**Problem:** Stored price data lacks context about the events that drive it. A price drop in AAPL on a specific day could be a stock split, a dividend ex-date, an earnings miss, or a market-wide decline. Without reference data, downstream backtesting engines must independently source and reconcile this context.

**Proposal:** Add a lightweight reference data layer:
- **Corporate actions:** Integrate split/dividend data from a free provider (Yahoo Finance, Stooq) and store as structured events alongside tick data
- **Earnings calendar:** Fetch earnings dates and consensus estimates; annotate bars within ±2 sessions of earnings with an `earnings_adjacent` flag
- **Economic calendar:** FOMC dates, CPI/PPI release times — useful for annotating high-volatility periods
- **Storage:** Side-car JSONL files per symbol per year (`AAPL_corporate_actions_2025.jsonl`) using the existing storage naming convention
- **API:** `/api/reference/{symbol}/actions?from=2025-01-01` — expose reference data alongside market data

**Impact:** High — enriches stored data for backtesting and strategy research without requiring external data joins.
**Effort:** M
**Risk:** Low — purely additive; corporate action data is widely available for free.

---

### 8.3 Multi-Instance Coordination (Horizontal Scaling) — 📝 Future

**Problem (H2):** The system is designed as a single-instance service. Running two instances against the same storage directory would produce duplicate events, corrupt the WAL, and race on file writes. For high-symbol-count deployments (500+ symbols), a single instance becomes a bottleneck.

**Proposal:** Add optional multi-instance coordination:
- **Leader election:** Use a lightweight file-lock or Redis-based lease to elect a primary instance; secondary instances take over symbol subsets on primary failure
- **Symbol partitioning:** Divide the symbol list across instances (consistent hash by symbol); each instance owns its shard
- **Shared storage fence:** WAL writes acquire exclusive byte-range locks so concurrent file writes are safe
- **Health broadcast:** Instances publish heartbeats to a shared coordination channel; each can detect peer failures

This feature would be opt-in via `"Coordination": { "Mode": "multi-instance", "InstanceId": "node-1" }` — single-instance mode (the default) is unchanged.

**Impact:** High — enables horizontal scaling for large deployments.
**Effort:** L
**Risk:** High — coordination logic is complex and failure modes are subtle; requires extensive testing.

---

### 8.4 FIX Protocol / Drop-Copy Integration — 📝 Future

**Problem:** Institutional users running algorithmic trading strategies need to correlate collected market data with their own order and execution data. Currently there's no way to ingest order/execution events from a FIX drop-copy session into the same storage as market data.

**Proposal:** Add a FIX drop-copy listener as an optional data source:
- Implement a lightweight FIX 4.2/4.4 session acceptor that receives execution reports (8=Execution Report, 35=8) and order cancellations
- Parse FIX fields into a canonical `ExecutionEvent` type aligned with the existing `MarketEvent` schema
- Store execution events alongside market data in the `BySymbol` storage layout
- Expose via `/api/executions/{symbol}` for correlation with tick data
- Use `QuickFIX/n` (open-source .NET FIX engine) to avoid implementing the FIX session layer from scratch

**Impact:** High — critical for institutional buy-side users who want unified market + execution data.
**Effort:** L
**Risk:** Medium — FIX connectivity requires network access to broker infrastructure; session configuration is complex.

---

## Summary: Current State & Recommendations (Updated 2026-03-12)

### What has shipped since the original brainstorm (2026-03-03)

| Proposal | Status | Notes |
|----------|--------|-------|
| 1.3 Cost-Per-Query Estimator | ✅ Done | `BackfillCostEstimator` + `/api/backfill/cost-estimate` |
| 2.3 Circuit Breaker Dashboard | ✅ Done | `CircuitBreakerStatusService` + `ResilienceEndpoints` |
| 4.1 Data Lineage Visualization | ✅ Done | `DataLineageService` with API surface |
| 4.2 Retention Compliance Reports | ✅ Done | `RetentionComplianceReporter` + scheduled runs |
| 5.3 Lean Tight Integration | ✅ Done | `LeanAutoExportService`, `LeanSymbolMapper`, WPF page |
| 6.2 Parallel Backfill Orchestration | ✅ Done | `PriorityBackfillQueue` + `BackfillJobManager` |

### Partially complete (foundations shipped, full capability pending)

| Proposal | What's Done | Remaining |
|----------|-------------|-----------|
| 2.1 Replay-Based Regression Testing | `JsonlReplayer`, golden canonicalization fixtures | Drift-canary CI job (J8) |
| 2.2 Provider Health Trend Analysis | `ProviderDegradationScorer`, `LatencyHistogram` | Historical snapshot persistence, trend regression, sparklines |
| 3.1 Data Catalog with Search | `StorageSearchService`, `DataBrowserPage` | CLI shortcut; web dashboard timeline visualization |
| 3.2 Credential Rotation | `CredentialAuthStatus`, `OAuthRefreshService` | Proactive expiry tracking; zero-downtime env-var swap |
| 3.3 Interactive Backfill Planner | `BackfillCostEstimator`, `DataCalendarPage`, progress | Scope overlay; checkpoint resume; conflict resolution UI |
| 4.3 Schema Migration Toolkit | `ISchemaUpcaster`, `SchemaVersionManager`, registry | Lazy on-read upcasting; compatibility matrix API |
| 5.1 Notification Framework | `AlertDispatcher`, `AlertRunbookRegistry`, webhooks | User-defined JSON rule engine; Slack channel; CRUD API |

### Top 5 Recommendations for Next Sprint

Ranked by impact-to-effort ratio considering current project state:

| Rank | Proposal | Impact | Effort | Why Now? |
|------|----------|--------|--------|----------|
| 1 | 7.2 End-to-End Trace Propagation | High | M | Framework ready; pure wiring; unblocks latency diagnosis |
| 2 | 2.1 Drift-Canary CI Job (J8) | High | S | Last piece of canonicalization hardening; purely CI |
| 3 | 5.1 Notification Rule Engine | High | M | AlertDispatcher already deployed; adds major user value |
| 4 | 7.1 WebSocket Base Class (C3) | Medium | M | Eliminates maintenance risk across the active WebSocket-style providers; StockSharp now sits outside this direct migration path |
| 5 | 8.2 Reference Data Integration | High | M | High researcher value; safe additive change |

### Biggest game-changers (higher effort, transformative)

- **8.1 ML-Based Anomaly Detection** — upgrades data quality from rule-based to adaptive
- **8.3 Multi-Instance Coordination** — unlocks horizontal scaling for large symbol counts
- **5.2 Cloud Storage Sinks** — enables cloud-native analytics pipelines (S3, GCS, Azure Blob)
- **1.1 Cross-Symbol Correlation Engine** — turns the system into an analytical platform, not just a collector
