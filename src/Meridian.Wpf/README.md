---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-WPF
path: src/Meridian.Wpf
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-27
---

# src/Meridian.Wpf

## Purpose

WPF workstation is the active Windows desktop operator workstation sharing contracts and read models
with the browser workstation.

## Layer responsibility

This module owns the desktop shell, WPF pages, route hosting, and desktop workstation view models.
Keep shared contracts and read-model logic in shared UI services when browser and desktop both need
the behavior.

## Key folders and files

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
