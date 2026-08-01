# Equalization / Series Accounting for Open-End & Commingled Funds

**Status:** Partially implemented — the single-NAV `EqualizationCalculator` shipped; lot-level
Method A, Method B series accounting, persistence, and endpoints remain design-only
**Owner:** Ledger / FinancialOperations (fund operations)
**Reviewed:** 2026-08-01

**Scope:** Mid-period subscriptions into open-end / commingled vehicles carrying a performance
(incentive) fee with a high-water mark (HWM). Provides two industry-standard equalization
mechanics, a recommended default, the concrete domain model, ledger integration, persistence,
contracts, and a test plan.

## Delivery state (2026-08-01)

`src/Meridian.Ledger/EqualizationCalculator.cs` ships an **entry-exposure helper only** — not the
§5.1/§5.2 algorithm. `Compute(navPerUnit, highWaterNavPerUnit, subscriptionUnits,
performanceFeeRate)` returns an `EqualizationAdjustment(EqualisationCredit, ContingentRedemption)`
sized at the moment of subscription. Two limits matter before you reuse it:

- **It cannot crystallize.** There is no `GAV_cryst` parameter, so it can produce neither §5.1's
  bounded `creditReturned = f × Max(0, Min(GAV_cryst, GAV_d) − HWM) × shares` nor §5.2's
  recovery-to-date contingent redemption. Both are crystallization-time quantities.
- **Its NAV parameter is not §5.1's `GAV_d`.** §5.1 sizes the credit off the **gross** GAV before
  the accrued fee reverses into NAV; `navPerUnit` is the dealing NAV. In the §13.1 golden case
  (`GAV_d = 120`, `NAV_d = 116`, `HWM = 100`, `f = 0.20`, 100 shares) passing the documented dealing
  NAV returns `0.20 × (116 − 100) × 100 = 320`, where the required credit is `400`.

So **do not** treat the calculator as the per-unit math for the lot-level projection. Either call it
only for entry-time exposure and implement §5.1/§5.2 in the projector, or first extend it to take
the full crystallization inputs. The per-lot projector still owns scoping, zone classification, and
the fund-fee reconciliation invariant regardless.

Design-only: lot-level Method A projection (§5, §7.2), Method B series accounting (§6, §7.3),
persistence (§10), contracts and endpoints (§12).

> **Shared-convention notice.** This blueprint shares the ledger migration sequence, the
> `AutomatedJournalEventKind` / `ManualJournalEntryTypeDto` enums, and the fund high-water mark with
> the [incentive-fee](incentive-fee-mechanics.md) and
> [commitment & capital-call](commitment-and-capital-call-engine.md) blueprints. Migration ordinals,
> DDL precision, route prefixes, terminology, and the HWM-ownership contract are recorded in the
> canonical [blueprint register](../../engineering/blueprints/README.md#shared-conventions).
>
> **Spelling:** this document uses UK spelling ("equalisation", "crystallisation") in **prose only**.
> Every identifier it proposes — types, interfaces, DTOs, enum members, table and column names, route
> segments, test names — uses **US spelling**, matching the shipped `EqualizationCalculator` type and
> the sibling incentive-fee blueprint's `Crystallize*` surface.
>
> **Grandfathered exceptions (shipped, UK-spelled, do not rename in passing).** Three members
> already in source keep UK spelling until someone does a deliberate rename with its own migration
> and PR:
>
> | Member | Location |
> |---|---|
> | `EqualizationAdjustment.EqualisationCredit` | `src/Meridian.Ledger/EqualizationCalculator.cs` |
> | `EqualizationAdjustment.HasEqualisationCredit` | `src/Meridian.Ledger/EqualizationCalculator.cs` |
> | `ShareClassUnitRegisterProjector`'s `EqualisationCredit` field/parameters | `src/Meridian.Ledger/ShareClassUnitRegisterProjector.cs` |
>
> Note the split already in source: the *type* is `EqualizationAdjustment` (US) while its *members*
> are `EqualisationCredit` (UK). New code must not copy the member spelling — that split is the
> defect this convention exists to stop spreading, not a precedent.

---

## 1. Summary

Meridian today computes partnership incentive fees against a **single, fund-level high-water mark**
in `PartnershipInvestorAccountingProjector.Project(...)`
(`src/Meridian.Ledger/PartnershipInvestorAccountingProjector.cs`). That is correct when every
investor enters at the same NAV, but it is *unfair* the moment an investor subscribes mid-period at
a NAV different from the fund HWM: the single-HWM model either overcharges a late entrant (they pay
fee on gains earned before they invested) or undercharges them (they get a fee-free ride on a
recovery back to the HWM). This blueprint adds an **equalization layer** so performance-fee equity
is fair across investors who entered at different NAVs.

Two industry approaches are designed in full:

- **(A) Equalisation Credit / Debit (Contingent Redemption)** — keep a single published NAV per
  share and apply a per-subscriber *equalisation factor* that levels each subscriber onto the
  fund's fee footing at entry. An **equalisation credit** is collected when the subscriber enters
  *above* the peak (fee-paying zone); a **contingent redemption / equalisation debit** is levied
  when the subscriber enters *below* the HWM (loss-recovery zone).
- **(B) Series / Series-of-Shares accounting** — every subscription date opens a *new series* with
  its own issue price and its own HWM; the incentive fee is computed independently per series, and
  each series **consolidates (rolls up) into the lead series** once it has crystallised a fee and is
  therefore on the same fee footing.

**Recommended default: Method (A), Equalisation Credit/Debit**, exposed behind an
`EqualizationMethod` policy fork so a fund can opt into Method (B). Rationale (see §4): Meridian's
capital-account subledger and ledger book are keyed by investor/capital-account/currency and a
*single* fund NAV, not by series; Method (A) extends the existing single-HWM projector with additive
adjustment records rather than forking NAV strikes per subscription date, and preserves the single
dealing NAV that open-end/commingled investors, administrators, and pricing surfaces expect.

---

## 2. Grounding in current code (real references)

Every design decision below extends types that exist today. Exact signatures quoted.

### 2.1 The incentive-fee engine we are extending

`src/Meridian.Ledger/PartnershipInvestorAccountingProjector.cs` — the fee/HWM/allocation engine:

```csharp
public static PartnershipInvestorAllocationProjection Project(PartnershipInvestorAllocationInput input)
{
    ...
    var managementFee   = RoundCurrency(input.BeginningNav * input.ManagementFeeRate);
    var incentiveBase   = Math.Max(0m, input.EndingNavBeforeFees - input.HighWaterMark - managementFee);
    var performanceFee  = RoundCurrency(incentiveBase * input.PerformanceFeeRate);
    ...
    var updatedHighWaterMark = Math.Max(input.HighWaterMark, endingNavAfterFees);
    ...
}
```

Key facts this blueprint must respect:

- **The HWM is a single fund-level `decimal`.** `PartnershipInvestorAllocationInput.HighWaterMark`
  (`src/Meridian.Ledger/PartnershipInvestorAllocationInput.cs`) is one value per fund/period. There
  is no per-investor or per-series HWM today — that is exactly the gap equalization closes.
- **Investor allocation is a flat pro-rata split.** `BuildInvestorAllocations(...)` distributes
  `allocableProfitOrLoss` by `investor.AllocationPercent`, with the *last* investor taking the
  rounding residual (a "plug"):

  ```csharp
  var allocation = index == investors.Count - 1
      ? allocableProfitOrLoss - runningTotal
      : RoundCurrency(allocableProfitOrLoss * investor.AllocationPercent);
  ```

- **Investors** are `PartnershipInvestor(string investorId, string displayName, decimal allocationPercent)`
  (`src/Meridian.Ledger/PartnershipInvestor.cs`); percentages must sum to `1.000000` within
  `LedgerToleranceConstants.Allocation`.
- **Money is `decimal`, rounded through** `LedgerCurrencyRounding.RoundCurrency(decimal)` — 2 dp,
  `MidpointRounding.AwayFromZero` (`src/Meridian.Contracts/Ledger/LedgerCurrencyRounding.cs`).
- **Journal lines are `(LedgerAccount account, decimal debit, decimal credit)` tuples** and the
  projection self-checks `IsBalanced` (`TotalDebits == TotalCredits`) in
  `PartnershipInvestorAllocationProjection`.

### 2.2 Chart of accounts

`src/Meridian.Ledger/LedgerAccounts.cs` — scoped account factories we mirror for new accounts:

```csharp
public static LedgerAccount InvestorCapitalFor(string investorId) =>
    CreateScoped("Investor Capital", LedgerAccountType.Equity, investorId);

public static LedgerAccount PerformanceFeeExpenseFor(string fundId) =>
    CreateScoped("Performance Fee Expense", LedgerAccountType.Expense, fundId);

public static LedgerAccount PerformanceFeePayableFor(string fundId) =>
    CreateScoped("Performance Fee Payable", LedgerAccountType.Liability, fundId);

public static LedgerAccount RetainedEarningsFor(string fundId) =>
    CreateScoped("Retained Earnings", LedgerAccountType.Equity, fundId);
```

`CreateScoped(name, accountType, financialAccountId)` and `LedgerAccount(Name, AccountType, Symbol?,
FinancialAccountId?)` (`src/Meridian.Ledger/LedgerAccount.cs`; `FinancialAccountId` equality is
case-insensitive) are the extension seam for equalization/series accounts.

### 2.3 Governed-draft pattern

`src/Meridian.Ledger/AutomatedJournalDraft.cs`:

```csharp
public sealed record AutomatedJournalDraft(
    AutomatedJournalEvent Event,
    string Description,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)> Lines,
    JournalEntryMetadata Metadata)
{
    public decimal TotalDebits  => Lines.Sum(static line => line.debit);
    public decimal TotalCredits => Lines.Sum(static line => line.credit);
    public bool    IsBalanced   => TotalDebits == TotalCredits;
}
```

- `AutomatedJournalEvent(AutomatedJournalEventKind Kind, string Symbol, decimal Amount,
  DateTimeOffset Timestamp, ..., DateOnly? EffectiveDate, string? IdempotencyKey,
  IReadOnlyList<JournalEvidenceReference>? EvidenceReferences)`
  (`src/Meridian.Ledger/AutomatedJournalEvent.cs`).
- `AutomatedJournalEventKind` (`src/Meridian.Ledger/AutomatedJournalEventKind.cs`) already has
  `PerformanceFeeAccrued`; we add equalization kinds.
- `JournalEntryMetadata` (`src/Meridian.Ledger/JournalEntryMetadata.cs`) already carries
  `CapitalAccountId`, `InvestorId`, `FundEventId`, `FundEventType`, `IdempotencyKey`, `LedgerBook`,
  `Tags`, and `EvidenceReferences` — enough to stamp series/subscription identity without schema
  churn (series id rides in `Tags`, see §7).
- `LedgerLineDimensionSet` (`src/Meridian.Ledger/LedgerLineDimensionSet.cs`) has `FundId`,
  `InvestorId`, `CapitalAccountId`, `BookId`, and an `ExternalGlDimensions` bag — the per-line
  dimensional seam for a `SeriesId`.

### 2.4 The governed draft *service* (approval gate)

`src/Meridian.FinancialOperations/Ledger/AccountingJournalDraftService.cs`:

```csharp
public interface IAccountingJournalDraftService
{
    Task<AccountingJournalDraftResult> BuildDraftAsync(
        AccountingJournalDraftRequest request,
        CancellationToken ct = default);
}
```

`AccountingJournalDraftRequest` carries `Lines` (`AccountingJournalDraftLineRequest(LedgerAccount
Account, decimal Debit, decimal Credit, ...)`), `AccountingBasisKindDto`, `EffectiveDate`,
`LedgerBookId`, `LedgerPostingKindDto PostingKind`, `LedgerAdjustmentApprovalMetadataDto?
AdjustmentApproval`, and `EvidenceLinks`. `AccountingJournalDraftResult` returns validation issues,
`IsBalanced`, `CanSubmitForApproval`, and `CanPostWithoutAdditionalApproval`. **All equalization
postings flow through this seam** — never post directly.

### 2.5 Capital-account subledger

`src/Meridian.FinancialOperations/PrivateCapital/` (capital-account subledger — files):

- `PrivateCapitalCapitalAccountSubledgerBuilder.cs` — `Build(...)` keyed by
  `CapitalAccountSubledgerKey(string CapitalAccountId, string? InvestorId, string Currency)`,
  producing `PrivateCapitalCapitalAccountSubledgerDto` with a readiness ladder
  (`PrivateCapitalFundEventLedgerReadinessDto`: `Blocked → EvidenceMissing → ApprovalPending →
  PostingReview → ReportReview → Ready → Published`).
- `PrivateCapitalActivityProjectionBuilder.cs`, `PrivateCapitalCloseCockpitService.cs`,
  `PrivateCapitalEvidenceCategoryBuilder.cs`, `PrivateCapitalFundEventLedgerReadinessBuilder.cs`,
  `PrivateCapitalFundEventLedgerRecordBuilder.cs`, `PrivateCapitalPaymentIntentEvidenceBuilder.cs`.

**Critical:** the subledger key has **no series dimension** — it is
`CapitalAccountId | InvestorId | Currency`. This is a primary reason the *recommended default* is
Method (A): it fits the existing subledger grain without a schema fork. Method (B) requires a series
dimension, provided additively (§6, §8).

### 2.6 Fund structure / share-class / vehicle model

`src/Meridian.Contracts/FundStructure/FundStructureDtos.cs`:

- Node kinds `FundStructureNodeKindDto { Organization, Business, Client, Fund, Sleeve, Vehicle,
  InvestmentPortfolio, Entity, Account }` — **there is no first-class `ShareClass` or `Series`
  node.**
- `VehicleSummaryDto(Guid VehicleId, Guid FundId, Guid LegalEntityId, ...)` and
  `SleeveSummaryDto(Guid SleeveId, Guid FundId, ...)` are the closest existing grains.
- `LegalEntityFormDto` already includes `SeriesLimitedLiabilityCompany` and
  `SegregatedPortfolioCompany`, i.e. the legal shells series accounting typically rides on.

Design consequence: series are modeled as a **lightweight series registry keyed to a fund
(optionally a vehicle/sleeve)**, *not* a new node kind — the smallest safe change (Critical Rule 1).

### 2.7 Migration convention

`src/Meridian.Storage/Ledger/Migrations/` uses `V_ledger_###__snake_name.sql`. Latest on disk is
`V_ledger_028__wash_sale_activation.sql`. Scripts are **idempotent and replayed on every startup
(no version table)**: they use the `__SCHEMA__` placeholder, `create table if not exists`,
`add column if not exists`, `create index if not exists`, and `is null`-guarded backfills
(see `V_ledger_020__fund_scope_tenant_columns.sql`, `V_ledger_003__ledger_books.sql`). New scripts
here use this blueprint's reserved range **033–035**
([register](../../engineering/blueprints/README.md#ledger-migration-ordinals)); confirm the next
free ordinal at implementation time.

---

## 3. Concepts and notation

| Symbol | Meaning |
|---|---|
| `f` | Performance fee rate (e.g. `0.20m`). |
| `HWM` | Fund high-water-mark **per share** at the start of the current crystallisation period (the highest crystallised net NAV/share). |
| `GAV_d` | **Gross** NAV per share on dealing day `d` — before the current-period performance-fee accrual. |
| `NAV_d` | **Net** (published/dealing) NAV per share on day `d` = `GAV_d − accruedFeePerShare_d`. |
| `accruedFeePerShare_d` | `f × Max(0, GAV_d − HWM)` — the fee accrual embedded in the published NAV. |
| `shares` | Shares issued to a subscriber on day `d`. |
| Peak | Synonym for the per-share HWM used by the equalisation adjustment. |

All monetary results pass through `RoundCurrency`; per-share intermediates are held at full `decimal`
precision and rounded only when producing a posted amount, mirroring the existing projector.

---

## 4. Policy forks (options + RECOMMENDED default)

### 4.1 Fork 1 — Equalization method (the primary decision)

`EqualizationMethod` (fund/vehicle-scoped policy). This enum **already ships** in
`src/Meridian.Ledger/ShareClass.cs` with `None = 0` and `Equalisation = 1`; only `SeriesOfShares = 2`
is new (§7.1):

| Option | Behaviour | When to pick |
|---|---|---|
| **`Equalisation`** ✅ **RECOMMENDED DEFAULT** *(shipped member, ordinal 1; UK spelling is a grandfathered exception — §0)* | Single published NAV/share; per-subscriber equalisation credit (entry above peak) or contingent redemption / equalisation debit (entry below HWM). | Open-end / commingled vehicles with a single dealing NAV, frequent subscriptions, and a fund-level HWM — the exact shape Meridian's subledger and projector already model. |
| **`SeriesOfShares`** | Each subscription date opens a new series with its own HWM; series roll up into the lead series after crystallisation. | Funds contractually organised as series (common Cayman/BVI master-feeder and Series-LLC / SPC structures — see `LegalEntityFormDto.SeriesLimitedLiabilityCompany`, `SegregatedPortfolioCompany`). |
| **`None`** | Preserve today's single-HWM behaviour verbatim. | Funds where all investors enter at inception NAV, or no performance fee. Back-compatible no-op. |

**Recommendation: `Equalisation` (the shipped ordinal 1) as the platform default**, with `SeriesOfShares`
selectable per fund/vehicle. Rationale, grounded in code:

1. **Grain fit.** The capital-account subledger key is `CapitalAccountId | InvestorId | Currency`
   (§2.5) with no series axis; the ledger book is one-per-fund-structure-node
   (`V_ledger_003__ledger_books.sql`). Method (A) needs only additive per-investor adjustment rows
   on the existing grain; Method (B) forces a new series dimension through the subledger, dimensions,
   and reporting.
2. **Single NAV.** Open-end/commingled investors and downstream pricing expect *one* dealing NAV.
   Method (A) keeps it; Method (B) publishes a different NAV per series until roll-up.
3. **Minimal projector delta.** `PartnershipInvestorAccountingProjector` already produces a single
   fund `performanceFee` and a single `updatedHighWaterMark`. Method (A) layers a per-subscriber
   *reallocation* of that one fee number; Method (B) requires N independent fee computations and a
   conversion/cancellation event stream.
4. **Safest change (Critical Rule 1).** Method (A) is closer to what exists; Method (B) is offered
   for funds whose offering documents mandate series, behind the same policy switch.

### 4.2 Fork 2 — Equalisation-credit redemption style (Method A only)

| Option | Behaviour |
|---|---|
| **`ShareIssuance`** ✅ default | Return the equalisation credit at crystallisation by **issuing additional shares** to the subscriber (keeps a single NAV, no cash out). |
| `CashRebate` | Return the equalisation credit in cash. Simpler to explain, but a cash movement subject to liquidity/settlement. |

### 4.3 Fork 3 — Below-HWM entrant handling (Method A only)

| Option | Behaviour |
|---|---|
| **`ContingentRedemption`** ✅ default | New entrant below HWM has shares progressively redeemed to pay the manager the fee earned on the recovery to the HWM ("equalisation debit"). |
| `DepreciationDeposit` | Collect an upfront refundable deposit released as the fund recovers. Rare; supported as a config flag only if a fund's docs require it. |

### 4.4 Fork 4 — Crystallisation frequency

Reuse the existing `PeriodId` cadence (`PartnershipInvestorAllocationInput.PeriodId`). Equalisation
credits/debits and series roll-ups **crystallise on the same period boundary** as the fund
performance fee — do not introduce a second calendar.

### 4.5 Fork 5 — Rounding residual owner

Keep the existing convention: the **last** allocation row absorbs the rounding plug
(§2.1). Equalisation adjustments net to the fund performance fee **before** rounding is distributed,
so the manager's crystallised fee is invariant to the number of subscribers (asserted in tests, §11).

---

## 5. Method A — Equalisation Credit / Debit: exact per-subscriber math

### 5.1 Case 1 — subscription **above** the peak (`GAV_d > HWM`)

Existing holders carry an accrued performance fee embedded in the net NAV. A new subscriber buying at
`NAV_d` would be handed a windfall if the fund later fell back toward the entry level (the accrued
fee reverses into NAV). To equalise, the subscriber pays an **Equalisation Credit** equal to the
accrued fee per share at entry:

```
equalizationCreditPerShare = accruedFeePerShare_d       // see the policy note below
equalizationCredit          = equalizationCreditPerShare × shares
amountPaidBySubscriber      = shares × NAV_d + equalizationCredit
                            = shares × GAV_d              // economically the gross price
```

> **`accruedFeePerShare_d` is the *effective* accrued fee, not `f × (GAV_d − HWM)`.** The two
> coincide only under a plain HWM policy with no hurdle, no catch-up, and no loss carryforward. As
> soon as a fund enables any of the incentive-fee blueprint's forks, the flat expression overstates
> the credit: at `GAV_d = 120`, `HWM = 100`, `f = 20%` with an **8% hard hurdle**, the accrued fee
> per share is `0.20 × (120 − 108) = 2.40`, while `f × (GAV_d − HWM)` collects `4.00`. Method A
> would overcharge the subscriber by `1.60`/share and then **fail its own §11 reconciliation
> invariant**, because the redistributed total no longer equals the fund fee the projector computed.
>
> So the equalization projector must take the fund's **effective incentive-fee policy** as an input
> and obtain the accrued fee from `IncentiveFeeCalculator` (incentive-fee §5.1), not re-derive it:
> `EqualizationPeriodInput` carries the resolved policy, and both the entry credit and the
> crystallization-time `creditReturned` / contingent-redemption figures below are computed from the
> calculator's result for the relevant NAV level. `f × (GAV − HWM)` survives in this section only as
> the **no-hurdle special case**, and is written that way from here on.

**At the next crystallisation** (period end, fund still ≥ entry): the subscriber must not pay
performance fee on the `HWM → GAV_d` gain that predates their entry, so the manager **returns the
equalisation credit** — by issuing additional shares worth `equalizationCredit` at the
post-crystallisation NAV (`redemptionStyle = ShareIssuance`, default) or as cash
(`CashRebate`). If the fund is **below** entry at crystallisation, only the still-earned portion is
returned:

```
// NO-HURDLE SPECIAL CASE ONLY. The operative rule is the calculator's accrued fee at the bounded
// level, not this expression — see the policy note in §5.1.
creditReturned = accruedFeeAt(Min(GAV_cryst, GAV_d)) × shares          // bounded, ≤ equalizationCredit
             //  = f × Max(0, Min(GAV_cryst, GAV_d) − HWM) × shares    when no hurdle/catch-up applies
```

### 5.2 Case 2 — subscription **below** the HWM (`GAV_d < HWM`)

There is an unrecouped loss; existing holders pay no fee until the fund reclaims the `HWM`. A new
entrant would ride the recovery from `GAV_d` up to `HWM` fee-free — but that recovery is genuine
performance *for them*, on which the manager is owed a fee. Equalise with a **Contingent Redemption /
Equalisation Debit**. At each crystallisation:

```
// NO-HURDLE SPECIAL CASE ONLY, same as §5.1: size the fee from IncentiveFeeCalculator over the
// recovery range, not from the bare rate. An implementer using `f ×` under a hurdle or catch-up
// policy will disagree with the fund fee and break the §11 reconciliation invariant.
recoveryPerShare              = Max(0, Min(GAV_cryst, HWM) − GAV_d)
contingentRedemptionPerShare  = accruedFeeOver(recoveryPerShare)
                             // = f × recoveryPerShare    when no hurdle/catch-up applies
contingentRedemption          = contingentRedemptionPerShare × shares
```

`contingentRedemption` value is redeemed from the subscriber's shares and paid to the manager as the
equalisation performance fee. Once the fund exceeds `HWM`, the subscriber joins the normal fund-level
fee for gains beyond `HWM`.

### 5.3 Worked example (golden test vector)

`f = 0.20`, fund `HWM = 100.00`.

**Investor P** subscribes at inception at `NAV = 100.00`, 100 shares. Period 1 gross return +20% →
`GAV_1 = 120.00`, `accruedFeePerShare = 0.20 × (120 − 100) = 4.00`, `NAV_1 = 116.00`.

- **Case 1 subscriber Q** buys 100 shares mid-period at `NAV_1 = 116.00`:
  - `equalizationCredit = 0.20 × (120 − 100) × 100 = 400.00`; Q pays `116.00×100 + 400 = 12,000` (= `120×100`).
  - At period-end crystallisation (still `GAV = 120`): P crystallises fee `4.00×100 = 400`; Q's `400`
    credit is returned (share issuance). Post-fee: P net `11,600`, Q net `12,000`. Both entered at
    gross 120; both now pay fee only on **future** gains. ✅
- **Case 2 subscriber R** (alternate scenario: fund fell to `GAV = 90`, `HWM` still `100`,
  `NAV = 90`) buys 100 shares at `90`, pays `9,000`. Fund recovers to `GAV = 100` at crystallisation:
  - Existing holders pay `0` (only back to `HWM`). R's genuine gain `90 → 100` = `10/share`.
  - `contingentRedemption = 0.20 × (100 − 90) × 100 = 200`, redeemed and paid to manager.
  - R retains `100×100 − 200 = 9,800` — exactly as if charged 20% on the `1,000` gain. ✅

These two vectors become `EqualizationProjectorTests` golden cases (§11).

### 5.4 Ledger postings (Method A)

New scoped accounts in `LedgerAccounts` (mirroring §2.2):

```csharp
// Liability owed back to a mid-period subscriber (equalization credit collected at entry).
public static LedgerAccount EqualizationCreditPayableFor(string investorId) =>
    CreateScoped("Equalization Credit Payable", LedgerAccountType.Liability, investorId);

// Contra-equity capturing the crystallized equalization performance fee split.
public static LedgerAccount EqualizationReserveFor(string fundId) =>
    CreateScoped("Equalization Reserve", LedgerAccountType.Equity, fundId);
```

Reuse `PerformanceFeeExpenseFor` / `PerformanceFeePayableFor` / `InvestorCapitalFor`.

- **Entry, Case 1 (collect credit):** `Dr Cash [investor subscription]` / `Cr InvestorCapitalFor(Q)`
  (net NAV portion) and `Cr EqualizationCreditPayableFor(Q)` (the `400`).
- **Crystallisation, Case 1 (return credit, `ShareIssuance`):**
  `Dr EqualizationCreditPayableFor(Q)` / `Cr InvestorCapitalFor(Q)` for `creditReturned`; any
  unreturned remainder reverses to `PerformanceFeePayableFor(fund)`.
- **Crystallisation, Case 2 (contingent redemption):** `Dr InvestorCapitalFor(R)` /
  `Cr PerformanceFeePayableFor(fund)` for `contingentRedemption`.

Every posting set is emitted as balanced tuples and validated with `IsBalanced` before it reaches
`IAccountingJournalDraftService.BuildDraftAsync` (§8).

---

## 6. Method B — Series-of-shares: exact roll-up mechanics

### 6.1 Model

Each subscription date opens a **new series** issued at a fixed offering price `P0` (e.g.
`100.00`) with `HWM_s = P0`. Performance fee is computed **per series** using the *same* fee formula
as `PartnershipInvestorAccountingProjector`, with `BeginningNav/EndingNavBeforeFees` scaled to that
series' assets. Series with a lower/later HWM pay fee on their own gains only — equalisation is
automatic and exact.

> **Units: `HWM_s` is per-share; the projector is not.** `HWM_s` is stored per share
> (`incentive_fee_state.high_water_mark_per_share`, §9), but
> `PartnershipInvestorAccountingProjector.Project` computes
> `incentiveBase = Max(0, EndingNavBeforeFees − HighWaterMark − managementFee)` — subtracting
> `HighWaterMark` directly from a **total** NAV. Passing `HighWaterMark = HWM_s` therefore mixes
> units and overstates the fee. For a series of 100 shares worth `110` each with `HWM_s = 100` at a
> 20% fee, it yields `0.20 × (11,000 − 100) = 2,180` instead of the correct
> `0.20 × (11,000 − 10,000) = 200`.
>
> **Convert on both sides of the call**, using `units_s` = series units outstanding at period end
> (the same basis as the scaled `EndingNavBeforeFees`):
>
> ```
> HighWaterMark  = HWM_s × units_s          // scale in:  per-share -> series total
> ...Project(...)
> HWM_s'         = projection.UpdatedHighWaterMark / units_s   // scale out: total -> per-share
> ```
>
> The roll-forward persists `HWM_s'`, never the total-NAV candidate. Equivalently you may run the
> whole calculation per share and scale only the resulting fee by `units_s`; do not mix the two.
> If `units_s` changes mid-period (a redemption out of the series — series take no new
> subscriptions after issuance, §6.2), scale in with the **same** `units_s` used to scale out, or
> the crystallised HWM will not round-trip.
>
> **This applies to Method A too**, with `units_s` = fund units outstanding. The fund-level scope
> stores a per-share HWM for the same reason (§9): a total-NAV HWM would let a mid-period
> subscription look like gain.
>
> **`units_s = 0` — full redemption on a crystallization date.** `CrystallizeOnRedemption` funds can
> crystallize a scope that redeems out entirely, and the scale-out is undefined there. Do not divide:
> compute the fee on the units outstanding **immediately before** the redemption, write no per-share
> HWM, and close the scope (`CloseScopeAsync`, `Status = Closed`). A later series for the
> same investor is a new scope seeded at its own issue price, not a revival. Incentive-fee §5.4
> carries the same rule on the roller side.

### 6.2 Roll-up / consolidation

At a crystallisation date, a non-lead series `s` that has just **crystallised a performance fee**
(so its NAV is now net of a fully realised fee, i.e. on the same footing as the lead series `L`)
consolidates into `L`:

```
conversionRatio      = NAV_s_postFee / NAV_L_postFee
leadSharesIssued     = seriesShares_s × conversionRatio          // per holder
// value preserved:  seriesShares_s × NAV_s_postFee == leadSharesIssued × NAV_L_postFee
```

Series-`s` shares are cancelled; lead-series shares are issued; the holder now tracks the single lead
HWM. A series still **underwater** (no fee crystallised) stays separate until it crystallises. New
subscriptions in the next period open a fresh series again.

### 6.3 Worked example (golden test vector)

`f = 0.20`, `P0 = 100`.

- Inception: **Series 1 (lead)** @ `100`, `HWM_1 = 100`.
- Period 1 +20%: `GAV_1 = 120`; fee `0.20×(120−100) = 4`; `NAV_1 = 116`; `HWM_1 → 116`.
- Start Period 2: new subscriber Q → **Series 2** @ `100`, `HWM_2 = 100`.
- Period 2 +10% on both series' assets:
  - Series 1: `116 → 127.60`; fee `0.20×(127.60−116) = 2.32`; `NAV_1 = 125.28`; `HWM_1 → 125.28`.
  - Series 2: `100 → 110`; fee `0.20×(110−100) = 2.00`; `NAV_2 = 108.00`; `HWM_2 → 108.00`.
    (Series 1 paid fee on 10% only, Series 2 on its full 10% — fair.) ✅
- **Roll-up Series 2 → Series 1:** `conversionRatio = 108.00 / 125.28 = 0.862...`; Q's 100 Series-2
  shares → `86.20` Series-1 shares; value `86.20 × 125.28 ≈ 10,800 = 100 × 108`. ✅ Single HWM
  `125.28` afterwards.

### 6.4 Ledger postings (Method B)

New scoped accounts:

```csharp
// Per-series investor capital (Symbol carries the series id so per-series balances stay separate).
public static LedgerAccount SeriesCapitalFor(string seriesId, string investorId) =>
    new("Series Capital", LedgerAccountType.Equity, Symbol: seriesId, FinancialAccountId: investorId);
```

- **Per-series fee crystallisation:** identical to today's `PartnershipInvestorAccountingProjector`
  lines but scoped to the series' `SeriesCapitalFor(s, investor)` and the fund fee-payable accounts.
- **Consolidation:** `Dr SeriesCapitalFor(s, holder)` (cancel) / `Cr SeriesCapitalFor(L, holder)`
  (issue) at the conversion value — a value-preserving reclassification (`ManualJournalEntryTypeDto`
  reclass semantics; new `SeriesConsolidation` type in §7). Net zero; asserted balanced.

---

## 7. Domain model / new types

New engine types live beside the existing projector in **`Meridian.Ledger`** (sealed records,
`decimal` money, `DateOnly` for dealing/effective dates, constructor validation mirroring
`PartnershipInvestor` / `PartnershipInvestorAllocationInput`). Contract DTOs live in
**`Meridian.Contracts.Ledger`**.

### 7.1 Policy & enums

> **`EqualizationMethod` already ships — extend it, do not declare it.**
> `Meridian.Ledger.EqualizationMethod` exists in `src/Meridian.Ledger/ShareClass.cs` as
> `{ None = 0, Equalisation = 1 }`, is carried on `ShareClass.EqualizationMethod`, and gates the
> Method A path in `ShareClassUnitRegisterProjector` (`== EqualizationMethod.Equalisation`).
> Declaring a second enum of the same name in the same namespace will not compile.
>
> Ordinal `1` already means exactly this blueprint's `EqualizationCreditDebit`, so the change is a
> single **append** of `SeriesOfShares = 2` (append-only shared enum — see the
> [register](../../engineering/blueprints/README.md#enum-extension)). Member `1` keeps its shipped
> UK spelling `Equalisation` as a grandfathered exception (§0); do not rename it in passing, because
> `ShareClassUnitRegisterProjector` compares against it. A series-of-shares fund correctly falls
> outside that Method A branch.

```csharp
namespace Meridian.Ledger;

// EXISTING enum in ShareClass.cs — append SeriesOfShares only:
//   None = 0,
//   Equalisation = 1,     // == this blueprint's "equalisation credit / debit" method
//   SeriesOfShares = 2,   // <-- the one new member

// New enums below.
public enum EqualizationCreditRedemptionStyle { ShareIssuance = 0, CashRebate = 1 }
public enum BelowHwmEntryHandling             { ContingentRedemption = 0, DepreciationDeposit = 1 }

/// <summary>Which side of the peak a subscriber entered on.</summary>
public enum EqualizationZone { AtPeak = 0, AbovePeak = 1, BelowHighWaterMark = 2 }
```

New `AutomatedJournalEventKind` members (append to the existing enum, preserving ordinals):

```csharp
EqualizationCreditCollected,     // entry, Case 1
EqualizationCreditReturned,      // crystallization, Case 1
EqualizationDebitCrystallized,   // crystallization, Case 2 (contingent redemption)
SeriesFeeCrystallized,           // Method B per-series fee
SeriesConsolidationPosted,       // Method B roll-up
```

New `ManualJournalEntryTypeDto` members (append after `ClosingEntry = 15`, in
`src/Meridian.Contracts/Ledger/AccountingConfigurationDtos.cs`):

```csharp
EqualizationCredit = 16,
EqualizationDebit = 17,
SeriesConsolidation = 18,
```

### 7.2 Method A — subscription lots, input, adjustment, projection

```csharp
namespace Meridian.Ledger;

/// <summary>A single dated subscription tranche eligible for equalization.</summary>
public sealed record InvestorSubscriptionLot
{
    public InvestorSubscriptionLot(
        string investorId,
        string subscriptionId,
        DateOnly subscriptionDate,
        decimal shares,
        decimal grossNavPerShareAtEntry,     // GAV_d
        decimal netNavPerShareAtEntry,       // NAV_d
        decimal peakPerShareAtEntry,         // HWM at entry
        // This lot's OWN fee context: PeriodFraction runs from THIS subscription date to the
        // crystallization, and under a contributed-capital hurdle the basis is this lot's capital.
        // Required — the fund-level context on EqualizationPeriodInput cannot substitute (see the
        // note above the record).
        IncentiveFeeContext feeContext,
        string currency)
    {
        if (string.IsNullOrWhiteSpace(investorId))
            throw new ArgumentException("Investor identifier must not be null or whitespace.", nameof(investorId));
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("Subscription identifier must not be null or whitespace.", nameof(subscriptionId));
        if (shares <= 0m)
            throw new ArgumentOutOfRangeException(nameof(shares), shares, "Shares must be positive.");
        if (grossNavPerShareAtEntry < 0m || netNavPerShareAtEntry < 0m || peakPerShareAtEntry < 0m)
            throw new ArgumentOutOfRangeException(nameof(grossNavPerShareAtEntry), "Per-share values cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency must not be null or whitespace.", nameof(currency));
        ArgumentNullException.ThrowIfNull(feeContext);

        InvestorId = investorId.Trim();
        SubscriptionId = subscriptionId.Trim();
        SubscriptionDate = subscriptionDate;
        Shares = shares;
        GrossNavPerShareAtEntry = grossNavPerShareAtEntry;
        NetNavPerShareAtEntry = netNavPerShareAtEntry;
        PeakPerShareAtEntry = peakPerShareAtEntry;
        FeeContext = feeContext;
        Currency = currency.Trim();
    }

    public string InvestorId { get; }
    public string SubscriptionId { get; }
    public DateOnly SubscriptionDate { get; }
    public decimal Shares { get; }
    public decimal GrossNavPerShareAtEntry { get; }
    public decimal NetNavPerShareAtEntry { get; }
    public decimal PeakPerShareAtEntry { get; }
    public string Currency { get; }

    /// <summary>This lot's own dated fee context — see the note above the record. Never substitute
    /// the period-level FundContext: PeriodFraction runs from THIS SubscriptionDate.</summary>
    public IncentiveFeeContext FeeContext { get; }

    public EqualizationZone Zone =>
        GrossNavPerShareAtEntry > PeakPerShareAtEntry ? EqualizationZone.AbovePeak
        : NetNavPerShareAtEntry < PeakPerShareAtEntry ? EqualizationZone.BelowHighWaterMark
        : EqualizationZone.AtPeak;
}

/// <summary>Period-level equalization input; extends the partnership allocation input with lots.</summary>
public sealed record EqualizationPeriodInput(
    PartnershipInvestorAllocationInput Allocation,   // reuses existing fund-level fee/HWM inputs
    EqualizationMethod Method,
    // Fund-level context for the period total. NOT sufficient on its own for the lot math — see
    // the per-lot contexts on InvestorSubscriptionLot below.
    IncentiveFeeContext FundContext,
    decimal FundHighWaterMarkPerShare,               // HWM per share at period start
    decimal GrossNavPerShareEnd,                     // GAV at crystallization
    IReadOnlyList<InvestorSubscriptionLot> Lots,
    EqualizationCreditRedemptionStyle RedemptionStyle = EqualizationCreditRedemptionStyle.ShareIssuance,
    BelowHwmEntryHandling BelowHwmHandling = BelowHwmEntryHandling.ContingentRedemption);

/// <summary>
/// Per-subscriber equalization result for one crystallization.
/// NOTE the name: `Meridian.Ledger.EqualizationAdjustment` is already taken by the shipped
/// two-decimal entry-exposure record in EqualizationCalculator.cs, which
/// ShareClassUnitRegisterProjector consumes. This lot-level record is a different shape and must
/// not reuse that name — see the naming note under §7.2.
/// </summary>
public sealed record EqualizationLotAdjustment(
    InvestorSubscriptionLot Lot,
    EqualizationZone Zone,
    decimal EqualizationCreditCollected,     // Case 1, at entry
    decimal EqualizationCreditReturned,      // Case 1, at crystallization
    decimal ContingentRedemption,            // Case 2
    decimal NetEqualizationPerformanceFee,   // fee attributable to this lot after equalization
    LedgerAccount InvestorCapitalAccount,
    LedgerAccount EqualizationCreditAccount);

/// <summary>Balanced projection; mirrors PartnershipInvestorAllocationProjection self-check.</summary>
public sealed record EqualizationProjection(
    EqualizationPeriodInput Input,
    decimal FundPerformanceFee,                 // == PartnershipInvestorAllocationProjection.PerformanceFee
    decimal TotalEqualizationCreditReturned,
    decimal TotalContingentRedemption,
    string Description,
    IReadOnlyList<EqualizationLotAdjustment> Adjustments,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public decimal TotalDebits  => Lines.Sum(static line => line.debit);
    public decimal TotalCredits => Lines.Sum(static line => line.credit);
    public bool    IsBalanced   => TotalDebits == TotalCredits;
}
```

### 7.3 Method B — series definitions & consolidation

```csharp
namespace Meridian.Ledger;

public enum FundSeriesStatus { Open = 0, Crystallized = 1, Consolidated = 2, Closed = 3 }

public sealed record FundSeriesDefinition(
    string SeriesId,
    string FundId,
    string? VehicleId,
    DateOnly IssueDate,
    decimal IssuePrice,
    // Seed value only — at issuance HWM_s == IssuePrice. This is NOT the durable HWM: the owner is
    // incentive_fee_state (§9). Series creation must write that row; see the note below.
    decimal HighWaterMarkPerShare,
    bool IsLead,
    FundSeriesStatus Status,
    string Currency);

public sealed record SeriesHolding(
    string SeriesId,
    string InvestorId,
    decimal Shares);

public sealed record SeriesConsolidation(
    string FromSeriesId,
    string ToLeadSeriesId,
    DateOnly EffectiveDate,
    decimal ConversionRatio,                  // NAV_from_postFee / NAV_lead_postFee
    IReadOnlyList<(string InvestorId, decimal FromShares, decimal LeadSharesIssued)> Holders,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public bool IsBalanced => Lines.Sum(static l => l.debit) == Lines.Sum(static l => l.credit);
}

public sealed record SeriesCrystallizationProjection(
    IReadOnlyList<FundSeriesDefinition> SeriesAfter,
    IReadOnlyList<PartnershipInvestorAllocationProjection> PerSeriesFee,   // one per series, reusing the existing projector
    IReadOnlyList<SeriesConsolidation> Consolidations,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public bool IsBalanced => Lines.Sum(static l => l.debit) == Lines.Sum(static l => l.credit);
}
```

---

## 8. Interfaces

### 8.1 Projectors (pure, static — matching `PartnershipInvestorAccountingProjector`)

```csharp
namespace Meridian.Ledger;

public static class EqualizationProjector
{
    /// <summary>Method A: per-subscriber equalization credit/debit for one crystallization.</summary>
    public static EqualizationProjection Project(EqualizationPeriodInput input);
}

public static class SeriesAccountingProjector
{
    /// <summary>
    /// Method B: per-series fee + roll-up into the lead series for one crystallization.
    ///
    /// Takes a per-series IncentiveFeeContext, NOT bare PartnershipInvestorAllocationInput values.
    /// This projector is pure: it cannot derive hurdle, catch-up, contributed-capital basis, reset
    /// mode, or period fraction from allocation inputs, so an interface carrying only those forces
    /// every implementation back onto the flat-rate legacy calculation — producing wrong fees and
    /// wrong HWMs for any series on such a policy, which is exactly what §9 forbids.
    /// </summary>
    public static SeriesCrystallizationProjection Crystallize(
        IReadOnlyList<FundSeriesDefinition> series,
        IReadOnlyList<SeriesHolding> holdings,
        IReadOnlyList<SeriesFeeInput> perSeriesInputs,      // context + prior state, per series
        DateOnly effectiveDate);
}

/// <summary>One series' complete policy-aware fee inputs for a crystallization.</summary>
public sealed record SeriesFeeInput(
    string SeriesId,
    IncentiveFeeContext Context,                // hurdle, catch-up, reset mode, period fraction
    IncentiveFeeStateRecord PriorState,         // that series' live HWM/LCF scope
    // TWO counts, matching IncentiveFeeSeriesInput (incentive-fee §6.2). §6.1 prices a
    // full-redemption fee on the POSITIVE pre-redemption units, then reads the post-redemption
    // count to decide closure. Passing 0 makes the fee basis unusable; passing the prior units
    // leaves Crystallize unable to tell a full close from a partial redemption.
    decimal UnitsOutstandingBeforeRedemption,   // scale-in/scale-out basis (§6.1); > 0
    decimal UnitsOutstandingAfterRedemption);   // 0 ⇒ full close: scope goes Closed, no divide-back

```

### 8.2 Application service (governed orchestration)

```csharp
namespace Meridian.FinancialOperations.Ledger;

public interface IEqualizationProjectionService
{
    /// <summary>Dispatches on EqualizationMethod, builds governed drafts via IAccountingJournalDraftService.</summary>
    Task<EqualizationDraftResult> BuildEqualizationDraftsAsync(
        EqualizationDraftRequest request,
        CancellationToken ct = default);
}

public sealed record EqualizationDraftRequest(
    Guid LedgerBookId,
    Guid PeriodId,
    string FundProfileId,
    EqualizationMethod Method,
    EqualizationPeriodInput? EqualizationInput,             // Method A
    SeriesCrystallizationRequest? SeriesInput,             // Method B
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null,
    IReadOnlyList<string>? EvidenceLinks = null);

public sealed record EqualizationDraftResult(
    IReadOnlyList<AccountingJournalDraftResult> Drafts,     // one per governed posting set
    bool AllBalanced,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);
```

### 8.3 Series registry persistence port

```csharp
namespace Meridian.Storage.Ledger;

public interface IFundSeriesStore
{
    Task<IReadOnlyList<FundSeriesDefinition>> ListAsync(string fundProfileId, CancellationToken ct = default);
    Task<FundSeriesDefinition> UpsertAsync(FundSeriesDefinition series, CancellationToken ct = default);
    Task RecordConsolidationAsync(SeriesConsolidation consolidation, CancellationToken ct = default);
    Task<IReadOnlyList<SeriesHolding>> ListHoldingsAsync(string seriesId, CancellationToken ct = default);
}
```

> **Opening a series must also open its HWM row — `IFundSeriesStore` alone is not enough.**
> Because `fund_series` carries no HWM column (§9), persisting a `FundSeriesDefinition` does *not*
> persist the series' protected level. If the workflow stops at `UpsertAsync`, the issue-price HWM
> exists only on the in-memory record and is gone after reload — the next crystallization then
> hydrates no HWM and can charge performance fee on the **entire** series NAV rather than on gains
> above the issue price.
>
> Two separate store calls **cannot** give that guarantee — a crash between them leaves exactly the
> corrupt state described above — so this is a **single adapter operation owning one database
> transaction**, not a sequence the caller composes:
>
> ```csharp
> // incentive-fee §6.1 — canonical signature, quoted in full. Both rows commit or neither does,
> // and the append-only enablement-evidence row is written in the SAME transaction, which is why
> // the operator's basis note and identity are parameters rather than an afterthought.
> Task<IncentiveFeeStateRecord> CreateSeriesWithStateAsync(
>     FundSeriesDefinition series, IncentiveFeeStateRecord seedState,
>     string openingBasisNote, string acknowledgedBy, CancellationToken ct = default);
> ```
>
> The seed state is `series_id = series.SeriesId`,
> `high_water_mark_per_share = series.IssuePrice`, `status = Live`, `loss_carryforward = 0`,
> `accrued_fee_balance = 0`, `last_crystallized_date = null`.
>
> Two symmetric operations complete the lifecycle, for the same reason:
>
> - **Consolidation** (§6.2) → `ConsolidateSeriesAsync(ledgerBookId, consolidation, reclassification, …)`
>   does the whole thing in one transaction: durably appends the **approved §11 reclassification
>   journal**, sets the absorbed `fund_series` row to `Consolidated`, cancels its holdings and issues
>   the lead-series equivalents, writes the consolidation-history row, and sets the absorbed scope to
>   `Consolidated` — each under its concurrency guard. Splitting the journal out to a separate draft
>   service (as an earlier draft did) lets a rejection or crash leave the journal, holdings, registry,
>   and HWM scope describing four different ownerships.
> - **Full redemption on a crystallization date** → `CloseScopeAsync(stateRecordId, seriesId, …)`
>   closes the HWM scope **and** the `fund_series` registry row in one transaction — it takes the
>   series key for exactly that reason. Closing them separately would leave an `Open` series with no
>   live HWM. See the zero-units rule in incentive-fee §5.4: the fee is computed on
>   pre-redemption units and no per-share HWM is written, because the divide-back is undefined at
>   zero units.
>
> **Loading** a series for fee evaluation joins `incentive_fee_state` to obtain `HWM_s` — the value
> on `FundSeriesDefinition` is a creation-time seed and must never be read as current state.

---

## 9. How it interacts with the incentive-fee engine (per-series vs per-investor HWM)

**The engine is not replaced; it is layered.**

- **Method A (per-investor equalisation, single fund HWM).** Obtain the single fund
  `PerformanceFee` and `UpdatedHighWaterMark` from the **policy-aware** path —
  `IncentiveFeeCalculator.Compute` with the period's `IncentiveFeeContext`, then
  `IncentiveFeeStateRoller.Roll` — **not** from `Project(input.Allocation)` alone.

  > **Both halves must come from the same calculation.** `Project(input.Allocation)` is the legacy
  > path: it takes a bare performance-fee rate and a HWM, so under a hurdle or catch-up it returns a
  > different fund total than the calculator that now sizes the per-lot credits and debits (§5.1).
  > Mixing them means the fund total uses the flat rate while the lot adjustments use the effective
  > accrued fee — the §11 invariant
  > `Σ NetEqualizationPerformanceFee (+ credits returned − debits) == FundPerformanceFee` then fails
  > by construction, or the wrong total gets posted. Method B carries the same rule (§9 below); this
  > is one rule for both methods, not a Method B special case.

  Then `EqualizationProjector.Project`
  *reallocates* that fee across subscription lots via the §5 formulas: each lot's
  `NetEqualizationPerformanceFee` is the fund fee attributable to that lot after removing pre-entry
  gains (credit) or adding post-entry recovery fee (debit). The **invariant**
  `Σ NetEqualizationPerformanceFee (+ credits returned − debits) == FundPerformanceFee` is asserted
  (§11) so the manager's crystallised fee is unchanged by equalisation — only its *distribution*
  across investors changes. The fund HWM stays a single value; there is **no per-investor HWM state**
  to persist.

- **Method B (per-series HWM).** Each series is an independent fee context: call
  `PartnershipInvestorAccountingProjector.Project` **once per series** with
  `HighWaterMark = HWM_s × units_s` — the projector works in total-NAV terms, so the per-share
  `HWM_s` must be scaled in and the returned candidate scaled back out (§6.1). This means
  **per-series HWM is real persisted state** — one `incentive_fee_state` row per series, keyed by
  `series_id` (incentive-fee blueprint §7.2).

  **Do not advance the series HWM through the existing `updatedHighWaterMark = Math.Max(...)` line.**
  That line sits on the legacy projector path, which takes only a bare performance-fee rate and a
  HWM — it cannot apply a hurdle, a catch-up, or a loss carryforward. A series on any such policy
  would get a different fee *and* a different protected level from what the incentive-fee blueprint
  specifies, while both documents require the two slices to land together. Each series runs the
  **policy-aware** path instead: `IncentiveFeeCalculator.Compute` with that series' own context,
  then `IncentiveFeeStateRoller.Roll` to advance its scope. Roll-up then collapses consolidated
  series onto the lead series' HWM row.

The choice therefore determines the *scope* of the HWM, not its home: Method A keeps a single
fund-scoped row, Method B one row per series. Both live in the same table. This is the core reason
Method A is the lighter, recommended default.

> **Cross-blueprint contract — HWM ownership (recorded 2026-08-01, revised after review).** The
> [incentive-fee blueprint](incentive-fee-mechanics.md) exposes the same choice as its **Fork G**
> (`FundLevel` vs `InvestorSeries`), and its `incentive_fee_state` table (§7.2 there) is the
> **single durable owner of the HWM under both methods**. This section is the authority on the
> *scope* of a row, not on a second store:
>
> | Equalization method | Incentive-fee Fork G | HWM row in `incentive_fee_state` |
> |---|---|---|
> | Method A (default) | `FundLevel` (default) | One row per book, `series_id is null`. No per-investor rows. |
> | Method B | `InvestorSeries` | One row per series, `series_id` set. |
>
> **The HWM is stored per share in both methods** (`high_water_mark_per_share`) — which is what this
> blueprint's glossary (§3) has always meant by `HWM`. A total-NAV HWM is not invariant to capital
> flows: a subscription raises ending NAV without being gain, so the projector would charge fee on
> contributed capital, and Method A's equalisation step cannot correct it because that step
> preserves the projector's total fund fee and only redistributes it. Every projector call therefore
> scales in (`× unitsOutstanding`) and scales back out (`÷ unitsOutstanding`) — §6.1.
>
> **This blueprint's `fund_series` table therefore does not carry a HWM column.** An earlier draft
> proposed `fund_series.high_water_mark_per_share`; it is removed (§10.3) because it would compete
> with `incentive_fee_state` — crystallization could advance one while the next fee calculation read
> the other. `PartnershipInvestorAllocationInput.HighWaterMark` is likewise **not** an owner: it is a
> transient `sealed record` projector input, hydrated from `incentive_fee_state` per period.
>
> If a fund adopts Method B, this blueprint's series accounting and the incentive-fee blueprint's
> `InvestorSeries` fork must land as one slice. Both documents record this contract; the canonical
> copy is the [blueprint register](../../engineering/blueprints/README.md#cross-blueprint-contracts).

---

## 10. Persistence & migrations

Follow the `V_ledger_###__name.sql` convention (§2.7): `__SCHEMA__` placeholder, idempotent,
replay-safe, `is null`-guarded backfills. The highest ordinal on disk is
`V_ledger_028__wash_sale_activation.sql`; this blueprint's reserved range is **033–035**
([register](../../engineering/blueprints/README.md#ledger-migration-ordinals)). Verify at
implementation time and update the register if another lane lands first.

### 10.1 `V_ledger_033__equalization_policy.sql`

```sql
-- Equalization policy per fund structure node (method + Method-A styling forks). Inert until the
-- projection service reads it; a null/absent row means EqualizationMethod=None (today's behaviour).
create table if not exists __SCHEMA__.fund_equalization_policy (
    ledger_book_id            uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    fund_profile_id           text not null,
    equalization_method       text not null default 'None',        -- None | Equalisation | SeriesOfShares  (matches the shipped enum)
    credit_redemption_style   text not null default 'ShareIssuance',
    below_hwm_handling        text not null default 'ContingentRedemption',
    tenant_id                 text null,
    created_at                timestamptz not null,
    updated_at                timestamptz not null,
    primary key (ledger_book_id)
);

create index if not exists ix_fund_equalization_policy_fund
    on __SCHEMA__.fund_equalization_policy (lower(trim(fund_profile_id)));
```

### 10.2 `V_ledger_034__equalization_subscription_lots.sql`

```sql
-- Method A: dated subscription tranches and their per-crystallization equalization adjustments.
create table if not exists __SCHEMA__.equalization_subscription_lots (
    subscription_id                text not null,
    ledger_book_id                 uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    investor_id                    text not null,
    capital_account_id             text null,
    subscription_date              date not null,
    shares                         numeric(38, 12) not null,
    gross_nav_per_share_at_entry   numeric(38, 12) not null,
    net_nav_per_share_at_entry     numeric(38, 12) not null,
    peak_per_share_at_entry        numeric(38, 12) not null,
    currency                       text not null,
    zone                           text not null,               -- AtPeak | AbovePeak | BelowHighWaterMark
    tenant_id                      text null,
    created_at                     timestamptz not null,
    primary key (ledger_book_id, subscription_id)
);

create index if not exists ix_equalization_lots_investor
    on __SCHEMA__.equalization_subscription_lots (ledger_book_id, lower(trim(investor_id)));

create table if not exists __SCHEMA__.equalization_adjustments (
    ledger_book_id                 uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    period_id                      uuid not null,
    subscription_id                text not null,
    equalization_credit_collected  numeric(38, 12) not null default 0,
    equalization_credit_returned   numeric(38, 12) not null default 0,
    contingent_redemption          numeric(38, 12) not null default 0,
    net_equalization_perf_fee      numeric(38, 12) not null default 0,
    journal_entry_id               uuid null,
    tenant_id                      text null,
    created_at                     timestamptz not null,
    primary key (ledger_book_id, period_id, subscription_id)
);
```

### 10.3 `V_ledger_035__fund_series.sql`

```sql
-- Method B: series registry, holdings, and consolidation history.
create table if not exists __SCHEMA__.fund_series (
    series_id                 text not null,
    ledger_book_id            uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    fund_profile_id           text not null,
    vehicle_id                text null,
    issue_date                date not null,
    issue_price               numeric(38, 12) not null,
    -- No HWM column here by contract (§9): the series HWM lives in incentive_fee_state,
    -- one row per series keyed by series_id. Join, do not duplicate.
    is_lead                   boolean not null default false,
    status                    text not null default 'Open',      -- Open | Crystallized | Consolidated | Closed
    currency                  text not null,
    tenant_id                 text null,
    created_at                timestamptz not null,
    updated_at                timestamptz not null,
    primary key (ledger_book_id, series_id),
    -- The lead index below is partial on status. Without this domain constraint any other value
    -- ('open', a typo from a manual repair) falls outside the predicate, silently excluding the
    -- existing lead and letting a second lead be inserted for the same book.
    constraint ck_fund_series_status
        check (status in ('Open', 'Crystallized', 'Consolidated', 'Closed'))
);

-- Partial on BOTH is_lead and a live status. Without the status predicate a lead series that
-- fully redeems keeps the constraint slot (close sets status, not is_lead), so the next
-- subscription cannot establish a new lead without a uniqueness violation.
create unique index if not exists ux_fund_series_lead
    on __SCHEMA__.fund_series (ledger_book_id)
    where is_lead and status in ('Open', 'Crystallized');

create table if not exists __SCHEMA__.fund_series_holdings (
    ledger_book_id  uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    series_id       text not null,
    investor_id     text not null,
    shares          numeric(38, 12) not null,
    primary key (ledger_book_id, series_id, investor_id)
);

create table if not exists __SCHEMA__.fund_series_consolidations (
    ledger_book_id    uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    from_series_id    text not null,
    to_lead_series_id text not null,
    effective_date    date not null,
    conversion_ratio  numeric(38, 12) not null,
    journal_entry_id  uuid null,
    tenant_id         text null,
    created_at        timestamptz not null,
    primary key (ledger_book_id, from_series_id, effective_date)
);
```

**Rounding/precision note:** every **numeric amount, rate, share-count, and per-share** column above
uses the ledger convention `numeric(38, 12)`
([register](../../engineering/blueprints/README.md#ddl-precision)). This applies to the numeric
columns only — the same tables also carry `uuid`, `text`, `date`, `boolean`, and `timestamptz`
columns, which keep their natural types. Storage precision is *not* the rounding policy: posted
monetary amounts are still rounded in C# to the 2-dp `RoundCurrency` policy. Never post an
unrounded per-share figure.

---

## 11. Projector + governed-draft integration

Flow (Method A shown; Method B is analogous with per-series inputs and a consolidation posting set):

```
EqualizationPeriodInput
  -> PartnershipInvestorAccountingProjector.Project(input.Allocation)   // single fund fee + HWM (unchanged)
  -> EqualizationProjector.Project(input)                              // reallocate fee -> EqualizationProjection (balanced tuples)
  -> IEqualizationProjectionService.BuildEqualizationDraftsAsync(...)
       for each posting set:
         new AccountingJournalDraftRequest(
             AggregateId:        <fund/investor aggregate>,
             PeriodId:           request.PeriodId,
             AccountingTimestamp:DateTimeOffset.UtcNow,      // deterministic clock injected in tests
             Description:        adjustment.Description,
             Lines:              adjustment.Lines.Select(l => new AccountingJournalDraftLineRequest(
                                     l.account, l.debit, l.credit,
                                     Dimensions: new LedgerDimensionSetDto{ InvestorId=..., FundId=..., /* SeriesId via ExternalGlDimensions */ })),
             EffectiveDate:      crystallizationDate,
             LedgerBookId:       request.LedgerBookId,
             PostingKind:        LedgerPostingKindDto.Originating,
             AdjustmentApproval: request.AdjustmentApproval,
             EvidenceLinks:      request.EvidenceLinks)
  -> IAccountingJournalDraftService.BuildDraftAsync(request, ct)        // policy/rule + approval gate
       -> AccountingJournalDraftResult { IsBalanced, CanSubmitForApproval, CanPostWithoutAdditionalApproval, ValidationIssues }
```

Rules:

- **Never bypass** `IAccountingJournalDraftService`; equalisation postings are adjustments subject to
  the same approval gate as manual entries (`docs/ai/assistant-workflow-contract.md` HITL gates).
- **Determinism:** the projector is pure and static (like `PartnershipInvestorAccountingProjector`);
  timestamps/GUIDs are injected by the service, not read inside the projector, so golden tests are
  reproducible.
- **Idempotency:** set `AutomatedJournalEvent.IdempotencyKey` /
  `JournalEntryMetadata.IdempotencyKey` to `"equalization:{ledgerBookId}:{periodId}:{subscriptionId}"`
  (Method A) or `"series-consol:{ledgerBookId}:{fromSeriesId}:{effectiveDate}"` (Method B), matching
  the journal idempotency guards from `V_ledger_013__journal_idempotency_guards.sql`.
- **Series identity for Method B** rides in `JournalEntryMetadata.Tags["seriesId"]` and
  `LedgerLineDimensionSet.ExternalGlDimensions["seriesId"]` — no new metadata column required.
- **Capital-account subledger:** Method A rows carry `CapitalAccountId`/`InvestorId` and appear in
  `PrivateCapitalCapitalAccountSubledgerBuilder.Build(...)` on the existing key with no schema
  change. Method B additionally surfaces per-series balances via the `seriesId` dimension (subledger
  grouping stays investor-level; series is a drill-down facet, see §12).

---

## 12. Contracts / DTOs, endpoints, UI surfaces

### 12.1 DTOs (`Meridian.Contracts.Ledger`)

> **Do not type these DTOs with the `Meridian.Ledger` enums.** `Meridian.Contracts` has **no**
> `ProjectReference` at all — it is a leaf, and the graph runs `Meridian.Ledger` →
> `Meridian.Core` → `Meridian.Contracts`. Referencing `Meridian.Ledger.EqualizationMethod` (or
> `EqualizationCreditRedemptionStyle` / `BelowHwmEntryHandling`) from a Contracts DTO would need a
> Contracts→Ledger reference and invert that graph. Serializing the shipped enum would also put its
> grandfathered UK member `Equalisation` on the wire.
>
> So Contracts owns **its own** wire enums with US-spelled values, and the application service maps
> at the boundary: `EqualizationMethodDto { None, EqualizationCreditDebit, SeriesOfShares }`,
> `EqualizationCreditRedemptionStyleDto`, `BelowHwmEntryHandlingDto`. The mapping is the one place
> `Equalisation` ↔ `EqualizationCreditDebit` is translated, which is also what keeps the
> grandfathered spelling out of the public contract.

- `EqualizationPolicyDto(Guid LedgerBookId, string FundProfileId, EqualizationMethodDto Method,
  EqualizationCreditRedemptionStyleDto RedemptionStyle, BelowHwmEntryHandlingDto BelowHwmHandling)`.
- `EqualizationLotAdjustmentDto(string InvestorId, string SubscriptionId, DateOnly SubscriptionDate,
  string Zone, decimal EqualizationCreditCollected, decimal EqualizationCreditReturned,
  decimal ContingentRedemption, decimal NetEqualizationPerformanceFee, string Currency)`.
- `FundSeriesDto(string SeriesId, DateOnly IssueDate, decimal IssuePrice,
  decimal HighWaterMarkPerShare, bool IsLead, string Status, string Currency)` — note
  `HighWaterMarkPerShare` is a **joined read** from `incentive_fee_state` (§9 HWM contract), not a
  column on `fund_series`.
- `SeriesConsolidationDto(string FromSeriesId, string ToLeadSeriesId, DateOnly EffectiveDate,
  decimal ConversionRatio, IReadOnlyList<SeriesConsolidationHolderDto> Holders)`.
- `EqualizationCrystallizationViewDto(Guid LedgerBookId, Guid PeriodId, EqualizationMethodDto Method,
  decimal FundPerformanceFee, IReadOnlyList<EqualizationLotAdjustmentDto> Adjustments,
  IReadOnlyList<SeriesConsolidationDto> Consolidations, bool AllBalanced,
  IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues)`.

Use `[JsonConverter(typeof(JsonStringEnumConverter<...>))]` on every new enum, matching the existing
DTO conventions in `AccountingConfigurationDtos.cs` and `FundStructureDtos.cs`, and register in the
source-generated JSON context (Critical Quality Guardrail: respect source-generated JSON).

### 12.2 Endpoints (`Meridian.Contracts/Api/UiApiRoutes.cs` + `Meridian.Application` handlers)

Routes use the existing **`/api/ledger/...`** prefix — `UiApiRoutes` has no `/api/accounting/`
prefix, and these sit beside the incentive-fee blueprint's `/api/ledger/incentive-fee/...` surface
that shares their crystallization boundary
([register](../../engineering/blueprints/README.md#api-route-prefixes)). Route segments use US
spelling, matching the shipped `EqualizationCalculator`.

- `GET  /api/ledger/equalization/policy/{ledgerBookId}` → `EqualizationPolicyDto`.
- `PUT  /api/ledger/equalization/policy/{ledgerBookId}` (governed; approval-gated).
- `POST /api/ledger/equalization/crystallize/preview` → `EqualizationCrystallizationViewDto`
  (dry-run: builds drafts, does not post; sets `DryRunCorrelationId` on the draft request).
- `POST /api/ledger/equalization/crystallize/submit` → submits governed drafts for approval.
- `GET  /api/ledger/equalization/series/{ledgerBookId}` → `IReadOnlyList<FundSeriesDto>`.

### 12.3 UI surfaces

Stays inside the approved top-level nav (`Trading, Portfolio, Accounting, Reporting, Strategy, Data,
Settings` — CLAUDE.md). All under **Accounting**:

- **Equalisation Crystallisation Cockpit** — preview table of per-subscriber adjustments (zone,
  credit collected/returned, contingent redemption, net fee), a "fee reconciles to fund fee" badge,
  and a submit-for-approval action. Model it on `PrivateCapitalCloseCockpitService` patterns and the
  readiness ladder.
- **Series Roll-up panel** (Method B funds only) — series list with per-series HWM/NAV, and a
  consolidation preview (conversion ratios, shares issued) before posting.
- Browser lane: `src/Meridian.Ui/dashboard/`; shared read models in `src/Meridian.Ui.Services/` /
  `src/Meridian.Ui.Shared/`. WPF parity is a follow-on under `W8-WPF-PARITY-001`; ship the shared
  read model + endpoint first so both clients consume one seam.

---

## 13. Test plan (xUnit + FluentAssertions)

Mirror the existing ledger projector tests (`tests/Meridian.Tests`, e.g. the
`PartnershipInvestorAccountingProjector` suite). Deterministic, no clock/GUID reads inside
projectors.

### 13.1 `EqualizationProjectorTests` (Method A)

- **`Project_AbovePeakSubscriber_CollectsAccruedFeeAsEqualizationCredit`** — the §5.3 Q vector:
  `equalizationCredit.Should().Be(400.00m)`; amount paid `== 120 × shares`.
- **`Project_AbovePeakSubscriber_ReturnsCreditAtCrystallization_WhenStillAbove`** — full `400`
  returned; net worth reconciles.
- **`Project_AbovePeakSubscriber_ReturnsPartialCredit_WhenFundFellBelowEntry`** — bounded
  `creditReturned` equals the calculator's accrued fee at `Min(GAV_cryst, GAV_d)` × shares. The
  golden numbers below assume the **no-hurdle** policy, where that reduces to
  `f × Max(0, Min(GAV_cryst, GAV_d) − HWM) × shares`.
- **`Project_WithHardHurdle_CreditMatchesCalculatorNotFlatRate`** — the same vector with an 8% hard
  hurdle produces the calculator's `2.40`/share, not `4.00`, and §11 still reconciles. This is the
  regression that the flat expressions above would silently reintroduce.
- **`Project_BelowHwmSubscriber_LeviesContingentRedemption_OnRecoveryToHwm`** — the §5.3 R vector:
  `contingentRedemption.Should().Be(200.00m)`; retained value `9,800`.
- **`Project_BelowHwmSubscriber_NoRedemption_WhenNoRecovery`** — recovery `≤ 0` ⇒ `0`.
- **`Project_AtPeakSubscriber_ProducesNoEqualization`** — zone `AtPeak`, all adjustments `0`.
- **`Project_SumOfLotFees_EqualsFundPerformanceFee`** — the invariant in §9 across a randomised set
  of lots (property-style with a fixed seed): manager fee is subscriber-count invariant.
- **`Project_AllPostingSets_AreBalanced`** — every `EqualizationProjection.IsBalanced` true;
  `TotalDebits == TotalCredits`.
- **`Project_RoundingResidual_AbsorbedByLastAllocation`** — matches the existing plug convention;
  totals reconcile to the penny.
- **`Project_MethodNone_IsBehaviourPreservingNoOp`** — output identical to
  `PartnershipInvestorAccountingProjector` alone (regression guard).
- **Validation:** negative shares / negative NAV / blank ids throw `ArgumentException` /
  `ArgumentOutOfRangeException` (mirror `InvestorSubscriptionLot` ctor).

### 13.2 `SeriesAccountingProjectorTests` (Method B)

- **`Crystallize_PerSeriesFee_UsesOwnHighWaterMark`** — §6.3 vectors: Series 1 fee `2.32`, Series 2
  fee `2.00`.
- **`Crystallize_RollUp_ConversionRatioPreservesValue`** — ratio `108.00/125.28`; issued lead shares
  ≈ `86.20`; value preserved within `RoundCurrency` tolerance.
- **`Crystallize_UnderwaterSeries_NotConsolidated`** — a series with no crystallised fee stays
  `Open`.
- **`Crystallize_ConsolidationPostings_NetToZero`** — `SeriesConsolidation.IsBalanced` true.
- **`Crystallize_LeadSeriesUniqueness`** — exactly one `IsLead` series after roll-up (matches
  `ux_fund_series_lead`).

### 13.3 Service / integration

- **`EqualizationProjectionServiceTests`** — dispatch on `EqualizationMethod`; each posting set flows
  through a faked `IAccountingJournalDraftService` and returns `AllBalanced == true`;
  `IdempotencyKey` stable across re-runs (no duplicate drafts).
- **`EqualizationPersistenceTests`** — round-trip `IFundSeriesStore`; migration replay is idempotent
  (apply `V_ledger_033..035` twice, assert no error / no dup rows).
- **`EqualizationSubledgerProjectionTests`** — Method A adjustments appear in
  `PrivateCapitalCapitalAccountSubledgerBuilder.Build(...)` on the existing
  `CapitalAccountId|InvestorId|Currency` key without schema change.

### 13.4 Cross-cutting assertions

- Money equality via exact `decimal` comparisons after `RoundCurrency`; per-share comparisons use
  `.BeApproximately(expected, 1e-8m)`.
- Cancellation honoured on every `async` service method (`ct` threaded through) — Quality Guardrail.
- Structured logging only; no string interpolation inside log calls — Quality Guardrail.

Run targeted: `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
--filter FullyQualifiedName~Equalization` (or the GitHub `Targeted Test` workflow,
`mode=dotnet-filtered`, per CLAUDE.md).

---

## 14. Implementation checklist (ordered, code-ready)

1. **Enums & event kinds** — append `SeriesOfShares = 2` to the **existing**
   `EqualizationMethod` (do not redeclare it, §7.1); add `EqualizationCreditRedemptionStyle`,
   `BelowHwmEntryHandling`, `EqualizationZone`, `FundSeriesStatus` in `Meridian.Ledger`; append
   `EqualizationCreditCollected/Returned`, `EqualizationDebitCrystallized`, `SeriesFeeCrystallized`,
   `SeriesConsolidationPosted` to `AutomatedJournalEventKind`; append `EqualizationCredit=16`,
   `EqualizationDebit=17`, `SeriesConsolidation=18` to `ManualJournalEntryTypeDto`.
2. **Chart of accounts** — add `EqualizationCreditPayableFor`, `EqualizationReserveFor`,
   `SeriesCapitalFor` to `LedgerAccounts.cs` (mirror `CreateScoped` / per-symbol patterns).
3. **Domain records** — `InvestorSubscriptionLot`, `EqualizationPeriodInput`,
   `EqualizationLotAdjustment`, `EqualizationProjection`; `FundSeriesDefinition`, `SeriesHolding`,
   `SeriesConsolidation`, `SeriesCrystallizationProjection` (constructor validation + `IsBalanced`).
4. **Projectors** — implement `EqualizationProjector.Project` (§5 math) and
   `SeriesAccountingProjector.Crystallize` (§6 math), reusing
   `PartnershipInvestorAccountingProjector.Project` and `RoundCurrency`. Pure/static/deterministic.
5. **Unit tests first-class** — author §13.1/§13.2 golden vectors against the projectors *before*
   wiring persistence (they need no DB).
6. **Persistence** — add `V_ledger_033__equalization_policy.sql`,
   `V_ledger_034__equalization_subscription_lots.sql`, `V_ledger_035__fund_series.sql` (idempotent,
   `__SCHEMA__`); implement `IFundSeriesStore` + a Postgres adapter in `Meridian.Storage.Ledger`.
   **In the same step**, implement the three transactional ports from incentive-fee §6.1 —
   `CreateSeriesWithStateAsync` (registry row + seeded `incentive_fee_state` row in one
   transaction), `ConsolidateSeriesAsync`, and `CloseScopeAsync` — and hydrate
   `HWM_s` from `incentive_fee_state` on load (§7.3 note). Shipping `IFundSeriesStore` on its own
   leaves every new series without a durable HWM.
7. **Application service** — `IEqualizationProjectionService` /
   `EqualizationProjectionService` in `Meridian.FinancialOperations.Ledger`, dispatching on method
   and routing every posting set through `IAccountingJournalDraftService.BuildDraftAsync` with
   idempotency keys and evidence links; register in DI.
8. **Contracts/DTOs** — add the §12.1 DTOs to `Meridian.Contracts.Ledger`, wire into the
   source-generated JSON context.
9. **Endpoints** — add routes to `UiApiRoutes.cs` and handlers in `Meridian.Application/Http`;
   preview (dry-run) before submit (approval-gated).
10. **Subledger surfacing** — confirm Method A rows flow through
    `PrivateCapitalCapitalAccountSubledgerBuilder`; add the `seriesId` drill-down facet for Method B.
11. **UI** — shared read model in `Meridian.Ui.Services`/`Meridian.Ui.Shared`; Equalisation
    Crystallisation Cockpit + Series Roll-up panel under **Accounting** in
    `src/Meridian.Ui/dashboard/`. WPF parity follows.
12. **Service/integration tests** — §13.3.
13. **Docs** — link this blueprint from `docs/development/accounting-blueprints/` index and the
    accounting docs front door; note the `EqualizationMethod` policy fork in operator docs.
14. **Validation** — `bash scripts/ci.sh` locally; targeted `dotnet test ...~Equalization`; then the
    authoritative GitHub `Meridian CI / quality-gate`.

---

## 15. Open questions

1. **Series as a first-class node?** This design keeps series in a lightweight registry keyed to the
   ledger book (not a new `FundStructureNodeKindDto`). If the roadmap wants series to appear in the
   fund-structure graph and ownership links, promoting `Series` to a node kind is a larger,
   separate change — confirm before committing.
2. **NAV source of truth.** `EqualizationProjector` needs `GrossNavPerShareEnd` and per-share
   entry NAVs. Which subsystem is authoritative for per-share NAV strikes (pricing/valuation vs. the
   ledger)? The blueprint treats them as inputs; the producer must be pinned.
3. **Multi-currency lots.** `InvestorSubscriptionLot.Currency` and the subledger key include
   currency. Do equalisation credits/debits ever cross currencies (e.g. hedged share classes), and
   if so, where does FX translation post (reuse `UnrealizedFxGain/Loss` accounts)?
4. **Partial redemptions between crystallisations.** How is an un-returned equalisation credit or an
   outstanding contingent redemption handled if the subscriber redeems mid-period? (Proposed:
   settle pro-rata at redemption; needs an offering-document rule per fund.)
5. **`AllocationPercent` vs. share-based ownership.** `PartnershipInvestor.AllocationPercent` is a
   flat percentage; equalisation is inherently share/lot based. Confirm whether allocation percent is
   derived from shares at each period (recommended) or maintained independently.
6. **Depreciation-deposit variant demand.** Is `BelowHwmEntryHandling.DepreciationDeposit` actually
   required by any target fund, or can we ship `ContingentRedemption` only and defer the deposit
   path?
7. **Reporting disclosure.** Do investor statements need to *disclose* the equalisation
   credit/debit line explicitly (common LP expectation) or only the net capital-account movement?
   Affects the `Reporting` surface and report-pack templates.
```
