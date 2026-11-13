# Browser Workstation UX Consolidation Plan (2026-11-13)

> **Status:** In progress (Phase 0 and the first Phase 1 slice landed 2026-11-13) · **Created:** 2026-11-13 ·
> **Owner lane:** Workstation Shell and UX
> **Builds on:** [Browser Workstation UX Review and Improvement Plan (2026-11)](web-workstation-ux-review-plan-2026-11.md).
> That plan's findings still apply. This plan is a newer review of the same surface. It is
> organised around consolidation: one place, one vocabulary and one component for each concern.
> **WPF:** shared contracts stay unchanged, so the desktop lane can follow (`W8-WPF-PARITY-001`).

## Proposals

| # | Proposal | Core change |
|---|---|---|
| P1 | One place for "what needs attention" | The trust strip owns global state and the worst blocker. The status bar shows only connectivity and freshness. Per-screen trust rows show only differences. Today merges Finance queue and Operator inbox into one queue with a source filter |
| P2 | One status vocabulary and one badge | The 5 canonical severities in `design-system/status.tsx` (ready/review/action/blocked/info) are the only public status type. One `StatusBadge` that never truncates. Local tone maps are codemodded onto it |
| P3 | Numbers that agree, one figure format | A single money/number/date module: 24h UTC by default with a user time zone, relative times with absolute on hover, money as decimal strings (no `parseFloat`). Each headline figure states its scope. Price tables get a currency column |
| P4 | Operating scope impossible to miss | Permanent `entity › fund › account › period` scope in the top bar, carried in the URL. The empty value reads "All authorized", never "None". Single-account screens show an inline account picker |
| P5 | Navigation: fewer items, clearer names | Unique names. Admin items collapsed. Breadcrumb on every page. Close Calendar becomes a month grid. Pinned pages and "Recent" in the palette |
| P6 | Long screens become tabs or task modes | Shared in-page tab bar kept in the URL. One chevron `Disclosure`. Split the 5,000-line screen files along the tab lines |
| P7 | Tables that work like a finance grid | `TableWorkbench` as the only table. Global density, multi-select with a selection bar, sticky header and first column, virtualization above ~200 rows, em-dash plus tooltip for unavailable values |
| P8 | Consistent loading/empty/error/disabled states | `AsyncRegion` on every data panel, content-shaped skeletons, a `DisabledReason` pattern, and repeated "none" cells collapsed into a single note |
| P9 | Visual polish | Mono only for numbers and IDs. A 12px floor. One button hierarchy. Typographic arrows. Injected CSS moved into stylesheets. Dark-mode check |
| P10 | Workflow features | Record search in the palette, keyboard-first queues, one Views model (fix the `view=` collision), Close Calendar grid, Strategy visuals and comparison, undo, pop-out panels |

## Phased roadmap

Each phase ships on its own, behind the existing browser smoke tests and screenshot captures.

### Phase 0: Quick fixes (landed 2026-11-13)

| Item | Change |
|---|---|
| Clipped badges ("OPE", "Defaults or saved preferences a") | `.mds-sev` no longer uses ellipsis. A caps badge keeps its full width and does not shrink. A sentence badge wraps. `Badge` caps badges get `shrink-0 whitespace-nowrap` |
| Overflow assertion | `scripts/dev/capture-web-screenshots.mjs` reports a new `truncated-badge` content issue for any visible `.mds-sev`/`.mds-badge` whose text overflows its box |
| ASCII arrows | User-visible `->` in dashboard copy replaced with `→` |
| Trading debug pills | "Connection Connected" became "Broker Online/Degraded/Offline". "Panel None" is gone; "Open panel" shows only while a panel is open |
| "Basis: Not declared by source" | `StatStrip` prints a basis line only when a basis or as-of time is declared. The figure passport still states an undeclared basis |
| Bulk actions with nothing selected | The reconciliation bulk bar shows a hint and "Select all". Clear, Assign, Route and Resolve appear once breaks are selected, or while a draft is open |
| Duplicate keyboard shortcuts on Reconciliation | The inline disclosure was removed. The `?` button and dialog remain, and the dialog now states the permission/rationale rule |
| Accounting "Today" | Renamed "Close cockpit" in the Accounting navigation |
| Header title vs breadcrumb on Today | The document title, route announcement and context control now say "Today", matching the sidebar and breadcrumb |

**Not done in Phase 0:**
- The breadcrumb `<nav>` already renders on every routed page, and its last crumb is the page
  `<h1>`, so the two always agree by construction. On workspace overview pages it holds one
  crumb. Showing a visible ancestor there, such as a "Workspaces" root, is a P5 naming decision
  left for Phase 2.
- Captures were not re-run at 1440px and 1024px in this change. The `truncated-badge` check takes
  effect on the next capture run.

### Phase 1: Clarity foundations (P1–P4, ~3–4 weeks)

**First slice (landed 2026-11-13):**
- **P2 status styles.** `design-system/status-styles.ts` is now the only status-to-look mapping.
  - `toSeverity()` sends every badge variant, readiness tone and server status through
    `normalizeDesignSystemSeverity` onto the five severities.
  - `statusBadgeVariant`, `statusPanelClass`, `statusTextClass` and `statusMetricTone` give
    each severity one style.
  - `lib/shared-tone-mappings.ts` (15 helpers), `screens/accounting-screen.styles.ts` and
    `screens/data-screen.tone-styles.ts` are removed. Their ~200 call sites in 19 files were
    migrated.
  - These colour changes are deliberate:
    - Old helpers disagreed on "warning" (review in some, action in others). It is now always
      action.
    - "default" and "primary" are informational rather than primary-tinted.
    - Panels use the severity tokens.
    - Informational text inherits its colour instead of turning muted.
- **P3 exact money.** `components/accounting/money.ts` parses amounts as exact decimals (BigInt
  units plus scale).
  - `formatMoney` formats a decimal string from its digits, rounding half away from zero.
  - `sumAmounts` sums exactly.
  - `parseFloat` is gone.
  - Example: `"12345678901234.565"` now renders `12,345,678,901,234.57`, and `"1.005"` renders
    `1.01` (previously `1.00`).

**P1 first step (landed 2026-11-13):** the bottom status bar no longer repeats the
masthead's worst issue ("Cash variance needs review +5 more"). It holds Sync and page Freshness
only, so the trust strip is the one shell place that names the worst open issue.

**P1 second step and P3 formatter burn-down (landed 2026-11-13):**
- Page status rows (`OperationalTrustSummary`) no longer list the masthead's shell-wide issues in
  their one-line summary. Those issues stay under "Details", and a page whose own facts are ready
  reads "This page's sources are current · N workspace issues in the trust strip".
- `localMoneyFormatters` is at 0 (was 24 occurrences in 13 files). Screen-local money and signed
  formatters were replaced with `@/lib/format`, which gained a `signed` option on `formatNumber`,
  `formatPercent`, `formatRatioAsPercent`, `formatAmountWithCode` and `formatCount`, and now hosts
  `formatCurrencyWithCode`. `handRolledNumberFormatting` went from 75 to 72. The only output
  changes are thousands separators on signed percents and returns of 1,000 or more, "-0" counts
  now reading "0", and a trimmed currency code.

**P1 third step (landed 2026-11-13):** Today's "Finance queue" and "Operator inbox" now sit
under one Queue heading with a Source filter (All sources, Workspace signals, Shared inbox). The
inbox keeps its assignment, snooze and acknowledge controls. The two sources still render as two
tables: interleaving them into one grid needs a shared row model for workspace signals and inbox
items, which is a follow-up.

**P1 fourth step (landed 2026-11-13):** Today shows one interleaved queue grid ("Today queue").
- Row model: `TodayQueueRow` in `lib/daily-control-tower.ts`, built by `buildTodayQueueRows`. Each
  row has a source (`workspace` or `inbox`), label, detail, workspace, severity rank and observed
  time. A workspace row also carries its evidence passport and next action. An inbox row carries
  the full triage item.
- Order: severity, then oldest first (rows with no observed time go after dated rows), then each
  source's own rank, with workspace signals first on a tie. Workspace signals have no creation time,
  so their age is the time of their correlated evidence.
- The grid has a Source column (Workspace signal / Shared inbox). The Source filter filters rows of
  this one grid and shows a count per source. The workspace count is the sidebar Today badge's
  count, which still counts workspace signals only.
- The inbox read, filters, bulk selection and in-flight or unconfirmed command moved into
  `useOperatorTriageInbox`, above the filter. Filtering inbox rows out keeps a pending command,
  and "Retry same command" stays visible under every filter. `OperatorTriageQueue` uses the same
  hook and keeps its own table.
- A selected inbox row opens the triage rail: assign, acknowledge, snooze, route and activity. A
  selected workspace row opens the evidence rail. Keyboard j/k moves across both sources, and "a"
  opens assignment on an inbox row.
- "Affected output" and "Evidence" are now optional columns, hidden by default. The evidence rail
  still shows both.
- The screenshot catalog now waits on `table[aria-label="Today queue"]` and on
  `data-today-inbox-state` reaching `ready` or `unavailable`.

**P3 `.toFixed()` burn-down (landed 2026-11-13):** `handRolledNumberFormatting` is at 0 (was
72), so both formatter ratchets in the Phase 1 criteria are met.
- `@/lib/format` gained `formatDecimal(value, digits)` for displayed figures (grouped, signed
  option, "—" for non-finite values) and `formatFixed(value, digits)` for SVG and CSS geometry
  only.
- Output changes:
  - Thousands separators on figures of 1,000 or more.
  - Decimal ties round on the shortest decimal form, so 0.35 at one digit reads 0.4.
  - "—" replaces "NaN" and "Infinity" where there was no guard.

**Remaining in Phase 1:**
- Merge `lib/format.ts` `formatMoney` and the accounting one behind one API.
- One date format with a time-zone preference.
- The P4 scope bar.

Acceptance criteria:
- Only the 5 canonical severities are exported. `shared-tone-mappings.ts`, the
  `accounting-screen.styles.ts` tones and `data-screen.tone-styles.ts` are removed.
- Money/number formatter ratchets are at 0, with no `parseFloat` on monetary values.
- One date format with a user time-zone preference. Tests cover UTC, DST and relative/absolute times.
- Each attention item appears in at most 2 places: the trust strip and the queue.
- The first data row on Today, Portfolio and Accounting is at y ≤ 220px at 1440×900.
- Every figures strip prints its scope, and "No scope selected" no longer appears.
- Counts for the same item agree across screens in the seeded scenario, or carry an inline
  scope label that explains the difference.

### Phase 2: Structure and tables (P5–P8, ~4–6 weeks)

Acceptance criteria:
- No screen file is over 2,000 lines. The active tab is in the URL and survives reload.
- Raw `<table>` and ad-hoc table ratchets are at 0. Accounting tables run on `TableWorkbench`.
- A 10,000-row fixture scrolls at ≥ 50 fps.
- Every disabled action has a reason.
- Every data panel has loading, empty and error-with-retry states.
- The sidebar has ≤ 45 entries with no duplicate names, and axe checks are clean on touched routes.

### Phase 3: Visual polish (P9, ~2–3 weeks, can overlap Phase 2)

Acceptance criteria:
- The typography-floor baseline is at 0.
- Mono appears only on numeric/ID cells.
- No runtime-injected CSS, and inline `style={{}}` only for chart geometry.
- Duplicate primitives are removed.
- Light and dark captures pass the contrast checks.

### Phase 4: Workflow features (P10, ~4–6 weeks)

Order: record search → keyboard queues → unified Views → Close Calendar grid → Strategy visuals
and comparison → undo → pop-outs.

Acceptance criteria:
- The palette finds a record by ID or name in ~200 ms.
- Every queue supports j/k/x/a/e with a visible hint.
- There is one Views model, and the `view=` collision is fixed with deep-link tests.
- The Close Calendar month grid has "My tasks".
- Strategy shows a curve and drawdown per run, and compares 2–4 runs.
- Assign, dismiss and snooze can be undone.
- New contracts are documented for WPF parity.

**Recommended next step:** P2 and P3 from Phase 1. Consistent status and number display is the
foundation the later phases build on.
