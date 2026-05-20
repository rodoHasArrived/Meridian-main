# Covered Call Writing — Slice 1 (Backtest UI) Blueprint


## TODO Checklist (Concrete Implementation Items)
- [ ] Define scope boundaries for **covered call writing slice 1 blueprint** and document explicit in-scope vs out-of-scope items.
- [ ] Break delivery into PR-sized milestones with owner, dependency, and evidence artifact for each milestone.
- [ ] Implement the first milestone in code/config/scripts and link the exact validating test or command output.
- [ ] Add/update operator runbook steps and rollback procedure for the covered call writing slice 1 blueprint workflow.
- [ ] Record completion evidence in `docs/status/` (or linked packet) and mark corresponding checklist items done.

> **Depth Mode:** full
> **Pipeline stage:** Blueprint (from prioritized idea → code-ready design)
> **Branch:** `claude/covered-call-writing-L9kAs`
> **Related plans:** [`docs/plans/options-roadmap.md`](options-roadmap.md),
> [`docs/plans/backtest-studio-unification-blueprint.md`](backtest-studio-unification-blueprint.md)

This blueprint translates the prioritized idea **"begin covered call writing in the browser
workstation"** into a code-ready design for the first vertical slice: backtest-only,
non-executing, single underlying, single short-call leg.

It deliberately reuses the production-ready
`Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.CoveredCallOverwriteStrategy` rather than
introducing a new strategy class. Slice 1 wires that strategy into a new `Strategy → Covered
Call` workstation page, persists each run through `StrategyRunStore`, and renders results
(equity curve, position timeline, single-leg payoff diagram, metrics).

Paper execution, live execution, multi-leg orders, OMS changes, broker routing of options, and
the general spread builder are **explicitly deferred to later slices**.

---

## 1. Scope

**In Scope**
- New backend service `CoveredCallBacktestService` that adapts `CoveredCallOverwriteStrategy`,
  runs `BacktestEngine.RunAsync`, and persists results through `IStrategyRepository`.
- New REST surface under `/api/strategies/covered-call/...` (DTOs, SG-JSON context).
- New chain-provider adapter that bridges `Meridian.ProviderSdk.IOptionsChainProvider`
  (production data) → `Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.IOptionChainProvider`
  (strategy contract). The strategy is unchanged.
- New browser-workstation route `/strategy/covered-call`, screen file
  `src/Meridian.Ui/dashboard/src/screens/covered-call-screen.tsx`, view-model, three sub-views
  (Configure, Run, Results).
- Reuse of the existing single-leg payoff math; no new options pricing code.
- xUnit + Vitest test coverage for service, endpoints, view-model, and at least one render
  smoke test of each sub-view.

**Out of Scope (later slices)**
- Paper or live execution of any covered-call leg.
- Multi-leg / combo order types in `Meridian.Execution.Sdk`.
- Broker-side options routing or assignment confirmations.
- Drag-drop or arbitrary spread designer.
- Greeks surface visualization beyond the selected short call.
- IV-percentile sourcing (slice 1 uses the value supplied by the chain provider; if absent the
  candidate is rejected by `OptionsOverwriteFilters`).

**Assumptions**
- The operator already has at least one configured `IOptionsChainProvider`
  (`PolygonOptionsChainProvider`, `RobinhoodOptionsChainProvider`,
  `AlpacaOptionsChainProvider`, or `SyntheticOptionsChainProvider`) available via DI.
- Historical equity bars for the chosen underlying are present at `request.DataRoot` so
  `BacktestEngine.RunAsync` can stream `OnBar` events.
- `StrategyRunStore` (the `IStrategyRepository` implementation under
  `src/Meridian.Strategies/Storage/`) is registered in the workstation host.
- `OptionsOverwriteParams.MinStrike` is a required user input; the operator is expected to
  supply it. The UI must surface the "no meaningful default" semantic from the parameter
  attribute.

**Depth Mode:** full.

**No breaking changes.** All additions are net-new types in net-new files. The covered-call
strategy, the SDK `IOptionChainProvider` interface, and the production `IOptionsChainProvider`
interface are unchanged.

---

## 2. Architectural Overview

### Context Diagram

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Browser workstation  (src/Meridian.Ui/dashboard)                            │
│                                                                              │
│   /strategy/covered-call                                                     │
│   ├─ covered-call-screen.tsx          (3-step wizard shell)                  │
│   ├─ covered-call-screen.view-model.ts                                       │
│   ├─ components/covered-call/                                                │
│   │   ├─ configure-form.tsx           (inputs + chain preview)               │
│   │   ├─ run-progress.tsx             (poll status, cancel)                  │
│   │   ├─ results/equity-curve.tsx                                            │
│   │   ├─ results/position-timeline.tsx                                       │
│   │   ├─ results/payoff-diagram.tsx   (selected short call only)             │
│   │   └─ results/metrics-table.tsx                                           │
│   └─ lib/api.ts  (add covered-call client functions)                         │
│                                                                              │
└──────────────────────┬───────────────────────────────────────────────────────┘
                       │  HTTPS  /api/strategies/covered-call/...
                       ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  Workstation host  (src/Meridian.Ui.Shared/Endpoints)                        │
│                                                                              │
│   CoveredCallEndpoints     (new)        StrategyLifecycleEndpoints (exists)  │
│       │                                          ▲                           │
│       │ DI                                       │ shared StrategyRunStore   │
│       ▼                                          │                           │
│  ┌────────────────────────────────────────────────────────────────────────┐  │
│  │  Meridian.Application.Services                                         │  │
│  │   ├─ ICoveredCallBacktestService  (new)                                │  │
│  │   ├─ CoveredCallBacktestService   (new — orchestrates)                 │  │
│  │   ├─ OptionsChainService          (existing — chain access)            │  │
│  │   └─ CoveredCallChainProviderAdapter (new — SDK <-> Provider bridge)   │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
│        │                              │                                       │
│        ▼                              ▼                                       │
│  BacktestEngine                IOptionsChainProvider (Meridian.ProviderSdk)  │
│  (Meridian.Backtesting)              ▲                                        │
│        │                              │ implementations                       │
│        ▼                              │                                       │
│  CoveredCallOverwriteStrategy   PolygonOptionsChainProvider                  │
│  (Meridian.Backtesting.Sdk)     RobinhoodOptionsChainProvider                │
│  ─ unchanged                    AlpacaOptionsChainProvider                   │
│                                  SyntheticOptionsChainProvider               │
│                                                                              │
│  Run persistence: IStrategyRepository → StrategyRunStore                     │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Design Decisions

- **Decision:** Reuse `CoveredCallOverwriteStrategy` as-is; do **not** subclass or modify it.
  **Alternatives Considered:** (a) Wrap it in a new `IStrategyLifecycle` adapter; (b) clone and
  fork for the UI lane.
  **Rationale:** The strategy is already production-tested, supplies `Metrics`,
  `CompletedTrades`, and `OpenPositions`, and is contract-compatible with `BacktestEngine`.
  Subclassing risks behavior drift; forking duplicates well-tested code.
  **Consequences:** Slice 1 must stay backtest-only — the strategy does not implement
  `IStrategyLifecycle` and therefore is not eligible for paper/live until a later slice adds
  an `ILiveStrategy` covered-call wrapper.

- **Decision:** Run the backtest **in-process, asynchronously, with a `Channel`-backed job
  queue**. Status is polled by the UI via GET on a run ID.
  **Alternatives Considered:** Synchronous request/response (rejected — backtests can take
  seconds to minutes); reuse the Lean backtest endpoints (rejected — Lean is a separate
  engine, see `UiApiRoutes.LeanBacktest*`; covered-call uses Meridian's native engine).
  **Rationale:** Matches the existing async-job pattern used elsewhere (backfill, ingestion).
  **Consequences:** Need a small in-process job registry plus a cancellation-token store.
  Use `EventPipelinePolicy.Default.CreateChannel<CoveredCallBacktestCommand>()` per ADR-013.

- **Decision:** Persist each completed run through `IStrategyRepository` with
  `StrategyId = "covered-call-overwrite"` and a synthetic `RunId` shared with the API caller.
  **Alternatives Considered:** Bespoke `CoveredCallRunStore`.
  **Rationale:** `StrategyRunStore` already provides query/list/recall semantics that
  `StrategyRunReadService` and the `/api/strategies/...` endpoints surface. Reusing it gets
  the run into the existing run library for free.
  **Consequences:** `OptionsOverwriteMetrics` and equity/trade payloads must be serialized
  into the `StrategyRunEntry.ScopeMetadata` (or an analogous JSON blob) — confirmed acceptable
  by `StrategyRunScopeMetadataResolver` patterns.

- **Decision:** Bridge `IOptionsChainProvider` (production async, multi-provider) →
  `IOptionChainProvider` (synchronous, single-call, used by the strategy) inside the host
  rather than on the strategy side. The adapter caches per-date chain snapshots into an
  in-memory `IReadOnlyList<OptionCandidateInfo>` so the strategy's per-day `GetCalls(...)`
  call is non-blocking.
  **Alternatives Considered:** Change `IOptionChainProvider` to be async (rejected — touches
  every strategy + every test in the SDK).
  **Rationale:** Slice 1 only needs a one-direction adapter; the cost-and-blast-radius for
  async-ification of the SDK interface is too large for a UI-wiring slice.
  **Consequences:** Chain snapshots are fetched eagerly at the start of the backtest for the
  expirations within `[From - 1 day, To + MaxDte days]`. Memory cost is bounded by chain size.

- **Decision:** Single-leg payoff diagram is computed **client-side** from the selected short
  call's strike, multiplier, entry credit, and underlying spot. No new server endpoint.
  **Rationale:** It is a closed-form one-liner — `payoff(S) = credit − max(S − strike, 0)` ×
  contracts × multiplier — and removes one server round-trip per slider movement.
  **Consequences:** Payoff math lives in `dashboard/src/lib/covered-call/payoff.ts`. Backend
  is unchanged.

- **Decision:** Source-generated JSON for every new DTO via a new
  `CoveredCallJsonContext : JsonSerializerContext` in `src/Meridian.Ui.Shared/Serialization/`.
  **Rationale:** ADR-014. Mirrors `DirectLendingJsonContext.cs`.

---

## 3. Interface & API Contracts

### New Interfaces (C#)

```csharp
// File: src/Meridian.Application/Services/ICoveredCallBacktestService.cs
namespace Meridian.Application.Services;

/// <summary>
/// Orchestrates covered-call backtests for the browser workstation:
/// starts runs, reports progress, persists completed runs via <see cref="IStrategyRepository"/>,
/// and projects results into UI read-models.
/// </summary>
public interface ICoveredCallBacktestService
{
    /// <summary>Starts a new backtest run. Returns immediately with the assigned <c>RunId</c>.</summary>
    ValueTask<CoveredCallRunHandle> StartAsync(CoveredCallBacktestRequest request, CancellationToken ct = default);

    /// <summary>Returns the current status (queued, running, succeeded, failed, cancelled).</summary>
    ValueTask<CoveredCallRunStatus> GetStatusAsync(string runId, CancellationToken ct = default);

    /// <summary>Returns the completed result, or <c>null</c> if the run is not yet finished.</summary>
    ValueTask<CoveredCallRunResult?> GetResultAsync(string runId, CancellationToken ct = default);

    /// <summary>Lists prior covered-call runs (most recent first).</summary>
    ValueTask<IReadOnlyList<CoveredCallRunSummary>> ListRunsAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>Cancels a queued or running backtest. Idempotent.</summary>
    ValueTask CancelAsync(string runId, CancellationToken ct = default);

    /// <summary>Returns the chain snapshot that would be evaluated for <c>scanDate</c> — for the configure-form preview.</summary>
    ValueTask<CoveredCallChainPreview> PreviewChainAsync(CoveredCallChainPreviewRequest request, CancellationToken ct = default);
}
```

```csharp
// File: src/Meridian.Application/Services/ICoveredCallChainProviderFactory.cs
namespace Meridian.Application.Services;

/// <summary>
/// Builds the SDK-level <see cref="IOptionChainProvider"/> the strategy expects from the
/// production-level <see cref="IOptionsChainProvider"/> by eagerly snapshotting every needed
/// scan date for a single backtest run. One factory call per backtest.
/// </summary>
public interface ICoveredCallChainProviderFactory
{
    ValueTask<IOptionChainProvider> CreateAsync(
        string underlyingSymbol,
        DateOnly from,
        DateOnly to,
        int maxDte,
        CancellationToken ct = default);
}
```

### New Sealed Classes

| Class | File | Lifetime | Implements |
|-------|------|----------|------------|
| `CoveredCallBacktestService` | `src/Meridian.Application/Services/CoveredCallBacktestService.cs` | Singleton + `IHostedService` (drains the command channel) | `ICoveredCallBacktestService` |
| `CoveredCallChainProviderFactory` | `src/Meridian.Application/Services/CoveredCallChainProviderFactory.cs` | Singleton | `ICoveredCallChainProviderFactory` |
| `CoveredCallChainProviderAdapter` | `src/Meridian.Application/Services/CoveredCallChainProviderAdapter.cs` | Transient (one per run) | `Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.IOptionChainProvider` |
| `CoveredCallRunProjection` | `src/Meridian.Application/Services/CoveredCallRunProjection.cs` | Static helper | n/a |
| `CoveredCallJsonContext` | `src/Meridian.Ui.Shared/Serialization/CoveredCallJsonContext.cs` | `JsonSerializerContext` | n/a |
| `CoveredCallEndpoints` | `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs` | static class | n/a |

### Request / Response DTOs

```csharp
// File: src/Meridian.Ui.Shared/Contracts/CoveredCallContracts.cs
namespace Meridian.Ui.Shared.Contracts;

/// <summary>Operator-supplied parameters for a covered-call backtest.</summary>
public sealed record CoveredCallBacktestRequest(
    string UnderlyingSymbol,
    DateOnly From,
    DateOnly To,
    decimal MinStrike,
    double OverwriteRatio = 0.75,
    double MaxDelta = 0.35,
    int MinDte = 7,
    int? MaxDte = 60,
    double MinIvPercentile = 50.0,
    long MinOpenInterest = 1_000,
    long MinVolume = 100,
    double MaxSpreadPct = 0.05,
    double TakeProfitCapture = 0.80,
    double RollDelta = 0.55,
    int ExDivWindowDays = 7,
    OverwriteScoringModeDto ScoringMode = OverwriteScoringModeDto.Relative,
    double DepthBonusWeight = 0.05,
    double RiskFreeRate = 0.04,
    decimal InitialCash = 100_000m,
    long InitialUnderlyingShares = 100,
    string? Label = null);

/// <summary>Returned by <c>POST /start</c>.</summary>
public sealed record CoveredCallRunHandle(string RunId, DateTimeOffset QueuedAt);

/// <summary>Polled status for an in-flight run.</summary>
public sealed record CoveredCallRunStatus(
    string RunId,
    string Phase,                 // "Queued" | "WarmingUp" | "Running" | "Completed" | "Failed" | "Cancelled"
    double PercentComplete,
    DateOnly? CurrentBacktestDate,
    string? FailureMessage);

/// <summary>Full results for a completed run.</summary>
public sealed record CoveredCallRunResult(
    string RunId,
    string UnderlyingSymbol,
    DateOnly From,
    DateOnly To,
    CoveredCallMetricsDto Metrics,
    IReadOnlyList<CoveredCallEquityPoint> EquityCurve,
    IReadOnlyList<CoveredCallTradeDto> Trades,
    IReadOnlyList<CoveredCallOpenPositionDto> OpenPositionsAtEnd);

public sealed record CoveredCallRunSummary(
    string RunId,
    string UnderlyingSymbol,
    DateOnly From,
    DateOnly To,
    string? Label,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double? Cagr,
    double? SharpeRatio,
    double? WinRate);

public sealed record CoveredCallMetricsDto(
    double Cagr, double AnnualizedVolatility, double SharpeRatio, double SortinoRatio,
    double CalmarRatio, double MaxDrawdownPct, double WinRate, double AssignmentRate,
    double AverageHoldingDays, int TotalOptionTrades, int AssignedTrades,
    decimal TotalPremiumCollected, decimal TotalOptionPnl,
    double UpCapture, double DownCapture,
    double MonthlyVar1Pct, double MonthlyVar5Pct, double MonthlyCVar5Pct,
    double ReturnSkewness, double ReturnKurtosis, double AnnualizedTurnover);

public sealed record CoveredCallEquityPoint(DateOnly Date, decimal StrategyEquity, decimal UnderlyingEquity);

public sealed record CoveredCallTradeDto(
    decimal Strike, DateOnly Expiration, int Contracts, int Multiplier,
    DateOnly EntryDate, decimal EntryCredit, DateOnly ExitDate, decimal ExitDebit,
    string ExitReason, double? EntryImpliedVolatility,
    decimal NetPnlPerContract, decimal TotalNetPnl, int HoldingDays);

public sealed record CoveredCallOpenPositionDto(
    Guid PositionId, decimal Strike, DateOnly Expiration, int Contracts, int Multiplier,
    DateOnly EntryDate, decimal EntryCredit, decimal MarkToClose,
    double CurrentDelta, int CurrentDte, decimal UnrealisedPnl, double PremiumCaptured);

public sealed record CoveredCallChainPreviewRequest(
    string UnderlyingSymbol, DateOnly AsOf, decimal MinStrike,
    double MaxDelta, int MinDte, int? MaxDte, long MinOpenInterest, long MinVolume, double MaxSpreadPct);

public sealed record CoveredCallChainPreview(
    string UnderlyingSymbol, DateOnly AsOf, decimal UnderlyingPrice,
    IReadOnlyList<CoveredCallChainRow> Candidates,
    int TotalContractsScanned, int FiltersPassed);

public sealed record CoveredCallChainRow(
    decimal Strike, DateOnly Expiration, int DaysToExpiration,
    decimal Bid, decimal Ask, double Delta, double? ImpliedVolatility,
    long OpenInterest, long Volume,
    bool MeetsAllFilters, string? RejectReason);

public enum OverwriteScoringModeDto { Basic, Relative }
```

> The DTOs deliberately exclude internal types (`ShortCallPosition`, `OptionCandidateInfo`) so
> changes to the strategy's internal state shapes don't leak through the API.
> `CoveredCallRunProjection.ToDto(...)` is the single mapper.

### SG-JSON Context

```csharp
// File: src/Meridian.Ui.Shared/Serialization/CoveredCallJsonContext.cs
[ImplementsAdr("ADR-014", "Source-generated JSON for covered-call endpoint payloads")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CoveredCallBacktestRequest))]
[JsonSerializable(typeof(CoveredCallRunHandle))]
[JsonSerializable(typeof(CoveredCallRunStatus))]
[JsonSerializable(typeof(CoveredCallRunResult))]
[JsonSerializable(typeof(CoveredCallRunSummary))]
[JsonSerializable(typeof(IReadOnlyList<CoveredCallRunSummary>))]
[JsonSerializable(typeof(CoveredCallChainPreviewRequest))]
[JsonSerializable(typeof(CoveredCallChainPreview))]
[JsonSerializable(typeof(ProblemDetailsDto))]
internal sealed partial class CoveredCallJsonContext : JsonSerializerContext;
```

### Configuration Schema

```csharp
// File: src/Meridian.Application/Options/CoveredCallBacktestOptions.cs
public sealed class CoveredCallBacktestOptions
{
    public const string SectionName = "Strategies:CoveredCall";

    /// <summary>Soft cap on simultaneous in-flight covered-call runs (default: 3).</summary>
    public int MaxConcurrentRuns { get; init; } = 3;

    /// <summary>How long a completed run's full result is cached in memory before re-hydration from storage (default: 30 min).</summary>
    public TimeSpan ResultCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Max strikes per expiration to materialise from the production chain provider when adapting (default: 80).</summary>
    public int MaxStrikesPerExpiration { get; init; } = 80;

    /// <summary>Optional override of the underlying-bar `DataRoot` for the backtest engine.</summary>
    public string? DataRootOverride { get; init; }
}
```

```jsonc
// appsettings.json
{
  "Strategies": {
    "CoveredCall": {
      "MaxConcurrentRuns": 3,
      "ResultCacheDuration": "00:30:00",
      "MaxStrikesPerExpiration": 80
    }
  }
}
```

### REST Surface

All routes registered under `var group = app.MapGroup("/api/strategies/covered-call").WithTags("Strategies.CoveredCall");`.
Route constants added to `Meridian.Contracts.Api.UiApiRoutes`:

```csharp
// File: src/Meridian.Contracts/Api/UiApiRoutes.cs (appended)
public const string CoveredCallStart        = "/api/strategies/covered-call/runs";
public const string CoveredCallStatus       = "/api/strategies/covered-call/runs/{runId}/status";
public const string CoveredCallResult       = "/api/strategies/covered-call/runs/{runId}/result";
public const string CoveredCallList         = "/api/strategies/covered-call/runs";
public const string CoveredCallCancel       = "/api/strategies/covered-call/runs/{runId}/cancel";
public const string CoveredCallChainPreview = "/api/strategies/covered-call/chain-preview";
```

```
POST   /api/strategies/covered-call/runs           Body: CoveredCallBacktestRequest
                                                   200: CoveredCallRunHandle
                                                   400: ProblemDetails (validation)
                                                   429: rate-limited

GET    /api/strategies/covered-call/runs           Query: ?limit=N
                                                   200: CoveredCallRunSummary[]

GET    /api/strategies/covered-call/runs/{id}/status
                                                   200: CoveredCallRunStatus
                                                   404: unknown runId

GET    /api/strategies/covered-call/runs/{id}/result
                                                   200: CoveredCallRunResult
                                                   404: unknown runId
                                                   409: still running

POST   /api/strategies/covered-call/runs/{id}/cancel
                                                   200: CoveredCallRunStatus
                                                   404: unknown runId

POST   /api/strategies/covered-call/chain-preview  Body: CoveredCallChainPreviewRequest
                                                   200: CoveredCallChainPreview
                                                   503: no options chain provider configured
```

All mutation routes carry `.RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)` per the
existing pattern in `StrategyLifecycleEndpoints`.

---

## 4. Component Design

### CoveredCallBacktestService

**Namespace:** `Meridian.Application.Services`
**Type:** `sealed class CoveredCallBacktestService : ICoveredCallBacktestService, IHostedService, IAsyncDisposable`
**Lifetime:** Singleton

**Responsibilities**
- Accept `CoveredCallBacktestRequest` and enqueue a `CoveredCallBacktestCommand` on the
  internal `Channel`.
- Drain the channel from a single background loop, respecting `MaxConcurrentRuns`
  (`SemaphoreSlim`).
- For each command: build the strategy, build the chain-provider adapter, invoke
  `BacktestEngine.RunAsync`, persist the resulting `StrategyRunEntry`, expose status snapshots.
- Cache the full `CoveredCallRunResult` for `ResultCacheDuration` (`MemoryCache` keyed by
  `runId`).
- Map `OptionsOverwriteMetrics`, `CompletedTrades`, `OpenPositions`, and the equity curve to
  the DTO surface via `CoveredCallRunProjection.ToDto`.

**Dependencies (constructor-injected)**
```csharp
public CoveredCallBacktestService(
    BacktestEngine engine,
    ICoveredCallChainProviderFactory chainFactory,
    IStrategyRepository runRepository,
    IOptionsMonitor<CoveredCallBacktestOptions> options,
    IMemoryCache resultCache,
    ILogger<CoveredCallBacktestService> logger,
    TimeProvider timeProvider);
```

**Key Internal State**
```csharp
private readonly Channel<CoveredCallBacktestCommand> _channel =
    EventPipelinePolicy.Default.CreateChannel<CoveredCallBacktestCommand>();
private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);
private readonly SemaphoreSlim _concurrency;
private readonly CancellationTokenSource _hostCts = new();
private Task? _drainLoop;

private abstract record CoveredCallBacktestCommand(string RunId);
private sealed record StartCommand(string RunId, CoveredCallBacktestRequest Request) : CoveredCallBacktestCommand(RunId);
private sealed record CancelCommand(string RunId) : CoveredCallBacktestCommand(RunId);

private sealed class RunState
{
    public required string RunId { get; init; }
    public required CoveredCallBacktestRequest Request { get; init; }
    public required CancellationTokenSource Cts { get; init; }
    public string Phase { get; set; } = "Queued";
    public double Percent { get; set; }
    public DateOnly? CurrentDate { get; set; }
    public string? Failure { get; set; }
    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
```

**Concurrency Model**
- One drain loop reads commands; `SemaphoreSlim` gates concurrent execution.
- Each run owns its own linked `CancellationTokenSource` (linked to `_hostCts.Token`). Cancel
  paths cancel that CTS, which propagates into `BacktestEngine.RunAsync`.
- `RunState` mutations are pointer-atomic writes (status string, percent, current date); the
  REST status endpoint reads them without locks — eventual consistency is acceptable for UI
  polling.

**Error Handling**
- Bad input → `ValidationException` (caught at the endpoint, translated to 400).
- Missing underlying bars → `BacktestPreflightException` from `BacktestEngine` → mapped to
  failure phase with operator-readable message.
- Unhandled provider exceptions → log with structured fields, set `RunState.Failure`,
  transition to `"Failed"`.
- `OperationCanceledException` from a deliberate cancel → transition to `"Cancelled"` (do not
  rethrow past the loop boundary).

**Hot Config Reload**
- `IOptionsMonitor<CoveredCallBacktestOptions>.OnChange` resizes the concurrency semaphore (up
  or down) at the next drain-loop iteration. Cache duration is read per-write.

### CoveredCallChainProviderAdapter

**Namespace:** `Meridian.Application.Services`
**Type:** `sealed class CoveredCallChainProviderAdapter : IOptionChainProvider` (the SDK
interface in `Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite`)
**Lifetime:** Transient — one per backtest run

**Responsibilities**
- Hold the eagerly-materialised `Dictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>>`
  produced by the factory.
- Return the snapshot for the requested `asOf` (or `[]`).

**Construction**
```csharp
public CoveredCallChainProviderAdapter(
    IReadOnlyDictionary<DateOnly, IReadOnlyList<OptionCandidateInfo>> snapshotsByDate,
    ILogger<CoveredCallChainProviderAdapter> logger);
```

### CoveredCallChainProviderFactory

**Namespace:** `Meridian.Application.Services`
**Type:** `sealed class CoveredCallChainProviderFactory : ICoveredCallChainProviderFactory`
**Lifetime:** Singleton

**Responsibilities**
- For each trading date in `[from, to]`, call
  `IOptionsChainProvider.GetExpirationsAsync(...)`.
- For each expiration ≤ `to + MaxDte` and ≥ `from + MinDte`, fetch
  `GetChainSnapshotAsync(...)` (capped to `MaxStrikesPerExpiration`).
- Convert each call leg of each snapshot into an `OptionCandidateInfo` via
  `OptionChainConversions.ToCoveredCallCandidates(...)` (a small static helper added in this
  slice).
- Index by scan date.

**Dependencies**
```csharp
public CoveredCallChainProviderFactory(
    OptionsChainService chainService,   // existing service — already aggregates providers
    IOptionsMonitor<CoveredCallBacktestOptions> options,
    ILogger<CoveredCallChainProviderFactory> logger);
```

**Edge cases**
- No provider configured → throw `ConfigurationException("No options chain provider is registered.")`.
- Provider returns null/empty for every expiration → return an adapter that always returns
  `[]`; the backtest will produce zero option trades and the UI will surface that clearly.

### CoveredCallEndpoints

**Namespace:** `Meridian.Ui.Shared.Endpoints`
**Type:** `static class`

Mirrors the structure of `StrategyLifecycleEndpoints`. Each handler resolves
`ICoveredCallBacktestService` from `HttpContext.RequestServices`, validates the request,
maps to/from DTOs using `CoveredCallJsonContext`, and surfaces `ProblemDetails` on errors.

### DI Wiring

```csharp
// File: src/Meridian.Application/Composition/Features/StrategyFeatureRegistration.cs (extension)
services.Configure<CoveredCallBacktestOptions>(config.GetSection(CoveredCallBacktestOptions.SectionName));
services.AddSingleton<ICoveredCallChainProviderFactory, CoveredCallChainProviderFactory>();
services.AddSingleton<ICoveredCallBacktestService, CoveredCallBacktestService>();
services.AddHostedService(sp => (CoveredCallBacktestService)sp.GetRequiredService<ICoveredCallBacktestService>());
```

```csharp
// File: src/Meridian.Ui/Program.cs or wherever endpoints are mapped
app.MapCoveredCallEndpoints(jsonOptions);
```

---

## 5. Data Flow

### Run a backtest (happy path)

1. Operator opens `/strategy/covered-call`, fills the Configure form, clicks **Run**.
2. Dashboard calls `POST /api/strategies/covered-call/runs` with `CoveredCallBacktestRequest`.
3. `CoveredCallEndpoints.Start` deserializes via `CoveredCallJsonContext`, validates basic
   ranges (`From ≤ To`, `MinStrike > 0`, `0 < OverwriteRatio ≤ 1`, etc.).
4. Endpoint calls `ICoveredCallBacktestService.StartAsync(request, ct)` which:
   a. Generates `runId = Guid.NewGuid().ToString("N")`.
   b. Inserts a `RunState` (phase `"Queued"`) into `_runs`.
   c. Writes a `StartCommand` to `_channel`.
   d. Returns `CoveredCallRunHandle(runId, QueuedAt)`.
5. The drain loop picks up `StartCommand`:
   a. Acquires `_concurrency`.
   b. Sets phase `"WarmingUp"`; awaits
      `ICoveredCallChainProviderFactory.CreateAsync(symbol, from, to, MaxDte, ct)`.
   c. Builds `OptionsOverwriteParams` from the request fields (whitelisted mapping in
      `CoveredCallRunProjection.ToParams`).
   d. Constructs the strategy:
      `new CoveredCallOverwriteStrategy(symbol, params, chainAdapter, logger)`.
   e. Constructs `BacktestRequest` (single-symbol universe = `[symbol]`, `InitialCash`,
      `RiskFreeRate`, `DataRoot` from options).
   f. Calls `BacktestEngine.RunAsync(request, strategy, progress, runState.Cts.Token)`,
      where `progress` is an `IProgress<BacktestProgress>` that updates
      `RunState.Phase = "Running"`, `RunState.Percent`, `RunState.CurrentDate`.
   g. After `RunAsync` returns, calls `CoveredCallRunProjection.ToDto(...)` to assemble the
      `CoveredCallRunResult`, caches it, and persists a `StrategyRunEntry` via
      `IStrategyRepository.RecordRunAsync`.
   h. Sets phase `"Completed"`; releases the semaphore.
6. UI polls `GET /runs/{id}/status` every 1.5 s until phase ∈ {`Completed`, `Failed`,
   `Cancelled`}.
7. On `Completed`, UI fetches `GET /runs/{id}/result` once and renders the result components.

### Run a backtest (validation failure)

1. As steps 1–3 above.
2. Validation fails (e.g. `To < From`).
3. Endpoint returns `400 ProblemDetails` with field-level errors. UI surfaces inline beside
   the offending form field. No run is created.

### Run a backtest (engine failure)

1. Steps 1–5.f.
2. `BacktestEngine.RunAsync` throws (e.g. missing data root).
3. Drain loop catches, sets `RunState.Phase = "Failed"`, `RunState.Failure = ex.Message`,
   logs `_logger.LogError(ex, "Covered-call backtest {RunId} failed", runId)`.
4. UI's next poll observes `"Failed"` and shows a red banner with the message and a "Retry"
   button (re-submits the same `CoveredCallBacktestRequest`).

### Cancel an in-flight run

1. Operator clicks **Cancel** in the Run sub-view.
2. Dashboard calls `POST /api/strategies/covered-call/runs/{id}/cancel`.
3. Endpoint calls `ICoveredCallBacktestService.CancelAsync(runId)` which cancels the run's
   linked CTS.
4. `BacktestEngine.RunAsync` honours cancellation; the drain loop catches the OCE, transitions
   phase to `"Cancelled"`, and persists a `StrategyRunEntry` with `TerminalStatus = Cancelled`.

### Chain preview

1. Operator changes `MinStrike` / `MaxDelta` / DTE band in the Configure form.
2. Debounced (300 ms) dashboard call to
   `POST /api/strategies/covered-call/chain-preview` with the form values plus `AsOf = today`
   and the underlying symbol.
3. Endpoint calls `ICoveredCallBacktestService.PreviewChainAsync(...)` which:
   a. Resolves the underlying's last close from `OptionsChainService` (or a market-data
      adapter).
   b. Builds an ephemeral `IOptionChainProvider` adapter for that single date.
   c. Runs the strategy's own `OptionsOverwriteFilters` per candidate.
   d. Returns a `CoveredCallChainPreview` showing pass/fail per row.
4. UI renders the candidates table; selected row drives the payoff diagram preview.

---

## 6. UI Design

UI sub-section is non-XAML — this slice ships browser-workstation TSX. Layout sketches and
binding semantics are below.

### Route & top-level shell

```tsx
// src/Meridian.Ui/dashboard/src/app.tsx — add ABOVE the catch-all /strategy/*

const CoveredCallScreen = lazy(() => import("@/screens/covered-call-screen").then(
  (module) => ({ default: module.CoveredCallScreen })));

// ...inside <Routes>:
<Route path="/strategy/covered-call" element={<CoveredCallScreen />} />
<Route path="/strategy/quant-lab" element={<QuantLabScreen />} />
<Route path="/strategy/*" element={<ResearchScreen data={research} />} />
```

The "Strategy" workspace sidebar (`src/components/meridian/workspace-nav`) gains a
`Covered Call` link above `Quant Lab` and `Research`. Top-level operator nav stays the same
seven workspaces — covered call is an item inside Strategy.

### Screen structure

```
covered-call-screen.tsx                 — three-stage wizard shell
├── Stage 1: Configure
│   ├── UnderlyingInput          (symbol search bound to symbol service)
│   ├── DateRangeInput           (From / To)
│   ├── ParamFieldset            (MinStrike, OverwriteRatio, MaxDelta, MinDte, MaxDte,
│   │                             MinIvPercentile, MinOpenInterest, MinVolume, MaxSpreadPct,
│   │                             TakeProfitCapture, RollDelta, ExDivWindowDays, ScoringMode)
│   ├── PortfolioFieldset        (InitialCash, InitialUnderlyingShares)
│   └── ChainPreviewPanel        (live-filtered candidate list)
├── Stage 2: Run
│   ├── ProgressBar              (phase + percent + currentBacktestDate)
│   └── CancelButton
└── Stage 3: Results
    ├── MetricsTable             (Cagr/Sharpe/Sortino/Calmar/MaxDD, WinRate, AssignmentRate,
    │                             TotalPremiumCollected, TotalOptionPnl, UpCapture/DownCapture)
    ├── EquityCurveChart         (strategy vs underlying-only buy-and-hold)
    ├── PositionTimeline         (one row per CoveredCallTradeDto, color-coded by ExitReason)
    └── PayoffDiagram            (selected short call; client-side math)
```

All visual primitives reuse `@/components/ui/*` (Tailwind tokens, existing chart components
in `@/components/meridian/*`).

### View-model contract

```ts
// src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts

export type CoveredCallStage = "configure" | "run" | "results";

export interface CoveredCallScreenState {
  stage: CoveredCallStage;
  form: CoveredCallFormState;
  formErrors: Partial<Record<keyof CoveredCallFormState, string>>;
  chainPreview: ChainPreviewState;        // { status, candidates, selectedIndex }
  run: { runId: string | null; status: CoveredCallRunStatus | null };
  result: CoveredCallRunResult | null;
  history: CoveredCallRunSummary[];
  errorBanner: string | null;
}

export interface CoveredCallScreenViewModel extends CoveredCallScreenState {
  setField<K extends keyof CoveredCallFormState>(key: K, value: CoveredCallFormState[K]): void;
  refreshChainPreview(): Promise<void>;
  selectChainRow(index: number): void;
  startRun(): Promise<void>;
  cancelRun(): Promise<void>;
  loadHistory(): Promise<void>;
  openRun(runId: string): Promise<void>;     // loads a prior run into Stage 3
}
```

Implementation notes:
- Polling is implemented with a hook `useRunStatusPolling(runId, intervalMs = 1500)` that
  stops itself on terminal phases.
- All API calls live in `dashboard/src/lib/api/covered-call.ts` (new file) using `fetch` with
  the existing `apiClient` wrapper. The wrapper is responsible for credentials, JSON parsing,
  and `ProblemDetails` extraction.

### Payoff math (client-side)

```ts
// src/Meridian.Ui/dashboard/src/lib/covered-call/payoff.ts
export interface ShortCallPayoffInputs {
  strike: number;
  entryCredit: number;
  contracts: number;
  multiplier: number;
}

export function shortCallPayoff(spot: number, p: ShortCallPayoffInputs): number {
  return (p.entryCredit - Math.max(spot - p.strike, 0)) * p.contracts * p.multiplier;
}

export function payoffCurve(p: ShortCallPayoffInputs, spotMin: number, spotMax: number, steps = 100) {
  // returns { spot, payoff }[]; the underlying-position payoff is added in the chart layer.
}
```

The `PayoffDiagram` component combines `shortCallPayoff` with the long-underlying payoff
(`shares × (spot − costBasis)`) so the operator sees the covered-call net curve including the
capped upside and the premium cushion below cost.

---

## 7. Test Plan

**Principle:** Unit-test at interface boundaries with `Substitute`/`FakeItEasy` test doubles
(matching existing `tests/Meridian.Tests` conventions). Integration tests use the
`SyntheticOptionsChainProvider` and synthetic bars from the repo's fixture set. Dashboard
tests use Vitest + React Testing Library mirroring the existing `*-screen.test.tsx` pattern.

### Unit tests — CoveredCallBacktestService (`tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallBacktestServiceTests.cs`)

| Test | What it verifies | Setup notes |
|------|------------------|-------------|
| `StartAsync_AssignsRunIdAndEnqueues` | `RunId` returned matches the run later visible in `GetStatusAsync` and starts in phase `Queued` | Fake `BacktestEngine` that never completes |
| `StartAsync_RejectsInvalidDateRange` | Validation surfaces `ArgumentException` for `To < From` | n/a |
| `Drain_RunsBacktestAndPersistsRunEntry` | `IStrategyRepository.RecordRunAsync` receives an entry with `StrategyId = "covered-call-overwrite"` and matching `RunId` | Stub engine returns canned `BacktestResult` |
| `Drain_PopulatesResultFromStrategyMetrics` | DTOs include `Cagr`, `WinRate`, full equity curve, trades | Stub strategy via fake engine |
| `CancelAsync_TransitionsRunToCancelled` | Phase becomes `Cancelled`, `IStrategyRepository` entry has `TerminalStatus = Cancelled` | Engine blocks on a `TaskCompletionSource` |
| `Drain_FailingEngineSetsFailurePhase` | Phase `Failed`, `FailureMessage` populated, exception logged with structured fields | Engine throws `InvalidOperationException` |
| `Drain_RespectsMaxConcurrentRuns` | With `MaxConcurrentRuns=1`, second queued run does not start until first completes | Two slow engines |
| `OptionsMonitor_HotReloadsConcurrency` | Raising `MaxConcurrentRuns` from 1 → 3 lets two more runs begin | Use `TestOptionsMonitor<T>` |
| `GetResultAsync_CachesAndExpires` | Second call hits cache; after `ResultCacheDuration` it re-hydrates from repository | `FakeTimeProvider` |

### Unit tests — CoveredCallChainProviderAdapter (`tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallChainProviderAdapterTests.cs`)

| Test | What it verifies |
|------|-----------------|
| `GetCalls_ReturnsSnapshotForKnownDate` | Returns the exact list seeded by the factory |
| `GetCalls_UnknownDateReturnsEmpty` | Empty list, no exception |
| `GetCalls_IgnoresCaseAndWhitespaceOnSymbol` | `"spy "` matches `"SPY"` |

### Unit tests — CoveredCallChainProviderFactory (`tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallChainProviderFactoryTests.cs`)

| Test | What it verifies |
|------|-----------------|
| `CreateAsync_FetchesEveryEligibleExpiration` | One `GetChainSnapshotAsync` call per expiration in the date+MaxDte window |
| `CreateAsync_RespectsMaxStrikesPerExpiration` | `strikeRange` argument honours the configured cap |
| `CreateAsync_NoProviderThrowsConfigurationException` | `ConfigurationException` with provider-missing message |
| `CreateAsync_HonoursCancellation` | `OperationCanceledException` propagates without partial state |
| `CreateAsync_MapsChainSnapshotToCandidateInfo` | Bid/ask/delta/IV/OI/volume all round-trip correctly |

### Unit tests — CoveredCallEndpoints (`tests/Meridian.Ui.Tests/Endpoints/CoveredCallEndpointsTests.cs`)

| Test | What it verifies |
|------|-----------------|
| `Start_ReturnsHandleOn200` | Happy path returns `CoveredCallRunHandle` |
| `Start_ReturnsProblemDetailsOnValidationError` | 400 + field errors |
| `Status_ReturnsLatestPhase` | 200 with the polled state |
| `Status_404OnUnknownRunId` | 404 |
| `Result_409WhileRunning` | 409 with hint to keep polling |
| `Result_200OnCompleted` | Full payload |
| `Cancel_IsIdempotent` | Two cancels in a row both return 200 |
| `ChainPreview_503WhenNoProviderRegistered` | Bubbles `ConfigurationException` as 503 |

### Unit tests — CoveredCallRunProjection (`tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallRunProjectionTests.cs`)

| Test | What it verifies |
|------|-----------------|
| `ToParams_MapsAllWhitelistedFields` | Every DTO field that should reach `OptionsOverwriteParams` does |
| `ToParams_RejectsUnsafeValues` | `OverwriteRatio > 1.0` or `MinStrike <= 0` throws |
| `ToDto_HandlesEmptyTradeList` | Doesn't `NRE` when the strategy emitted zero trades |
| `ToDto_PreservesExitReasonOrder` | Enum-to-string mapping matches `ShortCallExitReason` member names |

### Dashboard tests (`src/Meridian.Ui/dashboard/src/screens/covered-call-screen.test.tsx`, `.view-model.test.ts`)

| Test | What it verifies |
|------|-----------------|
| `view-model: setField updates form and clears matching error` | Setting `minStrike` clears the prior `minStrike` validation error |
| `view-model: refreshChainPreview debounces` | Two quick edits result in one API call |
| `view-model: startRun transitions to "run" stage and polls` | After `startRun`, polling begins and stops on `Completed` |
| `view-model: cancelRun calls cancel endpoint and stops polling` | One cancel call, polling halts |
| `view-model: openRun loads prior result into Stage 3` | History click jumps stages |
| `screen: renders Configure form by default` | Smoke render |
| `screen: shows error banner when start returns 400` | ProblemDetails surfacing |
| `screen: payoff diagram updates when selected position changes` | Math + chart wiring |
| `lib/covered-call/payoff.test.ts` | `shortCallPayoff(spot)` matches reference table for sample strikes |

### Integration / scenario test (`tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallEndToEndTests.cs`)

| Test | What it verifies |
|------|-----------------|
| `RunAgainstSyntheticChainProducesNonZeroPnl` | Wire `SyntheticOptionsChainProvider` and synthetic bars, run a 1-year window, assert at least one trade, non-NaN Sharpe, persisted run entry |
| `StrategyIdInPersistedEntryMatchesContract` | `"covered-call-overwrite"` |

### Test Infrastructure Added

- `FakeBacktestEngine` test double (or refactor existing one if present) that fires a
  synthetic `BacktestResult` and lets tests gate completion with a `TaskCompletionSource`.
- `TestOptionsMonitor<CoveredCallBacktestOptions>` for hot-reload tests (likely already
  exists in `tests/Meridian.Tests/TestSupport/`).
- New fixture under `tests/fixtures/covered-call/` with a small synthetic chain JSON.

---

## 8. Implementation Checklist

**Estimated effort:** Medium — 6–9 working days for a single developer who is comfortable
with both the .NET backend and the dashboard. Roughly 60 % backend, 40 % UI.

**Suggested branch:** `claude/covered-call-writing-L9kAs` (already created).
**Suggested PR sequence:** two PRs.
- PR1: Backend service, endpoints, DTOs, SG-JSON, DI wiring, full xUnit suite, route
  constants. Mergeable on its own (the chain-preview endpoint becomes consumable by any
  client).
- PR2: Dashboard screen, view-model, components, Vitest suite, navigation wiring.

### Phase 1: Backend foundation (PR1)
- [ ] Create `src/Meridian.Ui.Shared/Contracts/CoveredCallContracts.cs` with all DTOs.
- [ ] Create `src/Meridian.Ui.Shared/Serialization/CoveredCallJsonContext.cs` (SG-JSON).
- [ ] Append `CoveredCall*` route constants to
      `src/Meridian.Contracts/Api/UiApiRoutes.cs`.
- [ ] Create `src/Meridian.Application/Options/CoveredCallBacktestOptions.cs`.
- [ ] Add `"Strategies": { "CoveredCall": {...} }` defaults to
      `config/appsettings.json` and `appsettings.Development.json`.

### Phase 2: Backend services (PR1)
- [ ] Create `ICoveredCallChainProviderFactory` + `CoveredCallChainProviderFactory`.
- [ ] Create `CoveredCallChainProviderAdapter` implementing the SDK `IOptionChainProvider`.
- [ ] Create `CoveredCallRunProjection` (static mapper: DTO ↔ `OptionsOverwriteParams`,
      strategy output ↔ DTOs).
- [ ] Create `ICoveredCallBacktestService` + `CoveredCallBacktestService` (channel,
      drain loop, semaphore, `IHostedService`, cache).
- [ ] Add `StrategyFeatureRegistration` extension or update the existing one to wire the
      new services and hosted service.

### Phase 3: Endpoints (PR1)
- [ ] Create `src/Meridian.Ui.Shared/Endpoints/CoveredCallEndpoints.cs` mirroring the shape
      of `StrategyLifecycleEndpoints`. Apply `UiEndpoints.MutationRateLimitPolicy` on POSTs.
- [ ] Wire `app.MapCoveredCallEndpoints(jsonOptions)` into the workstation host pipeline.
- [ ] Verify `ProblemDetails` shape via `EndpointHelpers` to stay consistent with neighbours.

### Phase 4: Backend tests (PR1)
- [ ] All test files listed in Section 7 — service, adapter, factory, projection,
      endpoints, end-to-end.
- [ ] Coverage ≥ 80 % on new code; run
      `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true`
      and `dotnet test tests/Meridian.Ui.Tests -c Release /p:EnableWindowsTargeting=true`.

### Phase 5: Dashboard scaffolding (PR2)
- [ ] Add `dashboard/src/lib/api/covered-call.ts` with typed client functions.
- [ ] Add types to `dashboard/src/types.ts` (mirror DTOs; or create
      `dashboard/src/types/covered-call.ts`).
- [ ] Add `dashboard/src/lib/covered-call/payoff.ts` + tests.
- [ ] Add `covered-call-screen.view-model.ts` + tests.
- [ ] Add `covered-call-screen.tsx` (three-stage shell).

### Phase 6: Dashboard components (PR2)
- [ ] `components/covered-call/configure-form.tsx`.
- [ ] `components/covered-call/run-progress.tsx`.
- [ ] `components/covered-call/results/equity-curve.tsx`.
- [ ] `components/covered-call/results/position-timeline.tsx`.
- [ ] `components/covered-call/results/payoff-diagram.tsx`.
- [ ] `components/covered-call/results/metrics-table.tsx`.

### Phase 7: Navigation & shell (PR2)
- [ ] Register `/strategy/covered-call` route in `dashboard/src/app.tsx` (note the route
      goes **above** the catch-all `/strategy/*` to `ResearchScreen`).
- [ ] Add a "Covered Call" link to the Strategy workspace nav in
      `dashboard/src/components/meridian/workspace-nav` (or wherever the strategy sub-nav
      currently lives — confirm during implementation).
- [ ] Run `npm --prefix src/Meridian.Ui/dashboard run test` and
      `npm --prefix src/Meridian.Ui/dashboard run build`.

### Phase 8: Wrap-up (both PRs)
- [ ] Update `docs/plans/options-roadmap.md` to reflect that slice 1 has landed.
- [ ] Update `README.md` if the "Strategy" section enumerates strategies. (Skip if not.)
- [ ] Run `python3 build/scripts/docs/check-ai-inventory.py --summary` to make sure no AI
      indexes need refreshing.
- [ ] Run `python3 build/scripts/ai-repo-updater.py known-errors` before opening the PR.

### Out-of-band items (do **not** include in slice 1)
- Live execution. Defer to slice 2.
- Multi-leg combo orders. Defer to slice 3.
- Drag-drop spread builder. Defer.

---

## 9. Open Questions & Risks

### Open Questions

| # | Question | Owner | Impact if Unresolved |
|---|----------|-------|----------------------|
| 1 | Where does the underlying's last close come from for `chain-preview` when only an `IOptionsChainProvider` is configured? `OptionsChainService` exposes `GetChainSnapshotAsync` but no spot price. | Implementer | Without this, the preview cannot compute deltas or filter by `MinStrike` ATM-relative — currently the strategy uses `_underlyingPrice` from the engine's bar feed. Likely answer: also resolve from `IMarketDataClient` last-trade cache or the most recent historical bar via `IHistoricalDataProvider`. |
| 2 | How are IV-percentile values populated for production chains? `OptionsOverwriteParams.MinIvPercentile` defaults to 50, but `OptionCandidateInfo.IvPercentile` is nullable and not populated by any current `IOptionsChainProvider`. | Implementer / Product | If left null, candidates fall to the filter's null branch (currently ignored). Slice 1 should expose an option to disable the IV-percentile filter for now or document the gap. |
| 3 | Does the workstation host already register a `BacktestEngine` instance for DI, or does the new service need to construct one? `BacktestEngine` is currently used by `BatchBacktestService` and Lean integration, but the DI registration path for the new lane needs verification. | Implementer | Could expand PR1 scope if a new registration is required. |
| 4 | Should `CoveredCallRunSummary` flow through the existing `StrategyRunReadService` so the run also appears in the Research/Run-library, or is it covered-call-only? | Product | Drives whether the existing Research page needs any change. Slice 1 default: covered-call-only screen for now; sync with Research in slice 1.5. |
| 5 | Rate-limit budget. `UiEndpoints.MutationRateLimitPolicy` is shared. Is one covered-call start per N seconds the right global budget, or should it have its own policy? | Implementer | Low impact; can be adjusted in config. |

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Production chain provider returns too few strikes / no IV → empty backtest | Medium | Medium (silent zero-trade results confuse operators) | Surface `TotalContractsScanned` and `FiltersPassed` in `CoveredCallChainPreview`; show a Stage-3 banner when `TotalOptionTrades == 0` explaining why. |
| Backtest engine takes longer than the operator expects; poll loop creates noise in logs | Medium | Low | Log only phase transitions (not every poll); add structured log fields `RunId`, `Phase`, `PercentComplete`. |
| Eager chain materialisation blows memory on very long backtests | Low | Medium | `MaxStrikesPerExpiration` cap (default 80) and `MaxDte` cap. Document with a Stage-1 advisory when window > 2y. |
| Strategy mutates `OptionsOverwriteParams` after construction | Very low | Low | `OptionsOverwriteParams` is a `record` with `init` setters — immutable by construction. |
| DTO drift if `OptionsOverwriteMetrics` adds a field | Low | Low | `CoveredCallRunProjection.ToDto` is the single mapper; missing fields will surface in its tests. |
| `StrategyRunEntry` JSON serialisation of the embedded metrics blob doesn't round-trip across versions | Medium | Low | Store result-as-Dto in the cache; persistence stores a smaller summary only. Full result can be re-derived by re-running if the cache misses. (This is actually how `StrategyRunStore` is already used in research.) |

---

## Appendix: Source Anchors

These are the existing files this blueprint reads from or stands next to. They should be the
implementer's starting points.

- Strategy: `src/Meridian.Backtesting.Sdk/Strategies/OptionsOverwrite/CoveredCallOverwriteStrategy.cs`
- Params: `src/Meridian.Backtesting.Sdk/Strategies/OptionsOverwrite/OptionsOverwriteParams.cs`
- Models: `src/Meridian.Backtesting.Sdk/Strategies/OptionsOverwrite/OptionsOverwriteModels.cs`
- Filters: `src/Meridian.Backtesting.Sdk/Strategies/OptionsOverwrite/OptionsOverwriteFilters.cs`
- Scoring: `src/Meridian.Backtesting.Sdk/Strategies/OptionsOverwrite/OptionsOverwriteScoring.cs`
- Engine: `src/Meridian.Backtesting/Engine/BacktestEngine.cs`
- Request: `src/Meridian.Backtesting.Sdk/BacktestRequest.cs`
- Chain provider (production): `src/Meridian.ProviderSdk/IOptionsChainProvider.cs`
- Chain providers (impls): `src/Meridian.Infrastructure/Adapters/{Polygon,Robinhood,Alpaca,Synthetic}/*OptionsChainProvider.cs`
- Chain aggregator: `src/Meridian.Application/Services/OptionsChainService.cs`
- Strategy lifecycle: `src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs`
- Strategy repository: `src/Meridian.Strategies/Interfaces/IStrategyRepository.cs`
- Strategy run-store services: `src/Meridian.Strategies/Services/StrategyLifecycleManager.cs`, `StrategyRunReadService.cs`
- Existing strategies endpoints: `src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs`
- Existing SG-JSON sibling: `src/Meridian.Ui.Shared/Serialization/DirectLendingJsonContext.cs`
- Existing UI route constants: `src/Meridian.Contracts/Api/UiApiRoutes.cs`
- Dashboard router: `src/Meridian.Ui/dashboard/src/app.tsx`
- Dashboard screens convention: `src/Meridian.Ui/dashboard/src/screens/quant-lab-screen.{tsx,view-model.ts}`

---

*End of blueprint.*
