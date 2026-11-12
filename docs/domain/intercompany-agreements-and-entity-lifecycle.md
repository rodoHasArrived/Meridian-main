# Intercompany agreements and entity lifecycle

**Status:** active implementation guidance

**Owner:** Accounting and Ledger

**Reviewed:** 2026-11-12

An **intercompany agreement** is a tenant-owned legal relationship between two distinct legal
entities, with a stable agreement identity and immutable term revisions. It is separate from a
Security Master OTC instrument and from the ownership graph used to resolve consolidation.
Multiple facilities of the same kind may coexist. Approved revisions of one facility cannot
have conflicting effective windows; dates use `[EffectiveFrom, EffectiveTo)`.

The approved terms pin each party's entity, book and stable chart node identities, currency,
settlement cadence, signed Evidence Vault artifact identity/hash, and lifecycle evidence.
Loan, cost-recharge, management-fee and expense-share agreements may support configured journal
templates. Guarantees are registry-only: they do not authorize postings or counterpart drafts.
Loan mirrors support principal; interest requires its own reviewed treatment.

Creation and amendment retain draft terms. Submission, approval, rejection, termination and
novation append lifecycle evidence rather than replacing approved terms. Approval requires the
configured party/account authority and retained signed-document evidence. A successor agreement
and novation evidence preserve the predecessor; merging an entity does not rewrite its agreements,
books or journals automatically.

An **account-role binding** connects an immutable chart node identity to Receivable, Payable,
Revenue, Expense or TranslationReserve. Chart path and ledger account name are separate fields.
Historical account names remain aliases for the same identity and cannot be reassigned to another
binding. Retiring a source mapping does not remove its outstanding posted balances. New postings
and elimination output still require current eligible mappings.

A **legacy intercompany attribution** is independently reviewed sidecar evidence over an exact
book, journal and line digest. It pins an approved agreement revision, account-binding revision
and transaction identity without editing the source journal. Corrections append a successor.
Missing source identity, currency, party or approval evidence remains unresolved; no historic
agreement or lifecycle period is invented from today's snapshot.

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

The shared entity timeline joins lifecycle revisions, ownership links, agreements and books.
Browser and desktop consume the same read model under Accounting/Portfolio navigation. A missing
lifecycle baseline or an earlier uncovered interval is shown as unknown.

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
