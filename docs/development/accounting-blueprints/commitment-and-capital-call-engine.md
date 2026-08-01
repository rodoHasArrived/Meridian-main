# Blueprint: Commitment & Capital-Call Engine for Private Capital

**Status:** Partially implemented — domain layer and capital-call draft *construction* shipped; the
posting/orchestration layer that makes a call operable, plus persistence, endpoints, and the
workbench read model, remain design-only
**Owner:** Ledger / private-capital lane
**Reviewed:** 2026-08-01

> Scope: new subsystem that layers investor **commitments**, **drawdown schedules**,
> **uncalled-commitment roll-forward**, **recallable distributions**, and **default / late-interest**
> handling on top of Meridian's existing private-capital ledger projection and governed
> automated-journal pipeline. No mobile lane. Browser workstation is the UI target (WPF parity
> follows the shared seam).

## Delivery state (2026-08-01)

Already in source — treat §4.1–§4.5 and **§7.2 step 1** as built, and verify against the live types
rather than re-deriving them:

- `src/Meridian.Ledger/PrivateCapitalCommitments.cs` — `CommitmentStatus`,
  `DrawdownInstallmentStatus`, `DistributionRecallability`, commitment and installment records
  (§4.1–§4.3), plus the §4.5 default-interest records (`CapitalCallDefault`,
  `DefaultInterestAccrual`, `DefaultInterestConvention`).
- `src/Meridian.FinancialOperations/PrivateCapital/CommitmentRollForwardCalculator.cs` — the
  `net-called + uncalled + expired = total` invariant carrier (§4.4).
- `src/Meridian.FinancialOperations/PrivateCapital/DefaultInterestCalculator.cs` — **§4.5 default
  and late-interest handling is shipped**, not outstanding: `ComputeSimpleInterest`, the
  `Thirty360Days` day-count, and `Evaluate`, with `DefaultInterestCalculatorTests` covering them.
  Do not re-implement it from the §4.5 sketch.
- `src/Meridian.Ledger/CapitalCallDraftFactory.cs`, `CapitalCallPlanBuilder.cs`,
  `CapitalCallScheduleDraftBuilder.cs` — **§7.2 step 1 only: draft *construction*.** These return
  balanced `AutomatedJournalDraft`s and stop there. A repo-wide check finds them referenced only by
  each other, `src/Meridian.Ledger/README.md`, and their tests — **no service consumes them**, so
  §7.2 steps 2–5 (approval, durable posting, projection-driven installment transition, funding
  orchestration) are **not** built. Capital calls are draftable today, not operable.
- `AutomatedJournalEventKind.CapitalCallIssued` / `CapitalCallFunded` /
  `CapitalCallDefaultInterestAccrued` are already on the enum — **do not re-append them**.

Still design-only: **§7.2 steps 2–5 (the posting/orchestration layer that makes a call operable)**,
persistence and stores (§6), endpoints (§8.3), and the commitment workbench read model (§8.2, §8.4).

> **Shared-convention notice.** This blueprint shares the ledger migration sequence and the
> `AutomatedJournalEventKind` enum with the
> [incentive-fee](incentive-fee-mechanics.md) and [equalization](equalization-and-series-accounting.md)
> blueprints. Migration ordinals, DDL precision, and route prefixes are recorded in the canonical
> [blueprint register](../../engineering/blueprints/README.md#shared-conventions).

---

## 1. Summary

Meridian already reconstructs private-capital capital-account activity (capital calls,
distributions, subscriptions, redemptions, management fees) **from posted journal entries and
in-flight drafts** through `PrivateCapitalFundEventLedgerProjector` and
`PrivateCapitalActivityProjectionBuilder`, and surfaces it through the
`CapitalAccountWorkbenchService`. What it does **not** yet have is the *master data* that gives
those movements meaning against an investor's obligation: the **total commitment**, the
**drawdown schedule** that plans calls against it, the **uncalled commitment** that rolls forward
across calls, the **recallable** classification that lets a return-of-capital distribution restore
uncalled commitment, and the **default / late-interest** treatment for an LP that misses a call.

This engine adds a commitment registry plus three pure calculators (roll-forward, recallable
restoration, default interest) that fold the *existing* projected fund events against commitments,
enforce the roll-forward invariant `net-called + uncalled + expired = total`, and emit governed
`AutomatedJournalDraft`s for the postings the engine originates (drawdown-driven capital calls and
late-interest accruals). Calls and distributions continue to post through the same
`AutomatedJournalApproval` submit → approve → post lifecycle and land back in the projector via
`TreasuryLedgerContextDto` metadata, so the commitment layer never forks ledger state — it decorates
the capital-account subledger and the capital-account workbench with commitment context.

---

## 2. Grounding in current code (real references)

Every design decision below is anchored to code that exists today. Exact signatures quoted.

### 2.1 Manual-journal activity types (the private-capital verbs)

`src/Meridian.Contracts/Ledger/AccountingConfigurationDtos.cs`:

```csharp
public enum ManualJournalEntryTypeDto
{
    General = 0, AccruedBalance = 1, AccruedExpense = 2, PrepaidExpense = 3, Expense = 4,
    Amortization = 5, Deferral = 6, Reclassification = 7, Reversal = 8,
    CapitalCall = 9, Distribution = 10, Subscription = 11, Redemption = 12,
    LpTransfer = 13, ManagementFee = 14, ClosingEntry = 15
}

public enum ManualJournalEntryStatusDto
{
    Draft = 0, NeedsFix = 1, Submitted = 2, Approved = 3, Rejected = 4,
    Posted = 5, Reversed = 6, Rebooked = 7, CloseLocked = 8
}

public enum PrivateCapitalFundEventLedgerReadinessDto
{
    Blocked = 0, EvidenceMissing = 1, ApprovalPending = 2, PostingReview = 3,
    ReportReview = 4, Ready = 5, Published = 6
}
```

The treasury context is the metadata seam that ties a manual journal to a private-capital fund
event (`AccountingConfigurationDtos.cs`):

```csharp
public sealed record TreasuryLedgerContextDto(
    DateOnly? EffectiveDate = null,
    string? IdempotencyKey = null,
    string? FundEventId = null,
    string? FundEventType = null,
    string? CapitalAccountId = null,
    string? InvestorId = null,
    string? PaymentIntentId = null,
    string? SettlementReference = null);
```

`ManualJournalEntryDraftDto` (same file) carries `EntryType`, `TreasuryContext`, `Currency`,
`EvidenceLinks`, `ApprovalId`, and lifecycle timestamps — this is the object the projection reads.

### 2.2 The projector (posted-journal reconstruction)

`src/Meridian.Ledger/PrivateCapitalFundEventLedgerProjector.cs`:

- `PrivateCapitalFundEventLedgerProjector.Project(IReadOnlyLedger ledger, IReadOnlyList<LedgerFinancialReportPack>? reportPacks = null, LedgerQuery? query = null)` groups every journal whose metadata `HasPrivateCapitalContext(...)` by `BuildEventGroupKey` (`metadata.FundEventId`), and builds a `PrivateCapitalFundEventLedgerEvent` per group.
- Capital-account impact is derived from **equity lines only**: `BuildCapitalAccountImpacts(...)` filters `line.Account.AccountType == LedgerAccountType.Equity` and routes the net through `Ledger.CalculateNetBalance(LedgerAccountType.Equity, debits, credits)`.
- Posting readiness is a hard gate:

```csharp
public bool IsPostingReady =>
    !HasCriticalIssues && HasEvidence && IsApprovalComplete &&
    LedgerImpacts.Count > 0 && LedgerImpacts.All(static item => item.IsBalanced) &&
    CapitalAccountImpacts.Count > 0;
```

- `BuildIssues(...)` raises **Critical** issues for missing `FundEventId`, `FundEventType`,
  `CapitalAccountId`, `EffectiveDate`, `IdempotencyKey`, missing evidence, unbalanced impact, and
  missing capital-account impact. **Any new engine posting must satisfy all of these or it will be
  flagged non-posting-ready.** The projector already reads arbitrary metadata tags through
  `FirstTagText(entries, key)` and evidence via the `evidenceLinks` / `approvalEvidenceLinks` /
  `sourceEvidenceLinks` / `settlementEvidenceLinks` tag keys — our new commitment linkage rides
  these same tag channels.

### 2.3 The projection builder (draft + posted merge, net-activity sign, running balance)

`src/Meridian.FinancialOperations/PrivateCapital/PrivateCapitalActivityProjectionBuilder.cs`:

```csharp
private static decimal CalculateNetCapitalActivity(ManualJournalEntryTypeDto entryType, decimal grossAmount)
    => entryType switch
    {
        ManualJournalEntryTypeDto.CapitalCall   => grossAmount,
        ManualJournalEntryTypeDto.Subscription  => grossAmount,
        ManualJournalEntryTypeDto.Distribution  => -grossAmount,
        ManualJournalEntryTypeDto.Redemption    => -grossAmount,
        ManualJournalEntryTypeDto.ManagementFee => -grossAmount,
        _ => 0m
    };
```

Note **`LpTransfer` and `General` net to `0`** today — a transfer moves commitment/interest between
LPs without changing fund-level called capital. The engine treats `LpTransfer` as a
commitment-ownership move (Section 4.6), not a call/return.

- Per-capital-account **running balance** is built in `BuildCapitalAccountSubledgerEntries(...)`
  (`runningNetActivity += item.NetCapitalActivity`), landing on
  `PrivateCapitalCapitalAccountSubledgerEntryDto.RunningNetActivity`. The commitment roll-forward
  runs the *same* fold with an additional uncalled-commitment accumulator.
- `BuildCapitalAccounts(...)` aggregates `Contributions` from `CapitalCall`, `Distributions` from
  `Distribution`, etc. as `Math.Abs(NetCapitalActivity)` — the commitment layer consumes these
  aggregates directly off `PrivateCapitalCapitalAccountActivityDto`.
- Drafts are skipped (with warning `manual-je.private-capital-context-pending`) unless the treasury
  context has `EffectiveDate`, `FundEventId`, `FundEventType`, and `CapitalAccountId`.

### 2.4 Capital-account contracts and workbench read model

`src/Meridian.Contracts/Ledger/AccountingConfigurationPrivateCapitalDtos.cs` defines
`PrivateCapitalCapitalAccountActivityDto` (`Contributions`, `Distributions`, `Subscriptions`,
`Redemptions`, `ManagementFees`, `NetActivity`), `PrivateCapitalCapitalAccountSubledgerEntryDto`
(`RunningNetActivity`), `PrivateCapitalCapitalAccountSubledgerDto`,
`PrivateCapitalActivityProjectionDto`, and the workbench surface
`CapitalAccountWorkbenchInvestorAccountDto` / `CapitalAccountWorkbenchDto`.

`src/Meridian.Ui.Shared/Services/CapitalAccountWorkbenchService.cs` implements
`ICapitalAccountWorkbenchService.GetWorkbenchAsync(...)` (declared in
`src/Meridian.Contracts/Ledger/AccountingConfigurationCloseReportingDtos.cs`), fetches the
projection via `_manualJournalEntryWorkbenchService.GetPrivateCapitalActivityAsync(...)`, and maps
each subledger to an investor account. This is the surface we extend.

### 2.5 Governed automated-journal draft pattern

`src/Meridian.Ledger/AutomatedJournalDraft.cs`:

```csharp
public sealed record AutomatedJournalDraft(
    AutomatedJournalEvent Event,
    string Description,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)> Lines,
    JournalEntryMetadata Metadata)
{
    public decimal TotalDebits => Lines.Sum(static line => line.debit);
    public decimal TotalCredits => Lines.Sum(static line => line.credit);
    public bool IsBalanced => TotalDebits == TotalCredits;
}
```

`src/Meridian.Ledger/AutomatedJournalApproval.cs` — the governed lifecycle we reuse verbatim:

- `AutomatedJournalApproval.Submit(draft, actor, occurredAtUtc, reason, evidenceLinks = null)` —
  requires `draft.IsBalanced`, allocates `JournalEntryId` + line ids, transitions Draft → Submitted.
- `Approve(actor, occurredAtUtc, reason, evidenceLinks)` — `requireEvidence: true`.
- `Reject(...)`, `PostTo(Ledger ledger, actor, occurredAtUtc, reason, evidenceLinks)` — posts
  `ToJournalEntry()` and transitions Approved → Posted.
- `BuildPostingMetadata()` stamps tags `automatedJournalApprovalId`, `automatedJournalStatus`,
  `approvedBy` — exactly the tags `ResolveApprovalState(...)` reads back in the projector.
- Transition guard `IsAllowedTransition`: `Draft→Submitted`, `Submitted→{Approved,Rejected}`,
  `Approved→Posted`. `AutomatedJournalApprovalStatus` = `Draft, Submitted, Approved, Rejected, Posted`.

### 2.6 Period locks

`src/Meridian.Ledger/LockedAccountingPeriodBook.cs`:
`EnsureCanPost(LedgerBookKey ledgerKey, JournalEntry journalEntry)` throws
`LedgerValidationException` when the entry timestamp falls in a locked period; `Post(...)` and
`PostLines(...)` guard before delegating to the book. **`AutomatedJournalApproval.PostTo` calls
`ledger.Post(...)` directly and does not consult the lock book** — see Open Question O-5.

### 2.7 Persistence convention

`src/Meridian.Storage/Ledger/Migrations/` uses ordered `V_ledger_###__name.sql` files with a
`__SCHEMA__` placeholder, `create table if not exists`, `create index if not exists`, `constraint
ck_...` checks and `unique(...)`. Highest existing is `V_ledger_028__wash_sale_activation.sql`; this
blueprint's reserved range is **031–032**
([register](../../engineering/blueprints/README.md#ledger-migration-ordinals)), so the engine's
first migration is **`V_ledger_031__private_capital_commitments.sql`**. Re-derive the next free
ordinal from disk at implementation time. (Fund-account master data under
`src/Meridian.Storage/FundAccounts/Migrations/` uses the simpler `001_*.sql` convention; policy fork
PF-6 covers store placement.)

### 2.8 Endpoints

`src/Meridian.Contracts/Api/UiApiRoutes.cs`:

```csharp
public const string LedgerPrivateCapitalActivity = "/api/ledger/private-capital/activity";
public const string LedgerPrivateCapitalCapitalAccountWorkbench = "/api/ledger/private-capital/capital-account-workbench";
```

Registered in `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs` with
`app.MapGet(UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench, ...).WithName("GetLedgerPrivateCapitalCapitalAccountWorkbench").Produces<CapitalAccountWorkbenchDto>(...)`.

---

## 3. Policy forks (options + RECOMMENDED default)

| ID | Decision | Options | RECOMMENDED default | Rationale |
|----|----------|---------|---------------------|-----------|
| **PF-1** | Recallable cap basis | (a) uncapped; (b) cap at cumulative distributions returned as return-of-capital; (c) cap at a `% of TotalCommitment` recall limit; (d) time-boxed to the investment period | **(b)+(c) combined**: `restorable ≤ min(cumulativeReturnOfCapital, RecallCapAmount)` where `RecallCapAmount = RecallCapPercent × TotalCommitment` (default `RecallCapPercent = 1.00`) | Matches common LPA language ("recyclable up to 100% of returned capital within the investment period"); (a) breaks the invariant's boundedness, (d) alone under-restricts. |
| **PF-2** | Default-interest day-count | Actual/360, Actual/365 Fixed, 30/360 | **Actual/365 Fixed** | Simplest exact-day accrual; deterministic and widely used for LP default interest. Configurable per commitment. |
| **PF-3** | Default-interest rate source | fixed annual rate; reference (`Prime`/`SOFR`) + spread; tiered penalty schedule | **fixed annual rate on the commitment record** (`DefaultInterestRateAnnual`), with an optional reference+spread override | Avoids a market-data dependency for v1; reference+spread can be layered later without a schema change (nullable columns). |
| **PF-4** | Interest compounding | simple; compounded daily; compounded monthly | **simple interest** to cure date | LPAs overwhelmingly specify simple interest on defaulted calls; compounding is opt-in per commitment. |
| **PF-5** | Uncalled-commitment expiry at investment-period end | (a) expire uncalled (release obligation, `E += uncalled`); (b) retain uncalled (callable post-period for follow-ons/expenses); (c) roll forward to a successor vehicle | **(b) retain**, with an explicit governed `Expire` action that moves the residual to `Expired` only when an operator records period close | Auto-expiry silently destroys callable capital; keep it operator-driven and evidence-backed, consistent with the close-cockpit's governed posture. |
| **PF-6** | Commitment master-data store location | (a) new tables in the Ledger schema (`V_ledger_031__...`); (b) new `src/Meridian.Storage/PrivateCapital/` store | **(a) Ledger schema** | Commitments are scoped by `fund_profile_id` + `ledger_book_id` + `investor_id`, exactly like the private-capital projection; co-locating keeps tenant/company scoping columns and migration tooling identical (see `V_ledger_019/020/021`). |
| **PF-7** | Does the capital call post a subscription receivable, or fund on cash receipt? | (a) memo-only uncalled tracking (no GL until cash); (b) GAAP: DR Capital-Call Receivable / CR Contributed Capital at call, DR Cash / CR Receivable at funding | **(b) receivable posting** | The projector already recognizes the equity leg (`Contributed Capital`) as the capital-account impact; a receivable makes default/aging measurable and drives the default-interest base. |
| **PF-8** | Default-interest credit destination | (a) fund income (CR Interest Income); (b) credited to non-defaulting partners' capital | **(a) fund income** for v1 | Simplest and always defensible; (b) is a re-allocation policy that belongs to a later allocation-engine blueprint. |

---

## 4. Domain model / new types

All new types follow the repo house style: `sealed record` with positional parameters, `decimal`
money, `DateOnly` for accounting dates, `DateTimeOffset` for audit stamps, `IReadOnlyList<T>`,
`[JsonConverter(typeof(JsonStringEnumConverter<T>))]` on enums. Domain types live in
`src/Meridian.Ledger/PrivateCapitalCommitments.cs` (alongside the projector); wire DTOs live in
`src/Meridian.Contracts/Ledger/`.

### 4.1 Enums

```csharp
namespace Meridian.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<CommitmentStatus>))]
public enum CommitmentStatus
{
    Active = 0,               // within investment period, callable
    InvestmentPeriodClosed = 1,
    FullyCalled = 2,          // uncalled == 0
    Defaulted = 3,            // one or more installments in default
    Expired = 4,             // residual uncalled released (PF-5)
    Closed = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<DrawdownInstallmentStatus>))]
public enum DrawdownInstallmentStatus
{
    Scheduled = 0,   // planned only
    Noticed = 1,     // drawdown notice issued to LP
    Called = 2,      // capital-call journal posted (receivable raised)
    Funded = 3,      // cash received in full
    PartiallyFunded = 4,
    Defaulted = 5,   // due date passed, unfunded
    Cured = 6,       // default cured (funded late + interest)
    Waived = 7,
    Cancelled = 8
}

[JsonConverter(typeof(JsonStringEnumConverter<DistributionRecallability>))]
public enum DistributionRecallability
{
    NonRecallable = 0,          // permanent return of profit / capital
    RecallableReturnOfCapital = 1  // restores uncalled commitment (subject to PF-1 cap)
}

[JsonConverter(typeof(JsonStringEnumConverter<DefaultInterestConvention>))]
public enum DefaultInterestConvention
{
    Actual365Fixed = 0,  // RECOMMENDED default (PF-2)
    Actual360 = 1,
    Thirty360 = 2
}
```

### 4.2 Commitment record (per investor)

```csharp
public sealed record InvestorCommitment(
    string CommitmentId,          // stable id, e.g. "commitment:{fundProfileId}:{investorId}:{seq}"
    string FundProfileId,
    Guid? LedgerBookId,
    string CapitalAccountId,      // ties to PrivateCapital* CapitalAccountId
    string InvestorId,
    string Currency,              // ISO 4217, upper-invariant
    decimal TotalCommitment,      // T, > 0
    DateOnly CommitmentDate,
    DateOnly? InvestmentPeriodEndDate,
    CommitmentStatus Status,
    decimal RecallCapPercent,     // PF-1, default 1.00m
    decimal DefaultInterestRateAnnual, // PF-3, e.g. 0.10m == 10%/yr
    DefaultInterestConvention DefaultInterestConvention, // PF-2
    int DefaultGraceDays,         // days after due date before default interest starts
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? TenantId = null,
    string? CompanyId = null)
{
    public bool IsCallable => Status is CommitmentStatus.Active or CommitmentStatus.InvestmentPeriodClosed;
}
```

### 4.3 Drawdown schedule + installments

```csharp
public sealed record DrawdownSchedule(
    string ScheduleId,
    string CommitmentId,
    string FundProfileId,
    Guid? LedgerBookId,
    DateOnly EffectiveFrom,
    string Basis,                 // "Percent" | "Amount"
    IReadOnlyList<DrawdownInstallment> Installments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DrawdownInstallment(
    string InstallmentId,         // stable; also becomes the FundEventId prefix
    string CommitmentId,
    int Sequence,
    DateOnly NoticeDate,
    DateOnly DueDate,
    decimal? CallPercent,         // when Basis == "Percent" (of TotalCommitment)
    decimal? CallAmount,          // when Basis == "Amount"
    DrawdownInstallmentStatus Status,
    string? JournalEntryId = null,      // set once the capital-call journal posts
    string? PaymentIntentId = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    // Resolved call amount against a known commitment total.
    public decimal ResolveCallAmount(decimal totalCommitment)
        => CallAmount ?? Math.Round((CallPercent ?? 0m) * totalCommitment, 2, MidpointRounding.ToEven);
}
```

### 4.4 Uncalled-commitment roll-forward (the invariant carrier)

The roll-forward is a **pure fold** over the commitment's posted fund events, ordered by effective
date, mirroring `BuildCapitalAccountSubledgerEntries`. It carries four running accumulators and
enforces the invariant on every step.

```csharp
public sealed record CommitmentRollForward(
    string CommitmentId,
    string CapitalAccountId,
    string InvestorId,
    string Currency,
    decimal TotalCommitment,             // T
    decimal CumulativeCalled,            // C  (gross capital-call amounts)
    decimal CumulativeRecallableRestored,// R  (returned capital that restored uncalled, PF-1 capped)
    decimal CumulativeExpired,           // E  (residual released at period close, PF-5)
    decimal RecallCapAmount,             // PF-1: RecallCapPercent * TotalCommitment
    IReadOnlyList<CommitmentRollForwardStep> Steps)
{
    public decimal NetCalled => CumulativeCalled - CumulativeRecallableRestored; // net drawn & outstanding
    public decimal Uncalled  => TotalCommitment - CumulativeCalled + CumulativeRecallableRestored - CumulativeExpired;

    // INVARIANT: net-called + uncalled + expired == total
    public bool InvariantHolds =>
        Math.Abs((NetCalled + Uncalled + CumulativeExpired) - TotalCommitment) <= LedgerToleranceConstants.Balance;

    public decimal RemainingRecallableCapacity =>
        Math.Max(0m, RecallCapAmount - CumulativeRecallableRestored);
}

public sealed record CommitmentRollForwardStep(
    string FundEventId,
    ManualJournalEntryTypeDto EntryType,
    DateOnly EffectiveDate,
    decimal CallDelta,          // + on capital call
    decimal RecallableDelta,    // + on recallable return-of-capital (post-cap)
    decimal ExpiredDelta,       // + on governed expiry
    decimal RunningUncalled,    // Uncalled after this step
    decimal RunningNetCalled);
```

Fold rules per fund event (aligned with `CalculateNetCapitalActivity` signs in §2.3):

- **CapitalCall**: `C += grossCall`; `Uncalled -= grossCall`. Guard: reject/flag if
  `grossCall > Uncalled + tolerance` (over-call beyond commitment).
- **Distribution** flagged `RecallableReturnOfCapital`: `restorable = min(grossReturn,
  RemainingRecallableCapacity)`; `R += restorable`; `Uncalled += restorable`. Any excess above
  `restorable` is a permanent (non-recallable) distribution and does not touch uncalled.
- **Distribution** `NonRecallable` / **Redemption** / **ManagementFee**: no uncalled effect.
- **Governed expiry** (PF-5): `E += residualUncalled` for the amount the operator releases.
- **LpTransfer** (§4.6): splits the commitment; both legs re-derive from their own transferred
  `TotalCommitment` slice, so the invariant holds per resulting commitment.

Because `Uncalled = T - C + R - E` and `NetCalled = C - R`, the identity
`NetCalled + Uncalled + E = (C - R) + (T - C + R - E) + E = T` holds by construction; `InvariantHolds`
is a runtime tripwire against arithmetic/rounding drift and over-calls.

### 4.5 Recallable distribution event & default/interest

```csharp
public sealed record RecallableDistributionEvent(
    string FundEventId,
    string CommitmentId,
    string CapitalAccountId,
    string InvestorId,
    string Currency,
    DateOnly EffectiveDate,
    decimal GrossReturnOfCapital,
    decimal RestoredToUncalled,   // post-cap (PF-1)
    decimal PermanentPortion,     // GrossReturnOfCapital - RestoredToUncalled
    DistributionRecallability Recallability);

public sealed record CapitalCallDefault(
    string DefaultId,
    string CommitmentId,
    string InstallmentId,
    string CapitalAccountId,
    string InvestorId,
    string Currency,
    decimal DefaultedAmount,       // unfunded portion of the call
    DateOnly DueDate,
    DateOnly? CuredDate,
    DrawdownInstallmentStatus Status,
    IReadOnlyList<DefaultInterestAccrual> Accruals);

public sealed record DefaultInterestAccrual(
    string AccrualId,
    string DefaultId,
    DateOnly AccrualFrom,          // DueDate + DefaultGraceDays
    DateOnly AccrualTo,            // CuredDate ?? asOf
    decimal Principal,             // DefaultedAmount
    decimal AnnualRate,
    DefaultInterestConvention Convention,
    decimal AccruedInterest,       // computed, PF-4 simple
    string? JournalEntryId = null);
```

Interest computation (PF-2/PF-4, pure):

```csharp
public static decimal ComputeSimpleInterest(
    decimal principal, decimal annualRate, DateOnly from, DateOnly to, DefaultInterestConvention c)
{
    if (to <= from || principal <= 0m || annualRate <= 0m) return 0m;
    var days = to.DayNumber - from.DayNumber;
    var yearBasis = c switch
    {
        DefaultInterestConvention.Actual360   => 360m,
        DefaultInterestConvention.Thirty360   => 360m,   // day count adjusted separately for 30/360
        _                                     => 365m,   // Actual365Fixed
    };
    var dayCount = c == DefaultInterestConvention.Thirty360 ? Thirty360Days(from, to) : days;
    return Math.Round(principal * annualRate * dayCount / yearBasis, 2, MidpointRounding.ToEven);
}
```

### 4.6 LP transfer (secondary) handling

`LpTransfer` nets to `0` in `CalculateNetCapitalActivity` (§2.3), so it never changes fund-level
called capital. In the commitment layer it is a **commitment split/merge**: the transferor
commitment's `TotalCommitment`, `CumulativeCalled`, and `CumulativeRecallableRestored` are reduced
by the transferred fraction and a new/receiving `InvestorCommitment` is created (or increased) with
that fraction. Each resulting commitment satisfies `InvariantHolds` independently. Transfer is
recorded as a governed action with evidence; no cash journal is required for the transfer itself.

---

## 5. Interfaces

Persistence (new store, `src/Meridian.Storage/Ledger/`):

```csharp
namespace Meridian.Storage.Ledger;

public interface ICommitmentStore
{
    Task<InvestorCommitment> UpsertCommitmentAsync(InvestorCommitment commitment, CancellationToken ct = default);
    Task<IReadOnlyList<InvestorCommitment>> ListCommitmentsAsync(
        string? fundProfileId, Guid? ledgerBookId, string? investorId,
        string? tenantId = null, string? companyId = null, CancellationToken ct = default);
    Task<DrawdownSchedule> UpsertScheduleAsync(DrawdownSchedule schedule, CancellationToken ct = default);
    Task<IReadOnlyList<DrawdownSchedule>> ListSchedulesAsync(
        string fundProfileId, Guid? ledgerBookId, CancellationToken ct = default);
    Task<CapitalCallDefault> UpsertDefaultAsync(CapitalCallDefault @default, CancellationToken ct = default);
    Task<IReadOnlyList<CapitalCallDefault>> ListDefaultsAsync(
        string fundProfileId, Guid? ledgerBookId, CancellationToken ct = default);
}
```

Pure calculators (`src/Meridian.FinancialOperations/PrivateCapital/`), static, mirroring
`PrivateCapitalCapitalAccountSubledgerBuilder`:

```csharp
public static class CommitmentRollForwardCalculator
{
    // Folds the existing projection's fund events against commitments.
    public static IReadOnlyList<CommitmentRollForward> Build(
        IReadOnlyList<InvestorCommitment> commitments,
        PrivateCapitalActivityProjectionDto activity,
        IReadOnlyList<CommitmentExpiryEvent> expiryEvents);
}

public static class DefaultInterestCalculator
{
    public static IReadOnlyList<CapitalCallDefault> Evaluate(
        IReadOnlyList<InvestorCommitment> commitments,
        IReadOnlyList<DrawdownSchedule> schedules,
        PrivateCapitalActivityProjectionDto activity,
        IReadOnlyList<BankTransactionDto> funding, // cash-receipt evidence
        DateOnly asOf);
}
```

Draft factory — emits governed drafts using the existing `AutomatedJournalDraft` shape
(`src/Meridian.Ledger/`):

```csharp
public interface ICapitalCallDraftFactory
{
    // Drawdown-notice-driven capital call (receivable posting, PF-7(b)).
    AutomatedJournalDraft BuildCapitalCallDraft(
        InvestorCommitment commitment, DrawdownInstallment installment, decimal amount,
        DateOnly effectiveDate, string idempotencyKey, IReadOnlyList<string> evidenceLinks);

    // Default late-interest accrual (PF-8(a): CR interest income).
    AutomatedJournalDraft BuildDefaultInterestDraft(
        CapitalCallDefault @default, DefaultInterestAccrual accrual,
        DateOnly effectiveDate, string idempotencyKey, IReadOnlyList<string> evidenceLinks);
}
```

Read-model service (UI-shared, mirrors `CapitalAccountWorkbenchService`):

```csharp
public interface ICommitmentWorkbenchService
{
    Task<CommitmentWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null, Guid? ledgerBookId = null,
        string? capitalAccountId = null, string? investorId = null,
        string? currency = null, DateOnly? asOf = null, CancellationToken ct = default);
}
```

---

## 6. Persistence & migrations

New migration `src/Meridian.Storage/Ledger/Migrations/V_ledger_031__private_capital_commitments.sql`
(reserved range 031–032; the highest ordinal on disk is `V_ledger_028`, so confirm the next free
number before writing the file). Follows the `__SCHEMA__` / `create ... if not exists` /
`constraint ck_*` / tenant-column conventions and the `numeric(38, 12)` precision used by the
existing ledger migrations.

```sql
create schema if not exists __SCHEMA__;

create table if not exists __SCHEMA__.investor_commitments (
    commitment_id text primary key,
    fund_profile_id text not null,
    ledger_book_id uuid null,
    capital_account_id text not null,
    investor_id text not null,
    currency text not null,
    total_commitment numeric(38, 12) not null,
    commitment_date date not null,
    investment_period_end_date date null,
    status text not null,
    recall_cap_percent numeric(38, 12) not null default 1.0,
    default_interest_rate_annual numeric(38, 12) not null default 0,
    default_interest_convention text not null default 'Actual365Fixed',
    default_grace_days integer not null default 0,
    tenant_id text null,
    company_id text null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_investor_commitments_total_positive check (total_commitment > 0),
    constraint ck_investor_commitments_status
        check (status in ('Active','InvestmentPeriodClosed','FullyCalled','Defaulted','Expired','Closed')),
    constraint ck_investor_commitments_convention
        check (default_interest_convention in ('Actual365Fixed','Actual360','Thirty360'))
);

create index if not exists ix_investor_commitments_scope
    on __SCHEMA__.investor_commitments (fund_profile_id, ledger_book_id, investor_id);
create index if not exists ix_investor_commitments_tenant_lower
    on __SCHEMA__.investor_commitments (lower(tenant_id), lower(company_id));

create table if not exists __SCHEMA__.drawdown_installments (
    installment_id text primary key,
    commitment_id text not null references __SCHEMA__.investor_commitments(commitment_id) on delete cascade,
    sequence integer not null,
    notice_date date not null,
    due_date date not null,
    call_percent numeric(38, 12) null,
    call_amount numeric(38, 12) null,
    status text not null,
    journal_entry_id uuid null,
    payment_intent_id text null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_drawdown_installments_basis
        check (call_percent is not null or call_amount is not null),
    constraint ck_drawdown_installments_status
        check (status in ('Scheduled','Noticed','Called','Funded','PartiallyFunded','Defaulted','Cured','Waived','Cancelled')),
    unique (commitment_id, sequence)
);

create index if not exists ix_drawdown_installments_due
    on __SCHEMA__.drawdown_installments (commitment_id, due_date);

create table if not exists __SCHEMA__.capital_call_defaults (
    default_id text primary key,
    commitment_id text not null references __SCHEMA__.investor_commitments(commitment_id) on delete cascade,
    installment_id text not null references __SCHEMA__.drawdown_installments(installment_id) on delete cascade,
    defaulted_amount numeric(38, 12) not null,
    due_date date not null,
    cured_date date null,
    status text not null,
    accrued_interest numeric(38, 12) not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_capital_call_defaults_amount check (defaulted_amount >= 0)
);

create index if not exists ix_capital_call_defaults_scope
    on __SCHEMA__.capital_call_defaults (commitment_id, due_date);
```

Notes:
- Money is `numeric(38, 12)` to match ledger precision; the C# layer keeps `decimal` and rounds to 2dp
  for postings.
- Recallable classification and commitment linkage on *movements* are **not** stored here — they
  ride the journal metadata tags (§7), so posted movements remain the single source of truth and the
  projector stays authoritative.
- Governed **expiry events** (PF-5) persist as an evidence-backed row in an existing audit surface or
  a small `commitment_expiry_events` table (add in the same migration if PF-5 default is accepted).

---

## 7. Projector + governed-draft integration

### 7.1 Metadata linkage (backward compatible)

Extend `TreasuryLedgerContextDto` with optional trailing fields (positional record, defaulted →
non-breaking) and mirror them into the journal metadata tags the projector already reads:

```csharp
public sealed record TreasuryLedgerContextDto(
    DateOnly? EffectiveDate = null,
    string? IdempotencyKey = null,
    string? FundEventId = null,
    string? FundEventType = null,
    string? CapitalAccountId = null,
    string? InvestorId = null,
    string? PaymentIntentId = null,
    string? SettlementReference = null,
    // NEW (all optional; safe defaults):
    string? CommitmentId = null,
    string? DrawdownInstallmentId = null,
    DistributionRecallability? Recallability = null);
```

When the engine builds a draft it stamps `JournalEntryMetadata.Tags` with `commitmentId`,
`drawdownInstallmentId`, and `recallable` (`"true"`/`"false"`). `PrivateCapitalFundEventLedgerProjector`
already exposes arbitrary tags through `FirstTagText(entries, key)`, so the roll-forward calculator
reads these back with **no projector change**. To surface them structurally, add optional
`CommitmentId`, `DrawdownInstallmentId`, `Recallability` to `PrivateCapitalFundEventDto` and populate
them in `PrivateCapitalActivityProjectionBuilder` (from `context`/tags) — additive, default-null.

### 7.2 Posting a drawdown-driven capital call

The capital-call *amount* verb continues to flow through the manual-journal workbench, but the
engine can originate it as a governed draft to guarantee the projector's Critical-issue gates
(§2.2) are satisfied up front:

1. `ICapitalCallDraftFactory.BuildCapitalCallDraft(...)` produces a **balanced**
   `AutomatedJournalDraft`:
   - PF-7(b): `DR Capital-Call Receivable (Asset)` / `CR Contributed Capital — {investor} (Equity)`.
   - `JournalEntryMetadata` carries `EffectiveDate`, a stable `IdempotencyKey`
     (`"capital-call:{commitmentId}:{installmentId}"`), `FundEventId`
     (`"fund-event:{fundProfileId}:capital-call:{installmentId}"`), `FundEventType = "CapitalCall"`,
     `CapitalAccountId`, `InvestorId`, plus the commitment tags. The `CR` line is `Equity`, so
     `BuildCapitalAccountImpacts` records the capital-account impact the projector requires.
2. `AutomatedJournalApproval.Submit(draft, actor, occurredAtUtc, reason, evidenceLinks)` →
   `.Approve(actor, occurredAtUtc, reason, evidenceLinks)` (evidence required) →
   `.PostTo(ledger, actor, occurredAtUtc, reason, evidenceLinks)`.
3. `BuildPostingMetadata()` stamps `automatedJournalApprovalId` / `automatedJournalStatus` /
   `approvedBy`, which `ResolveApprovalState(...)` maps to `Approved`/`Posted`, so
   `IsApprovalComplete` and `IsPostingReady` hold.
4. On the next projection, the call appears as a `CapitalCall` fund event; the roll-forward folds
   `CallDelta = grossCall`, `Uncalled -= grossCall`, and marks the installment `Called`.
5. On cash receipt: `DR Cash / CR Capital-Call Receivable` (a settlement journal, tied to the same
   `PaymentIntentId`), moving the installment to `Funded`. Existing payment-intent evidence plumbing
   in `PrivateCapitalActivityProjectionBuilder.BuildPaymentIntentWorkflows(...)` already tracks this.

### 7.3 Posting a recallable distribution

A return-of-capital distribution is posted as today (`DR Contributed Capital (Equity) / CR Cash`)
with `FundEventType = "Distribution"` and the new `recallable = "true"` /
`Recallability = RecallableReturnOfCapital` tag. The roll-forward applies the PF-1 cap and folds
`RecallableDelta = min(grossReturn, RemainingRecallableCapacity)` into `Uncalled`. No new posting
type is introduced — recallability is metadata over the existing `Distribution` verb.

### 7.4 Posting default late-interest (governed)

1. `DefaultInterestCalculator.Evaluate(...)` detects installments whose `DueDate + DefaultGraceDays`
   has passed with insufficient funding evidence, computes `ComputeSimpleInterest(...)`.
2. `ICapitalCallDraftFactory.BuildDefaultInterestDraft(...)` → balanced draft
   `DR Interest Receivable — {investor} (Asset) / CR Interest Income (Income)` (PF-8(a)),
   `FundEventType = "DefaultInterest"`, idempotency
   `"default-interest:{defaultId}:{accrualTo:yyyyMMdd}"`.
3. Same `Submit → Approve → PostTo` governed lifecycle; evidence links reference the drawdown notice
   and aging report.
4. `MapPrivateCapitalEntryType` currently maps unknown fund-event types to
   `ManualJournalEntryTypeDto.General` (which nets to `0`). Add a `"defaultinterest"` →
   (new) mapping or classify it as `General` with a memo; because the interest leg is **Income**, not
   **Equity**, it does not distort the capital-account equity roll-forward. (If default interest
   should credit partner capital instead — PF-8(b) — the CR leg becomes Equity and would flow into
   the capital-account impact; deferred.)

### 7.5 Period locks

Before any engine `PostTo`, call `LockedAccountingPeriodBook.EnsureCanPost(ledgerKey,
approval.ToJournalEntry())` (or route through `LockedAccountingPeriodBook.Post(...)`), because
`AutomatedJournalApproval.PostTo` posts to the `Ledger` directly and does **not** consult the lock
book (§2.6, Open Question O-5). This keeps back-dated call/interest journals out of hard-closed
periods and lets them surface as `ManualJournalEntryStatusDto.CloseLocked` in the workbench.

---

## 8. Contracts / DTOs, endpoints, and UI surfaces

### 8.1 New wire DTOs (`src/Meridian.Contracts/Ledger/PrivateCapitalCommitmentDtos.cs`)

```csharp
public sealed record CommitmentRollForwardDto(
    string CommitmentId,
    string CapitalAccountId,
    string InvestorId,
    string Currency,
    decimal TotalCommitment,
    decimal CumulativeCalled,
    decimal CumulativeRecallableRestored,
    decimal CumulativeExpired,
    decimal NetCalled,
    decimal Uncalled,
    decimal RemainingRecallableCapacity,
    decimal CalledPercent,           // NetCalled / TotalCommitment
    bool InvariantHolds,
    CommitmentStatusDto Status,
    DateOnly? InvestmentPeriodEndDate,
    IReadOnlyList<CommitmentRollForwardStepDto> Steps,
    IReadOnlyList<DrawdownInstallmentDto> Schedule,
    IReadOnlyList<CapitalCallDefaultDto> Defaults,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

public sealed record CommitmentWorkbenchDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    DateOnly AsOf,
    string Currency,
    string WorkbenchRoute,
    string StatusLabel,
    string StatusReason,
    int CommitmentCount,
    decimal TotalCommitment,
    decimal TotalUncalled,
    decimal TotalNetCalled,
    decimal TotalExpired,
    int InvariantBreachCount,
    int DefaultCount,
    IReadOnlyList<CommitmentRollForwardDto> Commitments,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<string>? LiveCapabilities = null,
    IReadOnlyList<string>? PlannedCapabilities = null)
{
    public IReadOnlyList<string> LiveCapabilities { get; init; } = LiveCapabilities ?? [];
    public IReadOnlyList<string> PlannedCapabilities { get; init; } = PlannedCapabilities ?? [];
}
```

Emit a **Critical** `AccountingConfigurationValidationIssueDto` (code
`private-capital.commitment-invariant-breach`) whenever `!InvariantHolds` or an over-call is
detected — this rides the exact same validation channel the workbench already renders and the
close-cockpit already gates on.

### 8.2 Extend the existing capital-account workbench investor row

Add optional trailing commitment fields to `CapitalAccountWorkbenchInvestorAccountDto` (additive,
default-null so existing serialization/tests are unaffected):

```csharp
// appended to CapitalAccountWorkbenchInvestorAccountDto
decimal? TotalCommitment = null,
decimal? UncalledCommitment = null,
decimal? NetCalledCommitment = null,
decimal? ExpiredCommitment = null,
decimal? CalledPercent = null,
bool CommitmentInvariantHolds = true
```

`CapitalAccountWorkbenchService.BuildInvestorAccount(...)` joins the commitment roll-forward by
`(CapitalAccountId, InvestorId, Currency)` — the same key `BuildAccountKey(subledger)` already uses —
and populates these when a commitment exists.

### 8.3 Endpoints (`src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs`)

New route constants in `src/Meridian.Contracts/Api/UiApiRoutes.cs`, following the existing
`/api/ledger/private-capital/*` family:

```csharp
public const string LedgerPrivateCapitalCommitmentWorkbench = "/api/ledger/private-capital/commitment-workbench";
public const string LedgerPrivateCapitalCommitmentRollForward = "/api/ledger/private-capital/commitment-roll-forward";
```

Registration mirrors `GetLedgerPrivateCapitalCapitalAccountWorkbench` (query params
`fundProfileId`, `ledgerBookId`, `capitalAccountId`, `investorId`, `currency`, `asOf`; tenant/company
resolved from `tenantContext`):

```csharp
app.MapGet(UiApiRoutes.LedgerPrivateCapitalCommitmentWorkbench, async (
        HttpContext context, string? fundProfileId, Guid? ledgerBookId,
        string? capitalAccountId, string? investorId, string? currency, DateOnly? asOf) =>
    {
        var service = ResolveCommitmentWorkbenchService(context);
        var dto = await service.GetWorkbenchAsync(
            fundProfileId, ledgerBookId, capitalAccountId, investorId, currency, asOf, context.RequestAborted)
            .ConfigureAwait(false);
        return Results.Ok(dto);
    })
    .WithName("GetLedgerPrivateCapitalCommitmentWorkbench")
    .Produces<CommitmentWorkbenchDto>(StatusCodes.Status200OK);
```

Write paths (commitment upsert, schedule upsert, drawdown-notice issue, governed
call/interest post, expiry) go through the existing manual-journal / governed-approval surfaces plus
a thin `ICommitmentStore`-backed admin endpoint; all mutations honor the human-in-the-loop gates in
`docs/ai/assistant-workflow-contract.md`.

### 8.4 Browser workstation

Under `src/Meridian.Ui/dashboard/` (Accounting screen, capital-account area — top-level nav stays
`Trading, Portfolio, Accounting, Reporting, Strategy, Data, Settings`):

- A **Commitments** panel on the capital-account workbench showing, per investor:
  `Total | Net Called | Uncalled | Called % | Expired`, with an invariant badge (green when
  `InvariantHolds`, red `Critical` when breached).
- A **Drawdown schedule** timeline (scheduled → noticed → called → funded) with a "Generate
  capital-call draft" action that posts through the governed draft flow.
- A **Recallable capacity** meter (`RemainingRecallableCapacity` vs. `RecallCapAmount`).
- A **Defaults & interest** subpanel listing `CapitalCallDefaultDto` rows with accrued interest and
  cure status, linking to the governed interest-accrual draft.

Add a matching read model in `src/Meridian.Ui.Shared/` and register the service in
`WorkstationServiceCollectionExtensions`. WPF parity (`W8-WPF-PARITY-001`) consumes the same
`CommitmentWorkbenchDto` seam afterward — no client-side forking of commitment state.

---

## 9. Test plan (xUnit + FluentAssertions)

New suites under `tests/Meridian.Tests/FinancialOperations/PrivateCapital/` and
`tests/Meridian.Tests/Ui/`, following the shape of the existing
`CapitalAccountWorkbenchServiceTests`, `PrivateCapitalCloseCockpitServiceTests`, and
`AccountingConfigurationServiceTests`.

### 9.1 Roll-forward invariant (`CommitmentRollForwardCalculatorTests`)

- **Single call reduces uncalled**: commitment $10M, one $2.5M call → `NetCalled == 2_500_000`,
  `Uncalled == 7_500_000`, `InvariantHolds.Should().BeTrue()`.
- **Multi-call sequence**: 4 quarterly calls of 25% each → after all four `Uncalled == 0`,
  `Status == FullyCalled`; running `Steps[i].RunningUncalled` decreases monotonically.
- **Invariant holds at every step**: property-style loop asserting
  `NetCalled + Uncalled + Expired == Total` (within `LedgerToleranceConstants.Balance`) after each
  fund event.
- **Over-call is flagged**: a call exceeding remaining uncalled produces a `Critical`
  `private-capital.commitment-invariant-breach` issue and `InvariantHolds == false`.

### 9.2 Recallable distributions

- **Recallable return restores uncalled**: after $10M commitment, $6M called, a $2M
  `RecallableReturnOfCapital` distribution → `Uncalled == 6_000_000`, `NetCalled == 4_000_000`,
  invariant holds.
- **Recall cap enforced (PF-1)**: with `RecallCapPercent = 0.5` on $10M, cumulative recallable
  restorations cap at $5M; a return beyond the cap splits into `RestoredToUncalled` and
  `PermanentPortion`; `RemainingRecallableCapacity` never goes negative.
- **Non-recallable distribution**: `Uncalled` unchanged.

### 9.3 Default & late interest (`DefaultInterestCalculatorTests`)

- **Default detected after grace**: installment due `2026-03-31`, `DefaultGraceDays = 10`, no funding
  evidence by `asOf = 2026-05-01` → one `CapitalCallDefault` with `Status == Defaulted`.
- **Actual/365 simple interest**: principal $1M, 10%/yr, `2026-04-10 → 2026-05-01` (21 days) →
  `AccruedInterest.Should().Be(Math.Round(1_000_000m * 0.10m * 21m / 365m, 2))`.
- **Cure stops accrual**: funding evidence on `2026-04-20` → `AccrualTo == 2026-04-20`,
  `Status == Cured`.
- **Convention variants**: Actual/360 and 30/360 produce the expected day-count deltas.

### 9.4 Governed posting (`CapitalCallDraftFactoryTests`)

- Built call draft `IsBalanced == true`; metadata carries `EffectiveDate`, non-empty
  `IdempotencyKey`, `FundEventType == "CapitalCall"`, `CapitalAccountId`, and the equity CR leg.
- Round-trip: `Submit → Approve → PostTo` against an in-memory `Ledger`, then
  `PrivateCapitalFundEventLedgerProjector.Project(ledger)` yields an event with
  `IsPostingReady == true` and one `CapitalAccountImpact`.
- **Period lock**: posting into a period locked via `LockedAccountingPeriodBook` throws
  `LedgerValidationException` (asserts §7.5 wiring).
- **Idempotency**: two calls with the same key do not double-count in the roll-forward.

### 9.5 Workbench read model (`CommitmentWorkbenchServiceTests`)

- Given a stub projection + commitments, `GetWorkbenchAsync` returns per-investor
  `CommitmentRollForwardDto` with correct totals and a `StatusLabel` of `"Ready"` when invariants
  hold, `"Blocked"` when a breach exists (mirroring `CapitalAccountWorkbenchService.BuildStatus`).
- Extended `CapitalAccountWorkbenchInvestorAccountDto` commitment fields populate when a commitment
  matches `(CapitalAccountId, InvestorId, Currency)` and stay null otherwise (back-compat).

---

## 10. Implementation checklist (ordered, code-ready)

1. **Enums + domain records** — add `src/Meridian.Ledger/PrivateCapitalCommitments.cs`
   (`CommitmentStatus`, `DrawdownInstallmentStatus`, `DistributionRecallability`,
   `DefaultInterestConvention`, `InvestorCommitment`, `DrawdownSchedule`, `DrawdownInstallment`,
   `CommitmentRollForward`, `CommitmentRollForwardStep`, `RecallableDistributionEvent`,
   `CapitalCallDefault`, `DefaultInterestAccrual`). Money `decimal`, dates `DateOnly`.
2. **Interest math** — `DefaultInterestCalculator.ComputeSimpleInterest(...)` + `Thirty360Days(...)`,
   pure, unit-tested first (§9.3).
3. **Roll-forward calculator** — `CommitmentRollForwardCalculator.Build(...)` in
   `src/Meridian.FinancialOperations/PrivateCapital/`, folding
   `PrivateCapitalActivityProjectionDto.FundEvents` + commitments; emit invariant-breach validation
   issues.
4. **Migration** — `V_ledger_031__private_capital_commitments.sql` (§6) + `ICommitmentStore`
   implementation in `src/Meridian.Storage/Ledger/` with tenant/company scoping.
5. **Metadata seam** — extend `TreasuryLedgerContextDto` with `CommitmentId`,
   `DrawdownInstallmentId`, `Recallability` (optional trailing); thread into
   `PrivateCapitalActivityProjectionBuilder` and stamp `commitmentId` / `drawdownInstallmentId` /
   `recallable` tags; add optional fields to `PrivateCapitalFundEventDto`.
6. **Draft factory** — `ICapitalCallDraftFactory` (`BuildCapitalCallDraft`,
   `BuildDefaultInterestDraft`) producing balanced `AutomatedJournalDraft`s with all Critical-gate
   metadata; post via `AutomatedJournalApproval` + `LockedAccountingPeriodBook.EnsureCanPost`.
7. **Wire DTOs** — `PrivateCapitalCommitmentDtos.cs` (`CommitmentRollForwardDto`,
   `CommitmentWorkbenchDto`, `DrawdownInstallmentDto`, `CapitalCallDefaultDto`, `CommitmentStatusDto`).
8. **Read-model service** — `ICommitmentWorkbenchService` + `CommitmentWorkbenchService` in
   `src/Meridian.Ui.Shared/Services/`, joining commitments to the private-capital activity
   projection; register in `WorkstationServiceCollectionExtensions`.
9. **Extend capital-account workbench** — append commitment fields to
   `CapitalAccountWorkbenchInvestorAccountDto` and populate in
   `CapitalAccountWorkbenchService.BuildInvestorAccount`.
10. **Endpoints** — add `UiApiRoutes` constants + `MapGet` registrations in `LedgerEndpoints.cs`
    with `.WithName(...)` / `.Produces<CommitmentWorkbenchDto>(...)`.
11. **Browser workstation** — Commitments / drawdown / recallable / defaults panels in
    `src/Meridian.Ui/dashboard/` (Accounting screen) over the new read model.
12. **Tests** — suites in §9 (calculators first, then draft factory + projector round-trip, then
    read model). Validate with the narrowest commands:
    `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true` filtered to the new
    classes, plus `npm --prefix src/Meridian.Ui/dashboard run test`.
13. **Docs** — update `docs/domain/README.md` private-capital section and the AI navigation index
    per CLAUDE.md rule 4 (keep docs/code aligned).

---

## 11. Open questions

- **O-1 (PF-1 scope):** Should the recall cap be per-commitment or a fund-level recycling limit
  shared across LPs? Blueprint assumes per-commitment; a fund-level pool changes the calculator to a
  shared accumulator.
- **O-2 (PF-5 expiry authority):** Who authorizes uncalled-commitment expiry, and does it require a
  close-cockpit lane + approval gate? Default assumes an operator-driven governed action with
  evidence.
- **O-3 (default interest beneficiary, PF-8):** Confirm interest credits fund income (v1) vs.
  non-defaulting partners' capital; the latter needs an allocation-engine blueprint and turns the CR
  leg into Equity (affecting the capital-account impact).
- **O-4 (`DefaultInterest` fund-event type):** Add a dedicated `ManualJournalEntryTypeDto` value and
  `MapPrivateCapitalEntryType` mapping, or keep it as `General` + memo? A dedicated type improves
  reporting but is an enum/schema change.
- **O-5 (period-lock in governed post):** Should `AutomatedJournalApproval.PostTo` itself consult
  `LockedAccountingPeriodBook`, rather than relying on callers to guard? Centralizing the check would
  protect *all* governed postings, not just this engine.
- **O-6 (subscription vs. commitment):** Existing `Subscription`/`Redemption` verbs (open-end funds)
  vs. `CapitalCall`/`Distribution` (closed-end) — should commitments also model open-end subscription
  obligations, or is the engine closed-end-only for v1? Blueprint targets closed-end drawdown
  mechanics.
- **O-7 (multi-currency commitments):** Commitments are single-currency here. Cross-currency funds
  (commitment in EUR, calls in USD) need an FX policy fork before the invariant can span currencies.
