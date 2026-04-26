# Research Backtest Trust and Velocity Blueprint

**Owner:** Core Team
**Audience:** Research, Desktop, Backtesting, API, and Architecture contributors
**Last Updated:** 2026-04-25
**Status:** Active focused blueprint for the next Research implementation slice; request-level batch sweeps and WPF Batch Backtest ViewModel coverage are now present, while real strategy selection, persisted sweep grouping, and stage-aware shared orchestration remain open

> Companion to:
>
> - [trading-workstation-migration-blueprint.md](trading-workstation-migration-blueprint.md)
> - [backtest-studio-unification-blueprint.md](backtest-studio-unification-blueprint.md)

---

## Summary

This blueprint defines the next implementation slice for Meridian's Research workspace.

The slice is intentionally narrower than full Backtest Studio unification. Its goal is to turn the current Research shell, backtest launcher, and batch backtest page into a workflow that researchers can trust and use repeatedly without falling back to ad hoc manual steps. As of 2026-04-25, the Batch Backtest page is no longer only a static demo: request-level parameter sweeps, progress/cancellation handling, result metric projection, and ViewModel tests are present. The remaining Research roadmap gap is to connect those controls to real strategy selection, persisted sweep grouping, and the shared run model.

The slice delivers three user-facing outcomes:

1. A strategy-aware launcher with a real preflight gate for coverage and data trust.
2. A stage-aware run console for native Meridian backtests, backed by the shared run model instead of a WPF-only singleton path.
3. A real Parameter Lab first cut, backed by actual batch execution and persisted grouped results.

The design reuses Meridian's existing strengths:

- `StrategyRunEntry` remains the durable workflow object.
- `StrategyRunReadService` remains the main read-model seam.
- `BacktestStudioRunOrchestrator` remains the execution seam for single runs.
- `IBatchBacktestService` remains the low-level batch engine seam, but its request model must stop being demo-only.

---

## Scope

### In scope

- Replace the hard-coded `BuyAndHoldStrategy` launcher path in `BacktestViewModel` with a strategy catalog plus dynamic parameter form.
- Replace the current WPF-only coverage scan flow with an application-level preflight service that can also back workstation API endpoints.
- Surface stage-aware native backtest progress in the Research workspace and Backtest page.
- Refactor the current WPF `BacktestService` so it uses shared orchestration and persistence instead of directly constructing `BacktestEngine`.
- Turn `BatchBacktestPage` and `BatchBacktestViewModel` into a real Parameter Lab first cut using actual strategy instances and persisted sweep grouping.
- Add local workstation API routes for research preflight, active native run status, and persisted sweep detail.
- Align Research navigation metadata so the launcher, run browser, and Parameter Lab all belong to the same operator workflow.

### Out of scope

- Lean normalization and mixed-engine comparison depth.
- New fill-model research profiles beyond what is already planned in `backtest-studio-unification-blueprint.md`.
- Full notebook, QuantScript, or external research-pack orchestration.
- Live promotion redesign beyond the existing promotion workflow.
- Large-scale backtest engine rewrites unrelated to operator trust or elapsed-time visibility.

### Assumptions

- The primary operator surface for this slice is WPF.
- The retained localhost workstation API remains important for automation, Swagger, and future shared tooling, so new research contracts should not be WPF-only.
- Native Meridian backtests ship first in this slice; Lean stays on the broader unification track.
- Additive contract changes are preferred; wide breaking changes should be avoided unless the existing seam is unused or demonstrably placeholder-only.

---

## Architecture

### 1. Keep the shared run model primary

This slice should continue to use the existing shared run path:

- write path: `src/Meridian.Backtesting/BacktestStudioRunOrchestrator.cs`
- durable model: `src/Meridian.Strategies/Models/StrategyRunEntry.cs`
- read path: `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- workstation DTOs: `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`

The Backtest page should stop treating a run as a local in-memory activity and instead treat it as a persisted strategy run from the moment the user clicks `Run`.

### 2. Add a strategy catalog instead of hard-coding one strategy

The repo already has the right discovery seam in:

- `src/Meridian.Backtesting/Plugins/StrategyPluginLoader.cs`
- `src/Meridian.Backtesting.Sdk/StrategyParameterAttribute.cs`

The missing piece is an application service that turns reflection output into a stable UI contract. Add:

- `src/Meridian.Application/Backtesting/BacktestStrategyCatalogService.cs`
- `src/Meridian.Contracts/Workstation/ResearchBacktestModels.cs`

This service should:

- discover available native `IBacktestStrategy` implementations
- expose parameter metadata and default values
- instantiate a configured strategy from a selected descriptor plus user-entered parameter values

WPF should never reflect directly over strategy assemblies.

### 3. Move preflight logic out of the WPF-only service

`src/Meridian.Wpf/Services/BacktestDataAvailabilityService.cs` is useful but currently too narrow and too UI-local. It only answers file presence by month.

Create an application-level preflight service:

- `src/Meridian.Application/Backtesting/IBacktestPreflightService.cs`
- `src/Meridian.Application/Backtesting/BacktestPreflightService.cs`

This service should combine:

- coverage presence derived from the local data root
- symbol count and requested trading-window summary
- Security Master availability and unknown-symbol warnings
- obvious blocking conditions such as empty symbol universe or inverted date ranges

For this slice, the service may still reuse the file-system scan algorithm from `BacktestDataAvailabilityService`, but that logic should move into the application layer or into a shared helper consumed by both layers.

`BacktestDataAvailabilityService` should become a thin compatibility wrapper or be removed after the view models are migrated.

### 4. Refactor the WPF backtest path onto shared orchestration

`src/Meridian.Wpf/Services/BacktestService.cs` currently:

- constructs `BacktestEngine` directly
- allows only one local active run
- returns a raw `BacktestResult`
- fires WPF-only completion events

That is the wrong seam for a workstation workflow that already has `BacktestStudioRunOrchestrator`.

Refactor `BacktestService` into a WPF-facing adapter over:

- `BacktestStudioRunOrchestrator`
- `IBacktestPreflightService`
- `StrategyRunWorkspaceService`

The adapter should:

- run preflight
- submit `BacktestStudioRunRequest`
- poll `GetStatusAsync(runId)` for progress updates
- refresh the active run context after completion

It should not own persistence or instantiate the engine directly.

### 5. Extend progress from percent-only to stage-aware

`src/Meridian.Backtesting.Sdk/BacktestProgressEvent.cs` currently exposes:

- `ProgressFraction`
- `CurrentDate`
- `PortfolioValue`
- `EventsProcessed`
- `Message`
- `LiveMetrics`

That is enough for a primitive progress bar, but not enough for a trustworthy operator console.

Additive stage metadata should be introduced so the UI can distinguish:

- validating inputs
- validating coverage
- loading replay data
- adjusting corporate actions
- replaying market data
- simulating fills and portfolio changes
- computing metrics
- persisting run artifacts

The Backtest page and Research shell should display those stages explicitly rather than reducing everything to a single spinner and date string.

### 6. Turn batch backtesting into a real Parameter Lab

`src/Meridian.Wpf/ViewModels/BatchBacktestViewModel.cs` is no longer only a simulated demo: it now drives validated request-level parameter sweeps through `IBatchBacktestService`, with progress, cancellation, result metrics, and ViewModel coverage. `src/Meridian.Backtesting/BatchBacktestService.cs` also applies swept parameters to each `BacktestRequest`, but it still runs a `NoOpStrategy` rather than selected strategy definitions.

This slice should not ship more UI around placeholder strategy execution. The next step is to connect the new sweep controls to real strategy selection, run persistence, and sweep grouping.

The fix is:

- keep `IBatchBacktestService` as the low-level concurrent executor and retain the new request-parameter sweep behavior
- replace its demo request model with run definitions that carry a real strategy instance or strategy-builder result
- add an application workflow service that builds those run definitions from a selected strategy plus parameter sweep definitions
- persist each completed sweep run through the same run store, grouped by a new `SweepId`

This gives Meridian:

- a real sweep execution path
- one browser/query model for both single runs and sweep-generated runs
- easy handoff from Parameter Lab into run comparison and promotion review

### 7. Make the Research shell the coordinating surface

The Research shell already describes the right UX posture in:

- `src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml`
- `src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml.cs`

This slice should make that shell operational by adding:

- active preflight status for the currently configured launcher
- active native run stage/status card
- recent sweep summary card
- quick actions to open the current run, current sweep, latest portfolio, and latest ledger

The shell should coordinate, not duplicate, the detailed launcher and lab pages.

---

## Interfaces and Models

### New shared research contracts

Add a new file:

- `src/Meridian.Contracts/Workstation/ResearchBacktestModels.cs`

Recommended public models:

```csharp
namespace Meridian.Contracts.Workstation;

public enum BacktestRunStage : byte
{
    Queued,
    ValidatingInputs,
    ValidatingCoverage,
    LoadingData,
    AdjustingCorporateActions,
    ReplayingMarketData,
    SimulatingOrders,
    ComputingMetrics,
    PersistingRun,
    Completed
}

public enum BacktestIssueSeverity : byte
{
    Info,
    Warning,
    Blocking
}

public sealed record BacktestPreflightRequest(
    string StrategyId,
    string StrategyName,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Symbols,
    DateOnly From,
    DateOnly To,
    string DataRoot);

public sealed record BacktestPreflightIssue(
    string Code,
    BacktestIssueSeverity Severity,
    string Title,
    string Message,
    IReadOnlyList<string>? AffectedSymbols = null,
    string? SuggestedAction = null);

public sealed record BacktestCoverageMonth(
    string Symbol,
    int Year,
    int Month,
    int DaysPresent,
    int TradingDaysInMonth);

public sealed record BacktestPreflightSummary(
    bool CanRun,
    bool HasBlockingIssues,
    int RequestedSymbolCount,
    int CoveredSymbolCount,
    int TradingDaysRequested,
    int TradingDaysCovered,
    bool SecurityMasterAvailable,
    IReadOnlyList<BacktestPreflightIssue> Issues,
    IReadOnlyList<BacktestCoverageMonth> Coverage);

public sealed record BacktestStrategyDescriptor(
    string StrategyId,
    string DisplayName,
    string Source,
    string? AssemblyPath,
    IReadOnlyList<BacktestStrategyParameterDescriptor> Parameters);

public sealed record BacktestStrategyParameterDescriptor(
    string PropertyName,
    string DisplayName,
    string ParameterType,
    string? Description,
    string? DefaultValue);

public sealed record ResearchSweepRequest(
    string StrategyId,
    string StrategyName,
    string SweepId,
    BacktestRequest BaseRequest,
    IReadOnlyDictionary<string, string> FixedParameters,
    IReadOnlyList<ResearchSweepAxis> Axes,
    int MaxConcurrency);

public sealed record ResearchSweepAxis(
    string PropertyName,
    IReadOnlyList<string> Values);

public sealed record ResearchSweepSummary(
    string SweepId,
    string StrategyId,
    string StrategyName,
    int TotalRuns,
    int CompletedRuns,
    int FailedRuns,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> RunIds);
```

### Additive changes to existing contracts

Modify:

- `src/Meridian.Application/Backtesting/BacktestStudioContracts.cs`
- `src/Meridian.Backtesting.Sdk/BacktestProgressEvent.cs`
- `src/Meridian.Strategies/Models/StrategyRunEntry.cs`
- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`

Recommended additive changes:

```csharp
public sealed record BacktestStudioRunStatus(
    string RunId,
    StrategyRunStatus Status,
    double Progress,
    DateTimeOffset StartedAt,
    DateTimeOffset? EstimatedCompletionAt,
    string? Message = null,
    BacktestRunStage? Stage = null);

public sealed record BacktestProgressEvent(
    double ProgressFraction,
    DateOnly CurrentDate,
    decimal PortfolioValue,
    long EventsProcessed,
    string? Message = null,
    IntermediateMetrics? LiveMetrics = null,
    BacktestRunStage Stage = BacktestRunStage.ReplayingMarketData);
```

Additive durable grouping on runs:

```csharp
public sealed record StrategyRunEntry(
    ...,
    string? SweepId = null);
```

Expose it in `StrategyRunSummary`, `StrategyRunDetail`, and comparison DTOs so Parameter Lab results can flow through existing read paths.

### New application services

Add:

- `src/Meridian.Application/Backtesting/IBacktestStrategyCatalogService.cs`
- `src/Meridian.Application/Backtesting/BacktestStrategyCatalogService.cs`
- `src/Meridian.Application/Backtesting/IBacktestPreflightService.cs`
- `src/Meridian.Application/Backtesting/BacktestPreflightService.cs`
- `src/Meridian.Application/Backtesting/IResearchSweepWorkflowService.cs`
- `src/Meridian.Application/Backtesting/ResearchSweepWorkflowService.cs`

Recommended service contracts:

```csharp
public interface IBacktestStrategyCatalogService
{
    Task<IReadOnlyList<BacktestStrategyDescriptor>> ListAsync(CancellationToken ct);
    IBacktestStrategy CreateStrategy(
        string strategyId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct);
}

public interface IBacktestPreflightService
{
    Task<BacktestPreflightSummary> EvaluateAsync(BacktestPreflightRequest request, CancellationToken ct);
}

public interface IResearchSweepWorkflowService
{
    Task<ResearchSweepSummary> RunAsync(
        ResearchSweepRequest request,
        IProgress<BatchBacktestProgress>? progress,
        CancellationToken ct);
}
```

---

## Exact Write Set

### Primary files to modify

| Path | Change | Why |
| ------ | -------- | ----- |
| `src/Meridian.Wpf/ViewModels/BacktestViewModel.cs` | Replace hard-coded strategy path, bind preflight, switch to orchestrator-backed run lifecycle | Main Research launcher seam |
| `src/Meridian.Wpf/Views/BacktestPage.xaml` | Add strategy picker, dynamic parameter region, preflight card, stage console | Main operator surface |
| `src/Meridian.Wpf/Services/BacktestService.cs` | Refactor into orchestrator-backed adapter | Remove local-engine-only workflow |
| `src/Meridian.Wpf/ViewModels/BatchBacktestViewModel.cs` | Connect current request-sweep execution to strategy selection and persisted sweep grouping | Request-level sweep UI exists; strategy/run persistence remains open |
| `src/Meridian.Wpf/Views/BatchBacktestPage.xaml` | Present sweep controls and persisted results | Parameter Lab surface |
| `src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml` | Add active run stage and recent sweep widgets | Shell coordination |
| `src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml.cs` | Load preflight/run/sweep summary from services | Shell behavior |
| `src/Meridian.Backtesting/BatchBacktestService.cs` | Accept real run definitions instead of `NoOpStrategy` | Request parameters are applied per run; selected strategy execution remains open |
| `src/Meridian.Backtesting/MeridianNativeBacktestStudioEngine.cs` | Preserve stage-aware status for polling | Progressive console support |
| `src/Meridian.Backtesting/BacktestStudioRunOrchestrator.cs` | Persist sweep metadata and expose richer status | Shared run orchestration |
| `src/Meridian.Strategies/Models/StrategyRunEntry.cs` | Add `SweepId` | Durable grouping for Parameter Lab |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | Surface sweep-aware run reads and filters | Shared read seam |
| `src/Meridian.Wpf/Services/StrategyRunWorkspaceService.cs` | Expose sweep summaries and latest active run refresh helpers | WPF workspace coordination |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | Add research preflight and sweep endpoints | Shared local API |
| `src/Meridian.Contracts/Api/UiApiRoutes.cs` | Add route constants | Route hygiene |
| `src/Meridian.Wpf/App.xaml.cs` | Register new services and retire direct engine construction in WPF | DI wiring |

### New files to add

| Path | Purpose |
| ------ | --------- |
| `src/Meridian.Contracts/Workstation/ResearchBacktestModels.cs` | Shared research contracts |
| `src/Meridian.Application/Backtesting/IBacktestStrategyCatalogService.cs` | Strategy metadata seam |
| `src/Meridian.Application/Backtesting/BacktestStrategyCatalogService.cs` | Strategy catalog implementation |
| `src/Meridian.Application/Backtesting/IBacktestPreflightService.cs` | Preflight service contract |
| `src/Meridian.Application/Backtesting/BacktestPreflightService.cs` | Preflight implementation |
| `src/Meridian.Application/Backtesting/IResearchSweepWorkflowService.cs` | Parameter Lab workflow seam |
| `src/Meridian.Application/Backtesting/ResearchSweepWorkflowService.cs` | Sweep execution and persistence |

### Secondary cleanup files

| Path | Cleanup |
| ------ | --------- |
| `src/Meridian.Wpf/Services/BacktestDataAvailabilityService.cs` | convert to adapter or remove after migration |
| `src/Meridian.Wpf/Models/WorkspaceRegistry.cs` | align page ownership so Research owns backtest surfaces consistently |
| `src/Meridian.Wpf/Models/ShellNavigationCatalog.cs` | add Parameter Lab wording and related links if needed |

---

## Data Flow

### Single native run

1. `BacktestViewModel` loads available strategies through `IBacktestStrategyCatalogService`.
2. When the user edits strategy, symbols, dates, or parameters, `BacktestViewModel` debounces a call to `IBacktestPreflightService.EvaluateAsync`.
3. The preflight result updates the left rail and enables or blocks the `Run Backtest` CTA.
4. On launch, `BacktestViewModel` creates a `BacktestStudioRunRequest` and passes it through the refactored `BacktestService`.
5. The refactored `BacktestService` calls `BacktestStudioRunOrchestrator.StartAsync`.
6. `BacktestStudioRunOrchestrator` records the initial `StrategyRunEntry`.
7. `MeridianNativeBacktestStudioEngine` executes the run and updates stage-aware status.
8. `BacktestService` polls `GetStatusAsync(runId)` and pushes stage/status changes back to `BacktestViewModel`.
9. On completion, the orchestrator persists the canonical result on the shared run entry.
10. `StrategyRunWorkspaceService` refreshes active run context and the Research shell updates KPI, portfolio, ledger, and compare affordances from the persisted run.

### Parameter Lab sweep

1. `BatchBacktestViewModel` loads the same strategy catalog and parameter metadata used by `BacktestViewModel`.
2. The user defines sweep axes and fixed parameters.
3. `IResearchSweepWorkflowService` expands those definitions into concrete run definitions.
4. `ResearchSweepWorkflowService` calls the updated `IBatchBacktestService` with real strategy instances.
5. Each completed child run is persisted with a shared `SweepId`.
6. `BatchBacktestViewModel` refreshes grouped results from `StrategyRunWorkspaceService` and `StrategyRunReadService`.
7. The user opens any winning run directly into the existing `RunDetail`, `RunPortfolio`, or `RunLedger` pages.

### Workstation API

Add routes under `WorkstationEndpoints`:

- `POST /api/workstation/research/preflight`
- `GET /api/workstation/research/runs/{runId}/status`
- `POST /api/workstation/research/sweeps`
- `GET /api/workstation/research/sweeps/{sweepId}`

These routes should use the same application services as WPF, not duplicate run logic in endpoint code.

---

## Rollout Slices

### Slice 1: Strategy-aware launcher and preflight

Primary write set:

- `BacktestViewModel.cs`
- `BacktestPage.xaml`
- `BacktestDataAvailabilityService.cs`
- new strategy catalog and preflight services
- `App.xaml.cs`

Exit criteria:

- user can choose a real strategy instead of the fixed `Buy & Hold` path
- parameter fields render from strategy metadata
- the page shows blocking vs warning preflight issues before launch

### Slice 2: Stage-aware native run console

Primary write set:

- `BacktestService.cs`
- `BacktestStudioRunOrchestrator.cs`
- `MeridianNativeBacktestStudioEngine.cs`
- `BacktestStudioContracts.cs`
- `BacktestProgressEvent.cs`
- `ResearchWorkspaceShellPage.xaml` and `.xaml.cs`

Exit criteria:

- launching from WPF creates a shared run immediately
- the run page shows explicit stages during execution
- shell KPIs and inspector shortcuts update from the persisted run

### Slice 3: Parameter Lab first cut

Primary write set:

- `BatchBacktestService.cs`
- `BatchBacktestViewModel.cs`
- `BatchBacktestPage.xaml`
- `StrategyRunEntry.cs`
- `StrategyRunReadService.cs`
- `StrategyRunWorkspaceService.cs`
- `WorkstationEndpoints.cs`

Exit criteria:

- batch execution uses real strategies
- results are grouped by `SweepId`
- the user can open any sweep child run in existing run drill-ins

---

## Edge Cases and Risks

### 1. Strategy metadata may be incomplete

Some `IBacktestStrategy` implementations may not use `StrategyParameterAttribute`.

Mitigation:

- support zero-parameter strategies cleanly
- render only attributed properties in the first slice
- show a clear empty-state message instead of reflecting every public property

### 2. Preflight can become too expensive if it scans the full tree repeatedly

Mitigation:

- keep the existing cache concept from `BacktestDataAvailabilityService`
- key the cache by normalized symbols, dates, and data root
- debounce UI-triggered preflight requests

### 3. WPF may show stale run status if orchestration polling is sloppy

Mitigation:

- centralize polling in the refactored `BacktestService`
- stop polling when the run reaches a terminal state
- on terminal state, reload detail from `StrategyRunWorkspaceService` rather than trusting cached status

### 4. Batch persistence can pollute normal run browsing

Mitigation:

- expose `SweepId` explicitly
- add default browser filtering or badges for sweep-generated runs
- keep grouped sweep detail available without hiding the underlying child runs

### 5. Navigation ownership is currently inconsistent

`ShellNavigationCatalog` already treats Backtest and Batch Backtest as Research pages, but `WorkspaceRegistry` still places `Backtest` under Trading.

Mitigation:

- fix that in the same slice so workspace ownership matches the operator model

### 6. Breaking changes

Expected repo-local breaking change:

- `BatchBacktestService` request/response contracts may need to evolve from request-parameter-grid semantics to real run definitions. This is acceptable if the strategy-selection slice keeps current request-sweep behavior compatible for the WPF Parameter Lab.

Avoided breaking change:

- keep `IBacktestStudioEngine` shape intact for this slice by polling `GetStatusAsync` instead of adding a new streaming method right now

---

## Test Plan

### Backtesting tests

Extend or add:

- `tests/Meridian.Backtesting.Tests/MeridianNativeBacktestStudioEngineTests.cs`
- `tests/Meridian.Backtesting.Tests/BacktestRequestConfigTests.cs`
- new `tests/Meridian.Backtesting.Tests/BatchBacktestServiceTests.cs`

Coverage goals:

- stage metadata is populated on native progress
- batch runs use the supplied strategy definitions, not `NoOpStrategy`
- batch failures stay isolated per run

### Application and run-store tests

Extend or add:

- `tests/Meridian.Tests/Application/Backtesting/BacktestStudioRunOrchestratorTests.cs`
- new `tests/Meridian.Tests/Application/Backtesting/BacktestPreflightServiceTests.cs`
- `tests/Meridian.Tests/Strategies/StrategyRunReadServiceTests.cs`
- `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs`

Coverage goals:

- preflight correctly distinguishes blocking vs warning issues
- `SweepId` persists and round-trips through read models
- workstation research endpoints return the same contracts WPF uses

### WPF tests

Add:

- new `tests/Meridian.Wpf.Tests/ViewModels/BacktestViewModelTests.cs`
- new `tests/Meridian.Wpf.Tests/ViewModels/BatchBacktestViewModelTests.cs`
- extend `tests/Meridian.Wpf.Tests/Services/StrategyRunWorkspaceServiceTests.cs`

Coverage goals:

- preflight debouncing and CTA enablement
- stage console text updates correctly
- completed sweep rows open into the expected run drill-ins

### Validation commands

Use the narrowest matching commands:

```bash
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release /p:EnableWindowsTargeting=true
```

---

## Open Questions

1. Should the first strategy catalog implementation only support assembly-backed strategies, or should it also surface QuantScript-authored strategies if they already compile into `IBacktestStrategy` instances?
2. Should preflight warnings be persisted on `StrategyRunEntry`, or remain launch-time only in this slice?
3. Should sweep child runs appear in the main run browser by default, or only through a sweep-aware filter/view?
4. Do we want the first Parameter Lab cut to include a heatmap immediately, or should it ship as a sweep grid plus compare/open actions first?
5. Should `BacktestService` remain as a compatibility façade name, or should we rename it to `BacktestStudioClientService` once callers are migrated?

---

## Recommended First PR

Start with a trust-building slice, not the full Parameter Lab:

1. Add `ResearchBacktestModels.cs`.
2. Implement `IBacktestStrategyCatalogService`.
3. Implement `IBacktestPreflightService`.
4. Refactor `BacktestViewModel` and `BacktestPage.xaml` to use those services.
5. Register the new services in `App.xaml.cs`.

That PR gives Meridian an immediately better Research workflow without blocking on batch execution or broader mixed-engine work.
