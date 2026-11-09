# Direct Lending: Term-Driven Excess Payment Application

**Status:** Approved design (owner decisions recorded 2026-11-02). Implementation proceeds by [increment](#increments); increments 1 (terms rule), 2 (balances and migration 010), 3 (excess routing), 4 (accrual consumption), 5 (split journals) and 7 (apply/refund commands, backend and API) are implemented; increment 6 (read models and UI) is in review.
**Owner:** Direct Lending + Ledger
**Reviewed:** 2026-11-02
**Registered in:** [`docs/engineering/blueprints/README.md`](README.md)

> **Backlog anchor:** `PRD-115` in [`docs/product/implementation-todo-list.md`](../../product/implementation-todo-list.md)
> **Depth:** full blueprint · **Source:** the 2026-11-02 audit follow-up (#3373 capped manual payment lines at the amount owed)

## Summary

When a borrower pays more than is currently owed, the excess should be applied according to the
loan's terms. Depending on the facility, it is held as unapplied cash, applied to principal as a
prepayment, or carried as prepaid interest that later accruals consume. Today the excess is only
reported in `MixedPaymentResolutionDto.UnappliedAmount`, and nothing tracks, journals or reconciles
it.

This blueprint adds:

1. a versioned **excess-payment rule** on the loan terms, added without re-keying existing terms
   versions;
2. two tracked **balances**, `PrepaidInterest` and `UnappliedCash`, persisted and replayed;
3. **routing** of the post-waterfall excess by that rule in both resolver implementations;
4. **accrual consumption** of prepaid interest;
5. **split journals** for mixed payments, so each allocation (and the excess) lands in its own
   account instead of one clearing account.

## Current state (verified against `main` d75b6de4)

| Concern | Today | Evidence |
|---|---|---|
| Excess after allocation | Computed, returned, then dropped | `MixedPaymentResolutionDto.UnappliedAmount` has no reader in `src` |
| Manual over-allocation | Capped at the amount owed (since #3373) | `ServicingAggregate.buildResolution`, `DirectLendingServiceSupport.ResolveMixedPayment` |
| Terms model | No rule for excess; has `PrepaymentAllowed`, `PrepaymentPenaltyRate` | `src/Meridian.Contracts/DirectLending/DirectLendingDtos.cs` (`DirectLendingTermsDto`) |
| Terms hash | SHA-256 of camel-case JSON of the **whole** record, no null/default skipping, computed independently in C# and F# | `DirectLendingServiceSupport.ComputeTermsHash`, `InMemoryDirectLendingService.ComputeTermsHash`, `DirectLendingEventRebuilder.ComputeTermsHash`, `ContractAggregate.computeTermsHash`; pinned by `DirectLendingTermsHashCharacterizationTests` and the F# `DirectLendingScheduleCharacterizationTests` |
| Balances | Principal, interest, commitment fee, fees, penalty; no prepaid or unapplied | `OutstandingBalancesDto`; Postgres columns in `PostgresDirectLendingStateStore` |
| Mixed-payment journal | Dr `Cash` / Cr `LoanAndAccrualsClearing` for the **full** receipt; allocations are not split | `DirectLendingOutboxDispatcher` (`loan.mixed-payment-applied`), `LoanAccountingProjector` |
| Daily accrual | Adds to `InterestAccruedUnpaid`; no prepaid offset | `ServicingAggregate.postDailyAccrual` |
| Prepayment penalty | Separate `ChargePrepaymentPenalty` command; refused when `PrepaymentAllowed = false` | `ServicingAggregate.chargePrepaymentPenalty` |

Two implementations must stay identical: the F# aggregate (Postgres path) and the C# in-memory
service.

## Design

### 1. Terms: `ExcessPaymentApplication` rule

```csharp
public enum ExcessPaymentApplication : byte
{
    HoldUnapplied = 0,        // credit UnappliedCash; operator applies or refunds later
    PrincipalPrepayment = 1,  // apply to PrincipalOutstanding (subject to prepayment terms)
    PrepaidInterest = 2,      // credit PrepaidInterest; consumed by later accruals
}

public sealed record DirectLendingTermsDto(
    /* …existing positional members unchanged… */
    DirectLendingSecurityMasterReferenceDto? SecurityMasterReference = null)
{
    // Expand-only: null means "not specified", which behaves as HoldUnapplied.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExcessPaymentApplication? ExcessPaymentApplication { get; init; }
}
```

**Hash compatibility.** The member is an `init` property, not a positional parameter, so existing
constructors and positional call sites are unaffected. It is `null` for every existing terms
version and omitted from the serialized JSON while `null`, so all three pinned characterization
hashes, and every persisted terms version, keep their current hash. A non-null value changes the
hash, which is correct because it is a material term. When set, the value serializes as its
numeric enum value, for example `"excessPaymentApplication":1`, like the other enum terms. Neither
hash serializer registers a string-enum converter. New tests pin both cases and the serialized
form, so a future change to the serializer options fails a test instead of silently re-hashing. Because the
attribute lives on the shared DTO, the C# and F# hashes stay in step without any change to their
serializer options.

**Amendment.** Changing the rule is a normal `AmendTerms` (a new terms version with an amendment
reason). The rule in force at the payment's effective date applies.

### 2. Balances: `PrepaidInterest`, `UnappliedCash`

`OutstandingBalancesDto` gains two members with default `0m`. They are appended at the end so
existing positional constructions compile unchanged:

```csharp
public sealed record OutstandingBalancesDto(
    decimal PrincipalOutstanding, decimal InterestAccruedUnpaid, decimal CommitmentFeeAccruedUnpaid,
    decimal FeesAccruedUnpaid, decimal PenaltyAccruedUnpaid,
    decimal PrepaidInterest = 0m, decimal UnappliedCash = 0m);
```

- **Persistence:** migration `010_direct_lending_excess_payment_balances.sql` (reserved in
  `database/migration-reservations.json`) adds `prepaid_interest` and `unapplied_cash` as
  `numeric NOT NULL DEFAULT 0` to the servicing-state table and its snapshot. `PostgresDirectLendingStateStore`
  reads and writes both.
- **Replay:** `DirectLendingEventRebuilder` derives both balances from events, so a rebuilt loan
  matches the live one. Older events carry no excess payload, and their balances stay 0. Since
  increment 3 the rebuilder replays `loan.mixed-payment-applied` from the recorded resolution;
  before that it rejected the event, so any loan with a mixed payment could not be rebuilt.
- **Loan value:** neither balance is a receivable. Both are amounts owed *to* the borrower, so
  read models must not add them to outstanding balance (see the `LoanPortfolioSummaryBuilder` review below).

### 3. Routing the excess

Both resolvers keep their allocation logic: the automatic waterfall, or the manual breakdown capped
at the amount owed. Only the **remaining excess** is then routed by the rule in force:

| Rule | Excess goes to | Limits |
|---|---|---|
| `null` / `HoldUnapplied` | `UnappliedCash` | none |
| `PrincipalPrepayment` | `PrincipalOutstanding` (lots earliest first, as principal payments do today); any remainder → `UnappliedCash` | capped at outstanding principal; when `PrepaymentAllowed = false` the excess is held unapplied with a note ([Decision 2](#decisions)); the penalty is charged automatically ([Decision 3](#decisions)) |
| `PrepaidInterest` | `PrepaidInterest` | none: uncapped, consumed by daily accruals ([Decision 4](#decisions)) |

`MixedPaymentResolutionDto.UnappliedAmount` stays the single canonical "held unapplied" figure,
but it now means the amount held **after** routing. Under the default `HoldUnapplied` rule it
is unchanged. The excess before routing is `ExcessToPrincipal + ExcessToPrepaidInterest +
UnappliedAmount`, so there is no separate field that could drift. The DTO adds:

```csharp
public decimal ExcessToPrincipal { get; init; }
public decimal ExcessToPrepaidInterest { get; init; }
public ExcessPaymentApplication? ExcessRuleApplied { get; init; }
public string? ExcessRuleNote { get; init; }        // ExcessPaymentRuleNotes, shared by C# and F#
public decimal PrepaymentPenaltyCharged { get; init; }
```

These fields are written into the `loan.mixed-payment-applied` event payload, so replay and journals
can read them.

**As implemented (increment 3):**

- **Interest-only period.** The automatic waterfall still pays no scheduled principal during the
  interest-only period, but the rule still applies to the excess: a voluntary prepayment is allowed
  then when `PrepaymentAllowed` is true. The C# resolver previously ignored the interest-only period
  and paid principal; it now matches the F# aggregate.
- **Allocation rows.** Routed excess gets its own `payment_allocation` rows (`PrincipalPrepayment`,
  `PrepaidInterest`, `UnappliedCash`, rule `Excess<Rule>`), so allocations always sum to the cash
  received. Principal reductions apply to drawdown lots earliest first in both services; the
  in-memory service previously used booking order.
- **Penalty in the same event.** The store saves one event per command, and its command-id
  idempotency check rejects a second event under the same id. The penalty is therefore recorded on
  the resolution (`PrepaymentPenaltyCharged`), added to `PenaltyAccruedUnpaid` in the same state
  transition, and journaled in the mixed-payment entry (Dr `PenaltyReceivable` / Cr
  `PenaltyIncome`) by `DirectLendingOutboxDispatcher`, the same lines `loan.prepayment-penalty-charged`
  posts. `LoanAccountingProjector` journals neither penalty form today; that gap predates PRD-115.

### 4. Accrual consumes prepaid interest

`postDailyAccrual` computes the day's interest exactly as today, then offsets it from
`PrepaidInterest` first:

```
consumed = min(PrepaidInterest, dailyInterest)
PrepaidInterest       -= consumed
InterestAccruedUnpaid += dailyInterest - consumed
```

The accrual event records `prepaidInterestConsumed`, and the journal reflects it (§5). PIK
accrual is left unchanged: PIK interest does not consume prepaid interest ([Decision 4](#decisions)).

**As implemented (increment 4):** `dailyInterest` is the day's *cash* interest (`InterestAmount`,
after any PIK share), so a PIK capitalization policy's deferred share and the legacy PIK toggle
consume nothing. The consumed amount is on the F# `DailyAccrualDecision` and the event payload
(`prepaidInterestConsumed`), not on `DailyAccrualEntryDto`, which is also stored in
`accrual_entry_projection` and would need a column. Entry income (`InterestAmount`) is unchanged.
The rebuilder treats accrual events without the field as consuming nothing. The accrual journal
still debits `AccruedInterestReceivable` for the full day until increment 5 splits it. The
in-memory lane posts accruals unrounded where the F# aggregate rounds to cents, a pre-existing
difference left to a separate change.

### 5. Journals

**Mixed payment, split by allocation.** This replaces the single clearing pair in both journal paths
(`DirectLendingOutboxDispatcher` and `LoanAccountingProjector`):

| Line | Dr | Cr |
|---|---|---|
| Cash received | `Cash` | |
| To interest | | `AccruedInterestReceivable` |
| To commitment fee / fees / penalty | | `CommitmentFeeReceivable` / `FeeReceivable` / `PenaltyReceivable` |
| To principal, including excess prepayment | | `LoanPrincipal` |
| Excess to prepaid interest | | `PrepaidInterestLiability` |
| Excess held unapplied | | `UnappliedCashLiability` |

The entry balances because allocations plus routed excess equal the cash received. Account names
must be added to the chart of accounts and its mapping; reuse existing receivable names where
they already exist.

**Accrual with prepaid consumption.** Dr `PrepaidInterestLiability` for `consumed`; Dr
`AccruedInterestReceivable` for the rest; Cr `InterestIncome` for the full day.

**As implemented (increment 5):** `DirectLendingJournalLines` builds both entries and is shared by
`LoanAccountingProjector` (in the command transaction) and `DirectLendingOutboxDispatcher`, so the
two journal paths cannot post differently. The mixed-payment entry also carries the prepayment
penalty charged on excess (Dr `PenaltyReceivable` / Cr `PenaltyIncome`). A payment counts as
"new" when its resolution carries the PRD-115 excess members; older payments keep Dr `Cash` / Cr
`LoanAndAccrualsClearing`. A resolution that does not sum to the receipt is rejected rather than
posted unbalanced. `PrepaidInterestLiability` and `UnappliedCashLiability` are liability accounts
and are on `LedgerPeriodPostingGuard`'s instrument-account list, so they need Security Master
evidence like the other loan accounts. `AccrualLedgerService.AccrueAsync` takes the consumed amount
as an optional argument.

**Migration of the clearing account.** Historical `LoanAndAccrualsClearing` postings are not
rewritten. A one-off reconciliation report lists the residual clearing balance per loan, so the
owner can decide whether to reclass it.

### 6. Operator commands (later increment)

- `ApplyUnappliedCash(loanId, amount, breakdown?)`: runs the waterfall or a manual breakdown
  against `UnappliedCash`.
- `RefundUnappliedCash(loanId, amount, externalRef)`: Dr `UnappliedCashLiability` / Cr `Cash`.

Both are governed like the other direct-lending commands: the `ManageDirectLending` permission,
the direct-lending mutation rate limit, fund-scoped write tenancy and command-id idempotency. (This
originally said "maker-checker"; no direct-lending command has a maker-checker step, and the owner
chose to keep these consistent on 2026-11-09.)

**As implemented (increment 7):** `POST /api/loans/{loanId}/unapplied-cash/apply` and
`/unapplied-cash/refund`. Apply allocates up to the requested amount by the automatic waterfall
(interest-only aware) or a manual breakdown capped at what is owed; only the allocated part leaves
`UnappliedCash`, and no cash is received (`LastPaymentDate` is unchanged). Refund requires an
external reference and records an `UnappliedCashRefund` cash transaction. Both refuse amounts that
are not positive or exceed the held cash. Events `loan.unapplied-cash-applied` and
`loan.unapplied-cash-refunded` are replayed by the rebuilder and journaled by
`DirectLendingJournalLines` in both journal paths. Workstation screens for the two commands follow
separately.

## Decisions

The owner decided all four on 2026-11-02, each as recommended.

1. **Default for loans without the rule: `HoldUnapplied`.** A null rule behaves as `HoldUnapplied`.
   This is the current behaviour, now tracked as `UnappliedCash`, so no existing loan changes
   allocation.
2. **`PrincipalPrepayment` with `PrepaymentAllowed = false`: hold as unapplied, with a note.** The
   excess goes to `UnappliedCash` and `ExcessRuleNote` records why. Cash posting never fails for
   this reason.
3. **Prepayment penalty: charged automatically.** When excess is applied to principal, the same
   command charges it (recorded on the mixed-payment event, see §3 "As implemented"; originally
   specified as a separate `loan.prepayment-penalty-charged` event) for `PrepaymentPenaltyRate × excess applied to
   principal`, journaled as today (Dr `PenaltyReceivable` / Cr `PenaltyIncome`). A zero or missing
   rate charges nothing.
4. **Prepaid-interest consumption: by daily accruals, uncapped.** Each daily accrual is offset from
   `PrepaidInterest` first until it is exhausted. PIK interest is not offset.

## Increments

| # | Scope | Tests | Done when |
|---|---|---|---|
| 1 | Terms rule + hash-compatible attribute; enum in Contracts | Characterization hashes unchanged; non-null rule changes the hash, identically in C# and F# | Both hash lanes pass, with no new terms version for existing loans |
| 2 | Balances on the DTO, migration 010, state-store read/write, rebuilder replay | Round-trip persist/load; replay equals live state; existing loans read as 0 | `PostgresDirectLendingStateStore` and rebuilder tests pass |
| 3 | Excess routing in both resolvers + event payload fields | Each rule × manual/auto × interest-only period × `PrepaymentAllowed` false; C# and F# give identical resolutions | Parity tests pass in the C# and F# lanes |
| 4 | Accrual prepaid consumption | Consumption to zero, partial day, PIK unaffected | Balances match a hand-computed schedule |
| 5 | Split mixed-payment and accrual journals in both journal paths | Every entry balances; account mapping resolves; reconciliation sees no clearing residue for new payments | Journal and reconciliation tests pass |
| 6 | Read models + UI (browser and WPF parity) show prepaid and unapplied | Shared read model returns both; screens render them | Both workstations display the same values |
| 7 | `ApplyUnappliedCash` / `RefundUnappliedCash` commands | Governance, idempotency, journals | Commands available on both workstations |

Increments 1–3 are worth shipping on their own: they make the excess visible and stop money from
going untracked, before any journal change.

## Risks

- **Hash re-keying:** prevented by the null-omitted `init` property. The characterization tests
  fail loudly if that assumption breaks.
- **Two implementations drift:** every rule/route test runs against both the F# aggregate and the
  C# in-memory service.
- **Journal change is visible to accounting:** split journals change ledger postings for new
  payments. Increment 5 needs an accounting sign-off, and historical clearing balances are
  reported, not rewritten.
- **`LoanPortfolioSummaryBuilder` and WPF totals:** review that neither adds the new liabilities
  into outstanding exposure.
