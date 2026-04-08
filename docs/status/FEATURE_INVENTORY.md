# Meridian — Feature Inventory

**Version:** 1.7.2
**Date:** 2026-03-31
**Purpose:** Comprehensive inventory of every functional area, its current implementation status, and the remaining work required to reach full implementation.

Use this document alongside [`ROADMAP.md`](ROADMAP.md) (delivery waves and sequencing), [`IMPROVEMENTS.md`](IMPROVEMENTS.md) (normalized improvement/backlog tracking), and [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) (consolidated non-assembly execution backlog).

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Fully implemented and tested |
| ⚠️ | Partially implemented — functional with caveats |
| 🔑 | Requires external credentials / build flag |
| 🔄 | Framework in place, one or more sub-features pending |
| 📝 | Planned, not yet started |

---

## 1. Core Infrastructure

| Feature | Status | Notes |
|---------|--------|-------|
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
|----------|--------|----------------|
| **Alpaca** | ✅ | Credential validation, automatic resubscription on reconnect, quote routing |
| **Interactive Brokers** | 🔑 | Real runtime requires `-p:DefineConstants=IBAPI` plus the official `IBApi` surface; non-`IBAPI` builds expose simulation/setup guidance instead of broker connectivity |
| **Polygon** | ⚠️ | Real connection when API key present; stub mode (synthetic heartbeat/trades) without key. WebSocket parsing now has committed recorded-session replay coverage, but broader live-feed coverage is still limited |
| **NYSE** | 🔑 | Requires NYSE Connect credentials; provider implementation complete |
| **StockSharp** | 🔑 | Requires StockSharp connector-specific credentials + connector type config. Unsupported connector / missing-package paths now return recovery-oriented guidance pointing to `EnableStockSharp=true`, connector package requirements, and the StockSharp connector guide |
| **Failover-Aware Client** | ✅ | `FailoverAwareMarketDataClient` with `ProviderDegradationScorer`, per-provider health |
| **Streaming Failover Service** | ✅ | `StreamingFailoverService` + `StreamingFailoverRegistry`; runtime failover orchestration with configurable rules and health evaluation |
| **IB Simulation Client** | ✅ | `IBSimulationClient` for testing without live connection |
| **NoOp Client** | ✅ | `NoOpMarketDataClient` for dry-run / test harness scenarios |

Provider validation matrix and evidence links now live in `docs/status/provider-validation-matrix.md` and are referenced by `production-status.md` for pass/fail readiness gating.

### Remaining work to reach full provider coverage

- **Polygon**: Validate WebSocket message parsing against Polygon v2 feed schema (trades, quotes, aggregates, status messages). Add round-trip integration test with a recorded WebSocket session replay.
- **StockSharp**: Runtime connector guidance and unsupported-path recovery messaging are now aligned; remaining work is expanding connector coverage/examples as more adapters are validated.
- **IB**: Scripted setup instructions and a compile-only smoke-build path now exist; remaining work is keeping the live vendor-DLL path validated against real IB API releases.

---

## 3. Historical Backfill Providers

| Provider | Status | Notes |
|----------|--------|-------|
| Alpaca | ✅ | Daily bars, trades, quotes; credentials required |
| Polygon | ✅ | Daily bars and aggregates; API key required |
| Tiingo | ✅ | Daily bars; token required |
| Yahoo Finance | ✅ | Daily bars; unofficial API, no credentials |
| Stooq | ✅ | Daily bars; free, no credentials |
| Finnhub | ✅ | Daily bars; token required |
| Alpha Vantage | ✅ | Daily bars; API key required |
| Nasdaq Data Link (Quandl) | ✅ | Various; API key required |
| Interactive Brokers | 🔑 | Full implementation behind `IBAPI`; smoke builds remain compile-only and are not operator-ready historical access |
| StockSharp | ✅ | Via StockSharp connectors; runtime/historical coverage depends on connector setup, package surface, and entitlement |
| **Composite Provider** | ✅ | Priority-based fallback chain, rate-limit tracking, per-provider health |
| **Gap Backfill Service** | ✅ | `GapBackfillService` triggered on reconnect; uses `WebSocketReconnectionHelper` gap window |
| **Backfill Rate Limiting** | ✅ | `ProviderRateLimitTracker` per provider; exponential backoff with `Retry-After` parsing |
| **Backfill Scheduling** | ✅ | Cron-based `ScheduledBackfillService`; `BackfillScheduleManager` with CRUD API |
| **Backfill Progress Reporting** | ✅ | `BackfillProgressTracker`, per-symbol %, exposed at `/api/backfill/progress` |
| **Priority Backfill Queue** | ✅ | `PriorityBackfillQueue`, `BackfillJobManager`, `BackfillJob`; priority-ordered job execution |
| **Gap Analysis (Infrastructure)** | ✅ | `DataGapAnalyzer`, `DataGapRepair`, `DataQualityMonitor`; storage scan, gap detection, automated repair |

---

## 4. Symbol Search

| Provider | Status | Notes |
|----------|--------|-------|
| Alpaca | ✅ | `AlpacaSymbolSearchProviderRefactored`; US equities + crypto |
| Finnhub | ✅ | `FinnhubSymbolSearchProviderRefactored`; US + international |
| Polygon | ✅ | `PolygonSymbolSearchProvider`; US equities |
| OpenFIGI | ✅ | `OpenFigiClient`; global instrument ID mapping |
| StockSharp | ✅ | `StockSharpSymbolSearchProvider`; multi-exchange |
| **Symbol Import/Export** | ✅ | CSV import/export via `SymbolImportExportService`; portfolio import |
| **Symbol Registry** | ✅ | `CanonicalSymbolRegistry` with persistence; `SymbolRegistryService` |
| **Symbol Normalization** | ✅ | `SymbolNormalization` utility; PCG-PA, BRK.A, ^GSPC, =SPX patterns |

---

## 5. Data Canonicalization

| Component | Status | Notes |
|-----------|--------|-------|
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
|---------|--------|-------|
| JSONL storage sink | ✅ | Append-only, gzip-compressed, configurable naming conventions |
| Parquet storage sink | ✅ | Columnar, compressed; enabled via `EnableParquetSink` config |
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
|---------|--------|-------|
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
|------|--------|--------|
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
| **Total route constants** | **300** | **0 stubs remaining** |

### OpenAPI annotations

| Endpoint family | Typed `Produces<T>` | Descriptions | Status |
|-----------------|---------------------|--------------|--------|
| Status | ✅ | ✅ | ✅ |
| Health | ✅ | ✅ | ✅ |
| Config | ✅ | ✅ | ✅ |
| Backfill / Schedules | ✅ | ✅ | ✅ |
| Providers / Extended | ✅ | ✅ | ✅ |
| All other families | ✅ | ✅ | ✅ |

---

## 9. Web Dashboard

| Feature | Status | Notes |
|---------|--------|-------|
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
- Command palette (`Ctrl+K`), keyboard shortcuts
- Theme switching, notification center, info bar
- Offline indicator (single notification + warning on backend unreachable)
- Session state persistence (active workspace, last page, window bounds)

### Pages with live service connections (Implemented)

| Page | Primary Service | Function |
|------|----------------|---------|
| DashboardPage | StatusService, ConnectionService | System overview, provider status |
| BackfillPage | BackfillService, BackfillApiService | Trigger/schedule backfills |
| DataSourcesPage | ConfigService, ProviderManagementService | Provider configuration |
| ProviderPage | ProviderManagementService | Provider detail + credentials |
| ProviderHealthPage | ProviderHealthService | Per-provider health metrics |
| SettingsPage | ConfigService, ThemeService | App settings |
| SymbolsPage | SymbolManagementService | Symbol list management |
| SymbolStoragePage | StorageServiceBase | Per-symbol storage view |
| SymbolMappingPage | SymbolMappingService | Cross-provider symbol mapping |
| DataQualityPage | DataQualityServiceBase | Quality metrics dashboard |
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
| ActivityLogPage | ApiClientService | Live event log |
| DiagnosticsPage | NavigationService, NotificationService | System diagnostics |
| SetupWizardPage | SetupWizardService | First-run onboarding |
| PackageManagerPage | PortablePackagerService | Create/import packages |
| ScheduleManagerPage | ScheduleManagerService | Backfill schedules |
| ServiceManagerPage | BackendServiceManagerBase | Backend service status |
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
- The remaining gap is broader paper/live data-source adoption, richer portfolio/ledger analytics, explicit cash-flow and multi-ledger views, richer reconciliation UX, and more complete cockpit-style workflow integration.

### Known WPF limitations

- `DiagnosticsPage` reads from local process/environment; not connected to remote backend API.
- Current functionality still relies on many existing pages under the hood, but the desktop taxonomy is now aligned around `Research`, `Trading`, `Data Operations`, and `Governance`; the remaining gap is no longer basic run-browser adoption, but deeper paper/live and cockpit-level workflow integration on top of the new shared run / portfolio / ledger model.

### WPF MVVM progress

| Area | Status | Notes |
|------|--------|-------|
| `DashboardViewModel` | ✅ | Extracted from `DashboardPage` code-behind; `BindableBase`, bindable properties, timer management |
| Remaining pages | 🔄 | Other pages still use code-behind for business logic; ViewModel extraction ongoing per ADR-017 |

---

## 11. CLI

| Feature | Status | Notes |
|---------|--------|-------|
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
|---------|--------|-------|
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
|--------|--------|-------|
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
|---------|--------|-------|
| Custom data types | ✅ | `LeanDataTypes.cs` — `Trade`, `Quote`, `OrderBook` Lean wrappers |
| `IDataProvider` implementation | ✅ | Reads stored JSONL/Parquet files as Lean data |
| Integration page (WPF) | ✅ | `LeanIntegrationPage` wires `LeanIntegrationService` |
| `LeanIntegrationService` | ✅ | Manages Lean engine connection and data feed |

---

## 14a. MCP Server

Two MCP (Model Context Protocol) server projects provide AI-agent tooling over the Meridian platform.

| Project | Status | Notes |
|---------|--------|-------|
| `Meridian.McpServer` | ✅ | Market-data–focused MCP server: `BackfillTools`, `ProviderTools`, `StorageTools`, `SymbolTools`; `MarketDataPrompts`, `MarketDataResources` |
| `Meridian.Mcp` | ✅ | Repo-tooling MCP server: `AdrTools`, `AuditTools`, `ConventionTools`, `KnownErrorTools`, `ProviderTools`; ADR/convention/template resources and code-review/test-writer prompts |
| MCP tests | ✅ | `tests/Meridian.McpServer.Tests/` — backfill tools and storage tools coverage |

---

## 15. Execution & Brokerage

| Feature | Status | Notes |
|---------|--------|-------|
| Paper trading gateway | ✅ | `PaperTradingGateway` in `Meridian.Execution`; zero-risk strategy validation |
| Order management system | ✅ | `OrderManagementSystem`, `OrderLifecycleManager` |
| Risk validation framework | ✅ | `CompositeRiskValidator` with `IRiskRule` implementations |
| Position limit rule | ✅ | `PositionLimitRule`; configurable per-symbol and total position limits |
| Drawdown circuit breaker | ✅ | `DrawdownCircuitBreaker`; automatic stop on drawdown threshold |
| Order rate throttle | ✅ | `OrderRateThrottle`; configurable order frequency limits |
| **Brokerage gateway framework** | ✅ | `IBrokerageGateway`, `BaseBrokerageGateway`, `BrokerageGatewayAdapter` |
| **Alpaca brokerage gateway** | ✅ | `AlpacaBrokerageGateway`; fractional quantity support, client order ID mapping |
| **IB brokerage gateway** | 🔑 | `IBBrokerageGateway`; conditional on IBAPI build flag |
| **StockSharp brokerage gateway** | 🔑 | `StockSharpBrokerageGateway`; connector-dependent |
| **Template brokerage gateway** | ✅ | `TemplateBrokerageGateway`; scaffold for new adapters |
| Brokerage DI registration | ✅ | `BrokerageServiceRegistration`; `BrokerageConfiguration` options |
| Execution SDK | ✅ | `Meridian.Execution.Sdk`; `IExecutionGateway`, `IOrderManager`, `IPositionTracker` |
| Paper trading portfolio | ✅ | `PaperTradingPortfolio`; simulated position and cash tracking |
| CppTrader order gateway | ✅ | `CppTraderOrderGateway`; native C++ matching engine integration |
| CppTrader live feed adapter | ✅ | `CppTraderLiveFeedAdapter`; real-time data from CppTrader host |

### Remaining execution work

- Wire brokerage gateways into the web dashboard paper-trading cockpit
- Validate brokerage adapters against live vendor APIs
- Build explicit `Backtest → Paper → Live` promotion workflow with audit trail
- Add paper-trading session persistence and replay

---

## 16. Testing

| Test Project | Test Files | Methods | Focus |
|---|---|---|---|
| `Meridian.Tests` | 266 | ~3,595 | Core: backfill, storage, pipeline, monitoring, providers, credentials, serialization, domain, integration endpoints, execution |
| `Meridian.FSharp.Tests` | 10 | ~174 | F# domain validation, calculations, transforms, trading transitions, ledger, risk, direct lending interop |
| `Meridian.Ui.Tests` | 55 | ~948 | UI services (API client, backfill, fixtures, forms, health, watchlist) |
| `Meridian.Wpf.Tests` | 35 | ~391 | WPF desktop services (navigation, config, status, connection, ViewModels) |
| `Meridian.Backtesting.Tests` | 14 | ~146 | Backtest engine, fill models, portfolio simulation, XIRR |
| `Meridian.DirectLending.Tests` | 7 | ~29 | Direct lending services, workflows, PostgreSQL integration |
| `Meridian.McpServer.Tests` | 3 | ~11 | MCP server tools (backfill, storage) |
| `Meridian.QuantScript.Tests` | 10 | ~76 | Script compiler, runner, statistics engine, plot queue, portfolio builder |
| **Total** | **400** | **~5,370** | |

### Key test infrastructure

| Feature | Status |
|---------|--------|
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
|---------|--------|-------|
| `SchemaValidationService` — stored data format validation | ✅ | `--validate-schemas`, `--strict-schemas`, `--check-schemas` |
| `SchemaVersionManager` | ✅ | Per-event-type schema versioning |
| JSON Schema generation from C# config models | Complete | `--generate-config-schema` produces the checked-in `config/appsettings.schema.json`; sample config references it and CI validates drift |

---

## 18. Trading Workstation Product Surfaces

This section inventories the workflow-centric product model that now sits above the older page inventory.

| Surface | Status | Notes |
|---------|--------|-------|
| Research workspace taxonomy | Partial | Desktop vocabulary now aligns on `Research`; the remaining gap is deeper workspace-native shells and operator flows |
| Trading workspace taxonomy | Partial | Command palette and shell terminology align on `Trading`; cockpit-grade execution UX remains pending |
| Data Operations workspace taxonomy | Partial | Operational pages are grouped consistently; further cross-links and workflow shells remain |
| Governance workspace taxonomy | Partial | Portfolio/ledger/diagnostics/settings surfaces are grouped conceptually; governance-first product flows remain incomplete |
| Shared `StrategyRun` DTO/read-model baseline | Partial | Shared run summary/detail/comparison models exist; paper/live history expansion remains |
| Shared portfolio read-model baseline | Partial | Portfolio summaries/positions derived from recorded runs exist; equity-history and broader source coverage remain |
| Shared ledger read-model baseline | Partial | Ledger summaries, journal rows, and trial balance rows exist; account-summary and richer reconciliation UX remain |
| Reconciliation run baseline | Partial | Run-scoped reconciliation service, history, and Security Master coverage issue detection now exist; broader break queues and non-run workflows remain |
| Security Master platform baseline | Complete | Wave 6 mechanics are delivered and workstation productization is live: hardened WPF activation, canonical `WorkstationSecurityReference` coverage/provenance, and shared research/trading/governance/portfolio/ledger propagation |
| Security Master — bond term richness | ✅ | Extended `SecurityEconomicDefinition` with coupon rate, maturity, day-count convention, seniority, callable flag, and issue price |
| Security Master — trading parameters | ✅ | Per-instrument lot size, tick size; `PaperTradingGateway` lot-size validation and `BacktestEngine` tick-size rounding wired; `GET /api/security-master/{id}/trading-parameters` |
| Security Master — corporate action events | ✅ | `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption` domain events; `CorporateActionAdjustmentService` applies split-adjusted bar prices in backtest replay; `GET /api/security-master/{id}/corporate-actions` |
| Security Master — exchange bulk ingest | ✅ | CSV + JSON bulk-ingest via `SecurityMasterImportService`; idempotent dedup; CLI `--security-master-ingest`; `POST /api/security-master/import` endpoint |
| Security Master — golden record conflict resolution | ✅ | `SecurityMasterConflictService` detects identifier-ambiguity conflicts; `GET /api/security-master/conflicts` list + `POST /api/security-master/conflicts/{id}/resolve` |
| Security Master — WPF browser | ✅ | `SecurityMasterPage` + `SecurityMasterViewModel` (BindableBase); hardened degraded-mode activation, search, detail panel, corporate action timeline, trading params, import/backfill capability gating |
| Security Master — workstation/governance integration | ✅ | Shared `WorkstationSecurityReference` metadata now flows through portfolio, ledger, reconciliation, Research, Trading, and Governance payloads with coverage state, provenance, and detail deep links |
| Direct lending vertical slice | Partial | Postgres-backed direct-lending services, migrations, workflow support, and `/api/loans/*` endpoints are live; broader governance/reporting integration remains |
| WPF run browser/detail/portfolio/ledger surfaces | In progress | Code present in `src/Meridian.Wpf/`; included in active build |
| Backtest Studio unification | Planned | Native and Lean backtests are still distinct operator experiences |
| Paper-trading cockpit | Partial | Execution primitives and brokerage gateway adapters (Alpaca, IB, StockSharp) exist; dedicated web dashboard cockpit wiring is still planned |
| Promotion workflow (`Backtest -> Paper -> Live`) | Partial | Brokerage gateway framework provides the execution adapter layer; safety-gated lifecycle workflow remains planned |

### Additional governance and platform tracks

- **Cash-flow modeling surfaces:** governance-oriented cash-movement and projection views are not yet productized.
- **Multi-ledger tracking:** governance workflows do not yet expose multiple ledgers, ledger groups, or cross-ledger consolidation explicitly.
- **Reconciliation engine expansion:** run-scoped reconciliation now exists for recorded strategy runs, but broader position, cash, NAV, external statement, and exception-queue workflows remain incomplete.
- **Report generation tools:** export infrastructure exists, but governed report-pack generation for investor, board, compliance, and fund-ops workflows is not yet productized.

### Remaining work

- Turn taxonomy alignment into true workspace-first shells with quick actions and cross-workflow entry points.
- Extend the shared run/portfolio/ledger model to paper/live history, cash-flow views, multi-ledger tracking, and richer reconciliation views.
- Extend Security Master-backed governance from the delivered coverage/reconciliation/drill-in baseline into broader report-pack, cash-flow, and multi-ledger workflows.
- Expand the current reconciliation seam into explicit break queues, match rules, exception workflows, and non-run governance use cases.
- Extend the direct-lending slice into governance-grade projections, reconciliation hooks, and reporting outputs.
- Add report generation tools that package auditable governance outputs for operators and stakeholders.
- Replace page-by-page mental models with workstation-native journeys for research, trading, data ops, and governance.

---

## 19. Flagship Planned Capabilities

These areas are part of the documented implementation scope even though they are not yet productized in the current repo state.

| Capability | Status | Notes |
|------------|--------|-------|
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
|----|------|--------|-------------|
| ✅ | Polygon validation | Medium | Recorded-session replay fixture validates trade, quote, and aggregate parsing without live network access |

### Medium priority (observability & developer experience)

| ID | Area | Effort | Description |
|----|------|--------|-------------|
| ✅ | OTLP trace visualization docs | Low | `docs/development/otlp-trace-visualization.md` documents collector/export wiring and local Jaeger flow |

### Low priority (architecture debt)

| ID | Area | Effort | Description |
|----|------|--------|-------------|
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
- Security Master should serve as the authoritative instrument-definition layer across research, trading, governance, portfolio, and ledger workflows; Wave 6 mechanics plus cross-workspace productization now deliver that baseline.
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
- **📝 Planned**: Not started; see ROADMAP.md Phase schedule.

---

*Last Updated: 2026-03-31*




