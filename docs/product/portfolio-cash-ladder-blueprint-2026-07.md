# Portfolio Cash Ladder — Blueprint (2026-07)

> **Status:** In progress — first vertical slice landed 2026-07-05 (see "Delivered Slice" below);
> the persisted-run/per-currency/structured-sourcing phases remain open (wave-8 candidate).
> **Source:** 2026-07-05 brainstorm session — "Portfolio Cash-Flow Forecasting & Liquidity Engine",
> selected as the recommended next large-scale update.
> **Depth mode:** full
> **Prepared for:** implementation following completion of the `codex/instrument-type-depth` branch.

---

## Delivered Slice (2026-07-05)

A goal-directed vertical slice of this lane is on `main`. It implements the operator moment
(portfolio-wide dated cash ladder with scenarios and drill-down) as a compute-on-request read
model; it does not yet implement the persisted-run, per-currency, or structured-sourcing phases
of this blueprint.

**What landed:**

- `Meridian.Contracts/AssetOperations/PortfolioCashLadderDtos.cs` — ladder/bucket/contribution/
  scenario DTOs, `IPortfolioCashLadderQueryService`, and provider seams
  `IPortfolioCashBalanceProvider` / `IPortfolioCapitalScheduleProvider`.
- `Meridian.Instruments/AssetOperations/PortfolioCashLadderEngine.cs` — pure aggregation +
  scenario engine (`portfolio-cash-ladder-v1`): terms-driven flows across positions, capital
  activity join, weekly-bucketed ladder, cumulative cash, minimum-balance breach flags, and five
  built-in scenarios (rates ±100bp floating-only, early call, redemption wave 20%, FX adverse
  10%) each carrying explicit `ModeledEffects` / `Assumptions`.
- `Meridian.Ui.Shared/Services/PortfolioCashLadderReadService.cs` — enumerates active Security
  Master subjects through `IAssetOperationsQueryService` and assembles engine inputs.
- `GET /api/portfolio/cash-ladder` (+ `/scenarios`) in
  `Meridian.Ui.Shared/Endpoints/PortfolioCashLadderEndpoints.cs`; constants in `UiApiRoutes`.
- Browser workstation: `/portfolio/cash-ladder` screen (stacked source bars, cumulative line,
  threshold band, scenario dropdown, bucket drill-down with projection-run and terms-version
  traceability), linked from the Portfolio workspace header.
- Tests: `tests/Meridian.Tests/AssetOperations/PortfolioCashLadderEngineTests.cs` (10) and
  `src/Meridian.Ui/dashboard/src/screens/cash-ladder-screen.view-model.test.ts` (8).

**Known deviations from this blueprint (deliberate, slice-scoped):**

- Engine lives in `Meridian.Instruments.AssetOperations` (pure, contracts-only inputs) rather
  than `Meridian.Application.PortfolioForecasting`; lifting it later is mechanical.
- No persisted ladder runs yet — compute-on-request `GET`, so the blueprint's
  `POST /api/portfolio/cash-ladder/runs` surface remains unclaimed and additive.
- Single blended ladder with explicit 1:1 non-base-currency warnings instead of per-currency
  views; the warning text makes the blend honest until the per-currency phase lands.
- Terms-driven sourcing only (no `ISecurityMasterCashFlowService` tier yet); unprojected
  positions surface as a count warning rather than per-security exclusion rows.
- Capital activity and an FX shock scenario shipped in the slice (the driving goal required
  them) even though this blueprint deferred both; the provider seam keeps investor-schedule
  data optional until the capital activity engine exists.

---

## Scope

**In Scope:**

- A portfolio-wide cash-ladder engine that aggregates per-security projected cash flows — from the
  structured cash-flow provider seam and the terms-driven asset-obligation projection — into dated,
  bucketed, per-currency inflow/outflow ladders with cumulative projected cash.
- Rate-scenario selection by pass-through to the existing per-security
  `StructuredCashFlowScenario` (Base, ±100/200/300bp, Stress).
- A portfolio-level liquidity shock engine (delay / haircut / drop / injected flow transforms)
  with operator-authored named scenarios.
- Persisted, versioned ladder runs with full lineage to the per-security projection runs that fed
  them, plus explicit exclusions for positions that could not be projected.
- REST endpoints under the shared workstation API surface.
- A Cash Ladder view inside the browser workstation Portfolio workspace.

**Out of Scope (adjacent lanes — do not creep):**

- Capital activity / investor flows (calls, distributions) — joins in a later phase once the
  investor capital activity engine exists.
- Expense accruals, FX conversion between currencies, and treasury sweep proposals.
- Term-level scenario re-projection inside Meridian (rate scenarios remain the structured
  providers' responsibility).
- WPF desktop parity view (follow-up; shared contracts make it cheap later).
- Portfolio risk reuse of the shock engine (deliberate future consumer, not built here).

**Assumptions (implementer must verify):**

1. `codex/instrument-type-depth` has merged: `AssetObligationProjectionService`,
   `IAssetOperationsQueryService`, and `ISecurityMasterCashFlowService` are available on `main`.
2. Ledger position accounts allow deriving per-security quantity and per-currency cash balances
   as of a date (see Open Question 2).
3. `AssetProjectedCashFlowDto.Amount` and `StructuredCashFlowScheduleEntry` amounts are
   position-scaled, not per-unit-of-face (see Open Question 1 — the single riskiest assumption).

**Breaking changes:** none. Everything here is additive; no existing public interface changes.

---

## Architectural Overview

### Context Diagram

```
                      Meridian.Contracts.PortfolioForecasting (new DTOs + interfaces)
                                          │
  ┌───────────────────────────────────────┼───────────────────────────────────────────┐
  │ Meridian.Application.PortfolioForecasting (new)                                   │
  │                                                                                   │
  │   PortfolioCashLadderService ──────► CashLadderShockEngine (pure transforms)      │
  │        │        │       │                                                         │
  │        │        │       └──────────► PortfolioCashLadderRunStore ── AtomicFileWriter
  │        │        │                     (DataRoot/forecasting/cash-ladder/)         │
  │        │        │                                                                 │
  │        │        └── flow sources (existing seams, Contracts-level):               │
  │        │              1. ISecurityMasterCashFlowService.GetProjectionAsync        │
  │        │                   (StructuredCashFlowScenario-aware; MIAC / Moody's /    │
  │        │                    client-provided / calculated providers)               │
  │        │              2. IAssetOperationsQueryService.GetOperationsAsync          │
  │        │                   (terms-driven AssetObligationProjectionService flows)  │
  │        │                                                                          │
  │        └── IPortfolioHoldingsSource (new seam)                                    │
  │              └── LedgerPortfolioHoldingsSource ── IReadOnlyLedger                 │
  └───────────────────────────────────────┬───────────────────────────────────────────┘
                                          │
              Meridian.Ui.Shared/Endpoints/PortfolioForecastingEndpoints.cs (new)
                                          │
              src/Meridian.Ui/dashboard/src/screens/portfolio-cash-ladder-screen.*
                                (Portfolio workspace, browser workstation)
```

### Design Decisions

- **Decision:** Build the engine in `Meridian.Application.PortfolioForecasting`, consuming only
  Contracts-level interfaces (`ISecurityMasterCashFlowService`, `IAssetOperationsQueryService`,
  `IReadOnlyLedger` via a holdings adapter).
  **Alternatives considered:** a new `Meridian.Forecasting` project; extending
  `Meridian.Instruments`.
  **Rationale:** the join crosses Security Master, asset operations, and Ledger — that is
  orchestration, which is Application's role (`SecurityMasterCashFlowService` is the direct
  precedent). Depending on Contracts interfaces keeps `Meridian.Instruments` internals private.
  **Consequences:** no new project plumbing; if forecasting grows into its own pillar, the
  namespace can be lifted later.

- **Decision:** Two-tier flow sourcing with explicit exclusions — prefer the structured cash-flow
  projection (rate-scenario aware), fall back to the terms-driven asset-operations projection
  (Base-only), otherwise record a `CashLadderExclusionDto` with a reason.
  **Alternatives considered:** single source; silent skipping of unprojectable positions.
  **Rationale:** the structured seam carries governed, provider-quality schedules where they
  exist; the terms projection covers the long tail. Silent gaps would make the ladder untrustworthy
  — exclusions make coverage a first-class, visible number.
  **Consequences:** every ladder reports coverage honestly; UI must render the exclusions banner.

- **Decision:** Rate scenarios delegate to the existing `StructuredCashFlowScenario` enum;
  portfolio-level shocks are pure flow transforms (`CashLadderShockEngine`) applied after
  aggregation.
  **Alternatives considered:** re-projecting terms under shocked curves inside the ladder engine.
  **Rationale:** rate-path modeling already belongs to `IStructuredCashFlowProvider`
  implementations; duplicating it would drift. Flow transforms (delay, haircut, drop, inject) are
  cheap, deterministic, and honest about being behavioral assumptions rather than valuation.
  **Consequences:** terms-fallback flows do not respond to rate scenarios — the run records which
  positions are rate-sensitive so the UI can annotate the share that responded.

- **Decision:** Per-currency ladders with no FX conversion in V1.
  **Alternatives considered:** converting to a base currency via a rate source.
  **Rationale:** no governed FX rate seam exists today; converting with ad-hoc rates would
  undermine the evidence discipline. Currency tabs keep the math exact.
  **Consequences:** multi-currency books see N ladders, not one blended view, until an FX seam
  exists.

- **Decision:** Persist every ladder run as an immutable JSON document via `AtomicFileWriter`
  (ADR-007) under `DataRoot/forecasting/cash-ladder/`, with an engine version constant and lineage
  to every per-security projection run.
  **Alternatives considered:** compute-on-request with no persistence.
  **Rationale:** matches the run-record discipline `AssetCashFlowProjectionRunDto` and
  `StrategyRunStore` already established; persisted runs are the hook for the Evidence Vault lane
  and for later run comparison.
  **Consequences:** retention management needed (options-bound count).

- **Decision:** Cold-path, request-scoped computation — no `EventPipeline` channels, no
  `IHostedService`.
  **Rationale:** ADR-013 channels exist for hot-path streaming; a ladder build is an on-demand
  aggregation. Bounded parallelism (`Parallel.ForEachAsync`, options-capped) is sufficient.
  **Consequences:** scheduled/nightly ladder builds can be added later via the existing
  operational scheduler without redesign.

---

## Interface & API Contracts

### New DTOs — `src/Meridian.Contracts/PortfolioForecasting/PortfolioForecastingDtos.cs`

```csharp
namespace Meridian.Contracts.PortfolioForecasting;

[JsonConverter(typeof(JsonStringEnumConverter<CashLadderBucketing>))]
public enum CashLadderBucketing { Daily, Weekly, Monthly }

/// <summary>Request to build a portfolio cash ladder run.</summary>
public sealed record CashLadderRequestDto(
    DateOnly AsOf,
    int HorizonDays,
    CashLadderBucketing Bucketing,
    StructuredCashFlowScenario RateScenario = StructuredCashFlowScenario.Base,
    string? ShockScenarioId = null);

/// <summary>One shock inside a named liquidity scenario. Parameters are shock-kind specific.</summary>
public sealed record CashLadderShockDto(string ShockKind, JsonElement Parameters);

/// <summary>Operator-authored named liquidity scenario (an ordered list of flow transforms).</summary>
public sealed record CashLadderScenarioDto(
    string ScenarioId,
    string DisplayName,
    string Description,
    IReadOnlyList<CashLadderShockDto> Shocks,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>Aggregated contribution of one flow type within a bucket.</summary>
public sealed record CashLadderFlowSliceDto(
    string FlowType, string AssetClass, decimal Amount, int FlowCount);

public sealed record CashLadderBucketDto(
    DateOnly BucketStart,
    DateOnly BucketEnd,
    decimal Inflows,
    decimal Outflows,
    decimal NetFlow,
    decimal ProjectedEndingCash,
    IReadOnlyList<CashLadderFlowSliceDto> Slices);

/// <summary>Complete ladder for one currency (no FX conversion in V1).</summary>
public sealed record CashLadderCurrencyViewDto(
    string Currency, decimal OpeningCash, IReadOnlyList<CashLadderBucketDto> Buckets);

/// <summary>Lineage link from the ladder to one per-security projection that fed it.</summary>
public sealed record CashLadderSourceRunDto(
    Guid SecurityId,
    string DisplayName,
    Guid? ProjectionRunId,
    string EngineVersion,
    string SourceKind,          // e.g. "MIAC", "ClientProvided", "asset-obligation-projection-v1"
    bool RateScenarioApplied,
    int FlowCount);

/// <summary>Position that produced no projectable flows, with the reason.</summary>
public sealed record CashLadderExclusionDto(Guid SecurityId, string DisplayName, string Reason);

public sealed record PortfolioCashLadderDto(
    Guid LadderRunId,
    DateOnly AsOf,
    int HorizonDays,
    CashLadderBucketing Bucketing,
    StructuredCashFlowScenario RateScenario,
    string? ShockScenarioId,
    string EngineVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CashLadderCurrencyViewDto> Currencies,
    IReadOnlyList<CashLadderSourceRunDto> SourceRuns,
    IReadOnlyList<CashLadderExclusionDto> Exclusions);

public sealed record CashLadderRunSummaryDto(
    Guid LadderRunId,
    DateOnly AsOf,
    StructuredCashFlowScenario RateScenario,
    string? ShockScenarioId,
    DateTimeOffset GeneratedAt,
    int SecurityCount,
    int ExclusionCount);

/// <summary>A held position as the ladder engine consumes it.</summary>
public sealed record PortfolioHoldingDto(
    Guid SecurityId,
    string DisplayName,
    string AssetClass,
    decimal Quantity,
    string Currency);

/// <summary>Opening cash per currency as of the ladder date.</summary>
public sealed record PortfolioCashPositionDto(string Currency, decimal Balance);
```

### New Interfaces — same file or sibling `IPortfolioCashLadderService.cs`

```csharp
public interface IPortfolioCashLadderService
{
    /// <summary>Builds, persists, and returns a new ladder run.</summary>
    Task<PortfolioCashLadderDto> BuildAsync(CashLadderRequestDto request, CancellationToken ct = default);

    Task<PortfolioCashLadderDto?> GetRunAsync(Guid ladderRunId, CancellationToken ct = default);

    Task<IReadOnlyList<CashLadderRunSummaryDto>> ListRunsAsync(int take = 50, CancellationToken ct = default);
}

/// <summary>Seam over the ledger (or a future positions service) for holdings and opening cash.</summary>
public interface IPortfolioHoldingsSource
{
    Task<IReadOnlyList<PortfolioHoldingDto>> GetHoldingsAsync(DateOnly asOf, CancellationToken ct = default);

    Task<IReadOnlyList<PortfolioCashPositionDto>> GetCashPositionsAsync(DateOnly asOf, CancellationToken ct = default);
}

public interface ICashLadderScenarioStore
{
    Task<IReadOnlyList<CashLadderScenarioDto>> ListAsync(CancellationToken ct = default);
    Task<CashLadderScenarioDto?> GetAsync(string scenarioId, CancellationToken ct = default);
    Task UpsertAsync(CashLadderScenarioDto scenario, CancellationToken ct = default);
}
```

### Shock kinds (string constants, V1)

```csharp
public static class CashLadderShockKinds
{
    public const string DelayFlows = "DelayFlows";     // { "days": 30, "flowTypes": ["Interest"] }
    public const string HaircutFlows = "HaircutFlows"; // { "fraction": 0.25, "assetClasses": ["DirectLoan"] }
    public const string DropFlows = "DropFlows";       // { "flowTypes": [...], "securityIds": [...] }
    public const string InjectFlow = "InjectFlow";     // { "dueDate": "...", "amount": -5000000, "currency": "USD", "label": "Redemption wave" }
}
```

Unknown shock kinds fail the build with a `ConfigurationException` — never silently skipped.

### Configuration Schema

```csharp
public sealed class PortfolioForecastingOptions
{
    public const string SectionName = "PortfolioForecasting";

    public int DefaultHorizonDays { get; init; } = 90;
    public int MaxHorizonDays { get; init; } = 370;
    public int MaxParallelProjections { get; init; } = 4;
    public int RunRetentionCount { get; init; } = 200;
}
```

```json
{
  "PortfolioForecasting": {
    "DefaultHorizonDays": 90,
    "MaxHorizonDays": 370,
    "MaxParallelProjections": 4,
    "RunRetentionCount": 200
  }
}
```

Registered with `IOptionsMonitor<PortfolioForecastingOptions>` (ADR-011).

### REST API Surface — `src/Meridian.Ui.Shared/Endpoints/PortfolioForecastingEndpoints.cs`

```
POST /api/portfolio/cash-ladder/runs
  Body: CashLadderRequestDto
  200: PortfolioCashLadderDto
  400: { "error": "HorizonDays exceeds MaxHorizonDays (370)" } (also invalid scenario id / unknown shock kind)

GET /api/portfolio/cash-ladder/runs?take=50
  200: CashLadderRunSummaryDto[]

GET /api/portfolio/cash-ladder/runs/{ladderRunId}
  200: PortfolioCashLadderDto | 404

GET /api/portfolio/cash-ladder/scenarios
  200: CashLadderScenarioDto[]

PUT /api/portfolio/cash-ladder/scenarios/{scenarioId}
  Body: CashLadderScenarioDto
  200: CashLadderScenarioDto (upserted; UpdatedAt/UpdatedBy stamped server-side)
```

All DTOs must be wired into the source-generated JSON context consumed by the shared endpoint
layer (ADR-014) — follow whatever context `WorkstationEndpoints` uses today; do not fall back to
reflection serialization.

---

## Component Design

### PortfolioCashLadderService

**Namespace:** `Meridian.Application.PortfolioForecasting`
**Type:** `sealed class PortfolioCashLadderService : IPortfolioCashLadderService`
**Lifetime:** Singleton
**Engine version constant:** `public const string EngineVersion = "portfolio-cash-ladder-v1";`

**Responsibilities:**
- Resolve holdings and opening cash from `IPortfolioHoldingsSource`.
- Fan out per-security flow resolution with bounded parallelism; normalize both source shapes into
  internal `LadderFlow` rows (positive = inflow to the portfolio).
- Apply the shock scenario, bucket per currency, compute cumulative projected cash.
- Persist the run via `PortfolioCashLadderRunStore` and return the DTO.

**Dependencies (constructor-injected):**
- `IPortfolioHoldingsSource holdingsSource`
- `ISecurityMasterCashFlowService structuredCashFlows`
- `IAssetOperationsQueryService assetOperations`
- `ICashLadderScenarioStore scenarioStore`
- `PortfolioCashLadderRunStore runStore`
- `IOptionsMonitor<PortfolioForecastingOptions> options`
- `ILogger<PortfolioCashLadderService> logger`

**Flow-resolution order per holding:**
1. `structuredCashFlows.GetProjectionAsync(securityId, request.RateScenario, ct)` — if a schedule
   returns, map `StructuredCashFlowScheduleEntry` → `LadderFlow` rows (`FlowType` = `"Principal"` /
   `"Interest"`, currency from the holding, `RateScenarioApplied = true`, `SourceKind` from the
   projection's `StructuredCashFlowSourceKind`).
2. Else `assetOperations.GetOperationsAsync(securityId, ct)` — take
   `AssetOperationsDetailDto.ProjectedCashFlows` (`AssetProjectedCashFlowDto`), keep `FlowType`,
   `DueDate`, `Amount`, `Currency`, lineage `ProjectionRunId`; `RateScenarioApplied = false`.
3. Else add `CashLadderExclusionDto(securityId, displayName, reason)` — reasons are fixed strings:
   `"NoStructuredSource"`, `"NoSecurityMasterSubject"`, `"NoProjectedFlows"`,
   `"ProjectionFailed: {message}"`.

Only flows with `AsOf < DueDate <= AsOf + HorizonDays` are included (strictly after as-of prevents
double-counting against opening cash; past-due flows are the variance surface's job, not the
ladder's). Flows whose `Status` marks them cancelled/superseded are skipped.

**Concurrency model:** `Parallel.ForEachAsync(holdings, new ParallelOptions { MaxDegreeOfParallelism = options.CurrentValue.MaxParallelProjections, CancellationToken = ct }, ...)`
collecting into a `ConcurrentBag<SecurityLadderResult>`; deterministic ordering (by security
display name, then date) applied after collection so identical inputs produce byte-identical runs.

**Error handling:** per-security failures become exclusions, never abort the run;
`OperationCanceledException` always rethrows (repo guard pattern). Invalid request parameters
throw `ConfigurationException` before any work starts. Structured logging throughout
(`"Built cash ladder {LadderRunId} covering {SecurityCount} positions with {ExclusionCount} exclusions in {ElapsedMs}ms"`).

### CashLadderShockEngine

**Namespace:** `Meridian.Application.PortfolioForecasting`
**Type:** `static class CashLadderShockEngine` — pure, no dependencies, fully unit-testable.

```csharp
public static IReadOnlyList<LadderFlow> Apply(
    CashLadderScenarioDto scenario, IReadOnlyList<LadderFlow> flows);
```

Shocks apply in declared order. `DelayFlows` shifts `DueDate` (flows delayed past the horizon drop
out and are counted in a `DelayedBeyondHorizon` slice metadata field). `HaircutFlows` scales
matched amounts. `DropFlows` removes matches. `InjectFlow` appends a synthetic flow with
`SourceKind = "Scenario"`. An empty shock list is the identity function.

### PortfolioCashLadderRunStore

**Namespace:** `Meridian.Application.PortfolioForecasting`
**Type:** `sealed class PortfolioCashLadderRunStore`
**Lifetime:** Singleton

Writes `DataRoot/forecasting/cash-ladder/{ladderRunId}.json` plus `runs-index.json` through
`AtomicFileWriter` (ADR-007 — never `File.WriteAllText`), serialized with the source-generated
context (ADR-014). Enforces `RunRetentionCount` by deleting oldest runs on write. Reads are
lock-free; writes serialize through a `SemaphoreSlim(1,1)`.

### CashLadderScenarioFileStore : ICashLadderScenarioStore

Same persistence discipline, `DataRoot/forecasting/cash-ladder/scenarios.json`. Ships with two
built-in read-only scenarios (`"delayed-receivables-30d"`, `"redemption-wave-10pct"`) so the UI
selector is never empty.

### LedgerPortfolioHoldingsSource : IPortfolioHoldingsSource

**Namespace:** `Meridian.Application.PortfolioForecasting`

Adapter over `IReadOnlyLedger` (`SummarizeAccounts` / `GetRunningBalance`) deriving per-security
quantities from position accounts and per-currency cash balances as of the ladder date. The exact
account-convention mapping is Open Question 2 — the seam exists precisely so this adapter can be
corrected or replaced without touching the engine.

### DI Registration — `src/Meridian.Application/Composition/Features/PortfolioForecastingFeatureRegistration.cs`

Follows the existing `IServiceFeatureRegistration` pattern (sibling of
`LedgerFeatureRegistration`): registers options, `IPortfolioHoldingsSource`,
`ICashLadderScenarioStore`, `PortfolioCashLadderRunStore`, and `IPortfolioCashLadderService` as
singletons.

---

## Data Flow

### Build Ladder (happy path)

1. Operator sets as-of / horizon / bucketing / rate scenario / shock scenario in the Cash Ladder
   view and clicks **Build**.
2. Dashboard view-model `POST`s `CashLadderRequestDto` to `/api/portfolio/cash-ladder/runs`.
3. Endpoint validates horizon against `PortfolioForecastingOptions` and delegates to
   `IPortfolioCashLadderService.BuildAsync`.
4. Service loads holdings + opening cash from `IPortfolioHoldingsSource`.
5. Bounded fan-out resolves flows per security: structured projection first, terms projection
   fallback, exclusion otherwise.
6. `CashLadderShockEngine.Apply` transforms the merged flow set (identity when no scenario).
7. Flows bucket per currency; cumulative `ProjectedEndingCash` accrues from opening cash.
8. `PortfolioCashLadderRunStore` persists the immutable run document; retention prunes old runs.
9. DTO returns; the view renders the ladder, KPI strip, coverage, and exclusions banner.

### Build Ladder (error path)

1–4 as above.
5. A security's structured lookup throws a provider error → caught (OCE excluded), logged
   structured, recorded as `"ProjectionFailed: {message}"` exclusion; run continues.
6. If the shock scenario id is unknown or contains an unrecognized `ShockKind`, the build fails
   fast with 400 before any persistence — a half-applied scenario must never produce a persisted
   run.
7. If holdings resolution itself fails, 500 with a structured error; nothing is persisted.

---

## UI Design — Browser Workstation (Portfolio workspace)

New screen triple following the existing dashboard convention
(`portfolio-screen.tsx` / `.view-model.ts` / tests):

- `src/Meridian.Ui/dashboard/src/screens/portfolio-cash-ladder-screen.tsx`
- `src/Meridian.Ui/dashboard/src/screens/portfolio-cash-ladder-screen.view-model.ts`
- `portfolio-cash-ladder-screen.test.tsx`, `.view-model.test.ts`, `.a11y.test.tsx`

Routed as a section inside the **Portfolio** workspace (top-level navigation stays at the seven
canonical workspaces).

**Layout (top to bottom):**

- **Control row:** as-of date picker · horizon select (30/90/180/370) · bucketing select ·
  rate-scenario select (default Base) · shock-scenario select (default None) · **Build** button ·
  currency tabs (one per `CashLadderCurrencyViewDto`).
- **KPI strip (secondary):** opening cash · net flow over horizon · projected trough (date +
  amount, amber when negative) · coverage (`SourceRuns.Count` vs holdings, with
  rate-scenario-applied share when a non-Base scenario is selected).
- **Primary chart:** stacked bars per bucket — inflow slices above the axis, outflows below,
  colored by `FlowType`; cumulative `ProjectedEndingCash` line overlaid. Calm default state; no
  animation unless data changes.
- **Exclusions banner (amber, only when `Exclusions.Length > 0`):** "N positions not projected" —
  expands to the reasons table.
- **Drill panel (right side, on bucket click):** slices for that bucket → expand a slice to
  per-security contributions → each row links to the existing asset-operations detail view via
  `SecurityId` (`SourceRuns` provides the lineage). Progressive disclosure: the ladder first,
  per-security evidence on demand.
- **Run history dropdown:** previously persisted runs by `GeneratedAt` + scenario labels;
  selecting one renders it read-only (no rebuild).

**View-model state:** request parameters, `phase: idle | building | loaded | failed`, selected
currency, selected bucket, ladder DTO, scenario list. All fetches go through the existing
dashboard API client with abort-signal wiring.

**XAML Design:** N/A in this slice — WPF parity is an explicit follow-up; the shared contracts and
endpoints are the parity seam.

---

## Test Plan

**Principle:** mock at the Contracts interface boundary (`IPortfolioHoldingsSource`,
`ISecurityMasterCashFlowService`, `IAssetOperationsQueryService`); the shock engine and bucketing
math are pure and tested without mocks. Market-realistic fixtures: a small multi-asset book (bond
with semiannual coupons, amortizing loan, private-fund commitment, equity with no flows, one
unmastered position).

### Unit Tests — CashLadderShockEngine (`tests/Meridian.Tests/PortfolioForecasting/CashLadderShockEngineTests.cs`)

| Test Name | What It Verifies |
|-----------|------------------|
| Apply_EmptyScenario_ReturnsIdenticalFlows | identity behavior |
| Apply_DelayFlows_ShiftsDueDatesAndDropsBeyondHorizon | date shift + horizon drop-out accounting |
| Apply_HaircutFlows_ScalesOnlyMatchedFlowTypes | filter targeting, amount precision |
| Apply_DropFlows_RemovesBySecurityIdAndAssetClass | filter combinations |
| Apply_InjectFlow_AppendsSyntheticScenarioFlow | synthetic flow with `SourceKind = "Scenario"` |
| Apply_ShocksApplyInDeclaredOrder | haircut-then-delay ≠ delay-then-haircut fixture |
| Apply_UnknownShockKind_Throws | fail-fast contract |

### Unit Tests — PortfolioCashLadderService (`PortfolioCashLadderServiceTests.cs`)

| Test Name | What It Verifies |
|-----------|------------------|
| BuildAsync_PrefersStructuredProjectionOverTermsFallback | source-resolution order; `RateScenarioApplied` flags |
| BuildAsync_FallsBackToAssetOperationsFlows | fallback path with `ProjectionRunId` lineage retained |
| BuildAsync_RecordsExclusionWhenNoSourceProjects | exclusion reasons; run still succeeds |
| BuildAsync_ProviderThrow_BecomesExclusionNotFailure | per-security fault isolation; OCE still propagates |
| BuildAsync_FiltersFlowsToStrictHorizonWindow | `AsOf < DueDate <= AsOf+H` — no double-count vs opening cash |
| BuildAsync_BucketsWeeklyAndMonthlyOnCalendarBoundaries | bucketing math including partial final bucket |
| BuildAsync_CumulativeCashAccruesPerCurrencyFromOpeningBalance | per-currency isolation, no FX blending |
| BuildAsync_DeterministicOrdering_SameInputsSameOutput | byte-stable run documents |
| BuildAsync_HorizonAboveMax_ThrowsConfigurationException | options validation |
| BuildAsync_UnknownShockScenarioId_FailsBeforePersistence | no half-applied persisted runs |
| BuildAsync_PersistsRunAndListRunsReturnsSummary | store round-trip via temp DataRoot |
| BuildAsync_CancellationPropagates | `ct` honored mid-fan-out |

### Unit Tests — LedgerPortfolioHoldingsSource + stores

| Test Name | What It Verifies |
|-----------|------------------|
| GetHoldingsAsync_DerivesQuantitiesFromSeededLedger | account-convention mapping |
| GetCashPositionsAsync_ReturnsPerCurrencyBalancesAsOfDate | as-of correctness |
| RunStore_WriteIsAtomicAndRetentionPrunesOldest | ADR-007 compliance, retention |
| ScenarioStore_UpsertPersistsAndBuiltInsAlwaysPresent | built-in scenarios survive |

### Endpoint Tests (`tests/Meridian.Tests/Ui/PortfolioForecastingEndpointTests.cs`, following `WorkstationStreamEndpointTests` host pattern)

| Test Name | What It Verifies |
|-----------|------------------|
| PostRun_ReturnsLadderAndPersists | 200 contract, JSON context serialization |
| PostRun_InvalidHorizon_Returns400 | validation surface |
| GetRun_UnknownId_Returns404 | not-found contract |
| PutScenario_RoundTripsThroughGet | scenario CRUD |

### Dashboard Tests (vitest)

| Test Name | What It Verifies |
|-----------|------------------|
| view-model builds request and transitions idle→building→loaded | fetch lifecycle |
| currency tab switch re-derives chart series without refetch | client-side derivation |
| exclusions banner renders only when exclusions exist | calm default state |
| bucket click populates drill panel with slice lineage | drill-down wiring |

### Test Infrastructure Needed

- `FakePortfolioHoldingsSource`, `FakeStructuredCashFlowService`, `FakeAssetOperationsQueryService`
  fixtures under `tests/Meridian.Tests/PortfolioForecasting/`.
- Temp-directory DataRoot fixture for store tests (pattern exists in storage tests).

---

## Implementation Checklist

**Estimated effort:** High — ~3 weeks / 15 working days for one developer.
**Suggested branch name:** `codex/portfolio-cash-ladder`
**Suggested PR sequence:** PR1 contracts + engine + stores + unit tests · PR2 endpoints +
registration + endpoint tests · PR3 dashboard screen + tests + doc index updates.

### Phase 1: Contracts (1d)
- [ ] Add `Meridian.Contracts/PortfolioForecasting/PortfolioForecastingDtos.cs` (all DTOs + enums above)
- [ ] Add `IPortfolioCashLadderService`, `IPortfolioHoldingsSource`, `ICashLadderScenarioStore`, `CashLadderShockKinds`
- [ ] Wire DTOs into the endpoint-layer source-generated JSON context (ADR-014)

### Phase 2: Engine (5d)
- [ ] Implement `CashLadderShockEngine` (pure) + internal `LadderFlow` model with sign policy (positive = inflow)
- [ ] Implement `PortfolioCashLadderService.BuildAsync` fan-out, two-tier sourcing, exclusions, bucketing, cumulative math
- [ ] Resolve Open Question 1 (flow scaling) against real store contents before wiring the structured path
- [ ] Implement `PortfolioCashLadderRunStore` + `CashLadderScenarioFileStore` on `AtomicFileWriter` with retention
- [ ] Implement `LedgerPortfolioHoldingsSource` (resolve Open Question 2 account conventions)
- [ ] Add `PortfolioForecastingFeatureRegistration` + `PortfolioForecastingOptions` registration

### Phase 3: Endpoints (2d)
- [ ] Add `PortfolioForecastingEndpoints` (5 routes) following the sibling `*Endpoints` registration pattern
- [ ] Validation: horizon bounds, unknown scenario id, unknown shock kind → 400 with structured error body

### Phase 4: Dashboard (5d)
- [ ] `portfolio-cash-ladder-screen.view-model.ts` (request state machine, currency/bucket selection, series derivation)
- [ ] `portfolio-cash-ladder-screen.tsx` (control row, KPI strip, stacked-bar + cumulative chart, exclusions banner, drill panel, run history)
- [ ] Route registration inside the Portfolio workspace navigation
- [ ] `npm --prefix src/Meridian.Ui/dashboard run test` and `run build` green

### Phase 5: Tests (2d, interleaved)
- [ ] All unit/endpoint tests in the tables above (~27 tests); `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true` filtered to `FullyQualifiedName~PortfolioForecasting` green
- [ ] Dashboard vitest suite green

### Phase 6: Wrap-up
- [ ] `appsettings.json` + `appsettings.Development.json` gain the `PortfolioForecasting` section defaults
- [ ] XML doc comments on all public contracts
- [ ] Roadmap registry: add the wave-8 item for this lane (`docs/roadmap/data/roadmap-items.yml`)
- [ ] ADR check — additive feature, no ADR amendment expected; confirm ADR-007/011/014 compliance in review
- [ ] Index this blueprint in `docs/product/README.md`; `bash scripts/ci.sh` before PR

---

## Open Questions

| # | Question | Owner | Impact if Unresolved |
|---|----------|-------|---------------------|
| 1 | Are `AssetProjectedCashFlowDto.Amount` and structured schedule amounts position-scaled or per-unit-of-face (does `Factor` require quantity multiplication)? | Implementer (verify store contents + `BuildProjectedCashFlowsFromSecurityTerms`) | Ladder magnitudes wrong by position size — the single riskiest assumption; resolve in Phase 2 before UI work |
| 2 | Which ledger account convention identifies position quantities and cash balances for `LedgerPortfolioHoldingsSource`? | Implementer + product | Holdings adapter can't be written; seam isolates the blast radius |
| 3 | Should ladder runs register into the Evidence Vault lane (W5X-EVIDENCE-001, in progress)? | Product | Missed evidence integration; additive later |
| 4 | Scenario authoring UX — JSON `PUT` only in V1, or a form editor? | Product/UX | V1 ships JSON-only; editor is a fast follow |
| 5 | WPF parity timing for the Cash Ladder view | Product | Browser-only until scheduled; contracts are the parity seam |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Projection coverage gaps make ladders misleading | Med | High | Exclusions are first-class in the DTO, banner, and coverage KPI — never silent |
| Flow-scaling semantics misread (OQ1) | Med | High | Resolve before Phase 4; record `SourceKind` + engine versions per source run so bad runs are attributable |
| Large books build slowly | Low | Med | Options-bound parallelism; persisted runs mean operators reload, not rebuild |
| Rate scenario silently ignored for terms-fallback positions | Med | Med | `RateScenarioApplied` per source run; UI shows the rate-responsive share |
| Double-counting settled flows against opening cash | Low | High | Strict `(AsOf, AsOf+H]` window; tested explicitly |
| Engine drift between per-asset and ladder runs | Low | Med | Ladder run records its own `EngineVersion` plus every source run's version |
