# Direct-lending receipt accounting

## Contract and evidence

The borrower owes principal and accrued receivables; a cash receipt cannot create additional
interest or fee income. A retained receipt accounts for its entire amount as collection of an
existing receivable, an expressly permitted principal prepayment, unapplied cash, or prepaid
interest. Unapplied cash and prepaid interest are credit balances, separate from amounts owed.

An optional version 1 `ExcessPaymentPolicy` on a new or amended terms version states a treatment
and a contractual evidence reference. Its absence is omitted from JSON so existing terms hashes
remain unchanged. No historical terms version receives an inferred election. New unresolved
excess is held as unapplied cash for review. Historical payment evidence is replayed as recorded;
missing historical liability accounting requires a separately reviewed remediation.

## Bounded allocation rules

Manual allocations are capped by the amount requested, remaining cash, and the outstanding
balance. The existing interest, commitment fee, other fee, and penalty priority remains intact.
Principal before maturity cannot bypass `PrepaymentAllowed`. The existing `InterestOnlyMonths`
window has no automatic scheduled principal allocation. Existing undeclared-policy waterfall behavior otherwise
remains compatible. For an explicit policy, automatic principal before maturity is an expressly
selected principal prepayment; the implementation does not invent a contractual amortization
schedule. An allowed manual principal allocation remains available.

`PrincipalPrepayment` may allocate the residual, including during interest-only periods, up to
outstanding principal. Excess beyond payoff remains unapplied. Version 1 refuses this election
when prepayment is prohibited or a positive penalty rate exists: the penalty base, assessment
order, notice conditions, and supporting instructions require explicit review. A principal-only
command rejects excess and directs the caller to the mixed-payment receipt path.

`PrepaidInterest` retains the residual as a liability. Receipt does not recognize income. Its
release instruction defaults to `HoldForReview`. An explicit `FutureCashInterest` instruction
permits FIFO application against earned cash interest on accrual dates strictly after the receipt
date. Each receipt retains its own instruction, reference, date, and remaining credit; an amendment
cannot retroactively change those instructions. PIK, fees, and penalties never consume these
credits. Residual cash received at or after maturity has no demonstrated future contractual interest
period and remains unapplied after collecting owed amounts. Refunds, application of held unapplied cash, other release priorities,
penalty-bearing prepayment, and retrospective corrections remain explicit review questions.

New receipts and allocations must round-trip the authoritative ledger's `numeric(38,10)` amount
contract exactly; unsupported precision is rejected, never rounded to fit. Operational cash,
allocation, balance, journal, and reconciliation columns retain accepted decimals exactly.
Currency amendments and restructuring cannot relabel nonzero cash liabilities. New fee and
prepayment-penalty assessments receive corresponding authoritative receivable postings; collection
against older unmarked assessments requires reviewed reconciliation instead of invented income.
The existing legacy penalty-assessment workflow remains separate from principal collection;
this change does not certify its enforcement or invent a penalty waiver.

The nonproduction C# service now uses the existing F# daily rounding convention: cash interest,
PIK interest, and commitment fees are rounded to two decimal places, away from zero at midpoint.
This corrects a prior difference in future synthetic accrual amounts; retained historical events
are not recalculated. The convention comes from the F# accrual implementation, not from the
ledger's ten-decimal precision limit. Accounting must review its suitability for supported
currencies and contractual rounding instructions.

## Journal treatment

| Event | Debit | Credit |
| --- | --- | --- |
| Receipt allocated to amounts owed | Cash | Corresponding accrued receivables and principal |
| Residual held unapplied | Cash | UnappliedCash liability |
| Residual held as prepaid interest | Cash | PrepaidInterest liability |
| Earned cash-interest accrual | AccruedInterestReceivable | InterestIncome |
| Supported application of prepaid interest | PrepaidInterest liability | AccruedInterestReceivable |

One receipt journal accounts for the full cash amount. A later supported application accompanies
the gross earned-interest accrual and reduces its receivable; it creates neither another cash
receipt nor additional income. Both authoritative postings and operational drafts use the recorded
allocation and currency. Reversal of an accrual that released prepaid interest is refused until a
governed restoration workflow can restore the original receipt balances and instructions.

## Persistence and replay

The new cash-accounting event version retains the input, allocation, terms version/hash,
currency, and cash receipt identity. The ledger debits the full cash receipt and credits the
specified receivables and held-cash liabilities. Operational drafts use the same allocation
interpretation. Events, balances, allocations, receipts, ledger entries, and outbox evidence
remain in the existing atomic PostgreSQL transaction. Replay applies recorded allocations,
never the current contract's rules, and never creates a second journal or receipt. The forward
migration protects retained cash and elected historical terms from older writers. Stop older
writers before activating this capability; database guards reject erasure and unsupported cash
reductions. A future refund/correction capability requires its own reviewed migration and evidence.

Receipt-allocation reconciliation distinguishes accounted-for cash from an applied receivable:
held cash remains visible with its receipt and source event, while incomplete historical
allocations remain an explicit difference. Synthetic verification is development evidence;
independent Accounting and Storage review is requested before merge. The existing per-fee
`unpaid_amount` breakdown is not a new allocation authority: fee collections here retain the
aggregate owed amount and receipt allocation without inventing a priority between individual fees.
