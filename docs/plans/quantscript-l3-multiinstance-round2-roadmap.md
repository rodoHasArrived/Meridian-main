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

#### Implementation Notes

- `ScriptRunRecord` persistence uses `System.Text.Json`; no new serializer dependency.
- The script content itself is stored by hash in a companion `.content/` directory
  (`{ScriptLibraryRoot}/.content/{hash}.csx`) to avoid storing duplicate full scripts.
- `StorageCatalogService` per-symbol JSONL pattern (rolling file, deterministic path) applies
  here directly — the `.history/` writer reuses the same `JsonlAppendWriter` helper.
- Diff view is a WPF `Grid` with two `AvalonEdit` instances in read-only mode; differing lines
  are highlighted via `IBackgroundRenderer`.

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

#### Implementation Notes

- `ISecurityMasterQueryService` already exists at
  `src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs` — no interface
  changes required.
- DI wiring: `QuantScriptModule` registers `SecurityGlobal` as a scoped service,
  receiving `ISecurityMasterQueryService` via constructor injection.
- The in-memory projection cache must be warmed before the first script run.
  `QuantScriptStartupInitializer` awaits `ISecurityMasterQueryService.WarmupAsync()` at
  application start.

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

#### Implementation Notes

- `ExportGlobal` wraps a `List<IPendingExport>` that accumulates export requests during script
  execution, then flushes when the run completes or when `Export.Charts.Pdf(...)` is called with
  `flush: true`.
- `PdfChartWriter` uses a 1-chart-per-page layout for v1; multi-chart grid layout is v2.
- Paths passed to `Export.*` are resolved relative to `QuantScriptOptions.ExportOutputRoot`
  if they are relative paths.

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

#### Implementation Notes

- `ReplViewModel` is a nested ViewModel on `QuantScriptViewModel`:
  `public ReplViewModel Repl { get; }`.
- The toggle shortcut (`Ctrl+``) is registered in the QuantScript page's
  `KeyBindings` collection in XAML.
- `ReplEntry` record: `{ string Source, string Result, bool IsError, DateTimeOffset Timestamp }`.
- Exception output is caught and displayed as an error-styled entry (red foreground in the
  output list) without crashing the session.

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

#### Implementation Notes

- `VenueCalibrationPriors` is a record in `src/Meridian.L3.Inference/Calibration/VenueCalibrationPriors.cs`.
- `VenueProfileResolver` resolves `"Auto"` at calibration time, not at model load time, to
  ensure it uses the symbol's runtime-resolved exchange code.
- Priors are injected into the MLE optimizer as regularization terms (Gaussian prior on each
  parameter), not as hard constraints — the posterior can deviate from the prior given
  sufficient data.

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

#### Implementation Notes

- Spread cost is computed as `(ask - bid) / 2` at the time of fill using the BBO snapshot from
  `fill-tape.jsonl`.
- Timing cost is arrival mid minus fill price.
- Market impact is estimated as the VWAP shortfall beyond timing cost (requires VWAP from
  `summary.json` VWAP field).
- Outlier threshold: `|slippageBps| > 3 × median(|slippageBps|)` across all orders.
- `BacktestMetricsEngine` already computes VWAP — reuse its output rather than recomputing.

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

#### Implementation Notes

- `DayEndQueueState` is a record in
  `src/Meridian.L3.Inference/Simulation/DayEndQueueState.cs`.
- `InferenceEngine.OnSessionCloseAsync()` calls `IQueueStatePersister.WriteAsync(state)`.
- `InferenceEngine.OnSessionOpenAsync()` calls `IQueueStatePersister.ReadAsync()`, applies
  decay, and injects into `QueueModel.InitialState`.
- When `day-state.json` is absent (first day of a carry-state run), `InferenceEngine` silently
  falls back to the default cold-start prior.

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

#### Implementation Notes

- `BinaryVersion` is added to the heartbeat record (`LeaseRecord`) and populated from
  `Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()`.
- The 30-second subscription acknowledgment timeout is configurable via
  `ClusterOptions.UpgradeSubscriptionAckTimeoutSeconds`.
- If acknowledgment times out, the upgrade is aborted and a `PUT /api/cluster/upgrade/cancel`
  endpoint resets all `"pre-reassign"` flags.
- Upgrade state is persisted in `ICoordinationStore` under key `cluster/upgrade-state` so it
  survives coordinator restart.

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

#### Implementation Notes

- Affinity resolution happens at rebalance time, not at subscription time. If a symbol's
  preferred instance goes down, the next rebalance cycle reassigns it to a fallback instance.
- `SubscriptionOwnershipService` is extended with `ITimezoneAffinityResolver`, injected as an
  optional dependency (null = affinity disabled).
- Affinity ties (multiple preferred instances) are broken by current symbol count. This
  implicitly load-balances within a region.

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
