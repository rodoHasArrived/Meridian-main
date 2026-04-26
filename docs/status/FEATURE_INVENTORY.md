# Meridian — Feature Inventory

**Version:** 1.7.2
**Date:** 2026-04-26
**Purpose:** Comprehensive inventory of every functional area, its current implementation status, and the remaining work required to reach full implementation.

Use this document alongside [`ROADMAP.md`](ROADMAP.md) (delivery waves and sequencing), [`IMPROVEMENTS.md`](IMPROVEMENTS.md) (normalized improvement/backlog tracking), and [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) (consolidated non-assembly execution backlog).

---

## Legend

| Symbol | Meaning |
| -------- | --------- |
| ✅ | Fully implemented and tested |
| ⚠️ | Partially implemented — functional with caveats |
| 🔑 | Requires external credentials / build flag |
| 🔄 | Framework in place, one or more sub-features pending |
| 📝 | Planned, not yet started |

---

## 1. Core Infrastructure

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Event Pipeline (`System.Threading.Channels`) | ✅ | Bounded channel, backpressure, 100 K capacity, nanosecond timing |
| Injectable `IEventMetrics` | ✅ | Static dependency removed; `TracedEventMetrics` decorator available |
| `CompositeSink` fan-out | ✅ | Per-sink fault isolation; JSONL + Parquet simultaneously |
| Write-Ahead Log (WAL) | ✅ | SHA-256 checksums, streaming recovery, uncommitted-size warnings |
| Provider Registry & DI | ✅ | `[DataSource]` scanning, `ProviderRegistry`, `ServiceCompositionRoot` |
| Config Validation Pipeline | ✅ | `ConfigValidationPipeline` with composable stages; obsoletes `ConfigValidationHelper` |
| Graceful Shutdown | ✅ | `GracefulShutdownService`, provider disconnect, flush-to-disk before exit |
| Category-accurate exit codes | ✅ | `ErrorCode.FromException()` maps to codes 3–7 for CI/CD differentiation |
| Dry-run mode (`--dry-run`) | ✅ | Full validation without starting collection; `--dry-run --offline` skips connectivity |
| Configuration hot-reload (`--watch-config`) | ✅ | `ConfigWatcher` triggers live config update |
| Persistent deduplication ledger | ✅ | `PersistentDedupLedger`; disk-backed dedup tracking that survives restarts |
| Ingestion job management | ✅ | `IngestionJobService`; per-symbol ingestion job lifecycle, status, and scheduling |

---

## 2. Streaming Data Providers

| Provider | Status | Remaining Work |
| ---------- | -------- | ---------------- |
| **Alpaca** | ✅ | Credential validation, automatic resubscription on reconnect, quote routing |
| **Interactive Brokers** | 🔑 | Real runtime requires `-p:DefineConstants=IBAPI` plus the official `IBApi` surface; non-`IBAPI` builds expose simulation/setup guidance instead of broker connectivity |
| **Polygon** | ⚠️ | Real connection when API key present; committed replay fixtures close the parser path, while live reconnect/websocket throttling remain explicitly runtime-bounded |
| **Robinhood** | 🔑 | Unofficial broker-backed quote polling plus brokerage reads/orders, options chains, and historical daily bars when `ROBINHOOD_ACCESS_TOKEN` is present; runtime bounds are tracked under `artifacts/provider-validation/robinhood/2026-04-09/` |
| **NYSE** | 🔑 | Requires NYSE Connect credentials; L1/shared-lifecycle evidence is strong, with auth/rate-limit/depth bounds tracked under `artifacts/provider-validation/nyse/2026-04-09/` |
| **StockSharp** | 🔑 | Requires StockSharp connector-specific credentials + connector type config. Runtime evidence for the validated adapter set lives under `artifacts/provider-validation/stocksharp/2026-04-09/`, while the active Wave 1 gate keeps StockSharp outside the core provider set as future validation inventory; crypto connectors remain optional/example paths |
| **Failover-Aware Client** | ✅ | `FailoverAwareMarketDataClient` with `ProviderDegradationScorer`, per-provider health |
| **Streaming Failover Service** | ✅ | `StreamingFailoverService` + `StreamingFailoverRegistry`; runtime failover orchestration with configurable rules and health evaluation |
| **IB Simulation Client** | ✅ | `IBSimulationClient` for testing without live connection |
| **NoOp Client** | ✅ | `NoOpMarketDataClient` for dry-run / test harness scenarios |

Provider validation matrix and evidence links now live in `docs/status/provider-validation-matrix.md`, `docs/providers/provider-confidence-baseline.md`, and `artifacts/provider-validation/`, with `scripts/dev/run-wave1-provider-validation.ps1` as the offline gate runner.

### Remaining work to reach full provider coverage

- **Polygon**: Validate WebSocket message parsing against Polygon v2 feed schema (trades, quotes, aggregates, status messages). Add round-trip integration test with a recorded WebSocket session replay.
- **Robinhood**: Quote polling, historical bars, symbol search, options chains, and brokerage paths are in code; remaining work is explicit runtime evidence for the bounded scenarios under `artifacts/provider-validation/robinhood/2026-04-09/`.
- **StockSharp**: Runtime connector guidance and unsupported-path recovery messaging are now aligned; remaining work is moving the validated adapter set from bounded to captured runtime evidence without broadening the Wave 1 set.
- **IB**: Scripted setup instructions, version-bound tests, and a compile-only smoke-build path now exist; remaining work is keeping the vendor-runtime path validated against real IB API releases and entitlements.

---

## 3. Historical Backfill Providers

| Provider | Status | Notes |
| ---------- | -------- | ------- |
| Alpaca | ✅ | Daily bars, trades, quotes; credentials required |
| Polygon | ✅ | Daily bars and aggregates; API key required |
| Robinhood | 🔑 | Daily bars via unofficial Robinhood API; access token required |
| Tiingo | ✅ | Daily bars; token required |
| Yahoo Finance | ✅ | Daily bars; unofficial API, no credentials |
| Stooq | ✅ | Daily bars; free, no credentials |
| Finnhub | ✅ | Daily bars; token required |
| Alpha Vantage | ✅ | Daily bars; API key required |
| FRED Economic Data | 🔑 | Economic time series mapped to synthetic daily bars by series ID; API key required |
| Nasdaq Data Link (Quandl) | ✅ | Various; API key required |
| Interactive Brokers | 🔑 | Full implementation behind `IBAPI`; smoke builds remain compile-only and are not operator-ready historical access |
| StockSharp | ✅ | Via StockSharp connectors; runtime/historical coverage depends on connector setup, package surface, and entitlement |
| **Composite Provider** | ✅ | Priority-based fallback chain, rate-limit tracking, per-provider health |
| **Gap Backfill Service** | ✅ | `GapBackfillService` triggered on reconnect; uses `WebSocketReconnectionHelper` gap window with Wave 1 repo-backed proof in `GapBackfillServiceTests` |
| **Backfill Rate Limiting** | ✅ | `ProviderRateLimitTracker` per provider; exponential backoff with `Retry-After` parsing |
| **Backfill Scheduling** | ✅ | Cron-based `ScheduledBackfillService`; `BackfillScheduleManager` with CRUD API |
| **Backfill Progress Reporting** | ✅ | `BackfillProgressTracker`, per-symbol %, exposed at `/api/backfill/progress` |
| **Priority Backfill Queue** | ✅ | `PriorityBackfillQueue`, `BackfillJobManager`, `BackfillJob`; priority-ordered job execution |
| **Gap Analysis (Infrastructure)** | ✅ | `DataGapAnalyzer`, `DataGapRepair`, `DataQualityMonitor`; storage scan, gap detection, automated repair |

---

## 4. Symbol Search

| Provider | Status | Notes |
| ---------- | -------- | ------- |
| Alpaca | ✅ | `AlpacaSymbolSearchProviderRefactored`; US equities + crypto |
| Robinhood | ✅ | `RobinhoodSymbolSearchProvider`; public instruments API, no authentication required |
| Finnhub | ✅ | `FinnhubSymbolSearchProviderRefactored`; US + international |
| Polygon | ✅ | `PolygonSymbolSearchProvider`; US equities |
| OpenFIGI | ✅ | `OpenFigiClient`; global instrument ID mapping |
| EDGAR | ✅ | `EdgarSymbolSearchProvider`; SEC `company_tickers.json` cache for US company lookup and issuer detail enrichment |
| StockSharp | ✅ | `StockSharpSymbolSearchProvider`; multi-exchange |
| **Symbol Import/Export** | ✅ | CSV import/export via `SymbolImportExportService`; portfolio import |
| **Symbol Registry** | ✅ | `CanonicalSymbolRegistry` with persistence; `SymbolRegistryService` |
| **Symbol Normalization** | ✅ | `SymbolNormalization` utility; PCG-PA, BRK.A, ^GSPC, =SPX patterns |

---

## 5. Data Canonicalization

| Component | Status | Notes |
| ----------- | -------- | ------- |
| Design document & field audit | ✅ | `docs/architecture/deterministic-canonicalization.md` |
| `MarketEvent` canonical fields | ✅ | `CanonicalSymbol`, `CanonicalizationVersion`, `CanonicalVenue`, `EffectiveSymbol` |
| `EventCanonicalizer` implementation | ✅ | Symbol resolution, venue normalization, typed payload extraction |
| `ConditionCodeMapper` — Alpaca (17 codes) | ✅ | CTA plan codes → `CanonicalTradeCondition`; `FrozenDictionary` |
| `ConditionCodeMapper` — Polygon (19 codes) | ✅ | SEC numeric codes → canonical |
| `ConditionCodeMapper` — IB (8 codes) | ✅ | IB field codes → canonical |
| `VenueMicMapper` — Alpaca (29 venues) | ✅ | Text names → ISO 10383 MIC |
| `VenueMicMapper` — Polygon (17 venues) | ✅ | Numeric IDs → MIC |
| `VenueMicMapper` — IB (17 venues) | ✅ | Routing names → MIC |
| `CanonicalizingPublisher` decorator | ✅ | Wraps `IMarketEventPublisher`; dual-write mode; lock-free metrics |
| Canonicalization metrics & API endpoints | ✅ | `/api/canonicalization/status`, `/parity`, `/parity/{provider}`, `/config` |
| Golden fixture test suite | Complete | 8 curated `.json` fixtures + `CanonicalizationGoldenFixtureTests`; PR checks now emit a canonicalization drift report and a manual maintenance workflow supports fixture upkeep |

### Remaining work

- Continue expanding fixture coverage as new providers or venue/condition edge cases are onboarded.

---

## 6. Storage & Data Management

| Feature | Status | Notes |
| --------- | -------- | ------- |
| JSONL storage sink | ✅ | Append-only, gzip-compressed, configurable naming conventions |
| Parquet storage sink | ✅ | Columnar, compressed; enabled via `EnableParquetSink` config. Wave 1 repo-backed tests now cover L2 snapshot flush, final dispose flush, and atomic temp-file cleanup |
| Tiered storage (hot/warm/cold) | ✅ | `TierMigrationService` with configurable retention per tier |
| Scheduled archive maintenance | ✅ | `ScheduledArchiveMaintenanceService`; tasks: integrity, orphan cleanup, index rebuild, compression |
| Portable data packaging | ✅ | `PortableDataPackager`; ZIP/tar.gz with manifest, checksums, SQL loaders |
| Package import | ✅ | `--import-package`, merge mode |
| Package validation | ✅ | SHA-256 integrity, schema compatibility checks |
| Storage quota enforcement | ✅ | `QuotaEnforcementService`; configurable max total and per-symbol limits |
| Data lifecycle policies | ✅ | `LifecyclePolicyEngine`; tag-based retention policies |
| Storage checksums | ✅ | `StorageChecksumService`; per-file SHA-256 tracking |
| Metadata tagging | ✅ | `MetadataTagService`; background save pattern; tag-based search |
| Analysis export (JSONL/Parquet/Arrow/XLSX/CSV) | ✅ | `AnalysisExportService`; configurable format, symbol filter, date range |
| Storage catalog | ✅ | `StorageCatalogService`; file inventory, symbol listing |
| Event replay | ✅ | `JsonlReplayer`, `MemoryMappedJsonlReader`, `EventReplayService`; pause/resume/seek; CLI `--replay` |
| File permissions service | ✅ | `FilePermissionsService`; cross-platform directory permission checks |
| Data lineage tracking | ✅ | `DataLineageService`; provenance chain per data file |
| Data quality scoring | ✅ | `DataQualityScoringService`; per-symbol quality scores |

---

## 7. Data Quality Monitoring

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Completeness scoring | ✅ | `CompletenessScoreCalculator`; expected vs. received events |
| Gap analysis | ✅ | `GapAnalyzer`; liquidity-adjusted severity (Minor → Critical) |
| Anomaly detection | ✅ | `AnomalyDetector`; price/volume outliers |
| Sequence error tracking | ✅ | `SequenceErrorTracker`; out-of-order and duplicate event detection |
| Cross-provider comparison | ✅ | `CrossProviderComparisonService` |
| Latency distribution | ✅ | `LatencyHistogram`; p50/p90/p99 tracking |
| Data freshness SLA monitoring | ✅ | `DataFreshnessSlaMonitor`; configurable thresholds, violation API |
| Quality report generation | ✅ | `DataQualityReportGenerator`; daily/on-demand reports |
| Dropped event audit trail | ✅ | `DroppedEventAuditTrail`; JSONL log + `/api/quality/drops` API |
| Bad tick filter | ✅ | `BadTickFilter`; placeholder price detection, spread sanity |
| Tick size validation | ✅ | `TickSizeValidator` |
| Spread monitoring | ✅ | `SpreadMonitor`; bid/ask spread alerts |
| Clock skew estimation | ✅ | `ClockSkewEstimator` |
| Timestamp monotonicity checking | ✅ | `TimestampMonotonicityChecker` |
| Backpressure alerts | ✅ | `BackpressureAlertService`; `/api/backpressure` endpoint |
| Provider degradation scoring | ✅ | `ProviderDegradationScorer`; composite health from latency, errors, reconnects |
| Liquidity profile | ✅ | `LiquidityProfileProvider`; symbol-level liquidity classification for gap severity calibration |
| SLO definition registry | ✅ | `SloDefinitionRegistry`; runtime SLO definitions, compliance scoring, alert threshold mapping |

---

## 8. API Surface (HTTP)

| Area | Routes | Status |
| ------ | -------- | -------- |
| Status & health | `/api/status`, `/api/health`, `/healthz`, `/readyz`, `/livez` | ✅ |
| Configuration | `/api/config/*` (8 endpoints) | ✅ |
| Providers | `/api/providers/*`, `/api/connections` | ✅ |
| Failover | `/api/failover/*` | ✅ |
| Backfill | `/api/backfill/*` (13 endpoints) | ✅ |
| Quality | `/api/quality/*`, `/api/sla/*` | ✅ |
| Maintenance | `/api/maintenance/*` | ✅ |
| Storage | `/api/storage/*` | ✅ |
| Symbols | `/api/symbols/*` | ✅ |
| Live data | `/api/live/*` | ✅ |
| Export | `/api/export/*` | ✅ |
| Packaging | `/api/packaging/*` | ✅ |
| Canonicalization | `/api/canonicalization/*` | ✅ |
| Diagnostics | `/api/diagnostics/*` | ✅ |
| Subscriptions | `/api/subscriptions/*` | ✅ |
| Historical | `/api/historical/*` | ✅ |
| Sampling | `/api/sampling/*` | ✅ |
| Alignment | `/api/alignment/*` | ✅ |
| IB-specific | `/api/ib/*` | ✅ |
| Direct lending | `/api/loans/*` | ✅ |
| Workstation and reconciliation | `/api/workstation/*` | ✅ |
| Metrics (Prometheus) | `/api/metrics` | ✅ |
| SSE stream | `/api/events/stream` | ✅ |
| OpenAPI / Swagger | `/swagger` | ✅ |
| API authentication | `X-Api-Key` header only (no query-string auth) | ✅ |
| Rate limiting | 120 req/min per key, sliding window | ✅ |
| **Total route constants** | **402** | **0 stubs remaining** |

### OpenAPI annotations

| Endpoint family | Typed `Produces<T>` | Descriptions | Status |
| ----------------- | --------------------- | -------------- | -------- |
| Status | ✅ | ✅ | ✅ |
| Health | ✅ | ✅ | ✅ |
| Config | ✅ | ✅ | ✅ |
| Backfill / Schedules | ✅ | ✅ | ✅ |
| Providers / Extended | ✅ | ✅ | ✅ |
| All other families | ✅ | ✅ | ✅ |

---

## 9. Retained Local Web/API Surface

This surface remains useful for local diagnostics, API-backed workflows, and support tooling. It is
not the primary operator shell; WPF owns current workstation delivery.

| Feature | Status | Notes |
| --------- | -------- | ------- |
| HTML dashboard (auto-refreshing) | ✅ | `HtmlTemplateGenerator`; SSE-powered live updates |
| Server-Sent Events stream | ✅ | `/api/events/stream`; 2-second push cycle |
| Configuration wizard UI | ✅ | Interactive provider setup, credential entry, symbol config |
| Backfill controls | ✅ | Provider select, symbol list, date range, run/preview |
| Symbol management | ✅ | Add/remove symbols, status per symbol |
| Provider comparison table | ✅ | Feature matrix across all providers |
| Options chain display | ✅ | Derivatives configuration and data display |

---

## 10. WPF Desktop Application

> **Status:** Code present in `src/Meridian.Wpf/` and `tests/Meridian.Wpf.Tests/`, both included in the solution build. Builds full WPF desktop app on Windows; produces a CI-compatible stub on Linux/macOS.

### Shell & Navigation (Complete baseline)

- Workspace model now persists built-in `Research`, `Trading`, `Data Operations`, and `Governance` workspaces, including legacy workspace ID migration for older saved sessions.
- Main workstation shell is metadata-driven through `ShellNavigationCatalog`, with workspace home pages, primary/secondary/overflow navigation tiers, recent pages, command-palette search keywords, and related-workflow links.
- Workspace home pages now act as shell-first operator launchpads (`ResearchShell`, `TradingShell`, `DataOperationsShell`, `GovernanceShell`) instead of a long page-directory entry model.
- Desktop launch and workflow automation now normalize page/deep-link startup actions through `DesktopLaunchArguments`, forward secondary launch arguments to the primary instance through the single-instance named pipe, keep `ShellAutomationState` available as a hidden-but-present page marker, and split isolated restore/build arguments so shortcuts and screenshot/manual workflows target the same canonical workspace tags operators use without target-framework asset drift.
- Command palette (`Ctrl+K`), keyboard shortcuts, workspace-tile switching, and governance/fund-ops aliases keep low-frequency pages reachable without promoting them to top-level roots.
- Workspace shell context strips now standardize scope, environment, freshness, review-state, alert, and currency cues across the four workstation shells.
- `TradingWorkspaceShellPage` now includes a desk briefing hero that projects current desk focus, readiness tone, next handoff, and primary/secondary actions from active-run, workflow-summary, and shared operator-readiness inputs.
- `ResearchWorkspaceShellPage` now includes a research desk briefing hero that projects market briefing, selected-run posture, run-detail and portfolio drill-ins, and paper-promotion review handoffs from shared workstation run data.
- `DataOperationsWorkspaceShellPage` now includes a data-operations desk briefing hero backed by `DataOperationsWorkspacePresentationBuilder`; it projects provider health, resumable backfills, storage health, collection sessions, export jobs, operational blockers, and next-handoff actions from shared service data.
- `WatchlistPage` now includes watchlist posture guidance for saved list count, pinned list coverage, symbol coverage, visible search scope, and empty-state handoffs before an operator loads or imports symbol sets.
- `ProviderHealthPage` now includes a provider-posture briefing that condenses stale snapshots, disconnected streaming sessions, mixed-provider states, and blocked backfill coverage into one next handoff before the operator scans individual provider cards.
- `ActivityLogPage` now includes a triage strip that summarizes visible entries, retained error and warning counts, latest entry posture, and active filters before the operator scans retained log rows.
- Fixture/offline workflow mode is explicitly separated from operational readiness: the shell presents deterministic fixture state as neutral demo data, while Data Operations carries environment-mode context when provider telemetry is absent.
- Legacy deep pages now route through `WorkspaceDeepPageHostPage` in both standalone and docked presentations, so direct navigation and workspace docks share the same workspace title, reachability metadata, related-workflow chrome, and trust-state posture without removing the underlying page functionality.
- Legacy deep pages can now suppress duplicate inner hero/title chrome through `WorkspaceShellChromeState` plus embedded-shell styles (`EmbeddedShellHeroCardStyle`, `EmbeddedShellHeaderGridStyle`, and `EmbeddedShellHeaderStackPanelStyle`), tightening density when pages are already hosted inside the shared workstation shell.
- Action-heavy hosted pages including `MessagingHubPage`, `NotificationCenterPage`, `SecurityMasterPage`, `ServiceManagerPage`, and `PositionBlotterPage` now collapse decorative identity chrome while preserving their page-specific commands, status badges, and trust signals inside the shared shell host.
- `PositionBlotterPage`, `SecurityMasterPage`, and `ServiceManagerPage` now go beyond top-band cleanup and render as workflow-native workbenches with persistent inspector rails for selection state, filters/runtime posture, and operator actions while preserving their existing commands and service integrations.
- The WPF landing page now starts from the four operator workspaces, and the main shell exposes a density toggle so operators can switch between standard and compact workstation framing without leaving the shell.
- `NotificationCenterPage` now behaves more like an operator inbox baseline, with search, unread-only filtering, severity filters, per-item read state, and richer empty/history states.
- `PositionBlotterPage` now includes a selected-position review rail with long/short/gross/net quantity, eligibility counts, and preview rows for batch-action review.
- `BatchBacktestPage` and `BatchBacktestViewModel` now run real request-level parameter sweeps through `IBatchBacktestService` with validation, progress, cancellation, result metrics, and focused ViewModel tests; strategy selection and persisted sweep grouping remain open Research work.
- Dock-hosted workspace pages are wrapped in `Frame` containers so WPF page content can be embedded safely inside the workstation docking surface.
- Theme switching, notification center, info bar
- Offline indicator (single notification + warning on backend unreachable)
- Session state persistence (active workspace, last page, window bounds)
- Shell-first regression coverage now includes DI registration checks, workspace-shell smoke tests, dock-hosting smoke tests, compact-host chrome assertions for representative legacy pages, isolated `MainPage` workflow automation, Provider Health posture-state tests, Activity Log triage-state tests, Watchlist posture-state tests, local single-instance mutex and launch-argument forwarding coverage, and a full registered-page navigation sweep in `tests/Meridian.Wpf.Tests/`.

### Pages with live service connections (Implemented)

| Page | Primary Service | Function |
| ------ | ---------------- | --------- |
| DashboardPage | StatusService, ConnectionService | System overview, provider status |
| BackfillPage | BackfillService, BackfillApiService | Trigger/schedule backfills |
| DataSourcesPage | ConfigService, ProviderManagementService | Provider configuration |
| ProviderPage | ProviderManagementService | Provider detail + credentials |
| ProviderHealthPage | ProviderHealthService | Per-provider health metrics with posture briefing |
| SettingsPage | ConfigService, ThemeService | App settings |
| SymbolsPage | SymbolManagementService | Symbol list management |
| SymbolStoragePage | StorageServiceBase | Per-symbol storage view |
| SymbolMappingPage | SymbolMappingService | Cross-provider symbol mapping |
| DataQualityPage | DataQualityServiceBase | Quality metrics dashboard with symbol-filter scope and empty-state guidance |
| DataSamplingPage | DataSamplingService | Data sampling configuration |
| DataCalendarPage | DataCalendarService | Calendar heat-map of collected dates |
| DataBrowserPage | ArchiveBrowserService | Browse stored data files |
| DataExportPage | AnalysisExportService | Export stored data |
| AnalysisExportPage | AnalysisExportService | Advanced export options |
| AnalysisExportWizardPage | AnalysisExportWizardService | Guided export workflow |
| ChartingPage | ChartingService | OHLCV chart display |
| LiveDataViewerPage | LiveDataService | Real-time tick viewer |
| OrderBookPage | OrderBookVisualizationService | L2 order book display |
| CollectionSessionPage | CollectionSessionService | Active session summary |
| ActivityLogPage | ApiClientService | Live event log with triage posture |
| DiagnosticsPage | NavigationService, NotificationService | System diagnostics |
| SetupWizardPage | SetupWizardService | First-run onboarding |
| PackageManagerPage | PortablePackagerService | Create/import packages |
| ScheduleManagerPage | ScheduleManagerService | Backfill schedules |
| ServiceManagerPage | BackendServiceManagerBase | Backend service status with control-lane and runtime inspector |
| StorageOptimizationPage | StorageOptimizationAdvisorService | Storage optimization advice |
| ArchiveHealthPage | ArchiveHealthService | Archive integrity status |
| SystemHealthPage | SystemHealthService | Comprehensive health view |
| AdvancedAnalyticsPage | AdvancedAnalyticsServiceBase | Advanced analytics |
| EventReplayPage | EventReplayService | Historical event replay |
| ExportPresetsPage | ExportPresetServiceBase | Saved export profiles |
| LeanIntegrationPage | LeanIntegrationService | QuantConnect Lean integration |
| MessagingHubPage | (messaging infrastructure) | WebSocket messaging hub |
| NotificationCenterPage | NotificationService | Notification history |
| OptionsPage | (options infrastructure) | Options/derivatives data |
| PortfolioImportPage | PortfolioImportService | Portfolio CSV import |
| RetentionAssurancePage | (RetentionAssuranceService) | Retention policy status |
| TimeSeriesAlignmentPage | TimeSeriesAlignmentService | Multi-symbol time alignment |
| WorkspacePage | WorkspaceService | Workspace management |

### Trading workstation migration target (Planned / partially implemented)

The current WPF app exposes broad capability coverage, but the active implementation wave reorganizes those capabilities into four workflow workspaces:

- **Research** - backtests, Lean engine flows, charts, replay, experiment comparison
- **Trading** - live monitoring, orders, fills, positions, strategy operation
- **Data Operations** - providers, symbols, backfills, schedules, storage, exports
- **Governance** - portfolio, ledger, diagnostics, retention, notifications, and settings

This migration is tracked in [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) and [`ROADMAP.md`](ROADMAP.md) Waves 1-3.

### Shared run / portfolio / ledger / reconciliation baseline (In progress)

- Shared workstation DTOs now exist for run summaries/details, portfolio summaries/positions, ledger summaries, journal rows, trial balance rows, and run comparison views.
- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` now derive those models from recorded strategy/backtest results.
- WPF now includes a first-pass `StrategyRuns` browser plus `RunDetail`, `RunPortfolio`, and `RunLedger` drill-in pages, and completed backtests are mirrored into that shared workstation flow.
- Run-scoped reconciliation contracts and service flows now exist through `ReconciliationRunRequest`, `ReconciliationRunSummary`, `ReconciliationRunDetail`, `ReconciliationRunService`, and `/api/workstation/reconciliation/*`.
- Governance fund operations now exposes explicit fund cash-flow projection ladders/events and account-linked multi-ledger views across consolidated, entity, sleeve, and vehicle dimensions.
- The remaining gap is broader paper/live data-source adoption, richer portfolio/ledger analytics, deeper per-entity/per-sleeve/per-vehicle posting fidelity, richer reconciliation UX, and more complete cockpit-style workflow integration.

### Known WPF limitations

- `DiagnosticsPage` reads from local process/environment; not connected to remote backend API.
- Current functionality still relies on many existing pages under the hood, but the desktop taxonomy is now aligned around `Research`, `Trading`, `Data Operations`, and `Governance`; the remaining gap is no longer basic run-browser adoption, but deeper paper/live and cockpit-level workflow integration on top of the new shared run / portfolio / ledger model.

### WPF MVVM progress

| Area | Status | Notes |
| ------ | -------- | ------- |
| `DashboardViewModel` | ✅ | Extracted from `DashboardPage` code-behind; `BindableBase`, bindable properties, timer management |
| Remaining pages | 🔄 | Other pages still use code-behind for business logic; ViewModel extraction ongoing per ADR-017 |

---

## 11. CLI

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Real-time collection | ✅ | `--symbols`, `--no-trades`, `--no-depth`, `--depth-levels` |
| Backfill | ✅ | `--backfill`, `--backfill-provider`, `--backfill-symbols`, `--backfill-from/to` |
| Data packaging | ✅ | `--package`, `--import-package`, `--list-package`, `--validate-package` |
| Configuration wizard | ✅ | `--wizard`, `--auto-config`, `--detect-providers`, `--validate-credentials` |
| Dry-run | ✅ | `--dry-run`, `--dry-run --offline` |
| Self-test | ✅ | `--selftest` |
| Schema check | ✅ | `--check-schemas`, `--validate-schemas`, `--strict-schemas` |
| Configuration watch | ✅ | `--watch-config` |
| Contextual help | ✅ | `--help <topic>` for 7 topics |
| Symbol management | ✅ | `--symbols-add`, `--symbols-remove`, `--symbol-status` |
| Query | ✅ | `--query` for stored data |
| Event replay | ✅ | `--replay` |
| Generate loader | ✅ | `--generate-loader` |
| Progress reporting | ✅ | `ProgressDisplayService`; progress bars, spinners, checklists, tables |
| Error codes reference | ✅ | `--error-codes` |

---

## 12. Observability & Operations

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Prometheus metrics export | ✅ | `/api/metrics`; event throughput, provider health, backpressure, error rates |
| OpenTelemetry pipeline instrumentation | ✅ | `TracedEventMetrics` decorator; `Meridian.Pipeline` meter |
| Activity spans (batch consume, backfill, WAL recovery) | ✅ | `MarketDataTracing` extension methods |
| End-to-end trace context propagation | Complete | Collector ingress creates/preserves `Activity` context and `EventPipeline` carries it through queueing, consumption, and storage append |
| Correlation IDs in structured logs | Complete | `EventPipeline` log scopes now include correlation, trace, span, event type/source, symbol, and sequence |
| API key authentication | ✅ | `ApiKeyMiddleware`; `MDC_API_KEY` env var; constant-time comparison |
| API rate limiting | ✅ | 120 req/min sliding window; `Retry-After` header on 429 |
| Kubernetes health probes | ✅ | `/healthz`, `/readyz`, `/livez` |
| Grafana/Prometheus deployment assets | ✅ | `deploy/monitoring/` with alert rules and dashboard provisioning |
| systemd service unit | ✅ | `deploy/systemd/meridian.service` |
| Docker image | ✅ | `deploy/docker/Dockerfile` + `docker-compose.yml` |
| Daily summary webhook | ✅ | `DailySummaryWebhook`; configurable endpoint |
| Connection status webhook | ✅ | `ConnectionStatusWebhook`; provider events |
| Alert dispatcher | ✅ | `AlertDispatcher`; centralized alert publishing and subscription management |
| Alert runbook registry | ✅ | `AlertRunbookRegistry`; runbook references per alert rule |
| Health check aggregator | ✅ | `HealthCheckAggregator`; parallel health check execution with per-provider timeout |

### Remaining observability work

- **OTLP / Jaeger / Zipkin docs**: Initial operator guide now lives in `docs/development/otlp-trace-visualization.md`; extend it as more hosts auto-bind tracing configuration.

---

## 13. F# Domain & Calculations

| Module | Status | Notes |
| -------- | -------- | ------- |
| `MarketEvents.fs` — F# event types | ✅ | Discriminated union: `Trade`, `Quote`, `DepthUpdate`, `Bar`, `Heartbeat` |
| `Sides.fs` — bid/ask/neutral | ✅ | Type-safe aggressor side |
| `Integrity.fs` — sequence validation | ✅ | Gap detection, out-of-order |
| `Spread.fs` — bid-ask spread | ✅ | Absolute and relative spread calculations |
| `Imbalance.fs` — order book imbalance | ✅ | Bid/ask depth imbalance metric |
| `Aggregations.fs` — OHLCV | ✅ | Streaming bar aggregation |
| `Transforms.fs` — pipeline transforms | ✅ | Map, filter, window transforms |
| `QuoteValidator.fs` | ✅ | Price/size range validation |
| `TradeValidator.fs` | ✅ | Trade sequence and sanity validation |
| `ValidationPipeline.fs` | ✅ | Composable validation pipeline |
| C# Interop generated types | ✅ | `Meridian.FSharp.Interop.g.cs` |

---

## 14. QuantConnect Lean Integration

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Custom data types | ✅ | `LeanDataTypes.cs` — `Trade`, `Quote`, `OrderBook` Lean wrappers |
| `IDataProvider` implementation | ✅ | Reads stored JSONL/Parquet files as Lean data |
| Integration page (WPF) | ✅ | `LeanIntegrationPage` wires `LeanIntegrationService` |
| `LeanIntegrationService` | ✅ | Manages Lean engine connection and data feed |

---

## 14a. MCP Server

Two MCP (Model Context Protocol) server projects provide AI-agent tooling over the Meridian platform.

| Project | Status | Notes |
| --------- | -------- | ------- |
| `Meridian.McpServer` | ✅ | Market-data–focused MCP server: `BackfillTools`, `ProviderTools`, `StorageTools`, `SymbolTools`; `MarketDataPrompts`, `MarketDataResources` |
| `Meridian.Mcp` | ✅ | Repo-tooling MCP server: `AdrTools`, `AuditTools`, `ConventionTools`, `KnownErrorTools`, `ProviderTools`; ADR/convention/template resources and code-review/test-writer prompts |
| MCP tests | ✅ | `tests/Meridian.McpServer.Tests/` — backfill tools and storage tools coverage |

---

## 15. Execution & Brokerage

| Feature | Status | Notes |
| --------- | -------- | ------- |
| Paper trading gateway | ✅ | `PaperTradingGateway` in `Meridian.Execution`; zero-risk strategy validation |
| Order management system | ✅ | `OrderManagementSystem`, `OrderLifecycleManager` |
| Risk validation framework | ✅ | `CompositeRiskValidator` with `IRiskRule` implementations |
| Position limit rule | ✅ | `PositionLimitRule`; configurable per-symbol and total position limits |
| Drawdown circuit breaker | ✅ | `DrawdownCircuitBreaker`; automatic stop on drawdown threshold |
| Order rate throttle | ✅ | `OrderRateThrottle`; configurable order frequency limits |
| **Brokerage gateway framework** | ✅ | `IBrokerageGateway`, `BaseBrokerageGateway`, `BrokerageGatewayAdapter` |
| **Alpaca brokerage gateway** | ✅ | `AlpacaBrokerageGateway`; fractional quantity support, client order ID mapping |
| **Robinhood brokerage gateway** | ✅ | `RobinhoodBrokerageGateway`; unofficial API, equity + option order support, cancel-via-resubmit semantics, and stable `/api/execution/*` seam coverage |
| **IB brokerage gateway** | 🔑 | `IBBrokerageGateway`; conditional on IBAPI build flag |
| **StockSharp brokerage gateway** | 🔑 | `StockSharpBrokerageGateway`; connector-dependent |
| **Template brokerage gateway** | ✅ | `TemplateBrokerageGateway`; scaffold for new adapters |
| Brokerage DI registration | ✅ | `BrokerageServiceRegistration`; `BrokerageConfiguration` options |
| Execution SDK | ✅ | `Meridian.Execution.Sdk`; `IExecutionGateway`, `IOrderManager`, `IPositionTracker` |
| Paper trading portfolio | ✅ | `PaperTradingPortfolio`; simulated position and cash tracking |
| CppTrader order gateway | ✅ | `CppTraderOrderGateway`; native C++ matching engine integration |
| CppTrader live feed adapter | ✅ | `CppTraderLiveFeedAdapter`; real-time data from CppTrader host |

### Remaining execution work

- Wire brokerage gateways into the shared workstation paper-trading cockpit and primary WPF operator lane
- Validate brokerage adapters against live vendor APIs
- Complete cockpit-visible `Backtest → Paper → Live` workflow hardening and audit UX
- Complete paper-trading session persistence and replay operator flows

---

## 16. Testing

| Test Project | Test Files | Methods | Focus |
| --- | --- | --- | --- |
| `Meridian.Tests` | 329 | ~4,169 | Core: backfill, storage, pipeline, monitoring, providers, credentials, serialization, domain, integration endpoints, execution |
| `Meridian.FSharp.Tests` | 12 | ~233 | F# domain validation, calculations, transforms, trading transitions, ledger, risk, direct lending interop |
| `Meridian.Ui.Tests` | 55 | ~975 | UI services (API client, backfill, fixtures, forms, health, watchlist) |
| `Meridian.Wpf.Tests` | 83 | ~638 | WPF desktop services (navigation, config, status, connection, ViewModels) |
| `Meridian.Backtesting.Tests` | 19 | ~243 | Backtest engine, fill models, portfolio simulation, XIRR |
| `Meridian.DirectLending.Tests` | 5 | ~29 | Direct lending services, workflows, PostgreSQL integration |
| `Meridian.FundStructure.Tests` | 3 | ~24 | Governance shared-data access and in-memory fund-structure services |
| `Meridian.McpServer.Tests` | 3 | ~15 | MCP server tools (backfill, storage) |
| `Meridian.QuantScript.Tests` | 8 | ~93 | Script compiler, runner, statistics engine, plot queue, portfolio builder |
| **Total** | **517** | **~6,419** | |

### Key test infrastructure

| Feature | Status |
| --------- | -------- |
| `EndpointTestFixture` base (WebApplicationFactory) | ✅ |
| Negative-path endpoint tests (40+) | ✅ |
| Response schema validation tests (15+) | ✅ |
| `FixtureMarketDataClient` integration harness | ✅ |
| `InMemoryStorageSink` for pipeline integration | ✅ |
| Provider-specific test files (18 files, all providers + streaming failover) | ✅ |
| **IB order fixture tests** (`IBOrderSampleTests`, 5 JSON fixtures) | ✅ |
| Canonicalization golden fixtures (8 curated files) | ✅ |
| Priority backfill queue tests (`PriorityBackfillQueueTests`) | ✅ |
| Rate limiter tests (`RateLimiterTests`) | ✅ |
| Streaming failover service tests (`StreamingFailoverServiceTests`) | ✅ |
| Liquidity profile tests (`LiquidityProfileTests`) | ✅ |
| SLO definition registry tests (`SloDefinitionRegistryTests`) | ✅ |
| Golden-master pipeline replay tests (`GoldenMasterPipelineReplayTests`) | ✅ |
| WAL + event pipeline tests (`WalEventPipelineTests`) | ✅ |
| Ingestion job tests (`IngestionJobTests`, `IngestionJobServiceTests`) | ✅ |
| Data quality unit tests (AnomalyDetector, CompletenessScoreCalculator, GapAnalyzer, SequenceErrorTracker) | ✅ |
| Drift-canary CI job | Complete |

---

## 17. Configuration Schema Validation

| Feature | Status | Notes |
| --------- | -------- | ------- |
| `SchemaValidationService` — stored data format validation | ✅ | `--validate-schemas`, `--strict-schemas`, `--check-schemas` |
| `SchemaVersionManager` | ✅ | Per-event-type schema versioning |
| JSON Schema generation from C# config models | Complete | `--generate-config-schema` produces the checked-in `config/appsettings.schema.json`; sample config references it and CI validates drift |

---

## 18. Trading Workstation Product Surfaces

This section inventories the workflow-centric product model that now sits above the older page inventory.

| Surface | Status | Notes |
| --------- | -------- | ------- |
| Research workspace taxonomy | Partial | Desktop vocabulary now aligns on `Research`; the Research shell now has a desk briefing hero for selected-run, run-detail, portfolio, and `Backtest -> Paper` promotion-review handoffs, while deeper research workflow acceptance and Backtest Studio unification remain open |
| Trading workspace taxonomy | Partial | Command palette and shell terminology align on `Trading`, the Trading shell now keeps run-scoped versus account-scoped portfolio drill-ins inside the cockpit instead of bouncing operators back to `Research`, and the desk briefing hero projects context-required, replay-mismatch, controls-blocked, paper-review, and live-oversight handoffs from shared readiness inputs; the shared trading-readiness endpoint gives the cockpit one acceptance contract with recent risk/control audit evidence and missing-field explainability warnings, while cockpit-grade execution UX remains pending |
| Data Operations workspace taxonomy | Partial | Operational pages are grouped consistently, and the Data Operations shell now has a desk briefing hero for provider, backfill, storage, session, export, blocker, and next-handoff posture; deeper workflow acceptance and cross-workspace handoff proof remain open |
| Governance workspace taxonomy | Partial | Portfolio/ledger/diagnostics/settings surfaces are grouped conceptually, and Security Master/reconciliation drill-ins are live; broader governance-first product flows remain incomplete |
| Governance fund-ops workspace API baseline | Partial | `/api/fund-structure/workspace-view`, `/api/fund-structure/report-pack-preview`, and `/api/fund-structure/report-packs` now aggregate fund-account state, banking, ledger, reconciliation, NAV attribution, reporting previews, and local-first governed report-pack artifacts for a `fundProfileId`; the Governance WPF shell now reuses the same shared projection, while workstation-shell polish and broader board/investor/compliance packaging remain open. Guardrail: Security Master is the sole instrument source, and governance DTOs with instrument terms must carry Security Master identity/provenance references. Trial-balance and reconciliation symbol metadata now reuse canonical `WorkstationSecurityReference` records (same layer already used by run portfolio/ledger surfaces) rather than a separate classification-only projection. |
| Shared `StrategyRun` DTO/read-model baseline | Partial | Shared run summary/detail/comparison models exist; paper/live history expansion remains |
| Shared portfolio read-model baseline | Partial | Portfolio summaries/positions derived from recorded runs exist; equity-history and broader source coverage remain |
| Shared ledger read-model baseline | Partial | Ledger summaries, journal rows, and trial balance rows exist; account-summary and richer reconciliation UX remain |
| Reconciliation run baseline | Partial | Run-scoped reconciliation service, history, Security Master coverage issue detection, and a file-backed reconciliation break queue now exist. The queue seeds run-scoped breaks and supports review, resolve/dismiss, and audit-history routes; broader non-run, external-statement/custodian, SLA, and calibrated exception workflows remain. |
| Security Master platform baseline | Complete | The current Security Master mechanics are delivered and workstation productization is live: hardened WPF activation, canonical `WorkstationSecurityReference` coverage/provenance, and shared research/trading/governance/portfolio/ledger propagation |
| Security Master — bond term richness | ✅ | Extended `SecurityEconomicDefinition` with coupon rate, maturity, day-count convention, seniority, callable flag, and issue price |
| Security Master — trading parameters | ✅ | Per-instrument lot size, tick size; `PaperTradingGateway` lot-size validation and `BacktestEngine` tick-size rounding wired; `GET /api/security-master/{id}/trading-parameters` |
| Security Master — corporate action events | ✅ | `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption` domain events; `CorporateActionAdjustmentService` applies split-adjusted bar prices in backtest replay; `GET /api/security-master/{id}/corporate-actions` |
| Security Master — exchange bulk ingest | ✅ | CSV + JSON bulk-ingest via `SecurityMasterImportService`; idempotent dedup; CLI `--security-master-ingest`; `POST /api/security-master/import`; typed `GET /api/security-master/ingest/status` polling surface |
| Security Master — EDGAR ingest provider | ✅ | `EdgarSecurityMasterIngestProvider`; SEC company-ticker and submission enrichment flow with provenance capture and SEC rate-limit-aware ingest behavior |
| Security Master — golden record conflict resolution | ✅ | `SecurityMasterConflictService` detects ingest-time identifier conflicts automatically; `GET /api/security-master/conflicts` list + `POST /api/security-master/conflicts/{id}/resolve`; workstation conflict queue and operator resolution path are live |
| Security Master — WPF browser | ✅ | `SecurityMasterPage` + `SecurityMasterViewModel` (BindableBase); search, results/detail/inspector workbench, ingest polling, conflict queue, corporate action timeline, trading params, import/backfill posture, and governance drill-ins |
| Direct lending vertical slice | Partial | Postgres-backed direct-lending services, migrations, workflow support, and `/api/loans/*` endpoints are live; broader governance/reporting integration remains |
| WPF run browser/detail/portfolio/ledger surfaces | In progress | Code present in `src/Meridian.Wpf/`; included in active build |
| Backtest Studio unification | Planned | Native and Lean backtests are still distinct operator experiences |
| Paper-trading cockpit | Partial | Trading workspace surfaces now cover positions, orders, fills, replay, sessions, promotion flows, replay-audit metadata, in-shell portfolio/accounting drill-ins, the WPF desk briefing hero, and a shared `/api/workstation/trading/readiness` contract for session/replay/control/promotion/DK1 trust-gate/brokerage/work-item posture; cockpit hardening, broader broker validation, and stronger acceptance criteria remain |
| Promotion workflow (`Backtest -> Paper -> Live`) | Partial | Endpoint layer and dashboard flows exist, and promotion approvals now carry an explicit approval checklist for DK1 trust packet, lineage, portfolio/ledger continuity, and risk-control review; safety-gated lifecycle hardening, broader operator acceptance, and full live-readiness remain open |

### Additional governance and platform tracks

- **Cash-flow modeling surfaces:** governance-oriented cash-movement and projection views are not yet productized.
- **Multi-ledger tracking:** governance workflows do not yet expose multiple ledgers, ledger groups, or cross-ledger consolidation explicitly.
- **Reconciliation engine expansion:** run-scoped reconciliation now exists for recorded strategy runs, but broader position, cash, NAV, external statement, and exception-queue workflows remain incomplete.
- **Governance architecture review check:** flag governance-local instrument definitions unless they are adapter-only intermediates with explicit mapping to Security Master IDs/provenance before downstream DTO/service exposure.
- **Reviewer search guidance:** for governance DTO/service diffs, search for instrument terms (`Symbol`, `Cusip`, `Isin`, `Coupon`, `Maturity`, `Issuer`, `Venue`, `AssetClass`) and confirm paired Security Master reference/provenance fields.
- **Report generation tools:** export infrastructure exists and fund-scoped report-pack APIs now deliver the first governed artifact slice: manifest/provenance JSON, trial-balance and asset-class JSON/CSV files, XLSX workbook output, checksums, history, and detail retrieval. Full investor, board, compliance, and expanded fund-ops templates remain future work.

### Remaining work

- Turn taxonomy alignment into true workspace-first shells with quick actions and cross-workflow entry points.
- Extend the shared run/portfolio/ledger model to paper/live history, cash-flow views, multi-ledger tracking, and richer reconciliation views.
- Keep the delivered Security Master baseline central while Wave 4 governance work deepens account/entity, cash-flow, multi-ledger, reconciliation, and reporting workflows on top of it.
- Treat [`docs/plans/security-master-productization-roadmap.md`](../plans/security-master-productization-roadmap.md) as the delivered-baseline reference for Security Master mechanics; canonical wave status remains in [`PROGRAM_STATE.md`](PROGRAM_STATE.md) and [`ROADMAP.md`](ROADMAP.md).
- Expand the current reconciliation seam from the delivered run-scoped break queue into richer match rules, calibrated exception workflows, external-statement/custodian coverage, and non-run governance use cases.
- Extend the direct-lending slice into governance-grade projections, reconciliation hooks, and reporting outputs.
- Expand report generation tools beyond the delivered trial-balance artifact slice into cash-flow, reconciliation-detail, portfolio, board, investor, compliance, and publication workflows.
- Replace page-by-page mental models with workstation-native journeys for research, trading, data ops, and governance.

---

## 19. Flagship Planned Capabilities

These areas are part of the documented implementation scope even though they are not yet productized in the current repo state.

| Capability | Status | Notes |
| ------------ | -------- | ------- |
| QuantScript library/project | ✅ | `src/Meridian.QuantScript` — Roslyn scripting API, PriceSeries/ReturnSeries domain types, StatisticsEngine, BacktestProxy, QuantDataContext, PlotQueue |
| QuantScript WPF editor/surface | ✅ | `QuantScriptPage.xaml` + `QuantScriptViewModel` — AvalonEdit editor, three-column layout, Console/Charts/Metrics/Trades/Diagnostics result tabs, ScottPlot charting |
| QuantScript tests/sample scripts/docs | ✅ | `tests/Meridian.QuantScript.Tests` (compiler, runner, stats, plot-queue); `scripts/example-sharpe.csx` sample script |
| L3 reconstruction timeline | ðŸ“ | Planned deterministic replay + merged timeline for queue inference |
| L3 inference model | ðŸ“ | Planned probabilistic queue-ahead inference with confidence scoring |
| Queue-aware execution simulator | ðŸ“ | Planned market/limit simulation with partial fills, latency, and exported artifacts |
| Simulation CLI workflow | ðŸ“ | `--simulate-execution` / calibration commands are documented but not yet implemented |
| Simulation WPF explorer | ðŸ“ | Dedicated simulation page and progress/results UX remain planned |

### Remaining work

- Convert both blueprints into real projects, contracts, tests, docs, and operator-facing entry points.
- Ensure these capabilities land on top of shared workstation models rather than as isolated feature islands.

---

## Summary: Remaining Work to Full Implementation

### High priority (blocking full provider coverage)

| ID | Area | Effort | Description |
| ---- | ------ | -------- | ------------- |
| ✅ | Polygon validation | Medium | Recorded-session replay fixture validates trade, quote, and aggregate parsing without live network access |

### Medium priority (observability & developer experience)

| ID | Area | Effort | Description |
| ---- | ------ | -------- | ------------- |
| ✅ | OTLP trace visualization docs | Low | `docs/development/otlp-trace-visualization.md` documents collector/export wiring and local Jaeger flow |

### Low priority (architecture debt)

| ID | Area | Effort | Description |
| ---- | ------ | -------- | ------------- |
| H2 | Multi-instance coordination | High | Distributed locking for symbol subscriptions across multiple collector instances |
| — | WPF ViewModel extraction | Medium | Extract remaining page code-behind logic into `BindableBase` ViewModels (ADR-017) |
| — | DailySummaryWebhook state | Low | Persist `_dailyHistory` to disk using `MetadataTagService` save pattern |
| — | StockSharp connector expansion | Low | Extend connector examples/validation coverage beyond the currently documented baseline |
| — | IB vendor-DLL validation | Low | Keep the scripted setup and smoke-build path aligned with the official IB API release surface |

---

## Target End Product Snapshot

Meridian’s intended end state is a comprehensive fund management platform rather than a loose collection of pages and utilities.

- `Research`, `Trading`, `Data Operations`, and `Governance` should operate as durable product surfaces, not only naming conventions.
- Backtests, paper sessions, and live-facing history should share one recognizable run model with first-class portfolio and ledger drill-ins.
- Account, entity, strategy-implementation, and trade-management workflows should be part of the same connected product surface.
- Security Master now serves as the authoritative instrument-definition layer across research, trading, governance, portfolio, and ledger workflows; the current repo already delivers that baseline.
- Governance should expose cash-flow modeling, trial-balance analysis, and multi-ledger tracking as first-class capabilities.
- Governance should include a reconciliation engine comparable to fund-operations tooling, plus report generation tools for audit, investor, and compliance outputs.
- Provider, replay, storage, diagnostics, and observability capabilities should support that operator workflow end to end.
- Optional scale-out and assembly-level optimization work can deepen the platform, but they are not required for the non-assembly product baseline to feel complete.

---

## How to Read This Document

- **✅ Complete**: No action required; tested and in production code paths.
- **⚠️ Partial**: Works with caveats; see "Remaining Work" column.
- **🔑 Credentials/build flag required**: Implementation is complete but requires external setup (credentials, IBAPI download, StockSharp license).
- **🔄 Framework in place**: Core structure exists; specific sub-feature is incomplete (for example, the workstation taxonomy is in place but deeper workspace-native shells and operator flows still remain).
- **📝 Planned**: Not started; see ROADMAP.md wave schedule.

---

_Last Updated: 2026-04-26_

