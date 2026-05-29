---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-WPF
path: src/Meridian.Wpf
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-28
---

# src/Meridian.Wpf

## Purpose

WPF workstation is the active Windows desktop operator workstation sharing contracts and read models
with the browser workstation.

## Layer responsibility

This module owns the desktop shell, WPF pages, route hosting, and desktop workstation view models.
Keep shared contracts and read-model logic in shared UI services when browser and desktop both need
the behavior. The seven operator workspaces now register through feature modules under
`src/Meridian.Wpf/Features/` so new workspace-level navigation and shell ownership lands in the
matching module before it expands through the older flat page folders.

## Key folders and files

- `Features/` - workspace-owned module registration for Trading, Portfolio, Accounting, Reporting,
  Strategy, Data, and Settings.
- `ViewModels/` - desktop operator workflow view models.
- `Views/` - WPF pages and controls.
- `Shell/` and `Services/` - navigation, route, launch, and desktop service seams.

## Important workflows

Keep desktop support aligned with shared contracts and governance posture.
Convention-based view-model wiring is handled by `Services/ViewModelViewResolver.cs`; shell pages
that follow the `*Page` to `*ViewModel` naming convention can receive a DI-constructed DataContext
without page-specific registration, while pages that set their own DataContext remain authoritative.
Runtime desktop capability toggles are declared by feature modules and surfaced in Settings through
the feature capability gate. The Security Master page projects the workstation trust
snapshot's `scheduleBook` and `openLotReadModel` payloads into operator-visible schedule, factor,
provenance, and open-lot review sections.
Run Cash Flow consumes `StrategyRunContinuityService` when the desktop shell provides it, so the
cash-flow drill-in presents the same run, portfolio, ledger, cash-flow, reconciliation, and warning
posture used by shared workstation continuity endpoints.
Fund Ledger reconciliation actions call the shared workstation reconciliation endpoints, refresh the
queue from the shared break read model after review/resolve/dismiss, and keep the selected decision
note, audit event, pending close sign-off posture, and contract-owned "Explain the Break" summary
visible in the retained detail panel. The WPF queue projection carries the same source systems,
probable cause, ledger impact, suggested next action, and evidence links as the browser governance
detail so desktop operators do not rebuild reconciliation narratives locally.
Shared close-workflow target tags stay explicit in desktop routing: `OperationsContinuity` and
`OperationsClose` are WPF aliases for the Fund Operations page, with navigation parameters that
land on the overview and report-pack readiness tabs while the browser resolves both tags to
`/accounting/operations-continuity`.
Shared evidence workflow target routing is also explicit: `EvidenceWorkbench` resolves to the WPF
Fund Audit Trail surface while the browser resolves the same shared tag to `/reporting/evidence`.
The route-registry parity test covers all built-in workflow entry and action target tags so shared
workflow catalog updates cannot silently become browser-only or desktop-only.
Shared workstation affordance primitives under `Workstation/Models` and `Workstation/Controls`
standardize action posture, readiness tone, evidence links, recovery actions, and sign-off
requirements for W4 close/report surfaces. Fund Ledger reconciliation and Report Pack handoff
surfaces consume these primitives so blocker, evidence, recovery, and sign-off signifiers stay
visible without creating desktop-only business rules.
`MainPage` remains the route-compatible desktop shell entry point, but shell chrome is now composed
from reusable WPF primitives: `InstitutionalShellFrameControl`, `ShellRailControl`,
`ShellMastheadControl`, `WorkspaceEvidenceStripControl`, `WorkspaceCommandSurfaceControl`,
`InstitutionalCommandPaletteControl`, and `WorkspaceInspectorHostControl`. Workspace shell posture
is WPF-only and resolved through `ShellNavigationCatalog.GetWorkspaceLayoutDescriptor`: Trading and
Data use `Terminal`, Portfolio, Accounting, Reporting, and Settings use `Cockpit`, and Strategy uses
`Workbench`. Legacy workspace names continue to resolve as aliases to the seven canonical roots.
High-value workbench pages should migrate through the shared workstation controls before broad
page sweeps; Strategy Runs now uses `DenseDataGridControl` plus tabbed inspector panes for run,
evidence, comparison, and artifact context while preserving existing page tags and navigation
commands.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-WPF -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-WPF -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```powershell
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
```

## Change rules

Keep WPF views declarative and move loading, disabled, preview, empty-state, and status-copy
behavior into view models. Do not duplicate product logic that belongs in shared UI services.

## Related docs

- `src/Meridian.Ui.Shared/README.md`
- `docs/development/wpf-implementation-notes.md`
- `docs/source/generated/source-module-index.md`
