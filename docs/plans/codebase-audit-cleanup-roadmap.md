# Codebase Audit & Cleanup Roadmap

**Date:** 2026-03-19
**Version:** 1.6.2
**Scope:** Complete existing functionality, clean up repository, improve maintainability

---

## Executive Summary

The Meridian codebase has 773+ source files, ~4,135 tests, 13 main projects, and 32 CI/CD workflows. While 94.3% of core improvement items are complete, this audit identified:

- **2 broken projects** using legacy `Meridian.*` namespace with invalid references
- **3 recurring namespace collision patterns** causing CS0104 build ambiguity
- **9 unused event handlers** suppressed via `#pragma warning disable`
- **52 WPF pages** with some placeholder-only implementations
- **3 incomplete improvement items** (C3 WebSocket, G2 tracing, J8 CI canary)
- **32 CI workflows** with stubs and potential consolidation targets
- **16 AI known-error entries** documenting recurring agent mistake patterns

The roadmap is organized into 5 phases, ordered by impact. Phases 1-2 address structural and functional gaps. Phases 3-5 address maintainability and documentation.

---

## Phase 1: Critical Structural Fixes

> Fix issues that cause build failures, namespace confusion, or block development.

### 1.1 Migrate Meridian.* Projects to Meridian.* (L) ✅ Complete

**Status:** Completed — the execution/strategies projects, namespaces, project references, and solution entries now use the `Meridian.*` naming throughout source.

**Problem:** `src/Meridian.Execution/` and `src/Meridian.Strategies/` reference non-existent projects (`Meridian.Contracts`, `Meridian.Core`, `Meridian.ProviderSdk`, `Meridian.Backtesting.Sdk`). 22 source files still use the old namespace prefix. These projects cannot build.

**Tasks:**
1. Rename `Meridian.Execution/` -> `Meridian.Execution/`
2. Rename `Meridian.Strategies/` -> `Meridian.Strategies/`
3. Rename `.csproj` files to match new project names
4. Update all ProjectReferences to point to `Meridian.Contracts`, `Meridian.Core`, `Meridian.ProviderSdk`, `Meridian.Backtesting.Sdk`
5. Replace `namespace Meridian.*` with `namespace Meridian.*` in all 22 source files
6. Update `GlobalUsings.cs` in both projects
7. Update `Meridian.sln` entries
8. Verify no other projects reference the old names

**Files:**
- `src/Meridian.Execution/Meridian.Execution.csproj`
- `src/Meridian.Strategies/Meridian.Strategies.csproj`
- `src/Meridian.Execution/**/*.cs` (all source files)
- `src/Meridian.Strategies/**/*.cs` (all source files)
- `Meridian.sln`

**Verify:**
```bash
dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true
grep -r "Meridian" src/ --include="*.cs" --include="*.csproj" | grep -v "//\|archived"
```

**Effort:** Large — namespace migration touches many files, requires careful verification.

---

### 1.2 Resolve Duplicate Type Names Across Namespaces (M) ✅ Complete

**Status:** Completed — the ambiguous DTO/UI-facing types have been renamed to `MarketEventDto`, `BackfillResultDto`, and `DiagnosticValidationResult`, and consumers now reference the non-conflicting names.

**Problem:** Three type name collisions have caused repeated CS0104 build failures (documented in ai-known-errors.md):

| Type Name | Namespace A | Namespace B | Recommendation |
|-----------|------------|------------|----------------|
| `MarketEvent` | `Meridian.Contracts.Domain.Events` | `Meridian.Domain.Events` | Rename Contracts variant to `MarketEventDto` |
| `BackfillResult` | `Meridian.Contracts.Api` | `Meridian.Application.Backfill` | Rename Contracts variant to `BackfillResultDto` |
| `ConfigValidationResult` | `Meridian.Ui.Services` (DiagnosticsService.cs) | `Meridian.Ui.Services.Contracts` (IConfigService.cs) | Rename parent-namespace variant to `DiagnosticValidationResult` |

**Tasks:**
1. Rename lighter/DTO variants to eliminate ambiguity
2. Update all consuming files (using statements, type references)
3. Update GlobalUsings.cs files in affected projects
4. Verify no new ambiguity is introduced

**Verify:**
```bash
dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true 2>&1 | grep CS0104
```

**Effort:** Medium — type renames cascade through consumers, but can be done with find-and-replace.

---

## Phase 2: Complete Existing Features

> Finish the 3 remaining core improvement items and audit WPF pages.

### 2.1 WebSocket Base Class for NYSE & StockSharp — C3 Completion (M)

**Problem:** `WebSocketProviderBase` (in `src/Meridian.Infrastructure/Adapters/Core/WebSocketProviderBase.cs`) has been adopted by Polygon but NYSE and StockSharp still use bespoke WebSocket logic.

**Tasks:**
1. Refactor NYSE streaming client to extend `WebSocketProviderBase`
2. Refactor StockSharp streaming client to extend `WebSocketProviderBase`
3. Add/update tests for both providers
4. Verify reconnection and backpressure behavior matches Polygon pattern

**Reference:** `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs` (completed example)

**Files:**
- `src/Meridian.Infrastructure/Adapters/NYSE/NyseMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/Core/WebSocketProviderBase.cs`

**Effort:** Medium — pattern exists, mostly mechanical refactor.

---

### 2.2 OpenTelemetry Trace Propagation — G2 Completion (M)

**Problem:** Trace propagation is partial. Activity/trace context doesn't flow end-to-end from provider ingestion through the EventPipeline bounded channels to storage sinks.

**Tasks:**
1. Ensure `TracedEventMetrics` propagates `Activity` context when enqueuing to bounded channels
2. Add trace context to `IStorageSink.WriteAsync` calls
3. Verify traces appear in telemetry exports spanning the full pipeline
4. Add integration test verifying trace propagation

**Files:**
- `src/Meridian.Application/Pipeline/EventPipeline.cs`
- `src/Meridian.Application/Pipeline/TracedEventMetrics.cs`
- `src/Meridian.Storage/Sinks/JsonlStorageSink.cs`
- `src/Meridian.Storage/Sinks/ParquetStorageSink.cs`

**Effort:** Medium

---

### 2.3 Golden Fixture CI Canary — J8 Completion (S)

**Problem:** J8 canonicalization golden fixture CI canary is only partially implemented.

**Tasks:**
1. Add a dedicated step to `pr-checks.yml` that runs golden fixture tests
2. Ensure the step fails loudly on canonicalization regressions
3. Verify it runs on every PR

**Files:**
- `.github/workflows/pr-checks.yml`
- `tests/Meridian.Tests/**/CanonicalizationGoldenFixtureTests.cs`

**Effort:** Small

---

### 2.4 WPF Placeholder Page Audit & Labeling (S)

**Problem:** The WPF app has 52 pages declared in `Pages.cs`. Some are fully implemented, others are empty shells with no visible "Coming Soon" indicator.

**Tasks:**
1. Audit each XAML page — categorize as: **functional**, **partial**, or **placeholder**
2. For placeholder pages, add a consistent "Coming Soon" overlay using a shared `PlaceholderOverlay` UserControl
3. Document the categorization in a table in `docs/development/wpf-page-status.md`
4. Do NOT implement new page functionality — just label and document

**Files:** `src/Meridian.Wpf/Views/*.xaml` and `*.xaml.cs`

**Effort:** Small — no new functionality, just audit and labeling.

---

## Phase 3: Code Cleanup

> Improve code consistency and remove dead code.

### 3.1 Seal Unsealed Classes (S)

Per CLAUDE.md conventions and ADR-001, classes should be `sealed` unless designed for inheritance.

**Seal these:**
- `WatchlistService` (`src/Meridian.Ui.Services/Services/WatchlistService.cs`)
- `ConfigService` (`src/Meridian.Ui.Services/Services/ConfigService.cs`)
- `CredentialService` (`src/Meridian.Ui.Services/Services/CredentialService.cs`)
- `CredentialVaultItem` (DTO in `src/Meridian.Wpf/Views/SettingsPage.xaml.cs`)

**Leave unsealed (intentional):**
- `*ServiceBase` classes (abstract base classes)
- `EventBuffer<T>` (designed for extension)
- Exception hierarchy types (`MeridianException`, `DataProviderException`)
- `SampleLeanAlgorithm` (user-extensible sample)

**Effort:** Small — add `sealed` keyword, rebuild, run tests.

---

### 3.2 Resolve Unused Event Handlers (M)

Nine events are declared but never raised, suppressed via `#pragma warning disable CS0067`:

| Event | File | Action |
|-------|------|--------|
| `BackfillRequestQueue.OnRequestReady` | Application/Backfill/ | Remove — backfill uses different signaling |
| `CompositeHistoricalDataProvider.OnProgressUpdate` | Infrastructure/Adapters/Core/ | Keep — needed for future progress UI |
| `CredentialTestingService.OnTokenRefreshed` | Application/Credentials/ | Remove — token refresh uses different pattern |
| `LeanIntegrationService.BacktestStatusChanged` | Application/Integrations/ | Keep — Lean integration is active |
| `BackfillScheduleManager.ScheduleDue` | Application/Backfill/ | Remove — scheduler uses channel-based signaling |
| `ProviderDegradationScorer.OnProviderRecovered` | Application/Monitoring/ | Keep — recovery notification planned |
| `TimeSeriesAlignmentService.ProgressChanged` | Application/Services/ | Keep — needed for WPF progress bars |
| `EventReplayService.EventReplayed` | Application/Services/ | Keep — event replay page needs these |
| `EventReplayService.EventReplayCompleted` | Application/Services/ | Keep — event replay page needs these |

**Tasks:**
1. Remove 3 dead events and their `#pragma` suppressions
2. For 6 retained events, add `// TODO: Wire up in [component]` comment and remove the `#pragma` only when the raise logic is added
3. Create tracking issues for the retained events

**Effort:** Medium

---

### 3.3 Move Template Provider to docs/ (S)

**Problem:** `src/Meridian.Infrastructure/Adapters/_Template/` is documentation, not production code. It compiles as part of the Infrastructure project unnecessarily.

**Tasks:**
1. Move `_Template/` to `docs/examples/provider-template/`
2. Exclude from compilation
3. Update `docs/development/provider-implementation.md` references

**Effort:** Small

---

### 3.4 Isolate Polygon STUB Mode (S)

**Problem:** `PolygonMarketDataClient.cs` generates synthetic trade data in production code when no API key is configured.

**Tasks:**
1. Extract stub behavior to `PolygonStubClient` in test project
2. Remove synthetic data generation from production client
3. Client should fail fast with a clear `ConfigurationException` when API key is missing

**File:** `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`

**Effort:** Small

---

### 3.5 Consolidate Conditional Compilation #else Stubs (S)

**Problem:** IB and StockSharp adapters have many `NotSupportedException` throws under `#else` blocks. While by-design, each method individually throws, creating noise.

**Tasks:**
1. Add a single `ThrowPlatformNotSupported()` helper per adapter
2. Replace individual throws with helper calls
3. Add XML doc comments explaining the pattern

**Effort:** Small

---

## Phase 4: Repository Organization

> Reduce CI/CD maintenance burden and clean up documentation.

### 4.1 Consolidate CI Workflows (M) ✅ Partially complete

| Workflow | Issue | Status |
|----------|-------|--------|
| `docker-image.yml` | 316-byte stub, no real implementation | ✅ Removed; CI build job merged into `docker.yml` |
| `dotnet-desktop.yml` | All triggers disabled; superseded by `desktop-builds.yml` | ✅ Removed |
| `update-uml-diagrams.yml` | Duplicate of diagram job | ✅ Removed; merged into `update-diagrams.yml` |
| `copilot-pull-request-reviewer.yml` | Unclear if actively used | Pending: verify with team; remove if unused |
| `copilot-swe-agent-copilot.yml` | Unclear if actively used | Pending: verify with team; remove if unused |
| `copilot-setup-steps.yml` | Unclear if actively used | Pending: verify with team; remove if unused |
| `documentation.yml` vs `docs-check.yml` | Potential overlap | Pending: merge if functionality overlaps |

**Target:** Reduce from 32 to ~24 workflows.

**Effort:** Medium

---

### 4.2 Prune Archived Documentation (S)

**Tasks:**
1. Review `docs/archived/` — identify files older than 6 months fully superseded by current docs
2. Remove superseded files
3. Update archive index to reference git history

**Effort:** Small

---

### 4.3 Documentation Freshness Audit (M)

**Tasks:**
1. Run `make ai-audit` to identify stale docs
2. Review `docs/evaluations/` (4 files, 3,154 lines) for currency
3. Move completed plans from `docs/plans/` to `docs/archived/`
4. Check `docs/ai/` for redundancy across `copilot/`, `agents/`, `prompts/` subdirectories

**Effort:** Medium

---

## Phase 5: Testing & Documentation Gaps

> Fill coverage gaps and update documentation to reflect changes.

### 5.1 Add Tests for Under-Tested Providers (L)

**Priority order** (providers most likely lacking tests):

| Provider | Likely Coverage | Test Priority |
|----------|----------------|---------------|
| TwelveData | Low | High |
| NasdaqDataLink | Low | High |
| OpenFigi | Medium | Medium |
| AlphaVantage | Low | High |
| Stooq | Medium | Medium |
| Finnhub | Medium | Medium |

For each, add at minimum:
- Constructor/configuration validation
- Response parsing (happy path + error)
- Rate limit handling
- Connection lifecycle

**Effort:** Large

---

### 5.2 Expand Backtesting Module Tests (M)

**Files to cover:**
- `BacktestEngine` — execution flow, multi-symbol, edge cases
- `BarMidpointFillModel` and `OrderBookFillModel` — fill logic accuracy
- `MultiSymbolMergeEnumerator` — merge ordering correctness
- `StrategyRunStore` — persistence

**Effort:** Medium

---

### 5.3 Post-Cleanup Documentation Update (S)

After Phases 1-4, update:
- `CLAUDE.md` — file counts, project list, version
- `docs/ai/claude/CLAUDE.structure.md` — reflect project renames/removals
- `docs/status/CHANGELOG.md` — add cleanup changelog entry
- `docs/ai/ai-known-errors.md` — add entries for any new patterns discovered

**Effort:** Small

---

## Dependency Graph

```
Phase 1 ─── Critical Fixes (do first)
  ├── 1.1 Migrate Meridian.* projects
  └── 1.2 Resolve duplicate type names
           │
Phase 2 ─── Feature Completion (after Phase 1 builds clean)
  ├── 2.1 WebSocket base for NYSE/StockSharp (C3)
  ├── 2.2 Trace propagation (G2)
  ├── 2.3 Golden fixture CI canary (J8)
  └── 2.4 WPF page audit & labeling
           │
Phase 3 ─── Code Cleanup (independent, parallelizable)
  ├── 3.1 Seal classes
  ├── 3.2 Unused event handlers
  ├── 3.3 Move template provider
  ├── 3.4 Isolate Polygon stub
  └── 3.5 Conditional compilation stubs
           │
Phase 4 ─── Repo Organization (independent, parallelizable)
  ├── 4.1 Consolidate CI workflows
  ├── 4.2 Prune archived docs
  └── 4.3 Documentation freshness audit
           │
Phase 5 ─── Testing & Docs (after code changes stabilize)
  ├── 5.1 Provider test coverage
  ├── 5.2 Backtesting module tests
  └── 5.3 Post-cleanup documentation update
```

Phases 3 and 4 are independent of each other and can run in parallel.
Phase 5 should be last as it documents the final state.

---

## Final Verification

After all phases are complete:
```bash
# Build
dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true

# Tests
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release
dotnet test tests/Meridian.Backtesting.Tests -c Release /p:EnableWindowsTargeting=true

# Audit
make ai-audit
python3 build/scripts/ai-repo-updater.py diff-summary

# Verify no legacy namespaces
grep -r "Meridian" src/ --include="*.cs" --include="*.csproj" | wc -l  # should be 0

# Verify no CS0104 ambiguities
dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true 2>&1 | grep CS0104  # should be empty
```
