# QuantScript, L3 Inference & Multi-Instance: Round 2 Feature Roadmap (Ideas #19–#30)

**Version:** 1.0
**Last Updated:** 2026-04-07
**Audience:** Quantitative researchers, core contributors, platform engineers, institutional operators
**Related Plans:**
- QuantScript v1 blueprint: [`docs/plans/quant-script-environment-blueprint.md`](quant-script-environment-blueprint.md)
- L3 inference implementation plan: [`docs/plans/l3-inference-implementation-plan.md`](l3-inference-implementation-plan.md)
- Previous brainstorm (ideas #1–#18): [`docs/evaluations/quant-script-blueprint-brainstorm.md`](../evaluations/quant-script-blueprint-brainstorm.md)

---

This document translates twelve Round 2 feature proposals — spanning QuantScript tooling (Track G),
L3 inference enrichment (Track H), and multi-instance cluster management (Track I) — into a
viability-assessed, dependency-ordered implementation roadmap. Each feature is evaluated against
Meridian's existing code, and all source file references are pinned to production paths.

**Effort key:** S = days | M = 1–2 weeks | L = 1+ month

---

## Ideas at a Glance

| # | Idea | Track | Effort | Audience | Impact | Depends On |
|---|------|-------|--------|----------|--------|------------|
| 19 | Script Debugger (Step / Watch / Breakpoint) | G — QuantScript | L | Hobbyist, Academic | High | QuantScript v1 ScriptRunner |
| 20 | Script Run History & Result Diffing | G — QuantScript | M | Hobbyist, Academic | High | QuantScript v1, StorageCatalogService |
| 21 | Security Master Data Access in QuantScript | G — QuantScript | S | Academic, Institutional | Medium-High | QuantScript v1, ISecurityMasterQueryService |
| 22 | Script Output Export (PDF / Parquet / Excel) | G — QuantScript | M | Academic, Institutional | Medium-High | QuantScript v1, AnalysisExportService |
| 23 | Script Expression REPL | G — QuantScript | S | Hobbyist | Medium | ScriptRunner |
| 24 | Venue-Specific L3 Calibration Profiles | H — L3 Inference | M | Academic, Institutional | High | L3 Phase 1 calibration store |
| 25 | TCA Report: Execution Cost Attribution | H — L3 Inference | M | Academic, Institutional | Very High | L3 fill tape, BacktestMetricsEngine |
| 26 | Multi-Day Queue State Continuity | H — L3 Inference | M | Institutional | Medium | L3 InferenceEngine, simulation manifest |
| 27 | Dark Pool / Hidden Liquidity Estimator | H — L3 Inference | L | Academic, Institutional | Medium | L3 Phase 1, trade aggressor-side data |
| 28 | Zero-Downtime Rolling Upgrade Orchestration | I — Multi-Instance | M | Institutional | High | LeaseManager, ClusterCoordinatorService, #30 |
| 29 | Geo-Distributed Collection by Exchange Timezone | I — Multi-Instance | L | Institutional | Medium | SubscriptionOwnershipService, #30 |
| 30 | Split-Brain Detection & Recovery | I — Multi-Instance | M | Institutional | High | ICoordinationStore, SharedStorageCoordinationStore |

---

## Track G — QuantScript: Additional Ideas (Round 2)

### Feature #19 — Script Debugger (Step / Watch / Breakpoint)

#### Overview

Roslyn scripting's `ScriptState` model exposes all declared variables after each top-level
statement. A lightweight "watch panel" in the QuantScript page interrogates this state: after each
cell executes, `ScriptRunner` serializes variable names, types, and values into a
`VariableWatchViewModel`, which drives a fourth "Watch" tab in the right panel (alongside Console,
Charts, and Metrics).

#### Design

`ScriptRunner` completes a cell's `CSharpScript.RunAsync` / `ContinueWithAsync` call and then
reflects over `ScriptState.Variables`:

```csharp
// Pseudocode — src/Meridian.QuantScript/Execution/ScriptRunner.cs
foreach (var v in scriptState.Variables)
{
    var preview = SmartPreview.Summarize(v.Name, v.Type, v.Value);
    watchBus.Publish(new WatchEntry(v.Name, v.Type.Name, preview));
}
```

**Smart preview rules:**

| Runtime type | Preview |
|---|---|
| `PriceSeries` | `"{Length} bars ({FirstDate}…{LastDate})"` |
| `ReturnSeries` | `"mean={Mean:F4}, σ={StdDev:F4}"` |
| `double` / `float` | Raw value, 6 sig-figs |
| `GridCellResult[]` | `"{Count} results, best Sharpe={MaxSharpe:F2}"` |
| Other | `ToString()` truncated to 120 chars |

**v1 pragmatic step-through:** A `// @watch` comment directly above a cell causes `ScriptRunner`
to pause execution after that cell, surface the watch panel, and wait for a "Continue" button
press on `ContinueCommandRelay`. True Roslyn interactive debug-host step-through is deferred to v2.
Cell-level pause matches the notebook mental model and is sufficient for 95% of debugging sessions.

**Watch panel layout:** Fourth tab in `QuantScriptViewModel.RightPanelTabs`. An
`ObservableCollection<WatchEntryViewModel>` drives a `DataGrid` with three columns: Name, Type,
Value. `BacktestProxy` fills surface the trade stream in the watch panel as a scrollable mini-grid
rendered via `WatchTradeGridViewModel`.

#### Implementation Notes

- `SmartPreview` is a static helper class in `src/Meridian.QuantScript/Debug/SmartPreview.cs`.
- `VariableWatchViewModel` lives in `src/Meridian.QuantScript/ViewModels/VariableWatchViewModel.cs`.
- The `// @watch` directive is detected by a simple prefix scan before each cell's source
  text is submitted to Roslyn — no syntax tree walking required.
- The "Continue" button binds to `IScriptRunner.ContinueAsync()` which resolves a
  `TaskCompletionSource` held by the paused runner.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| QuantScript v1 `IScriptRunner` | Planned | `src/Meridian.QuantScript/Compilation/IScriptRunner.cs` |
| QuantScript v1 cell execution model (idea #1 from Round 1) | Planned | `docs/plans/quant-script-environment-blueprint.md` |

#### Tradeoffs

- Roslyn v1 watch is reflection-only; it cannot halt mid-expression (e.g., inside a `foreach`).
  The mental model is "after-cell checkpoint", not "line-level breakpoint". Document this clearly.
- Off-screen cell virtualization in the WPF `ItemsControl` must account for the watch tab
  being active while cells scroll — ensure `WatchEntryViewModel` is not disposed on scroll.

#### Audience

Hobbyist, Academic. The watch panel lowers the cognitive cost of understanding what variables
hold after each analysis step — the most common source of "why is my Sharpe negative?"
confusion.

#### Effort

**L** (~3–4 weeks). The `SmartPreview` formatter, `VariableWatchViewModel`, and the directive
parser are modest. The bulk of the effort is in v1 `ScriptRunner` cell-pause plumbing and the
correct `TaskCompletionSource` / async relay chain.

---

### Feature #20 — Script Run History & Result Diffing

#### Overview

`ScriptRunner` persists a lightweight `ScriptRunRecord` to
`{ScriptLibraryRoot}/.history/{script-name}.jsonl` after every successful run. The QuantScript
page adds a "History" tab in the left sidebar. Clicking "Diff" on any historical entry opens a
split view showing script text changes (left) and metrics changes (right).

#### Design

**`ScriptRunRecord` schema:**

```jsonc
{
  "runId": "2026-04-07T14:32:00Z",
  "meridianBinaryHash": "a3f1c...",
  "scriptContentHash": "b9d2e...",
  "printOutput": ["Loaded SPY: 2517 bars", "Sharpe: 1.42"],
  "resultsSummary": {
    "sharpe": 1.42,
    "cagr": 0.183,
    "maxDrawdown": -0.091,
    "hadBacktest": true
  }
}
```

`StorageCatalogService` provides the root path via `ICatalogPathResolver.ScriptLibraryRoot`.
The `.history/` subdirectory is created on first write; each `{script-name}.jsonl` file is
appended with one JSON line per run.

**History tab layout:**

- Left sidebar gains a collapsible "History" section listing runs by timestamp.
- Each row: timestamp, one-line summary (e.g., `"Sharpe 1.42 · CAGR 18.3%"`), "Diff" button.
- Clicking "Diff" sets `QuantScriptViewModel.DiffMode = true` and populates:
  - `DiffViewModel.LeftScript` = historical script content hash → resolved via hash lookup
  - `DiffViewModel.RightScript` = current script text
  - `DiffViewModel.LeftMetrics` / `RightMetrics` = `ResultsSummary` DTOs side-by-side

**Text diff algorithm:** A 50-line recursive LCS implementation in `ScriptDiffEngine` (in
`src/Meridian.QuantScript/History/ScriptDiffEngine.cs`). No new NuGet package required — the
two scripts are typically < 500 lines and the LCS budget is trivially satisfied.

**Metrics diff:** Rendered as a two-column table with a ▲/▼ delta column.
`MetricsDiffViewModel.ComputeDeltas()` calculates absolute and relative changes for Sharpe, CAGR,
and max drawdown.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `IScriptHistoryStore` | Interface | `Task AppendAsync(string scriptName, ScriptRunRecord, CancellationToken)`, `Task<IReadOnlyList<ScriptRunRecord>> GetHistoryAsync(string scriptName, int maxEntries, CancellationToken)`, `Task<string?> GetContentAsync(string hash, CancellationToken)`, `Task WriteContentAsync(string hash, string text, CancellationToken)` | New |
| `ScriptHistoryWriter` | Sealed class : `IScriptHistoryStore` | `ScriptHistoryWriter(ICatalogPathResolver, IOptions<QuantScriptOptions>)` | JSONL append writer; prunes on write |
| `ScriptRunRecord` | Sealed record | `string RunId, string MeridianBinaryHash, string ScriptContentHash, IReadOnlyList<string> PrintOutput, ResultsSummary ResultsSummary, DateTimeOffset RunAtUtc` | Serialized to `.history/{name}.jsonl` |
| `ResultsSummary` | Sealed record | `double? Sharpe, double? Cagr, double? MaxDrawdown, bool HadBacktest` | JSON sub-object |
| `ScriptDiffEngine` | Static class | `static ScriptDiff Compute(string left, string right)` | 50-line LCS in `History/ScriptDiffEngine.cs` |
| `ScriptDiff` | Sealed record | `IReadOnlyList<DiffLine> Lines` | LCS result |
| `DiffLine` | Sealed record | `DiffKind Kind, string Text` | `DiffKind` enum: `Unchanged`, `Added`, `Removed` |
| `MetricsDiffViewModel` | Sealed class | `MetricsDiffViewModel(ResultsSummary left, ResultsSummary right)`, `void ComputeDeltas()` | Drives metrics diff table |
| `DiffViewModel` | Sealed class | `DiffViewModel(IScriptHistoryStore)` | Owns left/right script text and metrics |
| `HistoryEntryViewModel` | Sealed class | `HistoryEntryViewModel(ScriptRunRecord)` | Single history list row |

```csharp
// Constructor signatures
public ScriptHistoryWriter(ICatalogPathResolver pathResolver, IOptions<QuantScriptOptions> opts)
public DiffViewModel(IScriptHistoryStore historyStore)
public MetricsDiffViewModel(ResultsSummary left, ResultsSummary right)
public HistoryEntryViewModel(ScriptRunRecord record)
```

#### Implementation Notes

- `ScriptRunRecord` persistence uses `System.Text.Json`; no new serializer dependency.
- The script content itself is stored by hash in a companion `.content/` directory
  (`{ScriptLibraryRoot}/.content/{hash}.csx`) to avoid storing duplicate full scripts.
- `StorageCatalogService` per-symbol JSONL pattern (rolling file, deterministic path) applies
  here directly — the `.history/` writer reuses the same `JsonlAppendWriter` helper.
- Diff view is a WPF `Grid` with two `AvalonEdit` instances in read-only mode; differing lines
  are highlighted via `IBackgroundRenderer`.

**Concrete interface definitions:**

```csharp
// src/Meridian.QuantScript/History/IScriptHistoryStore.cs
public interface IScriptHistoryStore
{
    Task AppendAsync(string scriptName, ScriptRunRecord record, CancellationToken ct);
    Task<IReadOnlyList<ScriptRunRecord>> GetHistoryAsync(string scriptName, int maxEntries, CancellationToken ct);
    Task<string?> GetContentAsync(string contentHash, CancellationToken ct);
    Task WriteContentAsync(string contentHash, string scriptText, CancellationToken ct);
}

// src/Meridian.QuantScript/History/ScriptRunRecord.cs
public sealed record ScriptRunRecord(
    string RunId,
    string MeridianBinaryHash,
    string ScriptContentHash,
    IReadOnlyList<string> PrintOutput,
    ResultsSummary ResultsSummary,
    DateTimeOffset RunAtUtc);

public sealed record ResultsSummary(
    double? Sharpe,
    double? Cagr,
    double? MaxDrawdown,
    bool HadBacktest);
```

**DI registration (`QuantScriptFeatureRegistration.cs`):**

```csharp
services.AddSingleton<IScriptHistoryStore, ScriptHistoryWriter>();
services.AddSingleton<DiffViewModel>();
services.AddTransient<HistoryEntryViewModel>();
```

**Data flow:**

1. `ScriptRunner.ExecuteCellsAsync` completes all cells successfully.
2. `ScriptRunner` builds `ScriptRunRecord` from: `RunId = DateTimeOffset.UtcNow.ToString("O")`, `ScriptContentHash = SHA256(scriptText)`, `PrintOutput = consoleBuffer.ToArray()`, `ResultsSummary` from `BacktestProxy.LastResult`.
3. `IScriptHistoryStore.WriteContentAsync(hash, scriptText, ct)` writes `{ScriptLibraryRoot}/.content/{hash}.csx` only if the file does not already exist (idempotent by hash).
4. `IScriptHistoryStore.AppendAsync(scriptName, record, ct)` appends one JSON line to `{ScriptLibraryRoot}/.history/{scriptName}.jsonl`. After append, if the file has more than `QuantScriptOptions.MaxHistoryRunsPerScript` entries, `ScriptHistoryWriter` rewrites the file keeping only the most recent N records.
5. `HistoryTabViewModel` calls `IScriptHistoryStore.GetHistoryAsync(scriptName, 100, ct)` on tab activation to populate `ObservableCollection<HistoryEntryViewModel>`.
6. User clicks "Diff" on an entry → `DiffViewModel.LoadAsync(historicalRecord, currentScriptText, ct)`.
7. `DiffViewModel` calls `IScriptHistoryStore.GetContentAsync(record.ScriptContentHash, ct)` to retrieve the historical source text.
8. `ScriptDiffEngine.Compute(historicalText, currentText)` returns `ScriptDiff`; `DiffViewModel.DiffLines` is populated and the split view renders.
9. `MetricsDiffViewModel.ComputeDeltas()` computes Δ Sharpe, Δ CAGR, Δ MaxDrawdown (absolute and relative); both VMs bind to the two-column metrics table.

**Error handling and cancellation:**

- All `IScriptHistoryStore` operations accept a `CancellationToken`. On partial JSONL write (e.g., power loss), `ScriptHistoryWriter.GetHistoryAsync` validates each line on read; malformed lines are skipped and logged at `Warning`.
- If `GetContentAsync` returns `null` (content file missing — orphaned hash), `DiffViewModel` displays "Historical script content unavailable" in the left AvalonEdit panel instead of throwing.
- File system `IOException` on `AppendAsync` is caught, logged at `Error`, and the write is skipped — the script run itself is unaffected.

**Edge cases:**

1. **Two runs produce identical script content hashes:** `WriteContentAsync` checks file existence first; the second write is a no-op. The `.history/` JSONL correctly accumulates two entries pointing to the same hash.
2. **`MaxHistoryRunsPerScript = 0`:** Validated by `QuantScriptOptions` constructor; clamped to a minimum of 1 with a `Warning` log to prevent an infinite pruning loop.
3. **Diff of two scripts with only whitespace changes:** `ScriptDiffEngine.Compute` treats lines verbatim; whitespace-only diff lines are shown with `DiffKind.Modified`. The UI highlights them in a muted amber to visually distinguish from semantic changes.
4. **Script renamed between runs:** History is keyed by script name at write time. A renamed script accumulates a new history file. Old history is not migrated automatically; a `renamedFrom` field is reserved for a future v2 enhancement.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| QuantScript v1 `IScriptRunner` | Planned | `src/Meridian.QuantScript/Compilation/IScriptRunner.cs` |
| `StorageCatalogService` / `ICatalogPathResolver` | Implemented | `src/Meridian.Storage/Services/StorageCatalogService.cs` |

#### Tradeoffs

- History files accumulate indefinitely. Add a `QuantScriptOptions.MaxHistoryRunsPerScript`
  setting (default 100) so the JSONL is pruned by `ScriptHistoryWriter` on write.
- Storing script content by hash means a deleted script's history entries become orphaned.
  `HistoryCleanupService` can reconcile on startup, but this is a v2 concern.


#### Test Strategy

**Test class:** `ScriptHistoryWriterTests` — `tests/Meridian.QuantScript.Tests/History/ScriptHistoryWriterTests.cs`
**Pattern:** Arrange-Act-Assert; real file system via temp directory for store tests; no external mocks required.

1. `AppendAsync_WithSingleRecord_WritesValidJsonlLine` — appends one `ScriptRunRecord`, reads the `.jsonl` file, deserializes the single line, asserts `RunId` matches.
2. `AppendAsync_ExceedsMaxHistoryRuns_PrunesOldestEntries` — appends `MaxHistoryRunsPerScript + 3` records, reads file, asserts line count equals `MaxHistoryRunsPerScript`.
3. `GetHistoryAsync_WithMalformedLine_SkipsMalformedAndReturnsValid` — writes one valid JSON line and one corrupt line to the `.jsonl` file manually; asserts only one record returned.
4. `ScriptDiffEngine_Compute_WithIdenticalScripts_ReturnsAllUnchangedLines` — both inputs identical; asserts all `DiffLine.Kind == DiffKind.Unchanged`.
5. `ScriptDiffEngine_Compute_WithOneAddedLine_DetectsInsertedDiffLine` — right script has one extra line; asserts exactly one `DiffKind.Added` entry in result.

#### Audience

Hobbyist, Academic. Researchers iterating on a momentum strategy over days need to know whether
the latest change improved or degraded results — without having to maintain a manual spreadsheet.

#### Effort

**M** (~1–1.5 weeks). `ScriptRunRecord` persistence and the History tab list view are
straightforward. The diff view and LCS engine are the non-trivial parts.

---

### Feature #21 — Security Master Data Access in QuantScript

#### Overview

A `Securities` global is added to `QuantScriptGlobals`, delegating to
`ISecurityMasterQueryService`. Scripts can retrieve instrument metadata, filter by instrument type,
maturity, or other fundamental attributes, and combine the results with price data in a single
`.csx` file.

#### Design

**API surface in `QuantScriptGlobals`:**

```csharp
// Single symbol lookup
var meta = await Securities.GetAsync("AAPL");
Console.WriteLine($"{meta.Name} | {meta.InstrumentType} | {meta.Exchange}");

// Filtered query (in-memory snapshot, no DB round-trip)
var bonds = await Securities.QueryAsync(filter: s =>
    s.InstrumentType == InstrumentType.Bond &&
    s.EconomicDefinition.MaturityDate < DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)));
```

`Securities.GetAsync()` delegates to
`ISecurityMasterQueryService.GetByIdentifierAsync(identifier, AsOfUtc: DateTime.UtcNow)`.
The optional `asOf` parameter enables time-travel queries: `Securities.GetAsync("CUSIP:...",
asOf: new DateTime(2023, 01, 01))`.

`Securities.QueryAsync()` calls the Security Master projection cache's in-memory snapshot,
applying the supplied `Func<SecurityMasterEntry, bool>` predicate without a database round-trip.
This keeps latency low for exploratory scripts that might call `QueryAsync` in a loop across
many symbols.

**`SecurityEconomicDefinitionAdapter`:** Bridges F# domain types (from
`SecurityMaster.Domain.fs`) to the C# `SecurityEconomicDefinition` DTO consumed by scripts.
Located in `src/Meridian.QuantScript/Adapters/SecurityEconomicDefinitionAdapter.cs`.

**Null-safe contract:** Both methods return `null` (not throw) when the symbol is not found.
A console warning is emitted via `QuantScriptGlobals.Console.Warn(...)` so the script author
sees the issue without an unhandled exception derailing the run.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `SecurityGlobal` | Sealed class | `SecurityGlobal(ISecurityMasterQueryService, ILogger<SecurityGlobal>)`, `Task<SecurityMasterEntry?> GetAsync(string identifier, DateTime? asOf, CancellationToken)`, `Task<IReadOnlyList<SecurityMasterEntry>> QueryAsync(Func<SecurityMasterEntry,bool> filter, CancellationToken)` | Exposed as `Securities` global in `QuantScriptGlobals` |
| `SecurityEconomicDefinitionAdapter` | Static class | `static SecurityEconomicDefinition Adapt(FSharpDomainEntry entry)` | Bridges F# domain types → C# DTO |
| `SecurityEconomicDefinition` | Sealed record | `DateOnly? MaturityDate, string? Sector, string? Rating, string? CurrencyCode` | C# DTO consumed by scripts |

```csharp
// Constructor signatures
public SecurityGlobal(
    ISecurityMasterQueryService queryService,
    ILogger<SecurityGlobal> logger)
```

Note: `ISecurityMasterQueryService` is at `src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs` (not `src/Meridian.Application/SecurityMaster/`).

#### Implementation Notes

- `ISecurityMasterQueryService` already exists at
  `src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs` — no interface
  changes required.
- DI wiring: `QuantScriptModule` registers `SecurityGlobal` as a scoped service,
  receiving `ISecurityMasterQueryService` via constructor injection.
- The in-memory projection cache must be warmed before the first script run.
  `QuantScriptStartupInitializer` awaits `ISecurityMasterQueryService.WarmupAsync()` at
  application start.

**Concrete API surface (SecurityGlobal methods used in scripts):**

```csharp
// src/Meridian.QuantScript/Api/SecurityGlobal.cs
public sealed class SecurityGlobal
{
    public SecurityGlobal(
        ISecurityMasterQueryService queryService,
        ILogger<SecurityGlobal> logger) { ... }

    /// <returns>null if symbol not found — check before accessing fields.</returns>
    public Task<SecurityMasterEntry?> GetAsync(
        string identifier,
        DateTime? asOf = null,
        CancellationToken ct = default);

    public Task<IReadOnlyList<SecurityMasterEntry>> QueryAsync(
        Func<SecurityMasterEntry, bool> filter,
        CancellationToken ct = default);
}
```

**DI registration (`QuantScriptFeatureRegistration.cs`):**

```csharp
services.AddScoped<SecurityGlobal>();
// ISecurityMasterQueryService is already registered by SecurityMasterFeatureRegistration
```

**Data flow:**

1. Script calls `await Securities.GetAsync("AAPL")`.
2. `SecurityGlobal.GetAsync` calls `ISecurityMasterQueryService.GetByIdentifierAsync("AAPL", asOf: DateTime.UtcNow, ct)`.
3. If the service returns `null`, `SecurityGlobal` emits a console warning via `QuantScriptGlobals.Console.Warn("Symbol 'AAPL' not found in Security Master")` and returns `null` to the script.
4. For `QueryAsync`, `SecurityGlobal` calls `ISecurityMasterQueryService.GetAllAsync(ct)` to obtain the in-memory snapshot, then applies the `Func<SecurityMasterEntry, bool>` predicate client-side via LINQ.
5. Returned entries are `SecurityMasterEntry` records in C# form; `SecurityEconomicDefinitionAdapter.Adapt()` is called on demand if the script accesses `.EconomicDefinition`.

**Error handling and cancellation:**

- `ct` from the script's execution context is passed to `ISecurityMasterQueryService` calls. Cancellation during a long `QueryAsync` over a large security master stops iteration immediately.
- If the projection cache is not yet warmed at script run time, `GetAllAsync` returns an empty snapshot. `SecurityGlobal` logs `Warning: "Security Master cache not yet warmed — results may be incomplete"` and returns empty results rather than blocking.
- `SecurityEconomicDefinitionAdapter.Adapt` catches `InvalidCastException` for unrecognized F# DU cases and returns a `SecurityEconomicDefinition` with all nullable fields set to `null`.

**Edge cases:**

1. **Unknown identifier in `GetAsync`:** Returns `null` with a console warning. The `/// <returns>` XML doc comment on `GetAsync` states "null if symbol not found" so IntelliSense in the script editor shows the null-return contract.
2. **`QueryAsync` called before cache warm:** Returns empty list. `QuantScriptGlobals.Console.Warn("Security Master not yet warmed — results may be incomplete")` is emitted.
3. **`asOf` date earlier than Security Master data coverage:** `ISecurityMasterQueryService` returns whatever data it has for the date; `SecurityGlobal` surfaces the result without additional validation. Script authors must document their assumptions about point-in-time accuracy.
4. **Large security master with expensive predicate in `QueryAsync`:** Execution time can be high. If execution exceeds 2 seconds, `Warning` is logged recommending the user restrict the filter predicate. A 10-second hard timeout is enforced via `CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10))` linked to `ct`.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| QuantScript v1 `QuantScriptGlobals` | Planned | `src/Meridian.QuantScript/Api/QuantScriptGlobals.cs` |
| `ISecurityMasterQueryService` | Implemented | `src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs` |
| Security Master projection cache | Implemented | `src/Meridian.Ui.Shared/Services/SecurityMasterSecurityReferenceLookup.cs` |

#### Tradeoffs

- In-memory `QueryAsync` requires the full projection to be loaded. For large security masters
  (millions of instruments) this is a memory trade-off; gate behind
  `QuantScriptOptions.EnableSecurityMasterInMemoryQuery` (default `true` for typical deployments).
- The F#-to-C# adapter adds a translation layer. If `SecurityMaster.Domain.fs` evolves,
  `SecurityEconomicDefinitionAdapter` must be updated to stay in sync.


#### Test Strategy

**Test class:** `SecurityGlobalTests` — `tests/Meridian.QuantScript.Tests/Api/SecurityGlobalTests.cs`
**Pattern:** Arrange-Act-Assert with `Mock<ISecurityMasterQueryService>` (Moq).

1. `GetAsync_WithKnownSymbol_ReturnsEntry` — mocks `GetByIdentifierAsync` returning a populated entry; asserts `SecurityGlobal.GetAsync("AAPL")` returns that entry with matching `Name`.
2. `GetAsync_WithUnknownSymbol_ReturnsNullAndEmitsConsoleWarning` — mocks service returning `null`; asserts return is `null` and the console warning collector received one entry.
3. `QueryAsync_WithFilterMatchingSubset_ReturnsOnlyMatchingEntries` — mocks `GetAllAsync` returning 5 entries; applies a filter matching 2; asserts result count is 2.
4. `QueryAsync_WhenCacheEmpty_ReturnsEmptyListAndLogsWarning` — mocks `GetAllAsync` returning empty list; asserts result is empty and warning is logged.
5. `SecurityEconomicDefinitionAdapter_Adapt_WithValidFSharpEntry_MapsFieldsCorrectly` — constructs a test F# domain entry; calls `Adapt`; asserts DTO fields match expected values.

#### Audience

Academic, Institutional. Cross-asset factor scripts that combine fundamental attributes (maturity,
sector, rating) with price data are a primary use case for quant researchers.

#### Effort

**S** (3–5 days). The interfaces already exist; the work is the global registration, the
adapter, and the null-safe contract implementation.

---

### Feature #22 — Script Output Export (PDF / Parquet / Excel)

#### Overview

An `Export` global is added to `QuantScriptGlobals`, enabling scripts to export charts, data
series, and full run reports to PDF, Parquet, and Excel formats. The existing
`AnalysisExportService` in `src/Meridian.Storage/Export/` handles Parquet and Excel. A PDF
chart export path and an HTML report template are added.

#### Design

**API surface:**

```csharp
// Chart export
Export.Charts.Pdf("./reports/aapl-momentum.pdf");

// Data export
Export.Data(fills, "./reports/fills.parquet");
Export.Data(priceSeries, "./reports/spy.xlsx");

// Full HTML report (charts + metrics table, self-contained)
Export.Report("./reports/full-run.html");
```

**PDF chart export:** Uses ScottPlot's built-in `Plot.SaveFig()` PNG rendering piped through a
minimal `System.Drawing`-based PDF wrapper (`PdfChartWriter` in
`src/Meridian.QuantScript/Export/PdfChartWriter.cs`). No new NuGet package — ScottPlot is
already a dependency via the QuantScript blueprint; `System.Drawing.Common` is already
transitively included in the .NET 9 runtime.

**HTML report template:** A Razor-style template bundled as an embedded assembly resource
(`src/Meridian.QuantScript/Export/Templates/RunReport.html.template`). The template is
self-contained: chart PNGs are embedded as base64 `<img>` data URIs; the metrics table is
inlined HTML. `HtmlReportWriter` renders the template using `string.Replace` token substitution
(no Razor runtime dependency required).

**Parquet / Excel delegation:** `Export.Data(series, path)` calls
`AnalysisExportService.ExportAsync(series, ExportFormat.Parquet, path)` or
`ExportFormat.Excel`. The service already supports both formats.

**WPF toolbar:** The "Export Run" button in `QuantScriptViewModel` fires
`ExportRunCommand`, which delegates to `IExportGlobal.FlushAllAsync()` — collecting all pending
export requests and executing them in sequence.

**Configuration:**

```json
"QuantScript": {
  "ExportChartWidthPx": 1200,
  "ExportChartHeightPx": 800
}
```

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `IExportGlobal` | Interface | `IChartExport Charts`, `Task DataAsync<T>(IEnumerable<T>, string, ExportFormat, CancellationToken)`, `Task ReportAsync(string, CancellationToken)`, `Task FlushAllAsync(CancellationToken)` | Exposed as `Export` in `QuantScriptGlobals` |
| `ExportGlobal` | Sealed class : `IExportGlobal` | `ExportGlobal(AnalysisExportService, PdfChartWriter, HtmlReportWriter, IOptions<QuantScriptOptions>)` | Accumulates `List<IPendingExport>`; flushes on run completion |
| `IPendingExport` | Interface | `Task ExecuteAsync(CancellationToken)` | Strategy pattern for deferred exports |
| `PendingPdfExport` | Sealed class : `IPendingExport` | `PendingPdfExport(IReadOnlyList<ScottPlot.Plot>, string path, QuantScriptOptions)` | One chart per page |
| `PendingDataExport<T>` | Sealed class : `IPendingExport` | `PendingDataExport(IEnumerable<T>, string, ExportFormat, AnalysisExportService)` | Delegates to `AnalysisExportService` |
| `PendingReportExport` | Sealed class : `IPendingExport` | `PendingReportExport(HtmlReportWriter, string, RunReportModel)` | Template substitution |
| `PdfChartWriter` | Sealed class | `PdfChartWriter(IOptions<QuantScriptOptions>)`, `Task WriteAsync(IReadOnlyList<Plot>, string, CancellationToken)` | ScottPlot PNG → PDF via `System.Drawing.Common` |
| `HtmlReportWriter` | Sealed class | `HtmlReportWriter()`, `Task WriteAsync(RunReportModel, string, CancellationToken)` | Reads embedded `RunReport.html.template`; token substitution |

```csharp
// Constructor signatures
public ExportGlobal(
    AnalysisExportService exportService,
    PdfChartWriter pdfWriter,
    HtmlReportWriter htmlWriter,
    IOptions<QuantScriptOptions> opts)
public PdfChartWriter(IOptions<QuantScriptOptions> opts)
public HtmlReportWriter()   // reads embedded assembly resource
```

#### Implementation Notes

- `ExportGlobal` wraps a `List<IPendingExport>` that accumulates export requests during script
  execution, then flushes when the run completes or when `Export.Charts.Pdf(...)` is called with
  `flush: true`.
- `PdfChartWriter` uses a 1-chart-per-page layout for v1; multi-chart grid layout is v2.
- Paths passed to `Export.*` are resolved relative to `QuantScriptOptions.ExportOutputRoot`
  if they are relative paths.

**Concrete interface definitions:**

```csharp
// src/Meridian.QuantScript/Export/IExportGlobal.cs
public interface IExportGlobal
{
    IChartExport Charts { get; }
    Task DataAsync<T>(IEnumerable<T> data, string path,
        ExportFormat format = ExportFormat.Parquet,
        CancellationToken ct = default);
    Task ReportAsync(string path, CancellationToken ct = default);
    Task FlushAllAsync(CancellationToken ct);
}

public interface IChartExport
{
    void Pdf(string path, bool flush = false);
}
```

**DI registration (`QuantScriptFeatureRegistration.cs`):**

```csharp
services.AddScoped<IExportGlobal, ExportGlobal>();
services.AddSingleton<PdfChartWriter>();
services.AddSingleton<HtmlReportWriter>();
// AnalysisExportService is already registered by StorageFeatureRegistration
```

**Data flow:**

1. Script calls `Export.Data(fills, "./reports/fills.parquet")`.
2. `ExportGlobal.DataAsync` creates `PendingDataExport<FillEvent>(fills, resolvedPath, ExportFormat.Parquet, _exportService)` and appends it to `_pendingExports`.
3. When the script run completes, `QuantScriptViewModel` calls `IExportGlobal.FlushAllAsync(ct)`.
4. `FlushAllAsync` iterates `_pendingExports` sequentially, calling `IPendingExport.ExecuteAsync(ct)` for each.
5. `PendingDataExport.ExecuteAsync` calls `AnalysisExportService.ExportAsync(data, format, path, ct)`.
6. `PendingPdfExport.ExecuteAsync` calls `PdfChartWriter.WriteAsync(charts, path, ct)` — ScottPlot renders each plot to PNG bytes; the PDF wrapper concatenates pages using `System.Drawing.Common`.
7. `PendingReportExport.ExecuteAsync` calls `HtmlReportWriter.WriteAsync(model, path, ct)` — `RunReport.html.template` (embedded resource) is loaded; `{{SHARPE}}`, `{{CAGR}}`, `{{CHART_PNG_B64}}` tokens are substituted; the file is written.
8. Path resolution: relative paths are combined with `QuantScriptOptions.ExportOutputRoot`; the output directory is created if absent.

**Error handling and cancellation:**

- Each `IPendingExport.ExecuteAsync` call is individually wrapped in `try/catch(Exception)`. Failure of one export does not abort the others. All exceptions are collected and surfaced as a single `AggregateException` after all exports complete.
- On Linux: `PdfChartWriter` probes GDI availability at startup via `RuntimeInformation.IsOSPlatform(OSPlatform.Linux)` and a `new Bitmap(1,1)` probe. If GDI is unavailable, `WriteAsync` logs `Warning: "PDF export skipped: GDI+ unavailable on this platform"` and returns immediately without throwing.
- If `ct` is cancelled during `FlushAllAsync`, the currently-executing export completes (no mid-write cancellation), and remaining pending exports are skipped cleanly.

**Edge cases:**

1. **`Export.Data(series, path)` called with empty `IEnumerable`:** `PendingDataExport.ExecuteAsync` delegates to `AnalysisExportService` which creates a zero-row file — valid and consistent with the service's existing behavior.
2. **Path collision — two exports target the same file:** The last export wins (files are overwritten). A `Debug` log notes the collision, but no exception is thrown since overwrites are intentional in script workflows.
3. **`MaxReportEmbedCharts` exceeded:** `ExportGlobal` counts queued chart exports before report generation. If count exceeds `QuantScriptOptions.MaxReportEmbedCharts`, excess charts are omitted from the HTML report and a `Warning` log records the count.
4. **`FlushAllAsync` called with no pending exports:** Returns immediately — idempotent and expected for scripts that make no `Export.*` calls.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| QuantScript v1 `QuantScriptGlobals` | Planned | `src/Meridian.QuantScript/Api/QuantScriptGlobals.cs` |
| `AnalysisExportService` (Parquet/Excel) | Implemented | `src/Meridian.Storage/Export/AnalysisExportService.cs` |
| ScottPlot (chart rendering) | Planned via blueprint | `docs/plans/quant-script-environment-blueprint.md` |

#### Tradeoffs

- `System.Drawing.Common` on Linux requires the `libgdiplus` native library. If Meridian's
  Linux deployment omits GDI, PDF export will fail silently. Add a runtime capability check and
  log a warning if GDI is unavailable; skip PDF export gracefully.
- HTML report with embedded base64 PNGs can become large (5–10 MB) for scripts with many
  charts. Add a `QuantScriptOptions.MaxReportEmbedCharts` cap (default 20).


#### Test Strategy

**Test class:** `ExportGlobalTests` — `tests/Meridian.QuantScript.Tests/Export/ExportGlobalTests.cs`
**Pattern:** Arrange-Act-Assert with `Mock<AnalysisExportService>` and temp directories.

1. `DataAsync_WithParquetFormat_DelegatesToAnalysisExportServiceWithCorrectFormat` — verifies `AnalysisExportService.ExportAsync` is called with `ExportFormat.Parquet`.
2. `FlushAllAsync_WithMultipleExports_ExecutesAllInSequence` — queues 3 different `IPendingExport` instances; asserts all three `ExecuteAsync` calls complete after `FlushAllAsync`.
3. `FlushAllAsync_WhenOneExportFails_ContinuesRemainingExportsAndAggregatesExceptions` — first export throws `IOException`; asserts second and third still execute and aggregate exception contains one inner exception.
4. `PdfChartWriter_WhenGdiUnavailable_LogsWarningAndReturnsWithoutThrowing` — simulates GDI unavailability; asserts no exception propagated and `Warning` was logged.
5. `HtmlReportWriter_WriteAsync_ProducesHtmlFileWithSubstitutedMetricTokens` — writes to temp path; reads HTML; asserts `{{SHARPE}}` token replaced with the expected value.

#### Audience

Academic, Institutional. Researchers who need to share results with colleagues or include
outputs in reports are the primary beneficiaries.

#### Effort

**M** (~1–1.5 weeks). `AnalysisExportService` integration is trivial; the PDF wrapper and HTML
report template are the bulk of the work.

---

### Feature #23 — Script Expression REPL

#### Overview

A floating REPL panel toggled by `Ctrl+`` keyboard shortcut. A single-line input bar at the
bottom of the QuantScript page executes expressions against the current session's `ScriptState`
(or a fresh state if no session is active). Results print inline. The REPL accelerates
interactive exploration without the overhead of a full cell execution.

#### Design

**Layout:** A 36 px bar docked at the bottom of the editor pane. Visible only when toggled on.
Consists of: a `TextBox` for input, a `Run` button (or `Enter`), a `Clear` button, and an
output `TextBlock` showing the last result.

**Execution:** `ReplViewModel.ExecuteAsync(string expression)` calls
`IScriptRunner.ContinueWithAsync(expression, currentState)` using Roslyn's
`ScriptState.ContinueWithAsync`. Results are appended to a `CircularBuffer<ReplEntry>` (history
capacity: 200 entries).

**`CircularBuffer<T>`:** Already implemented at
`src/Meridian.Ui.Services/Collections/CircularBuffer.cs`. Wire `ReplViewModel` to use the
existing type — no new collection implementation required.

**Multi-line input:** `Shift+Enter` inserts a newline into the `TextBox`. The accumulated
multi-line expression is submitted on plain `Enter`.

**History navigation:** `Up` / `Down` arrow keys cycle through the `CircularBuffer` history,
setting `ReplViewModel.CurrentInput` to the selected entry's source text.

**Fresh state mode:** If `QuantScriptViewModel.CurrentScriptState` is `null` (no cells have
been run), the REPL creates a new `ScriptState` with `QuantScriptGlobals` injected, so the user
can experiment without running the script first.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `ReplViewModel` | Sealed class | `ReplViewModel(IScriptRunner, IReplHistory, IOptions<QuantScriptOptions>)`, `ICommand ExecuteCommand`, `ICommand ClearCommand`, `string CurrentInput { get; set; }`, `bool IsVisible { get; set; }` | Nested on `QuantScriptViewModel.Repl` |
| `ReplEntry` | Sealed record | `string Source, string Result, bool IsError, DateTimeOffset Timestamp` | One output line in REPL history |
| `IReplHistory` | Interface | `void Add(ReplEntry)`, `bool TryGetAt(int offsetFromNewest, out ReplEntry?)`, `void Clear()`, `IReadOnlyList<ReplEntry> ToList()` | Thin wrapper over `CircularBuffer<ReplEntry>` |
| `ReplHistory` | Sealed class : `IReplHistory` | `ReplHistory(int capacity = 200)` | Wraps existing `CircularBuffer<ReplEntry>` from `Meridian.Ui.Services` |

```csharp
// Constructor signatures
public ReplViewModel(
    IScriptRunner scriptRunner,
    IReplHistory history,
    IOptions<QuantScriptOptions> opts)
public ReplHistory(int capacity = 200)  // wraps CircularBuffer<ReplEntry>
```

#### Implementation Notes

- `ReplViewModel` is a nested ViewModel on `QuantScriptViewModel`:
  `public ReplViewModel Repl { get; }`.
- The toggle shortcut (`Ctrl+``) is registered in the QuantScript page's
  `KeyBindings` collection in XAML.
- `ReplEntry` record: `{ string Source, string Result, bool IsError, DateTimeOffset Timestamp }`.
- Exception output is caught and displayed as an error-styled entry (red foreground in the
  output list) without crashing the session.

**Concrete interface definitions:**

```csharp
// src/Meridian.QuantScript/Repl/IReplHistory.cs
public interface IReplHistory
{
    int Count { get; }
    void Add(ReplEntry entry);
    bool TryGetAt(int offsetFromNewest, out ReplEntry? entry);
    void Clear();
    IReadOnlyList<ReplEntry> ToList();
}

// src/Meridian.QuantScript/Repl/ReplEntry.cs
public sealed record ReplEntry(
    string Source,
    string Result,
    bool IsError,
    DateTimeOffset Timestamp);
```

**DI registration (`QuantScriptFeatureRegistration.cs`):**

```csharp
services.AddSingleton<IReplHistory>(_ => new ReplHistory(capacity: 200));
services.AddSingleton<ReplViewModel>();
```

**Data flow:**

1. User types an expression in the REPL `TextBox` and presses `Enter` (or clicks "Run").
2. `ReplViewModel.ExecuteCommand` fires; calls `ExecuteAsync(CurrentInput)`.
3. `ExecuteAsync` checks `QuantScriptViewModel.CurrentScriptState`: if `null`, creates a fresh `ScriptState` via `IScriptRunner.CreateInitialStateAsync(QuantScriptGlobals, ct)`.
4. A 5-second timeout `CancellationTokenSource` is created: `using var tcs = new CancellationTokenSource(TimeSpan.FromSeconds(5));` linked to the session `CancellationToken`.
5. Calls `await IScriptRunner.ContinueWithAsync(expression, scriptState, tcs.Token)`.
6. On success: result is `scriptState.ReturnValue?.ToString() ?? "(void)"`. A `ReplEntry` is created with `IsError = false`.
7. On `OperationCanceledException` (timeout): entry created with `Result = "Timed out (5 s)"`, `IsError = true`.
8. On any other `Exception`: entry created with `Result = ex.Message`, `IsError = true`.
9. `IReplHistory.Add(entry)` appends to the circular buffer.
10. `ReplViewModel.OutputEntries` (`ObservableCollection<ReplEntry>`) is updated on the UI dispatcher thread; `CurrentInput` is cleared.

**Error handling and cancellation:**

- The 5-second `CancellationTokenSource` is the primary guard against infinite loops in REPL expressions. When the timeout fires, `OperationCanceledException` is caught and surfaced as a timeout entry — the underlying Roslyn task may continue in the background but its result is discarded.
- `ContinueWithAsync` compilation errors (e.g., `CompilationErrorException`) are caught at the outermost level in `ExecuteAsync` and shown as error entries with red foreground styling via `IsError` → WPF `DataTrigger`.

**Edge cases:**

1. **Expression with side effects on shared `ScriptState`:** The REPL mutates the shared state (e.g., redefines a variable used by a downstream cell). A tooltip on the REPL input bar reads: "REPL expressions modify the current session state." State forking is deferred to v2.
2. **`Up` / `Down` navigation on empty history:** `IReplHistory.TryGetAt` returns `false`; `CurrentInput` is unchanged. The navigation key is silently consumed.
3. **Multi-line expression with `Shift+Enter`:** `TextBox.AcceptsReturn` is `True` while `Shift` is held. The `Enter` key binding fires `ExecuteCommand` only when `ModifierKeys.Shift` is not active (checked in the `KeyDown` handler).
4. **REPL panel toggled off while an expression is executing:** `ReplViewModel.IsVisible = false` hides the bar but does not cancel the in-flight execution. The 5-second timeout still applies; the result entry is added to history and displayed when the bar is re-opened.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| `IScriptRunner.ContinueWithAsync` | Planned | `src/Meridian.QuantScript/Compilation/IScriptRunner.cs` |
| `CircularBuffer<T>` | Implemented | `src/Meridian.Ui.Services/Collections/CircularBuffer.cs` |

#### Tradeoffs

- The REPL shares `ScriptState` with the cell runner. A badly-formed REPL expression can
  corrupt state (e.g., redefining a variable used by a downstream cell). Add a
  `// REPL expressions run in a forked state` warning note in the UI tooltip.
- `ContinueWithAsync` is not cancellation-safe once started. Set a 5-second timeout on
  `ReplViewModel.ExecuteAsync` and surface a "Timed out" entry if exceeded.


#### Test Strategy

**Test class:** `ReplViewModelTests` — `tests/Meridian.QuantScript.Tests/Repl/ReplViewModelTests.cs`
**Pattern:** Arrange-Act-Assert with `Mock<IScriptRunner>` (Moq).

1. `ExecuteAsync_WithSimpleExpression_AddsSuccessEntryToHistory` — mocks `ContinueWithAsync` returning a state with `ReturnValue = 42`; asserts `IReplHistory.Count == 1` and `Entries[0].IsError == false`.
2. `ExecuteAsync_WhenTimeoutExceeded_AddsTimeoutErrorEntry` — mocks `ContinueWithAsync` as `Task.Delay(Timeout.Infinite, ct)` (respects cancellation); asserts entry contains "Timed out" and `IsError = true` after 5 s.
3. `ExecuteAsync_WhenExceptionThrown_AddsErrorEntryWithMessage` — mocks throwing `InvalidOperationException("bad input")`; asserts entry `IsError = true` and `Result` contains "bad input".
4. `ReplHistory_TryGetAt_WithValidOffset_ReturnsCorrectEntry` — adds 3 entries; calls `TryGetAt(0)` (newest); asserts it returns the third entry.
5. `ReplHistory_Add_ExceedingCapacity_OverwritesOldestEntry` — `capacity = 3`; adds 4 entries; asserts `Count == 3` and oldest is gone.

#### Audience

Hobbyist. Quant hobbyists who want to inspect a variable mid-session without re-running cells
are the primary users. The REPL lowers the barrier to exploratory debugging significantly.

#### Effort

**S** (3–4 days). The REPL panel is a thin UI wrapper over `ContinueWithAsync` + the already-
implemented `CircularBuffer`. Total new code is small.

---

## Track H — L3 Inference: Additional Ideas (Round 2)

### Feature #24 — Venue-Specific L3 Calibration Profiles

#### Overview

`InferenceModelConfig` gains a `venueProfile` field. Each venue profile is a
`VenueCalibrationPriors` record loaded from `config/venue-calibration-priors.json` containing
venue-specific priors for arrival rate, decay alpha, and queue depth scaling. Priors serve as
both starting point and regularization anchors for MLE fit, tightening confidence intervals on
short calibration windows.

#### Design

**Config extension:**

```jsonc
// config/inference-model.json
{
  "venueProfile": "Auto",  // "NASDAQ" | "NYSE" | "CBOE" | "Auto"
  ...
}
```

**`config/venue-calibration-priors.json` format:**

```jsonc
{
  "NYSE": {
    "arrivalRatePrior": { "mean": 0.82, "stdDev": 0.12 },
    "decayAlphaPrior":  { "mean": 0.31, "stdDev": 0.05 },
    "queueDepthScalePrior": { "mean": 1.0, "stdDev": 0.15 }
  },
  "NASDAQ": { ... },
  "CBOE":   { ... }
}
```

**Auto-detection:** `"Auto"` resolves the venue from the symbol's canonical exchange code via
`VenueMicMapper` (`src/Meridian.Application/Canonicalization/VenueMicMapper.cs`). The resolved
venue name is logged at `Information` level: `"NYSE profile applied (AutoDetected for AAPL)"`.

**Calibration report card:** Gains a `venuePriorDivergence` field (KL divergence between the
posterior and the venue prior). If divergence exceeds a configurable threshold
(`CalibrationOptions.PriorDivergenceWarningThreshold`, default `0.4`), the report card letter
grade is downgraded one level (e.g., A → B) and a `CalibrationWarning.OverfitOnShortWindow`
flag is set.

**Shared prerequisite with #29:** Both features require `VenueMicMapper` to expose an
`IanaTimezone` field populated from `config/venue-mapping.json`. Adding this field unblocks
both ideas in a single config change.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `VenueCalibrationPriors` | Sealed record | `string VenueName, GaussianPrior ArrivalRatePrior, GaussianPrior DecayAlphaPrior, GaussianPrior QueueDepthScalePrior` | `src/Meridian.L3.Inference/Calibration/VenueCalibrationPriors.cs` |
| `GaussianPrior` | Sealed record | `double Mean, double StdDev` | Reusable prior type; validated `StdDev > 0` at load time |
| `IVenueProfileResolver` | Interface | `Task<VenueCalibrationPriors?> ResolveAsync(string exchangeMic, CancellationToken)`, `VenueCalibrationPriors GetGenericFallback()` | |
| `VenueProfileResolver` | Sealed class : `IVenueProfileResolver` | `VenueProfileResolver(VenueMicMapper, IReadOnlyDictionary<string, VenueCalibrationPriors>, ILogger<VenueProfileResolver>)` | Resolves `"Auto"` at calibration time |
| `VenueProfileLoader` | Static class | `static IReadOnlyDictionary<string, VenueCalibrationPriors> LoadFromFile(string path)` | Parses `config/venue-calibration-priors.json` |

```csharp
// Constructor signatures
public VenueProfileResolver(
    VenueMicMapper micMapper,
    IReadOnlyDictionary<string, VenueCalibrationPriors> profiles,
    ILogger<VenueProfileResolver> logger)
```

Note on `VenueMicMapper`: it maps `(string Provider, string RawVenue) → string? MIC`. For
`"Auto"` profile resolution, `VenueProfileResolver` uses the canonical MIC to look up the
venue name key in the `profiles` dictionary. The `IanaTimezone` field required by Features
#24 and #29 must be added to `config/venue-mapping.json` and exposed on `VenueMicMapper`.

#### Implementation Notes

- `VenueCalibrationPriors` is a record in `src/Meridian.L3.Inference/Calibration/VenueCalibrationPriors.cs`.
- `VenueProfileResolver` resolves `"Auto"` at calibration time, not at model load time, to
  ensure it uses the symbol's runtime-resolved exchange code.
- Priors are injected into the MLE optimizer as regularization terms (Gaussian prior on each
  parameter), not as hard constraints — the posterior can deviate from the prior given
  sufficient data.

**Concrete interface definitions:**

```csharp
// src/Meridian.L3.Inference/Calibration/IVenueProfileResolver.cs
public interface IVenueProfileResolver
{
    Task<VenueCalibrationPriors?> ResolveAsync(string exchangeMic, CancellationToken ct);
    VenueCalibrationPriors GetGenericFallback();
}

// src/Meridian.L3.Inference/Calibration/VenueCalibrationPriors.cs
public sealed record VenueCalibrationPriors(
    string VenueName,
    GaussianPrior ArrivalRatePrior,
    GaussianPrior DecayAlphaPrior,
    GaussianPrior QueueDepthScalePrior);

public sealed record GaussianPrior(double Mean, double StdDev);
```

**DI registration (`L3InferenceFeatureRegistration.cs`):**

```csharp
services.AddSingleton<IVenueProfileResolver>(sp =>
{
    var mapper = sp.GetRequiredService<VenueMicMapper>();
    var priors = VenueProfileLoader.LoadFromFile(
        Path.Combine(
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            "config/venue-calibration-priors.json"));
    return new VenueProfileResolver(mapper, priors,
        sp.GetRequiredService<ILogger<VenueProfileResolver>>());
});
```

**Data flow:**

1. Calibration job starts for symbol `AAPL`; `CalibrationOrchestrator` reads `InferenceModelConfig.VenueProfile`.
2. If `VenueProfile == "Auto"`: `IVenueProfileResolver.ResolveAsync(symbol.ExchangeMic, ct)` is called.
3. `VenueProfileResolver` uses the canonical MIC to look up the venue name (e.g., `"XNAS"` → `"NASDAQ"`) in the profiles dictionary.
4. If no match: `GetGenericFallback()` returns a flat prior (`Mean = 0, StdDev = 1.0`) and logs `Warning: "'XNAS' not in venue-calibration-priors.json; using Generic fallback"`.
5. `VenueCalibrationPriors` is injected into the MLE optimizer as Gaussian regularization terms: `logLikelihood -= 0.5 * ((param - prior.Mean) / prior.StdDev)^2` for each parameter.
6. After calibration, `CalibrationReportCard` computes `venuePriorDivergence` as KL divergence between posterior and prior. If `> CalibrationOptions.PriorDivergenceWarningThreshold` (default `0.4`), the letter grade is downgraded one notch.

**Error handling and cancellation:**

- `ct` is passed to `ResolveAsync`; resolution is a fast in-memory dictionary lookup — effectively instantaneous relative to calibration time.
- If `venue-calibration-priors.json` is malformed at startup, `VenueProfileLoader.LoadFromFile` throws `JsonException`. This is a fatal configuration error; the host startup fails with a clear error message.
- KL divergence can return `NaN` if the posterior has zero variance; guarded with `if (double.IsNaN(divergence)) divergence = 0.0` and a `Debug` log.

**Edge cases:**

1. **Dual-listed symbol (multiple venues simultaneously):** `VenueProfileResolver` resolves using the symbol's `PrimaryExchangeMic`. A `Debug` log notes which venue was selected.
2. **`VenueProfile` explicitly set to a venue name absent from the config file:** `ResolveAsync` returns `null`; `GetGenericFallback()` is used with a `Warning` log.
3. **Prior `StdDev = 0` in JSON config (degenerate prior):** `VenueProfileLoader` validates all `StdDev > 0`; throws `InvalidOperationException` at load time naming the offending venue.
4. **Calibration data window shorter than 30 minutes:** `CalibrationWarning.ShortWindowPriorUnreliable` is added to the report card alongside the reduced-confidence `venuePriorDivergence`.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| L3 Phase 1 calibration store | Planned | `docs/plans/l3-inference-implementation-plan.md` |
| `VenueMicMapper` | Implemented | `src/Meridian.Application/Canonicalization/VenueMicMapper.cs` |
| `config/venue-mapping.json` (add `IanaTimezone`) | Partial | `config/` root |

#### Tradeoffs

- Venue priors are hand-tuned constants in a JSON config file. They become stale if market
  microstructure changes (e.g., post-regulation regime shift). Add a `--update-venue-priors`
  CLI command as a future self-calibration path.
- `"Auto"` resolution can fail if `VenueMicMapper` does not recognize the exchange code. Fall
  back to a `"Generic"` prior and log a warning rather than failing the calibration run.


#### Test Strategy

**Test class:** `VenueProfileResolverTests` — `tests/Meridian.L3.Inference.Tests/Calibration/VenueProfileResolverTests.cs`
**Pattern:** Arrange-Act-Assert with a mock `VenueMicMapper`.

1. `ResolveAsync_WithKnownExchangeMic_ReturnsMatchingVenuePriors` — maps `"XNYS"` → `"NYSE"` priors; asserts `ArrivalRatePrior.Mean` matches the configured value.
2. `ResolveAsync_WithUnknownExchangeMic_ReturnsNullAndLogsWarning` — MIC not in config; asserts return `null` and warning logged.
3. `GetGenericFallback_ReturnsNonZeroStdDevForAllPriors` — asserts `ArrivalRatePrior.StdDev > 0`, `DecayAlphaPrior.StdDev > 0`, `QueueDepthScalePrior.StdDev > 0`.
4. `VenueProfileLoader_LoadFromFile_WithMalformedJson_ThrowsJsonException`
5. `VenueProfileLoader_LoadFromFile_WithZeroStdDev_ThrowsInvalidOperationException` — JSON has `"stdDev": 0`; asserts `InvalidOperationException` at load time.

#### Audience

Academic, Institutional. Researchers calibrating across multiple venues want tighter confidence
intervals without needing to supply more historical data.

#### Effort

**M** (~1 week). Config changes are small; the MLE regularization hook is the non-trivial part.

---

### Feature #25 — TCA Report: Execution Cost Attribution

#### Overview

After every simulation run, a `PostSimulationTcaReporter` class emits `tca-report.json` and
`tca-report.html` alongside the existing `summary.json`. The TCA report decomposes execution
cost into spread cost, timing cost, and market impact; identifies outlier orders; and benchmarks
against VWAP, arrival mid, and close. A `fills.TcaReport()` extension method in QuantScript
surfaces the result as a plottable `TcaResult`.

This is the highest-leverage idea in this batch: it requires no new data sources, no new UI
frameworks, and directly answers "how good was my execution?" — the central question for every
execution researcher.

#### Design

**`tca-report.json` schema:**

```jsonc
{
  "benchmark": "ArrivalMid",
  "totalOrders": 47,
  "costSummary": {
    "avgSlippageBps": 3.2,
    "avgSpreadCostBps": 1.1,
    "avgTimingCostBps": 2.1,
    "avgMarketImpactBps": 0.0
  },
  "outliers": [
    {
      "orderId": "ord-14",
      "slippageBps": 18.4,
      "cause": "HighToxicityFill",
      "session": "Close"
    }
  ],
  "benchmarkComparison": {
    "vsVwap": "+1.2 bps better",
    "vsArrivalMid": "-3.2 bps worse",
    "vsClose": "+5.1 bps better"
  }
}
```

**`tca-report.html`:** A professional-grade, printable one-pager rendered by
`TcaHtmlReportWriter`:

- **Cost waterfall bar chart** (spread cost → timing cost → market impact → total): ScottPlot
  bar series, embedded as base64 PNG.
- **Scatter plot** of order size vs. slippage: reveals size-dependent impact.
- **Outlier order table**: orderId, size, slippage, cause, session, inline highlighted.

**`PostSimulationTcaReporter`:** Located in
`src/Meridian.Backtesting/Metrics/PostSimulationTcaReporter.cs`. Post-processes
`fill-tape.jsonl` and `summary.json` from the simulation output directory. Invoked automatically
by the simulation engine after each run.

**QuantScript integration:**

```csharp
var result = await Test.RunAsync(strategy);
var tca = result.Fills.TcaReport();
Plot.Waterfall("Execution Cost Breakdown", tca.CostWaterfall);
Print(tca.Summary);
```

`TcaResult` exposes `CostWaterfall` (array of `(string label, double bps)`) for ScottPlot
rendering, and a `Summary` string for `Print()`.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `TcaExecutionCostSummary` | Sealed record | `double? AvgSlippageBps, double? AvgSpreadCostBps, double? AvgTimingCostBps, double? AvgMarketImpactBps, int BboCoverageCount, int TotalFills, double BboCoveragePct` | New; added alongside existing `TcaCostSummary` in `TcaReportModels.cs` |
| `TcaReport` (extended) | Sealed record | All existing fields + `TcaExecutionCostSummary? ExecutionCostSummary = null` | Non-breaking optional addition |
| `TcaBboSnapshot` | Sealed record | `Guid FillId, decimal BidAtFill, decimal AskAtFill, decimal ArrivalMid, DateTimeOffset Timestamp` | BBO context captured at fill time from L3 engine |
| `TcaHtmlReportWriter` | Sealed class | `TcaHtmlReportWriter()`, `Task WriteAsync(TcaReport report, string path, CancellationToken ct)` | Embeds ScottPlot cost-waterfall chart as base64 PNG |
| `TcaResult` | Sealed class | `IReadOnlyList<(string Label, double Bps)> CostWaterfall`, `string Summary` | QuantScript extension surface; returned by `fills.TcaReport()` |

```csharp
// TcaReport record with new optional field (non-breaking):
public sealed record TcaReport(
    DateTimeOffset GeneratedAtUtc,
    string? StrategyAssemblyPath,
    TcaCostSummary CostSummary,
    IReadOnlyList<SymbolTcaSummary> SymbolSummaries,
    IReadOnlyList<TcaFillOutlier> Outliers,
    TcaExecutionCostSummary? ExecutionCostSummary = null);   // NEW

// PostSimulationTcaReporter.Generate already exists (commission-only).
// Feature #25 extends it: BBO data present → compute ExecutionCostSummary.
// BBO data absent → ExecutionCostSummary = null (graceful degradation).
```

Note: `BacktestResult.TcaReport` already exists — no schema change needed there.

#### Implementation Notes

- Spread cost is computed as `(ask - bid) / 2` at the time of fill using the BBO snapshot from
  `fill-tape.jsonl`.
- Timing cost is arrival mid minus fill price.
- Market impact is estimated as the VWAP shortfall beyond timing cost (requires VWAP from
  `summary.json` VWAP field).
- Outlier threshold: `|slippageBps| > 3 × median(|slippageBps|)` across all orders.
  The existing `OutlierThresholdMultiplier = 3.0` constant in `PostSimulationTcaReporter` is reused.
- `BacktestMetricsEngine` already computes VWAP — reuse its output rather than recomputing.

**Concrete record definitions (additions to `TcaReportModels.cs`):**

```csharp
// src/Meridian.Backtesting.Sdk/TcaReportModels.cs — new record
public sealed record TcaExecutionCostSummary(
    double? AvgSlippageBps,
    double? AvgSpreadCostBps,
    double? AvgTimingCostBps,
    double? AvgMarketImpactBps,
    int BboCoverageCount,
    int TotalFills,
    double BboCoveragePct);

// TcaReport extended (non-breaking — new optional field with default null):
public sealed record TcaReport(
    DateTimeOffset GeneratedAtUtc,
    string? StrategyAssemblyPath,
    TcaCostSummary CostSummary,
    IReadOnlyList<SymbolTcaSummary> SymbolSummaries,
    IReadOnlyList<TcaFillOutlier> Outliers,
    TcaExecutionCostSummary? ExecutionCostSummary = null);

// BBO snapshot record stored in fill-tape.jsonl per fill:
public sealed record TcaBboSnapshot(
    Guid FillId,
    decimal BidAtFill,
    decimal AskAtFill,
    decimal ArrivalMid,
    DateTimeOffset Timestamp);
```

**DI registration:** `PostSimulationTcaReporter` is a static class; `TcaHtmlReportWriter` is registered separately:

```csharp
services.AddSingleton<TcaHtmlReportWriter>();
// PostSimulationTcaReporter.Generate is a static method called by BacktestResultSerializer
```

**Data flow:**

1. `BacktestResultSerializer` writes `fill-tape.jsonl` including `BboSnapshot` objects when L3 is enabled.
2. Simulation engine calls `PostSimulationTcaReporter.Generate(request, fills)`.
3. `Generate` runs the existing commission cost path, producing `TcaCostSummary` unchanged.
4. If any fill has non-null `BboSnapshot.BidAtFill`/`AskAtFill`: compute per-fill execution costs:
   - **Spread cost (bps):** `(ask - bid) / midPrice / 2 × 10_000`
   - **Timing cost (bps):** `(arrivalMid - fillPrice) / arrivalMid × 10_000 × sideSign` (positive = adverse)
   - **Market impact (bps):** VWAP shortfall minus timing cost, using VWAP from `summary.json`
5. Average across all fills with BBO data → `TcaExecutionCostSummary` with `BboCoveragePct = bboCount / totalFills`.
6. If `BboCoveragePct < 0.80`, the JSON output includes `"dataQualityWarning": "BBO coverage below 80%"`.
7. `TcaHtmlReportWriter.WriteAsync(report, path, ct)` renders the HTML report with an embedded cost waterfall bar chart (ScottPlot `BarPlot`) encoded as base64 PNG.
8. `fills.TcaReport()` extension method in `src/Meridian.QuantScript/Extensions/FillExtensions.cs` calls `PostSimulationTcaReporter.Generate` and wraps the result in a `TcaResult` for QuantScript use.

**Error handling and cancellation:**

- If `fill-tape.jsonl` is absent or empty, `Generate` returns a `TcaReport` with `ExecutionCostSummary = null` and logs `Warning: "fill-tape.jsonl not found; TCA execution cost components skipped"`.
- VWAP shortfall computation guards against division by zero: `if (totalNotional == 0m) return null` for that symbol.
- `TcaHtmlReportWriter.WriteAsync` propagates `ct`; if cancelled before file write completes, the partial file is deleted to avoid corrupt reports on disk.

**Edge cases:**

1. **Zero fills in `fill-tape.jsonl`:** `Generate` returns `TcaReport` with `TcaCostSummary` of all zeros and `ExecutionCostSummary = null`. No division-by-zero exceptions.
2. **All fills on the same side (all buys or all sells):** `sideSign` is applied uniformly; single-sided fill tapes are valid inputs.
3. **BBO snapshot has `bid == ask` (crossed market — data quality issue):** Spread cost for that fill is `0.0 bps`; a `Debug` log notes the crossed BBO. The fill is still included in the BBO coverage count.
4. **`BacktestResult.TcaReport` already exists:** No schema change is needed on `BacktestResult`. Only the `TcaReport` record gains the optional `ExecutionCostSummary` field, which is backward-compatible.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| L3 fill tape (`fill-tape.jsonl`) | Planned | `docs/plans/l3-inference-implementation-plan.md` |
| `BacktestMetricsEngine` | Planned | `src/Meridian.Backtesting/Metrics/` |
| ScottPlot (chart rendering) | Planned via blueprint | `docs/plans/quant-script-environment-blueprint.md` |

#### Tradeoffs

- TCA accuracy depends on BBO snapshot quality in `fill-tape.jsonl`. If quotes are sparse,
  spread cost estimates will be noisy. Surface a `dataQualityWarning` field in `tca-report.json`
  when BBO coverage < 80% of fills.
- The HTML report is a static one-pager — no interactive filtering. QuantScript's `TcaResult`
  covers the interactive exploration use case.


#### Test Strategy

**Test class:** `PostSimulationTcaReporterTests` — `tests/Meridian.Backtesting.Tests/Metrics/PostSimulationTcaReporterTests.cs`
**Pattern:** Arrange-Act-Assert; `PostSimulationTcaReporter.Generate` is a static method — no mocks needed.

1. `Generate_WithFillsAndBboData_PopulatesExecutionCostSummary` — provides 10 `FillEvent` records each with a `TcaBboSnapshot`; asserts `ExecutionCostSummary` is not null and `AvgSpreadCostBps > 0`.
2. `Generate_WithNoBboData_ReturnsNullExecutionCostSummary` — fills have no `BboSnapshot`; asserts `ExecutionCostSummary == null`.
3. `Generate_WithBboCoverageBelow80Pct_SetsBboCoveragePctCorrectly` — 7 of 10 fills have BBO; asserts `BboCoveragePct ≈ 0.7`.
4. `Generate_WithZeroFills_ReturnsZeroCostSummaryWithoutException` — empty fills list; asserts returns without exception and `TotalFills == 0`.
5. `Generate_WithOneOutlierFill_IncludesItInOutliersList` — one fill has slippage >> `3 × median`; asserts it appears in `Outliers` with correct `FillId`.

#### Audience

Academic, Institutional. Every execution researcher and portfolio analyst benefits. This is the
highest-ROI idea in Track H.

#### Effort

**M** (~1–1.5 weeks). No new data sources or UI frameworks. The HTML template and the cost
decomposition math are the bulk of the work.

---

### Feature #26 — Multi-Day Queue State Continuity

#### Overview

The simulation engine gains an optional carry-state mode. `InferenceEngine` persists
`DayEndQueueState` to `{output}/day-state.json` at each session close. On the following day's
open, this state is loaded as the initial condition with a configurable `NightlyDecayFactor`
applied, modeling the partial information decay overnight.

#### Design

**`day-state.json` schema:**

```jsonc
{
  "symbol": "AAPL",
  "date": "2026-01-15",
  "estimatedQueuePositionAtClose": 42,
  "bookImbalanceAtClose": 0.37,
  "bidAskSpreadAtClose": 0.03,
  "decayFactorApplied": 0.5
}
```

**CLI flags:**

```bash
dotnet run --project src/Meridian -- \
  --simulate-execution \
  --symbols AAPL \
  --sim-from 2026-01-01 \
  --sim-to 2026-01-31 \
  --sim-carry-state \
  --sim-nightly-decay 0.5
```

**`sim-manifest.json` additions:**

```jsonc
{
  "carryStateEnabled": true,
  "nightlyDecayFactor": 0.5
}
```

**`NightlyDecayFactor`:** Applied as a scalar multiplier to all state values:
`initialState = dayEndState × decayFactor`. Default `0.5`. Valid range `[0.0, 1.0]`.
`0.0` = forget all carry state (equivalent to no carry). `1.0` = full persistence.

**Parallelism constraint:** Multi-day carry-state requires days to be processed sequentially
for a given symbol. When `--sim-carry-state` is active, the simulation runner switches from
a parallel day-batch to a sequential per-symbol, sequential-day schedule. A warning is logged:
`"Carry-state mode: day-level parallelism disabled for {symbol}"`.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `DayEndQueueState` | Sealed record | `string Symbol, DateOnly Date, int EstimatedQueuePositionAtClose, double BookImbalanceAtClose, decimal BidAskSpreadAtClose, double DecayFactorApplied` | `src/Meridian.L3.Inference/Simulation/DayEndQueueState.cs` |
| `IQueueStatePersister` | Interface | `Task WriteAsync(DayEndQueueState, CancellationToken)`, `Task<DayEndQueueState?> ReadAsync(string symbol, DateOnly forDate, CancellationToken)`, `Task DeleteAsync(string symbol, CancellationToken)` | |
| `FileQueueStatePersister` | Sealed class : `IQueueStatePersister` | `FileQueueStatePersister(string outputDirectory)` | Reads/writes `{outputDir}/{SYMBOL}/day-state.json` |
| `SimCarryStateOptions` | Sealed record | `bool Enabled, double NightlyDecayFactor` | Parsed from `--sim-carry-state` / `--sim-nightly-decay` CLI flags; validated `[0.0, 0.8]` |

```csharp
// Constructor signatures
public FileQueueStatePersister(string outputDirectory)
// DayEndQueueState — record constructor auto-generated
```

#### Implementation Notes

- `DayEndQueueState` is a record in
  `src/Meridian.L3.Inference/Simulation/DayEndQueueState.cs`.
- `InferenceEngine.OnSessionCloseAsync()` calls `IQueueStatePersister.WriteAsync(state)`.
- `InferenceEngine.OnSessionOpenAsync()` calls `IQueueStatePersister.ReadAsync()`, applies
  decay, and injects into `QueueModel.InitialState`.
- When `day-state.json` is absent (first day of a carry-state run), `InferenceEngine` silently
  falls back to the default cold-start prior.

**Concrete interface definitions:**

```csharp
// src/Meridian.L3.Inference/Simulation/IQueueStatePersister.cs
public interface IQueueStatePersister
{
    Task WriteAsync(DayEndQueueState state, CancellationToken ct);
    Task<DayEndQueueState?> ReadAsync(string symbol, DateOnly forDate, CancellationToken ct);
    Task DeleteAsync(string symbol, CancellationToken ct);
}

// src/Meridian.L3.Inference/Simulation/DayEndQueueState.cs
public sealed record DayEndQueueState(
    string Symbol,
    DateOnly Date,
    int EstimatedQueuePositionAtClose,
    double BookImbalanceAtClose,
    decimal BidAskSpreadAtClose,
    double DecayFactorApplied);
```

**DI registration (`L3InferenceFeatureRegistration.cs`):**

```csharp
services.AddSingleton<IQueueStatePersister>(sp =>
    new FileQueueStatePersister(
        sp.GetRequiredService<SimulationOutputOptions>().OutputDirectory));
```

**Data flow:**

1. CLI parses `--sim-carry-state` → `SimCarryStateOptions.Enabled = true` and `NightlyDecayFactor` is validated to `[0.0, 0.8]`.
2. Simulation runner detects `Enabled = true` → disables day-level parallelism for all symbols; switches to sequential-per-symbol, sequential-day scheduling. Logs `Warning: "Carry-state mode: day-level parallelism disabled for {symbol}"`.
3. For each day, `InferenceEngine.OnSessionOpenAsync(symbol, date, ct)` calls `IQueueStatePersister.ReadAsync(symbol, date, ct)`.
4. If `DayEndQueueState` found: decay is applied to each field — `initialQueuePos = (int)(state.EstimatedQueuePositionAtClose × NightlyDecayFactor)`, similarly for imbalance and spread. Result is injected into `QueueModel.InitialState`.
5. If not found (first day): cold-start prior is used; `Debug` log notes the absence.
6. At session close, `InferenceEngine.OnSessionCloseAsync(symbol, date, ct)` reads final `QueueModel` state, constructs `DayEndQueueState` with `DecayFactorApplied = NightlyDecayFactor`, calls `IQueueStatePersister.WriteAsync(state, ct)`.
7. File path: `{outputDirectory}/{SYMBOL}/day-state.json` — symbol is `ToUpperInvariant()` for case-insensitive filesystem safety; one file per symbol, overwritten each day.

**Error handling and cancellation:**

- `ReadAsync` catches `FileNotFoundException` silently (returns `null`). Other `IOException` is logged at `Warning` and returns `null` — carry state is silently disabled for that day rather than aborting the simulation.
- `WriteAsync` catches `IOException`, logs at `Error`, and rethrows — losing carry state silently is worse than surfacing the error clearly.
- `NightlyDecayFactor` CLI validator enforces `[0.0, 0.8]`; values > 0.8 produce a CLI validation error with the message `"NightlyDecayFactor must be ≤ 0.8 to prevent bias amplification"`.

**Edge cases:**

1. **`NightlyDecayFactor = 0.0`:** All fields multiplied by zero; `DayEndQueueState` with all-zero numeric fields is written. `OnSessionOpenAsync` detects all-zero state and uses cold-start prior (same outcome as no carry state). No special casing needed.
2. **Symbol starts on day 2 of a multi-day run:** `ReadAsync` returns `null`; cold-start prior is used. No error.
3. **Output directory does not exist:** `FileQueueStatePersister` creates the directory (including `{SYMBOL}/` subdirectory) on first `WriteAsync` via `Directory.CreateDirectory`.
4. **Two symbols with different cases (e.g., `aapl` vs `AAPL`):** `FileQueueStatePersister` normalises to `symbol.ToUpperInvariant()` in all path operations, ensuring a single canonical file on case-sensitive filesystems.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| L3 `InferenceEngine` | Planned | `docs/plans/l3-inference-implementation-plan.md` |
| L3 simulation manifest | Planned | `docs/plans/l3-inference-implementation-plan.md` |

#### Tradeoffs

- Sequential execution is significantly slower than parallel for multi-symbol runs. Document
  the performance trade-off clearly; users should only enable carry-state when overnight state
  continuity is theoretically meaningful for their strategy.
- `NightlyDecayFactor = 1.0` could amplify systematic bias if the close-state estimate is
  wrong. Restrict to `[0.0, 0.8]` in the CLI validator and surface a warning for values > 0.7.


#### Test Strategy

**Test class:** `FileQueueStatePersisterTests` — `tests/Meridian.L3.Inference.Tests/Simulation/FileQueueStatePersisterTests.cs`
**Pattern:** Arrange-Act-Assert with a temp directory; no external mocks.

1. `WriteAsync_ThenReadAsync_RoundTripsStateCorrectly` — writes a `DayEndQueueState`, reads it back, asserts all fields (`Symbol`, `Date`, `BookImbalanceAtClose`, etc.) match.
2. `ReadAsync_WhenFileAbsent_ReturnsNull` — temp directory is empty; asserts `ReadAsync` returns `null` without throwing.
3. `WriteAsync_WhenOutputDirectoryAbsent_CreatesDirectoryAndWritesFile` — uses a non-existent subdirectory; asserts directory is created and file is readable afterwards.
4. `InferenceEngine_OnSessionOpenAsync_WithCarryState_AppliesDecayFactor` — integration test: provides a `DayEndQueueState` with known values; asserts the second day's `QueueModel.InitialState` is within the expected decay-factor range.
5. `SimulationRunner_WithCarryStateEnabled_ProcessesDaysSequentially` — asserts days are processed in chronological order (not in parallel) when `SimCarryStateOptions.Enabled = true`.

#### Audience

Institutional. Multi-day overnight carry-state is relevant for strategies that hold positions
across sessions and need realistic queue-continuation modeling.

#### Effort

**M** (~1 week). The plumbing is straightforward; the key engineering work is ensuring the
sequential constraint is enforced correctly without breaking the multi-symbol batch runner.

---

### Feature #27 — Dark Pool / Hidden Liquidity Estimator

#### Overview

A `DarkPoolEstimator` is added to the L3 inference engine. For each trade tick, if the executed
price is within the spread but the corresponding L2 level shows no size reduction, the tick is
flagged as a dark pool print candidate. A rolling `darkPoolParticipationRate` estimate is
maintained and used to reduce effective queue depth during simulation, producing faster simulated
fills when dark participation is high.

#### Design

**Dark print detection heuristic:**

```
IF trade.price ∈ [bid, ask]
   AND L2.bid_size unchanged after trade tick
   AND L2.ask_size unchanged after trade tick
THEN candidate = DarkPoolPrint, estimatedHiddenSize = trade.size
```

**`HiddenLiquidityModel` state:**

- Rolling window of 100 trade ticks per symbol.
- `darkPoolParticipationRate = darkPrintCount / windowSize`.
- Exponential moving average (α = 0.05) for smoothing.

**Simulation integration:** `QueueModel.EffectiveQueueDepth` is adjusted:

```
effectiveDepth = visibleDepth × (1 - darkPoolParticipationRate × darkPoolScaleFactor)
```

`darkPoolScaleFactor` is configurable in `InferenceModelConfig` (default `0.7`).

**Calibration report card addition:** `darkPoolConfidence` field — a value in `[0.0, 1.0]`
representing confidence in the dark pool fraction estimate, based on the consistency of the
detection signal over the calibration window.

**Output addition:** `summary.json` gains `estimatedDarkPoolFraction` field.

**Feature gate:** `EnableDarkPoolEstimation: false` by default in `InferenceModelConfig`. The
gate exists because the detection heuristic can produce false positives on symbols with low
L2 update latency (L2 update arrives after the trade due to feed sequencing differences).

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `IDarkPoolEstimator` | Interface | `bool IsEnabled`, `DarkPoolPrintResult ProcessTick(L3Tick, L2Snapshot l2Before, L2Snapshot l2After)`, `double CurrentParticipationRate`, `double CurrentConfidence`, `void Reset()` | |
| `DarkPoolEstimator` | Sealed class : `IDarkPoolEstimator` | `DarkPoolEstimator(IOptions<InferenceModelConfig>, ILogger<DarkPoolEstimator>)` | `src/Meridian.L3.Inference/DarkPool/DarkPoolEstimator.cs` |
| `HiddenLiquidityModel` | Sealed class | `HiddenLiquidityModel(int windowSize = 100, double emaAlpha = 0.05)`, `void Update(bool isDarkPrint, int tradeSize)`, `double ParticipationRate`, `double Confidence` | Rolling window EMA state |
| `DarkPoolPrintResult` | Sealed record | `bool IsDarkPrintCandidate, int EstimatedHiddenSize, DarkPrintReason? Reason` | Per-tick output |
| `DarkPrintReason` | Enum | `NoBidSizeChange, NoAskSizeChange, BothSidesUnchanged` | |

```csharp
public DarkPoolEstimator(IOptions<InferenceModelConfig> config, ILogger<DarkPoolEstimator> logger)
public HiddenLiquidityModel(int windowSize = 100, double emaAlpha = 0.05)
```

#### Implementation Notes

- `DarkPoolEstimator` lives in
  `src/Meridian.L3.Inference/DarkPool/DarkPoolEstimator.cs`.
- `HiddenLiquidityModel` is a class in
  `src/Meridian.L3.Inference/DarkPool/HiddenLiquidityModel.cs`.
- The detection heuristic requires trade aggressor-side data (to distinguish dark prints from
  delayed L2 updates). If aggressor-side data is unavailable, `DarkPoolEstimator` logs a
  `Warning` and disables itself for that symbol.
- False positive mitigation: a 2-millisecond grace window after the trade tick before checking
  L2 for size reduction accounts for normal feed latency.

**Concrete interface definitions:**

```csharp
// src/Meridian.L3.Inference/DarkPool/IDarkPoolEstimator.cs
public interface IDarkPoolEstimator
{
    bool IsEnabled { get; }
    DarkPoolPrintResult ProcessTick(L3Tick tick, L2Snapshot l2Before, L2Snapshot l2After);
    double CurrentParticipationRate { get; }
    double CurrentConfidence { get; }
    void Reset();
}

public sealed record DarkPoolPrintResult(
    bool IsDarkPrintCandidate,
    int EstimatedHiddenSize,
    DarkPrintReason? Reason);
```

**DI registration (`L3InferenceFeatureRegistration.cs`):**

```csharp
services.AddSingleton<IDarkPoolEstimator, DarkPoolEstimator>();
// Enabled/disabled via InferenceModelConfig.EnableDarkPoolEstimation (default false)
```

**Data flow:**

1. `InferenceEngine.ProcessTickAsync(tick, ct)` is called for each incoming `L3Tick`.
2. If `InferenceModelConfig.EnableDarkPoolEstimation == false`, `IDarkPoolEstimator.IsEnabled` returns `false`; the estimator path is skipped entirely.
3. If enabled: `InferenceEngine` retrieves `L2Snapshot l2Before` from its 2 ms `TimestampedBuffer<L2Snapshot>`.
4. After the 2 ms grace window, `L2Snapshot l2After` is read from the current L2 state.
5. `IDarkPoolEstimator.ProcessTick(tick, l2Before, l2After)` checks: `tick.Price ∈ [bid, ask]` AND `l2After.BidSize == l2Before.BidSize` AND `l2After.AskSize == l2Before.AskSize`.
6. If candidate: `HiddenLiquidityModel.Update(isDarkPrint: true, tick.Size)`. EMA updates `ParticipationRate`.
7. `QueueModel.EffectiveQueueDepth = visibleDepth × (1 - participationRate × darkPoolScaleFactor)`.
8. After calibration window, `CalibrationReportCard.DarkPoolConfidence = HiddenLiquidityModel.Confidence`.
9. `summary.json` writer appends `"estimatedDarkPoolFraction": currentParticipationRate`.

**Error handling and cancellation:**

- If aggressor-side data unavailable: `DarkPoolEstimator` sets `IsEnabled = false` for that symbol and logs `Warning: "Aggressor-side data unavailable for {symbol} — DarkPoolEstimator disabled"`.
- Division-by-zero in confidence: `Confidence = darkPrintCount > 0 ? (double)consistentPrints / darkPrintCount : 0.0`.
- `ProcessTick` is synchronous and must complete within 5 µs. If profiling shows > 5 µs, the `TimestampedBuffer` scan must be changed to an indexed ring-buffer lookup.

**Edge cases:**

1. **Symbol halted during data collection:** `HiddenLiquidityModel.Update` receives no ticks; `ParticipationRate` is frozen at its last value. `DarkPoolEstimator.Reset()` is called on halt/resume.
2. **Pre-market / post-market sessions:** High false-positive rates in illiquid sessions. Gated via `InferenceModelConfig.DarkPoolEstimationSessionFilter` (default: `RegularSession` only).
3. **Tick with price outside spread (crossed market):** Heuristic check fails; tick classified as lit print. No false positives from crossed-market ticks.
4. **Low-latency co-located feed (L2 update arrives before 2 ms window):** The heuristic correctly identifies a lit print (false negative, not false positive). The trade-off is documented in calibration guidance.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| L3 Phase 1 `InferenceEngine` | Planned | `docs/plans/l3-inference-implementation-plan.md` |
| Trade aggressor-side data in `fill-tape.jsonl` | Planned | `docs/plans/l3-inference-implementation-plan.md` |

#### Tradeoffs

- The detection heuristic is approximate. On venues with co-located L2 feeds (low latency),
  false positive rates are low. On slower feeds, the 2 ms grace window may need tuning.
- Dark pool estimation adds per-tick overhead. Profile the `DarkPoolEstimator.ProcessTick`
  path; if it exceeds 5 µs per tick, consider batching updates.


#### Test Strategy

**Test class:** `DarkPoolEstimatorTests` — `tests/Meridian.L3.Inference.Tests/DarkPool/DarkPoolEstimatorTests.cs`
**Pattern:** Arrange-Act-Assert; no external dependencies.

1. `ProcessTick_WithPriceInSpreadAndUnchangedL2Sizes_ReturnsDarkPrintCandidate`
2. `ProcessTick_WhenL2BidSizeDecreases_ReturnsLitPrint` — bid size reduced by the trade; asserts `IsDarkPrintCandidate == false`.
3. `ProcessTick_WhenAggressorSideUnavailable_DisablesEstimatorAndLogsWarning`
4. `HiddenLiquidityModel_Update_WithDarkPrints_IncreasesParticipationRateViaEma`
5. `DarkPoolEstimator_Reset_ClearsAllHiddenLiquidityModelState`

#### Audience

Academic, Institutional. Researchers studying execution quality in markets with significant
dark pool activity (US equities, fixed income) will find this most valuable.

#### Effort

**L** (~3–4 weeks). The detection heuristic itself is small, but ensuring correctness across
venue feed latency variations, building the confidence metric, and handling edge cases
(halts, auctions, pre-/post-market) is substantial.

---

## Track I — Multi-Instance: Additional Ideas (Round 2)

### Feature #28 — Zero-Downtime Rolling Upgrade Orchestration

#### Overview

The cluster coordinator detects instances running different binary versions (via
`BinaryVersion` field in heartbeats, sourced from `InformationalVersionAttribute`). A rolling
upgrade flow marks each instance's symbols for pre-reassignment, waits for subscription
acknowledgment, sends a graceful shutdown, then gradually reassigns symbols to the new-version
instance. A full rolling upgrade of a 4-instance cluster completes in approximately 90 seconds
with zero coverage gap.

**Prerequisite: Feature #30 (Split-Brain Detection) must ship before this feature.** Rolling
upgrade involves temporary single-coordinator windows that are indistinguishable from
split-brain scenarios without `SplitBrainDetector`.

#### Design

**API entry point:**

```http
PUT /api/cluster/upgrade
Content-Type: application/json

{ "targetVersion": "1.8.0" }
```

**`RollingUpgradeOrchestrator` flow:**

1. Coordinator reads all heartbeats from `ICoordinationStore`; identifies instances with
   `BinaryVersion ≠ targetVersion`.
2. For each stale instance (sorted: workers first, coordinator last):
   a. Mark instance's symbols as `"pre-reassign"` in `PartitionManifest`.
   b. Other instances subscribe to the pre-reassigned symbols.
   c. Await subscription acknowledgment from all receiving instances (timeout: 30 s).
   d. Send `GracefulShutdownHandler.InitiateShutdownAsync(instanceId)`.
      (`src/Meridian.Application/Services/GracefulShutdownHandler.cs`)
   e. Instance drains in-flight events, flushes WAL, exits.
   f. New-version instance starts, re-registers heartbeat.
   g. Coordinator reassigns symbols to new instance.
3. Upgrade complete when all heartbeats report `targetVersion`.

**"Coordinator last" sequencing:** `RollingUpgradeOrchestrator` sorts the upgrade order so the
instance holding the coordinator lease is upgraded last, minimizing the window during which
lease re-election is in progress.

**`RollingUpgradeOrchestrator`** lives in
`src/Meridian.Application/Coordination/RollingUpgradeOrchestrator.cs`.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `RollingUpgradeOrchestrator` | Sealed class | `RollingUpgradeOrchestrator(ICoordinationStore, IClusterCoordinator, GracefulShutdownHandler, ISubscriptionOwnershipService, IOptions<ClusterOptions>, ILogger<>)`, `Task<UpgradeResult> ExecuteAsync(string targetVersion, CancellationToken)` | `src/Meridian.Application/Coordination/RollingUpgradeOrchestrator.cs` |
| `ShutdownSignalWatcher` | Sealed class : `BackgroundService` | `ShutdownSignalWatcher(ICoordinationStore, GracefulShutdownHandler, IOptions<ClusterOptions>, ILogger<>)` | Polls `cluster/shutdown-requests/{instanceId}`; calls local `GracefulShutdownHandler` |
| `UpgradeState` | Sealed record | `string TargetVersion, IReadOnlyList<string> PendingInstanceIds, IReadOnlyList<string> UpgradedInstanceIds, UpgradeStatus Status, DateTimeOffset StartedAtUtc` | Persisted to `ICoordinationStore` under `cluster/upgrade-state` |
| `UpgradeResult` | Sealed record | `UpgradeStatus Status, string? ErrorReason, TimeSpan Duration` | |
| `UpgradeStatus` | Enum | `InProgress, Completed, Aborted` | |

```csharp
public RollingUpgradeOrchestrator(
    ICoordinationStore store,
    IClusterCoordinator coordinator,
    GracefulShutdownHandler shutdownHandler,
    ISubscriptionOwnershipService subscriptionService,
    IOptions<ClusterOptions> clusterOptions,
    ILogger<RollingUpgradeOrchestrator> logger)
```

**Schema additions required before this feature can be implemented:**

- `LeaseRecord` must gain `string? BinaryVersion = null` (does not currently exist).
- `ISubscriptionOwnershipService` must gain `int OwnedSymbolCount { get; }` (does not currently exist).

**IMPORTANT CORRECTION — `GracefulShutdownHandler.InitiateShutdownAsync` signature:**
The method takes `(ShutdownReason reason, string? message, CancellationToken ct)` and operates
on the **local process only** — it cannot be called cross-process. The orchestrator signals
remote shutdown by writing to `ICoordinationStore` under key
`cluster/shutdown-requests/{instanceId}`. A new `ShutdownSignalWatcher` background service
on each instance polls that key and calls its local
`GracefulShutdownHandler.InitiateShutdownAsync(ShutdownReason.Requested, "Rolling upgrade", ct)`.

#### Implementation Notes

- `BinaryVersion` is added to the heartbeat record (`LeaseRecord`) and populated from
  `Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()`.
- The 30-second subscription acknowledgment timeout is configurable via
  `ClusterOptions.UpgradeSubscriptionAckTimeoutSeconds`.
- If acknowledgment times out, the upgrade is aborted and a `PUT /api/cluster/upgrade/cancel`
  endpoint resets all `"pre-reassign"` flags.
- Upgrade state is persisted in `ICoordinationStore` under key `cluster/upgrade-state` so it
  survives coordinator restart.

**Required schema additions (these fields do NOT yet exist in the codebase):**

```csharp
// LeaseRecord.cs — add BinaryVersion field:
public sealed record LeaseRecord(
    string ResourceId,
    string InstanceId,
    long LeaseVersion,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset LastRenewedAtUtc,
    string? BinaryVersion = null);   // NEW — from AssemblyInformationalVersionAttribute

// ISubscriptionOwnershipService.cs — add OwnedSymbolCount property:
public interface ISubscriptionOwnershipService
{
    Task<LeaseAcquireResult> TryAcquireAsync(string providerId, string kind, string symbol, CancellationToken ct);
    Task<bool> ReleaseAsync(string providerId, string kind, string symbol, CancellationToken ct);
    int OwnedSymbolCount { get; }   // NEW — used by orchestrator and Feature #29 load-balancing
}
```

**DI registration (`CoordinationFeatureRegistration.cs`):**

```csharp
services.AddSingleton<RollingUpgradeOrchestrator>();
services.AddHostedService<ShutdownSignalWatcher>();
```

**Data flow:**

1. `PUT /api/cluster/upgrade` calls `RollingUpgradeOrchestrator.ExecuteAsync(targetVersion, ct)`.
2. Orchestrator calls `ICoordinationStore.GetAllLeasesAsync(ct)` to read all heartbeat leases (key prefix `"cluster/heartbeats/"`). Identifies instances where `LeaseRecord.BinaryVersion != targetVersion`.
3. Sorts stale instances workers-first, current coordinator last. Persists initial `UpgradeState` to `ICoordinationStore` under `cluster/upgrade-state`.
4. For each stale instance:
   a. Writes `"pre-reassign"` markers to `PartitionManifest` for that instance's symbols.
   b. Polls `ICoordinationStore.GetAllLeasesAsync` with exponential backoff (250 ms → 500 ms → 1 s) until all symbols show a new owner, up to `UpgradeSubscriptionAckTimeoutSeconds`.
   c. Writes shutdown signal to `ICoordinationStore.TryAcquireLeaseAsync("cluster/shutdown-requests/{instanceId}", ...)`.
   d. Target instance's `ShutdownSignalWatcher` detects the key and calls local `GracefulShutdownHandler.InitiateShutdownAsync(ShutdownReason.Requested, "Rolling upgrade", ct)`.
   e. Instance flushes WAL, disposes async disposables, exits.
   f. Orchestrator polls heartbeat store until the instance's lease expires (TTL 15 s), confirming it stopped.
   g. New-version instance starts, registers heartbeat with updated `BinaryVersion`.
   h. Orchestrator updates `UpgradeState.UpgradedInstanceIds` in store.
5. When all instances upgraded: `UpgradeState.Status = UpgradeStatus.Completed`.

**Error handling and cancellation:**

- Ack timeout: orchestrator writes `UpgradeState.Status = UpgradeStatus.Aborted`, resets all `"pre-reassign"` markers, returns `UpgradeResult(Aborted, "Ack timeout", elapsed)`.
- `ct` cancellation mid-upgrade writes `UpgradeStatus.Aborted` to the store before returning.
- On coordinator restart mid-upgrade: new instance reads existing `UpgradeState` from store and resumes from `PendingInstanceIds`.
- `ICoordinationStore.GetCorruptedLeaseFilesAsync` is checked at upgrade start; corrupted files cause a pre-flight abort.

**Edge cases:**

1. **All instances already at `targetVersion`:** No stale instances; returns `UpgradeResult(Completed, null, TimeSpan.Zero)` immediately.
2. **Upgrade triggered while a previous one is `InProgress`:** If `StartedAtUtc` < 10 minutes ago, returns `409 Conflict`. Stale upgrades (> 10 min) are forcibly reset.
3. **Instance crashes during drain:** After TTL expiry the heartbeat-poll succeeds. The crashed instance's symbols are temporarily orphaned; the next rebalance cycle reassigns them.
4. **`OwnedSymbolCount` returns 0 immediately after pre-reassign (race condition):** Exponential backoff handles this; the orchestrator does not declare ack success until symbols show positive `OwnedSymbolCount` on a receiving instance.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| `LeaseManager` | Implemented | `src/Meridian.Application/Coordination/LeaseManager.cs` |
| `ClusterCoordinatorService` | Implemented | `src/Meridian.Application/Coordination/ClusterCoordinatorService.cs` |
| `GracefulShutdownHandler` | Implemented | `src/Meridian.Application/Services/GracefulShutdownHandler.cs` |
| `ICoordinationStore` | Implemented | `src/Meridian.Application/Coordination/ICoordinationStore.cs` |
| Feature #30 (Split-Brain Detection) | Planned — prerequisite | See Feature #30 below |

#### Tradeoffs

- "Coordinator last" sequencing means the cluster runs with a temporarily reduced coordinator
  set for ~90 seconds. If a second failure occurs in this window, recovery depends on #30.
  Document the reduced-redundancy window in the operator runbook.
- Acknowledgment-based sequencing adds latency. For large symbol portfolios (1000+ symbols),
  the per-instance subscription acknowledgment can take > 30 seconds. Make the timeout
  configurable and document observed timing for representative deployments.


#### Test Strategy

**Test class:** `RollingUpgradeOrchestratorTests` — `tests/Meridian.Application.Tests/Coordination/RollingUpgradeOrchestratorTests.cs`
**Pattern:** Arrange-Act-Assert with `Mock<ICoordinationStore>`, `Mock<IClusterCoordinator>` (Moq).

1. `ExecuteAsync_WhenAllInstancesAtTargetVersion_ReturnsCompletedImmediately`
2. `ExecuteAsync_WhenAckTimeoutExpires_ReturnsAbortedAndResetsPreReassignMarkers`
3. `ExecuteAsync_WithOneStaleInstance_WritesShutdownSignalKeyToStore` — asserts `TryAcquireLeaseAsync` called with key matching `"cluster/shutdown-requests/"` prefix.
4. `ExecuteAsync_OnCoordinatorRestartMidUpgrade_ResumesFromPersistedUpgradeState` — pre-stores partial `UpgradeState`; creates new orchestrator instance; asserts resumes from correct `PendingInstanceIds`.
5. `LeaseRecord_WithBinaryVersion_SerializesAndDeserializesCorrectly` — round-trips through `System.Text.Json`; asserts `BinaryVersion` field preserved.

#### Audience

Institutional. Zero-downtime upgrades are a hard requirement for any deployment collecting
24/5 market data across multiple asset classes.

#### Effort

**M** (~1.5 weeks). The underlying primitives (`GracefulShutdownHandler`, `LeaseManager`,
`ICoordinationStore`) are all implemented. The new work is orchestration logic and the upgrade
state machine.

---

### Feature #29 — Geo-Distributed Collection by Exchange Timezone

#### Overview

`PartitionManifest` gains an optional `timezoneAffinity` map. During symbol rebalancing, the
coordinator assigns symbols to instances based on the venue's IANA timezone, preferring
instances in the same region. Affinities are hints not hard constraints — if the preferred
instance is unavailable, any healthy instance receives the symbol.

**Prerequisite: Feature #30 (Split-Brain Detection) must ship before this feature.** Geo-
distributed collection across regions introduces network partition scenarios that require
`SplitBrainDetector` to be operational.

**Shared prerequisite with #24:** Both features require `VenueMicMapper` to expose an
`IanaTimezone` field from `config/venue-mapping.json`.

#### Design

**`PartitionManifest` extension:**

```jsonc
{
  "timezoneAffinity": {
    "Europe/London":      ["inst-eu-1"],
    "Asia/Tokyo":         ["inst-ap-1"],
    "America/New_York":   ["inst-us-1", "inst-us-2"]
  }
}
```

**Rebalancing logic in `SubscriptionOwnershipService`:**

```
For each unassigned symbol:
  1. Resolve venue IANA timezone via VenueMicMapper.GetIanaTimezone(symbol.Exchange)
  2. Look up preferred instances from timezoneAffinity[timezone]
  3. From preferred instances, pick the one with lowest current symbol count
  4. If no preferred instance is healthy, fall back to lowest-load any-healthy instance
  5. Assign symbol; update PartitionManifest
```

**`VenueMicMapper` extension:** Add `string? IanaTimezone` property to the venue mapping
record, populated from a new `ianaTimezone` field in `config/venue-mapping.json`.
`VenueMicMapper` (`src/Meridian.Application/Canonicalization/VenueMicMapper.cs`) already loads
this config file — adding the field is a non-breaking extension.

**Operator experience:** For single-region or single-instance deployments, `timezoneAffinity`
is omitted from `PartitionManifest` and the feature is entirely inactive. No behavioral
difference for deployments that do not configure affinities.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `ITimezoneAffinityResolver` | Interface | `string? ResolveIanaTimezone(string exchangeMic)`, `IReadOnlyList<string> GetPreferredInstances(string ianaTimezone)`, `bool HasAffinity(string ianaTimezone)` | Optional DI dep on `SubscriptionOwnershipService` |
| `TimezoneAffinityResolver` | Sealed class : `ITimezoneAffinityResolver` | `TimezoneAffinityResolver(VenueMicMapper, IReadOnlyDictionary<string, IReadOnlyList<string>> affinityMap)` | Loaded from `PartitionManifest.timezoneAffinity` at startup |

```csharp
public TimezoneAffinityResolver(
    VenueMicMapper micMapper,
    IReadOnlyDictionary<string, IReadOnlyList<string>> affinityMap)
```

**`VenueMicMapper` extension required:** The constructor gains a second
`FrozenDictionary<string, string> timezoneByMic` parameter (MIC → IANA timezone). This is
populated from a new `"ianaTimezone"` field added to each entry in `config/venue-mapping.json`.
`VenueMicMapper` currently maps `(Provider, RawVenue) → MIC` only; the timezone layer is
additive and non-breaking.

#### Implementation Notes

- Affinity resolution happens at rebalance time, not at subscription time. If a symbol's
  preferred instance goes down, the next rebalance cycle reassigns it to a fallback instance.
- `SubscriptionOwnershipService` is extended with `ITimezoneAffinityResolver`, injected as an
  optional dependency (null = affinity disabled).
- Affinity ties (multiple preferred instances) are broken by current symbol count. This
  implicitly load-balances within a region.

**Concrete interface definitions:**

```csharp
// src/Meridian.Application/Coordination/ITimezoneAffinityResolver.cs
public interface ITimezoneAffinityResolver
{
    string? ResolveIanaTimezone(string exchangeMic);
    IReadOnlyList<string> GetPreferredInstances(string ianaTimezone);
    bool HasAffinity(string ianaTimezone);
}

// config/venue-mapping.json — each entry gains ianaTimezone:
// { "provider": "InteractiveBrokers", "rawVenue": "NYSE", "mic": "XNYS",
//   "ianaTimezone": "America/New_York" }   <-- NEW field
```

**DI registration (`CoordinationFeatureRegistration.cs`):**

```csharp
// Registered as nullable — null when timezoneAffinity is absent from PartitionManifest
services.AddSingleton<ITimezoneAffinityResolver?>(sp =>
{
    var manifest = sp.GetRequiredService<PartitionManifest>();
    if (manifest.TimezoneAffinity is null or { Count: 0 }) return null;
    var mapper = sp.GetRequiredService<VenueMicMapper>();
    return new TimezoneAffinityResolver(mapper, manifest.TimezoneAffinity);
});
```

**Data flow:**

1. `SubscriptionOwnershipService.RebalanceAsync(ct)` fires on startup or instance join/leave.
2. For each unassigned symbol:
   a. `ITimezoneAffinityResolver?.ResolveIanaTimezone(symbol.ExchangeMic)` → `string? tz`.
   b. If `tz != null` and `HasAffinity(tz)`: get preferred instances via `GetPreferredInstances(tz)`.
   c. Filter to healthy instances only (those with non-expired heartbeat leases in `ICoordinationStore`).
   d. From healthy preferred instances, pick the one with the lowest `OwnedSymbolCount` (the new `ISubscriptionOwnershipService` member from Feature #28).
   e. If no healthy preferred instances: fall back to the existing lowest-load any-healthy assignment.
   f. `ISubscriptionOwnershipService.TryAcquireAsync(providerId, kind, symbol.Symbol, ct)` assigns the symbol.
3. Startup validation: if any instance ID in `timezoneAffinity` does not appear in any current heartbeat, log `Warning: "Affinity instance '{id}' not in cluster heartbeats — affinity map may be stale"`.

**Error handling and cancellation:**

- `ITimezoneAffinityResolver` is nullable; if `null` (no affinity configured), `SubscriptionOwnershipService` uses the existing load-balancing path unchanged.
- `ResolveIanaTimezone` returns `null` for unknown MICs; fallback path is used silently.
- `ct` propagates through `RebalanceAsync`; partial assignment on cancellation is corrected on the next rebalance cycle via idempotent acquire logic.

**Edge cases:**

1. **All preferred instances for a timezone are unhealthy:** Full fallback to load-balanced any-healthy assignment. `Debug` log: `"All preferred instances for 'Europe/London' unhealthy — using fallback"`.
2. **`timezoneAffinity` map contains an empty list for a timezone:** `GetPreferredInstances` returns empty; fallback path used for all symbols in that timezone. No exception.
3. **Symbol's exchange MIC not in `venue-mapping.json`:** `ResolveIanaTimezone` returns `null`; symbol assigned via default load-balancing.
4. **Single-instance deployment with non-empty `timezoneAffinity`:** Every timezone zone resolves to the single instance; feature is a no-op. Fully transparent to single-instance operators.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| `ISubscriptionOwnershipService` | Implemented | `src/Meridian.Application/Coordination/ISubscriptionOwnershipService.cs` |
| `SubscriptionOwnershipService` | Implemented | `src/Meridian.Application/Coordination/SubscriptionOwnershipService.cs` |
| `VenueMicMapper` (add `IanaTimezone`) | Partial | `src/Meridian.Application/Canonicalization/VenueMicMapper.cs` |
| `config/venue-mapping.json` (add `ianaTimezone` field) | Partial | `config/` root |
| Feature #30 (Split-Brain Detection) | Planned — prerequisite | See Feature #30 below |

#### Tradeoffs

- Affinities are hints. A misconfigured `timezoneAffinity` map (e.g., wrong instance IDs) will
  silently degrade to round-robin assignment. Add a startup validation step that warns if
  preferred instance IDs in `timezoneAffinity` do not match any registered instance heartbeat.
- Cross-region network partitions are the primary failure mode. Without #30, a partition that
  splits `inst-eu-1` from the coordinator could result in double-collection of London symbols.


#### Test Strategy

**Test class:** `TimezoneAffinityResolverTests` — `tests/Meridian.Application.Tests/Coordination/TimezoneAffinityResolverTests.cs`
**Pattern:** Arrange-Act-Assert with a mock `VenueMicMapper`.

1. `ResolveIanaTimezone_WithKnownMic_ReturnsCorrectTimezone` — maps `"XNYS"` → `"America/New_York"`.
2. `ResolveIanaTimezone_WithUnknownMic_ReturnsNull`
3. `GetPreferredInstances_WithConfiguredTimezone_ReturnsInstanceList`
4. `SubscriptionOwnershipService_RebalanceAsync_WithAffinity_AssignsSymbolToPreferredInstance` — integration test; verifies timezone-guided assignment when preferred instance is healthy.
5. `SubscriptionOwnershipService_RebalanceAsync_WhenAllPreferredUnhealthy_UsesDefaultLoadBalancedAssignment`

#### Audience

Institutional. Operators running multi-region infrastructure will see reduced WAN latency for
exchange feed ingestion when collection instances are co-located with their exchange.

#### Effort

**L** (~3 weeks). `VenueMicMapper` extension and `PartitionManifest` schema change are small.
The `SubscriptionOwnershipService` rebalancing logic extension and the cross-region failure
mode testing are the bulk of the effort.

---

### Feature #30 — Split-Brain Detection & Recovery

#### Overview

A `SplitBrainDetector` background service runs on every instance. It writes a heartbeat every
5 seconds and reads all other instances' heartbeats. If two coordinator heartbeats are detected
simultaneously, the instance with the lower lexicographic `InstanceId` yields — releasing its
lease immediately. The yielding instance runs a consistency check, identifies double-collected
symbols, and writes a reconciliation request to shared storage. The web dashboard gains a
"Cluster Events" log panel.

This feature must ship before Features #28 and #29, which both rely on its guarantee.

#### Design

**Heartbeat write:**

```
ICoordinationStore.WriteHeartbeatAsync(new InstanceHeartbeat {
    InstanceId      = _options.InstanceId,
    BinaryVersion   = _binaryVersion,
    IsCoordinator   = _leaseManager.HoldsLease,
    Timestamp       = DateTimeOffset.UtcNow,
    SymbolCount     = _subscriptionOwnershipService.OwnedSymbolCount
});
```

Heartbeat key: `cluster/heartbeats/{instanceId}`. TTL: 15 seconds (3 missed heartbeats).

**Split-brain detection:**

```
var coordinatorHeartbeats = allHeartbeats
    .Where(h => h.IsCoordinator && h.Age < TimeSpan.FromSeconds(15))
    .ToList();

if (coordinatorHeartbeats.Count > 1)
{
    _logger.LogWarning("SplitBrain detected: {count} coordinators", coordinatorHeartbeats.Count);
    var shouldYield = string.Compare(_options.InstanceId,
        coordinatorHeartbeats.Min(h => h.InstanceId), StringComparison.Ordinal) > 0;
    if (shouldYield) await YieldCoordinatorLeaseAsync();
}
```

**`YieldCoordinatorLeaseAsync` procedure:**

1. Release lease via `ILeaseManager.ReleaseLease()`.
2. Log `SplitBrainResolved` at `LogLevel.Warning`.
3. Read `PartitionManifest` and per-instance symbol subscriptions from `ICoordinationStore`.
4. Identify double-collected symbols (symbols present in both this instance's and the
   surviving coordinator's subscription lists).
5. Write `ReconciliationRequest` to `ICoordinationStore` under
   `cluster/reconciliation/{requestId}`.
6. Surviving coordinator picks up the `ReconciliationRequest`, marks duplicates for
   `IDedupStore` cleanup (`src/Meridian.Application/Pipeline/IDedupStore.cs`).

**Dashboard "Cluster Events" panel:** An `ObservableCollection<ClusterEventViewModel>` driven
by a Server-Sent Events stream from `/api/cluster/events`. Events include:
`SplitBrainDetected`, `SplitBrainResolved`, `LeaseReacquired`, `ReconciliationComplete`.

#### Class Design

| Type | Kind | Key Members | Notes |
|---|---|---|---|
| `SplitBrainDetector` | Sealed class : `BackgroundService` | Already exists as skeleton; `ExecuteAsync(CancellationToken)` 5-second loop, `DetectAndHealAsync(CancellationToken)` filled in by this feature | `src/Meridian.Application/Coordination/SplitBrainDetector.cs` |
| `ReconciliationRequest` | Sealed record | `string RequestId, string YieldingInstanceId, string SurvivingInstanceId, IReadOnlyList<string> DoubleCollectedSymbols, DateTimeOffset CreatedAtUtc` | Written to `ICoordinationStore` under `cluster/reconciliation/{requestId}` |
| `ReconciliationWorker` | Sealed class : `BackgroundService` | `ReconciliationWorker(ICoordinationStore, ISubscriptionOwnershipService, PersistentDedupLedger, ILogger<>)` | New; processes `ReconciliationRequest` entries idempotently |
| `ClusterEventViewModel` | Sealed class | `string EventType, string Description, DateTimeOffset OccurredAt, string? InstanceId` | Dashboard "Cluster Events" panel |

```csharp
// SplitBrainDetector constructor (already exists):
public SplitBrainDetector(
    ICoordinationStore store,
    IClusterCoordinator coordinator,
    ILogger<SplitBrainDetector> logger)

// ReconciliationWorker (new):
public ReconciliationWorker(
    ICoordinationStore store,
    ISubscriptionOwnershipService subscriptionService,
    PersistentDedupLedger dedupLedger,
    ILogger<ReconciliationWorker> logger)
```

**IMPORTANT CORRECTIONS:**

- **Heartbeats are stored as leases** via `ICoordinationStore.TryAcquireLeaseAsync("cluster/heartbeats/{instanceId}", instanceId, TimeSpan.FromSeconds(15), TimeSpan.Zero, ct)` — refreshed every 5 seconds with `ILeaseManager.RenewAsync`. There is NO separate `WriteHeartbeatAsync` API.
- **`IDedupStore` / `PersistentDedupLedger`** is for event-level deduplication (tracks whether an event ID has been processed). After split-brain recovery, the reconciliation process force-expires `PersistentDedupLedger` cache entries for the affected symbols' event streams within the split-brain window, causing those events to be re-evaluated for duplicates rather than silently dropped.

#### Implementation Notes

- `SplitBrainDetector` already exists at
  `src/Meridian.Application/Coordination/SplitBrainDetector.cs` as a skeleton. This feature
  fills in the detection and recovery logic.
- `SharedStorageCoordinationStore` (`src/Meridian.Application/Coordination/SharedStorageCoordinationStore.cs`)
  provides `ICoordinationStore` — heartbeats and reconciliation requests are written here.
- Lexicographic tie-breaking is deterministic and requires no consensus round-trip.
- The `ReconciliationRequest` is processed by the surviving coordinator's
  `ReconciliationWorker` background service (new, in `src/Meridian.Application/Coordination/`).
- `IDedupStore.MarkForDeduplication(symbolId, duplicateInstanceId)` is called for each
  identified double-collected symbol.

**Heartbeat mechanism — using `ICoordinationStore` leases (no separate API):**

```csharp
// Heartbeat write every 5 seconds (inside SplitBrainDetector.ExecuteAsync loop):
// First registration:
await _store.TryAcquireLeaseAsync(
    $"cluster/heartbeats/{_instanceId}", _instanceId,
    TimeSpan.FromSeconds(15), takeoverDelay: TimeSpan.Zero, ct);
// Renewal:
await _store.RenewLeaseAsync(
    $"cluster/heartbeats/{_instanceId}", _instanceId,
    TimeSpan.FromSeconds(15), ct);
```

**DI registration (`CoordinationFeatureRegistration.cs`):**

```csharp
services.AddHostedService<SplitBrainDetector>();  // already registered as skeleton
services.AddHostedService<ReconciliationWorker>(); // NEW
```

**Data flow:**

1. `SplitBrainDetector.ExecuteAsync(ct)` runs a 5-second loop calling `DetectAndHealAsync(ct)`.
2. **Heartbeat write:** `ICoordinationStore.RenewLeaseAsync("cluster/heartbeats/{instanceId}", instanceId, TTL=15s, ct)`.
3. **Detection:** `ICoordinationStore.GetAllLeasesAsync(ct)` → filters leases with `ResourceId.StartsWith("cluster/heartbeats/")` and `ExpiresAtUtc > now`. Determines which instances are coordinators by checking `ICoordinationStore.GetLeaseAsync("leader/cluster-coordinator", ct)`.
4. If `coordinatorCount > 1`: `string.Compare(myInstanceId, lowestCoordinatorId, Ordinal) > 0` → this instance yields.
5. **YieldCoordinatorLeaseAsync:**
   a. `ICoordinationStore.ReleaseLeaseAsync("leader/cluster-coordinator", myInstanceId, ct)`.
   b. Log `Warning: "SplitBrain: yielding coordinator role to {survivingId}"`.
   c. Read all symbol leases owned by this instance (prefix `"symbols/"`); cross-reference with surviving coordinator's to find double-collected symbols.
   d. Create `ReconciliationRequest` and write via `ICoordinationStore.TryAcquireLeaseAsync("cluster/reconciliation/{requestId}", ...)` with TTL 1 hour.
6. **`ReconciliationWorker`** polls `GetAllLeasesAsync` every 10 s for keys with prefix `"cluster/reconciliation/"`.
7. For each unprocessed request:
   a. `ISubscriptionOwnershipService.ReleaseAsync(...)` for each double-collected symbol on the yielding instance.
   b. Force-expire `PersistentDedupLedger` entries for those symbols' event streams within the 15-second split-brain window (set `expiryTicks = 0` in the in-memory cache), causing re-evaluation of events received during the split-brain window.
   c. `ICoordinationStore.ReleaseLeaseAsync("cluster/reconciliation/{requestId}", ...)` marks processed.
8. Dashboard SSE stream broadcasts `SplitBrainDetected`, `SplitBrainResolved`, `ReconciliationComplete` events.

**Error handling and cancellation:**

- `DetectAndHealAsync` is wrapped in `try/catch(Exception ex)` inside the loop. Any exception is logged at `Error` and the loop continues — a single crashed detection cycle must not permanently disable the detector.
- `ReconciliationWorker` is idempotent: if a `requestId` lease has already been released, that request is skipped on restart.
- `ct` propagates to all `ICoordinationStore` calls; on graceful shutdown, in-progress detection is abandoned cleanly.
- `ICoordinationStore.GetCorruptedLeaseFilesAsync` checked on startup; non-empty results logged at `Warning`.

**Edge cases:**

1. **Both instances share identical InstanceIds (misconfiguration):** Both yield; cluster loses coordinator. `CoordinationFeatureRegistration` validates at startup that the configured `InstanceId` does not match any existing heartbeat; throws `InvalidOperationException` on conflict.
2. **Three-way split (3 coordinators):** Two higher-ID instances yield; both write `ReconciliationRequest` entries. `ReconciliationWorker` processes both independently. Surviving coordinator handles multiple reconciliation requests correctly.
3. **`GetAllLeasesAsync` takes > 4 s (storage contention):** Guard with an `Interlocked`-based `_detectionInProgress` flag; skip the detection cycle if a previous one is still running.
4. **`TotalDuplicates` counter spikes after dedup window reset:** Expected and documented in the operator runbook. The spike is bounded to events received during the ≤ 15-second split-brain window.

#### Dependencies

| Dependency | Status | Location |
|---|---|---|
| `ICoordinationStore` | Implemented | `src/Meridian.Application/Coordination/ICoordinationStore.cs` |
| `SharedStorageCoordinationStore` | Implemented | `src/Meridian.Application/Coordination/SharedStorageCoordinationStore.cs` |
| `LeaseManager` | Implemented | `src/Meridian.Application/Coordination/LeaseManager.cs` |
| `SplitBrainDetector` (skeleton) | Partial | `src/Meridian.Application/Coordination/SplitBrainDetector.cs` |
| `IDedupStore` | Implemented | `src/Meridian.Application/Pipeline/IDedupStore.cs` |

#### Tradeoffs

- Lexicographic tie-breaking always yields the "higher" InstanceId. If instance IDs are not
  unique (e.g., both set to `"instance-1"` by misconfiguration), both yield simultaneously
  and the cluster loses the coordinator role. Add a startup uniqueness check that fails fast
  if two instances share an ID.
- The 5-second heartbeat interval + 15-second TTL means split-brain detection can take up to
  15 seconds. During this window, both coordinators may issue conflicting rebalance commands.
  `IDedupStore` must handle duplicates from this window; ensure its dedup window exceeds 15
  seconds.
- `ReconciliationWorker` must be idempotent — if it crashes mid-reconciliation and restarts,
  it should not create new duplicates.


#### Test Strategy

**Test class:** `SplitBrainDetectorTests` — `tests/Meridian.Application.Tests/Coordination/SplitBrainDetectorTests.cs`
**Pattern:** Arrange-Act-Assert with `Mock<ICoordinationStore>`, `Mock<IClusterCoordinator>` (Moq).

1. `DetectAndHealAsync_WithSingleCoordinator_DoesNotCallReleaseLeaseAsync`
2. `DetectAndHealAsync_WithTwoCoordinators_HigherLexicographicIdYields` — two coordinator heartbeat leases; local instance has higher ID; asserts `ReleaseLeaseAsync("leader/cluster-coordinator", ...)` called.
3. `DetectAndHealAsync_WithTwoCoordinators_LowerLexicographicIdDoesNotYield` — local instance has lower ID; asserts `ReleaseLeaseAsync` NOT called.
4. `YieldCoordinatorLeaseAsync_WritesReconciliationRequestToCoordinationStore` — asserts `TryAcquireLeaseAsync` called with key starting with `"cluster/reconciliation/"`.
5. `ReconciliationWorker_ProcessesRequest_ReleasesDoubleCollectedSymbolSubscriptions` — mock store returns one reconciliation request; asserts `ISubscriptionOwnershipService.ReleaseAsync` called for each double-collected symbol.

#### Audience

Institutional. Any operator running more than one instance needs split-brain protection as a
foundational guarantee before deploying rolling upgrades or geo-distribution.

#### Effort

**M** (~1–1.5 weeks). The skeleton exists; the recovery procedure and `ReconciliationWorker`
are the new work. The dashboard event panel is a small additive change.

---

## Synthesis

### Highest-Leverage Idea

**Feature #25 — TCA Report** is the highest-leverage idea in this batch. It:

- Requires no new data sources (post-processes existing `fill-tape.jsonl` and `summary.json`).
- Requires no new UI frameworks (HTML template + ScottPlot, both already in scope).
- Answers "how good was my execution?" — the question every execution researcher asks first.
- Delivers a printable professional report that can be included in research papers or client
  presentations with zero additional work from the user.

Start here if the team can only ship one idea from Track H.

### Platform Bets

Three features represent long-term platform bets with disproportionate strategic value:

| Feature | Bet |
|---|---|
| #19 Script Debugger | Positions QuantScript as a genuine research IDE, not just a scripting environment. Reduces time-to-insight from hours to minutes. |
| #27 Dark Pool Estimator | Differentiates Meridian's L3 engine from purely lit-market simulations. As dark pool participation grows, this becomes a correctness requirement, not a nice-to-have. |
| #29 Geo-Distributed Collection | Unlocks institutional-grade multi-region deployments. Most quant platforms cannot offer this without significant infrastructure investment. |

### Cross-Cutting Shared Prerequisites

Three pieces of work unblock multiple features and should be completed early:

| Prerequisite | Unblocks | Work Required |
|---|---|---|
| **Feature #30** (Split-Brain Detection) | Features #28, #29 | Fill in recovery logic in existing `SplitBrainDetector.cs` skeleton |
| **`VenueMicMapper.IanaTimezone`** field | Features #24, #29 | Add `ianaTimezone` to `config/venue-mapping.json`; expose on `VenueMicMapper` |
| **QuantScript v1 `ScriptRunner` + `QuantScriptGlobals`** | Features #19, #20, #21, #22, #23 | Complete the QuantScript v1 foundation per `docs/plans/quant-script-environment-blueprint.md` |

Completing these three items first maximizes parallelism: Track G features can proceed
independently of Track H and Track I once the QuantScript v1 foundation is in place.

### Suggested Sequencing

#### Phase 1 — Foundations (prerequisite clearance)

| Item | Notes |
|---|---|
| QuantScript v1 ScriptRunner + QuantScriptGlobals | Required for all Track G features |
| Feature #30 — Split-Brain Detection & Recovery | Required before #28 and #29 |
| `VenueMicMapper.IanaTimezone` field | Required before #24 and #29; 1-day config change |

#### Phase 2 — High-value, low-risk delivery

| Feature | Rationale |
|---|---|
| #23 Script Expression REPL | S-effort; immediate UX win once QuantScript v1 ships |
| #21 Security Master in QuantScript | S-effort; already-implemented backend; thin wiring |
| #25 TCA Report | M-effort; highest-leverage idea in the batch; no new dependencies |
| #20 Script Run History & Diffing | M-effort; high retention value for academic users |

#### Phase 3 — Medium-complexity features

| Feature | Rationale |
|---|---|
| #22 Script Output Export | M-effort; AnalysisExportService already handles Parquet/Excel |
| #24 Venue-Specific Calibration Profiles | M-effort; VenueMicMapper already extended in Phase 1 |
| #26 Multi-Day Queue State Continuity | M-effort; sequential constraint needs careful testing |
| #28 Rolling Upgrade Orchestration | M-effort; requires #30 from Phase 1 |

#### Phase 4 — Long-horizon investments

| Feature | Rationale |
|---|---|
| #19 Script Debugger | L-effort; most impactful for UX but largest QuantScript v1 dependency |
| #27 Dark Pool Estimator | L-effort; correctness across venue feed latency is the challenge |
| #29 Geo-Distributed Collection | L-effort; requires #30 and VenueMicMapper; only matters at multi-region scale |

---

## Related Documents

- **QuantScript v1 blueprint:** [`docs/plans/quant-script-environment-blueprint.md`](quant-script-environment-blueprint.md)
- **L3 inference implementation plan:** [`docs/plans/l3-inference-implementation-plan.md`](l3-inference-implementation-plan.md)
- **Previous brainstorm (ideas #1–#18):** [`docs/evaluations/quant-script-blueprint-brainstorm.md`](../evaluations/quant-script-blueprint-brainstorm.md)
- **Feature inventory:** [`docs/status/FEATURE_INVENTORY.md`](../status/FEATURE_INVENTORY.md)
- **Main roadmap:** [`docs/status/ROADMAP.md`](../status/ROADMAP.md)

### Key Source Files Referenced

Files marked **[exists]** are already in the repository. Files marked **[proposed]** are new
paths that will be created when implementing the corresponding feature.

#### Existing files — touched or extended by these features

| File | Feature |
|---|---|
| `src/Meridian.Application/Canonicalization/VenueMicMapper.cs` | #24, #29 |
| `src/Meridian.Application/Coordination/ClusterCoordinatorService.cs` | #28, #30 |
| `src/Meridian.Application/Coordination/ICoordinationStore.cs` | #28, #30 |
| `src/Meridian.Application/Coordination/ILeaseManager.cs` | #28, #30 |
| `src/Meridian.Application/Coordination/ISubscriptionOwnershipService.cs` | #29 |
| `src/Meridian.Application/Coordination/LeaseManager.cs` | #28, #30 |
| `src/Meridian.Application/Coordination/SharedStorageCoordinationStore.cs` | #30 |
| `src/Meridian.Application/Coordination/SplitBrainDetector.cs` | #30 (skeleton; fill in recovery logic) |
| `src/Meridian.Application/Coordination/SubscriptionOwnershipService.cs` | #29 |
| `src/Meridian.Application/Pipeline/IDedupStore.cs` | #30 |
| `src/Meridian.Application/Services/GracefulShutdownHandler.cs` | #28 |
| `src/Meridian.Backtesting/Metrics/PostSimulationTcaReporter.cs` | #25 (extend with TCA logic) |
| `src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs` | #21 |
| `src/Meridian.Storage/Export/AnalysisExportService.cs` | #22 |
| `src/Meridian.Storage/Services/StorageCatalogService.cs` | #20 |
| `src/Meridian.Ui.Services/Collections/CircularBuffer.cs` | #23 |
| `src/Meridian.Ui.Shared/Services/SecurityMasterSecurityReferenceLookup.cs` | #21 |

#### Proposed new files — created when implementing these features

| File | Feature |
|---|---|
| `src/Meridian.Application/Coordination/RollingUpgradeOrchestrator.cs` | #28 |
| `src/Meridian.L3.Inference/Calibration/VenueCalibrationPriors.cs` | #24 |
| `src/Meridian.L3.Inference/DarkPool/DarkPoolEstimator.cs` | #27 |
| `src/Meridian.L3.Inference/DarkPool/HiddenLiquidityModel.cs` | #27 |
| `src/Meridian.L3.Inference/Simulation/DayEndQueueState.cs` | #26 |
| `src/Meridian.QuantScript/Adapters/SecurityEconomicDefinitionAdapter.cs` | #21 |
| `src/Meridian.QuantScript/Api/QuantScriptGlobals.cs` | #19, #20, #21, #22, #23 |
| `src/Meridian.QuantScript/Compilation/IScriptRunner.cs` | #19, #20, #23 |
| `src/Meridian.QuantScript/Debug/SmartPreview.cs` | #19 |
| `src/Meridian.QuantScript/Execution/ScriptRunner.cs` | #19, #20 |
| `src/Meridian.QuantScript/Export/PdfChartWriter.cs` | #22 |
| `src/Meridian.QuantScript/History/ScriptDiffEngine.cs` | #20 |
| `src/Meridian.QuantScript/ViewModels/VariableWatchViewModel.cs` | #19 |

---

_Last Updated: 2026-04-07_
