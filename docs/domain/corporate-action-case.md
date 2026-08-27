# Corporate Action Case

**Status:** active guidance
**Owner:** accounting-and-ledger
**Reviewed:** 2026-08-25

## Definition

A Corporate Action Case is Meridian's durable, scoped operating record for reviewing and
processing one Security Master corporate-action source event for an identified organization,
account, accounting book, basis, position population, and reporting period.

The case does not replace the issuer-level Security Master event. It links immutable source facts
to entitlement and election evidence, basis-specific treatment decisions, projected economic and
accounting consequences, approvals, posting, reconciliation, reporting, correction, and audit
proof.

## Relationships

- References one canonical Security Master corporate-action event and its append-only supersede
  lineage.
- Belongs to exact tenant, company, structure-node or fund, financial account, portfolio, custody
  account, book, period, currency, and jurisdiction scope where applicable.
- References dated position and lot evidence used to calculate entitlement.
- Owns zero or more election records and one treatment decision per accounting book and basis.
- Produces versioned economic recipes, lot-mutation plans, journal projections, and posting sets
  through the Asset Accounting Event Spine.
- Links to approvals, policy versions, reconciliation cases, reporting classifications, proof
  artifacts, reversals, rebooks, and restatements.

## Business Rules

- Source facts, treatment decisions, and generated consequences are separate authoritative layers.
- Provider methodology is retained as a source assertion; it does not itself approve accounting or
  tax treatment.
- STAT, GAAP, tax, and configured management treatments are independent and cannot overwrite one
  another.
- Every transition is durable, actor-attributed, optimistic-concurrency checked, and idempotent.
- A failed acceptance or posting attempt leaves the case recoverable in its prior state.
- Consequential actions require an exact evidence and projection version; dependent changes make
  prior approvals stale.
- Posting is prohibited without exact scope, approved policy coverage, open-period posture,
  balanced journals, valid lot mutations, retained evidence, and required maker-checker approval.
- Journals and posted lot effects are immutable. Corrections preserve the original result and add
  reversal, rebook, adjustment, or restatement lineage.
- A completed case must reconcile security movements, cash, lots, journals, and reporting
  classifications and retain an inspectable proof chain.
- Unsupported or policy-dependent action branches are valid blocked outcomes; Meridian must not
  coerce them into a generic sale or exchange.

## Provider Observation Release Gate

Provider registration is not acceptance authority. Each adapter release declares one of two
statuses: `AcceptanceEligible` or `ReviewOnly`. Review-only observations can be retained,
compared, rejected, and used to open specialist review, but cannot be accepted as canonical
Security Master facts. The gate is persisted with the observation and is rechecked at acceptance.

| Registered corporate-action feed | Release status | Acceptance evidence posture |
| --- | --- | --- |
| Alpaca | `AcceptanceEligible` | Native announcement ID, content-derived version, canonical raw-payload SHA-256, and retained typed Alpaca reference are implemented. Each observation must still contain all four. |
| Tiingo | `ReviewOnly` | Adjusted-EOD rows do not yet provide the certified stable event/version and retained raw-event evidence contract. |
| Twelve Data | `ReviewOnly` | Stable provider event/version and retained raw-event evidence are not yet implemented. |
| Finnhub | `ReviewOnly` | Stable provider event/version and retained raw-event evidence are not yet implemented. |
| Alpha Vantage | `ReviewOnly` | Stable provider event/version and retained raw-event evidence are not yet implemented. |
| Nasdaq Data Link | `ReviewOnly` | Stable provider event/version and retained raw-event evidence are not yet implemented. |

Meridian may create deterministic `synthetic-*` / `unverified-content-*` technical replay keys so
a review-only row can be staged idempotently. Those keys are not evidence. Missing evidence hashes
or references remain null; the ingest boundary must never manufacture a hash or a
`provider-event://` reference from event labels. The public source-proposal endpoint also forces
submitted release status to `ReviewOnly`; only the in-process registered-adapter orchestrator can
assert a certified adapter's release status. The legacy compatibility append path applies the same
gate and cannot auto-append review-only consensus or an eligible observation missing native
identity and retained evidence.

## Provider Revision Lineage

All durable revisions sharing `(ProviderId, SourceEventId)` form one linear amendment chain. The
store serializes that event family, treats `(ProviderId, SourceEventId, SourceEventVersion)` as the
exact replay key, automatically binds an unseen version to the locked current tip, and rejects
stale, cross-event, out-of-order, branched, or changed same-version writes. Database constraints
allow one root and one successor per node. When intermediate revisions were not accepted, a later
accepted revision points to the nearest accepted source ancestor's canonical fact; if no ancestor
was accepted, it remains a canonical root.

## Lifecycle

The processing lifecycle is distinct from the Security Master fact lifecycle:

`Detected -> NeedsTerms/Disputed -> TermsConfirmed -> Election/Allocation -> AccountingReview ->
ReadyForApproval -> Approved -> Scheduled -> Posted -> Reconciled -> Reported -> Closed`

`Blocked`, `Cancelled`, `Superseded`, and `RestatementRequired` are explicit governed states.

## Examples

- A tender event creates linked STAT, GAAP, and tax decisions. The STAT treatment can use a
  call-style recipe while GAAP uses a sale-style recipe without changing the shared source fact.
- A reinvested dividend preserves the gross dividend and successor purchase even when net cash is
  zero.
- An amended source event in a closed period retains the original posting and opens a scoped
  restatement decision rather than silently replacing history.

## Accounting Integration Boundary

The currently exposed HTTP workflow creates one tenant-and-company-scoped case for an accepted
canonical fact. It does not yet fan that fact out into the authoritative fund, account, portfolio,
custody, ledger-book, period, basis, currency, and jurisdiction assignments described above.
Until those assignments can be resolved from trusted system data, the endpoints reject supplied
narrow scope on acceptance and hide or deny mutations for narrowly scoped cases. This is a
deliberate fail-closed boundary, not a claim that tenant/company scope is sufficient for posting.

Production composition registers the deterministic corporate-action accounting projector and the
Asset Accounting Event Spine mapper as singleton services. Registration makes the pure preparation
boundary available to governed workflows; it does not make a Corporate Action Case posting-ready
and does not create a posting call site.

The current implementation deliberately stops before workflow orchestration. A safe production
orchestrator still needs authoritative, versioned dependencies for the exact case and election,
position and lot snapshots, basis-specific policy decision, promoted accounting rule pack,
retained evidence, ledger book, and accounting period. Once those inputs are supplied, the mapper
can produce a spine request and a deterministic posting idempotency key. The existing Asset
Accounting Event Spine remains the only route to candidate preparation, policy and evidence
validation, maker-checker approval, optimistic-concurrency checks, and durable posting. Neither
the projector nor the mapper can append a journal directly.

## Future Expansion Notes

Initial implementation uses the Clearwater corporate-action methodology as an effective-dated
source policy profile. Company-approved accounting, statutory, tax, chart-of-accounts, materiality,
and reporting policies should be added as separately versioned rule packs. External custodian
election submission remains outside this object until a governed integration is approved.
