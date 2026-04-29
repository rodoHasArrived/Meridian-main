# Web UI Development Pivot

**Date:** 2026-04-29
**Status:** Active direction

## Decision

Pause new desktop-app feature development and continue operator UI delivery through the web dashboard in `src/Meridian.Ui/dashboard/`. Keep `src/Meridian.Wpf/` available for retained desktop support, shared-contract regression checks, and compatibility fixes, but do not start new WPF-first operator surfaces while this pivot is active.

New product behavior should land behind shared contracts, local/web API endpoints, or shared read
models before any client-specific workflow is expanded. WPF remains one supported client, not the
product boundary. Major UI features are not considered accepted until the relevant workflow is
API-addressable and visible from the browser workstation or explicitly documented as retained
desktop support only.

## Commercial UI Implication

The browser dashboard is the primary UI lane for proving the commercial story: Meridian as the system of record for investment decision evidence. New Assurance Loop surfaces should start from shared contracts and web-visible workflows for Data Trust Passport, Run Evidence Graph, Promotion Passport, accounting-grade paper evidence, reconciliation casework, and governed report-pack readiness. The accounting-led commercial layer should also start in the browser dashboard: Buyer Demo Mode, role-based demo views, readiness dashboards, close workflow previews, evidence packet actions, broker statement reconciliation, and controls-policy summaries should be web-first product targets after shared contracts exist. WPF remains retained support and regression coverage for those contracts; it should not define new commercial modules while this pivot is active.

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

1. Restore and keep the dashboard runnable with local `npm install`, `npm run test`, and `npm run build`. Current evidence includes a refreshed workstation asset build, app-shell view-model coverage for loading/partial-degradation/bootstrap-failure status panels, canonical `WORKSPACES` metadata for `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`, route-aware command-palette and placeholder-route view-model coverage, and Research run-library component/view-model coverage for two-run compare/diff readiness, promotion-history loading, and command-error alerts.
2. Keep the dashboard shell first-class: route the seven canonical workspaces from shared workspace metadata, preserve legacy aliases for `/overview`, `/research`, `/data-operations`, and `/governance`, and replace placeholder routes as dedicated web screens are implemented.
3. Keep the shared dashboard shell aligned with `Meridian Design System (3)/`: use the documented masthead plus left-rail workstation frame, restrained ambient background, tokenized surfaces, square badges/chips, mono data, and the copied Meridian brand mark before adding screen-specific visual treatments.
4. Keep the first browser-first read-only surface focused on the Operator Readiness Console at `/trading/readiness`. It aggregates latest Strategy runs, active paper-session posture, DK1/provider trust, reconciliation breaks, promotion blockers, governance report-pack readiness, and operator-inbox work items from shared API payloads. It must hold the headline posture in review until the shared operator inbox loads cleanly, even when the trading-readiness payload already reports `Ready`.
5. Move Wave 2 cockpit acceptance into the web UI: session restore, replay verification, execution controls, promotion rationale, and operator work items should all consume shared workstation endpoints. Current Trading cockpit evidence includes a refreshable readiness-contract summary for overall, paper-operation, brokerage-sync, and as-of posture plus a link into the read-only console.
6. Move data and governance operator workflows next: provider posture, backfill preview/trigger, export/report-pack preview, Security Master, reconciliation, ledger review, close workflow previews, and evidence packet actions.
7. Treat Buyer Demo Mode and role-based demo views as browser-dashboard packaging on top of seeded shared evidence, not as separate fixture-only UI. The demo path should prove the same readiness, accounting, reconciliation, and report-pack contracts the operator workflow uses.
8. Keep WPF tests only where shared contracts or retained desktop compatibility would otherwise regress.
9. Treat the existing web Research run library as Wave 3 support evidence only; it does not close strategy-aware launch/preflight, persisted sweep grouping, or Backtest Studio unification.

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
`MERIDIAN_API_BASE_URL` or `http://localhost:8080`, and the dashboard uses typed dev fixtures only
for the initial dashboard bootstrap GETs when the API host is absent.

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
