# Structural Improvements Analysis

**Date:** 2026-02-07
**Version Analyzed:** 1.6.1
**Focus:** Modularity, abstraction clarity, extensibility, development process, UX

---

## Summary

This analysis identifies 15 high-impact structural improvements organized into four themes. Each item includes the problem, concrete remedy, affected files, and expected benefit. Items are ranked by impact-to-effort ratio within each theme.

Companion document: `docs/IMPROVEMENTS.md` covers feature-level and reliability improvements.

---

## A. Modularity & Abstraction Clarity

### A1. Unify Provider Creation into a Single Extensible Registry

**Problem:** Three separate provider creation mechanisms exist and compete:

1. `MarketDataClientFactory` — switch-based factory (`Infrastructure/Providers/MarketDataClientFactory.cs:81-91`)
2. `ProviderFactory` — parallel factory system (`Infrastructure/Providers/Core/ProviderFactory.cs`)
3. Direct instantiation in `Program.cs` (lines 296-298)

Adding a new streaming provider requires changes in all three locations. The `[DataSource]` attribute discovery system (ADR-005) is implemented in `DataSourceAttribute.cs` but never wired into the main startup path.

**Remedy:**
- Merge `MarketDataClientFactory` and `ProviderFactory` into a single `ProviderRegistry` that uses `[DataSource]` attribute scanning to discover providers at startup
- Replace the `switch` statement with a `Dictionary<DataSourceKind, Func<IMarketDataClient>>` populated by reflection or explicit registration
- Remove direct instantiation from `Program.cs`; resolve all providers through DI

**Files:** `MarketDataClientFactory.cs`, `ProviderFactory.cs`, `Program.cs:278-298`, `DataSourceAttribute.cs`, `DataSourceRegistry.cs`

**Benefit:** Adding a new provider becomes: implement interface, add attribute, done. No factory or startup code changes.

---

### A2. Consolidate DI and Direct Instantiation into a Single Composition Path

**Problem:** `ServiceCompositionRoot.cs` registers services into DI, but `Program.cs` bypasses DI for critical components:

- Collectors (`QuoteCollector`, `TradeDataCollector`, `MarketDepthCollector`) are created via `new` at `Program.cs:278-281`
- Storage pipeline (`JsonlStoragePolicy`, `JsonlStorageSink`, `WriteAheadLog`, `EventPipeline`) is created via `new` at `Program.cs:213-224`
- Configuration is loaded twice: once minimal (line 57), once full (line 69)

This means the DI registrations in `ServiceCompositionRoot.cs` (lines 525-529 for pipeline, lines 440-460 for collectors) are dead code in the main startup path.

**Remedy:**
- Move all object creation in `Program.cs` behind `HostStartup` or `ServiceCompositionRoot`
- Use `IServiceProvider.GetRequiredService<T>()` consistently
- Cache the loaded `AppConfig` after the first load; pass it to DI as a singleton

**Files:** `Program.cs:57-69, 213-281`, `ServiceCompositionRoot.cs:440-529`, `HostStartup.cs`

**Benefit:** One composition path. DI registrations become the source of truth. Testable via service replacement.

---

### A3. Extract WebSocket Provider Base Class Usage

Historical note (2026-03-21): this archived recommendation predates the later provider-lifecycle cleanup. Polygon and NYSE were valid shared-lifecycle targets, but StockSharp is now explicitly treated as a connector-runtime exception rather than a direct `WebSocketProviderBase` migration candidate.

**Problem:** `WebSocketProviderBase` exists (`Infrastructure/Shared/WebSocketProviderBase.cs`) with connection lifecycle, heartbeat, resilience pipeline, and reconnection logic. However, none of the major WebSocket providers use it:

- Polygon (1,263 lines) manages `ClientWebSocket` directly with custom reconnection
- NYSE manages `ClientWebSocket` directly with separate `CancellationTokenSource`
- StockSharp (1,325 lines) has custom task-based reconnection
- Only Alpaca uses the separate `WebSocketConnectionManager` helper

Approximately 800+ lines of WebSocket management are duplicated across these providers.

**Remedy:**
- Refactor Polygon and NYSE to extend `WebSocketProviderBase`; do not force StockSharp into the same inheritance path unless a connector-oriented abstraction proves it is the right fit
- Override `ConnectionUri`, `ProviderName`, and message handling hooks
- Move reconnection, heartbeat, and receive loop logic into the base class
- Keep provider-specific authentication and message parsing in subclasses

**Files:** `WebSocketProviderBase.cs`, `PolygonMarketDataClient.cs`, `NYSEDataSource.cs` (historical draft also listed `StockSharpMarketDataClient.cs`, but that is no longer the preferred target)

**Benefit:** Eliminates duplicated raw-WebSocket connection management where that pattern actually applies. StockSharp follow-up, if any, should focus on connector-runtime consolidation rather than pretending it is the same class of transport.

---

### A4. Inject Metrics Instead of Using Static Globals

**Problem:** `EventPipeline` calls `Metrics.IncPublished()` and `Metrics.IncDropped()` via static methods (`EventPipeline.cs:282, 312`). `PrometheusMetrics` is also statically accessible. This prevents substitution in tests and couples the pipeline to a specific metrics backend.

**Remedy:**
- Extract `IEventMetrics` interface with `IncPublished()`, `IncDropped()`, `IncConsumed()`, `IncRecovered()`
- Inject `IEventMetrics` into `EventPipeline` via constructor
- Default implementation delegates to existing `Metrics`/`PrometheusMetrics` static classes
- Tests can supply a no-op or counting implementation

**Files:** `EventPipeline.cs:32-34, 282, 312`, `Application/Monitoring/Metrics.cs`, `PrometheusMetrics.cs`

**Benefit:** Pipeline becomes testable without side effects. Opens the door to alternative metrics backends (OpenTelemetry, StatsD).

---

### A5. Standardize Error Handling Strategy

**Problem:** The codebase uses three concurrent error handling approaches:

1. **Exceptions** — 9 custom exception types in `Core/Exceptions/`
2. **Result<T, TError>** — functional result type in `Application/Results/Result.cs`
3. **Environment.Exit(1)** — hard exits in `Program.cs` (lines 493-502)

These are used inconsistently: providers throw exceptions, some services return `Result<T>`, and startup code calls `Environment.Exit()`. Exception chaining is sometimes lost (e.g., `ConfigurationException` constructed with `null` inner exception when wrapping `UnauthorizedAccessException`).

**Remedy:**
- Adopt a single convention: exceptions for unrecoverable/unexpected errors, `Result<T>` for expected failures in domain operations
- Document the convention in `CLAUDE.md` coding conventions
- Replace `Environment.Exit(1)` calls with throwing `ConfigurationException` and catching at the top-level
- Fix exception chaining to always pass the original exception as `innerException`

**Files:** `Program.cs:493-502`, `Result.cs`, `Core/Exceptions/*.cs`, `ConfigurationService.cs`

**Benefit:** Predictable error propagation. Callers know what to expect. No silent process termination.

---

## B. Extensibility & Development Process

### B1. Extract CLI Argument Parsing into a Shared Helper

**Problem:** Each command in `Application/Commands/` re-implements argument parsing inline. Pattern like `GetArgValue(args, "--flag")` is duplicated across `SymbolCommands`, `PackageCommands`, `ConfigCommands`, `DiagnosticsCommands`, etc.

**Remedy:**
- Create `CliArgumentParser` utility class with: `HasFlag(args, flag)`, `GetValue(args, flag)`, `GetValues(args, flag)` (for comma-separated lists), `GetDateValue(args, flag)`
- Refactor all `ICliCommand` implementations to use it
- Consider a lightweight `ParsedArgs` record that each command receives pre-parsed

**Files:** `Application/Commands/*.cs` (9 files), new `Application/Commands/CliArgumentParser.cs`

**Benefit:** Eliminates parsing duplication. Consistent error messages for malformed arguments. Easier to add new commands.

---

### B2. Add Integration Tests for HTTP Endpoints

**Problem:** The HTTP API layer (66 mapped endpoints) has no integration tests using `WebApplicationFactory<T>`. Only `EndpointStubDetectionTests.cs` validates route format. This is the single highest-value open item from the existing improvements analysis (item #7).

**Remedy:**
- Add `Microsoft.AspNetCore.Mvc.Testing` package
- Create `EndpointIntegrationTestBase` with shared `WebApplicationFactory<T>` setup
- Write tests for core endpoints: `/api/status`, `/api/health`, `/api/config`, `/api/backfill/*`, `/api/providers/*`
- Assert status codes, content types, and response schema shapes
- Include negative cases (invalid input, missing config, auth failures)

**Files:** New `tests/Meridian.Tests/Integration/EndpointTests/`, existing `EndpointStubDetectionTests.cs`

**Benefit:** Prevents regressions in the growing API surface. Validates endpoint-to-service wiring.

---

### B3. Add Infrastructure Provider Unit Tests

**Problem:** The infrastructure layer has 55 provider implementation files but only 8 test files covering them (ratio of ~369 LOC per test file). Major streaming providers (Alpaca core logic, NYSE, StockSharp) have no dedicated unit tests. Historical providers have minimal coverage given 26 implementation files.

**Remedy:**
- Prioritize tests for providers with the most code: StockSharp (1,325 lines), Polygon (1,263 lines)
- Test message parsing (deserialize a sample WebSocket frame, verify correct domain event)
- Test subscription management (subscribe, unsubscribe, resubscribe-on-reconnect)
- Test error handling paths (connection failure, malformed message, rate limit)
- Use recorded WebSocket message fixtures to avoid live API dependencies

**Files:** New tests in `tests/Meridian.Tests/Infrastructure/Providers/`

**Benefit:** Catches parsing regressions when provider APIs change. Enables safe refactoring of provider internals.

---

### B4. Introduce a Plugin Architecture for Storage Sinks

**Problem:** `EventPipeline` accepts a single `IStorageSink` (`EventPipeline.cs:33`). Multi-sink scenarios (e.g., write to JSONL and Parquet simultaneously, or JSONL + real-time analytics sink) require external composition before constructing the pipeline. There's no built-in way to fan out events to multiple sinks.

**Remedy:**
- Create `CompositeSink : IStorageSink` that wraps `IReadOnlyList<IStorageSink>` and fans out `AppendAsync` calls
- Register sinks in DI as `IEnumerable<IStorageSink>`; compose via `CompositeSink` in `ServiceCompositionRoot`
- Optionally support per-sink filtering (e.g., only trades to Parquet, all events to JSONL)

**Files:** New `Storage/Sinks/CompositeSink.cs`, `ServiceCompositionRoot.cs:525-529`, `EventPipeline.cs:33`

**Benefit:** Multi-format storage without pipeline changes. New sinks (CSV, database, cloud) can be added and composed independently.

---

## C. Functionality & Feature Completeness

### C1. Eliminate WPF/UWP Service Duplication

**Problem:** 25-30 services are nearly identical between `Meridian.Wpf/Services/` (43 files, 12,338 lines) and `Meridian.Uwp/Services/` (29 files, 16,553 lines). Examples: `ThemeService`, `ConfigService`, `NotificationService`, `NavigationService`, `ConnectionService`. The only differences are singleton patterns (WPF uses `Lazy<T>`, UWP uses `lock`-based) and minor platform API calls.

**Remedy:**
- Move shared service interfaces (`IThemeService`, `IConfigService`, `INotificationService`, etc.) to `Meridian.Ui.Services`
- Move shared implementations to `Meridian.Ui.Services` with constructor-injected platform abstractions
- Keep only platform-specific adapters in WPF/UWP projects
- WPF already has 15 interface definitions; promote them to the shared project

**Files:** `Wpf/Services/*.cs`, `Uwp/Services/*.cs`, `Ui.Services/`

**Benefit:** Eliminates ~10,000 lines of duplicated code. Bug fixes and features propagate to both platforms automatically.

---

### C2. Consolidate Configuration Validation

**Problem:** Configuration validation is spread across three separate classes with overlapping responsibilities:

1. `ConfigValidationHelper` — field-level validation
2. `ConfigValidatorCli` — CLI-oriented validation with output formatting
3. `PreflightChecker` — pre-startup validation including connectivity

There's no clear contract for what each validates or when it runs.

**Remedy:**
- Define `IConfigValidator` with `Validate(AppConfig) -> ValidationResult[]`
- Implement as a pipeline: `FieldValidator` -> `SemanticValidator` -> `ConnectivityValidator`
- `ConfigValidatorCli` becomes a formatter over `IConfigValidator` results
- `PreflightChecker` delegates to the validator pipeline, adding only startup-specific checks

**Files:** `Application/Config/ConfigValidationHelper.cs`, `Application/Config/ConfigValidatorCli.cs`, `Application/Services/PreflightChecker.cs`

**Benefit:** Single validation pipeline. Clear ordering. Easy to add new validation rules.

---

### C3. Expose Drop Statistics and Quality Metrics via API

**Problem:** `DroppedEventAuditTrail` collects detailed drop statistics but doesn't expose them via HTTP. The `DataQualityMonitoringService` computes completeness, gap, and anomaly metrics internally but several quality endpoints remain stubs.

**Remedy:**
- Implement `GET /api/quality/drops` returning `DroppedEventStatistics` (total, per-symbol, recent events)
- Implement `GET /api/quality/drops/{symbol}` for per-symbol drill-down
- Wire `DataQualityMonitoringService` metrics into existing quality endpoint stubs (`/api/quality/dashboard`, `/api/quality/metrics`)
- Include drop rate in the `/api/status` response

**Files:** `Ui.Shared/Endpoints/StubEndpoints.cs` (move routes to real handlers), `Application/Pipeline/DroppedEventAuditTrail.cs`, `Application/Monitoring/DataQuality/DataQualityMonitoringService.cs`

**Benefit:** Completes the observability story. Dashboards can display data quality in real time.

---

## D. User Experience

### D1. Consolidate UWP Navigation to Match WPF Workspace Model

**Problem:** WPF has been organized into 5 workspaces (Monitor, Collect, Storage, Quality, Settings) with ~15 navigation items and a command palette (Ctrl+K). UWP still has 40+ pages in a flat navigation list, making it overwhelming for users.

**Remedy:**
- Group UWP `NavigationViewItem` elements into the same 5 workspace categories
- Reduce visible navigation items to ~15 (one per logical page group)
- Use `NavigationViewItem.MenuItems` for sub-pages within each workspace
- Port the command palette (Ctrl+K) to UWP for keyboard-driven navigation

**Files:** `Uwp/Views/MainPage.xaml`, `Uwp/Services/NavigationService.cs`

**Benefit:** Consistent cross-platform UX. Users switching between WPF and UWP find the same structure.

---

### D2. Add a Unified CLI Help System with Examples

**Problem:** `HelpCommand` (249 lines) displays a wall of flags. Users must read the full output to find what they need. No contextual help, no examples per command category, no `--help backfill` sub-command support.

**Remedy:**
- Support `--help <topic>` for focused help on specific areas: `--help backfill`, `--help symbols`, `--help config`, `--help storage`
- Each topic shows a 2-3 line description, available flags, and 1-2 copy-paste examples
- Default `--help` (no topic) shows a summary with topic list
- Draw from the existing comprehensive documentation in `docs/HELP.md`

**Files:** `Application/Commands/HelpCommand.cs`, `Application/Commands/CliArguments.cs`

**Benefit:** Users find relevant help faster. Reduces support burden and documentation lookups.

---

## Priority Matrix

| # | Improvement | Impact | Effort | Priority |
|---|-----------|--------|--------|----------|
| A1 | Unified provider registry | High | Medium | **P1** |
| A2 | Single DI composition path | High | Medium | **P1** |
| A3 | WebSocket base class adoption | High | High | **P2** |
| A4 | Injectable metrics | Medium | Low | **P1** |
| A5 | Standardized error handling | Medium | Medium | **P2** |
| B1 | CLI argument parser | Low-Med | Low | **P2** |
| B2 | HTTP endpoint integration tests | High | Medium | **P1** |
| B3 | Infrastructure provider tests | High | High | **P2** |
| B4 | Composite storage sink | Medium | Low | **P1** |
| C1 | WPF/UWP service deduplication | High | High | **P2** |
| C2 | Consolidated config validation | Medium | Low | **P1** |
| C3 | Quality metrics API endpoints | Medium | Low | **P1** |
| D1 | UWP navigation consolidation | Medium | Medium | **P3** |
| D2 | Contextual CLI help | Low-Med | Low | **P2** |

### Recommended Execution Order

**Phase 1 — Quick wins with high structural impact:**
1. A4 — Injectable metrics (low effort, unblocks testability)
2. C2 — Consolidated config validation (low effort, cleaner startup)
3. B4 — Composite storage sink (low effort, new capability)
4. C3 — Quality metrics API (low effort, immediate UX value)

**Phase 2 — Core architecture alignment:**
5. A1 — Unified provider registry (eliminates triple-path creation)
6. A2 — Single DI composition path (removes dead DI code, single source of truth)
7. B2 — HTTP endpoint integration tests (regression safety for the API surface)

**Phase 3 — Larger refactors with high payoff:**
8. A3 — WebSocket base class adoption (eliminates ~800 lines of duplication)
9. C1 — WPF/UWP service deduplication (eliminates ~10,000 lines)
10. A5 + B1 + B3 + D1 + D2 — Remaining items in parallel as capacity allows
