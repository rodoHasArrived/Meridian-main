# Intercompany agreements and entity lifecycle

**Status:** active implementation guidance

**Owner:** Accounting and Ledger

**Reviewed:** 2026-11-14

The [canonical intercompany agreement registry](intercompany-agreements.md) owns legal terms,
parties, effective dates, currency, settlement terms, document evidence and legal review.
It is separate from Security Master OTC instruments and the consolidation ownership graph.

An **intercompany accounting profile** is immutable operational configuration linked to one exact
approved native agreement ID, integer version and payload hash. It owns only its accounting
template, party books, stable account-node mappings and a validity window within the legal window.
Legal facts are derived from that approved revision and cannot be edited through a profile.
Profile submission, owner approval, rejection and retirement append history. Owner approval of
configuration does not replace the native legal registry's reviewed decision. Separate
facilities may coexist; approved revisions of one profile cannot have conflicting validity
windows. Dates use `[EffectiveFrom, EffectiveTo)`.

Native legal drafts remain available during entity setup. Submitting or approving a new legal
commitment checks effective and current lifecycle eligibility; termination remains a closure
operation. Missing retained lifecycle history can use an active legacy snapshot only with an
explicit migration warning retained in the legal audit and receipt. That warning does not grant
governed accounting-profile activation or enforced-book posting eligibility.

Loan, loan-interest, cost-recharge, management-fee and expense-share profiles support configured
journal templates. Guarantees have no posting treatment. Loan mirrors support principal only;
interest is booked under a separately reviewed `LoanInterest` profile that maps interest income on
one party against interest expense on the other, and may be calculated by the
[governed loan-interest accrual](intercompany-agreements.md#governed-loan-interest-accrual). A successor profile cannot novate a legal agreement: retain the
separately approved native successor and predecessor termination. Entity mergers never rewrite
legal parties, accounting profiles, books or journals automatically.

Entity journal lines keep native legal GUID/version dimensions separate from string accounting
profile/revision dimensions. Both pins and the trusted native payload hash travel with source
and counterpart evidence. Native legal references can exist without a profile when the book's
account-role controls permit it. Fund elimination overlays retain legal source provenance in
their reviewed evidence rather than claiming to be an entity's legal-reference posting.

Governed elimination requires reviewed accounting attribution for role-classified lines that
carry native legal provenance. A native legal-only journal remains a valid source posting and
visible in gross reporting; a reviewed profile sidecar is required before it can contribute to
eliminated output. Opposite balances on different approved profile revisions remain separate
breaks. Earlier unversioned overlays are reclassified explicitly rather than silently assigned
to a newer revision.

An **account-role binding** connects an immutable chart node identity to Receivable, Payable,
Revenue, Expense or TranslationReserve. Chart path and ledger account name are separate fields.
Historical account names remain aliases for the same identity and cannot be reassigned to another
binding. Retiring a source mapping does not remove its outstanding posted balances. New postings
and elimination output still require current eligible mappings.

A **legacy intercompany attribution** is reviewed sidecar evidence over an exact
book, journal and line digest. It pins an approved agreement revision, account-binding revision
and transaction identity without editing the source journal. Corrections append a successor.
Missing source identity, currency, party or approval evidence remains unresolved; no historic
agreement or lifecycle period is invented from today's snapshot.

Existing attribution defaults to independent review. Explicit `OperatorReviewed` mode permits
the same retained preparing/reviewing operator with review rationale and evidence; it is labeled
operator-reviewed rather than independently approved. The additional fields omit their legacy
defaults/nulls, preserving historical source and attribution representations. Corrections still
append a successor over exact source evidence rather than rewriting a journal.

Revocation is an explicit reviewed successor with `Revoked = true`, the expected current
attribution identity and a required `ReviewRationale`. It retains the prior source digest,
canonical legal ID/version/hash, accounting-profile revision, account-binding revision and
transaction pins exactly; only successor/review metadata changes. Active resolution excludes the
revoked assertion while history keeps both records. Omitting `Revoked` preserves legacy JSON,
and revocation never changes the canonical native agreement hash or posted source.

The structured due-to/from schedule reads actual posted journal amounts and enriches them with
agreement/account/party evidence. Agreement terms alone never establish a balance. Close readiness
and NetSuite mapping controls expose unavailable or incomplete schedules; external provider control
evidence remains necessary and does not authorize external posting.

A **counterpart proposal** is durable work created from a committed source journal, not a posted
counterpart balance. Its identity uses source journal/line facts, direction, agreement revision,
transaction identity and rule version. Repeated equal amounts in one period remain distinct.
Delivery creates a balanced draft in the destination's existing review queue; it grants the
source preparer no destination edit or posting rights. Retry and explicit linking to an existing
counterpart retain evidence. Open proposals and breaks show their aging basis separately from
translation differences.

Entity lifecycle decisions use effective-dated, non-overlapping status windows. Draft and Forming
entities cannot create material postings or activate agreements. Active entities are operational;
AmendmentPending and Restructuring carry review warnings. WindingDown permits evidenced settlement,
closing expense, tax, revaluation, correction and agreement-settlement amendments, while refusing
new commitments. Dissolved and Merged entities remain readable, including residual balances; only
an approved exact correction exception can authorize a new correction. Lifecycle status never
silently removes an owned entity or its balances from consolidation.

New books start with enforced controls. Existing books start in `LegacyWarning`; their warning
history is visible until an audited migration preflight succeeds. Activation pins the current
source population and authority. Warning mode does not waive balancing, period locks, approval,
tenant isolation, consolidation freshness, or missing governed FX evidence.

The shared entity timeline joins lifecycle revisions, ownership links, legal agreements, accounting profiles and books.
Browser and desktop consume the same read model under Accounting/Portfolio navigation. A missing
lifecycle baseline or an earlier uncovered interval is shown as unknown.

## Obligation monitoring and structured legal terms

Structured declarations extend the existing native legal revision, with exact decimal strings,
explicit schedule/calendar conventions and calculation-free date previews. Loans, management
fees and bilateral cost shares remain governed by their canonical parties, currency and legal
window. Guarantees add beneficiary/limit disclosure without posting or ordinary balance coverage.
See [structured terms and external-review attestation](intercompany-agreements.md#structured-declarations-and-date-previews).

Legal agreement decisions and evidenced lifecycle revisions may retain an explicit external-review
attestation from the same Meridian operator who prepared them. The external reviewer, accepted
document, exact subject hash and attesting operator remain distinct audit facts. Ordinary
independent legal review remains available; the external-review companion does not change
independent journal review or posting controls. Legacy lifecycle revisions keep their original
hashes when this optional evidence is absent. Historical unknown intervals remain unknown.

The obligation monitor reads the existing posted due-to/from schedule and canonical legal/lifecycle
authority for an explicit book/fund scope and as-of date. The default expiry horizon is 30 calendar
days. It reports approaching expiry, expired agreement exposure, missing/invalid coverage, unknown
lifecycle and retained exposure involving winding-down, dissolved or merged entities. It also
returns guarantee disclosure and source-version/fingerprint evidence. Amounts come from journals,
not legal terms or matching-party guesses.

Approaching-expiry warnings follow the native approved projection through effective-date boundaries
within the horizon. A continuous approved amendment can extend coverage; an intervening gap or
termination still ends coverage even if a later revision resumes. Superseded raw expiry dates do
not create separate warnings.

Only the new obligation-monitor HTTP response writes decimal amounts in its embedded balance
schedule as exact decimal strings. The canonical balance-schedule serializer, its retained
numeric representation and its existing fingerprint remain unchanged. Transport formatting does
not become a new financial source or change the schedule's evidence hash.

Version changes during evaluation produce stale findings; unavailable source authority remains
unavailable rather than a statement that every balance is uncovered. These observations are an
optional advisory companion to close readiness. They do not change required contributors,
blockers, `IsComplete`, `IsReadyToClose`, posting eligibility or period-close authority. The existing
blocking schedule requirements retain their own behavior.

Entity-owned books require their exact legal entity. Account, Fund, Sleeve and Vehicle books
require canonical close-subject membership and retained evidence for the entire requested scope.
Missing or unavailable membership produces an unknown advisory without changing close eligibility.

This extension reuses the existing legal, lifecycle and attribution stores. Optional typed terms
round-trip in retained PostgreSQL JSONB payloads without replacing tables or migrating approved
payload hashes. It adds no new consolidation rule version: existing `w10-v1` through `w10-v4`
evidence, reporting transitions and roadmap statuses retain their established meanings.

## Reporting perimeter and implementation owners

A **reporting perimeter transition** is a reviewed cutover into a new empty dedicated elimination
book. Its immutable receipt pins the old history, effective member diff and new source/policy/FX
operands. It changes the reporting route, not legal ownership or agreement parties. Old overlays
stay in the historical book; the new book builds a fresh target and retains separate transition
lineage. Existing source-book Primary uniqueness remains enforced; only governed Fund/Primary
consolidation policy/version pairs allow additional distinctly named reporting books.

Implementation owners are `Meridian.Contracts/FundStructure`, `Meridian.Entities/FundStructure`,
`Meridian.FinancialOperations/Intercompany`, fund-structure and ledger stores, and the shared UI
services. See the [staged implementation plan](../engineering/plans/intercompany-entity-controls.md),
[consolidation rules](intercompany-consolidation.md), and
[operator procedure](../operators/intercompany-entity-controls.md). Roadmap status and verification
remain authoritative in `docs/roadmap/data/roadmap-items.yml`; source presence does not prove a
passed check or Windows desktop execution.
