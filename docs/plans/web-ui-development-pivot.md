# Web UI Development Pivot

**Date:** 2026-05-09
**Status:** Active direction

## Decision

Pause new desktop-app feature development and continue operator UI delivery through the browser workstation in `src/Meridian.Ui/dashboard/`. Keep `src/Meridian.Wpf/` available for retained desktop support, shared-contract regression checks, and compatibility fixes, but do not start new WPF-first operator surfaces while this pivot is active.

New product behavior should land behind shared contracts, local/web API endpoints, or shared read
models before any client-specific workflow is expanded. WPF remains one supported client, not the
product boundary. Major UI features are not considered accepted until the relevant workflow is
API-addressable and visible from the browser workstation or explicitly documented as retained
desktop support only.

## Commercial UI Implication

The browser workstation is the primary UI lane for proving the commercial story: Meridian as the system of record for investment decision evidence. New Assurance Loop surfaces should start from shared contracts and web-visible workflows for Data Trust Passport, Run Evidence Graph, Promotion Passport, accounting-grade paper evidence, reconciliation casework, and governed report-pack readiness. The accounting-led commercial layer should also start in the browser workstation: Buyer Demo Mode, role-based demo views, readiness dashboards, close workflow previews, evidence packet actions, broker statement reconciliation, and controls-policy summaries should be web-first product targets after shared contracts exist. The Evidence OS layer should follow the same path: proof certificates, strategy-to-ledger drilldowns, report-line provenance, close readiness, instrument passports, evidence SLA, decision memory, and no-orphan-evidence warnings should be shared-contract-backed browser workflows before they become client-specific UI. The current `/reporting/evidence` workbench and shared evidence packet/graph APIs are the first browser-visible support slice for that direction, not completion of the Evidence Vault or report-line provenance product. WPF remains retained support and regression coverage for those contracts; it should not define new commercial modules while this pivot is active.

## Active UI Lane

- `src/Meridian.Ui/dashboard/`: React/Vite dashboard source for the browser workstation.
- `src/Meridian.Ui/wwwroot/workstation/`: built workstation assets served by `Meridian.Ui`.
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`: shared workstation API contract source.
- `src/Meridian.Ui.Services/`: read-model and service support for workstation payloads.
- `Meridian Design System (3)/`: design-system contract for the web workstation shell, including
  cockpit color tokens, tight surface radii, shallow workstation shadows, the brand mark, line-icon
  usage, and operator-to-operator copy rules now reflected in shared dashboard primitives.

## Owners

- **Workstation Shell and UX:** Own web dashboard navigation, layout, operator interaction patterns, and build/test health.
- **Shared Workflow and Contracts:** Own DTO and endpoint compatibility for `/api/workstation/*`, `/api/execution/*`, and promotion/replay surfaces consumed by the dashboard.
- **Trading Workstation:** Own Wave 2 cockpit readiness flows in the web dashboard.
- **Governance and Ledger:** Own accounting, reconciliation, reporting, and Security Master web workflows.
- **Data Confidence and Validation:** Own provider, backfill, storage, and data-quality web workflows.

## Near-Term Implementation Slices

1. Restore and keep the dashboard runnable with local `npm install`, `npm run test`, and `npm run build`. Current evidence includes a refreshed workstation asset build, app-shell view-model coverage for loading/partial-degradation/bootstrap-failure status panels, canonical `WORKSPACES` metadata for `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`, route-aware command-palette coverage that now loads `/api/workstation/workflows` and pinned `/api/workstation/workflows/presets` into launchable browser commands, shared operator-inbox routing that sends reconciliation work to `Accounting`, report-pack review work to the dedicated `/reporting/report-packs` task panel, brokerage-sync review work to the dedicated `/portfolio/brokerage-sync` task panel, and Security Master coverage work to `Data`, independent browser bootstrap reads for `/api/workstation/accounting` and `/api/workstation/reporting`, Settings diagnostic endpoint posture derived from loaded workspace payloads and workspace errors, Settings Alpaca paper-key verification controls, Portfolio run-evidence and brokerage household/account selection state with filter focus restoration, Security Master print-packet readiness projection, Data live quote/order-book/watchlist workflows with multi-symbol quote snapshots, click-to-trade order-ticket staging, intraday and historical price charts, a gated `/strategy/quant-lab` browser surface backed by `/api/quant/run`, `/api/quant/parameters`, and `/api/quant/templates`, command-palette/mega-menu focus trapping, and Research run-library component/view-model coverage for two-run compare/diff readiness, promotion-history loading, and command-error alerts.
2. Keep the dashboard shell first-class: route the seven canonical workspaces from shared workspace metadata, preserve legacy aliases for `/overview`, `/research`, `/data-operations`, and `/governance`, and replace placeholder routes as dedicated web screens are implemented.
3. Keep the shared dashboard shell aligned with `Meridian Design System (3)/`: use the documented masthead plus left-rail workstation frame, restrained ambient background, tokenized surfaces, square badges/chips, mono data, and the copied Meridian brand mark before adding screen-specific visual treatments. Keep the rail and workspace header lean: avoid repeating current-workspace cards, route chips, environment badges, and command-count metadata when active-route, status, and session context are already present through accessible labels or shared shell chrome.
4. Keep the first browser-first read-only surface focused on the Operator Readiness Console at `/trading/readiness`. It aggregates latest Strategy runs, active paper-session posture, DK1/provider trust, reconciliation breaks, promotion blockers, Accounting reconciliation posture, Reporting report-pack readiness, and operator-inbox work items from shared API payloads. It must hold the headline posture in review until the shared operator inbox loads cleanly and governed report-pack readiness has at least one loaded target, even when the trading-readiness payload already reports `Ready`.
5. Move Wave 2 cockpit acceptance into the web UI: session restore, replay verification, execution controls, promotion rationale, and operator work items should all consume shared workstation endpoints. Current Trading cockpit evidence includes a refreshable readiness-contract summary for overall, paper-operation, brokerage-sync, and as-of posture plus a link into the read-only console.
   - Shared brokerage portfolio sync now uses the split account query and account management contracts for link validation, balance-history reads, snapshot enrichment, and reconciliation triggers, so web Portfolio brokerage performance does not require the broad `IFundAccountService` registration when the narrower read/write account contracts are present.
6. Move data and governance operator workflows next: provider posture, backfill preview/trigger, export/report-pack preview, Security Master print/export packet readiness, reconciliation, ledger review, close workflow previews, evidence packet actions, and report-line provenance previews. Evidence packet actions now have an initial browser path through `/reporting/evidence`, with links from Trading readiness, Strategy/Portfolio run context, Accounting reconciliation review, and Reporting report-pack context; keep expanding that path through shared evidence subjects before adding page-local evidence state. Portfolio run evidence actions and Reporting workbench evidence actions are view-model-owned so selected run/profile context determines the route contract instead of JSX-local string construction. Export preview and analysis-export fallbacks should preserve the actual generated artifact names: symbol CSV files use stable hash suffixes to avoid collisions, and generated Notebook, Parquet, Excel, HDF5, ClickHouse, and Lean sidecars should point at those exact CSV filenames. The Settings workspace now carries a backend capability coverage panel, view-model-owned header posture chips, and an Alpaca setup checklist that guides operators from paper endpoint selection through account verification into Trading readiness. Keep those panels current whenever new shared backend functionality is meant to be browser-accessible.
   - The Data watchlist now includes preset quick-add symbol packs backed by the same bulk symbol API as manual entry, so first-run operators can move from an empty table to live quote validation without guessing an initial universe.
   - Watchlist live-quote and bulk-add failures hand off directly to the Settings provider setup anchor, so operators can repair credentials or connection posture from the failing data workflow instead of stopping at an inline error.
   - The Overview priority routes now use the live status snapshot to send zero-symbol installations to `/data/watchlist` and provider-baseline gaps to `/settings` before defaulting to trading/accounting/reporting triage.
7. Treat Buyer Demo Mode and role-based demo views as browser-dashboard packaging on top of seeded shared evidence, not as separate fixture-only UI. The demo path should prove the same readiness, accounting, reconciliation, and report-pack contracts the operator workflow uses.
8. Keep Evidence OS UX ideas such as confidence rails, lifecycle breadcrumbs, "why am I blocked" panels, and evidence-first empty states grounded in shared readiness/evidence payloads rather than page-local copy. The Evidence Workbench should stay a shared packet/graph consumer, not a separate evidence store.
9. Keep WPF tests only where shared contracts or retained desktop compatibility would otherwise regress.
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
quotes, readiness console, and Alpaca paper provider setup checklist, so operators can distinguish no-host demo data from live
workstation state, review the same read-only evidence flow without credentials first, and then move
directly to the live-data handoff. The quote demo path must include fixture-backed quote, trade,
depth, and historical-bar responses so `/data/quotes?symbol=AAPL` opens as a populated workbench
instead of an empty symbol prompt.
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
- WPF remains stable for retained support but no longer defines the default UI implementation path.
