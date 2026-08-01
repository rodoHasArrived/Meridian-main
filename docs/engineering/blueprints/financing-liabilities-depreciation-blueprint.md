# Blueprint — Repo Engine, Depreciation Schedule, and Borrower-Side Debt

**Status:** Partially implemented — the depreciation *calculation core* shipped (no persistence,
endpoints, or UI); repo and borrower-side debt remain design-only
**Owner:** Accounting and Ledger lane
**Reviewed:** 2026-08-01

## Delivery state (2026-08-01)

Engine 2 (**fixed-asset depreciation**) has its *calculation core* in source. Treat only these
sections as built, and verify against the live types rather than re-deriving them:

- `src/Meridian.Ledger/DepreciationScheduleCalculator.cs` + `IDepreciationScheduleCalculator.cs`,
  `DepreciationMethod.cs`, `DepreciationInput.cs`, `DepreciationPeriod.cs`,
  `DepreciationProjection.cs`.
- `src/Meridian.Ledger/FixedAssetDepreciationProjector.cs`, `FixedAssetDepreciationDraftBuilder.cs`.
- `LedgerAccounts.AccumulatedDepreciationFor` / `DepreciationExpenseFor`.
- **`AutomatedJournalEventKind.DepreciationPosted` is already on the enum — do not re-append it.**

Engine 2's **persistence and operator surfaces are not built**, so Phase 2 is *partially* complete.
Absent from source: `FixedAssetRecordDto` / `DepreciationMethodDto` / `DepreciationPeriodDto` (the
wire contracts — note the domain types above ship without a `Dto` counterpart),
`IFixedAssetRegisterStore` and its Postgres migration, the posted-through watermark,
`FixedAssetDepreciationService`, any depreciation route in `UiApiRoutes`, the dashboard `types.ts`
DTOs, and the accounting-screen read model. Depreciation is computable but not yet storable,
postable through the governed draft path, or operable.

Engines 1 (**repo / reverse-repo**) and 3 (**borrower-side term debt**) are design-only: there is no
`Meridian.Application.Financing` project, no repo or borrowing projector, and no
`RepoInterestAccrued` / `BorrowingInterestAccrued` / `DebtIssuanceCostAmortized` enum member.
Phases 3–4 of the implementation checklist are therefore untouched.

> **Shared-convention notice.** This blueprint appends to the shared `AutomatedJournalEventKind`
> enum and adds ledger routes. Ordinals, DDL precision, and route prefixes are recorded in the
> canonical [blueprint register](README.md#shared-conventions).

This blueprint translates three prioritized financing/accounting capabilities into code-ready
technical designs that plug into Meridian's existing projector → ledger → approval pipeline:

1. **Repo / Reverse-Repo engine** — lifecycle and daily financing accrual for repurchase agreements.
2. **Fixed-Asset Depreciation engine** — an asset register plus periodic depreciation schedules.
3. **Borrower-side term debt** — the fund/entity modeled as a *borrower* (credit facility, term
   loan, note payable).

All three reuse the same posting spine used today by `LoanAccountingProjector`,
`FixedIncomeAmortizationProjector`, `AutomatedJournalDraftProjector`, and `AutomatedJournalApproval`.
Posting math and account classification stay in `Meridian.Ledger`; event-to-line mapping for the
financing instruments lives in the application layer, consistent with
`src/Meridian.Application/DirectLending/LoanAccountingProjector.cs`.

---

## Scope

**In Scope:**

1. **Repo/Reverse-Repo engine** — lifecycle plus daily financing accrual for repurchase agreements
   (fund as cash-borrower) and reverse repos (fund as cash-lender), projected to the ledger.
2. **Fixed-Asset Depreciation engine** — an asset register plus a periodic depreciation schedule
   (straight-line, declining-balance, units-of-production) projected to the ledger.
3. **Borrower-side term debt** — the fund/entity as *borrower* (credit facility, term loan, note
   payable): drawdown, interest accrual, fee amortization, principal repayment, retirement.

**Out of Scope:** total-return swaps / synthetic financing (separate blueprint); collateral
optimization / margin-call automation for repo (only the accrual + book legs here); tax-basis
depreciation books (single-book v1, extensible via `AccountingBasisKindDto`); secondary-market
valuation of the debt.

**Assumptions:**

- The `AutomatedJournalApproval` submit → approve → post lifecycle and
  `ILedgerJournalStore` / `LedgerJournalEntryWrite` remain the durable posting path (as verified in
  `LoanAccountingProjector.cs`).
- New instruments reuse the Security Master identity/lineage seam the way DirectLending does; a
  fixed-asset register is a *new* subject type that does **not** require a Security Master security.
- `LedgerAccountType` stays a 5-value enum; contra accounts are modeled as normal-type accounts
  carrying the opposite balance, exactly as `PremiumAmortization` already credits an `Asset`
  carrying-value account in `FixedIncomeAmortizationProjector.cs`.

**Depth Mode:** `full` (backend-focused; UI is read-model + endpoint surface only).

---

## Breaking Change Notice

**None to existing public interfaces.** All three engines are **additive**:

- New `AutomatedJournalEventKind` enum members (append-only; the `switch` in
  `AutomatedJournalDraftProjector.ProjectLines` already throws `ArgumentOutOfRangeException` on
  unknown kinds, so appending is safe).
- New `LedgerAccounts` factory methods (append-only static members).
- New projectors, aggregates, services, stores, and contracts — no signature changes to
  `JournalEntry`, `LedgerEntry`, `ILedgerJournalStore`, `IAccountingPolicyService`, or
  `AutomatedJournalApproval`.

One **soft** change is proposed and gated behind Open Question #1: generalizing
`Meridian.FSharp.Ledger/AccrualTypes.fs::AccrualEntry` (today keyed by `LoanId`) so repo and
borrowings can reuse the accrual primitive. The default recommendation is a **non-breaking sibling
type** rather than renaming `LoanId`.

---

## Architectural Overview

### Context diagram

```
                        SOURCE EVENTS (per engine)
   repo.opened / repo.accrued / repo.rolled / repo.closed
   depreciation.period-run
   borrowing.drawn / borrowing.interest-accrued / borrowing.principal-repaid / borrowing.retired
                                   |
        +--------------------------+--------------------------+
        v                          v                          v
  RepoAgreementProjector   FixedAssetDepreciation-    BorrowingAccountingProjector
  (Meridian.Application.     Projector                (Meridian.Application.
   Financing)               (Meridian.Ledger)          Financing)
        |                          |                          |
        |        balanced (LedgerAccount, debit, credit) lines |
        +--------------------------+--------------------------+
                                    v
              LedgerAccounts  <--- new well-known accounts
                                    |
             +----------------------+-----------------------+
             v                                              v
   AutomatedJournalDraft --> AutomatedJournalApproval   JournalEntry --> LedgerJournalEntryWrite
   (governed: depreciation)   (submit/approve/reject/     (direct durable post via
                               post)                       ILedgerJournalStore, like loans)
             +---------------------------+-----------------+
                                         v
                          Ledger > TrialBalance > LedgerFinancialStatementBuilder
                                         v
                            NavAttributionService (Assets - Liabilities)
```

### Design decisions

- **Decision:** Two posting paths, chosen per engine. Repo daily accrual and borrowings interest
  post **directly and durably** via `ILedgerJournalStore` + `LedgerJournalEntryWrite` (the
  `LoanAccountingProjector` path). Depreciation period runs go through a **governed
  `AutomatedJournalDraft` + `AutomatedJournalApproval`** (the `DailyPortfolioPricingDraftBuilder`
  path).
  **Alternatives considered:** all-governed; all-direct.
  **Rationale:** Depreciation is a periodic, judgment-bearing close entry an accountant should
  approve (mirrors `PeriodCloseDraftBuilder` / daily mark). High-frequency loan-style accruals are
  deterministic and already post directly for DirectLending — consistency beats novelty.
  **Consequences:** Depreciation inherits locked-period plus approval evidence for free; repo and
  borrowings inherit period-resolution and policy binding from the loan pattern.

- **Decision:** Repo and Borrowings live in a **new `Meridian.Application.Financing`** area and a
  **new `Meridian.FSharp.Financing.Aggregates`** F# project, siblings to `DirectLending`.
  **Alternatives considered:** fold into `DirectLending`; put projectors in `Meridian.Ledger`.
  **Rationale:** The module-map boundary allows projection logic in the application layer that
  *depends on* `Meridian.Ledger`; `LoanAccountingProjector` already lives in
  `Meridian.Application.DirectLending`. Repo and borrowings are liability-side financing,
  conceptually distinct from private-credit assets.
  **Consequences:** Keeps `AccountingSemanticBoundaryTests` green — posting math stays in
  `Meridian.Ledger`, event-to-line mapping in the application layer.

- **Decision:** The depreciation projector lives **in `Meridian.Ledger`** next to
  `FixedIncomeAmortizationProjector`, as a pure static projector.
  **Rationale:** It is the same shape as premium/discount amortization (contra against a carrying
  account) and requires no external identity — it belongs to the ledger's accounting-math ownership.
  **Consequences:** The fixed-asset *register* (a small entity store) lives in the
  application/storage layers; only the balanced-line math is in the ledger.

- **Decision:** Model **accumulated depreciation** and **repo/debt principal** as normal-type
  accounts carrying contra/natural balances (`AccumulatedDepreciation` = credit-balance `Asset`;
  `RepoFinancingPayable` / `DebtPrincipalPayable` = `Liability`).
  **Rationale:** No enum change; `LedgerFinancialStatementBuilder` already nets by account type.
  Matches the existing premium-amortization-credits-`Asset` precedent.
  **Consequences:** Statement labels must recognize the contra account by name (small addition);
  tracked as Open Question #2.

---

## Interface and API Contracts

### New well-known ledger accounts — `Meridian.Ledger/LedgerAccounts.cs` (append)

```csharp
// ---- Repo / reverse-repo financing ----
/// <summary>Obligation to repurchase collateral sold under a repo (fund borrowed cash).</summary>
public static LedgerAccount RepoFinancingPayableFor(string repoId) =>
    CreateScoped("Repo Financing Payable", LedgerAccountType.Liability, repoId);
/// <summary>Interest expense accrued on a repo financing leg.</summary>
public static LedgerAccount RepoInterestExpenseFor(string repoId) =>
    CreateScoped("Repo Interest Expense", LedgerAccountType.Expense, repoId);
/// <summary>Interest payable accrued but not yet settled on a repo.</summary>
public static LedgerAccount RepoInterestPayableFor(string repoId) =>
    CreateScoped("Repo Interest Payable", LedgerAccountType.Liability, repoId);
/// <summary>Cash advanced to a counterparty under a reverse repo (fund lent cash).</summary>
public static LedgerAccount ReverseRepoReceivableFor(string repoId) =>
    CreateScoped("Reverse Repo Receivable", LedgerAccountType.Asset, repoId);
/// <summary>Interest income accrued on a reverse-repo leg.</summary>
public static LedgerAccount ReverseRepoInterestIncomeFor(string repoId) =>
    CreateScoped("Reverse Repo Interest Income", LedgerAccountType.Revenue, repoId);
public static LedgerAccount ReverseRepoInterestReceivableFor(string repoId) =>
    CreateScoped("Reverse Repo Interest Receivable", LedgerAccountType.Asset, repoId);

// ---- Fixed-asset depreciation ----
/// <summary>Gross cost of a capitalized fixed asset.</summary>
public static LedgerAccount FixedAssetCostFor(string assetId) =>
    CreateScoped("Fixed Asset Cost", LedgerAccountType.Asset, assetId);
/// <summary>Contra-asset: accumulated depreciation (carries a credit balance).</summary>
public static LedgerAccount AccumulatedDepreciationFor(string assetId) =>
    CreateScoped("Accumulated Depreciation", LedgerAccountType.Asset, assetId);
/// <summary>Periodic depreciation expense for a fixed asset.</summary>
public static LedgerAccount DepreciationExpenseFor(string assetId) =>
    CreateScoped("Depreciation Expense", LedgerAccountType.Expense, assetId);

// ---- Borrower-side term debt ----
/// <summary>Principal owed under a borrowing facility / term loan / note payable.</summary>
public static LedgerAccount DebtPrincipalPayableFor(string borrowingId) =>
    CreateScoped("Debt Principal Payable", LedgerAccountType.Liability, borrowingId);
/// <summary>Interest expense on borrowings.</summary>
public static LedgerAccount InterestExpenseFor(string borrowingId) =>
    CreateScoped("Interest Expense", LedgerAccountType.Expense, borrowingId);
/// <summary>Interest payable accrued but not yet paid on borrowings.</summary>
public static LedgerAccount InterestPayableFor(string borrowingId) =>
    CreateScoped("Interest Payable", LedgerAccountType.Liability, borrowingId);
/// <summary>Unamortized debt issuance costs (contra-liability, carries a debit balance).</summary>
public static LedgerAccount UnamortizedDebtIssuanceCostFor(string borrowingId) =>
    CreateScoped("Unamortized Debt Issuance Cost", LedgerAccountType.Liability, borrowingId);
/// <summary>Amortization of debt issuance costs into expense.</summary>
public static LedgerAccount DebtIssuanceCostAmortizationFor(string borrowingId) =>
    CreateScoped("Debt Issuance Cost Amortization", LedgerAccountType.Expense, borrowingId);
```

### New `AutomatedJournalEventKind` members — `Meridian.Ledger/AutomatedJournalEventKind.cs` (append)

```csharp
/// <summary>Periodic fixed-asset depreciation charge.</summary>
DepreciationPosted,          // ALREADY SHIPPED — present on the enum; do not re-append
/// <summary>Repo financing interest accrued for a period slice.</summary>
RepoInterestAccrued,
/// <summary>Borrowing interest accrued for a period slice.</summary>
BorrowingInterestAccrued,
/// <summary>Debt issuance cost amortized for a period.</summary>
DebtIssuanceCostAmortized,
```

`DepreciationPosted` landed with the depreciation engine and sits before the capital-call members
added by the [commitment & capital-call blueprint](../../development/accounting-blueprints/commitment-and-capital-call-engine.md).
Append the three financing kinds **after** the current tail; the enum is append-only and shared
across blueprints.

> Only `DepreciationPosted` is wired through `AutomatedJournalDraftProjector` (governed path). The
> repo/borrowing kinds are carried on events for classification/idempotency, but their lines are
> produced by their own projectors (direct-post path), matching how DirectLending sets
> `ActivityType` without routing through `AutomatedJournalDraftProjector`.

### 1 — Repo engine contracts — `Meridian.Contracts.Financing.Repo`

```csharp
public enum RepoLegKindDto { Repo, ReverseRepo }          // fund borrows cash / lends cash
public enum RepoStatusDto { Draft, Open, Rolled, Closed, Defaulted }

public sealed record RepoAgreementTermsDto(
    Guid RepoId,
    RepoLegKindDto Leg,
    string CounterpartyId,
    string Currency,
    decimal Principal,               // cash leg notional
    decimal RepoRate,                // annualized
    string DayCountBasis,            // reuse DirectLending DayCountBasis strings
    DateOnly StartDate,
    DateOnly MaturityDate,
    decimal HaircutPercent,          // collateral over-pledge
    string? CollateralSecurityId = null,
    decimal? CollateralMarketValue = null);

public sealed record RepoAccrualSliceDto(
    Guid RepoId, DateOnly AccrualDate,
    DateOnly PeriodStart, DateOnly PeriodEnd,
    decimal InterestAmount, string Currency, Guid SourceEventId);
```

```csharp
/// <summary>Commands + queries for repo/reverse-repo agreements.</summary>
public interface IRepoAgreementService
{
    ValueTask<RepoAgreementDetailDto> OpenAsync(RepoAgreementTermsDto terms, RepoWriteMetadata meta, CancellationToken ct = default);
    ValueTask<RepoAgreementDetailDto> AccrueDailyAsync(Guid repoId, DateOnly asOf, RepoWriteMetadata meta, CancellationToken ct = default);
    ValueTask<RepoAgreementDetailDto> RollAsync(Guid repoId, DateOnly newMaturity, decimal? newRate, RepoWriteMetadata meta, CancellationToken ct = default);
    ValueTask<RepoAgreementDetailDto> CloseAsync(Guid repoId, DateOnly asOf, RepoWriteMetadata meta, CancellationToken ct = default);
    ValueTask<RepoAgreementDetailDto?> GetAsync(Guid repoId, CancellationToken ct = default);
    // Read/query surface — required so background accrual schedulers can discover repos that
    // need AccrueDailyAsync, and so UI read-models can list open agreements.
    ValueTask<IReadOnlyList<RepoAgreementSummaryDto>> ListByStatusAsync(RepoStatusDto status, CancellationToken ct = default);
    ValueTask<IReadOnlyList<RepoAgreementSummaryDto>> ListAccruableAsync(DateOnly asOf, CancellationToken ct = default);
}

/// <summary>Projects a repo lifecycle event into balanced ledger journal writes (mirrors LoanAccountingProjector).</summary>
public interface IRepoAgreementProjector
{
    Task<IReadOnlyList<LedgerJournalEntryWrite>> ProjectAsync(
        Guid repoId, RepoAgreementDetailDto detail, string eventType,
        DateOnly? effectiveDate, JsonDocument payload, Guid sourceEventId,
        RepoWriteMetadata metadata, CancellationToken ct);
}
```

### 2 — Depreciation contracts — `Meridian.Contracts.Ledger` (projector I/O, mirrors `FixedIncomeAmortizationInput`)

```csharp
public enum DepreciationMethodDto { StraightLine, DecliningBalance, UnitsOfProduction }

/// <summary>A capitalized asset in the fixed-asset register.</summary>
public sealed record FixedAssetRecordDto(
    Guid AssetId, string DisplayName, string Currency,
    decimal Cost, decimal SalvageValue,
    DateOnly InServiceDate, int UsefulLifeMonths,
    DepreciationMethodDto Method,
    decimal DecliningBalanceFactor = 2.0m,         // 200% = double-declining
    long? TotalUnits = null,                        // units-of-production only
    string? FinancialAccountId = null);
```

```csharp
// Meridian.Ledger — pure projector (static), sibling of FixedIncomeAmortizationProjector
public sealed record DepreciationInput(
    Guid AssetId, LedgerAccount CostAccount, decimal DepreciationAmount,
    string? FinancialAccountId = null, string? Description = null);

public sealed record DepreciationProjection(
    Guid AssetId, string Description,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public decimal TotalDebits  => Lines.Sum(static l => l.debit);
    public decimal TotalCredits => Lines.Sum(static l => l.credit);
    public bool IsBalanced      => TotalDebits == TotalCredits;
}

/// <summary>Generates a period-by-period depreciation schedule for an asset.</summary>
public interface IDepreciationScheduleCalculator
{
    // StraightLine and DecliningBalance produce a full forward schedule from the asset record alone.
    // UnitsOfProduction is usage-driven: pass the projected (or actual) units for each period —
    // when null, the calculator returns only the StraightLine/DecliningBalance schedule and reports
    // that UnitsOfProduction requires per-period unit input (it cannot be projected forward blindly).
    IReadOnlyList<DepreciationPeriodDto> BuildSchedule(
        FixedAssetRecordDto asset,
        IReadOnlyList<long>? projectedUnitsPerPeriod = null);
}
public sealed record DepreciationPeriodDto(
    int PeriodIndex, DateOnly PeriodEnd,
    decimal OpeningNetBookValue, decimal DepreciationAmount, decimal ClosingNetBookValue);
```

### 3 — Borrower-side debt contracts — `Meridian.Contracts.Financing.Borrowings`

```csharp
public enum BorrowingKindDto { RevolvingCredit, TermLoan, NotePayable, SubscriptionLine }
public enum BorrowingStatusDto { Draft, Active, Repaying, Retired, Defaulted }

public sealed record BorrowingTermsDto(
    Guid BorrowingId, BorrowingKindDto Kind, string LenderId, string Currency,
    decimal CommitmentAmount, decimal DrawnAmount,
    string RateType,                    // "Fixed" | "Floating" (reuse DirectLending semantics)
    decimal FixedRate, string? FloatingIndex, decimal FloatingSpread,
    string DayCountBasis, DateOnly StartDate, DateOnly MaturityDate,
    decimal IssuanceCost = 0m, string? FinancialAccountId = null);

public interface IBorrowingService
{
    ValueTask<BorrowingDetailDto> DrawAsync(Guid borrowingId, decimal amount, DateOnly asOf, BorrowingWriteMetadata meta, CancellationToken ct = default);
    ValueTask<BorrowingDetailDto> AccrueInterestAsync(Guid borrowingId, DateOnly asOf, BorrowingWriteMetadata meta, CancellationToken ct = default);
    ValueTask<BorrowingDetailDto> RepayPrincipalAsync(Guid borrowingId, decimal amount, DateOnly asOf, BorrowingWriteMetadata meta, CancellationToken ct = default);
    // RetireAsync is a status-only lifecycle marker (see the projector table below); it closes a
    // facility whose principal is already zero and posts no journal lines.
    ValueTask<BorrowingDetailDto> RetireAsync(Guid borrowingId, DateOnly asOf, BorrowingWriteMetadata meta, CancellationToken ct = default);
    // Read/query surface — required for background interest-accrual scheduling and UI read-models.
    ValueTask<BorrowingDetailDto?> GetAsync(Guid borrowingId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<BorrowingSummaryDto>> ListByStatusAsync(BorrowingStatusDto status, CancellationToken ct = default);
    ValueTask<IReadOnlyList<BorrowingSummaryDto>> ListAccruableAsync(DateOnly asOf, CancellationToken ct = default);
}

public interface IBorrowingAccountingProjector   // mirrors LoanAccountingProjector, liability-side
{
    Task<IReadOnlyList<LedgerJournalEntryWrite>> ProjectAsync(
        Guid borrowingId, BorrowingDetailDto detail, string eventType,
        DateOnly? effectiveDate, JsonDocument payload, Guid sourceEventId,
        BorrowingWriteMetadata metadata, CancellationToken ct);
}
```

### F# accrual reuse — `Meridian.FSharp.Financing.Accruals` (new; non-breaking)

```fsharp
// Sibling to Meridian.FSharp.Ledger/AccrualTypes.fs — keyed by generic ContractId, not LoanId.
[<CLIMutable>]
type FinancingAccrualEntry =
    { AccrualEntryId: Guid
      ContractId: Guid           // repoId or borrowingId
      ContractKind: string       // "Repo" | "ReverseRepo" | "Borrowing"
      AccrualDate: DateOnly
      PeriodStartDate: DateOnly
      PeriodEndDate: DateOnly
      InterestAmount: decimal
      FeeAmount: decimal
      Currency: string
      SourceEventId: Guid
      RecordedAt: DateTimeOffset }

module FinancingAccrual =
    /// interest = principal * rate * dayCountFraction(basis, start, end)
    let dailyInterest (principal: decimal) (annualRate: decimal) (fraction: decimal) =
        System.Decimal.Round(principal * annualRate * fraction, 6)
```

### Configuration — `Meridian.Application.Financing/FinancingOptions.cs`

```csharp
public sealed class FinancingOptions
{
    public const string SectionName = "Financing";
    public string DefaultDayCountBasis { get; init; } = "Act360";
    public bool RepoInterestSettlesDaily { get; init; } = false;   // false = accrue to payable
    public decimal DefaultDecliningBalanceFactor { get; init; } = 2.0m;
    public bool DepreciationRequiresApproval { get; init; } = true;
}
```

```jsonc
{ "Financing": { "DefaultDayCountBasis": "Act360", "RepoInterestSettlesDaily": false,
                 "DefaultDecliningBalanceFactor": 2.0, "DepreciationRequiresApproval": true } }
```

Register with `services.AddOptions<FinancingOptions>().BindConfiguration(FinancingOptions.SectionName)`
and consume via `IOptionsMonitor<FinancingOptions>` (ADR-011) so financing defaults hot-reload.
`BindConfiguration` (not a manual `Configure(config.GetSection(...))`) registers the reload-token
source, matching the repo convention in
`src/Meridian.Ui.Shared/Services/WorkstationServiceCollectionExtensions.cs`.

### REST surface — `Meridian.Ui.Shared/Endpoints/FinancingEndpoints.cs` (read-model + command intake)

All routes sit under the existing **`/api/ledger/...`** prefix. `UiApiRoutes` has no
`/api/financing/` or `/api/accounting/` prefix, and these surfaces are ledger subsidiary registers —
siblings of `/api/ledger/private-capital/...`
([register](README.md#api-route-prefixes)).

```
POST /api/ledger/financing/repos                 -> open repo/reverse repo   (RepoAgreementTermsDto)
POST /api/ledger/financing/repos/{id}/accrue     -> run daily accrual as-of date
POST /api/ledger/financing/repos/{id}/close
GET  /api/ledger/financing/repos/{id}            -> RepoAgreementDetailDto

POST /api/ledger/financing/borrowings            -> create facility
POST /api/ledger/financing/borrowings/{id}/draw  -> { amount, asOf }
POST /api/ledger/financing/borrowings/{id}/accrue-interest
POST /api/ledger/financing/borrowings/{id}/repay -> { amount, asOf }

GET  /api/ledger/fixed-assets                 -> register list
POST /api/ledger/fixed-assets                 -> capitalize asset (FixedAssetRecordDto)
GET  /api/ledger/fixed-assets/{id}/schedule   -> DepreciationPeriodDto[]
POST /api/ledger/fixed-assets/depreciate      -> { periodEnd } -> submits governed draft(s)
```

---

## Component Design

### RepoAgreementProjector

- **Namespace:** `Meridian.Application.Financing`
- **Type:** `sealed class RepoAgreementProjector : IRepoAgreementProjector`
- **Lifetime:** Scoped
- **Dependencies (constructor-injected):** `ILedgerJournalStore journalStore`,
  `IAccountingPolicyService accountingPolicyService`, `IOptionsMonitor<FinancingOptions> options`,
  `ILogger<RepoAgreementProjector> logger`

**Responsibilities:** map repo events to balanced lines; resolve posting period plus accounting
policy (reuse the `LoanAccountingProjector.ResolvePostingPeriodAsync` pattern); enforce the
idempotency key `repo:{repoId:N}:{eventType}:{sourceEventId:N}`; attach source evidence.

**Event → lines** (via a local `Add(account, type, debit, credit)` helper like
`LoanAccountingProjector.cs`):

| eventType | Repo leg (fund borrows cash) | Reverse-repo leg (fund lends cash) |
|---|---|---|
| `repo.opened` | Dr `Cash`, Cr `RepoFinancingPayable` | Dr `ReverseRepoReceivable`, Cr `Cash` |
| `repo.accrued` | Dr `RepoInterestExpense`, Cr `RepoInterestPayable` | Dr `ReverseRepoInterestReceivable`, Cr `ReverseRepoInterestIncome` |
| `repo.settled` | Dr `RepoInterestPayable`, Cr `Cash` | Dr `Cash`, Cr `ReverseRepoInterestReceivable` |
| `repo.closed` | Dr `RepoFinancingPayable`, Cr `Cash` | Dr `Cash`, Cr `ReverseRepoReceivable` |

**Concurrency / errors:** stateless; throws a `FinancingCommandException` (analog of
`DirectLendingCommandException`) when `journalStore` is null or no period accepts the date (copy the
guard in `LoanAccountingProjector.cs`).

### FixedAssetDepreciationProjector

- **Namespace:** `Meridian.Ledger`
- **Type:** `public static class FixedAssetDepreciationProjector` (pure, mirrors
  `FixedIncomeAmortizationProjector`)
- **Method:** `DepreciationProjection Project(DepreciationInput input)`
- **Lines:** `Dr DepreciationExpense(assetId) / Cr AccumulatedDepreciation(assetId)` for
  `DepreciationAmount`. Validates amount >= 0, non-zero, and that
  `CostAccount.AccountType == Asset` (copy the guard in `FixedIncomeAmortizationProjector.cs`).

**Paired governed builder — `FixedAssetDepreciationDraftBuilder` (Meridian.Ledger):** batches
**all** in-scope asset projections for the period into a **single** `AutomatedJournalDraft` (kind
`AutomatedJournalEventKind.DepreciationPosted`), carrying per-asset lines and per-asset evidence,
then routes that one draft through `AutomatedJournalApproval` — exactly how
`DailyPortfolioPricingDraftBuilder` batches all daily fair-value marks into one governed draft. This
lets an accountant approve the entire period's depreciation run in a single action rather than one
approval per asset.

**DepreciationScheduleCalculator (`Meridian.Ledger`):** pure schedule generator.

- *Straight-line:* `(Cost - Salvage) / UsefulLifeMonths` per month; the final period absorbs the
  rounding remainder so `ClosingNetBookValue == Salvage` exactly.
- *Declining-balance:* `OpeningNBV * (Factor / UsefulLifeMonths)`, floored at Salvage; auto-switch
  to straight-line over the remaining life when SL > DB (standard convention).
- *Units-of-production:* `(Cost - Salvage) * unitsThisPeriod / TotalUnits` (units supplied per run).

### BorrowingAccountingProjector

- **Namespace:** `Meridian.Application.Financing`
- **Type:** `sealed class BorrowingAccountingProjector : IBorrowingAccountingProjector` (structural
  twin of `LoanAccountingProjector`, liability-side)

| eventType | lines |
|---|---|
| `borrowing.drawn` | Dr `Cash`, Cr `DebtPrincipalPayable` (and, if issuance cost: Dr `UnamortizedDebtIssuanceCost`, Cr `Cash`) |
| `borrowing.interest-accrued` | Dr `InterestExpense`, Cr `InterestPayable` |
| `borrowing.interest-paid` | Dr `InterestPayable`, Cr `Cash` |
| `borrowing.principal-repaid` | Dr `DebtPrincipalPayable`, Cr `Cash` |
| `borrowing.issuance-cost-amortized` | Dr `DebtIssuanceCostAmortization`, Cr `UnamortizedDebtIssuanceCost` |
| `borrowing.retired` | *No journal lines.* Status-only lifecycle marker; guarded so it is accepted only when remaining principal, interest payable, and unamortized issuance cost are all zero. The final cash movement is the preceding `borrowing.principal-repaid` (which brings principal to zero), not this event. |

> `borrowing.retired` deliberately produces no lines. Meridian's ledger design filters zero-amount
> lines (see `LoanAccountingProjector.cs`), so a "Dr `DebtPrincipalPayable` 0 / Cr `Cash` 0" entry
> would be dropped to an empty journal entry. Retirement is therefore modeled as a state transition
> on the borrowing aggregate with retained evidence, not a ledger post.

Interest amount = `FinancingAccrual.dailyInterest(drawn, allInRate, dcf)`, reusing the DirectLending
day-count kernel for `dcf` and floating all-in-rate resolution.

### Persistence

- `IRepoAgreementStateStore` / `IBorrowingStateStore` plus `PostgresRepoAgreementStateStore` /
  `PostgresBorrowingStateStore` in `Meridian.Storage.Financing` (mirror
  `PostgresDirectLendingStateStore`; migration files under `Meridian.Storage/Financing/Migrations/`).
- `IFixedAssetRegisterStore` plus `PostgresFixedAssetRegisterStore` in `Meridian.Storage.Ledger`
  (small: asset record plus a posted-through-period watermark for idempotent re-runs).

---

## Data Flow

### Repo daily accrual (happy path)

1. Scheduler/operator calls `IRepoAgreementService.AccrueDailyAsync(repoId, asOf, meta)`.
2. The service loads `RepoAgreementDetailDto`, computes `dcf` via the DirectLending day-count
   kernel, and builds `RepoAccrualSliceDto` interest.
3. It emits a `repo.accrued` event with `payload = { interestAmount }` and a `sourceEventId`.
4. `RepoAgreementProjector.ProjectAsync` resolves the period plus policy and produces balanced lines
   (Dr `RepoInterestExpense` / Cr `RepoInterestPayable`), idempotency-keyed.
5. `ILedgerJournalStore` persists the `LedgerJournalEntryWrite` (dedup on idempotency key).
6. The trial balance now carries the expense plus payable; `NavAttributionService` nets the payable,
   so NAV falls by the accrued financing cost.

### Depreciation period run (governed path)

1. Operator issues `POST /api/ledger/fixed-assets/depreciate { periodEnd }`.
2. `FixedAssetDepreciationService` loads the register and filters to assets that are in service, not
   fully depreciated, and not already posted through `periodEnd`.
3. For each in-scope asset: `DepreciationScheduleCalculator.BuildSchedule` -> this period's amount
   -> `FixedAssetDepreciationProjector.Project`. `FixedAssetDepreciationDraftBuilder` then
   **batches every asset's lines into one submitted `AutomatedJournalApproval` draft** for the
   period (not one draft per asset).
4. The accountant approves the single period draft; it posts to `ILedgerJournalStore`; the register
   watermark advances to `periodEnd` for every asset included in that draft.

### Error path — locked period (all engines)

- Posting date falls in a `LockedAccountingPeriod`. `LedgerPeriodPostingGuard` / the period resolver
  returns no postable period.
- The projector throws `FinancingCommandException` (repo/borrowings) or the draft is rejected at
  approval (depreciation); the service surfaces a validation error to the endpoint; there is no
  partial post (a single `LedgerJournalEntryWrite` per event is atomic).

---

## UI Design

**Backend + workstation feature.** UI ships across both active lanes per `CLAUDE.md`; WPF parity is tracked as `W8-WPF-PARITY-001`.
Surface additions are read-model tiles on the existing **Accounting** screen
(`src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts`): a "Financing & Fixed
Assets" section listing open repos, borrowings, and the fixed-asset register with net book value. No
new top-level navigation (stays within `Accounting`). TypeScript `types.ts` gains mirrored DTOs.

---

## Test Plan

**Principle:** test projectors as pure functions (assert exact balanced lines plus `IsBalanced`);
test schedule math against hand-computed golden values; mock `ILedgerJournalStore` /
`IAccountingPolicyService` at the interface boundary; add F# property tests for accrual/schedule
invariants.

### Unit — RepoAgreementProjector (`tests/Meridian.Tests/Financing/RepoAgreementProjectorTests.cs`)

| Test | Verifies |
|---|---|
| `Project_RepoOpened_DebitsCashCreditsFinancingPayable` | opening leg direction for `Repo` |
| `Project_ReverseRepoOpened_DebitsReceivableCreditsCash` | opposite direction for `ReverseRepo` |
| `Project_RepoAccrued_PostsInterestExpenseAndPayable` | accrual lines plus balanced |
| `Project_RepoClosed_UnwindsPrincipal` | close nets `RepoFinancingPayable` to zero |
| `Project_NoPostablePeriod_Throws` | locked/absent period guard |
| `Project_SameSourceEvent_IdempotencyKeyStable` | dedup key format |

### Unit — DepreciationScheduleCalculator (`tests/Meridian.Tests/Ledger/DepreciationScheduleCalculatorTests.cs`)

| Test | Verifies |
|---|---|
| `StraightLine_FullLife_SumsToCostMinusSalvage` | sum of depreciation == Cost - Salvage; final NBV == Salvage |
| `StraightLine_FinalPeriodAbsorbsRounding` | no penny drift |
| `DecliningBalance_SwitchesToStraightLine` | auto-switch convention |
| `DecliningBalance_NeverBelowSalvage` | salvage floor |
| `UnitsOfProduction_ProratesByUsage` | units math |

### Unit — FixedAssetDepreciationProjector

| Test | Verifies |
|---|---|
| `Project_PostsExpenseAndAccumulatedDepreciation` | Dr Expense / Cr AccumDep, balanced |
| `Project_NonAssetCostAccount_Throws` | account-type guard |
| `Project_ZeroAmount_Throws` | mirrors the fixed-income projector guard |

### Unit — BorrowingAccountingProjector (`tests/Meridian.Tests/Financing/BorrowingAccountingProjectorTests.cs`)

| Test | Verifies |
|---|---|
| `Project_Drawn_DebitsCashCreditsPrincipalPayable` | drawdown |
| `Project_InterestAccrued_ExpenseAndPayable` | liability-side accrual |
| `Project_PrincipalRepaid_ReducesPayable` | repayment |
| `Project_IssuanceCostAmortized_ReducesUnamortizedContra` | issuance-cost amortization |
| `Project_Retired_RequiresZeroPrincipal` | retirement guard |

### F# — `tests/Meridian.FSharp.Tests/FinancingAccrualTests.fs`

| Test | Verifies |
|---|---|
| `dailyInterest matches principal*rate*fraction` | rounding to 6 dp |
| `accrual entries validate non-negative amounts` | invariant parity with `AccrualEntry.validate` |

### Integration (flag: deferred one sprint)

| Test | Verifies |
|---|---|
| `RepoAccrual_PostsThroughLedgerStore_AndReducesNav` | Postgres store plus `NavAttributionService` end-to-end |
| `Depreciation_GovernedDraft_ApprovePost_Roundtrip` | `AutomatedJournalApproval` submit -> approve -> post |
| `Borrowing_FullLifecycle_TrialBalanceReturnsToZero` | draw -> accrue -> repay -> retire nets flat |

### Test infrastructure needed

- `InMemoryLedgerJournalStore` test double (may already exist for DirectLending tests — reuse).
- Fixture golden JSON for full straight-line and declining-balance schedules under
  `tests/fixtures/financing/`.

---

## Implementation Checklist

**Estimated effort:** XL — roughly 3–4 weeks for one developer across all three engines.
**Suggested branch:** `claude/liabilities-depreciation-integration-9ldf0k`.
**Suggested PR sequence:** PR1 Depreciation (self-contained, in-ledger) -> PR2 Repo engine ->
PR3 Borrowings.

### Phase 1: Foundation (all PRs)

- [ ] Append the new `LedgerAccounts` factory methods plus XML docs.
- [ ] Append the `AutomatedJournalEventKind` members.
- [ ] Add `FinancingOptions` plus the `appsettings` section plus DI registration via
      `AddOptions<FinancingOptions>().BindConfiguration(FinancingOptions.SectionName)` (not manual
      `Configure`) so `IOptionsMonitor` hot-reload works.

### Phase 2: Depreciation (PR1) — partially shipped (calculation core only)

- [ ] `FixedAssetRecordDto`, `DepreciationMethodDto`, `DepreciationPeriodDto` contracts.
- [x] `DepreciationScheduleCalculator` + `FixedAssetDepreciationProjector` +
      `FixedAssetDepreciationDraftBuilder`.
- [ ] `IFixedAssetRegisterStore` + Postgres impl + migration + posted-through watermark.
- [ ] `FixedAssetDepreciationService`; wire the `DepreciationPosted` governed draft path.
- [ ] Endpoints + `types.ts` DTOs + accounting-screen read model.

**What actually landed:** the in-memory calculation core only — `DepreciationScheduleCalculator`
(+ `IDepreciationScheduleCalculator`), `FixedAssetDepreciationProjector`, and
`FixedAssetDepreciationDraftBuilder`, all in `src/Meridian.Ledger/`, plus the
`AutomatedJournalEventKind.DepreciationPosted` member and the `AccumulatedDepreciationFor` /
`DepreciationExpenseFor` account factories from Phase 1.

**What remains in this phase:** every persistence and operator-facing slice. There is no fixed-asset
DTO contract, no `IFixedAssetRegisterStore` or its migration, no posted-through watermark, no
`FixedAssetDepreciationService`, no route in `UiApiRoutes`, no `types.ts` DTO, and no
accounting-screen read model. Depreciation can be *computed* today; it cannot yet be *stored,
posted through the governed draft path, or operated*. The repo/borrowing halves of Phase 1 also
remain.

### Phase 3: Repo engine (PR2)

- [ ] `Meridian.FSharp.Financing.Accruals` project + `FinancingAccrualEntry` + `dailyInterest`;
      register in interop.
- [ ] Repo contracts; `RepoAgreementProjector`; `IRepoAgreementService` + in-memory + Postgres
      services.
- [ ] `IRepoAgreementStateStore` + Postgres store + migration.
- [ ] Endpoints + read model.

### Phase 4: Borrowings (PR3)

- [ ] Borrowing contracts; `BorrowingAccountingProjector`; `IBorrowingService` impls.
- [ ] `IBorrowingStateStore` + Postgres store + migration.
- [ ] Reuse the DirectLending day-count kernel for `dcf` / floating rate.
- [ ] Endpoints + read model.

### Phase 5: Tests

- [ ] All unit + F# tests above; >= 80% on new code.
- [ ] Integration tests (may defer to a follow-up sprint — flagged).

### Phase 6: Wrap-up

- [ ] Confirm `AccountingSemanticBoundaryTests` allowlist covers the new application-layer
      projectors (posting math stayed in `Meridian.Ledger`).
- [ ] `docs/domain/` note: repo and borrowings as financing subjects; update `LedgerAccounts` and
      module READMEs.
- [ ] Run `python3 build/scripts/ai-repo-updater.py known-errors`; `bash scripts/ci.sh`.
- [ ] XML docs on all public surfaces; no `.Result` / `.Wait()`; structured logging only.

---

## Open Questions

| # | Question | Owner | Impact if Unresolved |
|---|---|---|---|
| 1 | Reuse/extend `AccrualTypes.fs::AccrualEntry` (keyed by `LoanId`) or add the sibling `FinancingAccrualEntry`? Recommendation: sibling (non-breaking). | Implementer + Ledger owner | Touching `LoanId` is a breaking F# change to loan accrual consumers. |
| 2 | How should `LedgerFinancialStatementBuilder` present contra accounts (`AccumulatedDepreciation`, `UnamortizedDebtIssuanceCost`) — net against parent or as a separate line? | Product / Accounting | Balance-sheet presentation correctness; `AccountingEquationVariance` must still net to zero (contras share the parent's type, so it does). |
| 3 | Does repo need collateral/margin-call modeling in v1, or is book-the-cash-leg-plus-accrual sufficient? Recommendation: accrual-only v1; collateral as memo. | Product | Scope creep into margin-call automation. |
| 4 | Multi-book/basis (GAAP vs tax depreciation) in v1? Recommendation: single `Primary` basis, extensible via `AccountingBasisKindDto`. | Product | Rework if a tax book is demanded later. |
| 5 | Should borrowings integrate with `MarginLoanPayable` (unify all fund leverage) or stay a distinct facility concept? | Architecture | Duplicate leverage representations if not reconciled. |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `AccountingSemanticBoundaryTests` fails if posting logic drifts into the application layer | Med | High | Keep all `LedgerAccount` / balance math in `Meridian.Ledger`; projectors only *map* events to existing accounts. Verify the allowlist in Phase 6. |
| Depreciation penny-drift over asset life | Med | Med | Final-period rounding absorption; golden-fixture sum tests. |
| Repo direction sign errors (borrow vs lend legs inverted) | Med | High | Explicit per-leg projector tests; the `RepoLegKindDto` split makes direction total. |
| Idempotency gaps cause double-posting on retry | Low | High | Stable idempotency-key format plus store-level dedup (copied from DirectLending). |
| F# interop regeneration friction for the new accrual project | Low | Med | Isolate to one small module; follow the `Interop.DirectLending.fs` precedent. |
| Locked-period late postings for back-dated accruals | Med | Med | Route through `LedgerPeriodPostingGuard`; use the `Adjustment` posting kind plus approval for prior-period corrections. |

---

## Related Source

- `src/Meridian.Ledger/LedgerAccounts.cs`, `LedgerAccountType.cs`, `AutomatedJournalEventKind.cs`,
  `AutomatedJournalDraftProjector.cs`, `FixedIncomeAmortizationProjector.cs`,
  `DailyPortfolioPricingDraftBuilder.cs`, `PeriodCloseProjector.cs`,
  `LedgerFinancialStatementBuilder.cs`
- `src/Meridian.Application/DirectLending/LoanAccountingProjector.cs`
- `src/Meridian.FSharp.Ledger/AccrualTypes.fs`
- `src/Meridian.Reporting/NavAttributionService.cs`
- `docs/architecture/module-map.md` (Operational Record Boundaries)
