# Browser Workstation UX Review and Improvement Plan (2026-11)

> **Status:** Proposed · **Created:** 2026-11-06 · **Owner lane:** Workstation Shell and UX
> **Builds on:** UI Phase 0–2 (merged 2026-11-04/05: font-fallback fix; A1–A5 task-first routes,
> one-row masthead, Today front door, single StatusBadge; B2–B4 shared formatting, single table
> system, three-tier type scale)
> **Depth:** findings and phased plan; no code in this change

## Scope and Evidence

Reviewed `src/Meridian.Ui/dashboard` (391 screen files, the shared component kit, design tokens)
and a fresh fixture-backed screenshot capture of 8 key routes from `main` on 2026-11-06 (8/8
succeeded). The checked-in catalog in `docs/screenshots/web/` dates from 2026-10-28 and predates
Phase 0–2; Phase 4 regenerates it. This plan does not re-propose Phase 0–2 work.

**Already working (build on these):** the seven-workspace sidebar with attention counts; the Today
hero ("What needs an operator decision now"); queue → detail → action layout on Close Cockpit and
Reconciliation; the one-line posture strip; the shared table with sorting, column visibility and
keyboard row navigation.

## Proposals

| # | Proposal | Area | Priority |
|---|---|---|---|
| P1 | Headline numbers agree across screens | Clarity, trust | Critical |
| P2 | Fix the detail panes | Polish, clarity | High |
| P3 | Today queue without requiring a scope | Clarity, function | High |
| P4 | Limit monospace to numbers and IDs | Polish | High |
| P5 | One reading order per workspace page | Hierarchy | Medium-high |
| P6 | Tidy status language and badge use | Clarity, polish | Medium |
| P7 | Table overflow and density | Polish, function | Medium |
| P8 | Bulk actions and assignment in queues | Function | Medium |
| P9 | Charts where finance users expect them | Function, visual | Medium |
| P10 | "What changed since I last looked" | Function | Medium-low |

### P1. Headline numbers agree across screens — Critical

**Problem.** KPI strips answer the same question differently: Settings shows "Open positions 5",
Portfolio shows 2, Trading's posture strip shows "1 positions"; Settings says "Providers online
3/4" while Data says "Providers healthy 2". Deltas (`+1`, `+3`, `0%`, `+2.1%`) state no
comparison basis.

**Solution.**
- Feed every strip number from the shared read model in `Meridian.Ui.Services`, not per-screen
  fixtures or view models.
- Give each KPI a fixed definition: label, scope, as-of time, comparison basis.
- Show the basis on every delta ("+1 vs yesterday's close") and as-of time on hover.
- Hide a delta with no baseline instead of rendering `0%`.
- Remove strips that do not belong to the workspace (positions and storage health on Settings).

**Affected:** KPI strips on Trading, Accounting, Strategy, Data, Settings, Portfolio; Today counts.

### P2. Fix the detail panes — High

**Problem.**
- Words broken mid-word (Trading: "Interacti / ve / Brokers").
- Right-aligned values in narrow two-column tiles wrap onto three lines ("Jan 1, / 2026, / 00:00 UTC").
- Raw IDs as values (Accounting "Evidence status" shows a UUID).
- Three labels for one thing: "Selected from …" pill, "DETAIL" chip, "SELECTED WORK" eyebrow; on
  Reconciliation the title sits flush against the card border.
- Trading Orders stacks orders and fills panes that mostly repeat the selected row.

**Solution.**
- One `DetailPane` primitive with a single heading line: object type, name, status badge.
- Key-value list that collapses from two columns to one under ~360px; wrapped values align left
  and never break mid-word.
- Dates on one line using the B2 short format.
- IDs move to a copyable "Technical details" disclosure.
- Show only fields not already in the selected row, plus the next action; orders and fills share
  one pane keyed to the last-selected row.

**Affected:** Trading (Orders, Positions), Portfolio, Accounting (Close Cockpit, Casework, Ledger
and trial balance), Reporting, Settings.

### P3. Today queue without requiring a scope — High

**Problem.** Today says "6 ranked items" and "1 blocked and 5 review", but the Finance queue only
shows "Choose the scope for today's queue" until a scope is picked. Five posture cards take the
space above the fold; the Blocker card repeats the hero.

**Solution.** Show the combined ranked queue by default (workspace, owner, age, "go" action); keep
scope as an optional filter chip; merge posture cards into the shared posture strip and drop the
Blocker card; add "Assigned to me" and "Unassigned" quick filters.

**Affected:** `daily-control-tower-screen.tsx`, sidebar counts.

### P4. Limit monospace to numbers and IDs — High

**Problem.** Monospace is still applied to key-value labels and text values, breadcrumb links, date
cells, and half of mixed cells (Reporting Activity: "1 template · 1 run · latest **No period
confirmed**"). Dashboard source has 1,224 `font-mono` uses. Small-caps sentence-case eyebrows
("Wʜʏ ɴᴏᴡ") read as a rendering bug.

**Solution.** Monospace with tabular figures only for amounts, quantities, prices, IDs and times
inside tables; everything else in the body font; replace small-caps eyebrows with the B4 caption
tier; add a ratchet in `scripts/design-system-ratchets.mjs` holding non-numeric `font-mono` to a
shrinking baseline.

### P5. One reading order per workspace page — Medium-high

**Problem.** Each page has two titles (workspace H1 plus the real page title), and the order of KPI
strip, posture strip, banners and section header varies (Reporting opens with a lone "Evidence"
button; Portfolio puts its title above the strip; Accounting below).

**Solution.** Breadcrumb carries the workspace label; the page title is the only H1. Fixed order:
title and purpose → action bar → KPI strip → posture → blocking banners → content, encoded in
`workspace-workbench-shell`.

### P6. Tidy status language and badge use — Medium

**Problem.** Caps "AWAITING APPROVAL" beside sentence-case "Period confirmation required"; a red
"OPEN" chip under a "BLOCKED" header (Close Cockpit); Portfolio's Mark readiness column leads with
"REVIEW REQUIRED" plus "Observed Unknown · age Unknown", pushing Symbol to column two.

**Solution.** One status vocabulary per object type (break, report run, position mark) over a fixed
word set (ready, review, blocked, stale, unavailable); identifier first, status after; collapse
unknowns into one "No mark observed" hint.

### P7. Table overflow and density — Medium

**Problem.** Reconciliation's table overflows its card ("Column" button, clipped "CASE STATUS");
no totals rows on positions or fills; no CSV export (only 2 screens import `lib/csv`); 42 files
still hand-render `<table>` instead of `DenseDataTable`.

**Solution.** Contained horizontal scroll with a sticky first column and pinned toolbar; optional
totals/subtotals footer; "Export CSV (visible columns)"; migrate remaining hand-built tables behind
a ratchet.

### P8. Bulk actions and assignment in queues — Medium

**Problem.** Break, exception, approval and triage queues act on one item at a time; Assign always
opens a dialog.

**Solution.** Row checkboxes in `DenseDataTable` with a bulk bar (Assign to…, Assign to me, Attach
evidence, Route to approval), each through the existing governed confirm flow. Never bulk-resolve
without a per-item decision note.

**Affected:** Accounting (Casework, Exceptions, Approvals), Reporting triage, Today queue.

### P9. Charts where finance users expect them — Medium

**Problem.** The chart kit (`EquityCurve`, `DrawdownChart`, `Sparkline`, `Histogram`,
`CorrelationHeatmap`) is used only in Strategy, Portfolio, Live Quotes and Covered Call.

**Solution.** Break count and aging histogram on Casework; cash/NAV sparkline in the Portfolio KPI
strip; close-progress burn-down on Close Calendar; report-family on-time trend in Reporting. Use
existing primitives and shared read models; follow the dataviz palette in both themes.

### P10. "What changed since I last looked" — Medium-low

**Problem.** Only Reporting has change-since-review logic (`lib/report-change-since-review.ts`).

**Solution.** Per-viewer last-viewed marker per workspace and queue; "3 new since 09:40" plus a dot
on new rows; a "Changes" filter.

### Smaller fixes

- Seeded-data banner (~46px), PAPER/SEEDED chip and masthead BLOCKED chip say the same thing;
  collapse the banner into the chip once seen.
- "Cancel all" sits as a prominent red button beside "New order"; move it to an overflow menu with
  confirmation.
- Large empty areas below short pages (Today, Settings) could hold the queue or recent activity in
  a two-column layout.

## Phased Plan

Phases continue the Phase 0–2 numbering already in history.

### Phase 3 — Trust and readability (~1 week) · P1, P2, P4, Cancel-all

1. KPI definition registry in the shared read model; move all strips onto it.
2. `DetailPane` primitive; migrate Trading, Portfolio, Accounting, Reporting, Settings.
3. Monospace-scope ratchet; convert key-value tiles, breadcrumbs, date cells.
4. "Cancel all" into an overflow menu with a confirm step.

**Acceptance criteria**
- On one fixture, any same-named KPI shows the same value on every route (vitest cross-route
  assertion).
- Every delta shows its basis; none renders without a baseline.
- At 1440×1100 and 1024px width: no mid-word breaks or wrapped dates in detail panes; no raw UUIDs
  outside "Technical details" (fresh screenshots).
- Non-numeric `font-mono` count below recorded baseline; ratchet fails CI on increase.
- `npm run test`, `npm run build`, `npm run ratchets` and axe a11y suites pass.

### Phase 4 — Navigation and hierarchy (~1 week) · P3, P5, P6

1. Today: combined queue by default, scope as filter, posture cards merged into the strip,
   "Assigned to me" filter.
2. Fixed page order in `workspace-workbench-shell`; single H1.
3. Status vocabulary map per object type in `shared-tone-mappings`; identifier-first columns.

**Acceptance criteria**
- With no scope selected, Today lists every ranked item and the count equals the sum of sidebar
  badges.
- Every route has exactly one `h1`; section order matches the shell contract (snapshot test).
- No column mixes caps and sentence-case status labels; unknown marks show one hint.
- `docs/screenshots/web` regenerated and the freshness validator passes.

### Phase 5 — Tables and throughput (1–2 weeks) · P7, P8

1. Contained scroll, sticky first column, totals footer, CSV export in `DenseDataTable`.
2. Migrate remaining hand-built tables behind a ratchet.
3. Multi-select with a governed bulk bar on Casework, Exceptions, Approvals, Reporting triage.

**Acceptance criteria**
- No table toolbar or header clipped at 1280px.
- Totals equal the sum of visible filtered rows (unit test).
- CSV export has visible columns in display order, formatted by B2 formatters.
- A bulk action yields one governed confirmation and one audit entry per item; bulk resolve
  requires a per-item note.
- Keyboard: Space toggles a row, Shift+Arrow extends selection; axe reports no violations.

### Phase 6 — Insight (1–2 weeks) · P9, P10

1. Break-aging histogram, close burn-down, NAV/cash sparkline, report on-time trend.
2. Per-viewer last-viewed markers, "N new since…", Changes filter.

**Acceptance criteria**
- Each chart is fed from a shared read-model endpoint, has an accessible data-table alternative,
  and passes light/dark contrast checks.
- Chart values reconcile with the table beneath on the same fixture.
- New-since markers survive reloads, stay per-viewer, and clear when viewed.
- Before closing, file matching WPF-parity backlog items over the same contracts
  (`W8-WPF-PARITY-001`, see [WPF / Web-UI Alignment Plan](wpf-web-ui-alignment-plan.md)).

## Progress

**2026-11-10 — operator-copy pass (parts of P2 and P6).** Checked against a fresh fixture-backed
capture of all 80 routes rather than the 2026-10-28 catalog.

- P2 raw IDs: display names replace GUID-shaped identifiers on the close cockpit (blocker label and
  evidence count; record IDs move under Audit details), the trial balance heading, the
  factor-paydown scope line and the loan-book facility selector. `lib/identifiers.ts` is the shared
  helper.
- P6 unknown marks: a position with no shared mark assessment shows one "No mark observed" hint in
  the table cell and one row in the detail pane.
- Counts agree with their nouns on the eight routes that rendered "1 rows"-style text, and on the
  workspace posture and navigation summaries.
- Status agreement: the status bar repeats a provider-trust warning beside Sync, and the Control
  Tower evidence line names only evidence correlated with the leading row.
- Guard: the screenshot harness fails a route whose rendered main content shows a raw identifier
  or a count of one before a plural noun.

Still open from P2: Operations Continuity names a close workflow by period and fund-account
identifier because `OperationsContinuityWorkflowSummary` carries no account display name. Adding
one is a shared-contract change for both workstations; the route holds the only
`allowedContentIssues` entry until then. The detail-pane primitive, wrapped dates and mid-word
breaks in P2 are untouched.

## Recommended Next Step

Start with Phase 3 item 1: disagreeing KPI numbers are the largest trust problem and an
inexpensive fix.
