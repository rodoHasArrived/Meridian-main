# Concrete Refactor Map (Dependency-Safe)

**Goal:** Reduce implementation complexity while preserving runtime behavior and public functionality.

**Scope constraints:**
- Preserve all existing CLI commands, API routes, and provider capabilities.
- Keep architecture layer boundaries intact.
- Prefer additive adapters first, then swaps, then cleanup.

## Risk Scale

- **1-2 (Low):** Localized internal refactor, easy rollback.
- **3 (Medium):** Cross-project wiring changes, test updates likely.
- **4 (High):** Runtime behavior surface impacted, migration sequencing critical.
- **5 (Very High):** Broad architecture migration; requires staged rollout + feature flags.

---

## Phase 0 — Baseline & Safety Rails ✅ COMPLETE

### Step 0.1 — Lock baseline behavior snapshots ✅
- **Status:** Complete.
- **What was done:**
  - 18 integration endpoint test files covering status, health, config, backfill, provider, storage, symbol, maintenance, failover, quality, and negative-path endpoints.
  - `ResponseSchemaSnapshotTests` and `ResponseSchemaValidationTests` validate JSON schema structure (fields, types, required keys).
  - Provider message parsing tests for Polygon, NYSE, StockSharp.
- **Key files:**
  - `tests/Meridian.Tests/Integration/EndpointTests/*` (18 files)
  - `tests/Meridian.Tests/Infrastructure/Adapters/*` (12 files)

### Step 0.2 — Add temporary observability counters for migration ✅
- **Status:** Complete.
- **What was done:**
  - `MigrationDiagnostics` static class with factory hit counts (streaming, backfill, symbol search), reconnect counters (attempts, successes, failures by provider), resubscribe counters, and registration counters.
  - `GetSnapshot()` returns immutable record for monitoring.
- **Key files:**
  - `src/Meridian.Core/Monitoring/MigrationDiagnostics.cs`

---

## Phase 1 — Unify Provider Construction (No Feature Change) ✅ COMPLETE

### Step 1.1 — Introduce `ProviderRegistry` abstraction ✅
- **Status:** Complete.
- **What was done:**
  - `ProviderRegistry` in `Infrastructure/Adapters/Core/` serves as the single source of truth for all provider types.
  - Streaming factories registered as `ConcurrentDictionary<string, Func<IMarketDataClient>>`, keyed by lower-case provider IDs.
  - Universal queries: `GetAllProviderMetadata()`, `GetProvider<T>()`, `GetProviders<T>()`, `GetBestAvailableProviderAsync<T>()`.
  - `IProviderMetadata` unified identity and capabilities contract.
- **Key files:**
  - `src/Meridian.Infrastructure/Adapters/Core/ProviderRegistry.cs`

### Step 1.2 — Wire attribute-based discovery into registry (behind switch) ✅
- **Status:** Complete.
- **What was done:**
  - Added `ProviderRegistryConfig` record with `UseAttributeDiscovery` flag to `AppConfig`.
  - `ServiceCompositionRoot.AddProviderServices()` checks `config.ProviderRegistry?.UseAttributeDiscovery` flag.
  - When true, `RegisterStreamingFactoriesFromAttributes()` iterates `DataSourceRegistry.Sources` to auto-register `IMarketDataClient` implementations via `[DataSource]` attribute discovery.
  - Attribute-discovered providers register by `source.Id` directly; `DataSourceKind` remains only a compatibility type at the config boundary.
  - Default: false (manual lambda registration preserved as fallback).
- **Key files:**
  - `src/Meridian.Core/Config/AppConfig.cs` (added `ProviderRegistryConfig`)
  - `src/Meridian.Application/Composition/ServiceCompositionRoot.cs` (added discovery methods)

### Step 1.3 — Remove direct provider instantiation from host startup ✅
- **Status:** Complete.
- **What was done:**
  - `HostStartup.CreateStreamingClient()` delegates to `ProviderRegistry.CreateStreamingClient()`.
  - `ServiceCompositionRoot` remains the single source of truth for DI registration.
  - The shared startup/orchestration layer under `Application/Composition/Startup/` now owns config-path resolution, deployment selection, validation, and command dispatch planning.
  - `Program.cs` is now a thin bootstrapper; host execution still resolves providers through `HostStartup` + DI/registry rather than `new` statements.
- **Key files:**
  - `src/Meridian.Application/Composition/Startup/SharedStartupBootstrapper.cs`
  - `src/Meridian.Application/Composition/HostStartup.cs`
  - `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`

---

## Phase 2 — Single Composition Root (DI Everywhere) ✅ COMPLETE

### Step 2.1 — Move pipeline construction entirely to DI ✅
- **Status:** Complete.
- **What was done:**
  - `JsonlStoragePolicy`, `JsonlStorageSink`, `ParquetStorageSink`, `CompositeSink`, `WriteAheadLog`, `DroppedEventAuditTrail`, and `EventPipeline` all registered as singletons in `ServiceCompositionRoot.AddPipelineServices()`.
  - `IStorageSink` resolved as `CompositeSink` when Parquet enabled, otherwise `JsonlStorageSink`.
  - `IMarketEventPublisher` wraps `EventPipeline` via `PipelinePublisher`.
- **Key files:**
  - `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`

### Step 2.2 — Single config load path ✅
- **Status:** Complete.
- **What was done:**
  - `ConfigStore` registered as singleton in DI, loads config once.
  - `Program.cs` uses `LoadConfigMinimal()` only for pre-DI logging initialization (justified).
  - All other config access goes through `ConfigStore.Load()` via DI.
- **Key files:**
  - `src/Meridian/Program.cs`
  - `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`

---

## Phase 3 — WebSocket Lifecycle Consolidation 🔄 IN PROGRESS

### Step 3.1 — Define migration contract on `WebSocketProviderBase` ✅
- **Status:** Complete.
- **What was done:**
  - Created `WebSocketProviderBase` abstract class in `Infrastructure/Adapters/Core/`.
  - Delegates connection lifecycle to `WebSocketConnectionManager` (resilience, heartbeat, reconnection gating).
  - Template method hooks: `BuildWebSocketUri()`, `AuthenticateAsync()`, `HandleMessageAsync()`, `ResubscribeAsync()`, `ConfigureWebSocket()`.
  - Automatic reconnection with `MigrationDiagnostics` counter integration.
  - Clean `IAsyncDisposable` implementation.
- **Key files:**
  - `src/Meridian.Infrastructure/Adapters/Core/WebSocketProviderBase.cs` (new)

### Step 3.2 — Migrate Polygon reconnection to shared helper ✅
- **Status:** Complete (partial migration).
- **What was done:**
  - Replaced Polygon's ~60-line manual reconnection logic (`SemaphoreSlim` gating, `CalculateReconnectDelay`, manual attempt tracking) with `WebSocketReconnectionHelper.TryReconnectAsync()`.
  - Polygon still manages its own `ClientWebSocket` directly (required for protocol-specific handshake: sync message exchange for `WaitForConnectionMessage` and `Authenticate` before receive loop).
  - Full migration to `WebSocketProviderBase` deferred due to Polygon's sync handshake pattern (send auth → wait for response → then start receive loop).
- **Key files:**
  - `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`

### Step 3.3 — Migrate NYSE to base class ⏳ DEFERRED
- **Status:** Deferred.
- **Reason:** NYSE implements `DataSourceBase` + `IRealtimeDataSource` + `IHistoricalDataSource` (hybrid pattern), not `IMarketDataClient`. Migrating requires interface refactoring beyond WebSocket consolidation scope.

### Step 3.4 — Migrate StockSharp to base class ⏳ DEFERRED
- **Status:** Deferred.
- **Reason:** StockSharp wraps a third-party `Connector` (not raw WebSocket) behind `#if STOCKSHARP` conditional compilation. `WebSocketProviderBase` doesn't apply to connector-based providers.

### Step 3.5 — Remove redundant reconnect implementations ✅
- **Status:** Complete (for Polygon).
- **What was done:**
  - Removed Polygon's manual `SemaphoreSlim _reconnectGate`, `_reconnectAttempts`, `MaxReconnectAttempts`, `ReconnectBaseDelay`, `ReconnectMaxDelay` fields.
  - Removed `CalculateReconnectDelay()` method.
  - Reconnection now delegated to `WebSocketReconnectionHelper` which provides identical behavior (gated exponential backoff with jitter).
- **Key files:**
  - `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`

---

## Phase 4 — Metrics Abstraction (Decouple from Statics) ✅ COMPLETE

### Step 4.1 — Introduce `IEventMetrics` ✅
- **Status:** Complete.
- **What was done:**
  - `IEventMetrics` interface with properties (`Published`, `Dropped`, `Trades`, etc.) and increment methods (`IncPublished()`, etc.).
  - Platform-owned `DefaultEventMetrics` delegates to the Platform static `Metrics` class with `[MethodImpl(AggressiveInlining)]` for zero-allocation hot path.
  - Platform-owned `TracedEventMetrics` wraps `DefaultEventMetrics` for OpenTelemetry export.
- **Key files:**
  - `src/Meridian.Contracts/Monitoring/IEventMetrics.cs`
  - `src/Meridian.Platform/Tracing/DefaultEventMetrics.cs`
  - `src/Meridian.Platform/Tracing/Metrics.cs`
  - `src/Meridian.Platform/Tracing/TracedEventMetrics.cs`

### Step 4.2 — Inject metrics into hot pipeline paths ✅
- **Status:** Complete.
- **What was done:**
  - `EventPipeline` accepts `IEventMetrics` via constructor.
  - `PipelinePublisher` accepts `IEventMetrics` for integrity tracking.
  - `DataQualityMonitoringService` accepts `IEventMetrics` via constructor.
  - DI registration in `ServiceCompositionRoot.AddPipelineServices()` with optional `TracedEventMetrics` wrapper.
- **Key files:**
  - `src/Meridian.Application/Pipeline/EventPipeline.cs`
  - `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`

---

## Phase 5 — Desktop Service Consolidation (WPF-only) ✅ COMPLETE

> **Note:** The UWP desktop application has been fully removed from the codebase. WPF is the sole desktop client.

### Step 5.1 — Promote shared service interfaces into `Ui.Services` ✅
- **Status:** Complete.
- **What was done:**
  - 16 shared interfaces in `Ui.Services/Contracts/`: `IConfigService`, `IStatusService`, `IThemeService`, `IMessagingService`, `INotificationService`, `ILoggingService`, `ICredentialService`, `IAdminMaintenanceService`, `IArchiveHealthService`, `ISchemaService`, `IBackgroundTaskSchedulerService`, `IOfflineTrackingPersistenceService`, `IPendingOperationsQueueService`, `IWatchlistService`.
  - Shared types: `ConnectionState`, `ConnectionSettings`, `NavigationEntry`, `NavigationEventArgs`.
- **Key files:**
  - `src/Meridian.Ui.Services/Contracts/*`

### Step 5.2 — Move shared implementations where possible ✅
- **Status:** Complete.
- **What was done:**
  - 5 shared base classes: `ThemeServiceBase`, `NavigationServiceBase`, `ConfigServiceBase` (432 LOC), `StatusServiceBase` (350 LOC), `ConnectionServiceBase` (440 LOC).
  - Template method pattern: base classes define algorithms, WPF overrides platform-specific methods.
  - WPF services delegate to base classes for state machines, polling loops, validation logic.
- **Key files:**
  - `src/Meridian.Ui.Services/Services/*Base.cs`
  - `src/Meridian.Wpf/Services/*`

---

## Phase 6 — Validation Pipeline Unification ✅ COMPLETE

### Step 6.1 — Introduce `IConfigValidator` pipeline ✅
- **Status:** Complete.
- **What was done:**
  - `IConfigValidator` interface with `Validate(AppConfig)` returning `IReadOnlyList<ConfigValidationResult>`.
  - `ConfigValidationPipeline` with composable stages: `FieldValidationStage` (FluentValidation rules) + `SemanticValidationStage` (cross-property constraints).
  - `ConfigValidationHelper` deprecated static methods removed (Phase 7.1).
  - FluentValidation validators preserved: `AppConfigValidator`, `AlpacaOptionsValidator`, `StockSharpConfigValidator`, `StorageConfigValidator`, `SymbolConfigValidator`.
- **Key files:**
  - `src/Meridian.Core/Config/IConfigValidator.cs`
  - `src/Meridian.Core/Config/ConfigValidationHelper.cs` (validators only)

---

## Phase 7 — Final Cleanup & Hardening ✅ COMPLETE

### Step 7.1 — Remove deprecated code paths and flags ✅
- **Status:** Complete.
- **What was done:**
  - Removed `ConfigValidationHelper` static class (3 obsolete methods: `ValidateAndLog()` × 2, `ValidateOrThrow()`).
  - Preserved all FluentValidation validator classes (`AppConfigValidator`, `AlpacaOptionsValidator`, etc.) as they're used by `ConfigValidationPipeline`.
  - Polygon reconnection logic consolidated to `WebSocketReconnectionHelper`.
- **Key files:**
  - `src/Meridian.Core/Config/ConfigValidationHelper.cs`

### Step 7.2 — Update architecture docs and ADRs ✅
- **Status:** Complete.
- **What was done:**
  - This file updated with completion status for all phases.
  - Phase completion markers added to each step.
  - Deferred items documented with rationale.

---

## Phase 8 — Shared Infrastructure Consolidation ✅ COMPLETE

One-fix-many leverage pass: each shared component below collapses a family of copy-pasted
implementations and fixes the robustness gaps those copies had diverged on. Behavior is preserved
at every migrated site except where a divergence was itself the defect (noted per step).

### Step 8.1 — Single PostgreSQL migration runner ✅
- **What was done:**
  - `PostgresMigrationRunner` + `PostgresMigrationRunnerOptions` in `src/Meridian.Storage/Migrations/` consolidate the eight per-feature `{Feature}MigrationRunner` classes (AssetOperations, SecurityMaster, Banking, MoneyMarket, Ledger, FundStructure, FundAccounts, DirectLending), which are now thin option-mapping wrappers.
  - Every schema now gets the strongest tier's semantics: one transaction over the whole run, a `pg_advisory_xact_lock` serializing concurrent starters (fixes the Banking/MoneyMarket/SecurityMaster check-then-act race), a durable migration ledger with SHA-256 checksums (fixes the Ledger/FundStructure/FundAccounts/DirectLending no-tracking/no-transaction tier), and strict schema-identifier validation everywhere.
  - Historical differences preserved via options: ledger table name/key column (AssetOperations), ordinal-prefixed filenames (SecurityMaster), quoted vs raw `__SCHEMA__` rendering, missing-directory tolerance (Banking/MoneyMarket), and `MigrationDriftPolicy.Reapply` for the formerly re-run-every-boot schemas so their edit-in-place script workflow keeps working. Legacy ledger rows without checksums are adopted (checksum backfilled) rather than re-run.
- **Key files:**
  - `src/Meridian.Storage/Migrations/PostgresMigrationRunner.cs`
  - `src/Meridian.Storage/Migrations/PostgresMigrationRunnerOptions.cs`
- **Follow-up:** `PostgresScopedAccessAssignmentStore.EnsureMigratedAsync` (Meridian.Identity) still applies inline DDL; fold into the shared runner when its DDL moves to script files. `SchemaUpcasterRegistry` (ADR-007) vs `SchemaVersionManager` remain two JSON-document migration engines to reconcile separately.

### Step 8.2 — Canonical backoff primitive ✅
- **What was done:**
  - `Backoff.ExponentialDelay(attempt, baseDelay, maxDelay, jitterFraction, multiplier, random)` in `src/Meridian.Core/Resilience/Backoff.cs` replaces ~10 hand-rolled `Math.Pow(2, …)` variants: `WebSocketConnectionManager`, `WebSocketReconnectionHelper`, `NYSEDataSource`, `BackfillWorkerService`, `PriorityBackfillQueue`, `PollingProviderBase`, `DataSourceBase`, `BaseBrokerageGateway`, `DailySummaryWebhook`, `ReconciliationOrchestrationResilienceOptions`, and `ExponentialBackoffRetry` (ConnectionWarmUp).
  - Robustness fixes riding along: `BaseBrokerageGateway` reconnect delays are now capped (60 s) and jittered; `DailySummaryWebhook` retries are capped (2 min); `ExponentialBackoffRetry` uses `Random.Shared` instead of a private `Random`; the IB reconnect loop passes its cancellation token into the backoff wait; integer-overflow on high attempt counts is guarded.
- **Key files:**
  - `src/Meridian.Core/Resilience/Backoff.cs`
  - `tests/Meridian.Tests/Core/BackoffTests.cs`
- **Follow-up:** the three hand-rolled circuit breakers (`AutoResubscribePolicy`, `CompositeSink`, Polly pipelines) remain separate implementations; `IngestionJob.NextDelay` lives in Contracts and cannot reference Core.

### Step 8.3 — Shared guarded-endpoint helper ✅
- **What was done:**
  - `EndpointHelpers.GuardAsync` (src/Meridian.Ui.Shared/Endpoints/EndpointHelpers.cs) is the single try/catch contract for minimal-API handlers: cancellations propagate, per-endpoint typed-exception mapping runs first, remaining failures are logged and folded into `Results.Problem`.
  - ~49 copy-pasted handler guards migrated across `StorageEndpoints`, `StorageQualityEndpoints`, `CatalogEndpoints`, `BackfillValidationEndpoints`, `SymbolEndpoints`, `ArchiveMaintenanceEndpoints`, and `PackagingEndpoints`. Success and null-service payloads are unchanged.
  - Bug fixed: 27 handlers previously caught bare `Exception`, so client disconnects were logged as errors and returned HTTP 500; cancellations now propagate (the existing `HandleAsync(ct)` overload got the same fix).
- **Key files:**
  - `src/Meridian.Ui.Shared/Endpoints/EndpointHelpers.cs`
  - `tests/Meridian.Tests/Integration/EndpointTests/EndpointGuardTests.cs`
- **Follow-up:** `FundStructureEndpoints.cs` keeps its narrow typed-catch pattern (safe today); `DataQualityEndpoints`' file-local wrappers return raw `ex.Message` and were left for a deliberate response-shape decision.

### Step 8.4 — Shared JSON file snapshot store ✅
- **What was done:**
  - `JsonFileSnapshotStore<TSnapshot>` in `src/Meridian.Storage/Store/` provides the load → read-modify-write → `AtomicFileWriter` persist cycle (SemaphoreSlim-gated) that ~15 stores had each re-implemented; supports reflection and source-generated serializers, with overridable corrupt-snapshot and post-load (version-check) hooks.
  - Migrated the dominant `_snapshotPath + SemaphoreSlim + AtomicFileWriter` family in `Meridian.Ui.Shared` (accounting configuration/migration-run/tenancy/certification/journal-draft stores, saved-view and workflow-preset stores), `Meridian.FinancialOperations` (statement checkpoint/fetch-schedule/mapping-profile stores), and `Meridian.Strategies` (reconciliation run/break-queue repositories), preserving each store's serializer options and corrupt-file policy.
- **Key files:**
  - `src/Meridian.Storage/Store/JsonFileSnapshotStore.cs`
  - `tests/Meridian.Tests/Storage/JsonFileSnapshotStoreTests.cs`
- **Follow-up:** non-atomic writers (`WindowStateStore`, `JsonRunbookStore`, `JsonCanonicalStatementStore`/`JsonReconciliationBreakStore`) and the ctor-loading registry family (`SourceRegistry`, `SymbolRegistryService`, `QualityArchiveStore`) are candidates for a later pass.

### Step 8.5 — Shared lot-consumption core ✅
- **What was done:**
  - `LotConsumption.Consume` in `src/Meridian.Ledger/LotConsumption.cs` implements the walk-ordered-lots / split-partial-lot algorithm once; callers keep their own relief ordering, rounding, fee treatment, and side effects, so engine-specific accounting semantics stay at their owners.
  - Migrated all seven duplicated consume loops: `LedgerTaxLotReliefProjector.SelectLots`, `TaxLotSelectors` (ordered + specific-id), `BacktestMetricsEngine` (realized-P&L and win-rate FIFO matchers), `SimulatedPortfolio.RealiseLots`/`RealiseShortLots`, and `PaperPosition.ConsumeLots`.
- **Key files:**
  - `src/Meridian.Ledger/LotConsumption.cs`
  - `tests/Meridian.Tests/Ledger/LotConsumptionTests.cs`
- **Follow-up (known divergences deliberately NOT changed in this pass):** oversell behavior still differs by engine (ledger throws, backtest flips, paper clamps); only the ledger projector rounds realized amounts to 2 dp; `PaperPosition.UnrealisedPnl` returns 0 for shorts; `BacktestMetricsEngine` attribution remains FIFO-only for non-FIFO accounts. Each is a product decision to reconcile explicitly, not silently.

---

## Suggested Execution Order (Strict)

1. ~~Phase 0 (tests + telemetry)~~ ✅
2. ~~Phase 1 (provider registry)~~ ✅
3. ~~Phase 2 (DI composition root)~~ ✅
4. Phase 3 (WebSocket consolidation) — 🔄 Partially complete (Polygon migrated, NYSE/StockSharp deferred)
5. ~~Phase 4 (metrics injection)~~ ✅
6. ~~Phase 6 (validation pipeline)~~ ✅
7. ~~Phase 5 (desktop deduplication)~~ ✅
8. ~~Phase 7 (cleanup)~~ ✅

> Why this order: it minimizes blast radius by first creating verification rails, then consolidating backend composition and provider internals, and only then moving UI-heavy duplication work.

## Rollback Strategy

- Keep feature flags around discovery/registration until at least one release cycle proves parity.
- Migrate one provider at a time with fixture parity tests.
- Preserve old implementations behind adapters during UI service extraction.
- Do not delete legacy path until integration, replay, and smoke tests pass in CI for two consecutive runs.

## Remaining Work

### Phase 3 — WebSocket Lifecycle (Deferred Items)
- **NYSE migration:** Requires interface refactoring (`DataSourceBase` → `IMarketDataClient`) before `WebSocketProviderBase` can be applied. Track as separate work item.
- **StockSharp migration:** Connector-based architecture (wraps third-party `Connector` class) is fundamentally different from raw WebSocket providers. `WebSocketProviderBase` doesn't apply. Consider a separate `ConnectorProviderBase` if patterns emerge.

---

## Test Topology Guardrails

Use these placement rules when adding or moving tests so cross-platform coverage does not drift into Windows-only assemblies:

| Test layer / concern | Test project | Rationale |
| --- | --- | --- |
| Startup, DI composition, host wiring that is not WPF-specific, provider contracts, endpoint-shape/schema snapshots | `tests/Meridian.Tests` | Must stay runnable without Windows desktop support. |
| Shared desktop services, base classes, collection helpers, mapping/filtering logic, refresh orchestration with an injected scheduler abstraction | `tests/Meridian.Ui.Tests` | Shared desktop logic should stay out of page code-behind and out of WPF-only tests. |
| WPF-only binding behavior, navigation/page registration, resource usage, and desktop host wiring | `tests/Meridian.Wpf.Tests` | These tests genuinely depend on WPF types and Windows targeting. |

### Data quality refresh rule

- Keep mapping, filtering, and refresh behavior in platform-neutral services or plain viewmodel logic.
- Isolate recurring scheduling behind an interface so the core logic can be tested without `DispatcherTimer` or page lifecycle hooks.
- Add WPF-specific tests only for the binding/navigation/host-wiring seam that connects the shared logic to the desktop shell.

---

## Related Documentation

- **Architecture and Planning:**
  - [Repository Cleanup Action Plan](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/plans/repository-cleanup-action-plan.md) - Prioritized technical debt reduction (completed)
  - [Repository Organization Guide](./repository-organization-guide.md) - Code structure conventions
  - [ADR Index](../adr/README.md) - Architectural decision records

- **Implementation Guides:**
  - [Provider Implementation Guide](./provider-implementation.md) - Adding new data providers
  - [Desktop Platform Improvements archive](../../archive/docs/assessments/desktop-platform-improvements-implementation-guide.md) - Historical desktop development assessment; use current engineering/operator docs for active work
  - [WPF Implementation Notes](./wpf-implementation-notes.md) - WPF architecture

- **Status and Tracking:**
  - [Project Roadmap](../roadmap/README.md) - Overall project timeline
  - [CHANGELOG](../status/CHANGELOG.md) - Version history

