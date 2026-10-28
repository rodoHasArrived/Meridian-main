# OTC Contract Lifecycle

A lifecycle event records an externally evidenced servicing fact on one bilateral contract.
It is distinct from the shared instrument's economic terms and from a corporate action on all
holders of that instrument. Two contracts on the same instrument can have different fixings,
compression results, counterparties and credit-settlement histories.

The version-one vocabulary is `RateReset`, `Compression`, `Novation`, `CreditEvent` and
`AuctionSettlement`. Its typed payloads live in `OtcContractLifecycle.cs`. Unknown event
variants, unsupported payload versions, altered snapshots and noncontiguous replay histories fail
closed. An event records its own id, source reference, authenticated actor, recording time,
effective date, exact predecessor and resulting contract snapshots. Reset and credit events also
retain the validated instrument revision and leg/index or reference-entity target. A novation also retains the
validated successor snapshot. Retained events are immutable; correction is a later explicit
workflow, not overwriting or silently superseding an earlier fact.

All economic wire members are explicitly required: omitting a rate, notional, credit kind, price or
settlement scale never invents zero or the first enum member. Actor remains server-owned.

A write requires a confirmed live contract, its expected version and a unique event id. Future-effective
events are refused because the current snapshot model cannot stage a future notional/status change. An exact
retry returns the original event even after the contract advances; reuse of its id with different
content is a conflict. Each event advances the contract version, including a reset or credit
trigger that does not otherwise change the contract snapshot. This invalidates exposure evidence
based on the predecessor version. Once a contract has lifecycle history, generic snapshot
amendments are refused so they cannot break replay.

- A rate reset retains leg id, index name, fixing date and an explicit annual-fraction rate,
  including zero or a negative rate. Its leg must be explicitly identified on the current instrument as a floating leg with the
  matching index; fixed-only structures, missing legs and unrelated indexes are refused. It does not
  change the shared instrument, create a coupon
  payment or post a journal. A second fixing for the same leg, index and date requires a future
  correction workflow.
- Compression records one contract's externally agreed remaining notional and compression-run
  reference. A positive result reduces its notional. Zero closes the contract as `Terminated`,
  preserving the positive historical notional on the closed snapshot. No other contract is
  modified or invented; this is not an automated portfolio compression optimizer.
- Full novation closes the predecessor as `Novated` only after a separately confirmed successor
  exists. The successor must match instrument, direction, notional and currency, take effect on
  the novation date, and belong to another netting set. One successor cannot complete two retained
  novations. Its identity is explicit; no contractual relationship is inferred from a similar
  trade id or counterparty name.
- A credit event records the externally determined trigger, reference entity and determination
  reference on a `CreditDefaultSwap` contract, matching the instrument
  reference entity under a shared lock on that instrument revision. It does not supply an auction price or settlement
  amount. The contract stays live pending auction settlement; further compression, reset,
  novation or credit determination requires an explicit correction or servicing workflow.
- Auction settlement links the retained credit event on the same CDS contract. Auction date must
  follow the trigger and precede or equal settlement's effective date. Final price is a percentage
  from 0 through 100. The signed cash amount is notional times loss percentage, positive for bought
  protection and negative for sold protection, rounded with `ToEven` to the explicitly declared
  settlement scale (0–8 decimal places). Currency must match the contract's notional currency.
  Settlement closes the contract as `Terminated`. Retaining the event does not authorize a ledger
  posting or move cash.

`POST /api/security-master/otc/contracts/{contractId}/lifecycle` applies the write with the
session actor and mutation permission; `GET` reads retained history. The PostgreSQL store takes
existing netting-set contract locks, then a shared instrument-row lock before contract row locks, so a lifecycle update cannot race margin-call
issuance using the predecessor exposure snapshot. Migration 056 adds an append-only history table
and a unique novation-successor index. Contract update and event append share one transaction.

This supplies the contract-event foundation before onboarding any additional derivatives. It does
not onboard total-return swaps or FX options, implement physical credit settlement, reset cash-flow
consumption, accrue premium after a credit trigger, or create accounting instructions. Those
consumers must interpret the retained event and its evidence explicitly before enabling financial
outputs.
