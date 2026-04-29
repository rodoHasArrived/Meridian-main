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

## Owners

- **Workstation Shell and UX:** Own web dashboard navigation, layout, operator interaction patterns, and build/test health.
- **Shared Workflow and Contracts:** Own DTO and endpoint compatibility for `/api/workstation/*`, `/api/execution/*`, and promotion/replay surfaces consumed by the dashboard.
- **Trading Workstation:** Own Wave 2 cockpit readiness flows in the web dashboard.
- **Governance and Ledger:** Own accounting, reconciliation, reporting, and Security Master web workflows.
- **Data Confidence and Validation:** Own provider, backfill, storage, and data-quality web workflows.

## Near-Term Implementation Slices

1. Restore and keep the dashboard runnable with local `npm install`, `npm run test`, and `npm run build`.
2. Make the dashboard shell first-class: route `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` from shared workspace metadata instead of WPF-only assumptions.
3. Move Wave 2 cockpit acceptance into the web UI: session restore, replay verification, execution controls, promotion rationale, and operator work items should all consume shared workstation endpoints.
4. Move data and governance operator workflows next: provider posture, backfill preview/trigger, export/report-pack preview, Security Master, reconciliation, and ledger review.
5. Keep WPF tests only where shared contracts or retained desktop compatibility would otherwise regress.

## Validation

Use the narrowest validation for the files touched:

```bash
cd src/Meridian.Ui/dashboard
npm run test
npm run build
```

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
