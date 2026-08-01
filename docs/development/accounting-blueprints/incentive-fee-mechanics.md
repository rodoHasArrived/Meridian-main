# Blueprint: Full Incentive-Fee Mechanics (Hurdles, Crystallization, Stateful HWM & Loss-Carryforward)

**Status:** Design (code-ready) — nothing from this blueprint is in source yet
**Owner:** Ledger / fund-accounting lane (`src/Meridian.Ledger` + `src/Meridian.Storage/Ledger` + `src/Meridian.Ui.Shared`)
**Reviewed:** 2026-08-01

Scope: extend the existing partnership fee projectors from a single-shot, fund-level, "high-water-mark
passed in per period" model into a **config-driven, durable, series-scoped incentive-fee engine** that
supports both US and European fund conventions.

> **Shared-convention notice.** This blueprint shares the ledger migration sequence, the
> `AutomatedJournalEventKind` enum, and the fund high-water mark with the
> [equalization / series-accounting blueprint](equalization-and-series-accounting.md) and the
> [commitment & capital-call blueprint](commitment-and-capital-call-engine.md). Migration ordinals,
> DDL precision, route prefixes, and the HWM-ownership contract are recorded in the canonical
> [blueprint register](../../engineering/blueprints/README.md#shared-conventions). Do not claim an
> ordinal or a new route prefix without checking it.

---

## 1. Summary

Meridian already computes a management fee and a high-water-mark (HWM) performance fee inside
`PartnershipInvestorAccountingProjector.Project`, and a mirrored accrual in
`FeeScheduleAccrualEventProducer.Produce`. In both places the HWM is an **input** that is recomputed and
returned but never persisted, there is **no hurdle rate**, **no GP catch-up**, **no crystallization
schedule**, and **no loss-carryforward series**. Fees are computed at the fund level and profit is
allocated pro-rata; the *fee itself* is not computed per investor against each investor's own protected
level.

This blueprint adds three capabilities, all driven by a durable `IncentiveFeePolicy` so that a US
"soft hurdle + full catch-up, annual crystallization" fund and a European "hard hurdle, quarterly
crystallization" fund are the same code path with different configuration:

1. **Hurdle rates** — soft vs hard hurdle with configurable GP catch-up (full/100% catch-up as the
   headline case), folded into one pure calculator that replaces the single `incentiveBase * rate`
   line used today.
2. **Crystallization schedules** as first-class config — frequency (monthly / quarterly / semi-annual /
   annual / on-redemption) and anchor dates that decide when accrued fee locks and the HWM rolls
   forward.
3. **Stateful series-scoped HWM and loss-carryforward series** (one scope under Method A, one per
   share-series under Method B — never per investor), persisted in the ledger schema and
   rolled forward each period, replacing the pass-in-per-period HWM with durable state and an audited
   snapshot history.

The engine keeps the existing governed lifecycle intact: economic events →
`AutomatedJournalDraftProjector` → `AutomatedJournalApproval.Submit/Approve/PostTo` → `Ledger.Post`.
Nothing about durability (WAL, atomic writes), central package management, or source-generated JSON is
bypassed.

---

## 2. Grounding in current code (real file/type references)

### 2.1 How management fee, performance fee, and HWM are computed today

`src/Meridian.Ledger/PartnershipInvestorAccountingProjector.cs`,
`PartnershipInvestorAccountingProjector.Project(PartnershipInvestorAllocationInput input)` returns
`PartnershipInvestorAllocationProjection`. The core math (lines 18-24) is:

```csharp
var grossProfitOrLoss = input.EndingNavBeforeFees - input.BeginningNav;
var managementFee     = RoundCurrency(input.BeginningNav * input.ManagementFeeRate);
var incentiveBase     = Math.Max(0m, input.EndingNavBeforeFees - input.HighWaterMark - managementFee);
var performanceFee    = RoundCurrency(incentiveBase * input.PerformanceFeeRate);
var allocableProfitOrLoss = grossProfitOrLoss - managementFee - performanceFee;
var endingNavAfterFees    = input.EndingNavBeforeFees - managementFee - performanceFee;
var updatedHighWaterMark  = Math.Max(input.HighWaterMark, endingNavAfterFees);
```

Key observations that drive this design:

- **`incentiveBase`** is `max(0, EndingNavBeforeFees − HighWaterMark − managementFee)`. There is no
  hurdle deduction and no catch-up; the fee is a flat `rate × excess-over-HWM`.
- **`input.HighWaterMark`** is a constructor parameter on
  `PartnershipInvestorAllocationInput` (`src/Meridian.Ledger/PartnershipInvestorAllocationInput.cs`,
  validated `>= 0`). `updatedHighWaterMark` is only *returned* on the projection — the caller is
  responsible for carrying it to the next period. It is not stored anywhere.
- Fee is **fund-level**: `managementFee`/`performanceFee` are computed once, then
  `BuildInvestorAllocations` splits `allocableProfitOrLoss` by `PartnershipInvestor.AllocationPercent`
  with a last-investor-absorbs-residual rule (lines 42-64). Each investor's *fee* is not computed
  against their own HWM.
- `RoundCurrency` is `Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency` — 2 dp,
  `MidpointRounding.AwayFromZero`. Allocation tolerance is
  `LedgerToleranceConstants.Allocation` (`0.000001m`).

The **same** incentive math is duplicated in
`src/Meridian.Ui.Shared/Services/AutomatedJournalEventProducers.cs`,
`FeeScheduleAccrualEventProducer.Produce(FeeScheduleAccrualRequest request)` (lines 239-241), which emits
`AutomatedJournalEvent`s with idempotency keys `mgmt-fee|{fundId}|{periodId}` and
`perf-fee|{fundId}|{periodId}`. Any change to the fee formula must land in a **shared calculator** that
both call, or the two copies will drift.

### 2.2 Tiered waterfall (preferred return, catch-up, carry, residual)

`src/Meridian.Ledger/PartnershipWaterfallProjector.cs`,
`PartnershipWaterfallProjector.Project(PartnershipWaterfallAllocationInput)` walks ordered
`PartnershipWaterfallTier` records (`src/Meridian.Ledger/PartnershipWaterfallTier.cs`), each with an
optional `decimal? CapAmount` and a list of `PartnershipWaterfallAllocationRule(investorId, percent)`.
`BuildTierAllocations` (lines 35-76) fills each tier up to its cap from `remainingProfit`, splits by
rule percent (last rule absorbs residual), and stops when profit is exhausted.

The existing test `PartnershipWaterfallProjector_AllocatesPreferredReturnAndCarryTiers`
(`tests/Meridian.Tests/Ledger/LedgerIntegrationTests.cs` line 3080) models a **preferred-return** tier
(cap 100, 100% to LP) followed by a **carry** tier (LP 0.80 / GP 0.20). What the waterfall projector does
**not** model is a **catch-up tier** where the GP receives 100% of a band until it has "caught up" to
its carry share of total profit above the hurdle. This blueprint adds catch-up as a first-class term in
the incentive calculator (Section 5.1) and, optionally, as a synthesizable waterfall tier (Section 7.4)
so the two surfaces stay consistent.

### 2.3 Ledger account factories for fees

`src/Meridian.Ledger/LedgerAccounts.cs` provides the scoped factories used by both projectors:

```csharp
public static LedgerAccount ManagementFeeExpenseFor(string fundId)  // "Management Fee Expense", Expense
public static LedgerAccount ManagementFeePayableFor(string fundId)  // "Management Fee Payable", Liability
public static LedgerAccount PerformanceFeeExpenseFor(string fundId) // "Performance Fee Expense", Expense
public static LedgerAccount PerformanceFeePayableFor(string fundId) // "Performance Fee Payable", Liability
public static LedgerAccount InvestorCapitalFor(string investorId)   // "Investor Capital", Equity
public static LedgerAccount RetainedEarningsFor(string fundId)      // "Retained Earnings", Equity
```

All are thin wrappers over `private static LedgerAccount CreateScoped(name, accountType, financialAccountId)`
(line 368). The incentive engine **reuses `PerformanceFee*` accounts** for both accrual and crystallization
(the `AutomatedJournalEventKind.PerformanceFeeAccrued` doc comment already reads "Performance or incentive
fee"), so no new chart-of-accounts entries are strictly required. Section 6.3 proposes two optional
factories (`IncentiveFeeCrystallizedPayableFor`) only if a fund wants crystallized fee segregated from
running accrual — a policy fork, not a requirement.

### 2.4 Projector → governed-draft → submit/approve/post pattern

- `src/Meridian.Ledger/AutomatedJournalEvent.cs` — normalized economic event
  (`Kind, Symbol, Amount, Timestamp, FinancialAccountId?, Description?, SecurityId?, SourceEventId?,
  EffectiveDate?, IdempotencyKey?, EvidenceReferences?`).
- `src/Meridian.Ledger/AutomatedJournalEventKind.cs` — enum; already has `ManagementFeeAccrued` and
  `PerformanceFeeAccrued`.
- `src/Meridian.Ledger/AutomatedJournalDraftProjector.cs`,
  `AutomatedJournalDraftProjector.Project(AutomatedJournalEvent)` → `AutomatedJournalDraft`. The
  `ProjectLines` switch (lines 39-122) maps `PerformanceFeeAccrued` to
  `[(performanceFeeExpense, amount, 0), (performanceFeePayable, 0, amount)]` where
  `accrualScope = financialAccountId ?? symbol`. Note **`ValidateAmount` requires `amount > 0`** (lines
  147-151) — downward accrual adjustments cannot be expressed as a negative amount and need their own
  event kind (Section 6.3).
- `src/Meridian.Ledger/AutomatedJournalDraft.cs` — `AutomatedJournalDraft(Event, Description, Lines,
  Metadata)` with `TotalDebits`/`TotalCredits`/`IsBalanced`.
- `src/Meridian.Ledger/AutomatedJournalApproval.cs` — governed aggregate.
  `Submit(draft, actor, occurredAtUtc, reason, evidenceLinks?)` (Draft→Submitted, refuses unbalanced
  drafts), `Approve(...)` (requires evidence), `Reject(...)`, `PostTo(Ledger ledger, ...)`
  (Approved→Posted, calls `ledger.Post(ToJournalEntry())`). Transition allow-list (lines 194-201):
  `Draft→Submitted→{Approved|Rejected}`, `Approved→Posted`.
- `src/Meridian.Ledger/LedgerGovernedLifecycle.cs` — `LedgerGovernedLifecycle.PrepareTransition(...)`
  centralizes actor/reason/evidence validation and the transition allow-list check.

### 2.5 Persistence conventions

- Migrations live in `src/Meridian.Storage/Ledger/Migrations/` as `V_ledger_###__name.sql`. Highest
  current number is **`V_ledger_028__wash_sale_activation.sql`** (note two files share `008`; keep new
  numbers unique). This blueprint's reserved range is **029–030** — see the
  [register](../../engineering/blueprints/README.md#ledger-migration-ordinals); re-derive the next
  free ordinal from disk at implementation time. Scripts use the `__SCHEMA__` placeholder, `create table if not exists`,
  `create index/unique index if not exists`, `ck_`/`ix_`/`ux_` naming, `numeric(38, 12)` for
  money/quantity precision, `timestamptz`, and `references __SCHEMA__.ledger_books(ledger_book_id)
  on delete cascade` (see `V_ledger_009__tax_lot_persistence.sql`).
- `src/Meridian.Storage/Ledger/LedgerMigrationRunner.cs` auto-discovers scripts under
  `Ledger/Migrations` — no registration needed.
- `src/Meridian.Storage/Ledger/ILedgerJournalStore.cs` — the store extension pattern to copy: optional
  capabilities (`SaveTaxLotAsync`, `ListOpenTaxLotsAsync`, …) are **default interface methods that throw
  `NotSupportedException`**, with plain `sealed record` row types (`LedgerTaxLotRecord`,
  `LedgerAccountTaxLotPolicyRecord`). New incentive-fee state persistence follows exactly this shape so
  the ~50 existing call sites and non-Postgres stores are untouched.

---

## 3. Domain glossary (US vs European conventions)

| Term | Meaning | Convention notes |
|------|---------|------------------|
| **Incentive / performance / carried interest** | Manager's share of profit above a protected level. | US HF "incentive fee"; EU "performance fee"; PE "carry". One engine, one `IncentiveFeeRate`. |
| **High-water mark (HWM)** | Highest post-fee NAV per share/interest reached at any prior crystallization. Fee only on gains above it. | Standard in both regions; the durable series in Section 5.3. |
| **Loss carryforward (LCF)** | Cumulative unrecouped loss that must be earned back before fees resume. Related to but distinct from HWM. | Some European "modified HWM" / "reset" funds cap how long LCF persists. |
| **Hurdle / preferred return** | Minimum return before incentive applies. | See soft vs hard below. |
| **Soft hurdle** | Once return clears the hurdle, fee is charged on the *whole* excess over HWM (usually with catch-up). | Common in US PE/credit. |
| **Hard hurdle** | Fee charged **only** on the excess *above* the hurdle; the hurdle band is never fee-bearing. | Common in European retail / UCITS-style. |
| **GP catch-up** | After a soft hurdle, GP takes an elevated share (often 100% = "full catch-up") of a band until GP's cumulative fee equals its carry % of total profit above the hurdle. | US PE default. Incompatible with a pure hard hurdle. |
| **Crystallization** | The moment accrued fee becomes locked/payable and the HWM rolls forward. | US HF: annual. EU: often monthly/quarterly + on redemption. |

---

## 4. Policy forks (options + RECOMMENDED default)

Each fork is a config knob on `IncentiveFeePolicy` / its sub-records (Section 5). Defaults preserve
today's behavior for funds that do not opt in.

### Fork A — Hurdle type
- Options: `None`, `Soft`, `Hard`.
- **RECOMMENDED default: `None`.** Preserves the current `incentiveBase` formula exactly (regression-safe
  for existing funds). Ship `Soft` + full catch-up as the documented PE template and `Hard` as the
  European retail template.

### Fork B — Hurdle basis (what the hurdle rate is applied to)
- Options: `BeginningNav`, `PriorHighWaterMark`, `ContributedCapital`.
- **RECOMMENDED default: `BeginningNav`.** Matches the basis already used for the management fee
  (`BeginningNav * ManagementFeeRate`), so a single period NAV drives both fees. `PriorHighWaterMark`
  is the compounding-hurdle option; `ContributedCapital` supports PE-style preferred return on paid-in
  capital.

### Fork C — Catch-up rate
- Options: `0` (no catch-up) … `1.0` (full/100% catch-up). Only meaningful when `HurdleType = Soft`.
- **RECOMMENDED default: `1.0` (full catch-up) when hurdle is Soft; `0` otherwise.** Full catch-up is the
  market-standard PE term and makes the GP economically whole to its carry % (worked example in 5.1).
- Guardrail: reject `CatchUpRate` with `HurdleType = Hard` (a hard hurdle by definition has no catch-up).

### Fork D — Hurdle compounding
- Options: `Simple` (rate × period fraction), `Compounded` (geometric across sub-periods since last
  crystallization).
- **RECOMMENDED default: `Simple`.** Deterministic and matches most fund docs; `Compounded` reserved for
  funds whose LPA specifies an annually compounding preferred return.

### Fork E — Crystallization frequency
- Options: `Monthly`, `Quarterly`, `SemiAnnual`, `Annual`, `OnRedemptionOnly`.
- **RECOMMENDED default: `Annual`.** US HF standard. `Quarterly`/`Monthly` cover European retail funds;
  `CrystallizeOnRedemption` is an independent boolean layered on top (Section 5.2).

### Fork F — HWM / reset mode
- Options: `HighWaterMark` (pure HWM), `LossCarryforward` (LCF series, no HWM), `Both` (HWM plus an
  auditable LCF ledger that mirrors the shortfall).
- **RECOMMENDED default: `HighWaterMark`.** Equivalent to today. `Both` is recommended for funds that
  must *report* the outstanding loss to recoup even though HWM already implies it.

### Fork G — Accounting model (who owns the HWM)
- Options: `FundLevel` (one HWM for the fund; fee computed once then profit split — today's behavior),
  `InvestorSeries` (per-series HWM/LCF; fee computed per series against its own protected level).
- **RECOMMENDED default: `FundLevel`.** Regression-safe. `InvestorSeries` is the target for funds with
  investors that subscribed at different NAVs/dates and is the reason the durable state in
  Section 5.3 exists; a fund flips to it via config once the equalization method is chosen (O-3).

> **Cross-blueprint contract — HWM ownership (recorded 2026-08-01, revised after review).**
> `incentive_fee_state` (§7.2) is the **single durable owner of the high-water mark** under both
> equalization methods. Fork G selects the *scope* of a row, not a different store:
>
> | Fork G | Equalization method | HWM row in `incentive_fee_state` |
> |---|---|---|
> | `FundLevel` (default) | Method A — equalisation credit/debit (default) | Exactly one row per book, `series_id is null`. Equalisation *reallocates* the one fund fee across subscription lots; no per-investor rows exist. |
> | `InvestorSeries` | Method B — series of shares | One row per series, `series_id` set. The fee projector runs once per series. |
>
> **The HWM is stored per share in both methods** (`high_water_mark_per_share`), and the projector
> is called with `HighWaterMark = high_water_mark_per_share × unitsOutstanding`. A total-NAV HWM is
> *not* invariant to capital flows: a fund at HWM `10,000` with 100 shares that issues 100 more at
> `100` has ending NAV `20,000` and zero market return, but the projector would compute
> `0.20 × (20,000 − 10,000) = 2,000` of fee on pure contributed capital. Equalisation cannot undo
> that — it preserves the projector's total fund fee and only redistributes it. Storing per share
> makes the protected level invariant to subscriptions and redemptions, and matches what the
> equalization blueprint's glossary has always meant by `HWM`.
>
> Two consequences, both of which earlier drafts of this contract got wrong:
>
> - **`PartnershipInvestorAllocationInput.HighWaterMark` is not an owner.** It is a `sealed record`
>   constructor parameter — a transient per-period projector input. Under this contract it is
>   *hydrated from* `incentive_fee_state` before each `Project` call and written back through the
>   §5.4 roller. Nothing durable lives on it.
> - **`fund_series` does not carry a HWM column.** Equalization §10.3 previously proposed
>   `fund_series.high_water_mark_per_share`; that column is removed, and series HWM is read from
>   `incentive_fee_state` keyed by `series_id`. Keeping both would let crystallization advance one
>   HWM while the next fee calculation reads the other.
>
> The scope is a **series, never an investor** — `IncentiveFeeStateRecord.SeriesId`, not
> `InvestorId`. Per-investor HWM rows are not permitted under either method. If Fork G is set to
> `InvestorSeries`, this blueprint and the equalization blueprint's Method B must land as one slice.
> Both documents record this contract; the canonical copy is the
> [blueprint register](../../engineering/blueprints/README.md#cross-blueprint-contracts).

### Fork H — Downward accrual adjustments (NAV falls below prior accrual)
- Options: `ReverseAccrual` (post a contra entry Dr Payable / Cr Expense), `ClampToZeroNoReversal`
  (never post negatives; only ratchet up).
- **RECOMMENDED default: `ReverseAccrual`.** Keeps the accrued-fee liability honest intra-crystallization.
  Requires a new event kind (Section 6.3) because `AutomatedJournalDraftProjector.ValidateAmount` forbids
  non-positive amounts.

---

## 5. Domain model / new types

New types live in `src/Meridian.Ledger` (calculation + records) and `src/Meridian.Contracts/Ledger`
(shared enums used by DTOs). All follow repo style: `sealed record`, XML docs, `decimal` money,
`DateOnly` for dates, validated constructors that trim and range-check.

### 5.1 Hurdle + catch-up: the calculator

```csharp
namespace Meridian.Ledger;

/// <summary>Kind of hurdle (preferred return) applied before incentive fee is charged.</summary>
public enum HurdleType
{
    /// <summary>No hurdle; fee applies to the full excess over the high-water mark (today's behavior).</summary>
    None,
    /// <summary>Once the hurdle is cleared, fee applies to the whole excess (usually with GP catch-up).</summary>
    Soft,
    /// <summary>Fee applies only to the excess above the hurdle band; catch-up is not permitted.</summary>
    Hard,
}

/// <summary>What the hurdle rate is applied to for the period.</summary>
public enum HurdleBasis { BeginningNav, PriorHighWaterMark, ContributedCapital }

/// <summary>How the hurdle accrues over the accrual window.</summary>
public enum HurdleCompounding { Simple, Compounded }

/// <summary>
/// Immutable hurdle terms. An annual hurdle rate is converted to a period amount using
/// <see cref="PeriodFraction"/> supplied on the calculation context.
/// </summary>
public sealed record HurdleTerms
{
    public HurdleTerms(
        HurdleType hurdleType,
        decimal annualHurdleRate = 0m,
        HurdleBasis basis = HurdleBasis.BeginningNav,
        decimal catchUpRate = 0m,
        HurdleCompounding compounding = HurdleCompounding.Simple)
    {
        if (annualHurdleRate < 0m || annualHurdleRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(annualHurdleRate), annualHurdleRate, "Hurdle rate must be between 0 and 1.");
        if (catchUpRate < 0m || catchUpRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(catchUpRate), catchUpRate, "Catch-up rate must be between 0 and 1.");
        if (hurdleType == HurdleType.Hard && catchUpRate > 0m)
            throw new ArgumentException("A hard hurdle cannot carry a GP catch-up.", nameof(catchUpRate));
        if (hurdleType == HurdleType.None && (annualHurdleRate > 0m || catchUpRate > 0m))
            throw new ArgumentException("HurdleType.None must not specify a hurdle or catch-up rate.", nameof(hurdleType));

        HurdleType = hurdleType;
        AnnualHurdleRate = annualHurdleRate;
        Basis = basis;
        CatchUpRate = catchUpRate;
        Compounding = compounding;
    }

    public HurdleType HurdleType { get; }
    public decimal AnnualHurdleRate { get; }
    public HurdleBasis Basis { get; }
    /// <summary>GP share of the catch-up band; 1.0 == full (100%) catch-up.</summary>
    public decimal CatchUpRate { get; }
    public HurdleCompounding Compounding { get; }

    public static HurdleTerms NoHurdle { get; } = new(HurdleType.None);
}

/// <summary>Inputs to a single-period incentive-fee computation for one investor (or the fund).</summary>
public sealed record IncentiveFeeContext(
    decimal BeginningNav,
    decimal EndingNavBeforeIncentiveFee,   // already net of management fee
    decimal PriorHighWaterMark,
    decimal PriorLossCarryforward,          // >= 0; unrecouped loss to earn back first
    decimal ContributedCapital,             // used only when HurdleBasis.ContributedCapital
    decimal IncentiveFeeRate,               // carry, e.g. 0.20
    HurdleTerms Hurdle,
    decimal PeriodFraction);                // year fraction since last crystallization, e.g. 0.25

/// <summary>Result of an incentive-fee computation, including rolled-forward state candidates.</summary>
public sealed record IncentiveFeeResult(
    decimal IncentiveFee,           // rounded; >= 0
    decimal HurdleAmount,           // currency hurdle for the period
    decimal FeeableExcess,          // profit the fee was charged on (post-hurdle/catch-up allocation)
    decimal CatchUpFee,             // portion of IncentiveFee from the catch-up band
    decimal CarryFee,               // portion from the residual carry band
    decimal GrossExcessOverHwm,     // max(0, ending - hwm - lcf)
    bool HurdleCleared,
    decimal CandidateHighWaterMark, // HWM if this period crystallizes
    decimal CandidateLossCarryforward);
```

**Calculator** (`src/Meridian.Ledger/IncentiveFeeCalculator.cs`) — the single source of truth that both
`PartnershipInvestorAccountingProjector` and `FeeScheduleAccrualEventProducer` call:

```csharp
public static class IncentiveFeeCalculator
{
    public static IncentiveFeeResult Compute(IncentiveFeeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // 1. Excess over the protected level, net of any loss still to be recouped.
        var grossExcess = Math.Max(0m, ctx.EndingNavBeforeIncentiveFee - ctx.PriorHighWaterMark - ctx.PriorLossCarryforward);

        // 2. Currency hurdle for this period.
        var hurdleBase = ctx.Hurdle.Basis switch
        {
            HurdleBasis.BeginningNav        => ctx.BeginningNav,
            HurdleBasis.PriorHighWaterMark  => ctx.PriorHighWaterMark,
            HurdleBasis.ContributedCapital  => ctx.ContributedCapital,
            _ => ctx.BeginningNav,
        };
        var periodRate = ctx.Hurdle.Compounding == HurdleCompounding.Compounded
            ? Pow(1m + ctx.Hurdle.AnnualHurdleRate, ctx.PeriodFraction) - 1m
            : ctx.Hurdle.AnnualHurdleRate * ctx.PeriodFraction;
        var hurdleAmount = RoundCurrency(hurdleBase * periodRate);

        var c = ctx.IncentiveFeeRate;
        decimal fee, catchUpFee = 0m, carryFee, feeable;
        bool cleared;

        switch (ctx.Hurdle.HurdleType)
        {
            case HurdleType.None:
                cleared = grossExcess > 0m;
                feeable = grossExcess;
                carryFee = c * feeable;
                fee = carryFee;
                break;

            case HurdleType.Hard:
                // Fee only on profit ABOVE the hurdle band; no catch-up.
                cleared = grossExcess > hurdleAmount;
                feeable = Math.Max(0m, grossExcess - hurdleAmount);
                carryFee = c * feeable;
                fee = carryFee;
                break;

            case HurdleType.Soft:
                if (grossExcess <= hurdleAmount) { cleared = false; feeable = 0m; carryFee = 0m; fee = 0m; break; }
                cleared = true;
                var r = ctx.Hurdle.CatchUpRate;                    // 1.0 == full catch-up
                // Catch-up band width X solves r*X = c*(hurdle + X)  =>  X = c*hurdle/(r - c).
                var catchUpCap = r > c ? (c * hurdleAmount) / (r - c) : decimal.MaxValue;
                var catchUpBand = Math.Min(grossExcess - hurdleAmount, catchUpCap);
                catchUpFee = r * catchUpBand;
                var residual = Math.Max(0m, grossExcess - hurdleAmount - catchUpBand);
                carryFee = c * residual;
                feeable = catchUpBand + residual;               // hurdle band itself is not fee-bearing
                fee = catchUpFee + carryFee;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(ctx));
        }

        fee = RoundCurrency(fee);
        var endingAfterFee = ctx.EndingNavBeforeIncentiveFee - fee;
        var candidateHwm = Math.Max(ctx.PriorHighWaterMark, endingAfterFee);
        // LCF grows when NAV is below the protected level; shrinks as NAV recovers toward it.
        var shortfall = Math.Max(0m, ctx.PriorHighWaterMark + ctx.PriorLossCarryforward - ctx.EndingNavBeforeIncentiveFee);
        var candidateLcf = RoundCurrency(shortfall);

        return new IncentiveFeeResult(fee, hurdleAmount, feeable, RoundCurrency(catchUpFee),
            RoundCurrency(carryFee), grossExcess, cleared, candidateHwm, candidateLcf);
    }
}
```

**Worked examples** (using the existing test's numbers: BeginningNav 1000, ending-before-incentive after
a 20 management fee = 1180, prior HWM 1050 ⇒ `grossExcess = 1180 − 1050 = 130`, carry `c = 0.20`; annual
period so `PeriodFraction = 1`):

| Scenario | Hurdle | Catch-up | Fee | Why |
|----------|--------|----------|-----|-----|
| Today | None | — | **26.00** | `0.20 × 130`. Matches `PartnershipInvestorAccountingProjector` test (line 3007). |
| Soft 8% + full catch-up | Soft, hurdle 80 | 1.0 | **26.00** | catch-up cap `0.2×80/0.8 = 20`; band 20 → GP 20; residual `130−80−20 = 30` → `0.2×30 = 6`; total 26. Full catch-up returns the GP to 20% of the whole excess. |
| Hard 8% | Hard, hurdle 80 | — | **10.00** | `0.20 × (130 − 80)`. The 80 hurdle band is never fee-bearing. |
| Soft 15% + full catch-up | Soft, hurdle 150 | 1.0 | **0.00** | `grossExcess 130 ≤ hurdle 150` ⇒ hurdle not cleared. |

This shows why the fork matters: same NAVs, fees of 26 / 26 / 10 / 0 purely from configuration.

### 5.2 Crystallization schedule

```csharp
namespace Meridian.Ledger;

public enum IncentiveCrystallizationFrequency { Monthly, Quarterly, SemiAnnual, Annual, OnRedemptionOnly }

/// <summary>First-class crystallization schedule: when accrued incentive fee locks and the HWM rolls forward.</summary>
public sealed record IncentiveCrystallizationSchedule
{
    public IncentiveCrystallizationSchedule(
        IncentiveCrystallizationFrequency frequency,
        DateOnly anchorDate,
        bool crystallizeOnRedemption = true)
    {
        Frequency = frequency;
        AnchorDate = anchorDate;   // e.g. fund fiscal year-end; defines the phase of the cycle
        CrystallizeOnRedemption = crystallizeOnRedemption;
    }

    public IncentiveCrystallizationFrequency Frequency { get; }
    public DateOnly AnchorDate { get; }
    /// <summary>When true a redemption event crystallizes the redeeming interest regardless of frequency.</summary>
    public bool CrystallizeOnRedemption { get; }
}

/// <summary>Deterministic calendar helper deciding whether a period-end date crystallizes fee.</summary>
public static class CrystallizationCalendar
{
    /// <summary>True when <paramref name="periodEnd"/> falls on a scheduled crystallization boundary.</summary>
    public static bool IsCrystallizationDate(IncentiveCrystallizationSchedule schedule, DateOnly periodEnd, bool isRedemption = false);

    /// <summary>The next crystallization date on/after <paramref name="from"/> (for accrual windows / period fraction).</summary>
    public static DateOnly NextCrystallizationDate(IncentiveCrystallizationSchedule schedule, DateOnly from);

    /// <summary>Year fraction from the last crystallization boundary to <paramref name="periodEnd"/> (feeds HurdleTerms).</summary>
    public static decimal PeriodFractionSinceLastCrystallization(IncentiveCrystallizationSchedule schedule, DateOnly periodEnd);
}
```

Semantics: between crystallization dates the engine **accrues** (contingent liability, HWM unchanged);
on a crystallization date the accrued balance **locks**, the HWM rolls forward to the post-fee NAV, and
(per Fork F) the LCF resets. `OnRedemptionOnly` funds crystallize only when `isRedemption` is true.

### 5.3 Durable series-scoped HWM + loss-carryforward series

```csharp
namespace Meridian.Ledger;

public enum IncentiveResetMode { HighWaterMark, LossCarryforward, Both }

/// <summary>
/// Lifecycle of one HWM scope. Only Live scopes are hydrated for fee evaluation;
/// ListIncentiveFeeStatesAsync filters to Live so a closed or absorbed scope can never be
/// mistaken for an active one.
/// </summary>
public enum IncentiveFeeScopeStatus
{
    Live = 0,
    Closed = 1,        // redeemed to zero units on a crystallization date
    Consolidated = 2,  // Method B: absorbed into a lead series (equalization §6.2)
}
public enum IncentiveFeeAccountingModel { FundLevel, InvestorSeries }

/// <summary>
/// Durable, rolled-forward incentive-fee state for one HWM scope. This table is the **single
/// durable owner of the high-water mark** (see the HWM-ownership contract in Section 4, Fork G).
/// The scope is a *series*, never an investor: SeriesId is null for the fund-level series
/// (Method A, exactly one row per book) and carries the series key under Method B (one row per
/// series). This replaces "HWM passed in per period" with persisted state.
/// </summary>
public sealed record IncentiveFeeStateRecord(
    Guid StateRecordId,
    Guid LedgerBookId,
    string FundProfileId,
    string? SeriesId,                   // null == fund-level series (Method A)
    decimal HighWaterMarkPerShare,      // PER SHARE in both methods — see the Fork G contract
    IncentiveFeeScopeStatus Status,     // Live | Closed | Consolidated — see below
    decimal LossCarryforward,           // >= 0
    DateOnly? LastCrystallizedDate,
    decimal CumulativeCrystallizedFee,
    decimal AccruedFeeBalance,          // contingent liability not yet crystallized
    string PolicyId,
    string PolicyVersion,
    DateTimeOffset UpdatedAt,
    long Version);                      // optimistic concurrency, mirrors LedgerAccountingPeriod.Version

/// <summary>Immutable audit snapshot: one row per (series, period) roll-forward.</summary>
public sealed record IncentiveFeeStateSnapshotRecord(
    Guid SnapshotId,
    Guid StateRecordId,
    string PeriodId,
    DateOnly AsOfDate,
    decimal HighWaterMarkBefore,
    decimal HighWaterMarkAfter,
    decimal LossCarryforwardBefore,
    decimal LossCarryforwardAfter,
    decimal AccruedFeeDelta,            // signed; negative == reversal
    decimal CrystallizedFee,
    bool Crystallized,
    Guid? SourceJournalEntryId,
    DateTimeOffset CreatedAt);
```

Roll-forward rules (pure, in `IncentiveFeeStateRoller.Roll(prior, result, crystallizes)`):

- **Accrual period (not crystallizing):** `AccruedFeeBalance' = result.IncentiveFee` (snapshot records the
  delta vs prior accrual; a decrease is a reversal — Fork H). HWM and `LastCrystallizedDate` unchanged.
  LCF updates to `result.CandidateLossCarryforward` for reporting when `ResetMode` ∈ {LossCarryforward, Both}.
- **Crystallization period:** the scope's HWM advances to `result.CandidateHighWaterMark` — writing
  `HighWaterMark'` on a fund-level row (`SeriesId is null`) or `HighWaterMarkPerShare'` on a series
  row, per the Fork G contract above. Then
  `CumulativeCrystallizedFee' += AccruedFeeBalance(→ result.IncentiveFee)`, `AccruedFeeBalance' = 0`,
  `LastCrystallizedDate' = periodEnd`, `LossCarryforward' = 0` when `ResetMode == HighWaterMark` else
  `result.CandidateLossCarryforward`.

  This roller is the **only** writer of the HWM. Because `incentive_fee_state` is the single durable
  owner, no equalization-side code advances a HWM independently.

  **Unit discipline — applies to every row, both methods.**
  `result.CandidateHighWaterMark` comes out of `PartnershipInvestorAccountingProjector` in
  **total-NAV terms**, but every row persists `HighWaterMarkPerShare`. Divide by the units
  outstanding used for that period's `EndingNavBeforeFees` before writing, and multiply back when
  hydrating the next period's input (equalization §6.1). Writing the total-NAV candidate straight
  into the per-share column silently overstates every subsequent period's fee.

  **Zero units — full redemption on a crystallization date.** `CrystallizeOnRedemption` funds can
  crystallize a scope that redeems to zero units, where the divide-back is undefined. Handle it
  explicitly rather than dividing:

  - Compute the period's fee using the units outstanding **immediately before** the redemption —
    that is the population the fee is owed on.
  - Do **not** write a per-share HWM. Set `Status = Closed` (§5.3) and leave
    `HighWaterMarkPerShare` at its prior value as a historical record; a closed scope is never
    hydrated again.
  - A scope that later re-opens (a new series with the same investor) is a **new row** with its own
    issue-price seed, not a revival of the closed one.

  The snapshot row still records the crystallized fee, so the audit trail is complete.

### 5.4 The policy aggregate

```csharp
namespace Meridian.Ledger;

/// <summary>Durable, versioned incentive-fee terms for a fund/ledger book. One config drives US and EU funds.</summary>
public sealed record IncentiveFeePolicy
{
    public IncentiveFeePolicy(
        string policyId,
        string fundProfileId,
        Guid ledgerBookId,
        decimal incentiveFeeRate,
        HurdleTerms hurdle,
        IncentiveCrystallizationSchedule crystallization,
        IncentiveFeeAccountingModel accountingModel,
        IncentiveResetMode resetMode,
        DateOnly effectiveDate,
        string policyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        ArgumentNullException.ThrowIfNull(hurdle);
        ArgumentNullException.ThrowIfNull(crystallization);
        if (incentiveFeeRate < 0m || incentiveFeeRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(incentiveFeeRate), incentiveFeeRate, "Incentive fee rate must be between 0 and 1.");
        // ... trim + assign
    }

    public string PolicyId { get; }
    public string FundProfileId { get; }
    public Guid LedgerBookId { get; }
    public decimal IncentiveFeeRate { get; }
    public HurdleTerms Hurdle { get; }
    public IncentiveCrystallizationSchedule Crystallization { get; }
    public IncentiveFeeAccountingModel AccountingModel { get; }
    public IncentiveResetMode ResetMode { get; }
    public DateOnly EffectiveDate { get; }
    public string PolicyVersion { get; }

    /// <summary>Behavior-preserving default equivalent to today: no hurdle, annual crystallization, fund-level HWM.</summary>
    public static IncentiveFeePolicy LegacyDefault(string fundProfileId, Guid ledgerBookId, decimal incentiveFeeRate, DateOnly effectiveDate) =>
        new("legacy-hwm", fundProfileId, ledgerBookId, incentiveFeeRate, HurdleTerms.NoHurdle,
            new IncentiveCrystallizationSchedule(IncentiveCrystallizationFrequency.Annual, new DateOnly(effectiveDate.Year, 12, 31)),
            IncentiveFeeAccountingModel.FundLevel, IncentiveResetMode.HighWaterMark, effectiveDate, "v1");
}
```

`HurdleType`, `HurdleBasis`, `IncentiveCrystallizationFrequency`, `IncentiveResetMode`, and
`IncentiveFeeAccountingModel` **must be mirrored as thin enums in `src/Meridian.Contracts/Ledger`**
so the Section 7 DTOs can name them.

> Referencing the `Meridian.Ledger` enums directly from contracts is **not** an option, though an
> earlier draft of this blueprint offered it. `Meridian.Contracts` has no `ProjectReference` at all
> — it is a leaf, and the graph runs `Meridian.Ledger` → `Meridian.Core` → `Meridian.Contracts`, so
> a direct reference would need Contracts→Ledger and invert it. The `LedgerTaxLotReliefMethod`
> precedent that draft cited does not support the claim either: that enum lives in
> `src/Meridian.Ledger/LedgerTaxLotReliefMethod.cs`, so Contracts cannot name it directly for the
> same reason. See the [register's DTO-layering convention](../../engineering/blueprints/README.md#dto-layering).

---

## 6. Interfaces

### 6.1 Policy + state store (extend `ILedgerJournalStore`, default-throwing)

Add to `src/Meridian.Storage/Ledger/ILedgerJournalStore.cs`, mirroring the tax-lot methods exactly:

```csharp
// --- Incentive-fee policy ---
Task<IncentiveFeePolicyRecord> SaveIncentiveFeePolicyAsync(IncentiveFeePolicyRecord policy, CancellationToken ct = default)
    => Task.FromException<IncentiveFeePolicyRecord>(new NotSupportedException("This ledger journal store does not support incentive-fee policy persistence."));

Task<IReadOnlyList<IncentiveFeePolicyRecord>> ListIncentiveFeePoliciesAsync(Guid ledgerBookId, CancellationToken ct = default)
    => Task.FromException<IReadOnlyList<IncentiveFeePolicyRecord>>(new NotSupportedException("This ledger journal store does not support incentive-fee policy persistence."));

// --- Incentive-fee state series ---
// Keyed by SERIES, not investor (§4 Fork G HWM contract). seriesId == null selects the
// fund-level row (Method A). Under Method B one investor may hold several series, so an
// investor-keyed lookup would collapse distinct HWMs onto one row.
Task<IncentiveFeeStateRecord?> GetIncentiveFeeStateAsync(Guid ledgerBookId, string? seriesId, CancellationToken ct = default)
    => Task.FromException<IncentiveFeeStateRecord?>(new NotSupportedException("This ledger journal store does not support incentive-fee state persistence."));

/// <summary>Live HWM scopes for a book (status = Live only): one row under Method A, one per series under Method B.</summary>
Task<IReadOnlyList<IncentiveFeeStateRecord>> ListIncentiveFeeStatesAsync(Guid ledgerBookId, CancellationToken ct = default)
    => Task.FromException<IReadOnlyList<IncentiveFeeStateRecord>>(new NotSupportedException("This ledger journal store does not support incentive-fee state persistence."));

/// <summary>
/// Method B: open a series and its HWM scope in ONE database transaction. Both rows commit or
/// neither does — a series registered without its HWM scope is corrupt state, and two separate
/// store calls cannot give that guarantee (a crash between them leaves exactly that).
/// The adapter owns the transaction; callers never compose this from smaller operations.
/// </summary>
Task<IncentiveFeeStateRecord> CreateSeriesWithStateAsync(FundSeriesDefinition series, IncentiveFeeStateRecord seedState, CancellationToken ct = default)
    => Task.FromException<IncentiveFeeStateRecord>(new NotSupportedException("This ledger journal store does not support incentive-fee state persistence."));

/// <summary>
/// Method B: consolidate an absorbed series into its lead in ONE transaction — record the
/// consolidation, set the absorbed scope's Status to Consolidated, and (per equalization §6.2)
/// leave the lead scope's HWM untouched. Prevents an orphaned scope that ListIncentiveFeeStatesAsync
/// would still return as live.
/// </summary>
Task ConsolidateSeriesStateAsync(SeriesConsolidation consolidation, CancellationToken ct = default)
    => Task.FromException(new NotSupportedException("This ledger journal store does not support incentive-fee state persistence."));

/// <summary>Close a scope that redeemed to zero units on a crystallization date (Status = Closed).</summary>
Task CloseIncentiveFeeStateAsync(Guid stateRecordId, long expectedVersion, CancellationToken ct = default)
    => Task.FromException(new NotSupportedException("This ledger journal store does not support incentive-fee state persistence."));

/// <summary>Persist rolled-forward state and its audit snapshot atomically; enforces optimistic Version.</summary>
Task<IncentiveFeeStateRecord> SaveIncentiveFeeStateAsync(IncentiveFeeStateRecord state, IncentiveFeeStateSnapshotRecord snapshot, long expectedVersion, CancellationToken ct = default)
    => Task.FromException<IncentiveFeeStateRecord>(new NotSupportedException("This ledger journal store does not support incentive-fee state persistence."));

Task<IReadOnlyList<IncentiveFeeStateSnapshotRecord>> ListIncentiveFeeStateSnapshotsAsync(Guid stateRecordId, CancellationToken ct = default)
    => Task.FromException<IReadOnlyList<IncentiveFeeStateSnapshotRecord>>(new NotSupportedException("This ledger journal store does not support incentive-fee state persistence."));
```

`IncentiveFeePolicyRecord` is the persistence projection of `IncentiveFeePolicy` (flattened hurdle/schedule
columns, plus `RecordId`, `CreatedAt`, `UpdatedAt`). The Postgres implementation lands in
`src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.cs`, alongside the existing tax-lot persistence,
and honors the same `IFundScopeTenantAccessor` tenant stamping used for fund-scoped reads.

### 6.2 Calendar + orchestration services

```csharp
namespace Meridian.Ledger;

/// <summary>Loads state + policy, computes accrual/crystallization for a period, and rolls state forward.</summary>
public interface IIncentiveFeeStateService
{
    Task<IncentiveFeeAccrualOutcome> EvaluatePeriodAsync(IncentiveFeePeriodRequest request, CancellationToken ct = default);
    Task<IncentiveFeeStateRecord> CommitAsync(IncentiveFeeAccrualOutcome outcome, string actor, IReadOnlyList<string> evidenceLinks, CancellationToken ct = default);
}
```

`IncentiveFeePeriodRequest` carries `LedgerBookId`, `FundProfileId`, `PeriodId`, `AsOfDate`,
`BeginningNav`, `EndingNavBeforeIncentiveFee`, the **series roster** (for `InvestorSeries` — a list of
`SeriesId` with that series' units outstanding, since the HWM is per-share and the projector needs
total-NAV terms; see equalization §6.1), and an optional `IsRedemption` flag. It is deliberately *not*
an investor roster: one investor may hold several series, and keying the evaluation by investor would
collapse their distinct HWMs onto a single row. `IncentiveFeeAccrualOutcome` bundles the per-series `IncentiveFeeResult`, the
resulting `AutomatedJournalEvent`s, and the candidate `IncentiveFeeStateRecord`/snapshot — so
`EvaluatePeriodAsync` is a pure preview and `CommitAsync` is the governed side-effect.

### 6.3 New event kinds (ledger flow)

Add to `AutomatedJournalEventKind` and `AutomatedJournalDraftProjector.ProjectLines` switch:

- **`PerformanceFeeAccrualReversed`** — Dr `PerformanceFeePayableFor(scope)` / Cr
  `PerformanceFeeExpenseFor(scope)`. Handles Fork H downward adjustments while keeping `Amount > 0`
  (`ValidateAmount` unchanged).
- **`IncentiveFeeCrystallized`** — reclass/settlement on a crystallization date. Default mapping:
  Dr `PerformanceFeePayableFor(scope)` / Cr `CashAccount(scope)` when the crystallized fee is paid, or a
  reclass to a segregated crystallized-payable account when a fund opts into 6.3-optional accounts. For
  funds that simply lock the accrued balance without cash movement, crystallization posts no journal and
  only rolls state (the accrual entries already sit in `PerformanceFeePayable`).

Optional segregation factories in `LedgerAccounts.cs` (only if a fund wants crystallized separated from
running accrual):

```csharp
public static LedgerAccount IncentiveFeeCrystallizedPayableFor(string fundId) =>
    CreateScoped("Incentive Fee Crystallized Payable", LedgerAccountType.Liability, fundId);
```

`ManagementFeeAccrued` and the existing `PerformanceFeeAccrued` mapping are unchanged, so nothing about the
current accrual path regresses.

---

## 7. Persistence & migrations

Follow `V_ledger_###__name.sql` with `__SCHEMA__`, `create table/index if not exists`, `numeric(38, 12)`
money precision, `timestamptz`, and `references __SCHEMA__.ledger_books(ledger_book_id) on delete cascade`.
The highest ordinal on disk is `V_ledger_028__wash_sale_activation.sql`; this blueprint's reserved
range is **029–030** ([register](../../engineering/blueprints/README.md#ledger-migration-ordinals)).
Re-derive from disk at implementation time and update the register if another lane lands first.

### 7.1 `V_ledger_029__incentive_fee_policy.sql`

```sql
create table if not exists __SCHEMA__.incentive_fee_policies (
    policy_record_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id) on delete cascade,
    fund_profile_id text not null,
    policy_id text not null,
    policy_version text not null,
    incentive_fee_rate numeric(38, 12) not null,
    hurdle_type text not null,                       -- 'None' | 'Soft' | 'Hard'
    annual_hurdle_rate numeric(38, 12) not null default 0,
    hurdle_basis text not null default 'BeginningNav',
    catch_up_rate numeric(38, 12) not null default 0,
    hurdle_compounding text not null default 'Simple',
    crystallization_frequency text not null,         -- 'Monthly' | 'Quarterly' | 'SemiAnnual' | 'Annual' | 'OnRedemptionOnly'
    crystallization_anchor_date date not null,
    crystallize_on_redemption boolean not null default true,
    accounting_model text not null default 'FundLevel',
    reset_mode text not null default 'HighWaterMark',
    effective_date date not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    constraint ck_incentive_fee_policies_rate check (incentive_fee_rate >= 0 and incentive_fee_rate <= 1),
    constraint ck_incentive_fee_policies_hurdle_rate check (annual_hurdle_rate >= 0 and annual_hurdle_rate <= 1),
    constraint ck_incentive_fee_policies_catch_up check (catch_up_rate >= 0 and catch_up_rate <= 1),
    constraint ck_incentive_fee_policies_hurdle_type check (hurdle_type in ('None', 'Soft', 'Hard')),
    constraint ck_incentive_fee_policies_frequency
        check (crystallization_frequency in ('Monthly', 'Quarterly', 'SemiAnnual', 'Annual', 'OnRedemptionOnly')),
    constraint ck_incentive_fee_policies_hard_no_catchup
        check (not (hurdle_type = 'Hard' and catch_up_rate > 0))
);

create unique index if not exists ux_incentive_fee_policies_book_policy_effective
    on __SCHEMA__.incentive_fee_policies (ledger_book_id, policy_id, effective_date);

create index if not exists ix_incentive_fee_policies_fund
    on __SCHEMA__.incentive_fee_policies (fund_profile_id, effective_date desc);
```

### 7.2 `V_ledger_030__incentive_fee_state.sql`

```sql
create table if not exists __SCHEMA__.incentive_fee_state (
    state_record_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id) on delete cascade,
    fund_profile_id text not null,
    -- Scope is a series, never an investor. This table is the single durable HWM owner.
    series_id text null,                               -- null == fund-level series (Method A)
    -- PER SHARE in both methods. A total-NAV HWM is not invariant to capital flows: a subscription
    -- raises ending NAV without being gain, so the projector would charge fee on contributed
    -- capital. Per-share is also what the equalization glossary means by HWM.
    high_water_mark_per_share numeric(38, 12) not null,
    status text not null default 'Live',               -- Live | Closed | Consolidated
    loss_carryforward numeric(38, 12) not null default 0,
    last_crystallized_date date null,
    cumulative_crystallized_fee numeric(38, 12) not null default 0,
    accrued_fee_balance numeric(38, 12) not null default 0,
    policy_id text not null,
    policy_version text not null,
    updated_at timestamptz not null,
    version bigint not null default 0,
    constraint ck_incentive_fee_state_lcf check (loss_carryforward >= 0),
    constraint ck_incentive_fee_state_hwm check (high_water_mark_per_share >= 0)
);

-- One LIVE scope per (book, series); coalesce so the fund-level (null) series has a stable key.
-- Partial on status so a closed or consolidated scope stays queryable for history without
-- blocking a replacement scope from being opened under the same key.
create unique index if not exists ux_incentive_fee_state_book_series
    on __SCHEMA__.incentive_fee_state (ledger_book_id, coalesce(series_id, ''))
    where status = 'Live';

create table if not exists __SCHEMA__.incentive_fee_state_snapshots (
    snapshot_id uuid primary key,
    state_record_id uuid not null references __SCHEMA__.incentive_fee_state(state_record_id) on delete cascade,
    period_id text not null,
    as_of_date date not null,
    high_water_mark_before numeric(38, 12) not null,
    high_water_mark_after numeric(38, 12) not null,
    loss_carryforward_before numeric(38, 12) not null,
    loss_carryforward_after numeric(38, 12) not null,
    accrued_fee_delta numeric(38, 12) not null,       -- signed; negative == reversal
    crystallized_fee numeric(38, 12) not null default 0,
    crystallized boolean not null default false,
    source_journal_entry_id uuid null references __SCHEMA__.journal_entries(journal_entry_id),
    created_at timestamptz not null
);

create unique index if not exists ux_incentive_fee_state_snapshots_state_period
    on __SCHEMA__.incentive_fee_state_snapshots (state_record_id, period_id);

create index if not exists ix_incentive_fee_state_snapshots_state_asof
    on __SCHEMA__.incentive_fee_state_snapshots (state_record_id, as_of_date desc);
```

`SaveIncentiveFeeStateAsync` writes the `incentive_fee_state` upsert and the snapshot insert in **one
transaction** guarded by `version = expectedVersion` (increment on success), mirroring
`SavePeriodAsync`'s optimistic-concurrency pattern so concurrent period runs cannot silently clobber the
HWM series.

---

## 8. Projector + governed-draft integration

### 8.1 Where the calculator plugs in

**`PartnershipInvestorAccountingProjector.Project`** — replace the single line
`var performanceFee = RoundCurrency(incentiveBase * input.PerformanceFeeRate);` with a call to
`IncentiveFeeCalculator.Compute`. To stay backward-compatible, add an **overload** that accepts an
`IncentiveFeePolicy` + prior `IncentiveFeeStateRecord`; the existing constructor path builds a
`HurdleTerms.NoHurdle` context so current callers/tests are byte-for-byte unchanged. `EndingNavBeforeIncentiveFee`
is `input.EndingNavBeforeFees − managementFee` (matching how `incentiveBase` already nets the management
fee). The projection surfaces the new `IncentiveFeeResult` fields (hurdle amount, catch-up split) alongside
the existing `PerformanceFee`/`UpdatedHighWaterMark`.

**`FeeScheduleAccrualEventProducer.Produce`** — replace the mirrored lines 239-241 with the same
`IncentiveFeeCalculator.Compute` call, loading `HurdleTerms`/rate from the policy instead of a bare
`PerformanceFeeRate`. The idempotency keys extend to include the crystallization boundary so intra-period
re-accruals are distinguishable: `perf-fee|{fundId}|{periodId}` stays for accrual; crystallization uses
`incentive-crystallize|{fundId}|{crystallizationDate}`.

### 8.2 End-to-end governed flow

```
IIncentiveFeeStateService.EvaluatePeriodAsync
  ├─ load IncentiveFeePolicy (Get... / effective-dated)         [store]
  ├─ load prior IncentiveFeeStateRecord per series              [store]
  ├─ for each series: IncentiveFeeCalculator.Compute(ctx)       [pure]
  ├─ CrystallizationCalendar.IsCrystallizationDate(...)         [pure]
  ├─ build AutomatedJournalEvent(s):
  │     PerformanceFeeAccrued            (accrual up)
  │     PerformanceFeeAccrualReversed    (accrual down, Fork H)
  │     IncentiveFeeCrystallized         (on crystallization, if cash/reclass)
  └─ IncentiveFeeStateRoller.Roll(prior, result, crystallizes) → candidate state + snapshot

IIncentiveFeeStateService.CommitAsync
  ├─ AutomatedJournalDraftProjector.Project(event) → AutomatedJournalDraft   (per event)
  ├─ AutomatedJournalApproval.Submit(draft, actor, now, reason)              (Draft→Submitted; refuses unbalanced)
  ├─ .Approve(actor, now, reason, evidenceLinks)                             (requires evidence)
  ├─ .PostTo(ledger, actor, now, reason, evidenceLinks)                      (Approved→Posted; ledger.Post)
  └─ store.SaveIncentiveFeeStateAsync(state, snapshot, expectedVersion)      (atomic roll-forward, sets snapshot.SourceJournalEntryId)
```

The governed lifecycle is untouched: `AutomatedJournalApproval` still enforces
`Draft→Submitted→Approved→Posted` and evidence on approve/post via
`LedgerGovernedLifecycle.PrepareTransition`. The only new invariant is that **state is committed only after
the journal posts** and the snapshot records the posting `JournalEntryId`, so the HWM series and the ledger
never disagree.

### 8.3 Ordering and idempotency

- Management fee is computed and netted first (unchanged), so the incentive `EndingNavBeforeIncentiveFee`
  is always net of it.
- Re-running a period is safe: `AutomatedJournalEvent.IdempotencyKey` (existing guard) blocks duplicate
  postings, and `SaveIncentiveFeeStateAsync`'s `expectedVersion` rejects a stale roll-forward. A re-run of a
  non-crystallization period recomputes the accrual delta against the persisted `AccruedFeeBalance`.

---

## 9. Contracts / DTOs, endpoints, and UI surfaces

### 9.1 DTOs (`src/Meridian.Contracts`)

`sealed record` DTOs mirroring the domain, JSON via the repo's existing source-generated context:

```csharp
public sealed record IncentiveFeePolicyDto(
    string PolicyId, string FundProfileId, Guid LedgerBookId, decimal IncentiveFeeRate,
    string HurdleType, decimal AnnualHurdleRate, string HurdleBasis, decimal CatchUpRate, string HurdleCompounding,
    string CrystallizationFrequency, DateOnly CrystallizationAnchorDate, bool CrystallizeOnRedemption,
    string AccountingModel, string ResetMode, DateOnly EffectiveDate, string PolicyVersion);

public sealed record IncentiveFeeStateDto(
    string FundProfileId, string? SeriesId,
    decimal HighWaterMarkPerShare, string Status, decimal LossCarryforward,
    DateOnly? LastCrystallizedDate, decimal CumulativeCrystallizedFee, decimal AccruedFeeBalance);

public sealed record IncentiveFeeAccrualPreviewRequest(
    Guid LedgerBookId, string PeriodId, DateOnly AsOfDate,
    decimal BeginningNav, decimal EndingNavBeforeFees, bool IsRedemption = false);

public sealed record IncentiveFeeAccrualPreviewResponse(
    decimal ManagementFee, decimal IncentiveFee, decimal HurdleAmount, decimal CatchUpFee, decimal CarryFee,
    bool HurdleCleared, bool Crystallizes, IReadOnlyList<IncentiveFeeStateDto> ProjectedState);
```

### 9.2 Endpoint routes (`src/Meridian.Contracts/Api/UiApiRoutes.cs`) + handlers (`LedgerEndpoints.cs`)

Follow the `/api/ledger/...` convention and the `app.MapGet/MapPost(UiApiRoutes.X, async (...) => {...})`
handler style:

```csharp
public const string LedgerIncentiveFeePolicies       = "/api/ledger/incentive-fee/policies";
public const string LedgerIncentiveFeePolicyByBook   = "/api/ledger/incentive-fee/policies/{ledgerBookId:guid}";
public const string LedgerIncentiveFeeState          = "/api/ledger/incentive-fee/state/{ledgerBookId:guid}";
public const string LedgerIncentiveFeeAccrualPreview = "/api/ledger/incentive-fee/accrual-preview";
public const string LedgerIncentiveFeeCrystallize    = "/api/ledger/incentive-fee/crystallize";
```

- `GET LedgerIncentiveFeePolicyByBook` — list effective-dated policies for a book.
- `POST LedgerIncentiveFeePolicies` — upsert a policy (validated by the aggregate; audited like posting-rule
  upserts).
- `GET LedgerIncentiveFeeState` — per-series HWM/LCF + snapshot history for the workbench.
- `POST LedgerIncentiveFeeAccrualPreview` — pure `EvaluatePeriodAsync` preview (no posting).
- `POST LedgerIncentiveFeeCrystallize` — governed `CommitAsync` for a crystallization date (Submit→Approve→
  Post + state roll-forward); requires actor + evidence, consistent with the other governed ledger POSTs.

### 9.3 UI surfaces

Stays inside the approved top-level nav (`Accounting`). Two additions under **Accounting**, both backed by
`src/Meridian.Ui.Services` / `src/Meridian.Ui.Shared` read models so the browser workstation
(`src/Meridian.Ui/dashboard/`) and WPF (`src/Meridian.Wpf/`, parity lane `W8-WPF-PARITY-001`) share one seam:

- **Fee Terms** (config panel): edit `IncentiveFeePolicy` — hurdle type/rate/basis, catch-up %, crystallization
  frequency + anchor, accounting model, reset mode. Inline preview using the accrual-preview endpoint.
- **Incentive Fee Workbench**: per-series (or fund-level) HWM and LCF series, accrued vs crystallized fee,
  the snapshot timeline, and a "preview next accrual/crystallization" action that renders the calculator's
  hurdle/catch-up/carry breakdown. Crystallization triggers the governed approve/post drawer used elsewhere
  in the ledger UI.

No mobile lane; responsive browser validation only.

### 9.4 Waterfall consistency (optional)

`PartnershipWaterfallProjector` can gain a `SynthesizeIncentiveTiers(IncentiveFeePolicy, hurdleAmount)`
helper that emits an ordered `preferred-return → catch-up → carry` tier set equivalent to the calculator, so
distribution modeling and fee accrual never disagree. This is additive and does not change the existing tier
walk.

---

## 10. Test plan (xUnit + FluentAssertions)

Mirror `tests/Meridian.Tests/Ledger/LedgerIntegrationTests.cs` style: `[Fact]` methods named
`Subject_Behavior`, arrange a record input, `.Should().Be(...)` assertions on exact decimals. New file
`tests/Meridian.Tests/Ledger/IncentiveFeeCalculatorTests.cs` plus additions to `LedgerIntegrationTests.cs`.

**Calculator (pure) — `IncentiveFeeCalculatorTests`:**
1. `Compute_NoHurdle_MatchesLegacyPerformanceFee` — grossExcess 130, c 0.20 ⇒ 26.00 (parity with the
   existing `PartnershipInvestorAccountingProjector` test).
2. `Compute_SoftHurdleFullCatchUp_ReturnsGpToCarryShare` — hurdle 80, catch-up 1.0 ⇒ fee 26.00, CatchUpFee
   20.00, CarryFee 6.00, HurdleCleared true.
3. `Compute_SoftHurdlePartialProfit_StaysInCatchUpBand` — grossExcess 90, hurdle 80 ⇒ fee 10.00 all from
   catch-up (band 10, cap 20), CarryFee 0.
4. `Compute_HardHurdle_ChargesOnlyAboveHurdle` — hurdle 80 ⇒ fee 10.00, CatchUpFee 0.
5. `Compute_HurdleNotCleared_ReturnsZero` — grossExcess 130, hurdle 150 ⇒ fee 0, HurdleCleared false.
6. `Compute_BelowHighWaterMark_ChargesNothingAndGrowsLcf` — ending < HWM ⇒ fee 0, `CandidateLossCarryforward`
   equals the shortfall.
7. `Compute_PartialPeriodFraction_ScalesHurdle` — annual 8%, PeriodFraction 0.25 ⇒ hurdle uses 2% of basis.
8. `Compute_CompoundedHurdle_DiffersFromSimple` — same annual rate, `HurdleCompounding.Compounded` vs
   `Simple` yields the expected geometric hurdle.
9. `HurdleTerms_HardWithCatchUp_Throws` / `HurdleTerms_NoneWithRate_Throws` — constructor guards.
10. Rounding: `Compute_Rounds_AwayFromZero_ToTwoPlaces`.

**State roll-forward — `IncentiveFeeStateRollerTests`:**
11. `Roll_AccrualPeriod_AdvancesAccruedNotHwm`.
12. `Roll_CrystallizationPeriod_LocksFeeAndAdvancesHwm`.
13. `Roll_NavRecovery_ReducesLossCarryforward` and `Roll_HwmResetMode_ZeroesLcfOnCrystallization`.
14. `Roll_AccrualDecrease_ProducesNegativeDelta` (Fork H reversal).

**Calendar — `CrystallizationCalendarTests`:**
15. `IsCrystallizationDate_Quarterly_TrueOnQuarterEnds`, `..._Annual_TrueOnAnchorMonthDay`,
    `..._OnRedemptionOnly_TrueOnlyForRedemption`.
16. `PeriodFractionSinceLastCrystallization_ReturnsYearFraction`.

**Projector + governed flow (integration) — additions to `LedgerIntegrationTests`:**
17. `PartnershipInvestorAccountingProjector_WithNoHurdlePolicy_MatchesLegacyFeeAndHwm` (regression).
18. `PartnershipInvestorAccountingProjector_WithSoftHurdleFullCatchUp_ProducesBalancedDraft` —
    `.IsBalanced.Should().BeTrue()`, expense/payable lines equal the computed fee.
19. `IncentiveFeeStateService_AccrueThenCrystallize_PostsAndRollsForward` — Submit→Approve→PostTo path;
    asserts `Ledger.GetBalance(LedgerAccounts.PerformanceFeePayableFor(fund))`, the rolled HWM, and the
    snapshot's `SourceJournalEntryId`.
20. `FeeScheduleAccrualEventProducer_UsesSharedCalculator_MatchesProjector` — the producer and projector
    return identical fees for the same policy (guards the two copies from drifting).
21. `IncentiveFeeStateService_RerunPeriod_IsIdempotent` — second run posts nothing new
    (idempotency key + `expectedVersion`).
22. `InvestorSeries_ComputesFeePerSeriesHwm` — two *series* with different prior HWMs get different
    fees, including the case where both series belong to the same investor (the regression that an
    investor-keyed store would silently collapse).

**Store (Postgres integration, mirroring tax-lot store tests):**
23. `PostgresLedgerJournalStore_SaveAndGetIncentiveFeeState_RoundTrips` and
    `..._SaveIncentiveFeeState_RejectsStaleVersion`.

Run targeted: `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
--filter FullyQualifiedName~IncentiveFee`.

---

## 11. Implementation checklist (ordered, code-ready)

1. **Enums + `HurdleTerms`** — add `HurdleType`, `HurdleBasis`, `HurdleCompounding`,
   `IncentiveCrystallizationFrequency`, `IncentiveResetMode`, `IncentiveFeeAccountingModel`, and the
   `HurdleTerms` record (with guards) in `src/Meridian.Ledger`. Mirror the enums needed by DTOs into
   `src/Meridian.Contracts/Ledger`.
2. **Calculator** — add `IncentiveFeeContext`, `IncentiveFeeResult`, and `IncentiveFeeCalculator.Compute`
   (Section 5.1). Unit-test first (tests 1-10).
3. **Calendar** — add `IncentiveCrystallizationSchedule` + `CrystallizationCalendar` (tests 15-16).
4. **State model + roller** — add `IncentiveFeeStateRecord`, `IncentiveFeeStateSnapshotRecord`,
   `IncentiveResetMode` handling, and `IncentiveFeeStateRoller.Roll` (tests 11-14).
5. **Policy aggregate** — add `IncentiveFeePolicy` (+ `LegacyDefault`) and `IncentiveFeePolicyRecord`.
6. **Refactor to shared calculator** — route `PartnershipInvestorAccountingProjector.Project` (new policy
   overload; legacy overload builds `HurdleTerms.NoHurdle`) and `FeeScheduleAccrualEventProducer.Produce`
   through `IncentiveFeeCalculator`. Run existing partnership tests unchanged (test 17, 20).
7. **Event kinds** — add `PerformanceFeeAccrualReversed` and `IncentiveFeeCrystallized` to
   `AutomatedJournalEventKind` and `AutomatedJournalDraftProjector.ProjectLines`; optional
   `IncentiveFeeCrystallizedPayableFor` in `LedgerAccounts.cs`.
8. **Store interface** — add the five default-throwing methods and row records to `ILedgerJournalStore.cs`.
9. **Migrations** — add `V_ledger_029__incentive_fee_policy.sql` and `V_ledger_030__incentive_fee_state.sql`
   (reserved range; confirm against the highest ordinal on disk first).
10. **Postgres store** — implement the new methods in `PostgresLedgerJournalStore.cs` with tenant stamping
    and transactional roll-forward (`expectedVersion`); store tests 23.
11. **Orchestration service** — implement `IIncentiveFeeStateService` (`EvaluatePeriodAsync` pure preview,
    `CommitAsync` governed Submit→Approve→PostTo + `SaveIncentiveFeeStateAsync`); integration tests 18-22.
12. **DTOs + routes + endpoints** — add DTOs to `Meridian.Contracts`, routes to `UiApiRoutes.cs`, and
    handlers to `LedgerEndpoints.cs`; register in the source-generated JSON context.
13. **UI** — Fee Terms config panel + Incentive Fee Workbench in `src/Meridian.Ui/dashboard/` over the shared
    read model; WPF parity follow-up under `W8-WPF-PARITY-001`.
14. **Docs** — link this blueprint from `docs/development/README.md` and note the new capability in the
    accounting/ledger docs; refresh AI navigation if a new subsystem entrypoint is introduced.
15. **Validate** — `dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true`,
    targeted `dotnet test`, `npm --prefix src/Meridian.Ui/dashboard run test`, and `bash scripts/ci.sh`
    before opening a `codex/incentive-fee-mechanics` PR to `main`.

---

## 12. Open questions

- **O-1 (crystallization cash vs accrual-only):** On a crystallization date, does the fund physically pay the
  fee (Dr Payable / Cr Cash) or merely lock the accrued liability? Default here is accrual-only + HWM roll;
  cash settlement is a config/event choice. Confirm per-fund.
- **O-2 (hurdle-on-loss recovery interaction):** When both a hurdle and LCF are active, does the hurdle apply
  to profit measured above HWM, above HWM+LCF, or above contributed capital? The calculator nets LCF into
  `grossExcess`; PE preferred-return funds may want the hurdle on contributed capital instead.
- **O-3 (equalization method for `InvestorSeries`) — answered by a sibling blueprint:** Series-of-shares vs
  equalization-credit/contingent-redemption accounting for investors subscribing mid-period is designed in
  [equalization-and-series-accounting.md](equalization-and-series-accounting.md), which recommends **Method A**
  (equalisation credit/debit) as the default. Section 5.3's durable state supports both, but the mid-period
  subscription bookkeeping (equalisation credit journals) is that blueprint's slice, not this one's. The
  binding consequence for Fork G is recorded in the cross-blueprint contract above: Method A ⇒ `FundLevel`,
  Method B ⇒ `InvestorSeries`. What remains open here is only the per-fund *choice*, not the mechanics.
- **O-4 (effective-dated policy changes):** How are in-flight accruals treated when a policy's terms change
  mid-crystallization-window? Proposal: freeze the accrual window under the policy effective at the window
  start; new terms apply from the next window.
- **O-5 (management-fee crystallization coupling):** Should management fee accrual reuse the same
  crystallization calendar, or stay period-by-period as today? This blueprint leaves management fee unchanged.
- **O-6 (partial-period hurdle for redemptions):** For `OnRedemptionOnly`/`CrystallizeOnRedemption`, confirm
  the day-count basis (ACT/365 vs 30/360) used by `PeriodFractionSinceLastCrystallization`.
```
