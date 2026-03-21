# Meridian – System Architecture

## Overview

The Meridian is a modular, event-driven system for capturing, validating,
and persisting high-fidelity market microstructure data from multiple data providers
including Interactive Brokers (IB), Alpaca, NYSE Direct, and extensible provider plugins.

The system is designed around strict separation of concerns and is safe to operate
with or without live provider connections. It supports multi-source operation for reconciliation,
provider failover, and data quality monitoring.

### Core Principles

- **Provider-Agnostic Design** – Unified abstraction layer supporting any data source
- **Archival-First Storage** – Write-ahead logging (WAL) ensures crash-safe persistence
- **Type-Safe Domain Models** – F# discriminated unions with exhaustive pattern matching
- **Quality-Driven Operations** – Multi-dimensional data quality scoring and gap repair

The architecture supports multiple deployment modes:
- **Standalone Console Application** – Single-process data collection with local storage
- **WPF Desktop Application** – Recommended Windows desktop app for configuration and monitoring
- **Web Dashboard** – Browser-based monitoring and management interface (ASP.NET Minimal API)

See [Consolidation Refactor Guide](../archived/consolidation.md) for shared UI contracts, storage profiles, pipeline policy, and configuration-service details.

---

## Layered Architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│                           Presentation Layer                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐          │
│  │  Web Dashboard  │  │  WPF Desktop   │  │  Standalone     │          │
│  │  (ASP.NET)      │  │  (Recommended) │  │  (Console)      │          │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘          │
└───────────┼────────────────────┼────────────────────┼────────────────────┘
            │ JSON/FS            │ Config/Status      │ CLI
┌───────────┼────────────────────┼────────────────────────────────────────┐
│           ▼                    ▼                                        │
│                       Application Layer                                  │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Composition | ConfigWatcher | StatusWriter | StatusHttpServer  │   │
│  │  BackfillService | Scheduling | Subscriptions | Metrics         │   │
│  │  EventPipeline | DualPathEventPipeline | IngestionJobService    │   │
│  └─────────────────────────────────┬───────────────────────────────┘   │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │ MarketEvents
┌────────────────────────────────────┼────────────────────────────────────┐
│                          Domain Layer                                    │
│  ┌─────────────────────────────────┴───────────────────────────────┐   │
│  │  TradeDataCollector | MarketDepthCollector | QuoteCollector     │   │
│  │  SymbolSubscriptionTracker                                      │   │
│  │  Domain Models: Trade, LOBSnapshot, BboQuote, OrderFlow, etc.   │   │
│  └─────────────────────────────────┬───────────────────────────────┘   │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │ publish()
┌────────────────────────────────────┼────────────────────────────────────┐
│           Storage Layer                                                  │
│  ┌─────────────────────────────────┴───────────────────────────────┐   │
│  │ JsonlStorageSink | ParquetStorageSink                           │   │
│  │ CatalogSyncSink | CompositeSink                                 │   │
│  │ TierMigrationService | LifecyclePolicyEngine                    │   │
│  │ DataRetentionPolicy | StorageCatalogService                     │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
                  ↑
┌─────────────────┼───────────────────────────────────────────────────────┐
│                 │              Infrastructure Layer                      │
│  ┌──────────────┴───────────────────────────────────────────────────┐  │
│  │ Streaming Providers                  Historical Data Providers    │  │
│  │ ├─ IBMarketDataClient               ├─ AlpacaHistoricalProvider  │  │
│  │ ├─ AlpacaMarketDataClient           ├─ YahooFinanceProvider      │  │
│  │ ├─ NYSEDataSource                   ├─ StooqProvider             │  │
│  │ ├─ StockSharpMarketDataClient       ├─ TiingoProvider            │  │
│  │ ├─ PolygonMarketDataClient          ├─ FinnhubProvider           │  │
│  │ └─ FailoverAwareMarketDataClient    ├─ NasdaqDataLinkProvider   │  │
│  │                                      └─ CompositeProvider        │  │
│  │ Connection Management                (automatic failover)        │  │
│  │ ├─ IBCallbackRouter                                               │  │
│  │ └─ WebSocketResiliencePolicy                                      │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Infrastructure
* Owns all provider-specific code
* **Unified Data Source Abstraction** in `Infrastructure/DataSources/`:
  - `IDataSource` – Base interface for all data sources
  - `IRealtimeDataSource` – Real-time streaming extension (trades, quotes, depth)
  - `IHistoricalDataSource` – Historical data retrieval (bars, dividends, splits)
  - `DataSourceCapabilities` – Declarative capability flags for feature discovery
  - `DataSourceRegistry` – Attribute-based automatic discovery via `[DataSource]`
* **Streaming Provider implementations** in `Infrastructure/Adapters/`:
  - `InteractiveBrokers/IBMarketDataClient` – IB TWS/Gateway connectivity with free equity data support
  - `InteractiveBrokers/IBSimulationClient` – IB simulation mode for testing without live connection
  - `Alpaca/AlpacaMarketDataClient` – Alpaca WebSocket client with IEX/SIP feeds
  - `NYSE/NYSEDataSource` – NYSE Direct connection for real-time and historical US equity data
  - `Polygon/PolygonMarketDataClient` – Polygon adapter (stub implementation)
  - `StockSharp/StockSharpMarketDataClient` – Multi-exchange streaming via StockSharp connectors
  - `Failover/FailoverAwareMarketDataClient` – Automatic provider failover wrapper
* **Historical Data Providers** for backfill operations:
  - `AlpacaHistoricalDataProvider` – Historical OHLCV bars, trades, quotes, and auctions
  - `YahooFinanceHistoricalDataProvider` – Free EOD data for 50K+ global securities
  - `StooqHistoricalDataProvider` – US equities EOD data
  - `TiingoHistoricalDataProvider` – Dividend-adjusted historical data with corporate actions
  - `FinnhubHistoricalDataProvider` – Global securities with fundamentals
  - `AlphaVantageHistoricalDataProvider` – Intraday historical data
  - `PolygonHistoricalDataProvider` – US equities historical data
  - `NasdaqDataLinkHistoricalDataProvider` – Alternative datasets via Quandl API
  - `IBHistoricalDataProvider` – Historical data via Interactive Brokers API
  - `StockSharpHistoricalDataProvider` – Historical data via StockSharp connectors
  - `TwelveDataHistoricalDataProvider` – OHLCV bars for US equities, ETFs, international stocks, forex, and crypto via Twelve Data API
  - `CompositeHistoricalDataProvider` – Automatic failover with rate-limit rotation
  - `BaseHistoricalDataProvider` – Shared base class with common HTTP handling
* **Symbol Search Providers** for symbol resolution:
  - `AlpacaSymbolSearchProvider` – Symbol search via Alpaca Markets
  - `FinnhubSymbolSearchProvider` – Global symbol search
  - `PolygonSymbolSearchProvider` – US equities symbol search
  - `OpenFigiSymbolResolver` – Cross-provider symbol normalization via OpenFIGI
  - `StockSharpSymbolSearchProvider` – Symbol search via StockSharp connectors
* **Provider Base Classes** in `Infrastructure/Adapters/Core/`:
  - `BaseHistoricalDataProvider` – shared HTTP handling, retry, and error mapping for all historical providers
  - `BaseSymbolSearchProvider` – shared base for symbol search providers with pagination and rate-limit handling
  - `WebSocketProviderBase` – shared WebSocket lifecycle, reconnection, and heartbeat management for streaming providers
  - `BackfillProgressTracker` – real-time progress tracking and ETA calculation for backfill operations
  - `ProviderSubscriptionRanges` – utility for splitting large symbol lists into provider-compatible subscription batches
* **Resilience Layer**:
  - `CircuitBreaker` – Open/Closed/HalfOpen states with automatic recovery
  - `ConcurrentProviderExecutor` – Parallel operations with configurable strategies
  - `RateLimiter` – Per-provider rate limit tracking and throttling
  - `WebSocketResiliencePolicy` – Automatic reconnection with exponential backoff
* **Infrastructure Utilities** in `Infrastructure/Utilities/`:
  - `HttpResponseHandler` – Centralized HTTP error handling with structured logging
  - `CredentialValidator` – API credential validation
  - `SymbolNormalization` – Cross-provider symbol normalization
* All streaming providers implement `IMarketDataClient` interface
* All historical providers implement `IHistoricalDataProvider` interface
* `IBCallbackRouter` normalizes IB callbacks into domain updates
* `ContractFactory` resolves symbol configurations to IB contracts
* No domain logic – replaceable / mockable
* **Provider Templates** – `ProviderTemplateFactory` standardizes metadata for UI/monitoring

### Domain
* Pure business logic – deterministic and testable
* `SymbolSubscriptionTracker` – base class providing thread-safe subscription management (registration, unregistration, auto-subscription)
* `TradeDataCollector` – tick-by-tick trades with sequence validation and order-flow statistics
* `MarketDepthCollector` – L2 order book maintenance with integrity checking (extends `SymbolSubscriptionTracker`)
* `QuoteCollector` – BBO state cache and `BboQuote` event emission
* Domain models: `Trade`, `LOBSnapshot`, `BboQuotePayload`, `OrderFlowStatistics`, integrity events

### F# Domain Library (`Meridian.FSharp`)
* **Type-Safe Domain Models** – Discriminated unions with exhaustive pattern matching
  - `MarketEvent` – Trade, Quote, Bar, Depth, Integrity variants
  - `ValidationError` – Rich error types with context
* **Railway-Oriented Validation** – Composable validation with error accumulation
  - `TradeValidator` – Price, size, timestamp, symbol validation
  - `QuoteValidator` – Bid/ask spread, price consistency
* **Pure Functional Calculations**:
  - `SpreadCalculations` – Absolute, percentage, basis points
  - `ImbalanceCalculations` – Order book imbalance metrics
  - `Aggregations` – VWAP, TWAP, microprice, order flow
* **Pipeline Transforms** – Declarative stream processing
  - Filtering, enrichment, aggregation operations
* **C# Interop** – Wrapper classes with nullable-friendly APIs

### Application
* `Program.cs` – thin console bootstrapper that delegates into the shared composition startup layer
* `ConfigWatcher` – hot reload of `appsettings.json`
* `ConfigurationService` – unified wizard, auto-config, validation, and hot reload
* `StatusWriter` – periodic health snapshot to `data/_status/status.json`
* `StatusHttpServer` – lightweight HTTP server for monitoring (Prometheus metrics, JSON status, HTML dashboard)
* `EventSchemaValidator` – validates event schema integrity
* `Metrics` – counters for published, dropped, and integrity events
* **Composition sub-module** (`Application/Composition/`):
  - `ServiceCompositionRoot` – shared DI registration layer used by console, web, desktop, and MCP hosts
  - `HostStartup` – single host graph construction surface used by the shared startup orchestrators
  - `HostAdapters` – host-specific adapter wiring
  - `Composition/Startup/*` – shared startup helpers/orchestrators for config resolution, mode selection, validation, and command dispatch
  - `CircuitBreakerCallbackRouter` – routes circuit-breaker state-change events to monitoring
* **Event Pipeline** (`Application/Pipeline/`):
  - `EventPipeline` – bounded `Channel<MarketEvent>` with configurable capacity and drop policy
  - `EventPipelinePolicy` – shared bounded-channel configuration for pipelines and queues
  - `DualPathEventPipeline` – zero-allocation hot path for `Trade`/`BboQuote` events via struct ring buffers; all other event types fall through to the standard `EventPipeline`
  - `HotPathBatchSerializer` – reusable `ArrayBufferWriter`-backed serializer that writes hot-path struct events to JSONL bytes without heap allocation on the producer thread
  - `PersistentDedupLedger` – JSONL-backed rolling deduplication log that survives restarts; keyed by `(provider, symbol, eventIdentity)` with configurable TTL and in-memory cache
  - `SchemaUpcasterRegistry` – registry for per-event-type schema migration (upcasting) functions
  - `DeadLetterSink` – captures events that fail all downstream sinks for later inspection or replay
  - `DroppedEventAuditTrail` – appends a lightweight audit record for every dropped event for ops visibility
  - `IngestionJobService` – unified ingestion job lifecycle management (create, transition, checkpoint, query) with disk persistence
  - `FSharpEventValidator` – bridges the F# `ValidationPipeline` into the C# pipeline for type-safe pre-storage validation
  - `IEventValidator` – validation contract implemented by F# and C# validators
* **Scheduling sub-module** (`Application/Scheduling/`):
  - `OperationalScheduler` – cron-based task scheduler used by both backfill and archive maintenance
  - `BackfillScheduleManager` – manages named backfill schedules with cron triggers and history
  - `ScheduledBackfillService` – hosted service that executes backfill jobs on schedule
  - `BackfillSchedule` / `BackfillExecutionLog` – schedule definition and execution history models
* **Subscription Orchestration** (`Application/Subscriptions/`):
  - `SubscriptionOrchestrator` – top-level coordinator for symbol subscriptions across all streaming providers
  - `AutoResubscribePolicy` – auto-resubscribes symbols after reconnection
  - `BatchOperationsService` – bulk subscribe/unsubscribe with rate-limited batching
  - `IndexSubscriptionService` – subscribes to index constituents (e.g. S&P 500) automatically
  - `MetadataEnrichmentService` – enriches subscription events with static symbol metadata
  - `PortfolioImportService` – imports watchlists from CSV/JSON portfolio files
  - `SchedulingService` – time-of-day subscription windows (e.g. market hours only)
  - `SymbolImportExportService` – persists and restores symbol lists across sessions
  - `SymbolManagementService` – CRUD operations on the active symbol set
  - `SymbolSearchService` – cross-provider symbol search and resolution
  - `TemplateService` – pre-defined subscription templates (e.g. "US large-cap equities")
  - `WatchlistService` – user watchlist management with persistence
* **Additional Application Services** (`Application/Services/`):
  - `DailySummaryWebhook` – publishes a daily metrics/health summary to a configured webhook
  - `DiagnosticBundleService` – bundles logs, config, and status snapshots for support diagnostics
  - `DryRunService` – validates configuration and connectivity without starting live collection
  - `ErrorTracker` – categorizes and counts operational errors for alerting
  - `FriendlyErrorFormatter` – maps internal error codes to human-readable messages
  - `GracefulShutdownService` – coordinates ordered shutdown of pipeline, sinks, and providers
  - `HistoricalDataQueryService` – queries stored JSONL/Parquet files for historical replay or export
  - `OptionsChainService` – fetches and caches options chain data for derivatives workflows
  - `PreflightChecker` – validates environment, credentials, and connectivity before startup
  - `ProgressDisplayService` – renders live progress bars and status for long-running operations
  - `SampleDataGenerator` – generates synthetic market events for testing and demos
  - `ServiceRegistry` – runtime service locator used by dynamic plugin scenarios
  - `StartupSummary` – prints a structured summary of active configuration at startup
  - `TradingCalendar` – market hours, holiday calendar, and session boundary utilities

### Storage
* **Archival-First Storage Pipeline**:
  - `ArchivalStorageService` – Write-Ahead Logging (WAL) for crash-safe persistence
  - `WriteAheadLog` – Append-only log with checksums and transaction semantics
  - `AtomicFileWriter` – Safe file operations with temp-file rename pattern
* **Storage Sinks**:
  - `JsonlStorageSink` – writes events to append-only JSONL files with retention enforcement
  - `ParquetStorageSink` – columnar storage format for efficient analytics
  - `CatalogSyncSink` – decorator that automatically updates the storage catalog on every flush, keeping directory indexes in sync without periodic rebuild calls
  - `CompositeSink` – fan-out sink that writes events to multiple underlying sinks in parallel
* **Compression & Archival**:
  - `CompressionProfileManager` – Storage tier-optimized compression (LZ4, ZSTD, Gzip)
  - `SchemaVersionManager` – Schema versioning with migration support and JSON Schema export
* **Export System**:
  - `AnalysisExportService` – Pre-built profiles for Python, R, Lean, Excel, PostgreSQL, Arrow/Feather, and XLSX
  - `AnalysisQualityReportGenerator` – Quality metrics with outlier detection and gap analysis
* **File Organization**:
  - `JsonlStoragePolicy` – flexible file organization with multiple naming conventions and date partitioning
  - `JsonlReplayer` – replays captured JSONL events for backtesting (supports gzip compression)
  - `MemoryMappedJsonlReader` – high-throughput JSONL reader using memory-mapped I/O for large replay workloads
  - `TierMigrationService` – moves files between hot/warm/cold storage tiers
  - `LifecyclePolicyEngine` – enforces tier-based lifecycle policies: compression upgrades, tier migrations, and retention
  - `DataRetentionPolicy` – time-based and capacity-based retention enforcement
* **Catalog & Discovery**:
  - `StorageCatalogService` – maintains a queryable root catalog (`_catalog.json`) and per-symbol manifests
  - `SourceRegistry` – maps provider identifiers to their data roots for cross-provider queries
  - `SymbolRegistryService` – tracks all symbols with stored data and their date ranges
  - `StorageSearchService` – searches across the catalog for symbol/date/type combinations
* **Data Quality & Governance**:
  - `DataQualityScoringService` – assigns multi-dimensional quality scores (completeness, accuracy, timeliness) to stored data
  - `DataQualityService` – higher-level quality orchestration wiring scoring and alerting
  - `DataLineageService` – records provenance, transformations, and dependency graphs for stored files
  - `StorageChecksumService` – validates SHA-256 checksums on stored files for integrity assurance
  - `RetentionComplianceReporter` – generates reports on retention compliance and upcoming expirations
  - `QuotaEnforcementService` – monitors and enforces per-symbol and per-provider storage quotas
* **Metadata & Maintenance**:
  - `MetadataTagService` – attaches user-defined key-value tags to stored files with background persistence
  - `FileMaintenanceService` – compaction, re-index, and repair operations on individual files
  - `FilePermissionsService` – validates and corrects file system permissions on data directories
  - `MaintenanceScheduler` – schedules background maintenance tasks with cron expressions
  - `EventBuffer` – in-memory accumulation buffer before batched flush to sinks
  - `ParquetConversionService` – converts existing JSONL archives to Parquet for analytics
* `StorageOptions` – configurable naming conventions, partitioning strategies, retention policies, and capacity limits
* `StorageProfilePresets` – optional presets for research, low-latency, and archival workflows

---

## Event Flow

1. Provider sends raw data (IB callbacks **or** Alpaca WebSocket messages)
2. Provider client normalizes data into domain update structs
3. Domain collectors process updates:
   - `TradeDataCollector.OnTrade(MarketTradeUpdate)`
   - `MarketDepthCollector.OnDepth(MarketDepthUpdate)`
   - `QuoteCollector.OnQuote(MarketQuoteUpdate)`
4. Collectors emit strongly-typed `MarketEvent` objects via `IMarketEventPublisher`
5. `CanonicalizingPublisher` resolves canonical symbols, maps condition codes, and normalizes venue identifiers — see [Deterministic Canonicalization](deterministic-canonicalization.md)
6. `FSharpEventValidator` runs railway-oriented F# validation before events enter the pipeline
7. `EventPipeline` (or `DualPathEventPipeline` for high-throughput Trade/Quote) routes events through a bounded channel to decouple producers from I/O
8. `JsonlStorageSink` appends events as JSONL; `CatalogSyncSink` keeps the storage catalog in sync on every flush
9. `StatusWriter` periodically dumps health snapshots for UI/monitoring

### Event Pipeline Details

* **Bounded channel** – `EventPipeline` uses `System.Threading.Channels` with configurable capacity (default 100,000) and `DropOldest` backpressure policy.
* **Dual-path hot routing** – `DualPathEventPipeline` routes `Trade`/`BboQuote` events through zero-allocation struct ring buffers (`SpscRingBuffer<T>`), bypassing `MarketEvent` heap allocation entirely. Events that do not fit fall back to the standard `EventPipeline` without data loss.
* **Persistent deduplication** – `PersistentDedupLedger` provides a JSONL-backed dedup log that survives restarts, keyed by `(provider, symbol, eventIdentity)` with TTL-based eviction.
* **Storage policy** – `JsonlStoragePolicy` supports multiple file organization strategies:
  - **BySymbol**: `{root}/{symbol}/{type}/{date}.jsonl` (default)
  - **ByDate**: `{root}/{date}/{symbol}/{type}.jsonl`
  - **ByType**: `{root}/{type}/{symbol}/{date}.jsonl`
  - **Flat**: `{root}/{symbol}_{type}_{date}.jsonl`
* **Date partitioning** – Daily (default), Hourly, Monthly, or None
* **Compression** – Optional gzip compression for all JSONL files
* **Metrics** – `Metrics.Published`, `Metrics.Dropped`, `Metrics.Integrity` track event throughput and data quality.

### Canonicalization Pipeline

The canonicalization pipeline runs between domain event emission and the `EventPipeline`, enriching each `MarketEvent` with normalized identifiers before storage:

* **`CanonicalizingPublisher`** – `IMarketEventPublisher` decorator that transparently applies canonicalization to every event. Supports:
  - **Dual-write mode (Phase 2)** – Publishes both the raw event and the canonicalized version for parity validation
  - **Canonical-only mode (Phase 3)** – Publishes only the enriched event
  - **Pilot symbol filtering** – Limits canonicalization to a configurable subset during rollout; non-pilot events pass through unchanged
* **`EventCanonicalizer`** – Core transformation logic:
  - Resolves `CanonicalSymbol` via `CanonicalSymbolRegistry` (ISIN, FIGI, alias, provider-mapping lookups)
  - Maps provider-specific condition codes to standardized codes via `ConditionCodeMapper` (CTA plan codes, Polygon numeric codes, IB free-text)
  - Normalizes exchange/venue identifiers to MIC codes via `VenueMicMapper`
  - Stamps `CanonicalizationVersion` on each enriched event for schema pinning
* **`CanonicalizationMetrics`** – Thread-safe lock-free counters for success, soft-fail, hard-fail, and dual-write events; per-provider parity dashboards expose match-rate percentages

See [Deterministic Canonicalization](deterministic-canonicalization.md) for the full design and rollout phases.

### Quote/BBO Path

* `QuoteCollector` maintains the latest BBO per symbol and emits `BboQuote` events with bid/ask price/size, spread, and mid-price when both sides are valid.
* Multiple providers can supply quote updates; sequence numbers (`SequenceNumber`) and stream identifiers (`StreamId`) are preserved to support reconciliation.
* `TradeDataCollector` uses `IQuoteStateStore` (implemented by `QuoteCollector`) to infer aggressor side when the upstream feed provides `AggressorSide.Unknown`.

### Resilience and Integrity

* **Trade sequence validation** – `TradeDataCollector` emits `IntegrityEvent.OutOfOrder` or `IntegrityEvent.SequenceGap` when sequence numbers regress or skip; trades causing out-of-order are rejected.
* **Depth integrity** – `MarketDepthCollector` freezes a symbol and emits `DepthIntegrityEvent` if insert/update/delete operations target invalid positions; operators must call `ResetSymbolStream` to resume.
* **Config hot reload** – `ConfigWatcher` listens for changes to `appsettings.json` and resubscribes symbols without process restart.
* **Pluggable data source** – the `IMarketDataClient` abstraction allows switching between different providers via the `DataSource` configuration option.

---

## Monitoring and Observability

### StatusHttpServer

The `StatusHttpServer` component provides lightweight HTTP-based monitoring without requiring ASP.NET:

* **Prometheus metrics** (`/metrics`) – Exposes counters and gauges in Prometheus text format:
  - `mdc_published` – Total events published
  - `mdc_dropped` – Events dropped due to backpressure
  - `mdc_integrity` – Integrity validation events
  - `mdc_trades`, `mdc_depth_updates`, `mdc_quotes` – Event type counters
  - `mdc_events_per_second` – Current throughput rate
  - `mdc_drop_rate` – Drop rate percentage

* **JSON status** (`/status`) – Machine-readable status including:
  - Current metrics snapshot
  - Pipeline statistics (channel depth, backpressure state)
  - Recent integrity events with timestamps and descriptions

* **HTML dashboard** (`/`) – Auto-refreshing browser dashboard showing:
  - Real-time metrics display
  - Table of recent integrity events
  - Links to Prometheus and JSON endpoints

The server uses `HttpListener` to avoid ASP.NET overhead, making it suitable for lightweight deployments and embedded scenarios.

### Event Schema Validation

The `EventSchemaValidator` component validates that emitted events conform to expected schemas, catching serialization issues and schema drift early.

### Distributed Tracing (OpenTelemetry)

`OpenTelemetrySetup` integrates the OpenTelemetry SDK for distributed tracing across the full market data pipeline:

* **Activity source** – `Meridian` named activity source propagates trace context from provider through collector to storage
* **`TracedEventMetrics`** – Wraps `IEventMetrics` to emit OTEL span events alongside Prometheus counters, enabling trace-linked latency analysis
* Supports OTLP, Jaeger, or console exporters via configuration

### Advanced Monitoring Components

The `Application/Monitoring/Core/` sub-module provides a production-grade alerting and SLO framework:

* **`AlertDispatcher`** – Centralized alert publishing with subscription management; tracks recent alerts and per-category/source statistics
* **`AlertRunbookRegistry`** – Maps alert rule IDs to operator runbook sections for automated escalation guidance
* **`HealthCheckAggregator`** – Aggregates health-check results from all sub-systems into a composite status
* **`SloDefinitionRegistry`** – Runtime registry of Service Level Objectives per sub-system; provides programmatic compliance scoring and maps SLOs to alert thresholds

Additional monitoring services in `Application/Monitoring/`:

* **`BadTickFilter`** – Filters out obvious data-quality anomalies (e.g. zero prices, extreme outliers) before events reach the pipeline
* **`CircuitBreakerStatusService`** – Exposes circuit-breaker open/closed state per provider to the health API
* **`ClockSkewEstimator`** – Estimates provider-to-collector clock skew using exchange vs. received timestamps
* **`ConnectionStatusWebhook`** – Publishes provider connection-state changes to a configured webhook URL
* **`DataLossAccounting`** – Tracks dropped-event counts by provider and symbol for audit purposes
* **`DetailedHealthCheck`** – Comprehensive health check that validates pipeline depth, storage access, and provider connectivity
* **`ErrorRingBuffer`** – Fixed-size ring buffer of recent operational errors for the monitoring dashboard
* **`SchemaValidationService`** – Validates stored JSONL lines against registered JSON Schema versions
* **`SystemHealthChecker`** – Aggregates CPU, memory, disk, and queue-depth metrics into an overall health status
* **`TickSizeValidator`** – Validates that trade prices conform to the minimum tick size for each instrument
* **`TimestampMonotonicityChecker`** – Detects backward-jumping timestamps within a symbol stream
* **`ValidationMetrics`** – Counters for validation pass/fail/skip outcomes by event type and provider

---

## Storage Management

### Retention Policies

Storage retention is enforced eagerly during writes by `JsonlStorageSink`:

* **Time-based retention** – `RetentionDays` configuration automatically deletes files older than the specified window
* **Capacity-based retention** – `MaxTotalBytes` configuration enforces a storage cap by removing oldest files first when the limit is exceeded
* Retention enforcement runs during each write operation, keeping disk usage predictable

### File Organization

The `FileNamingConvention` enum provides four organization strategies optimized for different access patterns:

1. **BySymbol** – Best for analyzing individual symbols across time
2. **ByDate** – Best for daily batch processing and archival workflows
3. **ByType** – Best for analyzing specific event types (trades, quotes) across all symbols
4. **Flat** – Simplest structure for small datasets and ad-hoc analysis

Date partitioning strategies (`DatePartition` enum) allow fine-tuning file granularity:
- **None** – Single file per symbol/type (append-only)
- **Daily** – One file per day (default, balances file size and access)
- **Hourly** – High-volume scenarios requiring smaller files
- **Monthly** – Long-term storage with less granular access

### Data Replay

The `JsonlReplayer` and `MemoryMappedJsonlReader` components enable backtesting and analysis by streaming previously captured events:

* **`JsonlReplayer`** – reads JSONL files in chronological order, automatically decompresses gzip (`.jsonl.gz`) files, and deserializes events back into strongly-typed `MarketEvent` objects. Supports filtering through standard LINQ operations.
* **`MemoryMappedJsonlReader`** – high-throughput alternative that uses memory-mapped I/O for large replay workloads, reducing GC pressure compared to stream-based reads.

Example usage:
```csharp
var replayer = new JsonlReplayer("./data");
await foreach (var evt in replayer.ReadEventsAsync(cancellationToken))
{
    // Process historical event
    if (evt.EventType == MarketEventType.Trade)
    {
        // Analyze trade
    }
}
```

### Portable Data Packaging

The `PortableDataPackager` (in `Storage/Packaging/`) creates self-contained, portable archives for sharing and archival:

* Bundles data files, quality reports, data dictionaries, and loader scripts into a single ZIP archive
* Includes auto-generated Python, R, and SQL import scripts for popular analysis environments
* SHA-256 manifest for end-to-end integrity verification
* Progress events via `ProgressChanged` for UI integration
* Supports both creation (`CreatePackageAsync`) and validated import (`ImportPackageAsync`) workflows

See `docs/operations/portable-data-packager.md` for usage details.

### Scheduled Archive Maintenance

`ScheduledArchiveMaintenanceService` (in `Storage/Maintenance/`) runs background maintenance tasks on a configurable schedule:

* Compaction, re-compression, and integrity verification of archive tiers
* `ArchiveMaintenanceScheduleManager` manages task schedules with cron-style triggers via `OperationalScheduler`
* `IMaintenanceExecutionHistory` persists execution results for audit and diagnostics

---

## Historical Data Backfill

The system supports historical data backfill from multiple providers with automatic failover:

### Provider Priority

| Priority | Provider | Data Type | Notes |
|----------|----------|-----------|-------|
| 5 | **NYSE Direct** | OHLCV bars, dividends, splits | Exchange-direct with adjustments |
| 5 | **Alpaca** | OHLCV bars, trades, quotes | IEX/SIP feeds with adjustments |
| 10 | **Yahoo Finance** | OHLCV bars | 50K+ global securities, free |
| 20 | **Stooq** | EOD bars | US equities |
| 30 | **Nasdaq Data Link** | Various | Alternative datasets |

### Backfill Features

* **Priority Backfill Queue** – Sophisticated job scheduling with priority levels:
  - Critical (0), High (10), Normal (50), Low (100), Deferred (200)
  - Dependency chains for ordered execution
  - Batch enqueue with automatic prioritization
* **Composite Provider** – Automatic failover when primary provider fails or hits rate limits
* **Rate-Limit Rotation** – Switches providers when approaching API limits
* **Gap Detection & Repair** – `DataGapRepairService` with automatic repair:
  - `DataGapAnalyzer` identifies missing data periods
  - Multi-provider gap repair with fallback
  - Gap types: Missing, Partial, Holiday, Suspicious
* **Data Quality Monitoring** – Multi-dimensional quality scoring:
  - Completeness (30%), Accuracy (25%), Timeliness (20%)
  - Consistency (15%), Validity (10%)
  - Quality grade: A+ to F with alerts
* **Fill-Only Mode** – Skip dates with existing data
* **Job Persistence** – Resume interrupted backfills after restart
* **Progress Tracking** – Real-time progress and ETA via API/dashboard

---

## QuantConnect Lean Integration

The system integrates with QuantConnect's Lean algorithmic trading engine for backtesting:

* **Custom Data Types** – `MeridianTradeData`, `MeridianQuoteData`
* **Data Provider** – `MeridianDataProvider` implements Lean's `IDataProvider`
* **Sample Algorithms** – Spread arbitrage, order flow strategies
* **JSONL Reader** – Automatic decompression and parsing of collected data

See [lean-integration.md](../integrations/lean-integration.md) for detailed integration guide.

---

---

## Archival Storage Pipeline

The system implements an archival-first storage strategy for crash-safe persistence:

### Write-Ahead Logging (WAL)

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Ingest    │────►│     WAL     │────►│   Buffer    │────►│   Storage   │
│   Events    │     │  (Durable)  │     │  (Memory)   │     │  (JSONL/    │
│             │     │             │     │             │     │   Parquet)  │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
                           │                                       │
                           │         ┌─────────────────────────────┘
                           ▼         ▼
                    ┌─────────────────────┐
                    │   Commit Point      │
                    │   (Acknowledge)     │
                    └─────────────────────┘
```

* **Crash Recovery** – Uncommitted records recovered on restart
* **Configurable Sync** – NoSync, BatchedSync, or EveryWrite modes
* **Per-Record Checksums** – SHA256 integrity validation
* **Automatic Truncation** – Old WAL segments cleaned after commit

### Compression Profiles

| Profile | Codec | Level | Throughput | Ratio | Use Case |
|---------|-------|-------|------------|-------|----------|
| Real-Time | LZ4 | 1 | ~500 MB/s | 2.5x | Hot data collection |
| Warm Archive | ZSTD | 6 | ~150 MB/s | 5x | Recent archives |
| Cold Archive | ZSTD | 19 | ~20 MB/s | 10x | Long-term storage |
| High-Volume | ZSTD | 3 | ~300 MB/s | 4x | SPY, QQQ, etc. |
| Portable | Gzip | 6 | ~80 MB/s | 3x | External sharing |

### Schema Versioning

* Semantic versioning for all event types (e.g., Trade v1.0.0, v2.0.0)
* Automatic migration between schema versions
* JSON Schema export for external tool compatibility
* Schema registry with version history

---

## Credential Management

The system supports multiple credential sources with priority resolution:

1. **Environment Variables** – `NYSE_API_KEY`, `ALPACA_API_KEY`, etc. (recommended for production)
2. **Windows Credential Store** – Platform credential manager via `CredentialService` in the WPF desktop app
3. **Configuration File** – `appsettings.json` (development only)

Note: Cloud secret managers (Azure Key Vault, AWS Secrets Manager) are not currently implemented.

### Credential Testing & Expiration

* **Validation on Startup** – Credentials tested before data collection begins
* **Expiration Tracking** – Alerts for expiring API keys
* **Rotation Support** – Hot-swap credentials without restart

---

## Performance Optimizations

The system includes several high-performance features:

* **Source-Generated JSON** – 2-3x faster serialization via `MarketDataJsonContext`
* **Bounded Channel Pipeline** – Async event processing with configurable backpressure
* **Parallel Provider Execution** – Concurrent backfill across multiple providers
* **Connection Warmup** – Pre-JIT critical paths and connection pooling

---

**Version:** 1.7.0
**Last Updated:** 2026-03-18
**See Also:** [c4-diagrams.md](c4-diagrams.md) | [domains.md](domains.md) | [deterministic-canonicalization.md](deterministic-canonicalization.md) | [why-this-architecture.md](why-this-architecture.md) | [provider-management.md](provider-management.md) | [F# Integration](../integrations/fsharp-integration.md) | [ADR Index](../adr/README.md)
