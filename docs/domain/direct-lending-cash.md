# Direct-lending receipt accounting

## Scope and approved terms

This implements the backend retained-cash increments of the [approved PRD-115 blueprint](../engineering/blueprints/direct-lending-excess-payment-application.md). A receipt collects existing amounts owed or retains a borrower credit; it cannot manufacture interest or fee income. Unapplied cash and prepaid interest are liabilities, separate from loan receivables and portfolio loan value.

The canonical nullable `DirectLendingTermsDto.ExcessPaymentApplication` property from #3380 is retained unchanged. Null is omitted from JSON and behaves as `HoldUnapplied`, preserving all three legacy terms-hash goldens. A non-null numeric rule is a material term and deliberately changes the hash; the existing principal-prepayment rule golden is also preserved. The implementation does not add another policy object, opt-in, contractual reference, historical election, or terms-hash migration.

| Contract rule | Treatment of residual after base allocation |
| --- | --- |
| Null or `HoldUnapplied` | Retain the full residual as `UnappliedCash`. |
| `PrincipalPrepayment`, permitted | Apply up to remaining principal; hold any remainder unapplied. Assess the contractual penalty on this excess principal only. |
| `PrincipalPrepayment`, prohibited | Hold residual unapplied with an explicit resolution note. Do not reject the terms or receipt merely for this combination. |
| `PrepaidInterest` | Retain the entire residual as prepaid interest, without a receipt or maturity cap; later daily cash-interest accrual consumes it. |

The existing capped manual allocation and automatic waterfall remain intact. Only their residual is routed by the new rule. In particular, the prepayment permission here governs `ExcessToPrincipal`; it does not reclassify or suppress an existing base principal allocation. The automatic waterfall skips base principal during the existing `InterestOnlyMonths` window. A principal-only command separately refuses overpayment and prohibited pre-maturity prepayment; the mixed-payment path retains unresolved cash.

`Breakdown` holds only the base allocation. The retained evidence must satisfy:

```
receipt amount = Breakdown.TotalAllocated + ExcessToPrincipal
                 + ExcessToPrepaidInterest + UnappliedAmount
principal reduction = Breakdown.ToPrincipal + ExcessToPrincipal
```

Receipt allocations, servicing balances, operational journals and authoritative journal writes use these same amounts. New receipts and resolved allocations must round-trip the ledger's `numeric(38,10)` contract exactly; unsupported precision is rejected rather than rounded to fit. Operational numeric columns retain accepted values without their former two-/six-decimal truncation.

## Automatic prepayment penalty

A permitted excess principal payment assesses `PrepaymentPenaltyRate × ExcessToPrincipal`, using the existing two-decimal, midpoint-away-from-zero penalty convention. A zero or missing rate emits no assessment. Base principal is excluded from this basis.

The primary `loan.mixed-payment-applied` event records the expected penalty amount and nullable child event identity. A positive assessment emits the immediately linked `loan.prepayment-penalty-charged` event in the same command and serializable database transaction. The child retains the receipt, cash, terms version/hash, currency, rate and excess basis. It has its own source event and ledger posting identity, shares the correlation identity, and has no duplicate caller command ID. Both journal writes and both events’ outbox messages commit with the receipt, allocations, servicing state and final snapshot. Failure of either journal rolls the compound command back.

The assessed penalty remains owed after the receipt. It neither reduces the cash receipt nor silently changes its allocations. Replay validates the complete pair and applies the penalty only from the child; a truncated or inconsistent pair is refused. Existing manual penalty commands remain separate.

## Prepaid-interest consumption

A prepaid receipt retains its cash identity, receipt date, original and remaining amount, and actual canonical terms hash. The receipt event retains the corresponding terms version. Later terms amendments cannot rewrite this retained evidence or convert held unapplied cash into prepaid interest. Currency changes cannot relabel nonzero cash liabilities.

Daily accrual first computes gross earned cash interest. Eligible prepaid lots settle at most that amount in receipt-date order, retaining the amount consumed and per-receipt releases in the accrual event. The balance and journal recognize only the earned interest. PIK interest, commitment fees, other fees and penalties do not consume prepaid interest. A receipt with no retained lot evidence cannot acquire release authority by inference.

**Accounting review boundary:** the blueprint authorizes later daily consumption but does not specify the ordering of accrual and cash received on the same effective date. This candidate permits consumption only when the accrual date is strictly later than the receipt date. Same-date application stays held pending explicit review; no extra contractual opt-in is introduced.

The nonproduction C# service uses the canonical F# daily rounding convention for cash interest, PIK interest and commitment fees: two decimals, midpoint away from zero, with the effective rate floor/cap applied. Principal reductions follow drawdown-date order in both paths. These narrow parity corrections affect new synthetic calculations; historical events are never recalculated. Accounting review must confirm the existing rounding convention is suitable for the supported currencies and contracts.

## Journal treatment and controls

| Event | Debit | Credit |
| --- | --- | --- |
| Collection allocated to amounts owed | `Cash` | Corresponding accrued receivables and `LoanPrincipal` |
| Residual held unapplied | `Cash` | `UnappliedCashLiability` |
| Residual prepaid interest | `Cash` | `PrepaidInterestLiability` |
| Automatic excess-principal penalty | `PenaltyReceivable` | `PenaltyIncome` |
| Gross earned cash-interest accrual | `AccruedInterestReceivable` | `InterestIncome` |
| Consumption accompanying that accrual | `PrepaidInterestLiability` | `AccruedInterestReceivable` |

The two accrual components are equivalent to debiting prepaid liability for consumption and accrued interest for the remainder, with one credit for gross earned income. Consumption creates no additional cash or income. One receipt journal balances to the full cash receipt; the penalty journal balances independently.

The existing projector constructs typed loan accounts with loan and Security Master dimensions. The new liability account codes participate in the same instrument-lineage guard. Period availability, accounting policy, journal balance, security lineage, idempotency and atomic persistence controls remain required. Collecting fees or penalties from unmarked historical assessment evidence requires reviewed reconciliation rather than invented income. Reversal of an accrual that consumed prepaid interest is refused until a governed restoration workflow can restore the original receipt credits.

## Terms selection and replay

**Product review boundary:** the blueprint requires the terms in force at the payment effective date, but current terms versions expose recording time without an explicit contractual amendment effective date. This candidate never treats recording time as contractual effectivity or silently selects an old version. It refuses a new backdated receipt when its date predates the current version's recording date and retained prior terms differ in relevant routing fields. An explicit effective-date/terms-selection contract is required to admit these ambiguous receipts. Exact retries are recognized first and remain valid after later amendments.

New versioned receipts retain the original request, resolution, cash identity, currency, terms version/hash and penalty expectation. Replay applies recorded deltas and retained receipt permissions, validates cash conservation and paired assessments, and never creates another cash receipt or journal. Legacy events retain their historical allocation and clearing behavior; no liabilities or income are fabricated for amounts that were never retained.

## Persistence, reconciliation and rollout

Migration `010_direct_lending_excess_payment_balances.sql` consumes the blueprint's reserved migration 010. It retains complete terms JSON, cash balances, prepaid lots and per-accrual consumption evidence in normalized projections as well as state/snapshots. It also widens scoped operational amount columns so accepted decimal amounts are not rounded during persistence. The accompanying migration waiver and lock/rollout implications require Storage review. The authoritative ledger's numeric contract is unchanged.

Drain older writers before activating this capability. Database guards reject erasure of nonzero retained cash, mutation of elected historical terms and unsupported reductions. Prepaid reductions require matching retained accrual-release evidence and hash-bound prepaid terms. A future refund or correction workflow needs its own reviewed command, journal and migration support.

Reconciliation exposes receipt allocations, retained unapplied/prepaid credit and invalid or unattributed releases, grouped without adding unlike currencies. Historical unallocated receipts remain explicit cash-allocation differences. Historical `LoanAndAccrualsClearing` postings are not rewritten: a review report shows retained operational clearing residuals per loan, currency and ledger basis, with source identities. Operational drafts are labeled as such; this report is not authoritative general-ledger or bank reconciliation and does not authorize reclassification. Existing per-fee unpaid detail is not a new allocation authority; aggregate fee collections do not invent priority among individual fees.

The backend exposes balances in servicing and currency-separated portfolio read models. Full browser/WPF presentation, governed application/refund of unapplied cash, historical remediation and supported prepaid-release reversals remain later increments or review decisions. Synthetic verification provides development evidence, not production accounting certification. Independent Accounting and Storage review is requested in draft PR #3381 before merge.
