# Books interface proposal — toward 4.3

**Status:** proposal, not law · **Date:** 2026-08-17 · **Baseline:** `ACCOUNTING_INTERFACE_4_2.md` — assumes its spine (O-1), Account Sheet (O-2), triptych (O-3), and C-1–C-6

4.2 organized the books by **scope** (period × basis × entity) and **obligation** (what must tie, post, close), and made figures prove their own arithmetic. Three axes remain unorganized: **assurance** (who has vouched for a figure — sign-off exists at object level, never on balances), **the group** (entity is a flat parameter while the books consolidate across a structure), and **knowledge time** (the books remember only their latest truth; "as we knew it when we signed" is unanswerable). Three further reorganizations (O-4–O-6) and six concepts (C-7–C-12), each stated as a gap with evidence from the current tree.

## The gap in one paragraph

The assurance machinery is complete and homeless: `AttestationPanel` binds signatures to object × version × snapshot, `ValueAuthority` defines an `approved` stage, `ProvenanceChip` carries `LastReviewedBy/At` — yet no trial balance can show which of its accounts a human has certified. Consolidation exists as two strings in `close-management` ("Intercompany eliminations" · "Schedule agreed across 4 entities") while `OwnershipGraph` draws the entity structure and `report-pack` prints "Consolidated portfolio" with no surface between them. Time: `TimeContext` declares the two clock families and `checkTimeCoherence` even predicts restatements ("effective after posting"); `AccountingPeriodSelector` knows `reopened` means "prior approvals may be invalid" — but invalidation is a tooltip, not a computation. Suspense accounts, flux explanations, standing journals: zero grep hits, one boolean, and nothing.

## Organizations

### O-4 · The assurance ledger — certification as carried state

**Gap.** Sign-off exists at object level only: close tasks carry `signoff`, change sets carry Preparer/Reviewer/Attester, `AttestationPanel` signs a whole close. The balance — the thing actually vouched for — carries no assurance state. "Which of these TB lines has a preparer even looked at?" has no answer on any surface.

**Proposal.** Assurance joins the `_books/` store as carried state per account × period × basis: **unprepared → prepared → reviewed → certified**, each stage a named signature bound to a balance snapshot (the `ObjectModel` attestation lifecycle applied at account grain). Surfaces: an assurance column on `TrialBalance` and `AccountTree`; an assurance strip on the Account Sheet (O-2); a **CertificationMatrix** view in `close-management` — accounts × stages, the close's real progress bar. The lock gate derives from certification coverage of material accounts, not task completion alone.

**Test.** From the TB, "who vouched for 4400 and against which snapshot?" is one glance; locking with uncertified material accounts is blocked with the uncovered accounts listed.

### O-5 · The group — entity as a structure, consolidation as a surface

**Gap.** `entity` in the spine is one flat value. Intercompany eliminations exist only as close-task strings; `OwnershipGraph` renders the structure but no books view consumes it; `report-pack` prints "Consolidated portfolio" from nowhere.

**Proposal.** The spine's entity becomes a path in the ownership tree (group → fund → SPV), picked from the `OwnershipGraph` structure. A consolidation view on `accounting-workstation`: statement lines × entity columns plus an **eliminations column** — every elimination figure traces to its JE pair — summing to the group column. Group scope anywhere renders consolidated figures with the elimination layer disclosed, never silently pre-summed.

**Test.** "Group net income = Σ entities − eliminations" is visible arithmetic; every elimination opens its JE pair; switching spine scope from MER-FUND-I to the group restates every books surface.

### O-6 · The second clock — the books as they were known

**Gap.** Every figure renders latest-truth only. Period states include `reopened` ("prior approvals may be invalid") and `prior-period` ("entries restate a closed period"), and `checkTimeCoherence` predicts restatements — but no surface can render June **as it stood on Aug 1**, and invalidation is prose.

**Proposal.** A knowledge instant — `asAt` — joins the spine as an optional third clock: any books view renders period 2026-06 as at a chosen instant, archive-toned per the period-state vocabulary. Per period, a **RestatementDiary**: every post-close movement as diary entries (entry · figure moved · sign-offs invalidated). Attestation snapshots (already versioned) are the anchor points.

**Test.** "The June TB as R. Chen signed it" is a URL — `?period=2026-06&asAt=2026-08-01T07:14Z`; diffing it against latest truth is one toggle.

## Concepts

### C-7 · SinceYouSigned — reopening as computed invalidation

When a period reopens, every figure that moved since the last attestation snapshot carries a delta marker (`+12,480.00 since v18`), and the diary lists which signatures the movement invalidates. `AttestationPanel` already binds version + snapshot; the `reopened` tone exists. The cheap first slice of O-6: needs snapshots, not full bitemporal storage.

### C-8 · FluxNote — explanation as an owned object

Any period-over-period movement beyond its `MaterialityAssessment` threshold demands a **FluxNote**: text + author + basis + the figure it explains, a quiet expander beside the variance. Today explanation is `explained?: boolean` on `TieOutItem` and one string in `accounting-proofing` — unowned, unrollable. FluxNotes roll up: C-6's "unexplained movement" becomes countable and `report-pack` gets its commentary from data.

### C-9 · SuspenseWatch — accounts that must die

Zero hits for suspense|clearing in the whole tree. Law + readout: accounts flagged suspense-class must trend to zero by lock; each carries balance + age; any aged past threshold is an exception feeding C-6's budget. One chip: `1999 Suspense · 12,441.00 · aged 9 days · must clear by lock`.

### C-10 · DraftLens — the books with pending writes applied

A lens toggle: apply all armed-but-unposted journals pro-forma across the current view — TB and statement figures restate, shaded `provisional` per the ValueEnvelope condition vocabulary, delta attributed per draft. Extends C-5's single-entry `PostingImpactPreview` to screen scope: "what will the books say once today's queue posts."

### C-11 · CarryMemory — the books repeat; the interface should know

The close is the same work every month, yet nothing carries the prior period's answers: close tasks have no last-period link, `JournalEntryForm` has no standing-entry concept, FluxNotes would die with their period. Law: every recurring object renders its prior-period twin (last amount, explanation, resolution) one gesture away; standing journals propose themselves with prior amounts for review — never auto-post (C-5 still gates).

### C-12 · IntercompanyZero — mirror pairs as a tie

IC balances must mirror to zero across entity pairs: a pair monitor (entity A receivable vs entity B payable, netted, materiality-colored) that joins the TieBar (C-2) and feeds O-5's elimination column. Today the entire IC concept is two task strings. The data contract is `TieOutPanel`'s two-source shape with entities as the sources.

## Sequence

| Wave | Ships | Why first |
| --- | --- | --- |
| 1 | O-4 assurance state · C-7 SinceYouSigned | Both key off attestation snapshots; the store work is shared |
| 2 | C-8 FluxNote · C-9 SuspenseWatch | Cell- and law-level; both feed C-6's budget; no new screens |
| 3 | O-5 group + consolidation view · C-12 IntercompanyZero | The structure and its zero law ship together |
| 4 | O-6 asAt clock · C-10 DraftLens · C-11 CarryMemory | Lenses and memory need the snapshotted foundation |

Nothing removes an API; everything assumes 4.2's spine. New exports: `CertificationMatrix`, the consolidation view (entity columns on `StatementTable` or a `ConsolidationMatrix`), `RestatementDiary`, `FluxNote`, `SuspenseWatch`, the IC pair monitor — plus store state (assurance, asAt, drafts, memory) in `_books/`.

**Card:** `docs/accounting-interface-4-3.card.html` · **Prior art:** `ACCOUNTING_INTERFACE_4_2.md` (baseline + numbering), `UX_DEPTH_4_1.md` (method), `operations/AttestationPanel` + `core/ValueAuthority` (assurance machinery), `charts/OwnershipGraph` (structure source).
