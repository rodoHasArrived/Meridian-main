# Web UI Structural Improvement Proposal

**Status:** in delivery (Phases A–E and the Accounting Phase-D follow-ups shipped; Phase F screen consolidation in delivery)
**Owner:** core-team
**Reviewed:** 2026-07-25

> **Delivery status (2026-07-13).** Phase A landed app-wide: the decision brief renders as a
> masthead status pill (`DecisionBriefPill`) instead of a per-route banner, the onboarding coach
> mark docks to the masthead ring and never auto-opens, and `StatStrip` replaces the four-card KPI
> band on migrated routes. Phases B–C landed for Trading as the template: `/trading/orders`,
> `/trading/positions`, and `/trading/risk` render focused master–detail views with a sticky
> detail rail, `/trading` renders the acceptance/session overview, and the in-page tab strip
> navigates the same sub-routes as the sidebar. Route capture heights fell from a uniform
> 4,419 px to 1,100–1,591 px. Data followed on the same pattern: the workspace header collapsed
> to the compact route header + tab strip (its filter bar, duplicate in-page folder rail, and
> command-deck highlight cards removed), the metric band adopted `StatStrip`, and P6 landed —
> when two or more Data analytics reads fail together, one consolidated amber degraded state
> (`data-screen.analytics-status.tsx`) with a retry-all action replaces the stacked per-panel
> alarms. Phase D plus the shell rollout landed next: Settings gained the compact route header,
> route-navigating tab strip, and an access-scoped profile row; Accounting gained a
> `showReporting` visibility flag (the reporting band no longer rides every route), the same
> compact header + tab strip over its task modes, and a disclosure-collapsed break-actions band
> on reconciliation; Strategy, Portfolio, and Reporting adopted route-scoped views
> (`resolveStrategyRouteView`, `resolvePortfolioRouteView`, and the Reporting task-mode tab
> strip), with the Reporting task-mode launcher card grid retired in favor of the tab strip.
> Phase E and the Accounting follow-ups landed on 2026-07-17: shared font, chart, plot-surface,
> and compact-row tokens now govern the browser and checked-in design-system package; the Configure
> surface moved into its own module; reconciliation uses a sticky case-detail rail; the dedicated
> Ledger Explorer and shared explorer shell are owned by the canonical `/accounting/ledger` route;
> and the workflow, posture, external-GL, and
> multi-asset tail is scoped to its owning Accounting workstreams.
>
> **Delivery status (2026-07-19).** Phase F (screen consolidation, `W8-UX-CONSOL-001`) is in
> delivery: Trial Balance now renders as a tab of the Ledger Explorer
> (`/accounting/ledger?view=trial-balance`) and the Formula Workbench placeholder as a tab of
> Quant Lab (`/strategy/quant-lab?view=formulas`); both retired routes redirect with query and
> hash scope preserved via `legacyWorkspaceRedirect()`. The Evidence Workbench is canonicalized
> to `/reporting/evidence`: the `/accounting/evidence` and `/data/evidence` mounts and the
> `/accounting/evidence/detail` stub redirect there with subject scope preserved, matching the
> existing `evidenceWorkbenchPath()` gravity. The Market Data desk
> (`screens/market-data-screen.tsx`) now owns `/data/quotes`, hosting the live-quote, watchlist,
> and price-alert panels as `?view=` tabs over the shared quote stream — three Data sidebar items
> collapse into one "Market data" entry, with `/data/watchlist` and `/data/alerts` redirecting.
> The reporting run-flow merge and reconciliation module extraction remain sequenced as
> follow-ups.

This document reviews the browser workstation as captured in the current screenshot catalog and
proposes a set of improvements, including large structural changes to page architecture, routing,
and the shell. It is the design-level companion to the screenshot audit in
[`../screenshots/web-audit.md`](../screenshots/web-audit.md): the audit grades what exists; this
proposal describes what the workstation should become and in what order.

Static HTML mockups of the proposed structures live in
[`mockups/web-ui/`](mockups/web-ui/README.md). They are proposal artifacts, not production
templates.

Related material:

- Screenshot catalog: `docs/screenshots/web/*.png` (77 fixture-backed captures at 1440 px wide)
- Design charter: [`../product/meridian-design-document.md`](../product/meridian-design-document.md)
- Visual design system: `Meridian Design System/` ("Concrete / Institutional Ops", the active
  reference; enforced against the dashboard by
  `src/Meridian.Ui/dashboard/src/design-system-contract.test.ts`)
- WPF parity lane: [`wpf-web-ui-alignment-plan.md`](wpf-web-ui-alignment-plan.md)

## 1. Evidence base

- Direct review of key captures: all seven workspace landings, the Daily Control Tower,
  `/trading/orders`, `/strategy/designer`, and `/accounting/reconciliation`.
- Measured page heights across the original 74-capture audit baseline; release verification follows
  the current 77-capture route catalog.
- The 2026-06/07 audit pass in `docs/screenshots/web-audit.md`.
- Code-structure survey of `src/Meridian.Ui/dashboard/` (shell, routing, screens, tokens, tests).

All captures are fixture-backed demo data; findings concern layout, structure, and workflow shape,
not data accuracy.

## 2. What is working — preserve these

The proposal deliberately builds on shapes the workstation already gets right:

- **Seven-workspace sidebar** (Trading, Portfolio, Accounting, Reporting, Strategy, Data,
  Settings) is consistent everywhere and matches the charter's navigation contract.
- **Bottom status bar** (session, data source, sync) is quiet, informative, desktop-grade chrome.
- **Command palette** (`Ctrl+K` "Go to route, action, evidence…") is the right fast path.
- **Strategy Designer** (`web-strategy-designer.png`) is the best screen in the product: a
  three-pane workbench (field catalog → cell canvas → inspector) where the tool itself fills the
  viewport. It should be the layout benchmark for every workspace.
- **Operator Readiness Console and Accounting Exceptions** keep a clear problem → cause → gate →
  action spine.
- **Evidence-first affordances** — severity badges, retained-evidence links, disabled-state
  reasons — are a genuine differentiator and must survive every change below.
- **Daily Control Tower** is the right concept for cross-workspace triage: one ranked decision
  queue.
- **Accounting close-cockpit gating** (task-mode section visibility, added 2026-07) produced the
  shortest, clearest workspace landing (~1,280 px). It proves the short shape works; the rest of
  this proposal generalizes it.

## 3. Findings

### F1 — Everything-at-once page architecture (the core structural problem)

Every workspace screen renders all of its lanes — posture, readiness, evidence, exceptions, risk,
blotters, history — stacked in a single scroll. Measured full-page heights at 1440 px wide:

| Capture | Height (px) | Viewport-heights (1100 px) |
| --- | ---: | ---: |
| `web-accounting-configure.png` | 19,082 | 17.3 |
| `web-accounting-reconciliation.png` | 14,550 | 13.2 |
| `web-accounting-security-master.png` | 14,183 | 12.9 |
| `web-accounting-exceptions.png` | 13,609 | 12.4 |
| `web-portfolio-workspace.png` | 8,122 | 7.4 |
| `web-settings-workspace.png` | 6,407 | 5.8 |
| `web-trading-workspace.png` | 4,419 | 4.0 |

Across the catalog: **median capture is ~2,760 px (2.5 viewport-heights); 43 of 74 routes exceed
two viewport-heights; 16 exceed five.** Eight of the ten tallest routes are in Accounting. An
operator cannot hold a 13-viewport page in their head; scroll position becomes the de facto
navigation system, and the audit's recurring complaint that "detail panels are visually far from
the row that controls them" is a direct symptom.

### F2 — Sub-routes render the whole workspace page

`web-trading-orders.png`, `web-trading-positions.png`, `web-trading-risk.png`, and
`web-trading-workspace.png` are all **exactly 4,419 px tall** — the same page. Clicking "Orders"
in the sidebar changes the breadcrumb and route chip, not what you see. The same holds for
`/settings/providers` and `/settings/preferences` (both 6,407 px, identical to `/settings`).

Root cause in code: routes lazy-load monolithic screen components
(`src/Meridian.Ui/dashboard/src/screens/trading-screen.tsx` ≈ 2,600 lines,
`settings-screen.tsx` ≈ 7,500, `accounting-screen.tsx` ≈ 6,700) that render every lane regardless
of sub-route. This is the root cause of the audit's "near-duplicate catalog" finding — the
captures are duplicates because the pages are.

### F3 — Shell chrome consumes the first viewport

The stack above task content on a typical route: top bar (brand, search, build/mode/source/
provider chips, four icon buttons, desk/operator) → breadcrumb → workspace badge → H1 with
description sentence → operating-context row → decision-brief banner → four-card KPI band → lane
hero card (heading + explanatory paragraph + chip row) → search box + filter-chip row + tab row.
Task content starts roughly 1,200–1,500 px down; on a 1100 px viewport the operator's first screen
contains no operable data at all.

### F4 — One accounting break paints the entire product red

The same "DECISION BRIEF: Resolve AmountMismatch break open" banner is pinned to every workspace
capture — Trading, Portfolio, Strategy, Data, Settings alike. It duplicates the Daily Control
Tower's job on every page, pushes content down ~90 px everywhere, and teaches operators to ignore
red. Severity color loses meaning exactly where the product most depends on it.

### F5 — Meta-UI: the interface spends pixels describing itself

Recurring patterns across captures:

- Cards that describe other cards ("Blotter management — working and partial orders stay visible
  so you can act on fill progress without context-switching") instead of showing data.
- The same heading and description rendered twice on one page (`/trading` shows the "Orders
  blotter" hero twice).
- A kicker label on every panel (`TRADING LANE`, `POSTURE`, `OPERATOR ACCEPTANCE`, `EXCEPTIONS`,
  `ACTIONS`, `HISTORY`) plus an explanatory sentence under every heading.
- Bottom-of-page "route card" grids that restate the sidebar.

The data-to-chrome ratio inverts what a workstation needs: on `/trading`, the Live positions,
Open orders, and Recent fills tables each show one fixture row beneath roughly 3,000 px of
posture/readiness/evidence panels.

### F6 — Onboarding popover overlaps working content

The floating "First 10 minutes" card overlaps actionable panels in every capture (on
`/accounting` it covers the Evidence and action panel of the case workbench). It has no reserved
layout slot, reappears across routes, and contaminates the screenshot catalog.

### F7 — Four-plus competing navigation systems

Sidebar; top-right "Workspaces" button; breadcrumb plus route chips; in-page tab rows
(Overview / Readiness / Exceptions / Order Actions / Paper Sessions); bottom route-card grids;
and the workflow-continuity dock. Several of these encode the same destinations. Every additional
system dilutes the others — and because of F2, most of them don't actually change the view.

### F8 — Degraded and empty states multiply instead of consolidating

On `web-data-workspace.png`, one unavailable backend produces four stacked full-width red alarm
panels (data quality, capability matrix, corporate-action inbox, security-master coverage). On
`web-strategy-workspace.png`, "PlotTool catalog not connected" is restated in roughly six panels
plus `N/A` placeholders and disabled editors. The correct response to a single upstream condition
is a single state, stated once, with the next productive action.

### F9 — Settings is a single dump page

6,407 px stacking profile/access posture, system posture, the provider connection center (with an
inline OpenAPI import JSON textarea), a guided integration workbench, CSV/REST dry-runs, data
providers, a provider-modules load error, and per-broker API-key forms — despite Settings already
having an internal tab row and real sub-routes. High-consequence controls (credentials, provider
wiring) sit mid-scroll in an undifferentiated stack.

### F10 — Visual-language drift from the product's own design system

The `Meridian Design System/` package ("Concrete / Institutional Ops") is the declared visual
reference, and the dashboard enforces token parity with it. The captures drift from it in ways the
token contract can't catch:

- Black chart panels embedded in light cards (Strategy scatter view) — surfaces not themed to
  tokens.
- Monospace small-caps used for nearly all labels, chips, and badges, flattening hierarchy; mono
  should be reserved for data values (ids, symbols, numbers).
- Uniform card weight: every panel shares the same border/background/radius, so nothing reads as
  primary.
- Red used simultaneously for the global banner, blocked states, unavailable panels, and
  destructive actions.
- Selected-row data rendered three times (table row → inline summary card → detail key-value
  card).

## 4. Proposals

Ordered by structural impact. P1–P3 are the large changes; the rest follow from them.

### P1 — Cockpit, not corridor: viewport-height master–detail workspace layout

Replace the stacked-lane long page with a layout that fills the viewport and stops:

```text
┌────────────────────────────────────────────────────────────────┐
│ masthead (brand · palette · mode · status pill · session)      │
├──────┬─────────────────────────────────────────┬───────────────┤
│ side │ primary surface (blotter / queue /      │ detail rail   │
│ bar  │ canvas) — fills remaining height,       │ (sticky;      │
│      │ scrolls internally                      │ selected row) │
├──────┴─────────────────────────────────────────┴───────────────┤
│ status bar                                                     │
└────────────────────────────────────────────────────────────────┘
```

- One compact header row per route (title, scope chips, primary actions) instead of the current
  badge → H1 → description → context-row → banner stack.
- The primary surface is the tool itself — the orders blotter, the reconciliation queue, the
  report-run table — sized to the viewport with internal scrolling.
- Everything currently below the fold moves into the detail rail, a drawer, or a dedicated
  sub-route (P2).
- **Page-height budget: no route exceeds two viewport-heights.** Strategy Designer already meets
  this shape; the Accounting close cockpit is close.
- Building blocks already exist: `components/ui/screen-layout`, `panel-surface`, and the
  companion-pane components.

Mockup: [`mockups/web-ui/01-trading-cockpit.html`](mockups/web-ui/01-trading-cockpit.html).

### P2 — Route-scoped views: a sub-route renders its tool, not the workspace

- `/trading/orders` renders a full-height Orders cockpit (blotter + detail rail). `/trading/risk`
  renders the risk cockpit. The workspace root (`/trading`) becomes a short overview: compact
  stats, the workspace's decision queue, and links into the focused tools.
- Decompose the monolith screens along the seams that already exist (the `*.view-model.ts` and
  split-panel siblings show the intended pattern): extract per-route view components from
  `trading-screen.tsx` first as the template, then `data-screen.tsx`, then the big two
  (`settings-screen.tsx`, `accounting-screen.tsx`).
- This directly fixes the audit's near-duplicate-catalog complaint: each capture becomes a
  distinct screen because each route becomes a distinct view.
- Cross-lane note: WPF already works this way (`PositionBlotterPage`, `OrderBookPage`, per-page
  views under a workspace shell). Web moving to route-scoped views *increases* parity and keeps
  both lanes on the shared read models.

Mockup: [`mockups/web-ui/02-workspace-overview.html`](mockups/web-ui/02-workspace-overview.html).

### P3 — Global banner becomes a status pill; alerts render where they live

- Remove the cross-workspace decision-brief banner. In its place, a compact masthead pill (e.g.
  `● 1 blocked · 5 review`) that opens the break queue / Daily Control Tower.
- A break renders as a banner only inside its owning workspace (the AmountMismatch break appears
  in Accounting, not in Strategy).
- The Daily Control Tower remains the single cross-workspace triage surface; the pill is its
  always-visible handle.
- Code: `app.tsx` masthead composition plus the `app-shell.*.ts` view-models that currently feed
  the banner.

### P4 — Remove meta-UI

- Delete cards-that-describe-cards, duplicate hero headings, and the bottom route-card grids
  (the sidebar already owns those destinations).
- Panel kickers are kept only where they carry state (e.g. severity), not as decoration.
- One-sentence explanations under headings move to tooltips or the help surface. The onboarding
  tour (P10) is the right home for "what is this lane" prose.

### P5 — Navigation collapses to two systems

- **Sidebar** (workspaces + sub-routes, single source of truth: the `lib/workspace.ts` catalog)
  and **command palette**. In-page tab rows either disappear or become the visual rendering of the
  same sidebar sub-route list — never a parallel taxonomy.
- Breadcrumb reduces to `workspace / route`. The top-right "Workspaces" button and route-chip
  clusters are retired.

### P6 — One consolidated degraded/connect state per screen

- A screen whose upstream dependency is missing renders **one** state panel: what is unavailable,
  why, and the single next productive action (connect provider, open diagnostics). Dependent
  panels collapse to skeletons or don't render — they do not each raise their own alarm.
- Generalize the guided empty state already shipped for Data Live Quotes; extend
  `components/data/empty-state` and `ui/async-region` into the shared pattern.

Mockup: [`mockups/web-ui/04-degraded-states.html`](mockups/web-ui/04-degraded-states.html).

### P7 — Settings becomes routed task pages

- Each existing Settings tab becomes what its route already claims to be: `/settings/access`,
  `/settings/providers`, `/settings/integrations`, `/settings/diagnostics` each render only their
  own task page behind a shared secondary nav.
- The guided integration workbench and OpenAPI import move into a step-wise wizard (drawer or
  dedicated route), not an always-rendered stack of JSON textareas.
- Credential forms get their own focused page with explicit save/verify actions — never
  mid-scroll in a 6,400 px column.

Mockup: [`mockups/web-ui/03-settings-task-route.html`](mockups/web-ui/03-settings-task-route.html).

### P8 — Design-system hardening (within Concrete, not a re-theme)

The Concrete light-first language stays. Hardening means using it with more contrast between
levels:

- **Surface hierarchy:** page canvas → primary panel → secondary card → inset, each with a defined
  border/background step; the primary surface visibly outranks its satellites.
- **Type hierarchy:** reserve monospace (Cascadia) for data values; UI labels use the sans ramp;
  restore a real size/weight ramp between H1, panel titles, and section labels.
- **Severity rationalization:** red = blocked/destructive only; amber = review; green = ready;
  neutral gray = informational. Maximum one banner per page (P3 removes the global one).
- **Chart surfaces themed to tokens** so a chart in a light card is a light chart (fixes the black
  panel in Strategy).
- **Table density defaults to compact** — tables are the workstation's core, not an afterthought
  under posture cards.
- Mechanics: all through `src/styles/index.css` (the ~440-var token sheet) and
  `tailwind.config.ts`, updated in lockstep with the `Meridian Design System/` package so
  `design-system-contract.test.ts` stays green.

### P9 — A single sticky detail rail replaces triple duplication

Selecting a row populates one detail rail (right side, sticky, viewport-height) that owns the
summary, key-values, evidence links, and actions. The inline summary card and the distant detail
card are removed. This also resolves the audit's focus-management risk: the detail target is
always in the same place, adjacent to the triggering table.

### P10 — Onboarding docks; screenshots exclude it

The "First 10 minutes" tour becomes a docked checklist opened from the masthead (help affordance
or the existing onboarding-progress slot). It never floats over actionable panels, appears
automatically only on first run, and is disabled in the screenshot fixture profile so the catalog
stays clean.

### P11 — KPI band becomes a compact stat strip

The four large KPI cards compress into one stat-strip row (label · value · delta), recovering
roughly 200 px on every route. Stats deep-link to the surface that explains them.

## 5. Phased delivery

Each phase is independently shippable and independently verifiable against the screenshot
pipeline (`npm run screenshots` → `scripts/dev/capture-web-screenshots.mjs`, validated by
`scripts/dev/validate-screenshot-captures.py`).

| Phase | Scope | Main code touchpoints | Risk |
| --- | --- | --- | --- |
| **A — Shell de-noising** | P3 pill, P4 meta-UI removal, P10 tour docking, P11 stat strip | `app.tsx`, `app-shell.*.ts`, masthead/onboarding components, per-screen hero sections | Low |
| **B — Route-scoped views** | P2, P5; Trading first as the template, then Data | route table in `app.tsx`, `trading-screen.tsx` decomposition, `lib/workspace.ts` catalog | Medium |
| **C — Master–detail cockpit** | P1, P9 layout for the Phase-B views | `screen-layout`/`panel-surface`, new detail-rail composite | Medium |
| **D — Settings & Accounting restructure** | P7 plus Accounting deep routes on the Phase-B/C pattern | `settings-screen.tsx`, `accounting-screen.tsx` decomposition | High (largest files) |
| **E — Token & density polish** | P8 | `styles/index.css`, `tailwind.config.ts`, design-system package + contract test | Low–medium |
| **F — Screen consolidation** | Fold sibling tools into deeper host screens behind the charter roots (`W8-UX-CONSOL-001`): Trial Balance → Ledger Explorer tab, Formula Workbench → Quant Lab tab, Evidence Workbench canonicalized to one Reporting home, Live Quotes + Watchlist + Price Alerts → one Market Data desk; retired routes stay as scope-preserving redirects | route table + `legacyWorkspaceRedirect()` in `lib/workspace.ts`, `workspace-nav.view-model.ts`, `app.tsx` routes, host screens (`finance-standard-pages-screen.tsx`, `quant-lab-screen.tsx`, `evidence-workbench-screen.tsx`, `live-quotes-screen.tsx`) | Medium |

Validation per phase: targeted vitest suites (including the per-screen `*.a11y.test.tsx` axe
suites), `npm --prefix src/Meridian.Ui/dashboard run build`, screenshot regeneration for touched
routes, and a re-grade of the affected rows in `docs/screenshots/web-audit.md`.

**Phase D follow-up completion (2026-07-17).** The four recorded Accounting follow-ups are closed.
`AccountingConfigurationPanel` now lives in `accounting-screen.configuration-panel.tsx`;
reconciliation comparison, statement runs, and case queue drive one sticky master–detail rail;
the unreachable `AccountingScreen.showLedgerExplorer` branch was removed in favor of the dedicated
shared-shell `/accounting/ledger` screen, with no hash-only alias; and the workflow, posture,
external-GL, and multi-asset sections default closed and open only for their owning task-mode or
explicit deep-link context. Route-ownership and accessibility assertions protect these seams.

## 6. Success metrics

- **Page height:** no route capture exceeds 2 viewport-heights (2,200 px at the standard 1440×1100
  capture); the original audit baseline had 43 of 74 above that threshold.
- **First viewport:** every route shows operable task content (a table, queue, canvas, or form)
  without scrolling.
- **Catalog distinctness:** zero byte-identical-height sub-route groups (today: 4 Trading routes
  at exactly 4,419 px, 3 Settings routes at 6,407 px).
- **Audit grades:** Watch-rated rows in `web-audit.md` move to Healthy at each phase's re-grade.
- **Severity integrity:** at most one red banner anywhere in the app at a time, and only in the
  owning workspace.

## 7. WPF parity impact

Per the shared-first rule in [`wpf-web-ui-alignment-plan.md`](wpf-web-ui-alignment-plan.md),
presentation may differ between lanes but business state may not. Everything proposed here is
presentation-lane restructuring: no read-model, contract, or endpoint changes are required, so no
WPF work is *forced*. Two touchpoints to manage:

- The parity matrix maps web screens to WPF pages by logical screen; Phase B/D decomposition
  renames/splits web screen files, so the matrix's "web screen (logical)" column needs a refresh
  in the same change.
- Route-scoped views bring the web shell's shape closer to WPF's existing per-page model, which
  should simplify future parity rows rather than add them.

## 8. Out of scope

- Any mobile lane (per repo policy). Responsive behavior of the browser workstation is unchanged
  by this proposal beyond what the layout work naturally improves.
- Backend/read-model changes, new endpoints, or contract changes.
- The WPF lane itself (impact covered in §7 only).
- A visual re-theme away from the Concrete design system.
