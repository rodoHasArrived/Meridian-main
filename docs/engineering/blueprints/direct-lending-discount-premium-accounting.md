# Direct Lending: Loan Discount and Premium Accounting

**Status:** Draft for owner review. Nothing in this document is implemented. Seven decisions are open; see [Open questions](#open-questions).
**Owner:** Direct Lending + Ledger
**Reviewed:** 2026-11-16
**Registered in:** [`docs/engineering/blueprints/README.md`](README.md)

> **Depth:** full blueprint · **Source:** the 2026-11-16 accounting review (PR #3612), which found the
> amortization posting dormant and booked the wrong way round for a lender.

## Summary

A lender that funds or buys a loan for less than par holds an unamortized discount; one that pays
more than par holds a premium. Both are part of the loan's carrying value and are recognised in
income over the loan's life.

Meridian has the fields and one posting for this, but no way to create the balance, so the posting
never does anything. This blueprint adds:

1. **origination** of a discount or premium when a drawdown is funded at a price other than par;
2. **daily amortization** inside the accrual run, posted to lender-side accounts;
3. **release** of the unamortized balance on prepayment, payoff, write-off and restructuring
   haircut;
4. **replay and tie-out**, so the balance survives a rebuild and reconciles to the ledger;
5. **carrying value** on the loan read models.

There is no breaking change. Every contract change is an optional member, and the four existing
ledger roles stay registered.

## Current state (verified against `main` `0c8c5766e`)

| Concern | Today | Evidence |
|---|---|---|
| Balance fields | `UnamortizedDiscount` and `UnamortizedPremium` exist on servicing state, default `0m` | `LoanServicingStateDto` in `src/Meridian.Contracts/DirectLending/DirectLendingDtos.cs` |
| Origination | Nothing sets either field above zero; every other write copies or reduces it | `PostgresDirectLendingCommandService.cs:1434-1435`, `InMemoryDirectLendingService.cs:812-813`, F# `ServicingAggregate.fs` (copy only) |
| Drawdown journal | Dr `LoanPrincipal` / Cr `Cash` for the same amount | `DirectLendingJournalLines.Events.cs` (`loan.drawdown-booked`) |
| Purchase price | `DirectLendingTermsDto.PurchasePrice` exists and is hashed, but no direct-lending code reads it | grep of `src/Meridian.Application/DirectLending`, `src/Meridian.FSharp*` |
| Amortization command | `AmortizeDiscountPremiumAsync`, straight-line over remaining days to maturity, C# only | `PostgresDirectLendingCommandService.cs:1415-1463`; not in the F# aggregate |
| Amortization trigger | Manual API endpoint only; `DailyAccrualWorker` does not call it; no UI | `DirectLendingEndpoints.cs:948-963` |
| Amortization journal | Dr `LoanDiscountAmortizationExpense` / Cr `LoanDiscount`; Dr `LoanPremium` / Cr `LoanPremiumAmortizationIncome` | `DirectLendingJournalLines.Events.cs:164-172` |
| Account types | `LoanDiscount` is an Asset, `LoanPremium` a Liability, with a note that changing it needs a governed rebook | `StandardChartOfAccounts.cs:50-54` |
| Replay | `DirectLendingEventRebuilder` has no case for `loan.discount-premium-amortized` (nor for write-off or restructuring) | event `switch` in `DirectLendingEventRebuilder.cs` |
| Write-off | Reduces principal only; discount and premium untouched | `ServicingAggregate.applyWriteOff` |
| Tests | One endpoint test; `DirectLendingOutboxDispatcherTests.cs:186-187` pins the current journal direction | `tests/Meridian.Tests` |

Because amounts are always zero and the journal builder drops zero lines, no ledger should hold a
posting to any of the four discount/premium accounts. That is an inference from the code, not a
query of any ledger; [phase 0](#phase-0-confirm-the-starting-point) checks it.

## Scope

**In scope:** lender-side discount and premium on direct loans, from origination to extinguishment,
in the Postgres (F#) and in-memory (C#) implementations, with journals, replay, tie-out and read
models.

**Out of scope:**

- Loan impairment, allowance and non-accrual status. Meridian has none today; this design only
  ensures a write-off releases the related discount or premium.
- Deferred origination fees received in cash and capitalised origination costs. They amortize the
  same way and can reuse these accounts later.
- Borrower-side debt issue costs (see the financing-liabilities blueprint).
- Fixed-income security lots, which already amortize through `FixedIncomeAmortizationProjector`.
- Multi-currency loans. Configured-book loan posting is USD-only today.

**Assumptions:** one base currency per loan; the F# aggregate and the C# in-memory service must
produce identical results, as the existing blueprints require.

**Depth mode:** full.

## Architectural overview

```text
BookDrawdownRequest (FundingPricePercent?)          DailyAccrualWorker
        |                                                   |
        v                                                   v
ServicingAggregate.bookDrawdown            ServicingAggregate.postDailyAccrual
  discount/premium = par - funded            + amortizeDiscountPremium (same day)
        |                                                   |
        +--------------------+------------------------------+
                             v
        loan events (payload carries discount/premium amounts)
                             |
        +--------------------+--------------------+
        v                    v                    v
DirectLendingJournalLines  DirectLendingEventRebuilder  read models
(shared by command service, (replays the balance)       (carrying value)
 outbox dispatcher and
 LoanAccountingProjector)
        |
        v
 ILedgerJournalStore  <----  DirectLendingSubledgerTieOut (ledger = servicing state)
```

### Design decisions

- **Decision:** originate at drawdown, from a funding price.
  **Alternatives considered:** a standalone "record discount" command; deriving it from
  `terms.PurchasePrice` only.
  **Rationale:** the discount is the difference between par booked and cash paid, and the drawdown
  is where both are known and journaled together. A separate command could leave principal and
  cash disagreeing with the discount.
  **Consequences:** `terms.PurchasePrice` becomes the default price for a loan's drawdowns
  (question 1); a per-drawdown price overrides it.

- **Decision:** add new ledger roles; leave the four existing roles registered and unused.
  **Alternatives considered:** retype `LoanPremium` from Liability to Asset and reuse the old
  roles.
  **Rationale:** the chart comment says changing the premium identity needs a governed rebook.
  New roles avoid that and keep any historical balance readable.
  **Consequences:** four legacy roles remain in `LedgerAccountRole`. Retire them in a later change
  once phase 0 confirms they were never posted.

- **Decision:** carry the discount as a credit-balance (contra) Asset account and the premium as
  a debit-balance Asset account, both scoped to the loan's financial account.
  **Alternatives considered:** net them into `LoanPrincipal`.
  **Rationale:** `LoanPrincipal` must keep tying to contractual principal for servicing and
  statements. Separate accounts give carrying value as principal less discount plus premium.
  **Consequences:** an Asset account with a credit balance shows negative on a trial balance. That
  is correct for a contra account, and report builders that total assets already net it.

- **Decision:** keep straight-line amortization for the first release, run by the daily accrual.
  **Alternatives considered:** the effective-interest method.
  **Rationale:** straight-line is the existing formula
  (`DirectLending.calculateStraightLineAmortization`), is deterministic to replay, and is
  self-correcting because each day divides the remaining balance by the remaining days.
  Effective-interest needs expected cash flows per loan and is a separate piece of work.
  **Consequences:** income timing differs from effective-interest, more so for long-dated or
  deeply discounted loans. Question 2 asks whether that is acceptable.

- **Decision:** move amortization into the F# aggregate and the daily accrual, and keep the manual
  endpoint as an idempotent catch-up.
  **Alternatives considered:** leave it as a C#-only manual command.
  **Rationale:** a manual step is easy to miss, and today's command amortizes again if called
  twice for one date.
  **Consequences:** servicing state gains `LastDiscountPremiumAmortizationDate`.

- **Decision:** release unamortized balance pro rata to principal extinguished.
  **Alternatives considered:** keep amortizing the full balance to original maturity.
  **Rationale:** a balance with no principal behind it is not an asset. On a prepayment the
  released amount is income (discount) or expense (premium); on a write-off or haircut it adjusts
  the loss.
  **Consequences:** principal-reducing events carry the released amounts in their payloads.

## Interface and API contracts

### Modified contracts (all additive)

```csharp
// src/Meridian.Contracts/DirectLending/DirectLendingDtos.cs
public sealed record BookDrawdownRequest(
    decimal Amount,                 // par amount added to principal, unchanged
    DateOnly TradeDate,
    DateOnly SettleDate,
    string? ExternalRef)
{
    /// <summary>
    /// Cash funded as a percentage of <see cref="Amount"/>. Null uses the terms' PurchasePrice,
    /// else 100. Below 100 creates a discount; above 100 a premium.
    /// </summary>
    public decimal? FundingPricePercent { get; init; }
}

public sealed record DrawdownLotDto(/* existing members */)
{
    public decimal FundedAmount { get; init; }        // defaults to the par amount
}

public sealed record LoanServicingStateDto(/* existing members, incl. UnamortizedDiscount/Premium */)
{
    public DateOnly? LastDiscountPremiumAmortizationDate { get; init; }
    /// <summary>Principal outstanding less unamortized discount plus unamortized premium.</summary>
    public decimal CarryingValue => Balances.PrincipalOutstanding - UnamortizedDiscount + UnamortizedPremium;
}
```

`DirectLendingTermsDto` is not changed, so no terms hash moves. `PurchasePrice` is already a hashed
member.

### Event payloads (additive members; absent means zero)

| Event | New members |
|---|---|
| `loan.drawdown-booked` | `fundedAmount`, `discountAmount`, `premiumAmount` |
| `loan.daily-accrual-posted` | `discountAmortization`, `premiumAmortization` |
| `loan.discount-premium-amortized` (manual catch-up) | unchanged names; now replayed |
| `loan.principal-payment-applied`, `loan.mixed-payment-applied` | `discountReleased`, `premiumReleased` |
| `loan.write-off-applied`, `loan.restructured` | `discountReleased`, `premiumReleased` |

### New ledger roles

```csharp
// src/Meridian.Ledger/LedgerAccountRole.cs
LoanUnamortizedDiscount,          // Asset, contra (credit balance)
LoanUnamortizedPremium,           // Asset
LoanDiscountAccretionIncome,      // Revenue
LoanPremiumAmortizationExpense,   // Expense

// src/Meridian.Ledger/StandardChartOfAccounts.cs
[LedgerAccountRole.LoanUnamortizedDiscount] = Loan("UnamortizedLoanDiscount", LedgerAccountType.Asset),
[LedgerAccountRole.LoanUnamortizedPremium] = Loan("UnamortizedLoanPremium", LedgerAccountType.Asset),
[LedgerAccountRole.LoanDiscountAccretionIncome] = Loan("DiscountAccretionIncome", LedgerAccountType.Revenue),
[LedgerAccountRole.LoanPremiumAmortizationExpense] = Loan("PremiumAmortizationExpense", LedgerAccountType.Expense),
```

### F# aggregate functions

```fsharp
// src/Meridian.FSharp.DirectLending.Aggregates/ServicingAggregate.fs
/// Splits a par drawdown into funded cash and a discount or premium.
val priceDrawdown : par: decimal -> fundingPricePercent: decimal -> DrawdownPricing
// { FundedAmount; DiscountAmount; PremiumAmount }, rounded to the currency's minor unit

/// One day's straight-line amortization. No-op when the date is not after the last amortized date.
val amortizeDiscountPremium :
    servicing: LoanServicingStateDto -> maturity: DateOnly -> date: DateOnly -> DiscountPremiumAmortization

/// Share of each unamortized balance released when principal falls by `principalReduced`.
val releaseOnPrincipalReduction :
    servicing: LoanServicingStateDto -> principalBefore: decimal -> principalReduced: decimal -> DiscountPremiumRelease
```

`InMemoryDirectLendingService` mirrors all three in C#.

### REST surface

No new routes. `POST …/drawdowns` accepts the optional `fundingPricePercent`. The existing
amortize route keeps its shape and becomes idempotent per accrual date.

## Component design

### Journal mapping — `DirectLendingJournalLines.Events.cs`

One shared projection serves the command service, the outbox dispatcher and
`LoanAccountingProjector`, so the mapping changes in one place.

| Event | Journal |
|---|---|
| Drawdown at a discount | Dr `LoanPrincipal` par · Cr `Cash` funded · Cr `LoanUnamortizedDiscount` difference |
| Drawdown at a premium | Dr `LoanPrincipal` par · Dr `LoanUnamortizedPremium` difference · Cr `Cash` funded |
| Amortization | Dr `LoanUnamortizedDiscount` / Cr `LoanDiscountAccretionIncome`; Dr `LoanPremiumAmortizationExpense` / Cr `LoanUnamortizedPremium` |
| Prepayment or payoff release | same accounts as amortization, for the released amounts |
| Write-off or haircut | Dr `LoanWriteOffExpense` (or `LoanRestructuringLoss`) for principal less discount released plus premium released · Dr `LoanUnamortizedDiscount` released · Cr `LoanUnamortizedPremium` released · Cr `LoanPrincipal` principal |

The existing negative-amount and balanced-journal guards stay as they are and fail closed.

### `ServicingAggregate` (F#) and `InMemoryDirectLendingService` (C#)

- **`bookDrawdown`** validates `0 < FundingPricePercent`, with an upper bound from question 3,
  prices the drawdown, adds to the unamortized balance, and records `FundedAmount` on the lot.
- **`postDailyAccrual`** calls `amortizeDiscountPremium` for the accrual date after interest, and
  includes the amounts in the accrual event so one journal covers the day.
- **Principal-reducing functions** (`applyPrincipalPayment`, mixed payment, `applyWriteOff`,
  restructuring haircut) call `releaseOnPrincipalReduction`. When principal reaches zero the whole
  remaining balance is released.
- **Maturity:** on the maturity date, amortization takes the full remaining balance, which the
  existing formula already yields when one day remains.
- **Rounding:** each amount is rounded to the currency minor unit; the final release or maturity
  amount takes the residual so the balance ends at exactly zero.

**Concurrency and idempotency:** unchanged. Commands run under the aggregate version check, and the
command idempotency store (migration 007) already covers retries.

### `DirectLendingEventRebuilder`

Add replay of the new payload members on drawdown, accrual and principal-reducing events, and a
case for `loan.discount-premium-amortized`. A rebuilt loan must equal the stored one field for
field.

### `PostgresDirectLendingStateStore` — migration 012

Add `unamortized_discount`, `unamortized_premium` (numeric, default 0) and
`last_discount_premium_amortization_date` (date, null) where balances are stored, plus
`funded_amount` on drawdown lots defaulting to the lot amount. Every statement must be re-runnable,
because this migration set runs under `DriftPolicy.Reapply`. Confirm first how the two existing
unamortized fields are persisted today; a search of `src/Meridian.Storage` found no column for
them.

### Read models

`LoanPortfolioSummaryBuilder` and the loan detail read model expose unamortized discount, premium
and carrying value. Fair-value or NAV consumers that read principal as the loan's value are listed
under question 6.

## Data flow

### Fund a drawdown at 98 (happy path)

1. Operator books a 1,000,000 drawdown with `fundingPricePercent = 98`.
2. `bookDrawdown` prices it: funded 980,000, discount 20,000.
3. State: principal +1,000,000; `UnamortizedDiscount` +20,000; lot records `FundedAmount`.
4. Event `loan.drawdown-booked` carries `fundedAmount` and `discountAmount`.
5. Journal: Dr `LoanPrincipal` 1,000,000 · Cr `Cash` 980,000 · Cr `LoanUnamortizedDiscount` 20,000.
6. Each accrual day: Dr `LoanUnamortizedDiscount` / Cr `LoanDiscountAccretionIncome` for the
   remaining balance divided by remaining days.

### Prepay 40% of principal

1. Payment applied; principal falls by 40%.
2. `releaseOnPrincipalReduction` releases 40% of the unamortized discount.
3. One journal carries the cash and principal lines plus the release to accretion income.

### Error path

A price that would produce a negative amount, or a journal that does not balance, raises
`DirectLendingCommandException` (`Validation`) before anything is saved, as today.

## UI design

No new screens. The loan detail in the browser workstation and in WPF gains three read-only figures
(unamortized discount, unamortized premium, carrying value), and the drawdown form gains an
optional funding price. Both clients read the same shared read model, per the parity rule.

## Test plan

**Principle:** every new function is tested in F# and through the C# in-memory service with the
same cases, so the two implementations cannot drift.

| Test | Verifies |
|---|---|
| `PriceDrawdown_BelowPar_CreatesDiscount` / `_AbovePar_CreatesPremium` / `_AtPar_CreatesNeither` | Funded amount and balances, with rounding |
| `BookDrawdown_NoPrice_UsesTermsPurchasePriceThenPar` | Default order |
| `DrawdownJournal_AtDiscount_Balances` and `_AtPremium_Balances` | Three-line journals; cash equals funded amount |
| `DailyAccrual_AmortizesStraightLine_AndIsIdempotentPerDate` | Daily amount; a second run for the same date posts nothing |
| `Amortization_ReachesZeroExactlyAtMaturity` | Residual handling over a full term |
| `Prepayment_ReleasesProRata` / `Payoff_ReleasesAll` | Release amounts and income lines |
| `WriteOff_NetsDiscountAgainstLoss` / `RestructuringHaircut_ReleasesProRata` | Loss equals carrying amount written off |
| `Rebuild_ReproducesUnamortizedBalances` | Replay parity for every event above |
| `SubledgerTieOut_UnamortizedAccountsMatchServicingState` | Extends `DirectLendingSubledgerTieOutTests` |
| `LegacyAmortizationEvent_ZeroAmounts_PostsNothing` | Old events replay without journals |
| `TermsHash_Unchanged` | Existing characterization tests stay green |
| Postgres integration: migration 012 applies twice; state round-trips | Runs in `integration-gate` |

`DirectLendingOutboxDispatcherTests.cs:186-187` currently pins the old direction and is rewritten.

## Implementation checklist

**Estimated effort:** medium to large, about 2 to 3 weeks for one developer.
**Suggested PR sequence:** one PR per phase; each leaves the system working.

### Phase 0: confirm the starting point

- [ ] Query each ledger for postings to `LoanDiscount`, `LoanPremium`,
      `DiscountAmortizationExpense`, `PremiumAmortizationIncome`. Expect none.
- [ ] Confirm how `UnamortizedDiscount` and `UnamortizedPremium` are persisted today.
- [ ] Resolve open questions 1 to 4.

### Phase 1: accounts and journal mapping

- [ ] Add the four roles and chart entries.
- [ ] Rewrite the amortization case in `DirectLendingJournalLines.Events.cs`; add the drawdown and
      release lines.
- [ ] Rewrite the dispatcher test; add journal tests.

### Phase 2: origination

- [ ] `FundingPricePercent`, `FundedAmount`, `priceDrawdown` in F# and C#.
- [ ] Migration 012; state store read and write.
- [ ] Replay of the drawdown members.

### Phase 3: amortization in the accrual run

- [ ] `amortizeDiscountPremium` in F# and C#; call from `postDailyAccrual`.
- [ ] Make the manual command delegate to it; add the last-amortized date.
- [ ] Replay of accrual members and of `loan.discount-premium-amortized`.

### Phase 4: release on extinguishment

- [ ] `releaseOnPrincipalReduction`; wire into payment, payoff, write-off and haircut.
- [ ] Replay for write-off and restructuring, which are not replayed today.

### Phase 5: read models, tie-out and UI

- [ ] Carrying value on summary and detail read models; tie-out extension.
- [ ] Browser and WPF loan detail figures; drawdown form field.

### Phase 6: wrap-up

- [ ] Module READMEs, reviewed source hashes, generated docs.
- [ ] Decide when to retire the four legacy roles.

## Open questions

| # | Question | Impact if unresolved |
|---|---|---|
| 1 | What does `terms.PurchasePrice` mean today: percent of par, or an amount? | Sets the default funding price; a wrong reading misprices every drawdown |
| 2 | Is straight-line acceptable for reporting, or is effective-interest required from the start? | Changes phase 3 from days to weeks and needs expected cash flows |
| 3 | What price range should be accepted (for example 50 to 150), and should a deep discount need approval? | Validation and whether a human gate applies |
| 4 | On a write-off, should the released discount reduce the loss (as designed) or go to income? | Journal mapping in phase 4 |
| 5 | Should delayed-draw facilities track discount per drawdown lot, not per loan? | Per-lot tracking changes state shape and release maths |
| 6 | Which NAV, fair-value or report consumers should switch from principal to carrying value? | Reported asset values after phase 5 |
| 7 | Should upfront fees received in cash share these accounts later? | Naming of the roles now |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| F# and C# implementations drift | Medium | High | Shared test cases run against both |
| A ledger does hold legacy postings | Low | Medium | Phase 0 query; if found, plan a governed rebook before phase 1 |
| Replay gaps hide balance errors | Medium | High | Rebuild parity test over every event |
| Contra-asset shows as negative on a report that assumes positive assets | Medium | Medium | Review statement and export builders in phase 5 |
| Straight-line misstates income timing on long, deeply discounted loans | Medium | Medium | Question 2; effective-interest as a follow-up |
