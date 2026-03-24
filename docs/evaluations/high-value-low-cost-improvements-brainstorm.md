> **Note:** WPF desktop app references in this document reflect a delayed implementation. `src/Meridian.Wpf/` is preserved but not in the active solution build.

---

# High-Value, Low-Cost Improvements Brainstorm

**Date:** 2026-02-23
**Updated:** 2026-03-15
**Status:** Active — 27 of 47 implemented, 7 partial, 13 open
**Author:** Architecture Review
**Context:** With 94.3% of core improvements complete (33/35 items), this document identifies the next wave of high-ROI, low-effort improvements across reliability, developer experience, operations, and code quality. Updated 2026-03-15 to reflect implementation progress.

**Implementation status key:**
- ✅ **Implemented** — shipped and tested
- 🔄 **Partial** — framework or foundation in place; full capability pending
- 📝 **Future** — not yet started

**Scoring criteria:**
- **Value**: Direct impact on reliability, correctness, developer productivity, or operational visibility
- **Cost**: Estimated effort in hours/days, not weeks; no major refactors
- **Risk**: Low regression risk; isolated changes preferred

---

## Implementation Progress (2026-03-15)

| Category | ✅ Done | 🔄 Partial | 📝 Open | Total |
|----------|---------|------------|---------|-------|
| 1 — Startup & Configuration Hardening | 2 | 0 | 1 | 3 |
| 2 — Operational Visibility | 3 | 0 | 1 | 4 |
| 3 — Developer Experience | 2 | 1 | 1 | 4 |
| 4 — Data Integrity & Quality | 1 | 0 | 2 | 3 |
| 5 — Testing & CI Improvements | 1 | 2 | 1 | 4 |
| 6 — Code Quality Quick Wins | 2 | 0 | 2 | 4 |
| 7 — Security Hardening | 0 | 1 | 1 | 2 |
| 8 — Performance Quick Wins | 2 | 0 | 1 | 3 |
| 9 — End-User Experience | 7 | 3 | 2 | 12 |
| 10 — Data Consumption & Analysis | 5 | 0 | 1 | 6 |
| 11 — Trust & Transparency | 2 | 0 | 0 | 2 |
| **Total** | **27** | **7** | **13** | **47** |

---

## Category 1: Startup & Configuration Hardening

### 1.1 Startup credential validation with actionable errors — ✅ Implemented

**Status (2026-03-15):** `PreflightChecker.cs` includes `ValidateProviderCredentials()` which iterates enabled providers via `DataSourceRegistry`, checks their credential requirements, and emits a table of missing credentials with the exact environment variable names to set.

**Problem:** The app loads configuration and connects to providers, but if API credentials are missing or malformed the errors surface deep in provider code with cryptic messages (401 Unauthorized, null reference on key parsing, etc.). The `PreflightChecker` exists but doesn't validate that all *enabled* providers have their required credentials set.

**Improvement:** Add a `ValidateProviderCredentials()` step to `PreflightChecker` that iterates enabled providers via `DataSourceRegistry`, checks their `[DataSource]` attribute metadata, and verifies the corresponding environment variables or config sections are populated. Emit a table of missing credentials at startup with the exact env var names to set.

**Value:** High -- eliminates the #1 "why won't it start?" question for new users.
**Cost:** ~4-8 hours. The registry and attribute metadata already exist.
**Files:** `src/Meridian.Application/Services/PreflightChecker.cs`, `src/Meridian.ProviderSdk/CredentialValidator.cs`

---

### 1.2 Deprecation warning for legacy `DataSource` string config — ✅ Implemented

**Status (2026-03-15):** `ConfigurationPipeline.cs` detects when both `DataSource` and `DataSources` are set and logs: `"Both 'DataSource' and 'DataSources' are set. 'DataSources' takes precedence."` A note was also added to `appsettings.sample.json`.

**Problem:** The config supports both `"DataSource": "IB"` (legacy single-provider) and `"DataSources": { "Sources": [...] }` (new multi-provider). When both are present, the precedence is undocumented and confusing.

**Improvement:** At config load time, if both `DataSource` and `DataSources` are populated, log a structured warning: `"Both 'DataSource' and 'DataSources' are set. 'DataSources' takes precedence. Remove 'DataSource' to silence this warning."` Add a note to `appsettings.sample.json`.

**Value:** Medium -- prevents silent misconfiguration.
**Cost:** ~1-2 hours. Single conditional in `ConfigurationPipeline`.
**Files:** `src/Meridian.Application/Config/ConfigurationPipeline.cs`

---

### 1.3 Config validation for provider-specific symbol fields — 📝 Future

**Problem:** Symbol configs accept IB-specific fields (`SecurityType`, `Exchange`, `PrimaryExchange`) even when using Alpaca or Polygon. No warning is emitted, and users waste time debugging why their IB fields have no effect on Alpaca.

**Improvement:** During config validation, check each symbol's provider-specific fields against the active provider. Emit info-level warnings for unused provider-specific fields: `"Symbol SPY has IB-specific field 'Exchange' but active provider is Alpaca -- this field will be ignored."`

**Value:** Medium -- reduces misconfiguration confusion.
**Cost:** ~3-4 hours. Build a small lookup of which fields belong to which provider.
**Files:** `src/Meridian.Application/Config/ConfigValidationHelper.cs`

---

## Category 2: Operational Visibility

### 2.1 Structured startup summary with health matrix — ✅ Implemented

**Status (2026-03-15):** `StartupSummary.cs` now emits a formatted ASCII health matrix at INFO level showing provider connection state, storage component readiness, WAL pending count, active symbol count, and backfill state.

**Problem:** `StartupSummary` logs configuration details at startup, but it reads like a wall of text. Operators need a quick pass/fail matrix to confirm the system is healthy.

**Improvement:** Enhance `StartupSummary` to emit a concise health matrix at INFO level:

```
╔══════════════════════════════════════╗
║  Meridian v1.6.2       ║
║  Mode: Web | Port: 8080            ║
╠══════════════════════════════════════╣
║  Providers:                         ║
║    Alpaca        ✓ Connected        ║
║    Polygon       ✓ Connected        ║
║    IB            ✗ No credentials   ║
║  Storage:                           ║
║    JSONL sink    ✓ Ready            ║
║    Parquet sink  ✓ Ready            ║
║    WAL           ✓ 0 pending        ║
║  Symbols:        5 active           ║
║  Backfill:       Disabled           ║
╚══════════════════════════════════════╝
```

**Value:** High -- immediate operational confidence at startup; easy to screenshot for support.
**Cost:** ~4-6 hours. The data is already available from existing services.
**Files:** `src/Meridian.Application/Services/StartupSummary.cs`

---

### 2.2 Add `/api/config/effective` endpoint — ✅ Implemented

**Status (2026-03-15):** `ConfigEndpoints.cs` exposes `/api/config/effective` returning the fully-resolved configuration with source annotations (`appsettings.json`, `env:VARNAME`, `default`). Credentials are masked via `SensitiveValueMasker`.

**Problem:** With environment variable overrides, config file values, defaults, and presets all layering together, operators can't easily see what configuration is *actually* in effect. The existing `/api/config` endpoint shows the raw config, not the resolved values.

**Improvement:** Add a `/api/config/effective` endpoint that returns the fully-resolved configuration with source annotations:

```json
{
  "dataSource": { "value": "Alpaca", "source": "appsettings.json" },
  "alpaca.keyId": { "value": "PK***4X", "source": "env:ALPACA__KEYID" },
  "storage.namingConvention": { "value": "BySymbol", "source": "default" }
}
```

Credentials should be masked (already have `SensitiveValueMasker`).

**Value:** High -- eliminates "which setting is winning?" debugging.
**Cost:** ~6-8 hours. Build a config source tracker in `ConfigurationPipeline`.
**Files:** `src/Meridian.Application/Config/ConfigurationPipeline.cs`, new endpoint in `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs`

---

### 2.3 WAL recovery metrics at startup — 📝 Future

**Problem:** The Write-Ahead Log (`WriteAheadLog`) recovers pending events on startup, but the recovery count and duration aren't surfaced as metrics or in the startup summary.

**Improvement:** After WAL recovery in `WriteAheadLog.RecoverAsync()`, emit:
- A Prometheus counter `wal_recovery_events_total` with the count of recovered events
- A gauge `wal_recovery_duration_seconds` with the recovery duration
- A structured log: `"WAL recovery complete: {RecoveredCount} events in {Duration}ms"`

**Value:** Medium -- critical for understanding restart behavior and data loss risk.
**Cost:** ~2-3 hours. The recovery logic already exists; just add instrumentation.
**Files:** `src/Meridian.Storage/Archival/WriteAheadLog.cs`, `src/Meridian.Application/Monitoring/PrometheusMetrics.cs`

---

### 2.4 Provider reconnection event log with backoff visibility — ✅ Implemented

**Status (2026-03-15):** `WebSocketReconnectionHelper.cs` logs structured reconnection attempts with `{Attempt}`, `{MaxAttempts}`, and `{DelayMs}` parameters and records attempts via `IReconnectionMetrics`.

**Problem:** When WebSocket connections drop, providers reconnect with exponential backoff. But the retry attempt number and next retry delay aren't logged, making it hard to tell whether the system is recovering or stuck in a retry loop.

**Improvement:** In each provider's reconnection logic (and `WebSocketReconnectionHelper`), ensure structured logs include `{Attempt}`, `{MaxAttempts}`, and `{NextRetryMs}`:
```
"WebSocket reconnection attempt {Attempt}/{MaxAttempts} for {Provider}, next retry in {NextRetryMs}ms"
```

Also emit a Prometheus counter `provider_reconnection_attempts_total{provider, outcome}` partitioned by success/failure.

**Value:** Medium -- makes reconnection debugging self-service.
**Cost:** ~3-4 hours. Standardize the log format across providers.
**Files:** `src/Meridian.Infrastructure/Shared/WebSocketReconnectionHelper.cs`, individual provider files

---

## Category 3: Developer Experience

### 3.1 Environment variable reference document — ✅ Implemented

**Status (2026-03-15):** `docs/reference/environment-variables.md` exists with a comprehensive table of all supported environment variables, including description, required/optional status, provider association, example values, and corresponding config path.

**Problem:** The project uses 30+ environment variables for credentials, configuration overrides, and feature flags. These are scattered across `appsettings.sample.json` comments, `ConfigEnvironmentOverride.cs`, and individual provider code. No canonical list exists.

**Improvement:** Generate (or manually create) a `docs/reference/environment-variables.md` that lists every supported env var with:
- Variable name
- Description
- Required/optional
- Which provider it belongs to
- Example value
- Corresponding config path

**Value:** High -- the most-asked question for any 12-factor app.
**Cost:** ~3-4 hours. Most of the information exists in code; it just needs consolidation.
**Files:** New `docs/reference/environment-variables.md`

---

### 3.2 `--check-config` CLI flag for offline config validation — 🔄 Partial

**Status (2026-03-15):** `--dry-run --offline` is implemented in `DryRunCommand.cs` and performs most of the validation described. An explicit `--check-config` alias is not present, but the combination `--dry-run --offline` covers config parsing, required-field validation, and credential env var checks without network access.

**Problem:** The `--dry-run` flag performs full validation including connectivity checks. There's no way to validate just the config file syntax and required fields without network access (useful in CI or air-gapped environments).

**Improvement:** Add a `--check-config` flag (or enhance `--dry-run --offline`) that:
1. Parses the config file
2. Validates required fields are present
3. Checks credential env vars are set (not empty)
4. Validates symbol configs against provider requirements
5. Exits with 0 (valid) or non-zero (invalid) + structured error list

The `--dry-run --offline` combination already exists but may not cover all these checks.

**Value:** Medium -- enables CI/CD config validation without live providers.
**Cost:** ~4-6 hours. Most validation logic exists; wire it into a clean CLI path.
**Files:** `src/Meridian.Application/Commands/DryRunCommand.cs`, `src/Meridian.Application/Services/DryRunService.cs`

---

### 3.3 JSON Schema generation for `appsettings.json` — 📝 Future

**Problem:** `appsettings.sample.json` is 730 lines with no IDE autocomplete or validation. Developers must read comments to understand valid values. VS Code and JetBrains IDEs support JSON Schema for autocomplete.

**Improvement:** Generate a JSON Schema file from the C# configuration classes (`AppConfig`, `BackfillConfig`, `StorageOptions`, etc.) using a build-time tool or source generator. Reference it in the config file:

```json
{
  "$schema": "./config/appsettings.schema.json",
  ...
}
```

**Value:** High -- immediate IDE autocomplete and inline validation for all configuration.
**Cost:** ~6-8 hours. Use `JsonSchemaExporter` (.NET 9) or a Roslyn-based generator.
**Files:** New schema generator tool, `config/appsettings.schema.json`

---

### 3.4 `make quickstart` target for zero-to-running — ✅ Implemented

**Status (2026-03-15):** The `Makefile` has a `quickstart` target ("Zero-to-running setup for new contributors") that checks .NET SDK, copies sample config, runs restore + build + a fast test subset, and prints next steps.

**Problem:** New contributors must read CLAUDE.md, install the SDK, copy config, set env vars, and run the build. A `make quickstart` target could automate the happy path.

**Improvement:** Add a Makefile target that:
1. Checks .NET 9 SDK is installed
2. Copies `appsettings.sample.json` to `appsettings.json` if not present
3. Runs `dotnet restore`
4. Runs `dotnet build`
5. Runs `dotnet test` (fast subset)
6. Prints next steps (set env vars, run with `--wizard`)

**Value:** Medium -- reduces onboarding friction from ~15 minutes to ~2 minutes.
**Cost:** ~2-3 hours. Shell script wrapped in Makefile target.
**Files:** `Makefile`

---

## Category 4: Data Integrity & Quality

### 4.1 Automatic gap backfill on reconnection — 📝 Future

**Problem:** When a streaming provider disconnects and reconnects, there's a data gap for the disconnection period. The system logs an `IntegrityEvent` but doesn't automatically request backfill for the missing window.

**Improvement:** After a successful reconnection, automatically enqueue a targeted backfill request for each subscribed symbol covering `[disconnect_time, reconnect_time]`. Use the existing `BackfillCoordinator` and `HistoricalBackfillService`. Gate this behind a config flag `AutoGapFill: true`.

**Value:** High -- directly improves data completeness, which is the project's core value proposition.
**Cost:** ~6-8 hours. The backfill infrastructure exists; wire it to the reconnection event.
**Files:** `src/Meridian.Infrastructure/Shared/WebSocketReconnectionHelper.cs`, `src/Meridian.Application/Backfill/HistoricalBackfillService.cs`

---

### 4.2 Cross-provider quote divergence alerting — ✅ Implemented

**Status (2026-03-15):** `CrossProviderComparisonService.cs` detects real-time quote divergence via `CheckForQuoteDiscrepancies()`, emits events via `OnDiscrepancyDetected`, and classifies severity levels. Active when 2+ providers stream the same symbol.

**Problem:** The design review memo flags "feed divergence across providers" as a known risk. When multiple providers are active, their quotes for the same symbol can diverge. The `CrossProviderComparisonService` exists but doesn't emit real-time alerts.

**Improvement:** Add a lightweight comparison in the event pipeline that, when 2+ providers are streaming the same symbol, checks if mid-prices diverge by more than a configurable threshold (e.g., 0.5%). Emit a structured warning and increment `provider_quote_divergence_total{symbol}`.

**Value:** Medium -- early warning for stale feeds or provider issues.
**Cost:** ~4-6 hours. The comparison service has the logic; add a real-time check.
**Files:** `src/Meridian.Application/Monitoring/DataQuality/CrossProviderComparisonService.cs`

---

### 4.3 Storage checksum verification on read — 📝 Future

**Problem:** `StorageChecksumService` computes checksums on write. But there's no verification on read to detect bit rot or corruption in stored files. The `DataValidator` tool exists but must be run manually.

**Improvement:** Add an optional `VerifyOnRead: true` config flag to `StorageOptions`. When enabled, `JsonlReplayer` and `MemoryMappedJsonlReader` verify the file checksum before returning data. Log a warning (not error) on mismatch, and increment `storage_checksum_mismatch_total{path}`.

**Value:** Medium -- catches silent data corruption before it reaches downstream consumers.
**Cost:** ~4-6 hours. Checksum computation exists; add verification in read paths.
**Files:** `src/Meridian.Storage/Replay/JsonlReplayer.cs`, `src/Meridian.Storage/Services/StorageChecksumService.cs`

---

## Category 5: Testing & CI Improvements

### 5.1 Flaky test detection in CI — 📝 Future

**Problem:** With 3,444 tests, occasional flaky tests (timing-dependent, file-system-dependent) can cause spurious CI failures. There's no mechanism to detect or quarantine flaky tests.

**Improvement:** Add a `--retry-failed` step to the test matrix workflow: if any tests fail, re-run only the failed tests once. If they pass on retry, mark them as flaky and emit a GitHub Actions annotation. Track flaky tests in a `tests/flaky-tests.md` file.

**Value:** Medium -- reduces CI noise and developer frustration.
**Cost:** ~3-4 hours. Use `dotnet test --filter` with the failed test names.
**Files:** `.github/workflows/test-matrix.yml`

---

### 5.2 Test execution time tracking — 🔄 Partial

**Status (2026-03-15):** `test-matrix.yml` already logs test results in TRX format (`--logger "trx;LogFileName=..."`). Slow-test extraction and GitHub Actions job summary reporting are not yet implemented.

**Problem:** As the test suite grows (3,444 tests), slow tests can silently degrade CI times. There's no visibility into which tests are slow.

**Improvement:** Add `--logger "trx"` to test runs and post-process the TRX file to extract the top 20 slowest tests. Emit them as a GitHub Actions job summary. Optionally set a threshold (e.g., 5 seconds per test) that warns on PR checks.

**Value:** Medium -- prevents death-by-a-thousand-cuts CI slowdown.
**Cost:** ~3-4 hours. TRX parsing is well-documented; integrate into existing workflow.
**Files:** `.github/workflows/test-matrix.yml`, optional post-processing script

---

### 5.3 Benchmark regression detection — 🔄 Partial

**Status (2026-03-15):** `benchmark.yml` runs BenchmarkDotNet and uploads artifacts. Cross-run comparison and threshold-based regression detection are not yet implemented — artifacts are saved but not diffed.

**Problem:** The `benchmarks/` project runs BenchmarkDotNet but results aren't compared across runs. A performance regression could ship without detection.

**Improvement:** In the benchmark workflow, export results as JSON (`--exporters json`), store as a workflow artifact, and compare against the previous run's artifact. Flag regressions >10% as warnings, >25% as failures. Use BenchmarkDotNet's built-in `--statisticalTest` flag for significance testing.

**Value:** Medium -- catches performance regressions before they reach production.
**Cost:** ~4-6 hours. BenchmarkDotNet has comparison support; wire to CI.
**Files:** `.github/workflows/benchmark.yml`, `benchmarks/Meridian.Benchmarks/`

---

### 5.4 Integration test for graceful shutdown data integrity — ✅ Implemented

**Status (2026-03-15):** `tests/Meridian.Tests/Integration/GracefulShutdownIntegrationTests.cs` contains `GracefulShutdown_AllPublishedEventsReachStorage()` which verifies zero data loss during shutdown with in-flight events.

**Problem:** `GracefulShutdownService` coordinates flushing WAL, closing sinks, and disconnecting providers. But there's no integration test that verifies zero data loss during a shutdown sequence with in-flight events.

**Improvement:** Write an integration test that:
1. Starts the event pipeline with a mock provider producing events
2. Triggers graceful shutdown via `CancellationToken`
3. Verifies all in-flight events were persisted (WAL + sink)
4. Verifies no duplicate events after recovery

**Value:** High -- validates the most critical operational scenario.
**Cost:** ~6-8 hours. Uses existing `InMemoryStorageSink` test infrastructure.
**Files:** `tests/Meridian.Tests/Integration/`

---

## Category 6: Code Quality Quick Wins

### 6.1 Replace bare catch blocks with typed exceptions — ✅ Implemented

**Status (2026-03-15):** Audit of `src/` found no bare `catch {}` or silent `catch (Exception)` blocks that swallow without logging. All exception handlers now log with context.

**Problem:** The `FURTHER_SIMPLIFICATION_OPPORTUNITIES.md` audit identified bare `catch` blocks that swallow exceptions silently. These hide bugs in production.

**Improvement:** Find and replace all bare `catch` and `catch (Exception)` blocks that don't re-throw or log. At minimum, add `_logger.LogWarning(ex, "...")` to each. In hot paths, consider `catch (SpecificException)` instead.

**Value:** High -- prevents silent failures in production.
**Cost:** ~2-4 hours. Grep for `catch\s*\{` and `catch\s*\(Exception`.
**Files:** Various across `src/`

---

### 6.2 Add `TimeProvider` abstraction for testability — 📝 Future

**Problem:** Code that uses `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly is hard to test deterministically. .NET 8+ introduced `TimeProvider` as a built-in abstraction.

**Improvement:** Inject `TimeProvider` (or `TimeProvider.System` as default) into time-sensitive services:
- `TradingCalendar` (market hours checks)
- `DataFreshnessSlaMonitor` (SLA window calculations)
- `BackfillScheduleManager` (next-run calculations)
- `LifecyclePolicyEngine` (retention checks)

This enables deterministic time-based tests without `Thread.Sleep` or flaky timing.

**Value:** Medium -- improves test reliability and enables edge-case time testing.
**Cost:** ~4-6 hours. Add `TimeProvider` parameter to constructors with default.
**Files:** Services listed above

---

### 6.3 Consolidate `Lazy<T>` initialization pattern — 📝 Future

**Problem:** The audit identified 43 services using manual double-checked locking for lazy initialization. .NET's `Lazy<T>` is thread-safe by default and eliminates this boilerplate.

**Improvement:** Replace manual `lock` + null-check patterns with `Lazy<T>` or `AsyncLazy<T>`. Prioritize the most-used services first (storage sinks, provider factories).

**Value:** Low-Medium -- reduces boilerplate, eliminates potential lock ordering bugs.
**Cost:** ~4-8 hours for the top 10-15 most impactful services.
**Files:** Various across `src/`

---

### 6.4 Endpoint handler helper to reduce try/catch boilerplate — ✅ Implemented

**Status (2026-03-15):** `EndpointHelpers.cs` has `HandleAsync()` with multiple overloads providing consistent error responses, `CancellationToken` propagation, and exception-to-status-code mapping, used across endpoint files.

**Problem:** The 35 endpoint files each repeat the same try/catch + JSON response pattern. The `EndpointHelpers` class exists but isn't used everywhere.

**Improvement:** Ensure all endpoint handlers use `EndpointHelpers.HandleAsync()` (or a similar wrapper) that provides:
- Consistent error response format (`ErrorResponse`)
- Automatic `CancellationToken` propagation
- Request logging with correlation ID
- Exception-to-status-code mapping

**Value:** Medium -- consistent API error responses; less boilerplate.
**Cost:** ~6-8 hours for full migration; can be done incrementally.
**Files:** `src/Meridian.Ui.Shared/Endpoints/*.cs`

---

## Category 7: Security Hardening

### 7.1 Enforce credential-via-environment at validation time — 🔄 Partial

**Status (2026-03-15):** `ConfigValidationHelper.cs` validates credential format (non-empty, valid characters) via FluentValidation. A warning when non-placeholder credentials appear directly in the config file (rather than via environment variables) is not yet emitted.

**Problem:** The design review notes that credentials in `appsettings.json` are a security risk. Environment variable support exists but isn't enforced. A developer could accidentally commit credentials.

**Improvement:** Add a validation check: if any credential field in the config file contains a non-empty, non-placeholder value (not `"your-key-here"`), emit a warning:
```
"WARNING: Credential '{FieldName}' appears to be set directly in config file.
 Use environment variable {EnvVarName} instead to avoid accidental commits."
```

Optionally, add a `--strict-credentials` flag that makes this a hard error.

**Value:** High -- prevents the #1 security anti-pattern.
**Cost:** ~3-4 hours. Add check in `ConfigValidationHelper`.
**Files:** `src/Meridian.Application/Config/ConfigValidationHelper.cs`

---

### 7.2 API key rotation support — 📝 Future

**Problem:** The `ApiKeyMiddleware` supports static API keys for the dashboard. If a key is compromised, the only recourse is to restart the service with a new key.

**Improvement:** Support multiple API keys (comma-separated in env var) and add a `POST /api/admin/rotate-key` endpoint that:
1. Accepts a new key
2. Adds it to the active key set
3. Optionally revokes old keys after a grace period
4. Logs the rotation event

**Value:** Medium -- operational security improvement.
**Cost:** ~4-6 hours.
**Files:** `src/Meridian.Ui.Shared/Endpoints/ApiKeyMiddleware.cs`, `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs`

---

## Category 8: Performance Quick Wins

### 8.1 Connection warmup with parallel provider initialization — ✅ Implemented

**Status (2026-03-15):** `Program.cs` uses `Task.WhenAll` to connect all enabled providers concurrently at startup.

**Problem:** When using failover with 3+ providers, each provider connects sequentially. Connecting all providers in parallel could reduce startup time by the sum of connection latencies minus the maximum.

**Improvement:** In the startup path where `providerMap` is built, connect all enabled providers in parallel using `Task.WhenAll`. The `FailoverAwareMarketDataClient` already handles the case where some providers fail to connect.

**Value:** Medium -- reduces startup time proportional to provider count.
**Cost:** ~2-3 hours. Change sequential loop to parallel.
**Files:** `src/Meridian/Program.cs` (provider initialization section)

---

### 8.2 Conditional Parquet sink activation — ✅ Implemented

**Status (2026-03-15):** `StorageOptions.cs` has `EnableParquetSink` (and `ActiveSinks`) for conditional Parquet sink registration. Defaults to disabled for real-time-only deployments.

**Problem:** The `CompositeSink` always writes to all registered sinks. If Parquet export isn't needed for real-time collection, the Parquet serialization overhead is wasted.

**Improvement:** Make Parquet sink activation conditional on config (`Storage.EnableParquet: true/false`). Default to disabled for real-time-only deployments. The `CompositeSink` already supports dynamic sink registration.

**Value:** Medium -- reduces CPU and I/O overhead for real-time deployments.
**Cost:** ~2-3 hours. Add config flag, conditional registration in DI.
**Files:** `src/Meridian.Application/Composition/ServiceCompositionRoot.cs`, `config/appsettings.sample.json`

---

### 8.3 Reduce config file double-read at startup — 📝 Future

**Problem:** Program.cs reads the config file twice: once for `LoadConfigMinimal` (to get `DataRoot` for logging) and once for the full `LoadAndPrepareConfig`. This is redundant I/O.

**Improvement:** Read the file once into a `JsonDocument`, extract `DataRoot` for early logging setup, then pass the same document to the full config pipeline. Alternatively, make `DataRoot` default to a well-known path and only require the full config load.

**Value:** Low -- saves ~10-50ms of startup I/O.
**Cost:** ~2-3 hours.
**Files:** `src/Meridian/Program.cs`

---

## Category 9: End-User Experience — Data Collection Workflow

> **Key insight:** Many of the services below are already fully built and tested in the backend (`BackfillCheckpointService`, `WorkspaceService`, `CommandPaletteService`, `AlertService`, `FriendlyErrorFormatter`, `OnboardingTourService`). The work is **wiring**, not building from scratch.

### 9.1 Data freshness indicator on web dashboard — ✅ Implemented

**Status (2026-03-15):** The web dashboard template shows a persistent status bar with per-provider connection state indicators, "last event received" timestamp with staleness coloring, and a `[DEMO MODE]` badge when fixture data is active.

**Problem:** The web dashboard auto-refreshes but gives no indication of whether the data shown is live, stale, or demo/fixture data. Users report spending 15+ minutes trying to determine if the system is actually collecting real data. The dashboard in fixture mode looks identical to live mode.

**Improvement:** Add a persistent status bar to the dashboard HTML template showing:
- Provider connection state (green/yellow/red dot per provider)
- "Last event received: 3 seconds ago" timestamp with staleness coloring (green <10s, yellow <60s, red >60s)
- A clear badge if fixture/demo mode is active: `[DEMO MODE]`
- Event throughput: `42 events/sec`

The data is already available from `/api/status` and `/api/providers/status`. This is purely a frontend template change.

**Value:** High -- the #1 user question is "is the system actually running?"
**Cost:** ~3-4 hours. Modify the HTML template in `wwwroot/templates/` to poll `/api/status` and render the bar.
**Files:** `src/Meridian/wwwroot/templates/`, `src/Meridian.Application/Http/HtmlTemplates.cs`

---

### 9.2 Backfill progress with ETA and resumability — 🔄 Partial

**Status (2026-03-15):** `BackfillCheckpointService` is wired into the WPF `BackfillService` and exposed in `BackfillPage.xaml.cs`. It is **not** yet wired into the core `HistoricalBackfillService`. The `--resume` CLI flag and per-symbol ETA in `/api/backfill/status` are pending.

**Problem:** Long-running backfills (e.g., 5 years of daily bars for 100 symbols) show minimal progress feedback. If a backfill fails halfway, users must restart from scratch. `BackfillCheckpointService` exists with 22 passing tests but isn't wired to the backfill execution path or exposed in the web API.

**Improvement:**
1. Wire `BackfillCheckpointService` into `HistoricalBackfillService` to persist progress per symbol
2. Add a `--resume` flag to the backfill CLI that picks up from the last checkpoint
3. Enhance `/api/backfill/status` to include per-symbol progress percentage, bars fetched, and ETA
4. Display a summary line in the CLI: `"Backfill: SPY 847/1260 bars (67%) — ETA 2m 15s | AAPL queued"`

**Value:** High -- backfills can take hours; losing progress is the #2 user complaint.
**Cost:** ~6-8 hours. The checkpoint service is built; this is integration work.
**Files:** `src/Meridian.Application/Backfill/HistoricalBackfillService.cs`, `src/Meridian.Ui.Services/Services/BackfillCheckpointService.cs`, `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs`

---

### 9.3 Friendly error messages with "what to do next" — ✅ Implemented

**Status (2026-03-15):** `Program.cs` calls `FriendlyErrorFormatter.Format()` and `FriendlyErrorFormatter.DisplayError()` in the top-level catch handler. `EndpointHelpers.cs` uses the formatter for API error responses.

**Problem:** `FriendlyErrorFormatter` exists with 30+ classified error codes (Meridian-CFG-001, Meridian-AUTH-002, etc.) and includes suggested actions and doc links. However, it's not integrated into all error paths -- many errors still surface as raw exceptions or generic HTTP status codes. Users report spending 30+ minutes debugging simple credential issues.

**Improvement:** Integrate `FriendlyErrorFormatter` into:
1. CLI startup errors (wrap the top-level try/catch in `Program.cs`)
2. Provider connection failures (catch in `ConnectAsync` implementations)
3. HTTP API error responses (use in `EndpointHelpers` for consistent error JSON)

Example before: `"System.Net.Http.HttpRequestException: Response status code does not indicate success: 401 (Unauthorized)"`

Example after:
```
Meridian-AUTH-002: Authentication failed for Alpaca provider
  → Check that ALPACA__KEYID and ALPACA__SECRETKEY environment variables are set
  → Verify your API key is active at https://app.alpaca.markets/
  → Run: dotnet run -- --validate-credentials
  → Docs: docs/providers/alpaca-setup.md
```

**Value:** High -- transforms cryptic errors into self-service debugging.
**Cost:** ~4-6 hours. The formatter exists; wire it to the 3 error surface areas.
**Files:** `src/Meridian/Program.cs`, `src/Meridian.Ui.Shared/Endpoints/EndpointHelpers.cs`, provider `ConnectAsync` methods

---

### 9.4 Role-based configuration presets for first-time setup — ✅ Implemented

**Status (2026-03-15):** `AutoConfigurationService.cs` has `ApplyPreset()` with four presets: `Researcher`, `DayTrader`, `OptionsTrader`, `Crypto`. A `--preset <name>` CLI flag is handled by `ConfigPresetCommand.cs`.

**Problem:** The configuration wizard asks many questions. New users don't know which provider to choose, how many depth levels to capture, or what storage settings to use. The project has 5+ streaming providers, 10+ historical providers, and dozens of config knobs.

**Improvement:** Add 4 role-based presets to `ConfigurationWizard` and `AutoConfigurationService`:

| Preset | Description | Defaults |
|--------|-------------|----------|
| **Researcher** | Historical analysis, daily bars | Stooq + Yahoo backfill, BySymbol storage, Parquet export, no real-time |
| **Day Trader** | Real-time streaming, L2 data | Alpaca streaming, 10 depth levels, JSONL hot storage, low-latency profile |
| **Options Trader** | Options chain + Greeks | IB streaming, derivatives enabled, weekly/monthly expirations |
| **Crypto** | 24/7 crypto collection | Alpaca crypto feed, no market hours filter, extended retention |

Each preset sets ~15 config values at once. Users can customize after applying a preset.

**Value:** High -- reduces time-to-value from 30+ minutes of config to 2 minutes.
**Cost:** ~4-6 hours. Define preset dictionaries, add `--preset <name>` CLI flag.
**Files:** `src/Meridian.Application/Services/ConfigurationWizard.cs`, `src/Meridian.Application/Services/AutoConfigurationService.cs`

---

### 9.5 Bulk symbol import from CSV/text file — ✅ Implemented

**Status (2026-03-15):** `SymbolCommands.cs` has `--symbols-import <file>` supporting CSV, TXT, and JSON formats with auto-detection, symbol validation, preview prompt, and `--symbols-export <file>` for round-trip sharing.

**Problem:** Adding 100+ symbols requires either editing `appsettings.json` manually or using `--symbols-add` one batch at a time. There's no way to import from a watchlist file, a broker export, or a simple text file with one symbol per line.

**Improvement:** Add `--symbols-import <file>` CLI flag that:
1. Reads a file (CSV, TXT, or JSON)
2. Detects format automatically (one-per-line, comma-separated, or JSON array)
3. Validates each symbol against the active provider's symbol search
4. Shows a preview: `"Found 147 valid symbols, 3 unknown (XYZ, FOO, BAR). Proceed? [Y/n]"`
5. Adds validated symbols to the configuration

Also add `--symbols-export <file>` to export the current symbol list for sharing.

**Value:** High -- users with large portfolios save hours of manual entry.
**Cost:** ~4-6 hours. File parsing is trivial; symbol validation uses existing `ISymbolSearchProvider`.
**Files:** `src/Meridian.Application/Commands/SymbolCommands.cs`

---

### 9.6 Collection health email/webhook digest — 📝 Future

**Problem:** `DailySummaryWebhook` sends Slack/Discord/Teams notifications at market close. But many users want a simple email digest or don't use chat platforms. There's also no weekly summary -- only daily.

**Improvement:**
1. Add a `WeeklySummaryWebhook` that aggregates daily stats into a week-over-week comparison:
   - Total events collected vs. previous week
   - Average SLA compliance with trend arrow
   - Top 5 symbols by gap count
   - Storage growth rate and projected capacity
2. Add an `--email-digest <address>` config option using SMTP (basic `SmtpClient` or `MailKit`)
3. Add `"Summary.Schedule": "daily|weekly|both"` config option

**Value:** Medium-High -- keeps users informed without requiring dashboard checks.
**Cost:** ~6-8 hours. Daily webhook exists; extend with weekly aggregation and email transport.
**Files:** `src/Meridian.Application/Services/DailySummaryWebhook.cs`

---

### 9.7 One-click data export from web dashboard — 🔄 Partial

**Status (2026-03-15):** The dashboard has export buttons for comparison reports and symbol mappings, but not a general-purpose export UI with symbol selector, date range, and format chooser. All backend export endpoints are live; frontend wiring remains.

**Problem:** The export API exists (7 formats: Parquet, CSV, JSON, Arrow, SQL, Excel, Lean) but can only be triggered via API calls or the WPF desktop app. The web dashboard has no export UI. Users collecting data headlessly on a server must craft API calls manually.

**Improvement:** Add an export section to the web dashboard HTML template:
1. Symbol selector (multi-select from monitored symbols)
2. Date range picker (from/to)
3. Format selector (dropdown: CSV, Parquet, JSON)
4. "Export" button that calls `POST /api/export/create` and shows download link
5. Recent exports list from `/api/export/history`

The backend endpoints already exist. This is a frontend-only addition.

**Value:** High -- makes headless server deployments fully self-service.
**Cost:** ~6-8 hours. HTML/JS template work; all backend endpoints exist.
**Files:** `src/Meridian/wwwroot/templates/`, `src/Meridian.Application/Http/HtmlTemplates.cs`

---

### 9.8 Provider comparison and recommendation engine — 📝 Future

**Problem:** New users face 5 streaming providers and 10 historical providers with no guidance on which to choose. The provider comparison doc exists in markdown but isn't programmatically accessible. Users must read docs and cross-reference feature tables manually.

**Improvement:** Add a `--recommend-providers` CLI command that:
1. Asks what symbols the user wants to collect (or reads from config)
2. Checks which providers support those symbols (via `ISymbolSearchProvider`)
3. Checks which providers the user has credentials for
4. Scores providers by: credential availability, symbol coverage, rate limits, data types supported
5. Outputs a recommendation table:

```
Recommended providers for your 15 symbols:
  Streaming: Alpaca (✓ credentials, 15/15 symbols, trades+quotes)
  Backfill:  Stooq (✓ free, 15/15 symbols, daily bars)
             Yahoo Finance (✓ free, 15/15 symbols, daily bars, backup)
  Note: IB would add L2 depth but requires TWS running
```

**Value:** Medium-High -- eliminates the "which provider?" analysis paralysis.
**Cost:** ~6-8 hours. Provider metadata exists in `DataSourceRegistry`; build scoring logic.
**Files:** `src/Meridian.Application/Commands/` (new command), `src/Meridian.ProviderSdk/DataSourceRegistry.cs`

---

### 9.9 Alert noise reduction with smart grouping — ✅ Implemented

**Status (2026-03-15):** `ConnectionStatusWebhook.cs` implements `ShouldSendAlert()` with rate limiting (`MinAlertIntervalSeconds`). `BackpressureAlertService.cs` tracks consecutive high-utilization periods and suppresses repetitive alerts.

**Problem:** During market volatility or provider outages, users receive 100+ alerts per minute for related issues (each stale symbol generates a separate SLA violation, each reconnection attempt generates a separate alert). The `AlertService` has deduplication and suppression logic, but this is only in the WPF desktop app -- not in the web dashboard or webhook notifications.

**Improvement:** Add alert aggregation to `ConnectionStatusWebhook` and `BackpressureAlertService`:
1. If 5+ symbols trigger SLA violations within 60 seconds, send a single grouped alert: `"SLA violation: 12 symbols stale (SPY, AAPL, MSFT, +9 more) — likely provider outage"`
2. If 3+ reconnection attempts occur in 5 minutes, summarize: `"Provider Alpaca: 5 reconnection attempts in last 5 min, currently retrying (attempt 3/10)"`
3. Send a "resolved" summary when the batch clears: `"12 symbols recovered after 3m 15s outage"`

**Value:** Medium-High -- prevents alert fatigue, which causes users to ignore real problems.
**Cost:** ~6-8 hours. Add a batching/windowing layer before webhook dispatch.
**Files:** `src/Meridian.Application/Monitoring/ConnectionStatusWebhook.cs`, `src/Meridian.Application/Monitoring/DataQuality/DataFreshnessSlaMonitor.cs`

---

### 9.10 Data completeness summary in CLI output — ✅ Implemented

**Status (2026-03-15):** `GracefulShutdownService.cs` prints a formatted collection session summary via `PrintSessionSummary()` on shutdown, covering event counts, throughput, latency, memory, and GC stats.

**Problem:** After a collection session ends (graceful shutdown), there's no summary of what was collected. Users must query the API or inspect storage files to understand the session's output. The daily summary webhook provides some of this, but only for users who configured webhooks.

**Improvement:** On graceful shutdown, print a collection session summary to the console:

```
Session Summary (2h 15m 42s):
  Events collected:  1,247,831
  Events dropped:    23 (0.002%)
  Symbols active:    15
  Data completeness: 99.8%
  Storage written:   847 MB (JSONL: 623 MB, Parquet: 224 MB)
  Gaps detected:     2 (SPY 10:31-10:32, AAPL 14:05-14:06)
  Files created:     45
```

The data is available from `Metrics`, `DataQualityMonitoringService`, and `StorageCatalogService`.

**Value:** Medium-High -- gives immediate feedback on session quality without additional tools.
**Cost:** ~3-4 hours. Wire existing metrics into `GracefulShutdownService` summary.
**Files:** `src/Meridian.Application/Services/GracefulShutdownService.cs`

---

### 9.11 Predictive storage capacity warnings — 🔄 Partial

**Status (2026-03-15):** `QuotaEnforcementService.cs` handles hard quota limits and violation detection. Growth-rate trending and predictive capacity forecasting (the "at current rate, full in N days" projection) are not yet implemented.

**Problem:** Storage fills up silently. Users discover disk full errors only when writes start failing. `QuotaEnforcementService` exists for hard limits, but there's no **predictive** warning ("at current rate, storage will be full in 3 days").

**Improvement:** Add a periodic check (every hour) that:
1. Calculates average storage growth rate over the last 24 hours
2. Projects when available disk space will be exhausted
3. If projected exhaustion is within 7 days, emit a warning: `"Storage warning: At current rate (2.3 GB/day), disk will be full in 4.2 days. Consider enabling tier migration or increasing disk space."`
4. Expose via `/api/storage/capacity-forecast` endpoint

**Value:** Medium -- prevents data loss from full disks.
**Cost:** ~4-6 hours. Storage metrics exist; add trend calculation and alert.
**Files:** `src/Meridian.Storage/Services/QuotaEnforcementService.cs`, `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs`

---

### 9.12 Keyboard shortcut and command palette wiring (WPF) — ✅ Implemented

**Status (2026-03-15):** `MainWindow.xaml.cs` wires `Ctrl+K` to `ShowCommandPalette()` which opens `CommandPaletteWindow`. Top navigation commands are wired and a first-run hint is shown.

**Problem:** The WPF desktop app has `CommandPaletteService` (47 commands with fuzzy search) and `KeyboardShortcutService` (35+ shortcuts) fully implemented and tested. But the Ctrl+K hotkey to open the command palette isn't wired in the main window, and many shortcuts aren't connected to their actions. Users don't know these features exist.

**Improvement:**
1. Wire `Ctrl+K` in `MainWindow.xaml.cs` to open `CommandPaletteWindow`
2. Show a subtle first-run hint: "Press Ctrl+K to open the command palette"
3. Add a "Keyboard Shortcuts" link in the navigation footer
4. Ensure the top 10 most-used commands (navigate to page, start backfill, toggle theme) are wired

**Value:** Medium -- transforms the desktop app from click-heavy to keyboard-driven.
**Cost:** ~2-3 hours. The services exist and are tested; this is event wiring.
**Files:** `src/Meridian.Wpf/MainWindow.xaml.cs`, `src/Meridian.Wpf/Views/CommandPaletteWindow.xaml.cs`

---

## Category 10: Data Consumption & Analysis Workflow

### 10.1 Quick-query CLI for stored data — ✅ Implemented

**Status (2026-03-15):** `QueryCommand.cs` implements `--query` with sub-commands: `last <symbol>`, `count <symbol>`, `summary <symbol>`, `symbols`, and `range`. Backed by `HistoricalDataQueryService` and `StorageCatalogService`.

**Problem:** Users collect data continuously but querying it requires either writing code, using the export API, or opening files manually. There's no quick CLI command to answer "what's the last price for SPY?" or "how many bars do I have for AAPL in January?"

**Improvement:** Add a `--query` CLI mode with common queries:

```bash
# Last known price
dotnet run -- --query "last SPY"
# Output: SPY | Last: 512.34 | Time: 2026-02-23 15:59:58 | Source: Alpaca

# Data inventory
dotnet run -- --query "count AAPL --from 2026-01-01 --to 2026-01-31"
# Output: AAPL | Trades: 1,247,831 | Quotes: 2,891,203 | Bars: 22 | Gaps: 0

# Date range summary
dotnet run -- --query "summary SPY --from 2026-02-01"
# Output: SPY | 16 trading days | 99.7% complete | 2 gaps (total: 4m)
```

**Value:** High -- enables instant data verification without leaving the terminal.
**Cost:** ~6-8 hours. Use `HistoricalDataQueryService` and `StorageCatalogService` as backends.
**Files:** `src/Meridian.Application/Commands/` (new query command)

---

### 10.2 Automatic Parquet conversion for completed trading days — ✅ Implemented

**Status (2026-03-15):** `ParquetConversionService.cs` has `ConvertCompletedDaysAsync()` which identifies prior trading days with JSONL files but no Parquet equivalent and converts them in the background. Configurable JSONL deletion after conversion.

**Problem:** Real-time data is stored as JSONL (optimized for append writes). For analysis, users prefer Parquet (columnar, compressed, fast queries). Currently, Parquet conversion requires manual export or the `CompositeSink` writing both formats simultaneously (doubling I/O).

**Improvement:** Add a background task that runs after market close (or on a schedule):
1. Identifies completed trading days with JSONL files but no Parquet files
2. Converts JSONL to Parquet in the background
3. Optionally deletes the JSONL originals after successful conversion (configurable)
4. Logs: `"Converted 15 JSONL files to Parquet (saved 340 MB). Originals retained."`

This separates the write-optimized hot path (JSONL) from the read-optimized archive (Parquet) without runtime overhead.

**Value:** Medium-High -- gives users analysis-ready files automatically.
**Cost:** ~6-8 hours. Both JSONL reading and Parquet writing exist; add a scheduled converter.
**Files:** `src/Meridian.Storage/Services/`, `src/Meridian.Application/Scheduling/`

---

### 10.3 Python/R loader script generation with exports — ✅ Implemented

**Status (2026-03-15):** `GenerateLoaderCommand.cs` implements `--generate-loader <language>` generating standalone loader scripts for Python, R, PyArrow, and PostgreSQL based on the current data directory.

**Problem:** `PortableDataPackager` creates ZIP packages with loader scripts, but these are only generated for explicit package operations. Users who just want to analyze today's data in Python must write their own loading code.

**Improvement:** Add a `--generate-loader` CLI flag that outputs a ready-to-run Python/R script for the current data directory:

```bash
dotnet run -- --generate-loader python --output ./load_data.py
```

Generated script:
```python
import pandas as pd
from pathlib import Path

DATA_DIR = Path("/data/live/alpaca/2026-02-23")
symbols = ["SPY", "AAPL", "MSFT"]

def load_trades(symbol: str) -> pd.DataFrame:
    return pd.read_json(DATA_DIR / f"{symbol}_trades.jsonl", lines=True)

# Quick start:
# df = load_trades("SPY")
# print(df.describe())
```

**Value:** Medium -- bridges the gap from "collected data" to "usable data" in 10 seconds.
**Cost:** ~3-4 hours. Template-based generation; the storage path conventions are well-defined.
**Files:** `src/Meridian.Application/Commands/` (new command), or extend `PortableDataPackager.Scripts.cs`

---

### 10.4 Wire export API endpoints to real backend processing — ✅ Implemented

**Status (2026-03-15):** `ExportEndpoints.cs` injects and calls `AnalysisExportService` directly. Export jobs run in a background task and status is tracked via a real `ConcurrentDictionary<string, ExportJobStatus>`. Download and job-cleanup are functional.

**Problem:** The export endpoints in `ExportEndpoints.cs` are **stubs** -- they accept requests, return a fake `jobId` and `status: "queued"`, but never actually invoke `AnalysisExportService`. Users calling `POST /api/export/analysis` get an immediate 200 response with no export happening. This is the most critical gap in the data consumption workflow because it silently does nothing.

**Improvement:** Wire the export endpoints to the real `AnalysisExportService`:
1. `POST /api/export/analysis` should call `AnalysisExportService.ExportAsync()` with the request parameters
2. For large exports, run in a background task and return a real job ID tracked in a `ConcurrentDictionary<string, ExportJobStatus>`
3. `GET /api/export/jobs/{jobId}` should return real status (queued/running/complete/failed) with progress percentage
4. `GET /api/export/download/{jobId}` should serve the completed export file
5. Add basic job cleanup (remove completed jobs after 24 hours)

The export service itself is fully implemented with 7 format writers. Only the HTTP layer is stubbed.

**Value:** High -- without this, the web API export feature literally doesn't work.
**Cost:** ~6-8 hours. The export service is built and tested; this is plumbing.
**Files:** `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs`, `src/Meridian.Storage/Export/AnalysisExportService.cs`

---

### 10.5 Data preview before export — ✅ Implemented

**Status (2026-03-15):** `ExportEndpoints.cs` handles `ExportPreviewRequest` at `POST /api/export/preview`, returning sample rows, column schema, record estimates, and projected file size before committing to a full export.

**Problem:** Users can't preview what an export will produce before committing to it. For a large export (weeks of multi-symbol data), they can't verify that their filters are correct, see the resulting schema, or estimate the output file size. A misconfigured export wastes time and disk.

**Improvement:** Add a `POST /api/export/preview` endpoint that:
1. Applies the same filters as a real export but only reads the first 100 records
2. Returns: sample rows, column names with types, total record estimate, projected file size
3. Can be called from the web dashboard before clicking "Export"

```json
{
  "sampleRows": 100,
  "totalEstimate": 1247831,
  "columns": ["Timestamp", "Symbol", "Price", "Volume", "Exchange"],
  "estimatedSize": "847 MB",
  "format": "parquet",
  "warnings": ["Excel format limited to 1M rows; your data has 1.2M rows"]
}
```

**Value:** Medium-High -- prevents wasted exports and builds trust in the export pipeline.
**Cost:** ~4-6 hours. Reuse `HistoricalDataQueryService` with a limit, add size estimation.
**Files:** `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs`, `src/Meridian.Application/Services/HistoricalDataQueryService.cs`

---

### 10.6 Wire FeatureSettings in export pipeline — 📝 Future

**Problem:** `ExportRequest` declares `FeatureSettings` and `AggregationSettings` properties that allow users to request technical indicators (SMA, EMA, RSI), rolling statistics, and time-series aggregation on export. These fields are **parsed from the request but completely ignored** during export -- data is always exported raw.

Users requesting `"Features": { "IncludeIndicators": true, "IndicatorPeriods": [20, 50] }` get raw trades with no indicators, and no error or warning that their request was silently dropped.

**Improvement:**
1. Wire `FeatureSettings.IncludeIndicators` to `TechnicalIndicatorService` (already exists, uses Skender library) during the export pipeline
2. Wire `AggregationSettings` for basic OHLCV bar aggregation from tick data
3. If a requested feature isn't supported yet, return a `warnings` array in the response instead of silently ignoring it

This turns the export from a raw data dump into an analysis-ready dataset.

**Value:** High -- transforms exports from "raw firehose" into analysis-ready data, which is what researchers actually need.
**Cost:** ~8-12 hours. `TechnicalIndicatorService` exists; wire it into the export format pipeline.
**Files:** `src/Meridian.Storage/Export/AnalysisExportService.Formats.cs`, `src/Meridian.Application/Indicators/TechnicalIndicatorService.cs`

---

## Category 11: Trust & Transparency

### 11.1 Data lineage in exports — ✅ Implemented

**Status (2026-03-15):** `AnalysisExportService.IO.cs` writes a `lineage_manifest.json` alongside every export containing source provider, symbols, date range, event types, record count, quality scores, and checksum.

**Problem:** When sharing exported data with a research team or auditor, there's no metadata about where the data came from. Which provider? What quality score? Were there gaps? What was the collection session duration? Users must manually track this information.

**Improvement:** Include a machine-readable manifest file alongside every export:

```json
{
  "exportedAt": "2026-02-23T16:05:00Z",
  "symbols": ["SPY", "AAPL"],
  "dateRange": { "from": "2026-02-01", "to": "2026-02-23" },
  "provider": "Alpaca",
  "format": "parquet",
  "qualityScores": { "SPY": 99.7, "AAPL": 98.2 },
  "knownGaps": [{ "symbol": "SPY", "from": "10:31", "to": "10:32", "date": "2026-02-15" }],
  "recordCount": 1247831,
  "checksum": "sha256:abc123..."
}
```

The data for this already exists in `DataLineageService`, `DataQualityScoringService`, and `StorageChecksumService`.

**Value:** Medium-High -- essential for research reproducibility and compliance.
**Cost:** ~4-6 hours. Assemble existing metadata into a JSON manifest alongside export output.
**Files:** `src/Meridian.Storage/Export/AnalysisExportService.IO.cs`, `src/Meridian.Storage/Services/DataLineageService.cs`

---

### 11.2 Trading calendar awareness in collection status — ✅ Implemented

**Status (2026-03-15):** `CalendarEndpoints.cs` exposes `GET /api/calendar/status` returning current market state, next open/close times, and trading session information. SLA violations are suppressed outside market hours.

**Problem:** Users see "no data received" warnings on weekends, holidays, and outside market hours. The system has `TradingCalendar` with market hours and holiday schedules, but this context isn't surfaced to users. They can't distinguish "no data because market is closed" from "no data because provider is broken."

**Improvement:**
1. In the web dashboard status bar, show market state: `"Market: Closed (weekend) — next open: Mon 9:30 AM ET"`
2. Suppress stale-data warnings outside market hours
3. In the CLI, skip SLA violation logging when market is closed
4. Add `GET /api/calendar/status` endpoint returning current market state and next open/close times

**Value:** Medium-High -- eliminates false alarms that erode user trust.
**Cost:** ~3-4 hours. `TradingCalendar` is fully implemented; expose it.
**Files:** `src/Meridian.Application/Services/TradingCalendar.cs`, `src/Meridian.Ui.Shared/Endpoints/`

---

## Priority Matrix

| ID | Improvement | Value | Cost | Priority | Status |
|----|------------|-------|------|----------|--------|
| 4.1 | Auto gap backfill on reconnection | High | 6-8h | **P1** | 📝 Future |
| 2.1 | Startup health matrix | High | 4-6h | **P1** | ✅ Done |
| 1.1 | Credential validation at startup | High | 4-8h | **P1** | ✅ Done |
| 7.1 | Enforce credentials via env vars | High | 3-4h | **P1** | 🔄 Partial |
| 6.1 | Replace bare catch blocks | High | 2-4h | **P1** | ✅ Done |
| 3.1 | Environment variable reference doc | High | 3-4h | **P1** | ✅ Done |
| 5.4 | Graceful shutdown integration test | High | 6-8h | **P1** | ✅ Done |
| 9.1 | Data freshness indicator on dashboard | High | 3-4h | **P1** | ✅ Done |
| 9.3 | Friendly error messages wiring | High | 4-6h | **P1** | ✅ Done |
| 9.4 | Role-based configuration presets | High | 4-6h | **P1** | ✅ Done |
| 9.5 | Bulk symbol import from file | High | 4-6h | **P1** | ✅ Done |
| 10.1 | Quick-query CLI for stored data | High | 6-8h | **P1** | ✅ Done |
| 10.4 | Wire export API to real backend | High | 6-8h | **P1** | ✅ Done |
| 10.6 | Wire FeatureSettings in export | High | 8-12h | **P1** | 📝 Future |
| 3.3 | JSON Schema for config | High | 6-8h | **P2** | 📝 Future |
| 2.2 | `/api/config/effective` endpoint | High | 6-8h | **P2** | ✅ Done |
| 9.2 | Backfill progress with ETA/resume | High | 6-8h | **P2** | 🔄 Partial |
| 9.7 | One-click export from web dashboard | High | 6-8h | **P2** | 🔄 Partial |
| 10.5 | Data preview before export | Med-High | 4-6h | **P2** | ✅ Done |
| 11.1 | Data lineage in exports | Med-High | 4-6h | **P2** | ✅ Done |
| 11.2 | Trading calendar in collection status | Med-High | 3-4h | **P2** | ✅ Done |
| 9.8 | Provider recommendation engine | Med-High | 6-8h | **P2** | 📝 Future |
| 9.9 | Alert noise reduction / grouping | Med-High | 6-8h | **P2** | ✅ Done |
| 9.10 | Session summary on shutdown | Med-High | 3-4h | **P2** | ✅ Done |
| 10.2 | Auto Parquet conversion after close | Med-High | 6-8h | **P2** | ✅ Done |
| 1.2 | Legacy config deprecation warning | Medium | 1-2h | **P2** | ✅ Done |
| 1.3 | Provider-specific field validation | Medium | 3-4h | **P2** | 📝 Future |
| 2.3 | WAL recovery metrics | Medium | 2-3h | **P2** | 📝 Future |
| 2.4 | Reconnection log standardization | Medium | 3-4h | **P2** | ✅ Done |
| 4.2 | Cross-provider divergence alerting | Medium | 4-6h | **P2** | ✅ Done |
| 4.3 | Checksum verification on read | Medium | 4-6h | **P2** | 📝 Future |
| 5.1 | Flaky test detection | Medium | 3-4h | **P2** | 📝 Future |
| 5.2 | Test execution time tracking | Medium | 3-4h | **P2** | 🔄 Partial |
| 5.3 | Benchmark regression detection | Medium | 4-6h | **P2** | 🔄 Partial |
| 3.2 | Offline config validation CLI | Medium | 4-6h | **P2** | 🔄 Partial |
| 3.4 | `make quickstart` target | Medium | 2-3h | **P2** | ✅ Done |
| 9.6 | Weekly digest and email support | Medium | 6-8h | **P2** | 📝 Future |
| 9.11 | Predictive storage capacity warnings | Medium | 4-6h | **P2** | 🔄 Partial |
| 10.3 | Python/R loader script generation | Medium | 3-4h | **P2** | ✅ Done |
| 6.2 | `TimeProvider` abstraction | Medium | 4-6h | **P3** | 📝 Future |
| 6.4 | Endpoint handler consolidation | Medium | 6-8h | **P3** | ✅ Done |
| 7.2 | API key rotation | Medium | 4-6h | **P3** | 📝 Future |
| 8.1 | Parallel provider initialization | Medium | 2-3h | **P3** | ✅ Done |
| 8.2 | Conditional Parquet sink | Medium | 2-3h | **P3** | ✅ Done |
| 9.12 | Command palette hotkey wiring | Medium | 2-3h | **P3** | ✅ Done |
| 6.3 | `Lazy<T>` consolidation | Low-Med | 4-8h | **P3** | 📝 Future |
| 8.3 | Config double-read elimination | Low | 2-3h | **P4** | 📝 Future |

---

## Implementation Notes

- **P1 items** are independent of each other and can be implemented in any order or in parallel
- Most improvements are additive (new code paths gated by config) rather than modifying hot paths
- All improvements should include corresponding test coverage
- Items in Categories 1-2 (startup/ops) deliver the most immediate user-facing value
- Items in Category 5 (CI) compound in value over time as the test suite grows
- Category 6 (code quality) items can be done opportunistically alongside other work
- **Category 9 items are disproportionately cheap** because the backend services already exist and are tested — the work is wiring, not building. Most are now complete.
- **Category 10 items bridge the "collection to analysis" gap** that determines whether users stick with the tool long-term. Items 10.1–10.5 are all complete; 10.6 (FeatureSettings wiring) remains.
- **Category 11 items** build user trust through transparency — lineage, calendar awareness, and quality metadata make the system credible for research use. Both are complete.
- **Total: 47 improvements** across 11 categories. As of 2026-03-15: **27 implemented, 7 partial, 13 open**.

### Remaining Open Items (13)

High-priority open work:
- **4.1** Auto gap backfill on reconnection (`WebSocketReconnectionHelper` → `BackfillCoordinator`)
- **10.6** Wire FeatureSettings/indicators in export pipeline (`TechnicalIndicatorService` → `AnalysisExportService`)
- **3.3** JSON Schema for `appsettings.json` (IDE autocomplete)

Medium-priority open work:
- **2.3** WAL recovery metrics (Prometheus counters + structured log in `WriteAheadLog.RecoverAsync`)
- **1.3** Provider-specific field validation in config (warn on IB fields with Alpaca active)
- **4.3** Checksum verification on read (`VerifyOnRead` option in `StorageOptions`)
- **5.1** Flaky test detection and quarantine in CI
- **6.2** `TimeProvider` abstraction in time-sensitive services
- **7.2** API key rotation endpoint (`POST /api/admin/rotate-key`)
- **9.6** Weekly digest and email support (extend `DailySummaryWebhook`)
- **9.8** Provider recommendation engine (`--recommend-providers` CLI command)

Partial completions needing follow-through:
- **3.2** Add `--check-config` alias (currently `--dry-run --offline`)
- **7.1** Credential-in-config file warning (only format validation today)
- **9.2** Wire `BackfillCheckpointService` into core `HistoricalBackfillService` + `--resume` flag
- **9.7** Full export UI section in web dashboard (symbol selector + date range + format)
- **9.11** Growth-rate trending in `QuotaEnforcementService` (capacity forecast)
- **5.2** Post top-N slow tests to GitHub Actions job summary from TRX files
- **5.3** Cross-run benchmark regression comparison (artifacts exist; diff logic missing)

Lower-priority open work:
- **6.3** `Lazy<T>` consolidation (replace manual double-checked locking)
- **8.3** Config double-read elimination at startup
