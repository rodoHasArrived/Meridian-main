# Meridian - Project Roadmap

**Last Updated:** 2026-05-27
**Status:** Active productization — the narrow Wave 1 trust gate is repo-closed, DK1 provider trust now has a signed 2026-04-27 pilot replay/sample-set parity packet, valid packet-bound operator sign-off, and cockpit readiness projection; Waves 2-4 remain the core operator-readiness path. Browser and WPF operator UI development are both active: the browser workstation lives in `src/Meridian.Ui/dashboard/` with built assets served from `src/Meridian.Ui/wwwroot/workstation/`, and the Windows desktop workstation lives in `src/Meridian.Wpf/`. New product behavior should land behind shared contracts, local/web API endpoints, or shared read models before either client composes it. Current post-May-9 deltas add Security Master/UFL reference-data projection support, browser provider-setup validation handoffs, browser Data backfill queue dense-table selection, browser Trading loading-state panel and Recent Fills detail support, browser Live Quotes trade-detail selection plus quick-trade readiness handoffs, browser Accounting reconciliation dense-table detail-queue support with keyboard selection and no-host break-queue fixtures, Quant Lab plot view-model extraction, Quant Notebook cell/data-fetch helpers, crash-safe shared UI persistence, WPF Data shell feature-module support, Overview Today panel support, local Price Alerts, visual Strategy Designer, Covered Call chain preview, saved-run history, and selectable trade-timeline detail state at `/strategy/covered-call`, full-console readiness checkpoint gates with provider-setup repair handoffs for BrokerageSync blockers, Meridian Design System reference workbench/tokenized-color support, view-model-owned browser workflow polish for hash-aware command routing, query-string route focus for subject/symbol handoffs, Portfolio brokerage-sync next actions, Watchlist no-quote/empty-state copy, Overview status/refresh presentation, Reporting report-pack actions and endpoint-link safety, Security Master lot-detail/conflict refresh state, UTC Trading readiness labels, accessible disabled-reason support, session/role-aware API error summaries, web-workstation installer config repair, fail-closed brokerage order-placement gating, ledger/promotion endpoint authorization checks, execution metadata sanitization, expanded factor-based fixed-income accounting coverage for MBS/ABS/loan instruments, Strategy Engine definitions/validate-run support, additive workstation continuity payload guards, provider capability matrix ownership, and structured roadmap/source documentation controls; they are support evidence, not wave exits.
**Repository Snapshot (2026-05-21 current repo):** solution/build/test project files: 40 | `src/` project files: 27 | test projects: 9 | workflow files: 10

Meridian is no longer primarily blocked on missing platform primitives. The repo already contains strong market-data, storage, replay, backtesting, execution, ledger, workstation, and Security Master foundations. The remaining delivery problem is now narrower and more product-shaped: prove operator trust, close workflow gaps, and deepen governance in one shared-contract-first operator workflow across browser and desktop UI surfaces without letting the product split into parallel subsystems.

The active roadmap therefore centers on five outcomes:

- prove operator trust with evidence-backed provider, checkpoint, and replay validation
- close Wave 2 cockpit reliability first, with desktop acceptance tracked through `docs/plans/desktop-ui-workflow-acceptance-matrix.md`
- close Wave 3 continuity as one run -> portfolio -> ledger -> reconciliation workflow across high-traffic operator pages
- close Wave 4 governance and close/report lifecycle through durable casework, sign-off, and reporting controls
- keep browser and desktop parity locked to shared contracts, shared endpoints, and shared read-model behavior

## Commercial Positioning

Meridian should be positioned as an evidence-backed investment operations platform and the system of record for investment decision evidence. The commercial wedge is not "another trading workstation"; it is the ability to prove what happened across trusted data, research, paper trading, accounting, reconciliation, and governed outputs.

The flagship product story is the **Meridian Assurance Loop**:

```text
Data Trust Passport
-> Run Evidence Graph
-> Promotion Passport
-> Accounting-Grade Paper Trading
-> Governed Report Pack
```

This framing sharpens Waves 2-4 without creating a new wave. The current repo has support evidence for pieces of the loop, including provider trust packets, shared run continuity, paper-session replay/audit metadata, promotion checklist state, reconciliation break queues, report-pack schema/version/validation checks, shared evidence packet/graph/vault-manifest APIs, browser Evidence Workbench actions, and the read-only web Operator Readiness Console. The named commercial modules remain roadmap targets until shared contracts, retained evidence, and browser-visible workflows prove them end to end.

The next accounting-led commercial wedge should be: **Can Meridian prove, book, reconcile, approve, and report this investment decision?** Ideas such as Books Before Broker, an investment accounting Transaction Lab, Close the Books workflow, broker statement reconciliation, Controls-as-Code, and one-click evidence packets should be treated as roadmap targets layered onto Waves 2-4. They are not current implementation claims; future delivery should start with shared evidence/accounting/readiness contracts and browser-dashboard workflows before any client-specific UI expands.

The umbrella product story can be **Meridian Evidence OS**: a governed investment operating system that proves the lifecycle from data to decision to books to report. Evidence OS should connect the Run Evidence Graph, Evidence Vault, Evidence Completeness Score, decision lineage, approval packets, replay verification, report-line provenance, and governed report packs. The current evidence subject, packet, graph, validation, and manifest-export seams are support evidence for that direction; treat Evidence OS itself as positioning for Waves 2-4, not a new platform implementation or wave.

The documentation rule attached to this positioning is explicit: prior plans, UI notes, and status snapshots that are deprecated by the evidence-backed investment-operations direction should move to `archive/docs/` and be referenced only as historical context. Active roadmap docs should point to the current evidence plan and the Wave 2-4 implementation anchors.

---

Use this document with:

- [`../plans/current-direction-and-status.md`](../plans/current-direction-and-status.md) - consolidated current direction, status, and plan-file roles
- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) - current capability status
- [`FULL_IMPLEMENTATION_TODO.md`](FULL_IMPLEMENTATION_TODO.md) - normalized non-assembly backlog
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) - completed improvement history
- [`production-status.md`](production-status.md) - current readiness posture and provider-confidence gates
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) - prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) - concise end-state product summary
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) - shortest combined roadmap, opportunity, and target-state entry point
- [`../plans/evidence-backed-investment-operations-plan.md`](../plans/evidence-backed-investment-operations-plan.md) - active differentiation and product-category filter
- [`../plans/meridian-pilot-workflow.md`](../plans/meridian-pilot-workflow.md) - golden-path pilot workflow and productization filter
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state
- [`../plans/brokerage-portfolio-sync-blueprint.md`](../plans/brokerage-portfolio-sync-blueprint.md) - external brokerage and custodian account-sync design
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) - current short-horizon execution plan
- [`../plans/waves-2-4-operator-readiness-addendum.md`](../plans/waves-2-4-operator-readiness-addendum.md) - concrete owner-based workstreams, dependencies, and exit criteria for the active Waves 2-4 path
- [`../plans/desktop-ui-workflow-acceptance-matrix.md`](../plans/desktop-ui-workflow-acceptance-matrix.md) - desktop/WPF acceptance filter for W2 cockpit, W3 continuity, and W4 governance workflow claims
- [`../plans/web-ui-development-pivot.md`](../plans/web-ui-development-pivot.md) - browser workstation foundation and desktop/browser coexistence history

---

## 2026-05-18 Planning Review

The project plan set was reviewed against the current `AGENTS.md` direction, shared Codex project
context, and the active roadmap source map. The consolidated planning entry point is now
[`../plans/current-direction-and-status.md`](../plans/current-direction-and-status.md), with
[`../plans/README.md`](../plans/README.md) retaining the detailed file-by-file classification. The
controlling interpretation is unchanged: Waves 2-4 are the core operator-readiness path, browser and WPF operator workflows are both active, shared contracts remain the integration boundary,
and mobile-specific product lanes remain out of scope.
Plan documents that are target-state packages, optional tracks, or later-wave blueprints remain
active only in the role named by the plan index; they do not alter the wave status table below.

2026-05-19 follow-up: the current release/distribution delta is support tooling, not a wave exit.
Generated publish output is expected under ignored `artifacts/publish/` paths, the publish script
now exposes `-SizeOptimized` for standalone size and low-disk investigations, and
`build/scripts/publish/measure-size.ps1` reports common repo-local output roots. The generated TODO
scan is also being cleaned so local diagnostic tool caches do not inflate project TODO counts.
These changes improve developer/release hygiene but do not close W2 cockpit, W3 continuity, W4
governance, or live-readiness gates.

2026-05-19 governance follow-up: newer repo evidence strengthens Wave 3/Wave 4 support without
changing wave status. Ledger writes now preserve `posting_kind` and enforce open, soft-closed, and
hard-closed period posting rules in the ledger store; governed report packs now carry structured
validation/lifecycle metadata; evidence manifest exports now have retained vault identity, index,
lookup, and a host-served manifest route; and reconciliation case storage now has safer path
handling, atomic writes, audit JSONL append, status normalization, and transition validation. The
active worktree also has a Security Master validation-gate/snapshot slice that can block unresolved
or unapproved records for strategy preflight, ledger posting, reconciliation intake, report-pack
evidence, and override approval workflows. Treat all of this as continuity and governance support
evidence, not as completion of durable close casework, report publication, Evidence Vault, or
live-readiness gates.

2026-05-19 operations-continuity follow-up: the account-period close lane is now more concrete
than earlier roadmap wording implied. Shared operations DTOs, the `OperationsContinuityWorkflow`
aggregate/service, hash-chained audit timeline, file-backed workstation repository registration,
optional transactional commit seam, and `/api/workstation/operations/continuity*` endpoints cover
broker import/normalization, Security Master resolution and governed override approval, ledger
draft/validate/post, reconciliation, approval, close, and governed reopen with trusted actor
handling and permission-gated mutations. This is significant W3/W4 support evidence for close
workflow orchestration, but it remains below a Wave 4 exit until browser/operator acceptance,
external statement/custodian intake, durable generalized casework, report publication controls, and
operator-approved calibration are proven.

2026-05-19 browser-workstation follow-up: the active dashboard now has more operator-safety
evidence without changing wave status. Provider setup seeds visible provider-routing connection and
binding metadata, credential source, environment, and warnings, and Settings refreshes
provider-routing connections, bindings, and trust snapshots after setup and during lightweight
refresh. Strategy Designer backend route actions now render browser-openable GET routes separately
from reference-only POST validation, preview, and run-backtest mutations. Reporting export commands
now propagate `AbortSignal` through `runAnalysisExport` and abort superseded same-profile or
profile-switch exports before stale results can replace the current selection. Treat these as
support for W2-W4 operator trust, not cockpit, Backtest Studio, or report-pack lifecycle closure.

2026-05-20 security/release follow-up: newer repo evidence adds fail-closed enforcement around
brokerage order placement, explicit authorization checks on ledger and promotion endpoints,
execution-order metadata sanitization before API/audit exposure, CodeQL and gitleaks hardening in
CI, and web-workstation installer upgrade repair that archives legacy installs, removes stale
shortcuts, and falls back invalid provider-specific configs to `Synthetic` when required provider
settings are missing. The browser workstation also now maps 401/403 API failures to
session/role-specific recovery messages and replaces tooltip-only disabled reasons with accessible
field support text in high-risk forms. Accounting support has broadened factor-schedule handling
for mortgage-backed, asset-backed, loan, and amortizing-loan instruments. These reduce operational
and security risk, but they do not close W2 cockpit acceptance, W3 shared continuity, W4
close/report acceptance, Evidence Vault, report-line provenance, or live-readiness.

2026-05-21 strategy/contract/docs follow-up: the current repo adds a shared Strategy Engine
foundation with explicit strategy definitions, parameter schemas, data-dependency policy,
pre-run validation, evidence hashing, and workstation definitions/validate-run endpoints for
Covered Call and visual designer flows. Shared continuity contracts now also have an additive-only
ledger/reconciliation/strategy payload profile with compatibility tests, and provider readiness is
tracked through a canonical provider capability matrix. The documentation system now has structured
roadmap/source registries, generated source README coverage, stale-doc marking, and hash validation.
These are governance and contract-control improvements for W2-W5; they do not close Backtest
Studio unification, Wave 2 cockpit acceptance, W3 continuity acceptance, W4 close/report
acceptance, or live-readiness.

Implementation follow-up on the acceleration plan: the golden-path pilot harness is now treated as
the executable proof lane. `PilotAcceptanceHarnessTests` exercises the shared service and endpoint
path for trusted data, research run, run comparison, paper promotion, paper session replay,
portfolio/ledger review, reconciliation, and governed report pack, then writes
`artifacts/pilot-acceptance/latest/pilot-readiness.json` and
`artifacts/pilot-acceptance/latest/pilot-readiness.md`. The `golden-path-validation.yml`
workflow runs that harness, validates the pilot dashboard renderer, generates the markdown/JSON
dashboard under `artifacts/pilot-acceptance/latest/`, and uploads `pilot-acceptance-evidence`.
This is W2-W4 support evidence and a stronger acceleration mechanism; W2 and W3 are now canonical
`Done` in `PROGRAM_STATE.md`, while W4 remains `In Progress` until close/report governance sign-off
criteria are met. Every W2/W3/W4 readiness claim must now be stage-gated: either it turns one of the
`pilot-readiness.*` stage gates green, or the stage gate records the unresolved blocker in its
`blockers` list. W2 claims map to `TrustedData`, `PaperPromotion`, and `PaperSession`; W3 claims map
to `TrustedData`, `ResearchRun`, `RunComparison`, `PaperPromotion`, `PortfolioLedgerReview`, and
`Reconciliation`; W4 claims map to `TrustedData`, `PortfolioLedgerReview`, `Reconciliation`, and
`GovernedReportPack`.

2026-05-27 desktop workflow follow-up: desktop/WPF claims now have a dedicated acceptance filter in
[`../plans/desktop-ui-workflow-acceptance-matrix.md`](../plans/desktop-ui-workflow-acceptance-matrix.md).
Use it before treating retained-WPF changes as Wave 2, Wave 3, or Wave 4 progress. The matrix maps
Lane A W2 cockpit reliability, Lane B W3 run -> portfolio -> ledger continuity, and Lane C W4
reconciliation/governance close flow to required operator scenarios, shared endpoint/read-model
checks, focused WPF tests, browser parity checks, release blockers, and milestone evidence; support
evidence remains separate from exit claims until the matching happy path, blocker path, and recovery
path evidence is present.
The same 2026-05-27 desktop pass added W4 support evidence in Fund Ledger: selected reconciliation
breaks now show lifecycle and required sign-off posture from shared queue metadata in the retained
WPF detail panel. Treat this as blocker-path support evidence, not durable close/casework
acceptance, until operations-continuity and browser-parity gates are green.
It also added W3 support evidence in Run Cash Flow: the retained WPF cash-flow drill-in now consumes
`StrategyRunContinuityService` and shows shared portfolio, ledger, cash-flow, reconciliation, and
warning posture for the selected run. Treat this as blocker-path support evidence, not Milestone 2
completion, until the full run -> portfolio -> ledger -> reconciliation workflow and browser parity
are proven.

---

## Canonical Program State

Program wave status is canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md). Any wave status wording in this file is explanatory context only. Generated ownership and escalation routing for status summaries is published in [`program-state-summary.md`](program-state-summary.md).

<!-- program-state:begin -->
| Wave | Owner | Primary Owner | Backup Owner | Escalation SLA | Dependency Owners | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Data Confidence and Validation | Trading Workstation | 4 hours / 1 business day | Trading Workstation; Shared Platform Interop; Governance and Ledger | Done | 2026-04-17 | [`production-status.md#provider-evidence-summary`](production-status.md#provider-evidence-summary) |
| W2 | Trading Workstation | Execution and Fund Accounts | Workstation Shell and UX | 4 hours / 1 business day | Shared Workflow and Contracts; Data Confidence and Validation; Governance and Ledger | Done | 2026-05-27 | [`../plans/current-direction-and-status.md`](../plans/current-direction-and-status.md) |
| W3 | Shared Platform Interop | Shared Workflow and Contracts | Workstation Shell and UX | 1 business day / 2 business days | Execution and Fund Accounts; Governance and Ledger; Data Confidence and Validation | Done | 2026-05-27 | [`../plans/current-direction-and-status.md`](../plans/current-direction-and-status.md) |
| W4 | Governance + Fund Ops | Governance and Ledger | Shared Workflow and Contracts | 1 business day / 2 business days | Execution and Fund Accounts; Workstation Shell and UX; Shared Platform Interop | In Progress | 2026-07-24 | [`ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline`](ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline) |
| W5 | Research Platform | Strategy and Research | Shared Workflow and Contracts | 2 business days / 3 business days | Workstation Shell and UX; Data Confidence and Validation; Shared Platform Interop | Planned | 2026-08-21 | [`ROADMAP.md#wave-5-backtest-studio-unification`](ROADMAP.md#wave-5-backtest-studio-unification) |
| W6 | Execution + Brokerage Integrations | Execution and Brokerage Integrations | Governance and Ledger | 4 hours / 1 business day | Data Confidence and Validation; Shared Platform Interop; Workstation Shell and UX | Planned | 2026-09-18 | [`ROADMAP.md#wave-6-live-integration-readiness`](ROADMAP.md#wave-6-live-integration-readiness) |
<!-- program-state:end -->

---

## Summary

Meridian's platform foundations are already broad enough that roadmap priority should now come from operator value and readiness evidence, not from generalized platform sprawl. The repo already includes:

- a strong ingestion and storage baseline with bounded channels, WAL durability, JSONL and Parquet sinks, replay, backfill scheduling, gap analysis, packaging, lineage, and export
- shared workstation endpoints and a workstation model now expressed through the visible `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, `Reporting`, and `Settings` workspaces
- shared `StrategyRun`, portfolio, ledger, and reconciliation read paths in `src/Meridian.Strategies/Services/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`, plus a bounded Ledger-compatible CLI journal report path for local balance/register/accounts checks
- execution, paper-trading, strategy lifecycle, and promotion seams, including wired `/api/execution/*`, `/api/promotion/*`, and `/api/strategies/*` surfaces
- an active WPF workstation baseline with run-centered pages, Security Master drill-ins, and desktop shell modernization already landed
- an active browser workstation lane in `src/Meridian.Ui/dashboard/`, including a read-only Operator Readiness Console at `/trading/readiness` that aggregates latest runs, active paper session, DK1/provider trust, reconciliation breaks, promotion blockers, governance report-pack readiness, full-console checkpoint gates, and operator-inbox work items from shared API payloads; the same lane includes a React/Vite Research run library that can review retained runs, cap compare selection to two runs, call shared compare/diff/promotion-history APIs, surface command failures as operator-visible alerts, replace placeholder routes with dedicated Portfolio/Reporting/Settings/Data task panels, load shared workflow library and pinned workflow presets into the command palette, expose `/data/quotes`, `/data/watchlist`, and `/data/alerts` for live quote/order-book/watchlist/Price Alerts workflows with selectable recent-trade detail inspection, expose `/strategy/quant-lab` for gated QuantScript runs, parameters, templates, and plot evidence, expose `/strategy/designer` for visual strategy payoff/participation support, expose `/reporting/evidence` for evidence subject selection, packet completeness, lineage, validation, and manifest export, project Portfolio at-a-glance and Today-panel context on Overview, expose richer sidebar sub-routes and command-palette grouping, cover menu-linked browser routes in the web screenshot route file, align with the Meridian Design System reference workbench/tokenized-color support, and ship built workstation assets from `src/Meridian.Ui/wwwroot/workstation/`
- a delivered Security Master platform seam with shared coverage and provenance flowing across research, trading, portfolio, ledger, reconciliation, governance, and WPF drill-ins; the seam now also has UFL/reference-data projection slices for bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, and certificates of deposit, plus browser Security Master search-result selection, identity drill-ins, detail/lots tracking, and server-side operator override storage

The meaningful repo delta since the April 8 planning refresh is no longer only WPF workflow consolidation. The earlier 2026-04-29 browser pivot has been superseded by the current parallel-surface direction: browser and WPF workflows are both active operator surfaces, while shared contracts, services, and read models remain the boundary that prevents client divergence. The first web Research support slice is present as a run-library view model plus component coverage for two-run compare/diff readiness, selected-run copy, promotion-history loading state, accessible error alerts, and refreshed built workstation assets. The web shell also has a `buildAppShellViewState` seam for active workspace resolution, loading status, partial-bootstrap degradation, failed-slice retry copy, and full-bootstrap failure handling. As of 2026-05-07, the browser workstation adds route-aware workflow commands backed by `/api/workstation/workflows` and `/api/workstation/workflows/presets`, a Portfolio brokerage-sync task panel with household account/position rollups, a Reporting report-pack task panel, a Settings capability-coverage panel plus Alpaca paper-key verification controls, Security Master print-packet readiness projection in Data, and shared API clients for portfolio aggregate/exposure endpoints. As of 2026-05-09, the web lane also includes Data routes for live quotes/order-book review, watchlists with multi-symbol quote snapshots, click-to-trade order-ticket staging, intraday and historical price charts backed by `/api/data/*` and `/api/historical` contracts, a gated Quant Lab route backed by `/api/quant/run`, `/api/quant/parameters`, and `/api/quant/templates`, focus-management improvements in the command palette, mega menu, and Portfolio account filter, a slimmer browser rail/workspace header that removes duplicated current-workspace and meta-card context while preserving active-route and session accessibility, and seeded no-host market-data fixtures that make `/data/quotes?symbol=AAPL` populate quote, trade, depth, symbol, snapshot, and historical-bar evidence in demo mode. These are support evidence for Wave 2-4 workflow consolidation, market-context review, browser-shell resilience, and buyer-demo packaging, not closure of Backtest Studio unification, Wave 2 cockpit acceptance, governed reporting acceptance, or controlled live-readiness.

On 2026-05-12, committed deltas broadened support evidence without changing wave status. Security Master now projects asset-class-specific reference data across the UFL expansion set through shared DTOs, Postgres projection stores, migrations, and `/api/reference-data/*` endpoints; browser Security Master now has selectable search-result rows that drive identity drill-ins, corporate-action and trading-parameter panels, an extended details panel, local lots tracker, and server-side operator override GET/PATCH support. The browser shell also adds a Portfolio at-a-glance Overview panel, sidebar sub-items, richer mega-menu/command-palette grouping, full menu-linked web screenshot route coverage, and refreshed workstation assets. Treat this as Wave 3/Wave 4 data-governance and browser-workstation maturity evidence; it does not close operator-approved reconciliation calibration, governed report-pack lifecycle acceptance, Evidence Vault/report-line provenance, or the Wave 2 cockpit acceptance gate.

The 2026-05-12 hardening deltas are support hardening in the same direction. The browser Data provider-setup success state now exposes view-model-owned next-validation routes into live quotes, backfill preview, Trading readiness, or Security Master coverage based on selected provider capabilities; Quant Lab plot rendering is split behind `quant-plot.view-model.ts` so chart geometry, unsupported states, ARIA labels, and reusable helpers can be tested apart from React markup; browser Live Quotes recent prints now use selectable dense rows with an accessible detail inspector for sequence, stream, venue, timestamp, and aggressor state, while accepted or rejected quick-trade ticket states expose a Trading readiness handoff; browser Portfolio position and run-evidence tables now use shared dense row selection with accessible expanded detail-panel state, and the Portfolio brokerage-sync panel now exposes next actions for provider repair, Trading readiness, and Trading cockpit review; browser Data backfill queues now use shared dense rows with keyboard-selectable expanded detail state; browser Watchlist no-quote, empty-value, refresh, and starter-pack busy labels now come from the view model; browser Security Master search results and lots now use selectable dense rows with identity/detail state for drill-in and cost-basis review; and browser Accounting reconciliation now has a view-model-owned dense detail queue with keyboard-selectable rows, accessible selected/expanded row state, fixture-backed no-host break-queue coverage, and empty-detail copy when no run is available. Browser Strategy support now also keeps run-library row inspection and promotion-history decision detail state in the view model so compare, diff, promotion, and evidence review remain tied to selected rows. The same hardening pass moves grouped command-palette sections, hash-aware `/settings#alpaca-provider-setup` route matching, app-shell query-string route focus for subject/symbol handoffs, Overview status-banner and refresh-command presentation, Security Master conflict refresh/retry state, Reporting report-pack preview/export actions plus backend-link navigation safety, and Trading readiness UTC timestamp labels and the Trading loading panel into view models so route, disabled, busy, and accessibility state stays out of JSX-local copy. Shared UI-service persistence now uses `AtomicFileWriter` for config saves, export preset writes/exports, and quality-archive snapshots, with export presets cloned before export so operator state is not mutated by packaging. Retained WPF Data shell work moves `DataWorkspaceShellPage` under `src/Meridian.Wpf/Features/Data/` with a feature module, snapshot service, presentation adapter, and view model owning loading/error, hero, queue, and action-resolution state. Treat these items as browser-lane reliability, market-context review, portfolio/accounting workflow orientation, and retained-desktop architecture evidence, not completion of provider onboarding, Backtest Studio, durable reconciliation casework, or Wave 2-4 acceptance.

As of 2026-05-15, the browser lane adds a Today panel for day P&L, movers, open orders, and fills; local Price Alerts with trigger history and UTC poll/trigger timing; a visual Strategy Designer for multi-leg payoff and participation review; Covered Call saved-run and trade-timeline inspection support; full-console readiness checkpoint gates with BrokerageSync repair links into provider setup; and Meridian Design System reference workbench/tokenized-color support. These items strengthen demo, readiness-review, and visual-contract evidence for the web lane. They do not close cockpit acceptance, durable reconciliation casework, report-pack lifecycle acceptance, Evidence Vault, report-line provenance, or controlled live-readiness.

The brokerage/account-sync delta is also narrower than a live-readiness claim. Shared contracts now distinguish brokerage account kinds such as taxable brokerage, Roth IRA, and traditional IRA; fund-account endpoints can link external brokerage accounts, run sync, read positions/activity, calculate cash-adjusted performance and cash-flow summaries, and aggregate a household portfolio projection. Alpaca connection support is paper-first API-key verification and revocation. Robinhood support is a read-only OAuth/aggregation lane plus a normalized `RobinhoodReadOnlyBrokerageSyncAdapter` that consumes configured account, portfolio, and activity endpoints without storing Robinhood passwords or reusing the execution gateway. Treat this as Wave 3/Wave 4 portfolio/accounting continuity support and Wave 6 prerequisite evidence, not controlled live trading readiness.

The design-system delta is enforcement support for the browser lane. The Meridian Design System preview set now has a browsable catalogue, shared preview navigation styles, and a governance checker that requires preview viewport metadata, `main` landmarks, `h1` headings, shared preview CSS, approved tokens, current workspace names, valid links, tight radii, and numeric-table conventions. This helps keep the browser workstation coherent, but it does not change product readiness gates.

The current shell baseline also now includes a first-run Welcome readiness progress strip for provider connection, symbol inventory, and storage-path posture; a Storage archive-posture card for daily growth, capacity horizon, last scan, and one archive handoff from the already-loaded analytics snapshot; a Storage preview scope strip that shows selected root, layout, compression, sample tree, and archive-path guidance before backfill/export/package work; an OrderBook posture strip that summarizes selected-symbol scope, depth availability, spread, cumulative delta, trade-tape readiness, and bid/ask pressure before an operator reads the ladder; compact shared deep-page command chrome that preserves related-workflow and trust-state context inside `WorkspaceDeepPageHostPage`; actionable shell-context attention detail with severity, owner, source, and next action; provider-degradation workflow summaries that route the next handoff to `ProviderHealth` instead of the generic provider page; and Trading desk hero plus main-shell queue logic that treats warning or critical shared `WorkItems` as first-class blockers and routes brokerage-sync work to `AccountPortfolio` before it can show a ready active-run state. Treat these as operator-orientation and routing-quality evidence, not as new wave exits.

The shared-run support evidence now also includes `RunCashFlowPage` empty-state guidance for selected-run, missing-run, no-event, and loaded cash-flow summaries. This tightens Wave 3 continuity around retained run evidence, but it does not close the broader governance cash-flow/projection work still listed under Wave 4.

The repo delta on 2026-04-28 is release-evidence hardening rather than a wave-exit change. `.github/workflows/refresh-screenshots.yml` now supports scheduled, push, and manual WPF screenshot capture with selectable catalog/manual workflow groups, least-privilege default permissions, per-workflow diagnostic artifacts, and a single downstream commit job so PNG updates are merged once after the matrix completes. The generated documentation dashboard also refreshed to an 89/100 health score and the generated coverage report shows 88.3% documented items; endpoint and configuration documentation gaps remain cleanup work, not operator-readiness blockers.

The DK1 evidence posture also sharpened after the prior roadmap snapshot. [`provider-validation-matrix.md`](provider-validation-matrix.md), [`dk1-pilot-parity-runbook.md`](evidence/dk1-pilot-parity-runbook.md), and [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md) now point to a `pilotReplaySampleSet` contract emitted by `scripts/dev/run-wave1-provider-validation.ps1`; the current repo also wires that validation run into `scripts/dev/generate-dk1-pilot-parity-packet.ps1` so the automation emits `dk1-pilot-parity-packet.json` and `.md` beside the Wave 1 summary. The current 2026-04-27 evidence set is present under `artifacts/provider-validation/_automation/2026-04-27/`: the parity packet is `ready-for-operator-review`, all four pilot sample rows are ready, all four evidence documents are validated, the trust-rationale and baseline-threshold contracts are validated, and the packet-bound sign-off file is signed by the Data Operations, Provider Reliability, and Trading owners with `operatorSignoff.validForDk1Exit=true`. Future DK1 reviews still need fresh date-stamped packets and matching packet-bound sign-off files; copied or stale sign-off files remain invalid. Shared interop readiness is **Ready for cadence monitoring** after the baseline `artifacts/contract-review/2026-04-27/contract-review-packet.*` packet and Shared Platform Interop owner approval locked the weekly Wednesday review cadence. The cockpit projects that DK1 packet through `Dk1TrustGateReadinessService`, `TradingTrustGateReadinessDto`, structured `OperatorSignoff`, `SampleReviews`, `EvidenceDocuments`, `TrustRationaleContract`, `BaselineThresholdContract`, and a `ProviderTrustGate` work item if a future packet lacks valid sign-off, so the readiness lane no longer has to infer DK1 state from documents alone. The promotion handoff lane has started in a narrow cockpit audit-feedback slice, the export DK2 lane is **Early In Progress** through governed report-pack schema/version checks, and the reconciliation/governance DK2 lane is now **Early In Progress** through a file-backed reconciliation break queue with review, resolve/dismiss, audit-history routes, seeded exception-route/tolerance/sign-off metadata, and `/api/workstation/reconciliation/calibration-summary` profile rollups for Ready/ReviewRequired/Blocked calibration posture. Durable generalized governance casework, operator-approved tolerance calibration, and Wave 2 cockpit handoff acceptance still remain open.

---

## Current State

Wave-by-wave status labels are tracked in the canonical table in [`PROGRAM_STATE.md`](PROGRAM_STATE.md).

For implementation detail and evidence, use:

- [`production-status.md`](production-status.md) for readiness posture and provider evidence
- [`FULL_IMPLEMENTATION_TODO.md`](FULL_IMPLEMENTATION_TODO.md) for active execution tracks
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) for the shortest stakeholder summary

---

## What Is Complete

### Platform baseline

- Meridian's ingestion, storage, replay, export, and data-quality stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and materially stronger operational-readiness foundations than earlier roadmap snapshots.
- The historical improvement backlog is effectively closed for the current platform baseline, which is a real milestone.

### Execution and workflow foundations

- The seven-workspace operator model is present in the current shell, with older four-workspace planning language retained only as compatibility grouping.
- The workstation surfaces contain material workflows for the visible `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, `Reporting`, and `Settings` workspaces rather than only navigation and summary surfaces; the browser dashboard and WPF desktop are both active operator shells that consume shared contracts and read models. The current web Research lane already exposes retained run review, two-run compare/diff readiness, promotion-history loading, and operator-visible command failure states through `ResearchScreen` and `useResearchRunLibraryViewModel`, while the web app shell now centralizes loading/degraded/failure route gating through `buildAppShellViewState`.
- The Wave 2 cockpit now has a shared readiness seam through `/api/workstation/trading/readiness` and `TradingOperatorReadinessDto`, joining paper-session state, replay verification, execution controls, DK1 trust-gate packet posture, promotion status, brokerage sync, acceptance gates, overall readiness, work items, and warnings into one operator-facing contract. The shared `/api/workstation/operator/inbox` endpoint now aggregates those readiness work items with actionable warning/critical run review-packet items from the latest runs, open/in-review reconciliation breaks, and navigation targets, and the WPF shell queue button consumes that endpoint to show review counts, severity tone, account-scoped brokerage/readiness blockers when an account operating context is active, route-aware primary-item navigation into concrete workbenches such as `FundReconciliation` and run review packets, and shell-context attention when a review item is active without closing the end-to-end operator-inbox workflow. Promotion approvals also have a canonical `PromotionApprovalChecklist` for DK1 trust-packet, run-lineage, portfolio/ledger-continuity, risk-control, and live-override review requirements. This is useful acceptance infrastructure, not a completed cockpit claim.
- The local Wave 2 replay-audit slice now strengthens that acceptance lane by recording replay consistency, compared fill/order/ledger counts, last-persisted timestamps, and primary mismatch reason into execution-audit metadata; the readiness gate also treats diverging active-session fill/order/ledger counts as stale replay coverage and drops back to review-required until replay verification is rerun. This improves restart and service-layer reconstruction evidence without closing the full cockpit-hardening gate.
- WPF already has meaningful run-centered workstation pages on top of the broader desktop page inventory, including a Position Blotter selection-review rail that summarizes grouped exposure, long/short/gross quantities, unsupported rows, and batch-action eligibility before an operator flattens or upsizes selected execution rows.
- The WPF shell/navigation baseline is materially delivered: four compatibility workspace shell pages, metadata-driven navigation, deep-page hosting, command/search metadata, context strips, canonical `ResearchShell` startup/deep-link handling, the Trading, Research, and Data desk briefing heroes, and navigation/shell smoke tests are present. The Trading hero now projects active-run, workflow-summary, replay/readiness, controls, DK1 trust, brokerage-sync, stale replay count detail, and handoff state into the shell; `TradingHours` now projects live-risk, pre-market staging, after-hours review, closed-planning session briefings, and holiday-calendar empty-state guidance from market-calendar state before operators stage trading-desk work; `OrderBook` now projects symbol scope, bid/ask depth availability, spread, cumulative delta, tape readiness, and pressure into one order-flow posture handoff before the ladder and tape; the Research hero now projects market briefing, selected-run, run detail, portfolio, and promotion-review handoff state; the Data shell now uses `DataFeatureModule`, `DataWorkspaceShellSnapshotService`, `DataWorkspaceShellPresentationService`, and `DataWorkspaceShellViewModel` to keep provider, backfill, storage, session, export, environment-mode, loading/error, hero, queue, and action-resolution state out of page code-behind while retaining the existing presentation builder; `ProviderHealth` now projects stale snapshots, disconnected streaming sessions, mixed-provider states, and blocked backfill coverage into one provider-posture briefing before the operator scans provider cards; `SystemHealth` now summarizes provider, storage, and retained event posture into one triage handoff before the operator scans diagnostics panels, and its provider/recent-event empty states now distinguish pending scans from confirmed empty snapshots; `NotificationCenter` now lets operators reset search, unread-only, and severity filters when retained notification history is hidden; `ActivityLog` now summarizes visible entries, retained error/warning counts, latest entry, and active filters before the operator scans individual log rows, and exposes export/clear header actions only when visible or retained log state supports them; `Watchlist` now summarizes saved lists, pinned lists, symbol coverage, visible search scope, and empty-state guidance before the operator loads or imports symbol sets, then orders pinned lists first with visible card badges so desk-ready symbol sets are easier to select; `MessagingHub` now projects message-flow posture, subscriber readiness, retained activity scope, bound refresh recency, and clear-activity command state from its view model; `StrategyRuns` now distinguishes an empty run library from filters that hide retained runs, keeps visible-versus-recorded run scope beside search, exposes reset-filters recovery without reloading the run store, and guides compare-run selection when visible run state cannot produce a valid pair; `BatchBacktest` now gives the sweep results pane stateful empty guidance for idle, validation-blocked, running, failed-without-results, cancelled, and populated states; `QuantScript` now exposes local execution history in-page with empty-state guidance plus run-browser, run-detail, and compare handoffs when a mirrored Strategy Run exists; `SecurityMaster` now exposes runtime-unavailable search recovery plus a bound `Clear Search` action so operators can reset no-match or unavailable-runtime search state without another workstation read; `FundAccounts` now projects fund-context, account-queue, provider-routing, blocked-route, shared-data-access, balance-evidence snapshot posture, and ready-for-reconciliation states from already-loaded account, route, provider, and balance-history evidence. Desktop workflow validation is also stronger because page-tag automation now uses an invisible-but-real `ShellAutomationState` element, isolated restore/build behavior avoids target-framework asset drift, local single-instance behavior has focused mutex and launch-argument forwarding coverage, and the GitHub screenshot refresh lane can capture catalog/manual WPF workflows with diagnostics before committing updated screenshots once. The remaining roadmap question is still whether those surfaces measurably improve active Wave 2-4 workflows.
- First-run, storage, context-strip, and deep-page shell polish now has concrete support evidence: `WelcomePageViewModel` summarizes readiness progress across connection, symbols, and storage; `StorageViewModel.BuildStoragePosture()` projects archive growth, capacity horizon, last-scan, empty-archive, capacity-warning, stable, and unavailable states from the loaded analytics snapshot; `StorageViewModel.RefreshPreview()` normalizes preview roots and projects layout/compression scope before archive-path decisions; `WorkspaceDeepPageHostPage` keeps compact command, related-workflow, and trust-state context around hosted pages; attention banners now include severity, owner, source, and action guidance; degraded provider workflow summaries open `ProviderHealth` as the next action; and the Trading hero blocks ready presentation when shared readiness work items still require attention. These sharpen operator orientation and routing but do not close cockpit, shared-model, or governance acceptance.

### Shared-model baseline

- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` give Meridian a stable seam for unifying backtest, paper, live-aware, portfolio, and ledger views. The Ledger-compatible CLI journal report path is useful local accounting support evidence, but it does not close broader shared ledger continuity or governed reporting work.
- Workstation endpoints already expose run comparison, diff, fills, attribution, ledger summaries, reconciliation, and Security Master read paths.

### Security Master baseline

- Security Master is no longer a blueprint-only seam. The WPF browser, workstation endpoints, shared security references, conflict handling, corporate actions, and trading-parameter flows are materially in code.
- Meridian now has one authoritative instrument-definition seam that already propagates into Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF drill-ins.

### Governance baseline

- Governance is no longer hypothetical. Security Master, Fund Accounts account-queue/routing posture, reconciliation, calibration-summary rollups, direct lending, export profiles, and governance-facing UI and API seams are real and discoverable in the repo.
- The product gap has shifted from "build governance foundations" to "finish governance productization and workflow continuity."

---

## What Remains

- **Wave 1 maintenance:** keep the closed provider-confidence, checkpoint, and Parquet evidence gate aligned around Alpaca, Robinhood, and Yahoo
- **DK1 execution:** preserve the signed 2026-04-27 provider-trust packet and rerun packet-bound review whenever provider evidence, trust rationale, or threshold calibration changes
- **Wave 2:** turn the current paper-trading cockpit from "visible" into "dependable," with acceptance evidence tied to DK1 trust signals
- **Wave 3:** make run history, brokerage/account posture, portfolio, ledger, cash-flow, and reconciliation behave like one cross-workspace model under the shared compatibility matrix
- **Wave 4:** deepen governance and fund-operations workflows on top of the delivered Security Master baseline, including the new reconciliation calibration-summary rollups and operations-continuity close-lane API, then prove them through DK2 promotion/export/reconciliation gates
- **Wave 5:** unify native and Lean workflows into one Backtest Studio once the shared model is stable enough to support it cleanly
- **Wave 6:** expand into controlled live integration readiness only after trust and paper-workflow gates are materially closed
- **Optional:** pursue advanced research, simulation, scale-out, and performance tracks only after the core workstation product is coherent and trustworthy

---

## Target End Product

Meridian's target end state is a self-hosted evidence-backed investment operations platform organized around the visible `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` workspaces.

`Data` establishes evidence-backed provider trust, `Strategy` turns that data into reviewed runs and comparisons, `Trading` promotes approved runs into paper workflows, `Portfolio` and `Accounting` review the resulting account, portfolio, ledger, cash-flow, and reconciliation evidence, and `Reporting` turns the governed evidence set into retained report packs.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context. Commercially, that makes Meridian the system of record for investment decision evidence, with the Assurance Loop as the sellable through-line.

---

## Recommended Next Waves

Across Waves 2-4, keep web-dashboard workflow consolidation, validation coverage, route/deep-link behavior, fixture/demo-mode cues, and architecture simplification reinforcing the same read-model and orchestration seams rather than becoming a parallel delivery program. Use WPF validation for active desktop workflows and shared-contract coverage.

### Wave 1: Closed provider confidence and checkpoint evidence gate

**Why now:** This gate is now closed in repo evidence and should be preserved as the trust boundary for every downstream readiness claim.

**Focus:**

- keep Alpaca provider and stable execution seam evidence explicit as the repo-closed core provider baseline
- keep Robinhood supported-surface evidence aligned with its bounded runtime artifact set without overstating live readiness
- formalize Yahoo as a historical-only core provider row backed by deterministic repo tests
- keep checkpoint reliability and Parquet L2 flush behavior on the passing suite list inside `run-wave1-provider-validation.ps1`
- treat the WPF Data shell and Provider Health posture briefing as active desktop operator evidence when they keep provider, backfill, storage, session, and export handoffs tied to shared operational services rather than duplicating readiness logic
- keep the emitted DK1 `pilotReplaySampleSet`, generated `dk1-pilot-parity-packet.*` artifacts, and packet-bound operator sign-off template synchronized with the pilot parity runbook and provider-validation matrix; generated packets are date-stamped run outputs and must be regenerated or attached for review, not assumed from removed repo artifacts
- keep provider-confidence docs, deferred-provider language, runtime artifact folders, the validation matrix, and the latest automation summary synchronized with executable evidence

**Exit signal:** The Wave 1 matrix, roadmap, status docs, DK1 pilot runbook, generated parity packet, packet-bound sign-off template, and automation summary all describe the same active provider set and pilot replay/sample contract; Alpaca and Yahoo remain repo-closed, Robinhood remains explicitly bounded, checkpoint and L2 rows stay closed in repo tests, and deferred providers are not implied to be current blockers.

<a id="wave-2-web-paper-trading-cockpit-completion"></a>

### Wave 2: Workstation paper-trading cockpit completion

**Why now:** Meridian already has the execution, session, and promotion APIs. Product value now depends on finishing the operator cockpit.

**Focus:**

- tighten positions, orders, fills, replay, sessions, and risk workflows into a dependable operator lane
- keep promotion evaluation, approval, and rejection rationale explicitly tied to operator review, with outcome severity and history refresh behavior visible in the cockpit
- use the new trading-readiness contract as the acceptance surface for session state, replay consistency and freshness, audit/control evidence, risk/control explainability warnings, promotion review, DK1 trust-gate packet/sign-off posture, overall readiness, brokerage-sync posture, and stable operator work items that can be refreshed by the web cockpit, the `/trading/readiness` Operator Readiness Console, and the shared operator-inbox endpoint without random ID churn; account operating contexts should preserve account-scoped brokerage/readiness blockers through `fundAccountId`, and run review-packet queue items should stay bounded to actionable warning/critical latest-run blockers
- validate the web Trading cockpit against context-required, replay-mismatch, controls-blocked, paper-review, and live-oversight operator states without treating WPF hero coverage alone as cockpit completion
- verify session persistence, replay behavior, and stale-replay recovery under realistic scenarios
- align cockpit behavior with brokerage-adapter and provider-confidence evidence

**Exit signal:** A strategy can move from backtest into a visible, auditable paper-trading workflow through the shared workstation contract, with the web dashboard and retained local API consuming the same readiness lane.

<a id="wave-3-shared-run--portfolio--ledger-continuity"></a>

### Wave 3: Shared run / portfolio / ledger continuity

**Why now:** The contracts exist, but the product experience around them is not yet fully realized.

**Focus:**

- deepen run history and comparison depth across backtest, paper, and live-aware modes
- strengthen portfolio, attribution, fills, ledger, cash-flow, and reconciliation continuity
- land brokerage and custodian account-sync ingestion that feeds the same shared portfolio, ledger, and reconciliation seams; current support evidence includes fund-account brokerage links, positions/activity reads, cash-adjusted performance, cash-flow summaries, household portfolio rollups, Alpaca paper connection verification, and a read-only Robinhood aggregation adapter, but not live-readiness closure
- grow the new file-backed reconciliation break queue and calibration-summary rollups beyond run-scoped seeded breaks and seeded exception-route/tolerance/sign-off metadata into operator-approved calibration, durable casework, and external-account/custodian review flows
- keep Security Master enrichment, the web Research run library, and WPF workflow work tied to the same shared read-model seam, including browser run pair selection for compare/diff, promotion-history loading/error states, the retained Research desk briefing hero's run-detail, portfolio, and paper-promotion review handoffs, StrategyRuns filter recovery on the already-loaded run browser rows, QuantScript run-history handoffs into shared Research views when mirrored runs exist, and stable route-aware review-packet work items for promotion, security coverage, continuity, and brokerage blockers

**Exit signal:** Strategy runs become Meridian's primary cross-workspace product object rather than one of several overlapping representations.

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

**Why now:** Governance is already visible in code, and Security Master is already the delivered authoritative instrument seam. This is now a workflow-deepening problem rather than a missing-foundation problem.

**Focus:**

- add account/entity and strategy-structure workflows on top of the existing governance baseline
- add multi-ledger, cash-flow, reconciliation, and reporting slices on top of shared DTOs, read services, and export seams
- connect external brokerage account state to fund-account review, cash movement, performance, household rollup, and reconciliation workflows through shared projections
- use the operations-continuity workflow as the shared account-period close-lane seam for broker intake, Security Master resolution, ledger posting, reconciliation, approval, close, and governed reopen while keeping full close acceptance open
- keep the Fund Accounts operator brief tied to shared account, provider-routing, retained balance-history, and `FundStructureSharedDataAccessDto` evidence while broader account/entity casework remains open
- deepen governance workflows without creating separate reporting or accounting stacks
- enforce the governance architecture guard: Security Master remains the sole instrument source, and governance DTO/service additions with instrument metadata must carry Security Master identity/provenance references


#### Wave 4 objective pass/fail gate (cockpit-style)

"Wave 4 objective" is **Pass** only when every governance/fund-ops criterion below is green in CI and locally reproducible. It is **Fail** if any criterion is red.

| Criterion | Required endpoint(s) + response fields | Required workstation surface behavior | Fail condition |
| --- | --- | --- | --- |
| Security Master conflict lifecycle is traceable end-to-end | `/api/security-master/conflicts`, `/api/security-master/conflicts/{conflictId}`, and `/api/security-master/conflicts/{conflictId}/resolve` must expose `ConflictReasonCode`, source-provenance identifiers, and resolution payload rationale (`ResolutionDecision`, `ResolutionRationale`, `Actor`, `TimestampUtc`, `CorrelationId`). | Operator can **search -> drill-in -> history -> resolution** for one conflicted instrument and see conflict reasons, source provenance, prior resolution history, and final resolution decision in one continuous flow. | Any missing linkage between conflict list/detail/resolution views, missing conflict reason code, missing source provenance, or missing explicit resolution rationale/audit chain in the same scenario run. |
| Corporate action provenance and parameter versioning remain explainable | `/api/security-master/corporate-actions` and `/api/security-master/trading-parameters` must return event provenance (`CorporateActionSource`, `IngestedAtUtc`) plus effective version fields (`EffectiveVersion`, `EffectiveFromUtc`, `SupersedesVersion`). | Operator can **search -> drill-in -> history -> resolution** from instrument view into corporate-action timeline and trading-parameter history, then resolve a flagged discrepancy with the effective-version trail visible. | Corporate-action timeline lacks provenance, trading-parameter change lacks effective-version traceability, or discrepancy resolution is recorded without explainable source/version linkage. |
| Governance audit trail is complete across fund-ops decisions | Governance workflow endpoints (`/api/fund-structure/workspace-view`, `/api/fund-structure/report-pack-preview`, and reconciliation decision endpoints) must emit audit metadata (`AuditActor`, `AuditTimestampUtc`, `CorrelationId`) and decision rationale fields for approvals/rejections. | Operator can **search -> drill-in -> history -> resolution** for an account/entity decision, inspect prior decision history, and complete or reject resolution with rationale that remains visible in history and governed output previews. | Any governance decision path that omits actor/timestamp/correlation, fails to retain decision rationale, or breaks history-to-resolution linkage between workspace and governed-output views. |

**Exit signal:** Governance becomes a real operator workflow with concrete review, drill-in, and governed-output seams built on the same contracts already used elsewhere in the workstation.

### Wave 5: Backtest Studio unification

**Why now:** Research becomes much stronger once Waves 1-4 have made the shared run model stable enough to unify native and Lean experiences cleanly.

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling beyond the current web Research first cut, which can call shared compare/diff APIs for a selected run pair but does not yet unify native and Lean execution workflows
- broaden fill-model realism
- improve performance for larger windows where it materially changes operator experience

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 6: Live integration readiness

**Why now:** Live-adjacent credibility should follow, not precede, a finished paper workflow and validated provider trust.

**Focus:**

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` promotion gates
- formalize operator controls such as manual overrides, circuit breakers, and intervention flows

**Exit signal:** Meridian can support a controlled live-readiness story without overclaiming broad live-trading completion.

<a id="phase-16-assembly-level-performance-optimizations"></a>

### Optional advanced research / scale tracks

**Focus:**

- QuantScript deeper integration beyond the delivered local Run History and Research handoff presentation
- L3 inference and queue-aware execution simulation
- multi-instance coordination
- Phase 16 performance work
- broader advanced research extensions after the core workstation product is trustworthy and coherent

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is operator-ready.

---

<a id="desktop-improvements"></a>
<a id="phase-8-repository-organization--optimization"></a>

## Wave DK Program (Focused Migration Wrapper for Waves 2-4)

To avoid piecemeal adoption, Meridian now treats the active workstation migration as a two-wave **Delivery Kernel (DK)** program that wraps and strengthens Waves 2-4 rather than running in parallel.

### Program intent

- keep one dependency-ordered path from provider trust to operator-ready cockpit, then into shared-model continuity and governance productization
- require the same quality gates in each wave: **parity pass**, **explainability pass**, **calibration pass**, and **operator sign-off**
- enforce shared interop contracts through one cross-wave owner so subsystem delivery does not drift into incompatible seams

### Wave DK1 - Data quality and provider trust hardening

**Scope alignment:** operationally reinforces Wave 2 and the trust-dependent portions of Wave 3.

**Primary outcomes:**

- maintain and extend the closed Wave 1 evidence gate into daily operator workflows
- make provider behavior, replay outcomes, and cockpit data surfaces explainable to operators
- calibrate trust metrics and promotion thresholds before expanding promotion scope

**Entry criteria (must all be true):**

1. **Parity entry:** Wave 1 matrix remains repo-closed for Alpaca, Robinhood (bounded), Yahoo, checkpoint reliability, and Parquet L2 proof.
2. **Explainability entry:** provider-confidence evidence is visible in operator-facing docs and workstation drill-ins, not only in scripts.
3. **Calibration entry:** baseline trust thresholds are declared for freshness, completeness, and replay consistency.
4. **Operator entry:** Data Operations and Trading operator reps agree on the DK1 pilot symbol/account set.

**Exit criteria (must all be true):**

1. **Parity pass:** paper-cockpit data views match validated provider and replay outputs for the agreed pilot set.
2. **Explainability pass:** every trust alert in scope has attributable source, reason code, and operator action guidance.
3. **Calibration pass:** trust thresholds are tuned against replay + paper session evidence with documented false-positive and false-negative review.
4. **Operator sign-off:** named Data Operations and Trading owners approve DK1 completion and unblock DK2 promotion scope.

### Wave DK2 - Promotion, export, and reconciliation continuity

**Scope alignment:** delivers the integration-critical path of Waves 3-4 (promotion workflow, export reliability, and governance reconciliation).

**Primary outcomes:**

- make `Backtest -> Paper -> Governance` promotion a single audited path
- ensure exports and governed outputs are consistent with shared run/portfolio/ledger contracts
- establish reconciliation as an always-on control rather than end-of-process cleanup

**Entry criteria (must all be true):**

1. **Parity entry:** DK1 exit is signed and shared run/portfolio/ledger DTO seams are the active path for pilot workflows.
2. **Explainability entry:** promotion and export decisions emit audit-grade rationale with linked run, portfolio, and ledger context.
3. **Calibration entry:** reconciliation tolerance bands and exception severities are defined per subsystem.
4. **Operator entry:** Governance and Trading operators accept the DK2 pilot operating playbook.

**Exit criteria (must all be true):**

1. **Parity pass:** promoted runs, exported artifacts, and reconciliation outputs agree across workstation, API, and governance views for pilot scenarios.
2. **Explainability pass:** operators can trace each promoted run to source data trust signals, approval chain, exported package, and reconciliation state.
3. **Calibration pass:** reconciliation thresholds and promotion controls are tuned with documented exception burn-down and zero unresolved critical mismatches.
4. **Operator sign-off:** Trading and Governance owners sign production-readiness for the DK2 scope.

### Subsystem ownership and interop governance

| Subsystem | Primary owner | Responsibilities |
| --- | --- | --- |
| Data quality + provider trust | Data Operations & Provider Reliability owner | Provider evidence gate maintenance, trust metrics, provider incident review |
| Promotion + paper-trading cockpit | Trading Workstation owner | Promotion controls, paper workflow reliability, operator controls |
| Export + packaging | Data Operations Export owner | Export contract parity, package lineage, operator-facing export diagnostics |
| Reconciliation + governance | Governance/Fund Ops owner | Reconciliation policy, exception workflow, governed outputs |
| Shared run/portfolio/ledger contracts | Shared Platform Interop owner (Architecture + Contracts) | Cross-subsystem DTO/version governance, compatibility policy, contract change review |

**Interop contract governance rule:** no DK subsystem can ship a contract-breaking change without Shared Platform Interop owner approval and a documented compatibility/rollback note.

### Risk register and rollback plans by subsystem

| Subsystem | Key risk | Leading indicator | Rollback plan |
| --- | --- | --- | --- |
| Data quality + provider trust | trust drift between validation scripts and cockpit surfaces | rising unresolved trust alert delta between scripts and UI | freeze promotion expansion, pin to last verified provider matrix + replay baseline, rerun DK1 calibration |
| Promotion + paper cockpit | promotion path divergence across UI/API | mismatched promotion state or approval chain in audits | revert promotion workflow to last signed contract version, disable new promotion lanes behind feature flags |
| Export + packaging | exported artifact schema drift or lineage gaps | increase in export validation failures or missing lineage links | roll back exporter contract version, regenerate artifacts from last good run snapshots |
| Reconciliation + governance | tolerance miscalibration causing exception floods or misses | sustained spike in unresolved critical exceptions | restore prior tolerance profile, reprocess affected window, require manual governance approval for new promotions |
| Shared interop contracts | uncoordinated DTO/version change cascades | cross-workspace contract test failures | revert to previous shared contract package/API shape and block downstream deploy until compatibility suite passes |

### Kernel readiness dashboard (single status surface)

Use [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md) as the single hand-authored status dashboard for DK wave and subsystem readiness.

Dashboard requirements:

- one row per subsystem with current DK wave state, gate status, owner, and next milestone
- explicit tracking of parity/explainability/calibration/operator-sign-off per subsystem
- linked evidence and rollback status so release decisions are auditable

### Alignment guardrail with Waves 2-4

DK1 and DK2 are **execution wrappers** for existing Waves 2-4, not new parallel scope:

- Wave 2 cockpit hardening work is planned and reported through DK1
- Wave 3 shared-model continuity is split: trust-dependent scope in DK1, promotion/export/reconciliation continuity in DK2
- Wave 4 governance productization readiness gates are tracked through DK2 exit criteria

Any proposed work item that cannot map to Wave 2, 3, or 4 plus DK1/DK2 gates should be treated as optional or deferred work, not core operator-readiness path.

### Immediate implementation commitments (2026-04-20 to 2026-05-29)

To move from planning into execution, the DK program now carries date-bounded commitments tracked in the dashboard:

1. **2026-04-20 -> 2026-05-01:** preserve the signed DK1 operator review around the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet`, generated `ready-for-operator-review` parity packet, packet-bound sign-off file, trust rationale mapping, and threshold review; rerun packet-bound sign-off if any evidence changes.
2. **2026-04-20 -> 2026-05-01:** keep the shared interop compatibility matrix and contract-review cadence active, with dashboard status tied to the cross-wave owner.
3. **2026-05-02 -> 2026-05-15:** lock promotion rationale fields and operator approval checklist coverage for DK1 -> DK2 handoff.
4. **2026-05-09 -> 2026-05-22:** freeze governed export schema/version contract and validate pilot scenarios.
5. **2026-05-16 -> 2026-05-29:** calibrate reconciliation tolerance profiles and exception routing for governance sign-off readiness.

The implementation source of truth remains [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md), which must be updated weekly.

---

## Risks and Dependencies

- **Provider trust is still the first dependency.** The narrow Wave 1 gate is now closed, but downstream workflow polish still depends on preserving that evidence boundary instead of reopening provider scope by prose drift.
- **Stronger tests are not the same as broad live-vendor proof.** Replay, contract, and pipeline evidence materially improve confidence but do not close every vendor-runtime gap by themselves.
- **Cockpit hardening should precede live-readiness claims.** Meridian now has meaningful trading surfaces, but operator trust still matters more than feature count.
- **The shared run model must remain the center of gravity.** If Research, Trading, Portfolio, Ledger, and Governance drift apart again, the workstation migration loses its product logic.
- **Security Master must remain the authoritative seam.** It should enrich portfolio, ledger, reconciliation, and reporting flows rather than being reimplemented inside parallel governance workflows.

- **Governance DTO/Service review search guidance:** in governance-related PRs, explicitly scan for new instrument-term fields (`Symbol`, `Cusip`, `Isin`, `Coupon`, `Maturity`, `Issuer`, `Venue`, `AssetClass`) that appear without Security Master identity/provenance references. Treat that as a review blocker unless the code is adapter-only with an explicit mapping step back to Security Master.
- **Governance should extend shared DTOs, not invent a new stack.** Cash-flow, reconciliation, and reporting should reuse the same read-model and export seams already in place.
- **WPF migration should avoid page-level re-fragmentation.** The right move is more orchestration and view-model or service extraction, not more page-local logic.
- **Documentation drift is now a real delivery risk.** The planning set is large enough that roadmap, status, blueprint, and short-horizon docs need deliberate synchronization.

---

## Release Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** the active provider gate for Alpaca, Robinhood, and Yahoo is documented in executable evidence, checkpoint reliability plus Parquet L2 flush behavior are closed in repo tests, and `run-wave1-provider-validation.ps1` reproduces the offline gate while generated provider-validation packets are attached as date-stamped run outputs.
2. **Wave 2 gates:** the workstation exposes a dependable paper-trading cockpit through the shared readiness contract, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

Until then, Meridian is best described as feature-rich, structurally strong, and actively being productized into its intended workstation and fund-operations end state.

---

## Reference Documents

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`../plans/evidence-backed-investment-operations-plan.md`](../plans/evidence-backed-investment-operations-plan.md)
- [`FULL_IMPLEMENTATION_TODO.md`](FULL_IMPLEMENTATION_TODO.md)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md)
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
