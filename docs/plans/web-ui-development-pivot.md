# Web UI Development Pivot


## TODO Checklist (Concrete Implementation Items)
- [ ] Define scope boundaries for **web ui development pivot** and document explicit in-scope vs out-of-scope items.
- [ ] Break delivery into PR-sized milestones with owner, dependency, and evidence artifact for each milestone.
- [ ] Implement the first milestone in code/config/scripts and link the exact validating test or command output.
- [ ] Add/update operator runbook steps and rollback procedure for the web ui development pivot workflow.
- [ ] Record completion evidence in `docs/status/` (or linked packet) and mark corresponding checklist items done.

**Date:** 2026-05-21
**Status:** Active reference

## Decision

The original browser-workstation pivot remains important because it established Meridian's shared workstation contracts and a strong web operator lane in `src/Meridian.Ui/dashboard/`. That work stays supported, and `src/Meridian.Wpf/` is now reopened as an active first-class operator surface alongside it. New operator workflows may land in either client when justified, but shared business logic, read models, and workstation API seams should stay in shared contracts or services first.

This pivot is interpreted through the consolidated current-direction entry point in
[`current-direction-and-status.md`](current-direction-and-status.md). It is a current delivery
policy for operator UI work, not a separate roadmap from the active Wave 2-4 path.

New product behavior should land behind shared contracts, local/web API endpoints, or shared read
models before any client-specific workflow is expanded. Browser and desktop are both supported
clients, not the product boundary. Major UI features are not considered accepted until the relevant
workflow is API-addressable or shared-read-model-backed, with the chosen operator surface validated
and the sibling surface preserved where the workflow is part of the common workstation story.

## Commercial UI Implication

The browser workstation remains the strongest distribution and demo lane for Meridian as the system of record for investment decision evidence, and the WPF desktop remains an active deep-work operator surface for the same shared contracts. New Assurance Loop surfaces should start from shared contracts and workflow seams that either client can consume for Data Trust Passport, Run Evidence Graph, Promotion Passport, accounting-grade paper evidence, reconciliation casework, and governed report-pack readiness. Buyer Demo Mode, role-based demo views, readiness dashboards, close workflow previews, evidence packet actions, broker statement reconciliation, and controls-policy summaries can still lead in the browser when that is the better distribution path, while desktop can productize the same shared seams for Windows-first operators. The current `/reporting/evidence` workbench and shared evidence packet/graph APIs are support slices for that direction, not completion of the Evidence Vault or report-line provenance product.

## Active UI Lane

- `src/Meridian.Ui/dashboard/`: React/Vite dashboard source for the browser workstation.
- `src/Meridian.Ui/wwwroot/workstation/`: built workstation assets served by `Meridian.Ui`.
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`: shared workstation API contract source.
- `src/Meridian.Ui.Services/`: read-model and service support for workstation payloads.
- `Meridian Design System`: design-system contract for the web workstation shell, including
  cockpit color tokens, tight surface radii, shallow workstation shadows, the brand mark, line-icon
  usage, and operator-to-operator copy rules now reflected in shared dashboard primitives.

## Owners

- **Workstation Shell and UX:** Own web dashboard navigation, layout, operator interaction patterns, degraded-bootstrap recovery handoffs, and build/test health.
- **Shared Workflow and Contracts:** Own DTO and endpoint compatibility for `/api/workstation/*`, `/api/execution/*`, and promotion/replay surfaces consumed by the dashboard.
- **Trading Workstation:** Preserve the closed Wave 2 cockpit readiness baseline in the web dashboard.
- **Governance and Ledger:** Own accounting, reconciliation, reporting, and Security Master web workflows.
- **Data Confidence and Validation:** Own provider, backfill, storage, and data-quality web workflows.

## Near-Term Implementation Slices

1. Restore and keep the dashboard runnable with local `npm install`, `npm run test`, and `npm run build`. Current evidence includes a refreshed workstation asset build, app-shell view-model coverage for loading/partial-degradation/bootstrap-failure status panels plus query-string route-focus keys for subject/symbol handoffs such as `/data/quotes?symbol=AAPL`, canonical `WORKSPACES` metadata for `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`, route-aware command-palette coverage that now keeps local quick-route commands available for readiness, brokerage sync, reconciliation, Security Master, report packs, evidence, Quant Lab, covered-call backtests, watchlist, live quotes, Price Alerts, backfills, and the hash-targeted `/settings#alpaca-provider-setup` provider repair panel with hash-aware current-route semantics while also loading `/api/workstation/workflows` and pinned `/api/workstation/workflows/presets` into launchable browser commands with keyboard filter, arrow navigation, and Enter-to-open activation, shared operator-inbox routing that sends reconciliation work to `Accounting`, report-pack review work to the dedicated `/reporting/report-packs` task panel, brokerage-sync review work to the dedicated `/portfolio/brokerage-sync` task panel, and Security Master coverage work to `Accounting`, independent browser bootstrap reads for `/api/workstation/accounting` and `/api/workstation/reporting`, Settings diagnostic endpoint posture derived from loaded workspace payloads and workspace errors, Settings Alpaca paper-key verification controls plus live-endpoint acknowledgement gates, Portfolio run-evidence and brokerage household/account selection state with filter focus restoration plus shared dense row selection for position/run detail panels, Overview portfolio-at-a-glance plus Today panel context, sidebar sub-items plus richer mega-menu/command-palette grouping, menu-linked screenshot-route coverage, browser Security Master search-result selection with identity drill-ins, details/lots tracking with server-side operator overrides, Security Master print-packet readiness projection, Data live quote/order-book/watchlist/Price Alerts workflows with multi-symbol quote snapshots and selectable recent-trade detail inspection, click-to-trade order-ticket staging with explicit review acknowledgement before submission, intraday and historical price charts, a gated `/strategy/quant-lab` browser surface backed by `/api/quant/run`, `/api/quant/parameters`, and `/api/quant/templates`, a visual `/strategy/designer` support surface, command-palette/mega-menu focus trapping with masthead routes for `/portfolio/brokerage-sync`, `/reporting/report-packs`, `/reporting/evidence`, `/strategy/covered-call`, `/data/watchlist`, `/data/quotes`, `/data/alerts`, and `/accounting/security-master`, full-console readiness checkpoint gating at `/trading/readiness`, Meridian Design System reference workbench/tokenized-color support, and Research run-library component/view-model coverage for two-run compare/diff readiness, selected run-detail inspection, promotion-history decision detail, and command-error alerts.
2. Keep the dashboard shell first-class: route the seven canonical workspaces from shared workspace metadata, preserve legacy aliases for `/overview`, `/research`, `/data-operations`, and `/governance`, set the correct workstation document title on first direct route load without stealing focus, lazy-load route screens behind an accessible workspace fallback, preserve query-string and hash-target focus after lazy route content resolves, and replace placeholder routes as dedicated web screens are implemented.
3. Keep the shared dashboard shell aligned with `Meridian Design System`: use the documented masthead plus left-rail workstation frame, restrained ambient background, tokenized surfaces, square badges/chips, mono data, and the copied Meridian brand mark before adding screen-specific visual treatments. Keep the rail and workspace header lean: avoid repeating current-workspace cards, route chips, environment badges, and command-count metadata when active-route, status, and session context are already present through accessible labels or shared shell chrome.
   - The masthead now includes a compact trust strip for browser bundle version, operating mode, data source, and provider posture. The strip is app-shell view-model owned, distinguishes local-host data from no-host demo fixtures, and keeps provider degradation visible across routes without requiring every screen to duplicate provider-health chrome.
   - The shell now exposes a unified Investment Operations Path dock across every route, replacing route-local workflow breadcrumbs with one status-aware path from trusted data through research, paper readiness, portfolio ledger review, reconciliation, and governed report packs. Its labels, active/next state, route targets, and loaded/degraded/review statuses stay in the app-shell view model so operators can see where they are, what is blocked, and what comes next without memorizing page boundaries.
   - The dock now opens with a Decision Brief that synthesizes the ranked focus queue, linked symbol context, latest evidence, and current workflow step into one prioritized action. Brief copy, severity, rationale, evidence label, and action routing stay in the app-shell view model so the operator sees the best next decision before scanning the supporting lanes.
   - The same dock now includes a ranked Operator Focus lane that surfaces the highest-priority cross-workspace blockers from loaded workstation payloads, including operator work items, readiness gates, provider posture, brokerage sync, reconciliation breaks, portfolio risk, reporting targets, and strategy-run review. Focus labels, blocker details, severity, routes, loading/empty copy, and action copy stay in the app-shell view model so every route can show why the next operational issue is blocked without forcing the operator to visit the readiness console first.
   - The command palette now consumes that full ranked focus queue as first-class commands ahead of workspaces, quick routes, presets, and workflow actions, including items hidden behind the compact dock overflow label. `Ctrl+K` therefore opens the fastest route to any ranked issue, preserving the same severity labels, accessible action copy, and target routes used by the global dock.
   - The shell also preserves route-derived symbol context, such as `/data/quotes?symbol=MSFT`, as a clearable operating subject that follows symbol-aware workflow links and command-palette routes for live quotes and Price Alerts. This keeps market-data investigation, alert setup, and downstream review tied to the same instrument without requiring the operator to retype or remember the symbol on each page.
   - The shell now expands that subject into a clearable global operating scope covering symbol, fund account, run, provider, and date-window query context. Scope chips, persistence, clear behavior, workflow-step route preservation, left-rail/drawer routes, mega-menu routes, workspace command routes, quick routes, workflow commands, and preset commands are view-model-owned so operators can move between Data, Trading, Portfolio, Accounting, Reporting, Strategy, and Settings without rebuilding the account/run/timeframe context by memory.
   - The dock now adds a portfolio-aware Linked Context lane for the active or inferred symbol, exposing direct Data, Trading, Portfolio, Accounting, and Reporting drill-through routes with the same subject query preserved. Linked context labels, route targets, status tones, decision posture, primary next action, and accessible copy stay in the app-shell view model, and attention routes are ranked before clear routes so operators can carry a symbol from quote evidence through trade impact, exposure, reconciliation, and evidence review without rebuilding context page by page.
   - The same dock now exposes a cross-workspace Evidence Timeline that orders timestamped audit, execution, readiness, brokerage-sync, strategy-run, backfill/export, and reconciliation events from loaded workstation payloads. Timeline labels, UTC timestamps, severity tone, routes, and accessible link copy stay in the app-shell view model so operators can understand what changed recently without opening each workspace one by one.
4. Keep the first browser read-only surface focused on the Operator Readiness Console at `/trading/readiness`. It aggregates latest Strategy runs, active paper-session posture, DK1/provider trust, reconciliation breaks, promotion blockers, Accounting reconciliation posture, Reporting report-pack readiness, and operator-inbox work items from shared API payloads. It must hold the headline posture in review until the shared operator inbox loads cleanly and governed report-pack readiness has at least one loaded target, even when the trading-readiness payload already reports `Ready`. Account-scoped inbox reloads must clear prior account rows while the new inbox is loading, the inbox retry command should stay view-model-owned with loading/error/busy state, provider-trust metrics should report `Ready` only when every visible provider-trust row is ready, and Security Master coverage actions route through the Accounting workspace.
5. Preserve the closed Wave 2 cockpit acceptance baseline in the web UI: session restore, replay verification, execution controls, promotion rationale, and operator work items should continue consuming shared workstation endpoints. Current Trading cockpit evidence includes a refreshable readiness-contract summary for overall, paper-operation, brokerage-sync, and explicit UTC as-of posture plus a link into the read-only console. New dashboard work should treat those as regression anchors while active delivery moves through W4 governance and fund-operations workflows.
   - Shared brokerage portfolio sync now uses the split account query and account management contracts for link validation, balance-history reads, snapshot enrichment, and reconciliation triggers, so web Portfolio brokerage performance does not require the broad `IFundAccountService` registration when the narrower read/write account contracts are present.
   - Trading position close actions now flow through the keyed `POST /api/execution/positions/actions/close` contract. `/api/workstation/trading` includes `positionKey` on each position row, and the browser sends that key instead of relying on the older symbol-only close route so option and multi-position workflows can avoid ambiguous symbol matches. Trading destructive confirmations now require a view-model-owned review acknowledgement before cancel-order, cancel-all, close-position, pause-strategy, or stop-strategy actions can execute.
   - Trading order-ticket submission now has the same paper-first trust posture: the browser keeps Submit disabled until valid fields produce a preview and the operator explicitly acknowledges the impact/risk warnings, with direct form-submit attempts rejected in the view model.
   - Trading paper-session creation now surfaces the active lock reason in the view-model-owned form description while create, restore, verify, close, or initial load work is running, so disabled strategy/cash fields and form commands point to visible guidance instead of relying on hover-only titles.
6. Move data and governance operator workflows next: Data owns provider posture, backfill preview/trigger, export readiness, watchlists, live quotes, reference-data projection review, and data-quality handoffs, while Accounting owns Security Master coverage, search-result drill-in, details/lots/operator override review, and print/export packet review alongside reconciliation dense-table review, ledger review, close workflow previews, evidence packet actions, and report-line provenance previews. Evidence packet actions now have an initial browser path through `/reporting/evidence`, with links from Trading readiness, Strategy/Portfolio run context, Accounting reconciliation review and dense-table detail selection, and Reporting report-pack context; keep expanding that path through shared evidence subjects before adding page-local evidence state. Portfolio run evidence actions and Reporting workbench evidence actions are view-model-owned so selected run/profile context determines the route contract instead of JSX-local string construction. Export preview and analysis-export fallbacks should preserve the actual generated artifact names: symbol CSV files use stable hash suffixes to avoid collisions, and generated Notebook, Parquet, Excel, HDF5, ClickHouse, and Lean sidecars should point at those exact CSV filenames. The Settings workspace now carries a backend capability coverage panel, view-model-owned header posture chips, and an Alpaca setup checklist that guides operators from paper endpoint selection through account verification into Trading readiness while requiring explicit acknowledgement before any live endpoint credential test. Keep those panels current whenever new shared backend functionality is meant to be browser-accessible.
   - The Data watchlist now includes preset quick-add symbol packs backed by the same bulk symbol API as manual entry, so first-run operators can move from an empty table to live quote validation without guessing an initial universe.
   - Provider setup now reports created provider-routing connection and binding metadata, credential source, environment, and warnings in the Data setup dialog, and Settings refreshes provider-routing connections, bindings, and trust snapshots after setup plus during lightweight live refresh so setup repair immediately feeds routing posture instead of waiting for a full workstation reload.
   - The Data watchlist dense table now keeps sortable-column metadata in the shared table primitive, preserving visible sort controls and `aria-sort` state for symbol, status, price, spread, and quote-age review.
   - The Data watchlist remove action now requires a view-model-owned confirmation click before the symbol is removed, keeping accidental row deletion from disrupting live quote review.
   - Security Master lots now require a view-model-owned confirmation click before removing a locally recorded cost-basis lot, so operators cannot erase manual lot evidence with a stray table action.
   - The Data backfill queue now uses the shared dense-table selected-row contract, so historical repair jobs are easier to scan, select with the keyboard, and inspect from `/data/backfills`.
   - Governance reporting export links, Portfolio brokerage-sync backend links, and Strategy promotion-to-Trading handoffs are now view-model-owned route contracts backed by `workstation-endpoints.ts` or shared workspace metadata; keep new browser handoffs out of JSX-local string literals.
   - Strategy Designer backend actions now use the endpoint catalog and render browser-openable GET routes separately from reference-only POST validation, preview, and run-backtest commands, so operators can inspect evidence endpoints without accidentally treating mutations as browser navigation.
   - The `/portfolio` readiness handoff and `/portfolio/brokerage-sync` task panel now expose view-model-owned next-action labels, detail IDs, provider repair, Trading readiness, Trading cockpit, and linked evidence routes, so portfolio review moves operators into the next trust-building step instead of stopping at backend payload links or screen-reader-ambiguous action groups.
   - The `/reporting/report-packs` task panel now starts with the strongest available export profile selected, preferring recommended and fully evidenced profiles, so report-pack preview/export actions are immediately visible without an extra discovery click.
   - The `/reporting/report-packs` backend endpoint panel now opens only concrete GET endpoints and renders the POST export mutation as reference-only, matching Settings capability semantics so operators do not accidentally browser-open a mutating route as a confusing GET.
   - Research promotion-to-paper-session controls now expose view-model-owned disabled reasons for unevaluated runs, ineligible runs, missing/invalid initial cash, and in-flight session creation, keep the setup form visible but locked while the paper session is being created, and ignore duplicate rapid submit, setter, or cancel attempts so operators cannot accidentally start, mutate, or hide two validation sessions from one promotion decision.
   - Quant Notebook cell deletion now requires a view-model-owned confirmation click, and normal notebook edits/actions clear the pending delete state so research notes and code are not erased by a stray toolbar click. Clearing all notebook outputs now follows the same view-model-owned confirmation pattern and stays disabled until there is execution output to clear. Data-context fetch locks and running-cell source locks now expose visible, view-model-owned disabled feedback linked from the disabled fields with `aria-describedby`, so operators can see why a notebook field is temporarily unavailable without relying on hover-only titles.
   - Watchlist live-quote and bulk-add failures hand off directly to the Settings provider setup anchor, so operators can repair credentials or connection posture from the failing data workflow instead of stopping at an inline error.
   - Live Quotes quick-trade accepted and rejected submission states now expose a view-model-owned Trading readiness handoff, so operators can immediately inspect paper cockpit readiness and execution-control evidence after an order attempt instead of stopping at terminal ticket copy.
   - Price Alerts creation success now exposes a view-model-owned live-quote handoff for the created symbol, so operators can immediately validate the quote feed that will drive the alert.
   - Price Alerts configured-alert deletion now requires a view-model-owned confirmation click with inline status text linked to the confirm button, so operators cannot accidentally erase a watched symbol or miss that a destructive action is pending while triaging alert state.
   - Price Alerts local persistence failures now surface a visible, live-region storage warning while keeping the in-memory alert active, so operators know when a browser-storage or quota problem may prevent alerts from surviving reload.
   - Strategy Designer canvas legs expose stable view-model field IDs plus move-up/down commands, so multi-leg strategies can be tuned and reordered without relying on pointer-only drag behavior.
   - Strategy Designer canvas clearing now requires a view-model-owned confirmation click, and normal canvas edits clear the pending confirmation so multi-leg strategies are not erased by a stray toolbar action.
   - Covered Call chain preview and previous-run history now use the shared dense-table selection contract with fixture-backed run history, deterministic UTC labels, keyboard reload, and safe chain-preview POST support, so `/strategy/covered-call` remains demo-ready without a live API host while real run creation stays unmocked.
   - Covered Call run submission now exposes view-model-owned run/cancel command states, disabled reasons, progress copy, and post-result handoffs into live quote validation, Strategy Designer refinement, and report-pack packaging, so operators can see whether a backtest is invalid, submitting, queued, running, complete, or no longer cancellable without guessing and can move directly into the next workflow.
   - Covered Call run and cancel commands now render their view-model-owned disabled, confirmation, or busy feedback inline and link it with `aria-describedby`, so operators are not dependent on hover-only titles to understand why a backtest cannot start or cancel yet, and accepted runs require an explicit confirm step before cancellation is sent.
   - Covered Call stage navigation now exposes the same unresolved submit/cancel lock reason as visible view-model feedback linked from the disabled step buttons, so operators can understand why the wizard is pinned to run progress without relying on hover text.
   - Covered Call completed-run trades now use the shared dense-table row/detail contract with view-model-owned labels, keyboard selection, assignment status, and premium/PnL evidence fields, so operators can inspect individual covered-call trade outcomes instead of trusting only aggregate run metrics.
   - Data provider health now uses the shared dense-table plus selected-detail contract for trust, latency, gate impact, and recovery guidance, so operators can keyboard-inspect degraded providers without scanning repeated card copy.
   - Operator Readiness Console timestamps now render explicit UTC labels for the headline snapshot, active paper-session creation, and selected work-item creation detail, so operators can judge queue freshness without parsing raw backend timestamps.
   - Dashboard API helpers preserve backend error details consistently across GET, POST, PUT, and DELETE, include field-level validation-problem details, tolerate HTTP 200/204 no-content success bodies, carry abort signals for superseded workstation bootstrap, trading readiness, operator-inbox, live market-data, and historical-chart refreshes, and keep Watchlist remove commands on the current row set when the backend returns an unsuccessful mutation result.
   - Settings Alpaca credential connect and clear commands now pass abort signals through the shared API helpers and abort in-flight brokerage requests when the setup panel unmounts, so provider credential work cannot keep running after the operator leaves Settings.
   - Settings Alpaca credential clearing now requires a view-model-owned confirmation step before Meridian sends the revoke request, so operators cannot accidentally remove the stored provider key reference while repairing setup.
   - Portfolio brokerage empty/no-projection states now use the same Settings provider setup handoff, so a disconnected or unhydrated household portfolio gives operators an immediate recovery path instead of passive empty-state copy.
   - Portfolio brokerage accounts now use the shared dense-table row/detail contract with keyboard row selection, VM-owned aggregate/account inspector state, and synced/warning labels, so operators can filter positions and inspect account trust posture without scanning detached account cards.
   - Trading readiness and Operator Readiness Console BrokerageSync work items now expose the same Settings provider setup handoff, so a blocked paper cockpit can move directly from the failed readiness item to credential repair.
   - Covered Call completed runs now expose trade history through the shared dense-table row/detail contract with keyboard row selection, VM-owned PnL/assignment labels, and an accessible selected-trade inspector.
   - The Overview "Today" empty state now sends first-run operators directly to `/settings#alpaca-provider-setup`, so the primary landing screen opens the actionable provider checklist instead of a generic Settings page.
   - The Overview portfolio cockpit empty states now expose route-backed actions owned by the view model: unhydrated portfolios open `/settings#alpaca-provider-setup`, while connected accounts with no open positions open `/trading` for the first paper-order path.
   - The Overview priority routes now use the live status snapshot to send zero-symbol installations to `/data/watchlist` and provider-baseline gaps directly to `/settings#alpaca-provider-setup` before defaulting to trading/accounting/reporting triage. The same overview view model also projects readiness blockers with direct repair links for provider degradation, missing symbols, storage warnings, active backfills, and recent system errors so first-run operators can see why the workstation is not yet trustworthy.
   - Watchlist symbol-add and starter-pack success feedback now exposes a view-model-owned live-quote handoff for the first requested symbol, so first-run setup moves directly from subscription into quote validation.
   - Watchlist subscribed-symbol load failures now expose a view-model-owned retry command inside the failed list panel, so operators can recover the Data workflow from the exact error state instead of hunting for a generic refresh control.
   - Accounting Security Master identifier-conflict loads now expose a view-model-owned refresh/retry command, and that command now locks with visible feedback while a conflict resolution is pending, so trust-critical reference-data failures remain recoverable without letting operators reload stale identity state mid-action. Reconciliation break detail timestamps use deterministic UTC labels from the view model so break freshness does not depend on the operator's local browser timezone.
   - Accounting Security Master detail review now keeps asset-class field visibility, hidden override counts, corporate-action dense-table detail state, and the master/detail command-deck summary in view models so operators can scan relevant reference data, schedules, controls, lots, conflicts, and audit cues without losing the selected security context.
   - Accounting reconciliation break actions now select the acted-on break before opening assignment or resolution workflows, keeping the dense-table detail panel, rationale form, and queued action context aligned even when operators act from a non-selected row. Break detail panels also expose routed handoff actions from the view model, so reconciliation findings can move directly to the appropriate accounting evidence route instead of leaving routing metadata as passive text.
   - Evidence Workbench manifest exports now return a retained `/workstation/evidence/...` route that the host serves from `DataRoot/workstation/evidence`, include vault identity/index metadata for retained-manifest lookup, and the browser success state exposes a view-model-owned open-manifest action for immediate audit review. Treat this as manifest retention and lookup support, not as a page-local evidence store or full Evidence Vault.
7. Treat Buyer Demo Mode and role-based demo views as browser-dashboard packaging on top of seeded shared evidence, not as separate fixture-only UI. The demo path should prove the same readiness, accounting, reconciliation, and report-pack contracts the operator workflow uses.
8. Keep Evidence OS UX ideas such as confidence rails, lifecycle breadcrumbs, "why am I blocked" panels, and evidence-first empty states grounded in shared readiness/evidence payloads rather than page-local copy. The Evidence Workbench should stay a shared packet/graph consumer, not a separate evidence store.
9. Keep WPF tests as a first-class validation lane for desktop workflows, while preserving shared-contract compatibility with the browser workstation.
10. Treat the existing web Research run library and gated Quant Lab surface as Wave 3 support evidence only; they do not close strategy-aware launch/preflight, persisted sweep grouping, or Backtest Studio unification.

## Validation

Use the narrowest validation for the files touched:

```bash
cd src/Meridian.Ui/dashboard
npm run dev
npm run preview
npm run test
npm run build
```

The Vite dev server hosts the browser shell under `/workstation/`; Vite preview serves the built
assets from `src/Meridian.Ui/wwwroot/workstation/`. Both commands proxy `/api` to
`MERIDIAN_API_BASE_URL` or `http://localhost:8080`. When `/healthz` is unavailable in local dev or
preview, the proxy serves typed fixtures for known bootstrap GETs before forwarding so no-host demos
do not show failed-resource noise. The shell surfaces a `Demo data` banner when fixture-backed
responses are used, with a route-aware evidence path linking to the watchlist, seeded `AAPL` live
quotes, readiness console, and Alpaca paper provider setup checklist plus a retry-live-data action
that reruns workstation bootstrap, so operators can distinguish no-host demo data from live
workstation state, review the same read-only evidence flow without credentials first, and then move
directly to the live-data handoff. The Strategy no-host path must include fixture-backed promotion evaluation GETs so `/strategy` can open the initial-cash paper-session handoff without a background 500. The Trading cockpit no-host path must also include fixture-backed paper-session,
replay-file, replay-verification, execution-audit, execution-control, and promotion-history GETs so first-run demos
do not show background 500s while the local API host is unavailable. Accounting reconciliation no-host routes must include the
fixture-backed break-queue GET so `/accounting/reconciliation` opens without failed-resource noise. The quote demo path must include
fixture-backed quote, trade, depth, and historical-bar responses so `/data/quotes?symbol=AAPL` opens as a populated workbench
instead of an empty symbol prompt; manual quote lookup must normalize symbols, explain empty input,
avoid firing live-market requests until a symbol is present, normalize historical bars
chronologically, and drop malformed historical-bar rows before building chart stats or SVG geometry
so one bad provider response cannot corrupt the visible trend. Keep browser workstation API route
strings centralized in the dashboard endpoint catalog so Settings, brokerage connection actions,
readiness, portfolio, run-history, workflow-summary, strategy-run drill-ins, Security Master,
backfill preview/progress/checkpoint recovery,
reconciliation, reporting export, provider, backfill, symbol, quality, market-data, Quant Lab, and
fetch helpers share the same current workspace contract. Dynamic route builders should reject blank
required path segments before fetches are issued, and query-string construction should trim strings,
drop blank or non-finite values, and serialize repeated symbol inputs through the shared helper. No-host
Vite fixture routes, dynamic fixture matchers, and fixture workflow action prefixes must also derive
from that catalog rather than restating `/api/*` literals, so demo fallback data continues to track
production endpoint names. Retained shared UI services should use `UiApiRoutes` and encoded route
builders for provider-management, provider-routing, schedule, and system-health API calls instead
of service-local string interpolation, so desktop support code follows the same route-contract rule
as the browser workstation. Cross-screen workflow links should normalize local `/workstation/...` routes, legacy
workspace aliases, query strings, and hashes through shared workspace metadata before falling back
to page-tag routing; API routes, protocol-relative routes, external URLs, and unknown workspace
paths must not become browser navigation actions. Evidence Workbench source-workflow links should
prefer packet subject routes, but fall back to shared page-tag routing when the evidence payload
omits a direct route or returns a non-workstation/API route. Evidence
Workbench packet validation and manifest export controls should expose disabled/busy state from the
view model and use per-command revision guards so overlapping same-subject completions cannot
replace newer results. Evidence Workbench load failures should remain visibly recoverable through
the view-model retry command and must not render the no-subjects empty state while the API failure
is unresolved. Settings capability coverage should keep concrete
GET endpoints as browser-openable evidence links and show templated GET plus POST/PUT/DELETE
endpoints as reference-only rows, so operators do not open placeholder URLs or trigger confusing
browser GETs against mutating API routes. Hash-targeted workflow links must keep watching
lazy-rendered route content until the requested target panel exists before falling back to the
workbench, so repair handoffs such as `/settings#alpaca-provider-setup` land on the actionable
panel instead of only announcing the route.
Watchlist action labels, Quant Lab source/template controls, Trading promotion-gate form metadata,
Live Quotes quick-trade ticket fields, recent-trade row/detail state, Security Master search-result rows, and reconciliation
resolve-dialog actions should remain view-model-owned so placeholders, ARIA labels, selected-row
state, disabled requirements, and action state do not drift from the shared operator contract.
Report-pack approval task actions should likewise expose selected-profile preview and export
commands in the route-specific task panel while keeping mutating backend endpoints reference-only.
Reporting export commands should pass `AbortSignal` through the shared export API and cancel
superseded same-profile or profile-switch exports, so late export results cannot overwrite the
operator's current selected report-pack profile.
Live Quotes quick-trade submissions should also keep order-ticket fields locked while an order is
submitting and ignore late submit results after the operator switches symbols, so accepted/rejected
order feedback cannot appear under the wrong active symbol.
Trading order-ticket submissions should keep a ref-backed pending guard in addition to disabled
button state, so a rapid duplicate submit from one render cannot send two order requests for the
same operator acknowledgement.
Covered-call backtest submissions should keep the wizard pinned to run progress while submit or
cancel actions are unresolved, with view-model-owned disabled reasons on Back and stage navigation,
so operators cannot hide an in-flight strategy request before the engine accepts or rejects it.
Covered-call primary actions should render disabled/busy feedback inline and attach it to the
owning button with `aria-describedby`, because disabled button titles alone are not reliable
operator guidance.
Evidence Workbench subject/packet loads plus packet validation/export commands, Trading readiness and Operator Inbox
refreshes, Live Quotes market-data and historical-chart requests, Watchlist symbol/quote
refreshes, Accounting Security Master drill-ins, and Settings Alpaca credential
actions, including live-endpoint acknowledgement gates, should keep their active-workstream and mounted/revision guards so late async completions
cannot overwrite newer operator state or repopulate hidden panels. Request-backed refreshes should
also pass `AbortSignal` through the shared API helpers when a newer route, account, symbol, or
timeframe supersedes the in-flight request.
Data provider setup should follow the same command discipline: provider kind, display name,
credential, endpoint, and capability controls stay disabled while setup is submitting, credentials
are cleared after completion, and late provider setup responses are ignored if a newer submit or
dialog reset supersedes them. The provider setup success state now exposes view-model-owned next
validation routes so first-run operators can move directly into live quotes, backfill preview,
Trading readiness, or Security Master coverage based on the configured capabilities.
Data backfill queue rows should use the shared dense-table selected-row contract, with keyboard
selection, selected row state, progress posture, and route-focus detail labels kept in view-model
state rather than button-card markup.
Quant Lab plot rendering should keep chart geometry, axis ticks, unsupported-state copy, ARIA
descriptions, and reusable preparation helpers in the plot view model rather than in React markup,
so heatmap/candlestick/line/bar/scatter/histogram support remains testable outside the screen.
Quant Notebook data-context fields and cell source editors should keep disabled feedback IDs,
described-by wiring, and blocked-state copy in the notebook view model, with visible inline field
feedback whenever fetch or execution state locks editing.
Accounting reconciliation should follow the same view-model ownership rule: shared dense-table rows, keyboard selection, selected-run state,
expanded row semantics, open-break copy, and no-run detail guidance belong in the reconciliation
queue panel view state so the screen renders route-backed accounting evidence without JSX-local
state contracts. The Accounting overview reconciliation summary should stay read-only and expose a
view-model-owned `/accounting/reconciliation` handoff, while the dedicated reconciliation route owns
the selectable detail queue.
The dashboard Vitest config caps forked workers so the jsdom-heavy component suite remains a
repeatable `npm run test` gate on workstation machines without relying on serial-only runs.
The shell should keep route screens behind dynamic imports so production builds split heavy
workstation surfaces into route chunks instead of shipping every cockpit, chart, and governance
screen in the initial bundle.
The shared shell degraded-bootstrap panel should include workflow-catalog failures alongside
workspace payload failures, and trading-only refresh failures should preserve the last good cockpit
payload while surfacing a retryable Trading slice error. Superseded full-bootstrap, trading-only,
readiness, operator-inbox, live-market, and historical-bar refreshes should abort their underlying
fetches as well as guard state publication, so late network work does not keep running after the
operator triggers a newer refresh or leaves the shell. Evidence
Workbench lineage rows, empty
lineage copy, table labels, and relationship labels should continue to come from the workbench view
model rather than JSX-local packet edge rendering. Evidence packet timestamps should render in
deterministic UTC labels, and validation/export commands should serialize through view-model command
state so one packet mutation cannot race another.
Portfolio brokerage-sync trust snapshots should also format generated/synced timestamps as explicit
UTC labels so operators do not have to infer time zones when accepting account state.
Overview control-tower heartbeat labels, recent activity timestamps, Settings diagnostic
heartbeat/event evidence, and Price Alerts trigger/last-poll timing should use explicit UTC labels
instead of raw ISO or browser-local time.
Live Quotes trade print and last-update timestamps should also render deterministic UTC labels from
the view model so operators can compare quote, depth, and tape freshness without inferring browser
time zone.
Price Alerts creation success should keep its live-quote validation handoff in the view model so the
created symbol, encoded route, and accessible action label stay testable outside JSX.
Price Alerts persistence warnings should stay view-model-owned and visible in a live region whenever
browser storage rejects or cannot provide a target for alert state, because local-only alerts must not
look durable when they may be lost on reload.
Strategy Designer canvas fields and reorder actions should stay view-model-owned so labels,
disabled reasons, and keyboard-operable ordering remain testable outside JSX.
Strategy Designer destructive canvas actions should keep their confirmation labels and pending state
in the view model so Clear canvas remains keyboard-accessible and testable outside JSX.
Operator Readiness Console snapshot and work-item creation timestamps should remain formatted in
the view model as deterministic UTC labels, including selected-row detail evidence, so the queue can
be audited without browser-local time ambiguity.
Workflow preset launches should merge the returned preset mutation into the shell's in-memory
workflow catalog immediately, preserving pinned/last-used ordering without requiring a full
workspace bootstrap reload. Persisted workflow preset snapshots are versioned under
`DataRoot/workstation/workflows/workflow-presets.json`; the shared store writes them through
`AtomicFileWriter`, serializes with a source-generated JSON context, and rejects unsupported
snapshot versions rather than loading unknown durable shapes.
Shared backfill checkpoint resume endpoints should derive child routes from
`UiApiRoutes.BackfillCheckpoints` and propagate `HttpContext.RequestAborted` into the resumed
backfill run, so route constants and request cancellation stay coherent across the web and retained
desktop clients. The Settings capability coverage panel should surface checkpoint history,
resumable jobs, validation, pending-symbol, and resume routes so operators can find recovery
evidence without leaving the browser workstation.
Fixture fallback data must stay neutral; do not emit dashboard-dev fallback notices as
operator-facing system events.

Broaden to .NET validation when web changes touch shared contracts or endpoints:

```bash
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj /p:EnableWindowsTargeting=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~MapWorkstationEndpoints" --logger "console;verbosity=normal"
```

## Exit Criteria

- The web dashboard has passing unit/component tests and a reproducible production build.
- The web dashboard can navigate the canonical operator workspaces without depending on WPF shell state.
- Wave 2 cockpit readiness can be reviewed from the browser using shared API contracts, starting with the read-only Operator Readiness Console under the Trading workspace.
- Wave 3 shared run/portfolio/ledger continuity and Wave 4 governance workflows have web-visible operator paths.
- Browser and desktop both remain healthy operator surfaces, with shared contracts preventing duplicated business logic between them.
