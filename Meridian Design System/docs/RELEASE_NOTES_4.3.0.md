# Release notes — 4.3.0

**Date:** 2026-09-08 · **Status:** release candidate (`docs/QA_REPORT_4.3.0.md`) · **Theme:** the agency layer — take it back, mark it up, compare what you chose

4.2 finished the operator layer: how an operator arrives, finds, submits and returns. What it left was a system in which an operator could commit a value but not take it back with a reason, could sign a figure but not annotate it, and could compare entities only where a template author had pre-declared the plates. 4.3 closes those three (`docs/UX_DEPTH_4_1.md` tracks B-2 · D-3 · E-1 · E-2), brings certification into the books as carried state (`docs/ACCOUNTING_INTERFACE_4_3.md` › O-4), gives the account a destination (O-2), and finishes the template sweep 4.2 deferred.

---

## B-2 · `OptimisticCell` — a write that can be taken back

`EditableCell` answered *what may I type here*. Nothing answered *what happened to what I typed*.

- **`OptimisticCell` · `OptimisticProvider` · `OptimisticRowBar`** (`components/data/`). A committed edit reads **Provisional** — an existing condition, no new vocabulary — until `commit` resolves. A throw **rejects it with the reason attached** and restores the prior value. A confirmed edit stays revertible: one cell (*Revert…*) or a row / selection batch (`OptimisticRowBar`), always under a stated reason, recorded as its own write — never an erasure.
- Card: **Data › Optimistic editing & revert**.

## E-1 · `AnnotationLayer` — mark-up bound to a version

- **`AnnotationProvider`** per object in view; **`AnnotationAnchor`** on a grid cell, series point, ledger line or object header (¶ marker, count, popover); **`AnnotationPanel`** (All · Stale · Pinned).
- Every note carries the object version it was written against. At write time the operator picks **follow** (goes stale on a later version; *Still holds at vN* re-affirms) or **pin**. Stale is text + marker, never colour alone.
- The ¶ control stops propagation, so annotating a row does not select it.
- Card: **Operations › Annotation layer**.

## E-2 · `SelectionCompare` — comparison the operator chooses

- **`CompareToggle`** on a row / series / line → **`CompareTray`** (inline or corner) → **`CompareSheet`** → `PlateGrid` with one shared domain and tick set.
- Capped at `max` (default 6): past ~12 the plate contract says table + one focused plate, so the component refuses rather than degrades.
- Card: **Data › Comparison by selection**.

## D-3 · `templates/interaction-depth`

The normative reference for the interaction cluster on a 50,000-row windowed grid: right-click verbs, bulk edit, saved views, column chooser, keyboard-only path, plus the three components above with fixture policies that **reject for stated reasons**. Classified in `api/template-eligibility.json` (eligible: sel · con · fig · cap).

## Adopted on six production screens

| Screen | What changed |
|---|---|
| `security-master-registry` | inspector fields write optimistically — the *Save changes* staging step is gone; record header is an annotation anchor (audit length = object version); annotations panel on the Notes tab |
| `ingestion-operations` | queue **priority** is an `OptimisticCell`; the scheduler refuses a third *high* under `SCHED-2` |
| `reconciliation` | every statement and ledger memo is a line anchor; version advances with matches |
| `trading-desk` | position symbols are cell anchors; version = fill count, so a follow-note goes stale on the next fill; panel row focuses the symbol |
| `accounting-workstation` › Account Sheet | account header anchor, version = live postings |
| `strategy-runs` › Run Ledger | ▢ on any completed run with a series → normalised equity paths on one scale |

Eligibility totals recomputed: **28 templates, 156 eligible cells, 22 conforming (14.1%)**. The percentage is the honest number — the components exist; the worklist is `docs/eligibility.card.html`.

---

## O-4 · Assurance — certification as carried state

`components/accounting/Assurance.jsx`, release-candidate. Stages climb **unprepared → prepared → reviewed → certified** one at a time (`signAssurance` throws on a skip). Every signature is bound to the balance it vouched for, and `assuranceState` reports a moved balance as **void, not stale** — the same rule `DecisionSnapshot` applies to an approval whose subject moved.

- `AssuranceChip` (derived or explicit stage; strikes through when void) · `CertificationMatrix` (accounts × stages — the close's real progress bar) · `assuranceCoverage` (what the lock gate reads). `TrialBalance` gained a `trailing` column renderer.
- In `accounting-workstation`: an Assurance column on the TB (row → Account Sheet), an assurance strip and the one advancing verb on the Account Sheet, a *Material accounts certified* gate and the matrix on Period Close — **locking is blocked with the uncovered accounts named**.
- Fixture: 1310 · Equities was certified on 15 June against 819,882.00 and JE-1080 then moved it, so the gate arrives blocked and says why.

## O-2 · Account Sheet — the account as destination, not filter

`templates/accounting-workstation/views/AccountSheetView.jsx`, reached as `?view=account&account=1100`, `G A`, a palette lookup (code or name → *Sheet* / *Ledger*), or *Account sheet* on the entry inspector and the chart of accounts. One surface: the closing balance stated on the account's normal side, above **RollforwardTrace** proving opening + debits − credits = closing (opening derived and *said so* where the fixture states none), **TieOutPanel**, open items by settlement clock, postings with running balance (row → entry inspector), and **EnvelopeCoherence** across the postings' envelopes. Deep links now win over the `view` tweak default when the URL names a view.

---

## C-01 · C-02 · `LocalDate` on the namespace and the template sweep

4.2 recorded 36 template findings and deferred them because screens had no date carrier. **`LocalDate`** now sits on the namespace beside `Format` (`toISO · fromISO · dayOfInstant · utcDayOfInstant · addDays · …`); `LocalDateText` renders one as `<time>`. All 36 findings swept: every `toLocaleString("en-US")` and the one `new Intl.NumberFormat` in a template go through `Format`; the three calendar-date slices go through `LocalDate` (the ingestion coverage grid *states* that its sessions are UTC-bucketed instead of smuggling it through `toISOString`). `_strategy-executor/screen-kit.js` gained `K.int · K.usd0 · K.usd0s`; its `K.isoD` reads `LocalDate`.

## The family gate, and what the cut found

Cutting the release ran the family assignment against the manifest and found **18 exports in no family**: the fifteen new to 4.3 — expected, they had not been classified yet — and three from 4.2 (`ActionQueue` · `ActionQueueChip` · `QUEUE_RUNGS`) that shipped unclassified and were never caught because the gate was not run at 4.2's cut. Optimistic and SelectionCompare groups → **`data`**; Annotation → **`evidence`**; ActionQueue → **`decision`** beside `ActionEnvelope`. All fifteen 4.3 exports enter at **release-candidate** maturity: 28 release-candidate exports of 334.

---

## Deferred, and why

- ~~Re-running the unit suite and browser matrix on the adopting screens.~~ **Done 2026-09-10** — `tests/unit.html` 49/49 and `tests/A11Y_RUN_4.3.0.md` (16 surfaces · 24 template loads · 40 PASS · 1 OPEN) on this tree. The run earned its keep: it found a focus-return defect in the new `OptimisticCell` revert dialog and three sub-24 px targets (`WorkspaceLayout` divider and collapse icon, `Toggle` label), all fixed at the component before the flip to stable.
- **Assistive-technology run** and **Firefox / WebKit** — unchanged from 4.2. Three releases deferred; the stack has stopped moving.
- **C-11 (15 findings)** — `aria-posinset`/`aria-setsize` on synthetic rows belongs with that run.
- **C-05 (18 findings)** — row min-height in component CSS; density-token sweep.
- **Eligibility worklist** — 134 eligible cells not yet migrated. The components are the release; adoption is the line.

## Migration

Additive; nothing removed or renamed. See `docs/UPGRADING.md › → 4.3` for the three things a consumer may feel.
