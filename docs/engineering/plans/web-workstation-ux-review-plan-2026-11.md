# Browser Workstation UX Review and Improvement Plan (2026-11)

> **Status:** In progress (Phase 3 started 2026-11-10; trust fixes 2026-11-12) · **Created:** 2026-11-06 ·
> **Extended:** 2026-11-10, 2026-11-12 (verified review corrections) · **Owner lane:** Workstation Shell and UX
> **Builds on:** UI Phase 0–2 (merged 2026-11-04/05: font-fallback fix; A1–A5 task-first routes,
> one-row masthead, Today front door, single StatusBadge; B2–B4 shared formatting, single table
> system, three-tier type scale)
> **Depth:** findings (P1–P10), the 2026-11-10 extended review (C1–C6, V1–V7, F1–F8) and the
> Phase 3–7 roadmap that replaces the original Phase 3–6 plan

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

## Extended Review (2026-11-10)

A second pass over the same routes after the 2026-11-10 copy pass, checked against a fresh
fixture-backed capture of all 80 routes. It adds clarity (C), visual-polish (V) and functionality
(F) proposals; several extend P1–P10 and say so. Priority: 🔴 critical · 🟠 high · 🟡 medium ·
⚪ low or needs checking.

### A. Clarity

#### C1 🔴 Status indicators should never contradict each other (extends P1)

**Problem.** Before any work content, a page can show up to six status layers: the PAPER/SEEDED
chip and the BLOCKED chip in the top bar; the full-width "Seeded demo data" banner; a row of
headline numbers (KPI strip); a row of Source / Scope / Freshness / Completeness pills (the posture
strip); a blocker banner; and the status bar at the bottom. On Trading, the first table starts
about 435px down a 1100px screen. Worse, the layers disagree:

- The bottom bar says "Sync up to date" while Portfolio's posture strip says "Freshness: Stale ·
  187d ago".
- The header says "No scope selected" while Trading's posture strip says "Scope: Ready ·
  DU1009034", and Portfolio's says "All brokerage accounts".
- Live Quotes shows "Connection: Connected" next to "Coverage 0/0 live".
- Strategy Lab's KPI strip says "Runs 24", but the Strategy overview list shows "2 of 2 loaded".

**Proposal.** Give each kind of status one fixed place:

| Layer | What it shows | Where |
|---|---|---|
| Environment | PAPER/LIVE, seeded data, signed-in operator | Top bar only. The seeded banner shrinks to the chip once it has been dismissed. |
| Page trust | Source, scope, freshness, completeness | One strip in one format. Today Trading uses a row of pills while Run Status uses four large cards. |
| Blockers | The single most important blocker, with a "+N more" link | One banner slot |
| System heartbeat | Sync and provider status | Bottom bar, calculated from the same freshness check the page strip uses |

Scope appears in one place only: the header scope picker. The posture strip shows the scope's
name, not a separate "Ready" verdict.

**Affected:** every route; `workstation-topbar`, `workstation-status-bar`, `app-shell.trust-strip`,
`data-provenance-banner`.

**Benefit:** operators can trust the status they see first, and the work starts 150–200px higher.

#### C2 🟠 One name per destination

**Problem.** The sidebar label, the breadcrumb and the page title often disagree:

| Sidebar label | Page title |
|---|---|
| Adjustments | Journal Entry |
| Casework | Reconciliation Casework |
| Strategy Lab | "PlotTool workstation" |
| Market data | Live Market Data Workspace |

On Market data, the Watchlist and Price alerts tabs do not appear in the sidebar at all. Every page
also repeats "<Workspace> Workstation" as its title and starts the breadcrumb with "Workstation".

**Proposal.**
- Read each route's label from the existing route registry and use it for the sidebar, the
  breadcrumb, the page heading, the browser tab title and the command palette.
- Drop the "Workstation" breadcrumb root and the repeated "<X> Workstation" title; the page heading
  names the destination.
- Put in-page tabs in the URL and show them as sidebar children.

**Benefit:** the destination clicked is the page landed on; deep links and saved views stay
meaningful.

#### C3 🟠 Make Accounting navigation lighter and give it one work queue

**Problem.** Accounting has 20 sidebar items in 5 groups; expanded, it pushes Reporting, Strategy
and Data near the bottom of the screen. Operators also learn at least four overlapping queues:
Today, Operations continuity, Casework and Exceptions/Approvals.

**Proposal.**
- Move Accounting's sub-pages into an in-page secondary rail or tab bar, with remembered expansion.
- Merge the queues into one Accounting work queue that filters by type (break, exception, approval,
  close task), owner and due date.
- Keep the existing routes as preset filters of that queue so deep links still work.

**Benefit:** less to scan, and one answer to "what do I work on next?"

#### C4 🔴 Speak operator language, not system language (follows the 11-10 copy pass)

**Problem.**
- A raw API URL is shown to users. Journal Entry displays "Recurring queue unavailable: Request
  failed for /api/ledger/journal-automation/recurring/occurrences?fundProfileId=default-fund&…".
  The text came from `lib/api-errors.ts`.
- Internal or migration wording remains: "Legacy pack mutations are retired. Continue … from the
  governed run" (Run Status); "Caller-visible Reporting records"; "Strategy Lane · PlotTool"; "No
  Strategy PlotTool payload is available. Connect a governed strategy analytics endpoint…" (three
  times on one screen); "Data Lane".
- Raw IDs appear as values: `default-fund`, `entity-fund-alpha`, `run-dev-1`.

**Proposal.**
- An error presenter that maps the HTTP status and error type to a plain sentence, a Retry action
  and a collapsed "Technical details" section (request path and correlation ID).
- Extend the screenshot harness's rendered-text check to fail on `/api/` paths, internal terms
  ("Lane", "payload", "mutation", "caller-visible") and slug-shaped IDs in the main content area.

**Benefit:** fewer support questions, and no leaked internals.

#### C5 🟠 Show time and urgency consistently

**Problem.**
- Trading shows times in ET ("09:42:00 ET") while Accounting and Reporting show UTC. There are
  about 10 hard-coded `timeZone: "UTC"` formatters and no timezone preference.
- Date-only fields show a fake time ("Jan 1, 2026, 00:00 UTC").
- Due dates have no urgency: "Due Jul 14, 2026" looks the same tomorrow or months overdue.

**Proposal.**
- A time-display policy in the shared formatters: date-only values render as dates; timestamps
  render in the operator's chosen timezone with the zone shown; due dates carry a relative chip
  (`Overdue 3d` danger, `Due today` warning, `Due in 2d`).
- Timezone and date-format settings.

**Benefit:** fewer cut-off and SLA mistakes; timestamps compare across workspaces.

#### C6 🟡 Overview pages that summarise their workspace

**Problem.**
- The Strategy and Data overviews are generic Work item / Context / Action tables with an identical
  "Review run" or "Open Backfill queue" button on every row.
- Settings › Preferences shows provider and position KPIs unrelated to preferences (P1 noted this).
- Chart components exist (equity curve, drawdown, heatmap, sparkline) but only 4 screens use them.

**Proposal.** Build overviews for their workspace's purpose. Strategy: a run leaderboard with
equity sparkline, Sharpe, max drawdown and promotion stage. Data: a provider × capability ×
freshness health matrix plus backfill progress bars. Portfolio: NAV and exposure trend. Settings ›
Preferences: remove the KPI strip. Overlaps P9.

### B. Visual polish

#### V1 🟠 One shared detail pane (P2 detail-pane work is still open)

**Problem.** Words still break mid-word ("Interacti / ve / Brokers"); dates still wrap onto three
lines inside narrow tiles; one selection gets up to three labels ("Selected from open orders" +
"DETAIL" + "WORKING"); Trading stacks two detail cards, one for each table.

**Proposal.** One `DetailPane` per page, showing whatever was selected last; labels sit above
values once the pane is narrower than about 360px; no two-column tile grid for values longer than
two words; one heading line: object type · ID · status badge.

#### V2 🟠 Typography: limit monospace and pick fonts deliberately (extends P4)

**Problem.** About 1,248 monospace references; uppercase, letter-spaced monospace labels ("OPEN
BREAKS", "DRAFT QUEUE", "THEME") sit next to sentence-case labels. Fonts load only through
`local()` (Segoe UI and Cascadia), so Linux and macOS fall back to heavier system fonts. Only 45
files use `tabular-nums`.

**Proposal.** Monospace only for numbers, IDs and code; one eyebrow style (small caps in the sans
font); either self-host a variable sans and mono pair with tabular figures (check the licence
first) or officially adopt the system font stack and test it on all platforms; `tabular-nums` for
all numeric cells through the table and KPI primitives.

#### V3 🟡 One badge system (P6; adoption is incomplete)

**Problem.** At least four badge styles are visible: pill in sentence case ("Needs review"), boxed
uppercase monospace ("REVIEW REQUIRED", "OPEN"), dotted uppercase ("• AWAITING APPROVAL") and a
tinted chip ("Selected detail"). Some items show redundant pairs ("AWAITING APPROVAL" next to
"APPROVAL NEEDED").

**Proposal.** Only the existing `StatusBadge`, with a ratchet counting other badge styles, and one
status badge per object.

#### V4 🟠 Show financial numbers like a finance tool

**Problem.**
- In Ledger Explorer, debit and credit amounts are terracotta, underlined links. In finance, red
  reads as negative or loss, and the brand accent clashes with that meaning.
- Journal Entry inputs show "1250" with no thousands separator.
- There is no running debit / credit / difference total at the bottom of the journal grid; balance
  shows only as a chip at the top.
- 7 `formatCurrency`, 11 `formatCount` and 6 `formatTimestamp` functions still exist alongside the
  B2 shared formatters.

**Proposal.** Amounts as neutral text with a proof icon or hover affordance that opens the Number
Passport; colour only for sign and variance; a negative-number style setting (minus sign or
parentheses); a sticky totals row on the journal grid; format numbers in inputs on blur; remove the
local formatter copies behind a ratchet.

#### V5 🟠 Disabled and destructive controls

**Problem.** Disabled primary buttons ("Submit approval", "Add symbol set") are washed-out accent
colour with unreadable text, and nothing says why they are disabled. "Cancel all" is still a
prominent red button next to "New order".

**Proposal.** A disabled style that keeps at least 3:1 contrast on the button edge and readable
text; the reason inline or in a tooltip ("Validate before submitting"); "Cancel all" in an overflow
menu with a confirmation step.

#### V6 🟡 Page layout templates and breakpoints

**Problem.** Run Status uses a half-width column and leaves the right half empty; Today,
Preferences and the Data overview have large dead areas. Strategy Lab nests cards three deep, each
repeating the "not connected" message. The CSS uses six breakpoints: 720, 900, 980, 1100, 1199 and
1279px.

**Proposal.** Four page templates (Queue–Detail, Overview dashboard, Form/Settings, Document);
cards nested no more than two deep; three named breakpoint tokens.

#### V7 ⚪ Focus outline after navigation (likely; needs checking)

**Problem.** The Ledger screenshot shows a heavy dark outline around the whole main area, probably
the focus the app sets on the main region after a route change.

**Proposal.** Keep moving focus to the main region (it helps keyboard and screen-reader users), but
show the outline only for `:focus-visible` and keep the skip link's outline.

### C. Functionality

| # | Priority | Proposal | Problem → solution → benefit |
|---|---|---|---|
| F1 | 🟠 | "My work" inbox across workspaces (extends P3, P8) | Work is spread across six workspaces' queues, and Today needs a scope selected first. → One inbox with assignment, aging, snooze and bulk actions, scope as a filter, and an "Assigned to me" default. → Each operator starts the day in one place. |
| F2 | 🟠 | Shared as-of date and period in the scope picker | Ledger has its own period picker (June 2026), the Close Cockpit shows 2026-05, and Reporting says "No as-of date retained". → Period and as-of date in the operating scope so every workspace answers for the same date, with a visible "viewing as of…" mode for past dates. → Numbers agree, and audits can replay a past date. |
| F3 | 🟡 | Drill-through everywhere | Number Passport and proof drawers exist but appear unevenly. → Any symbol, account, entity, journal or KPI figure opens the same passport drawer. → Every number can be traced to its source. |
| F4 | 🟡 | Period-over-period variance views | There is no comparison view. → Prior-period columns, variance and variance %, and threshold highlighting on trial balance, NAV, P&L attribution and cash ladder. → The core controller and fund-accountant review step. |
| F5 | 🟡 | Table power features (extends P7) | `lib/csv.ts` is used only by SQL query. → CSV/XLSX export of visible columns using the shared formatters, a totals row, a sticky first column, saved column sets per view, and filter chips. |
| F6 | ⚪ | Live Quotes start state | The page opens empty, the "NO DATA · NO DATA" chips have no labels, and connection and coverage are mixed together. → Seed the list from the watchlist, label every chip, and separate connection from coverage. |
| F7 | ⚪ | Record activity timeline (extends P10) | Comments, approvals and edits on a record are not shown in one place. → A per-record timeline in the detail pane plus "N new since you last looked". |
| F8 | 🟡 | Fuller Preferences | The page has only theme and density. → Timezone, date and number format, negative-number style, default landing page, per-workspace density and notification rules. |

## Phased Plan

Phase numbers continue the Phase 0–2 sequence already in history. This roadmap replaces the
original Phase 3–6 plan: its items are carried forward under the proposals they belong to (P1 KPI
definitions and "Cancel all" in Phase 3; P2 and P4 in Phase 5 as V1 and V2; P3, P7 and P8 in
Phase 6; P5 in Phase 4 with C2; P6 in Phase 5 as V3; P9 and P10 in Phase 7).

Each phase ends with:

- `npm --prefix src/Meridian.Ui/dashboard run test`, `run build`, `run lint` and `run ratchets`;
- the axe accessibility suites;
- regenerated `docs/screenshots/web` plus the freshness validator;
- WPF-parity backlog items filed under `W8-WPF-PARITY-001` for any change to a shared read model
  (see [WPF / Web-UI Alignment Plan](wpf-web-ui-alignment-plan.md)).

### Phase 3 — Trust and coherence (about 1.5 weeks) · C1, C4, V5, P1 KPI definitions

1. Status layer model: top bar for environment, one posture strip, one blocker slot, and a bottom
   bar fed from the same freshness check.
2. A KPI definition registry in the shared read model (P1 item 1), with the comparison basis shown
   on every change figure.
3. The error presenter and the extended rendered-text check.
4. Disabled-button style with reasons, and "Cancel all" moved to an overflow menu.

**Done when**
- On a fixture with stale data, no route shows both "up to date" and "stale", and scope is stated
  in one place. A vitest test across routes checks both.
- KPIs with the same name show the same value on every route, and no change figure appears without
  a baseline.
- No rendered main content contains `/api/`, a slug ID or a banned internal term; the screenshot
  harness fails the route otherwise.
- With a provider warning on Trading, content starts at or above 300px at 1440×1100.
- Disabled buttons pass axe contrast checks, and every disabled primary button has an accessible
  reason.

### Phase 4 — Wayfinding and time (about 1 week) · C2, C3, C5, F8 (time-zone part)

1. Labels read from the route registry; drop the "Workstation" breadcrumb root and the repeated
   title; in-page tabs in the URL.
2. Accounting secondary navigation; one Accounting work queue with preset filters for the old
   routes.
3. Time policy in the shared formatters (date-only, timezone, relative due chips) plus timezone and
   date-format settings.

**Done when**
- For every route, the sidebar label, breadcrumb leaf, heading and document title match (snapshot
  test).
- Expanding Accounting adds no more than 7 sidebar rows, and every old Accounting URL resolves to
  an equivalent filtered queue.
- No hard-coded `timeZone:` remains outside the formatter module (ratchet), and date-only fields
  never render a time.
- Overdue items show a danger chip with a day count, checked with frozen-time tests.

### Phase 5 — Visual system convergence (about 2 weeks) · V1, V2, V3, V4, V6, V7

1. A `DetailPane` primitive, migrated into Trading, Portfolio, Accounting, Reporting and Strategy.
2. Monospace limited to numbers, IDs and code; one eyebrow style; a font decision implemented;
   `tabular-nums` built into the table and KPI primitives.
3. Only `StatusBadge` for status; neutral amounts plus a proof affordance; journal totals row;
   negative-number style setting.
4. Four page templates and three breakpoint tokens; cards no more than two deep; outline only on
   `:focus-visible`.

**Done when**
- At 1440 and 1024px wide: no mid-word breaks, no dates wrapped onto more than one line, and one
  selection label per pane (screenshot review plus a DOM check for word-break on values).
- Non-numeric monospace usage and badge styles other than `StatusBadge` are below their recorded
  baselines and ratcheted to zero for the migrated routes.
- No financial amount uses danger or accent colour unless it is negative or a variance.
- The journal grid shows a debit / credit / difference footer that equals the sum of its lines
  (unit test).
- Breakpoint literals appear only in the token file (lint).

### Phase 6 — Throughput (about 2 weeks) · F1, F5, F3, P8

1. A "My work" inbox across workspaces, filtered by scope, with assignment, aging and snooze.
2. Governed bulk actions on Casework, Exceptions, Approvals and Reporting triage.
3. CSV/XLSX export, totals, sticky first column and saved column sets in `DenseDataTable`.
4. Drill-through to the passport drawer from every financial figure and entity cell.

**Done when**
- With no scope selected, the inbox count equals the sum of the sidebar badges.
- A bulk action produces one confirmation and one audit entry per item, and bulk resolve requires a
  note for each item.
- Exported files contain the visible columns, in display order, formatted the same way as the
  screen; totals equal the visible filtered rows.
- Every KPI tile and amount cell on migrated routes opens a passport drawer by keyboard and
  pointer, with no axe violations.

### Phase 7 — Insight (about 2 weeks) · C6, F2, F4, F7, P9, P10

1. Purpose-built Strategy, Data and Portfolio overviews using the existing chart components.
2. Period and as-of date in the operating scope, including a past-date view.
3. Variance columns on trial balance, NAV, P&L and cash ladder.
4. Per-record activity timeline and "new since you last looked" markers.

**Done when**
- Each chart comes from a shared read-model endpoint, has an accessible data-table alternative, and
  matches the table beneath it on the same fixture.
- Changing the as-of date in scope changes the period on Ledger, Close Cockpit and Reporting
  together; a past-date view shows a persistent banner and disables writes.
- Variance equals current minus prior, and the variance-% rules for zero and sign are unit-tested.
- "New since" markers survive reloads, are kept per viewer, and clear when the item is viewed.

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

**2026-11-10 — headline-figure trust rules (P1, browser side).** `@/lib/metric-trust` holds the
rules and the shared `StatStrip` and `MetricCard` apply them, so a screen cannot opt out.

- Source: every workspace strip (Trading, Portfolio, Accounting, Strategy, Data, Settings) prints
  its source and as-of time under the row, with scope where the page has one. The shell supplies
  the as-of time and data label (seeded, simulated, unverified) for strips fed by the workstation
  load; Trading and Portfolio use their own broker and refresh evidence.
- Deltas: a bare change is shown only with a stated baseline. The read models still send
  placeholders (`0%`, `+N`, `+session`) with no comparison behind them, so the strips now show
  none; "N new since your last visit" and descriptive captions are unchanged.
- Stale data: figures fed by stale or unverified data are muted, carry a clock and say why on
  hover and to screen readers. Portfolio's "Stale · 187d ago" now reaches its headline figures; a
  failed workstation refresh marks every workspace-fed strip.
- Disagreement: Portfolio shows "Why these differ" when the header strip (open positions in the
  workspace read model) and the Portfolio Explorer (records retained for a run) report different
  exposure, unrealized P&L or position counts, with both values and each source.
- Number style: `formatPnl`, `pnlToneForValue` and `normalizeFinancialLabel` in `@/lib/format`;
  "P&L" replaces "PnL" in browser labels and in the portfolio explorer read model. Two new
  ratchets hold the rest: `handRolledNumberFormatting` (88 `.toFixed(` calls in 27 screen files)
  and `localMoneyFormatters` (24 definitions in 13 files).

Still open from P1:

- The KPI definition registry (Phase 3 item 2) and the cross-route same-value assertion. Strips
  still take values from each workspace's read model; this change makes the source visible, it
  does not make the numbers agree.
- Real baselines and a page-level "Compare to" control. No read model carries a comparison period;
  `WorkstationMetricCard.Delta` needs a baseline (or a prior value) before either can exist. The
  browser `MetricSnapshot.baseline` field is ready to receive it.
- Variance direction. The break queue's `Variance` is an absolute amount for statement breaks and
  signed for shadow-NAV and provider breaks, and the reconciliation measures use actual minus
  expected (statement minus book). The browser renders it with a sign, so a statement break always
  reads "+". Labelling a direction needs the queue item to carry its basis first.
- WPF still spells "PnL" in its own view models (`RunRiskViewModel`,
  `StrategyRunPortfolioViewModel`, `QuantScriptViewModel`, `StrategyRunBrowserViewModel`).

**2026-11-10 — detail panes (P2).** `DetailPane` is the shared primitive; `EntitySummary` applies
the same heading and rows, which carries the change to the panels already built on it.

- One heading line (object type, name, one status badge) on Trading positions and the shared
  order/fill pane, Portfolio holdings and brokerage positions, the Reporting work pane and the task
  workbench used by Settings, Strategy and Data. The "Selected from …" pill is no longer drawn for
  any panel, and the "Detail" chips are gone.
- Values are left-aligned and whole. Rows put the label beside the value and stack below about
  290px; figures and dates stay on one line. The right-aligned two-column tiles on Trading,
  Portfolio, Strategy, Reporting, Settings, the journal-entry detail and the readiness console use
  the shared row. The reconciliation inspector's title is no longer flush against its border.
- Opaque identifiers in any field list move to a copyable "Technical details" disclosure.
- Trading orders and fills share one pane keyed to the table selected last, with Cancel order as
  its next action; panes omit the columns the selected row always shows.
- Checked at 1440×1100 and 1024px on the Trading, Portfolio, Accounting, Reporting, Settings and
  Strategy routes: no right-aligned values, wrapped dates or figures in any detail panel; the only
  values that may wrap mid-token are identifiers.

Still open from P2:

- Panels with bespoke bodies keep their own wrappers and only took the shared rows: Portfolio run
  evidence and brokerage account, the Security Master panels, the corporate-action case workspace
  and the control-tower evidence summary.
- "Show only fields not in the selected row" is applied where the table's always-visible columns
  are known (Trading, Portfolio). Other panes still repeat row values.
- Dates arrive preformatted from view models; the panes keep them on one line but do not reformat
  them, so two routes can still show different date styles.
- The checked-in screenshot catalog has not been regenerated.

**2026-11-10 — Phase 3, first increment (C4, V5, the freshness and scope parts of C1).** Checked
against a fixture-backed capture of all 80 routes before and after.

Done:

- **C4 error presenter.** `ApiError.message` is now operator copy (the service's reason, or a plain
  sentence for the status) and never carries the request path or status code, which removes the
  leak from every call site that renders `error.message`. `presentApiError` adds whether Retry can
  help and the status, request path and correlation identifier as technical details; `ErrorNotice`
  renders them with the details collapsed. The Recurring journals queue uses it. Unreadable
  responses raise `ApiResponseFormatError` instead of a message with the path in it.
- **C4 rendered-text check.** The harness fails a route on `api-path`, `internal-term` ("lane",
  "payload", "mutation", "caller-visible") and `slug-identifier`. Copy was rewritten on the 20
  routes that showed the terms ("Data Lane", "Strategy Lane · PlotTool", "Caller-visible Reporting
  records", "Legacy pack mutations are retired", the PlotTool "payload" messages and others).
- **C1 status layers.** The status-signal consolidation that merged alongside this increment
  supplies most of C1: one rule (`lib/status-signals.ts`) and one shell attention state feed the
  masthead pill, every page status row, Today and the status bar; a page row reads "All sources
  current" or names only the facts that are not ready. This increment sits on top of it.
- **C1 freshness.** The shell attention state covers shell-wide sources, not the freshness of the
  data on a page. The page status row reports the freshness fact it renders to the shell; when it
  needs review or is blocked the status bar says "Sync connected" and repeats the row's words as
  a Freshness item. Portfolio now reads "Stale · 187d ago" in both places. The harness's
  `status-contradiction` rule checks every route.
- **C1 scope.** Trading and Portfolio label a brokerage account "Account", so the row does not
  state a second "scope" beside the header's fund picker. The earlier change that dropped the
  "Ready" verdict from the scope fact is superseded: the new status row does not list ready facts.
- **V5.** One disabled style for every button variant (neutral fill, dashed edge, muted label at
  full opacity; label 4.8:1, edge at least 3:1 on every surface, asserted in a unit test). The
  harness fails a route with a disabled primary button that carries no reason; the six it found
  now have one. "Cancel all open orders…" moved into a "More order actions" overflow menu and still
  opens the confirmation step.

Open in Phase 3:

- **KPI definition registry (P1 item 1).** Not started; see "Still open from P1" above. The
  headline-figure trust rules now hide a change figure that has no baseline, but same-named
  numbers still come from each workspace's own read model, so "Runs 24" can still disagree with
  "2 of 2 loaded". The registry is a shared read-model change and needs a matching
  `W8-WPF-PARITY-001` item.
- **Content start height.** The first Trading card started at about 435px when this increment was
  written. Two changes that merged alongside it bring it to about 350px: the page-header
  simplification (one header row, no "<Workspace> Workstation" title, a fund picker in place of
  "No scope selected") and the status-signal consolidation (the seeded banner becomes a stripe).
  Portfolio's first card now starts at about 290px. Reaching 300px on Trading still needs the
  single blocker slot.
- **One blocker slot.** Page status rows now share one format; blocker banners are not yet limited
  to one slot with "+N more".
- **Scope in one place.** Screens other than Trading and Portfolio still label a strip fact
  "Scope"; the cross-route check covers freshness, not scope.
- **Excused routes.** Fifteen routes hold `allowedContentIssues` entries for the new rules. Twelve
  name records by slug key because their read models carry no display name (approval inbox,
  configure, ledger, margin control, operations continuity, trial-balance detail, operator
  readiness, run detail, Alpaca advanced setup, strategy run ledger, strategy overview, trading
  overview); three Settings diagnostics pages list backend routes by design; Operations Continuity
  shows "lane" in server-written summaries. Display names and the "lane" rename are shared-contract
  changes for both workstations.
- **Raw `<button disabled>` elements** outside the `Button` primitive keep their own dimmed styles.
- **Catalog.** `docs/screenshots/web` was not regenerated in this increment; that is a phase-end
  step.

No shared read model changed in this increment, so no WPF-parity item was filed.

**2026-11-12 — Phase 3, trust fixes (latency, Today counts, setup state, placeholders).** A
follow-up review proposed these fixes. Each claim was checked by two independent reviewers against
source and a fresh fixture-backed capture of all 82 routes, and only the claims that held up were
built. The before and after captures were compared route by route.

Done:

- **Latency is a duration.** The Data overview's provider cards printed the last response time,
  or "Never", under "Latency" when a provider reported no latency. `buildProviderRow` now keeps
  two values. `latencyText` holds only the reported latency, or "Latency not reported" (the same
  sentinel the server writes). `latencyOrLastGoodText` holds the old value and feeds only the
  "Trust / last good" column and its CSV export, which are unchanged. The row's accessible name and
  the trust fields follow `latencyText`. The summary card's description now reads "Last successful
  call or verification." The "Latency / Last Response" label stays the same as WPF's.
- **Today lists what its badge counts.** The sidebar badge, the status row and the status bar
  already counted one list and agreed with each other. That list was visible only while the
  operator inbox was loading. Today now shows it as "Finance queue" (6 items, the ones the badges
  count) above "Operator inbox" (3 triage items from the shared host). Each count names its unit:
  "6 items in the finance queue", "3 of 3 inbox items", and "+5 more issues" in the status bar. The
  unit hint is in the nav link's tooltip, because the badge itself takes no pointer events. The
  Today link now spans its row, so clicking the badge navigates.
- **Account setup is not a failure.** In a paper environment with no fund account in scope,
  Trading shows a neutral "Choose a fund account" state with one "Choose account" action, and the
  sub-pages show a neutral row. Live, unknown or failed states keep the red "Live trading blocked"
  block. The environment comes from the trading read model, which reports "paper" whenever the
  preferred run is not live. The neutral state therefore lowers only the displayed severity: it
  unblocks nothing, and the copy still says live trading stays off.
- **An undeclared basis is stated once, not as a failure.** Strips show "Basis · Not declared by
  source" once. Cards omit the line, and the figure passport says "Basis not declared by source".
- **Harness.** Two new blocking content rules: `latency-not-duration` (a `Latency` label followed
  by a clock time or "Never") and `unavailable-placeholder` ("Basis unavailable", "Saved views
  unavailable"). A saved-views fixture ends the false "Saved views unavailable" notice on 22
  captures; the product copy is unchanged, because it is accurate when the store really fails.
  The Today capture now waits for the inbox. Neither rule needed an `allowedContentIssues` entry.

| Rendered copy (82 captures) | Before | After |
|---|---|---|
| "Basis unavailable" | 48 in 34 files | 0 |
| "Saved views unavailable" | 34 in 22 files | 0 |
| `Latency` followed by a time or "Never" | 3 | 0 |
| "Live trading blocked" with no account in a paper environment | 4 files | 0 |
| "loaded workspace items" / "authorized items ·" | 1 / 1 | 0 / 0 |

Not done, and why:

- **"Providers healthy 1/2" above five provider cards** and **Strategy "Runs 24" above "2 of 2
  loaded"** come from the capture fixture: `/api/workstation/data` lists two providers, and the
  server sends no "Runs" card. In production the provider populations differ only for operators
  without ManageCredentials. One population needs the KPI definition registry and a
  `W8-WPF-PARITY-001` item.
- **Showing a provider's measured latency** (Alpaca's trust snapshot carries `latency:p50=18ms`)
  needs a typed contract field.
- **Inbox count equals the sum of the badges** (Phase 6 F1) needs the server inbox to cover the
  workspace queue. The two lists are now labelled as different units instead.
- **Ledger "Entity name unavailable"** stays excused until the posted-ledger DTO carries display
  names.
- **A new three-level tone model** was not added. The existing primitives cover setup (empty
  state), review and blocked.

No shared read model changed, so no WPF-parity item was filed. Validation: full dashboard vitest
suite, `tsc`, lint, ratchets, the harness node tests, the production build on Node 24 (the
`.nvmrc` toolchain, which rebuilds the base bundle byte for byte), and the full 82-route capture.

### Verified corrections to the 2026-11-11 follow-up review

The follow-up review also restated several open items. Verifying them changed these facts, which
later phases should use:

- **Scope (C1, F2).** The header shows "No scope selected" on 78 of 82 routes again. Commit
  `38baf358` replaced the dock with `OperatingScopeChip` ("All funds"), and a later merge restored
  `WorkflowContinuityDock` in `app.tsx`, so the chip is now used only in tests. The Phase 3 note
  above ("a fund picker in place of 'No scope selected'") no longer holds. Trading's "Scope:
  DU1009034" is the account the unscoped payload belongs to, not an operator scope.
- **Accounting navigation (C3).** It has 17 sub-items in 5 groups (22 rows), not 20. At
  1440×1100 every workspace stays visible, crowded at the bottom. Accounting › Today lands on a
  page headed "Close Cockpit", and every other workspace's first item is "Overview".
- **Period comparison (F4).** Ledger's "Vs prior period" works: it is backed by
  `PostedPeriodComparison` and its endpoint. The orphaned control is Trial balance's permanently
  disabled "Compare prior period" button, which sits beside the working toggle. The gap is scope:
  the comparison covers posted movements, not trial-balance balances, NAV, attribution or the
  cash ladder.
- **Run comparison.** Strategy › Promotions compares exactly two runs as a metrics table, with no
  equity or drawdown overlay. A third selection silently drops the oldest.
- **Since last visit (P10, F7).** Markers exist on the Accounting break queue and the open-breaks
  tile. `lib/report-change-since-review.ts` is imported only by its own test.
- **Charts (P9).** Drawdown and candles render through `EquityCurve` and `historical-chart`. The
  close burndown and the run trend are blank in captures because their endpoints have no fixture.
  Two implemented charts ("Close task progress", "Break aging") sit inside collapsed disclosures.
- **Detail panes (P2/V1).** Close Cockpit (`/accounting`) still has right-aligned values that
  wrap onto three lines; it was not among the migrated surfaces. The unmigrated `AccountingValue`
  tile has 41 call sites in `accounting-screen.tsx`. The Security Master panels already use
  `EntitySummary`.
- **Monospace (P4/V2).** Status-bar values, not labels, are mono. Dense-table headers and toolbar
  chips are mono uppercase through CSS rules (`ui-kit-primitives.css`), so a class-count ratchet
  would miss them. The P4 non-numeric `font-mono` ratchet has not been built.
- **Today inbox (F1, P3, P8).** The server ranks the inbox, but the browser never shows the rank
  or its reason. Owner, Age, Due and Triage are clipped off-screen beside the detail pane at
  1440px. Bulk and keyboard triage cover Assign only.
- **Portfolio market value.** "Unavailable" comes from a deliberate guard against using exposure
  as market value, and the backend never fills `MarketValue` or `Currency`. The fix is a contract
  change with a WPF-parity item.
- **Preferences** still carries the system KPI strip that C6 removes.

## Recommended Next Step

Finish Phase 3 with the KPI definition registry: the strips now say where each number comes from
and no longer show a change without a baseline, and the registry is what makes same-named numbers
agree and gives deltas a real baseline. Add label, scope, as-of time and comparison basis to the
shared read model, move every strip onto it, and file the matching `W8-WPF-PARITY-001` item. Then
add display names to the read models behind the twelve excused routes so their
`allowedContentIssues` entries can be removed.
