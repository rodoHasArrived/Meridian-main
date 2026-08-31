# Corporate Action Case

**Status:** active guidance
**Owner:** accounting-and-ledger
**Reviewed:** 2026-08-31

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

The current foundation does not have an authoritative service that can enumerate every affected
tenant and company, resolve the corresponding fund, account, portfolio, custody, ledger-book,
period, basis, currency, and jurisdiction assignments, and apply one source decision to all scoped
cases atomically. A decision against the global source proposal would otherwise let the first
tenant accept or reject the observation for every other tenant.

The public HTTP workflow is therefore explicitly read-only at the source-decision boundary.
Proposal inbox, list, detail, retained source evidence, and existing scoped case views remain
available. Their server-owned action availability sets both `CanAccept` and `CanReject` to false,
sets `CanCompareEvidence` to false because the compact inbox does not yet carry the retained
per-source candidates needed for an operable comparison, and returns a stable authoritative-fan-out
blocker. Canonical accept and reject commands return the typed
`corporate_action_persistence_unavailable` response without calling the decision service. The
retired unscoped inbox-apply route remains a `410 Gone` tombstone. This hard boundary is not an
operator-configurable feature toggle and must remain closed until trusted fan-out authority exists.

The endpoints still reject caller-supplied narrow scope on acceptance: assignments are read from
the record, never asserted. Post-acceptance, narrowly scoped cases are readable, and every
mutation on one requires a full-scope assertion that exactly matches the stored, server-resolved
scope — the assertion proves the caller acts on the record it read; it cannot resolve or widen an
assignment.

Production composition registers the deterministic corporate-action accounting projector and the
Asset Accounting Event Spine mapper as singleton services. Registration makes the pure preparation
boundary available to governed workflows; it does not by itself make a Corporate Action Case
posting-ready.

## Approval and Posting Lane

The approval and posting half of the lifecycle is a dedicated governed command path
(`ICorporateActionCaseAccountingService`), deliberately separate from the generic case transition
command, which can prepare a case through `ReadyForApproval` but can never grant `Approved` or
`Posted`:

1. **Attach projection** (`AccountingReview`, requires `PrepareCorporateActionAccounting`). The
   preparer drives the Asset Accounting Event Spine to an exact `Drafted` candidate through the
   existing ledger endpoints, then attaches a durable exact-version projection binding to the
   case. The binding is verified against the retained spine snapshot (drafted-candidate
   fingerprint, balanced generated journals, promoted rule pack with a selected rule, exact
   ledger-book/period/basis/fund/currency congruence with the case scope, expected period
   version) and is bound to the resulting case version — any later content mutation makes it
   stale. Attaching supersedes the previous binding and voids any unconsumed approval.
2. **ReadyForApproval** (generic transition, `PrepareCorporateActionAccounting`). The durable
   store's transactional guard admits the transition only when the current binding targets the
   exact case version, is balanced, policy-covered, lot-resolved, and matches the exact scope;
   otherwise it refuses with `corporate_action_projection_stale` and the other typed codes.
3. **Approve** (`ReadyForApproval → Approved`, requires `ApproveCorporateActionAccounting`).
   Maker-checker: the approver must be independent of the preparer retained on the binding, and
   must supply typed approval evidence (reference plus SHA-256). A governed return
   `Approved → AccountingReview` withdraws the candidate and voids the approval, audit-preserved.
4. **Post** (`Approved → Posted`, requires `PostCorporateActionAccounting` — granted to no
   built-in role; deployments assign it through an audited custom-role policy). Posting is
   refused with typed problem codes without exact scope, approved policy coverage, an open
   accounting period at the exact expected version, balanced journals, valid lot resolution,
   retained evidence, and the required maker-checker approval; the posting operator must be the
   approving operator so the spine's retained approval evidence attests one maker-checker act.
   The command executes the durable journal through the spine posting authority (which
   independently re-enforces maker≠checker, period posture, balanced lines, and lot authority),
   then records the immutable journal identity, ledger book, period, balanced amounts, currency,
   and Posted status on the case. A failed attempt leaves the case recoverable in `Approved`; a
   crash between the spine commit and the case record is recovered idempotently on retry, and a
   spine event posted outside the case's approval is refused rather than adopted.

Journals and posted lot effects stay immutable. `Posted → RestatementRequired` opens the governed
correction lane; corrections add reversal, rebook, or restatement lineage through the spine onto a
fresh exact-version binding — a superseded binding can never be posted twice.

The Asset Accounting Event Spine remains the only route to candidate preparation, policy and
evidence validation, maker-checker approval, optimistic-concurrency checks, and durable posting.
Neither the projector nor the mapper can append a journal directly.

## Future Expansion Notes

Initial implementation uses the Clearwater corporate-action methodology as an effective-dated
source policy profile. Company-approved accounting, statutory, tax, chart-of-accounts, materiality,
and reporting policies should be added as separately versioned rule packs. External custodian
election submission remains outside this object until a governed integration is approved.
