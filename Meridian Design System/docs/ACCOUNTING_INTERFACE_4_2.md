# Books interface proposal — toward 4.2

**Status:** proposal, not law · **Date:** 2026-08-15 · **Baseline:** post-4.1 tree (27 templates · 284 exports)

The accounting components are strong; their **organization** is inherited. The workstation is organized by *artifact* — a ledger view, a trial-balance view, a reconciliation screen — while an accountant's work is organized by **scope** (period × basis × entity) and **obligation** (what must tie, what must post, what must close). Views are lenses. The books are the object. Three reorganizations (O) and six concepts (C), each stated as a gap with evidence from the current tree.

## The gap in one paragraph

Open `accounting-workstation` today: the period is a muted text line (`{{ periodLine }}`), basis does not exist as a control, and the close is a place you navigate to (`view=close`) rather than a state you are always in. `close-management` and `reconciliation` each keep their own idea of "June 2026". Only `reconciliation view=exception` composes evidence and consequence around the work plane; GL, TB, and statements are single-plane tables whose figures offer no arithmetic on demand. `TieOutPanel`, `PostingImpactPreview`, `BasisBridge`, `RollforwardTrace`, and `MaterialityAssessment` all exist — as **screens you visit**, not **states you carry**.

## Organizations

### O-1 · The books spine — period × basis × entity as carried context

**Gap.** Four templates (`accounting-workstation`, `close-management`, `reconciliation`, `investment-event-ledger`) hold accounting state with no shared identity. Period is display text; switching screens can silently switch scope.

**Proposal.** A `templates/_books/` shared store — precedent: `templates/_strategy-executor/` (one store, one URL identity, one nav rail across four screens). URL carries `period` / `basis` / `entity`; `WorkspaceContextBar` renders the scope on every books surface; changing period anywhere changes it everywhere. Deep links stay copy-safe: `AccountingWorkstation.dc.html?period=2026-06&basis=gaap&view=tb`.

**Test.** From any books screen, the operator can answer "which period, which basis, which entity am I stating?" without navigating. Cross-screen: mark a break in reconciliation, jump to GL — same period, no re-selection.

### O-2 · Account as destination, not filter

**Gap.** An account number is a query param on the GL view. Balance (`AccountTree`), movement (`RollforwardTrace`), entries (`LedgerTable`), open items (`AgingTable`), recon state (`TieOutPanel`), and lineage (`LineageGraph`) exist on six different surfaces; nowhere do they converge.

**Proposal.** An **Account Sheet** drill-in — the books analogue of `security-master-registry`'s entity inspector: one surface where an account's closing balance sits above its proof (opening + activity = closing), its open items, its tie state, and its posting history. Every rendered account code anywhere in the suite becomes a link to it. Ships as a sibling DC inside `accounting-workstation` (precedent: `CloseTieOut.dc.html` inside `close-management`).

**Test.** "Why is 1200 · Cash what it is?" is one click from any occurrence of `1200`, and the answer page proves its own arithmetic.

### O-3 · Ledger · evidence · consequence — the accounting archetype

**Gap.** The triptych (work plane · evidence rail · consequence bar) exists once, in `reconciliation view=exception`. GL, TB, FX, and statements render a table and nothing else: select a figure and the screen stays silent about *why it is what it is* and *what the pending action will do*.

**Proposal.** Adopt the triptych as the archetype for every books view: right rail = the selected figure's envelope (`ValueEnvelope`: condition · authority · evidence · clocks · materiality) plus its trace; bottom bar = `PostingImpactPreview` for whatever write is armed. `LinkedSelectionProvider` already carries the selection; the rails subscribe.

**Test.** Select any cell in TB → the rail states condition, source, and as-of without a modal. Arm any Post → the debit/credit impact is visible before the click, every time.

## Concepts

### C-1 · PeriodPosture — the close as ambient state

One chip in the `StatusBar` of every books surface: period state (**open · soft-close · locked**), days to close deadline, failing-gate count — `2026-06 · SOFT-CLOSE · D-3 · 2 gates failing`. Click → `close-management`. Today the close is invisible from GL, recon, and statements; `CloseCalendar` has the data, no surface carries it. New `accounting/PeriodPosture` component; consumes the close store from O-1.

### C-2 · TieBar — "the books are tied" as a heartbeat

A persistent strip of subledger→GL ties (cash · positions · tax lots · income): each tie shows `Tied` or `Out by (2,340.00)`, colored by materiality, expanding to the full `TieOutPanel`. Converts tie-out from an event you schedule into a condition you observe. Sits under the workspace context bar on GL, TB, and close views. New `accounting/TieBar`; data contract already exists in `TieOutPanel`.

### C-3 · Proof-on-demand — no unproven figure at rest

Law plus one affordance: every **terminal balance** can expand its identity — opening + debits − credits = closing (`RollforwardTrace`), Σ children (`AccountTree`), statement line → TB lines (`CalculationTrace`). Rendered as a quiet expander on `AmountCell` (`proof` prop), reusing the `DataGrid` proof contract. Today proof exists on whole tables that opt in; a single figure in a statement offers nothing.

### C-4 · BasisLens — basis as a lens, not a report

`basis` (GAAP · Tax · Stat) joins the spine (O-1) and restates every figure on screen; any two bases diff via `BasisBridge` inline — select a figure, see the bridge items that explain the delta. Today basis lives only inside `BasisComparisonMatrix` on `alm-review`; the GL itself is basis-blind.

### C-5 · No blind postings — one gate for every ledger write

Every write to the ledger — journal drawer, recon accrual, FX reval post, close entries — runs the same sequence: balance gate → `PostingImpactPreview` → `ActionGuard`-wrapped Post. Today only the journal drawer gates on balance; `onPostAccrual` and `onPostReval` write directly. This is adoption law, not a new component; the pieces exist.

### C-6 · ExceptionBudget — close readiness as a number

Per period: Σ|out-by| across open reconciliation breaks + unexplained TB movement, tracked against `MaterialityAssessment` thresholds — `Open exposure 4,120.00 of 25,000.00 budget · 6 items`. Feeds C-1's gate count and O-3's consequence rail; the close lock (`canLock`) derives from it instead of a hand-set boolean. New derived selector in the `_books/` store plus a small `accounting/ExceptionBudget` readout.

## Sequence

| Wave | Ships | Why first |
| --- | --- | --- |
| 1 | O-1 spine (`_books/` store) · C-1 PeriodPosture | Everything else keys off carried scope |
| 2 | C-3 proof-on-demand · C-5 posting gate | Cell-level and law-level; no new screens |
| 3 | O-3 triptych on GL/TB/statements · C-2 TieBar | Rails consume the spine + proofs |
| 4 | O-2 Account Sheet · C-4 BasisLens · C-6 ExceptionBudget | New surfaces on a settled foundation |

Nothing above removes an API. New exports: `PeriodPosture`, `TieBar`, `ExceptionBudget`, the Account Sheet DC, and a `proof` prop on `AmountCell`; the rest is organization and adoption of what already exists.

**Card:** `docs/accounting-interface-proposal.card.html` · **Prior art:** `UX_DEPTH_4_1.md` (method), `templates/_strategy-executor/` (spine precedent), `guidelines/OPERATING_LANGUAGE.md` (rule-of-use format).
