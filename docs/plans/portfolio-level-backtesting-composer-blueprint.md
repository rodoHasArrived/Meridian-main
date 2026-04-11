# Portfolio-Level Backtesting Composer Blueprint

**Owner:** Core Team  
**Audience:** Research, Backtesting, Ledger, Risk, Workstation API, Web, and WPF contributors  
**Last Updated:** 2026-04-09  
**Status:** Proposed blueprint

---

## Summary

This blueprint defines a portfolio-level Backtesting Composer that lets users combine multiple strategy runs with allocation constraints, execute them on a shared simulation clock, and evaluate portfolio-level risk analytics (correlation, concentration, and drawdown contribution) across Backtesting, Ledger, and Risk pillars.

## Scope

### In scope

- Composer workflow in Research workspace for selecting compatible backtest runs and assigning weights.
- Allocation policy configuration (static weights first, then optional constraint-based optimization).
- Shared simulation clock that replays constituent runs in one timeline.
- Cross-strategy capital accounting that enforces cash contention and funding limits.
- Portfolio analytics output with concentration, correlation matrix, and drawdown attribution.
- Portfolio-level persistence as a new run artifact linked to existing `StrategyRunEntry` records.

### Out of scope

- Real-time/live portfolio optimization.
- New broker/execution adapters.
- Full factor-model risk engine replacement.
- Tax-lot optimization or post-trade accounting outside current ledger semantics.

### Assumptions

- Existing strategy runs already persist sufficient artifacts through `BacktestResult` and workstation run read models.
- Portfolio composition starts from completed backtests (`StrategyRunStatus.Completed`).
- Initial release targets deterministic replay, not Monte Carlo scenario generation.

## Architecture

### 1) Research-facing Composer surface

Add a portfolio composer lane to the Backtest studio/research workflow in `src/Meridian.Ui/dashboard`:

- Run picker: selects completed runs from existing `/api/workstation/runs` surfaces.
- Allocation editor: manages target weights and rebalance cadence.
- Constraint panel: long-only toggle, max single-strategy weight, min cash buffer, optional turnover cap.
- Validation panel: eligibility, timestamp overlap checks, and missing-artifact warnings.

For workstation orchestration, add corresponding endpoints in `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` to create, run, and inspect composer jobs while reusing existing strategy-run conventions.

### 2) Application orchestration layer

Add a dedicated orchestration seam in `Meridian.Application.Backtesting`:

- `IPortfolioBacktestComposerService`: validate inputs, construct execution plan, invoke replay engine.
- `IPortfolioCapitalAllocator`: resolve target weights into per-step notional budgets.
- `IPortfolioRebalanceScheduler`: determine rebalance events from cadence and calendar.

This keeps UI/API thin and prevents strategy lifecycle services from absorbing portfolio simulation concerns.

### 3) Shared clock + capital contention model

Execution model:

- Build a unified event timeline from selected run artifacts (`BacktestResult` snapshots/fills/cash flows).
- Replay each timestamp in deterministic order (timestamp, then stable run-id tie-breaker).
- Route all fills through a portfolio cash gate before accepting order effects.
- If total requested capital exceeds available capital at a timestamp, apply configured contention policy:
  - default v1: proportional haircut by requested notional;
  - emit explicit contention diagnostics.

This approach makes capital scarcity first-class and avoids unrealistic “each strategy had full standalone capital” assumptions.

### 4) Ledger + risk integration

Ledger integration (`src/Meridian.Ledger`):

- Record portfolio-level journals for funding, realized PnL, fees, and rebalance transfers.
- Preserve strategy-level lineage by tagging journal lines with source `runId` and composer allocation slice.

Risk integration (`src/Meridian.Risk`):

- Compute concentration metrics (HHI, top-N weight, issuer/symbol concentration where available).
- Compute return correlation matrix across component runs over aligned periods.
- Compute drawdown contribution using incremental equity-curve attribution by strategy.

### 5) Persistence and contracts

Extend workstation contracts in `src/Meridian.Contracts/Workstation` with additive records (no breaking removals):

- `PortfolioComposerRequest`
- `PortfolioComposerConstraintSet`
- `PortfolioComposerResultSummary`
- `PortfolioCorrelationMatrix`
- `PortfolioDrawdownContributionRow`

Persist as a portfolio-run artifact associated with a synthetic `StrategyRunEntry` (mode `Backtest`, engine discriminator e.g. `MeridianNative`) so existing run browser, governance traces, and promotion workflows can reference it consistently.

## Interfaces and Models

```csharp
namespace Meridian.Application.Backtesting;

public interface IPortfolioBacktestComposerService
{
    Task<PortfolioComposerValidationResult> ValidateAsync(PortfolioComposerRequest request, CancellationToken ct);
    Task<PortfolioComposerRunHandle> StartAsync(PortfolioComposerRequest request, CancellationToken ct);
    Task<PortfolioComposerRunStatus> GetStatusAsync(string runId, CancellationToken ct);
    Task<PortfolioComposerResultSummary?> GetResultAsync(string runId, CancellationToken ct);
}

public interface IPortfolioCapitalAllocator
{
    PortfolioCapitalPlan Allocate(
        DateTimeOffset asOf,
        decimal totalCapital,
        IReadOnlyList<PortfolioTargetWeight> targetWeights,
        PortfolioComposerConstraintSet constraints);
}

public interface IPortfolioRebalanceScheduler
{
    IReadOnlyList<DateTimeOffset> BuildSchedule(DateTimeOffset start, DateTimeOffset end, PortfolioRebalanceCadence cadence);
}
```

```csharp
namespace Meridian.Contracts.Workstation;

public sealed record PortfolioComposerRequest(
    string Name,
    decimal InitialCapital,
    PortfolioRebalanceCadence RebalanceCadence,
    IReadOnlyList<PortfolioStrategyAllocation> Allocations,
    PortfolioComposerConstraintSet Constraints);

public sealed record PortfolioStrategyAllocation(
    string RunId,
    decimal TargetWeight);

public sealed record PortfolioComposerResultSummary(
    string RunId,
    decimal TotalReturn,
    decimal MaxDrawdown,
    double? SharpeRatio,
    IReadOnlyList<PortfolioDrawdownContributionRow> DrawdownContributions,
    PortfolioCorrelationMatrix Correlation,
    PortfolioConcentrationSummary Concentration);
```

## Data Flow

1. User opens Composer in Research and selects eligible completed strategy runs.
2. API validates run compatibility (overlapping clock, currency/base assumptions, artifact completeness).
3. Application service creates a portfolio simulation plan and rebalance schedule.
4. Replay engine processes unified timeline, resolving capital contention on each step.
5. Ledger writer emits portfolio journals with strategy lineage tags.
6. Risk analyzer computes correlation/concentration/drawdown contribution on resulting portfolio series.
7. Result is stored and exposed through workstation run-detail and comparison endpoints.

## Edge Cases and Risks

- **Capital contention realism:** proportional haircut is deterministic but may differ from execution-priority semantics; provide pluggable contention policies later.
- **Timeline mismatch:** runs with sparse or non-overlapping periods can distort correlations; enforce minimum overlap threshold.
- **Multi-currency runs:** if run currencies differ and no FX series is attached, block composition in v1.
- **Path dependency:** rebalance cadence meaningfully changes outcomes; always store cadence and rebalance events in audit metadata.
- **Analytics explainability:** drawdown contribution can be misread; include method label and confidence caveats in UI.

## Test Plan

- Unit tests (`tests/Meridian.Tests`):
  - allocator constraints (weight caps, cash buffers, normalization)
  - contention policy behavior under capital shortfall
  - rebalance schedule generation across daily/weekly/monthly cadences
- Integration tests:
  - compose 2–3 deterministic runs and verify portfolio equity, ledger totals, and attribution consistency
  - verify correlation and drawdown contribution outputs on controlled fixtures
- Contract/API tests:
  - workstation endpoint coverage for create/start/status/detail flows
  - backward-compatibility checks for existing strategy-run endpoints

Suggested validation commands:

- `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true`
- `dotnet test tests/Meridian.Ui.Tests -c Release /p:EnableWindowsTargeting=true`

## Open Questions

- Should contention policy default to proportional haircut or strategy-priority queueing?
- Do we treat component-run slippage/fees as final, or allow portfolio-level slippage overlays?
- Should a composed run be promotable to paper trading directly, or remain research-only in v1?
- What minimum overlap window is required before we show correlation metrics as “trusted”?
- Do we need a dedicated Governance workspace panel for portfolio-run audit lineage at launch?
