# Web UI Development Pivot

**Date:** 2026-04-29
**Status:** Active direction

## Decision

Pause new desktop-app feature development and continue operator UI delivery through the web dashboard in `src/Meridian.Ui/dashboard/`. Keep `src/Meridian.Wpf/` available for retained desktop support, shared-contract regression checks, and compatibility fixes, but do not start new WPF-first operator surfaces while this pivot is active.

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
4. Move Wave 2 cockpit acceptance into the web UI: session restore, replay verification, execution controls, promotion rationale, and operator work items should all consume shared workstation endpoints.
5. Move data and governance operator workflows next: provider posture, backfill preview/trigger, export/report-pack preview, Security Master, reconciliation, and ledger review.
6. Keep WPF tests only where shared contracts or retained desktop compatibility would otherwise regress.
7. Treat the existing web Research run library as Wave 3 support evidence only; it does not close strategy-aware launch/preflight, persisted sweep grouping, or Backtest Studio unification.

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
- Wave 2 cockpit readiness can be reviewed from the browser using shared API contracts.
- Wave 3 shared run/portfolio/ledger continuity and Wave 4 governance workflows have web-visible operator paths.
- WPF remains stable for retained support but no longer defines the default UI implementation path.
