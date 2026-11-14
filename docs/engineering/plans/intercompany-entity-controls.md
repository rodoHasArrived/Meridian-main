# Intercompany contracts and entity controls: staged implementation

**Status:** implementation complete; verification in progress

**Owner:** Accounting and Ledger

**Reviewed:** 2026-11-13

The adopted scope is P1–P5: an agreement registry, effective entity controls, stable account roles
and N-entity consolidation, reviewed counterpart proposals, and full reporting-currency
translation with income elimination and shared workstation presentation. The canonical roadmap
retains status; this plan records implementation decisions and verification boundaries.

| Stage | Decision and acceptance boundary |
| --- | --- |
| P1 registry | Reuse the canonical native legal registry for terms, evidence and independent review. Immutable accounting profiles pin its exact ID/version/hash and own only templates, party books, account mappings and bounded validity. Configuration owner approval never grants legal approval or novation. Guarantees have no posting treatment. |
| P4 lifecycle | Effective-dated history; enforce immediately on new books, warn on existing books until audited activation. Winding-down supports evidenced controlled close, with no new commitments. Keep residual balances visible. |
| P2 `w10-v2` | All direct 100%-owned legal entities, at least two, in one functional currency. Stable chart-node bindings with historical aliases; distinct chart paths and ledger names. Preserve v1 readers and posted overlays. |
| P3 proposals | Atomic source-commit retention, durable retry, source-identity dedupe, explicit existing-counterpart linking, balanced destination drafts and destination review. Aging names its basis. Never auto-post. |
| P5 `w10-v3` | Whole-trial-balance translation using approved immutable rate sets and policy/configuration revisions. Closing, transaction-date/declared-period and historical conventions; retained opening facts; translation reserve; separate economic FX, rounding and intercompany breaks. |
| P5 `w10-v4` | The policy explicitly enables both `consolidation.receivable-payable` and `consolidation.revenue-expense`. Income pairs require a shared transaction identity. Both rules' exact versions remain in the reviewed evidence. |

P1 and P4 share no implementation dependency. P2 follows their contract seams; P3 consumes
approved legal revisions and accounting profiles; v3/v4 follow role identity and governed FX authority. These
stages are policy capabilities, not automatic activation milestones. An unchanged v1 book
continues to use its original rule; changing a book's version requires fresh review.

Migration inventories lifecycle history, chart-role bindings, source currency, agreements and
unresolved legacy lines. Independently reviewed legacy attribution pins each exact source digest
without rewriting journals. When pair-only v1 overlays must become agreement-specific, v2 creates
a linked reviewed reclassification: reverse the old attribution and add the new attribution,
with no duplicate net elimination. Missing or ambiguous history blocks activation. Never infer
historical eligibility from the current status.

Posting holds lifecycle, canonical legal, accounting-profile/account and applicable consolidation authority through the
owned transaction commit. Consolidation keeps source/overlay version checks, authoritative
ownership, ordered processing, stale-review refusal, deterministic reruns and linked corrections.
Source-map, lifecycle, agreement, rate or policy changes require a new calculation and review.
Migration controls do not weaken existing financial or authorization invariants.

The integrated native registry uses one graph fence, sorted lifecycle authority and a proof-aware
legal command transaction. Posting batches validate all native references under one sorted legal
lease before operational profile authority; they never reacquire the same shared graph or legal
fence behind a waiting writer. The bounded native preparer-attested dividend path retains these
entity controls through its outer journal, spine and case commit. Ordinary manual review uses
the same combined controls in a rollback-only validation transaction.

The bounded perimeter excludes indirect/partial ownership, minority interests and investment/equity
elimination. A reviewed reporting transition pins the prior book's retained overlays and the new member/source,
account, policy and FX population. Independent approval opens a new empty dedicated book at a
strictly later cutover, rebuilding the new target without importing old overlays. The old book
remains historical before cutover and is blocked afterward. Unknown or unapproved in-place member
changes still block. Former-member external exposures stay in gross without elimination. Guarantee posting,
loan-interest mirroring, automatic economic novation, automatic posting, live provider FX sourcing
and external-GL posting are outside this implementation. Browser and WPF use shared contracts and
APIs; WPF runtime proof requires Windows.

Verification must cover old v1 evidence, three/ten-entity populations, account rename/retirement,
source and mapping races, legacy attribution/reclassification, equal-amount independent events,
retry/restart, reciprocal directions, duplicate manual counterpart candidates, destination authority,
new versus legacy book controls, effective lifecycle boundaries, controlled close, missing/stale FX,
period/historical conventions, translation reserve and rounding, and income-rule gating. Added tests
and source paths are inspectable; their results belong in the change's final validation record.
No full-suite, Windows, hosted-CI or live-provider success is implied here.

## Structured terms and obligation-monitor extension

The subsequent monitoring/structured-terms extension reuses this foundation rather than repeating
its original stage numbering or creating another legal registry. Optional native structured terms
retain rates, schedules, bilateral allocation declarations and guarantee disclosure; pure date
previews reuse the existing kernels and do not calculate charge amounts. External-review attestation
adds a separately labeled, evidence/hash-bound sole-operator path alongside ordinary independent
legal review. Explicit operator-reviewed legacy attribution retains exact source and authority pins.

The obligation monitor consumes the actual balance schedule and canonical legal/lifecycle sources,
with a 30-calendar-day default expiry horizon, version/freshness evidence and a nonblocking close
advisory companion. Existing schedule blockers, posting controls, reporting transitions and exact
`w10-v1` through `w10-v4` behavior remain intact. Typed terms persist in the existing PostgreSQL
JSONB snapshots without schema replacement or historical hash migration. Acceptance evidence for
this extension is separate from full W10/W12 completion; roadmap statuses are not promoted merely
because the source exists. See the [domain extension](../../domain/intercompany-agreements-and-entity-lifecycle.md#obligation-monitoring-and-structured-legal-terms).
