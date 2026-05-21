---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SHARED
path: src/Meridian.Ui.Shared
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-20
---

# src/Meridian.Ui.Shared

## Purpose

UI Shared contains shared read models and compatibility shims used by browser workstation and retained WPF surfaces.

## Layer responsibility

This layer keeps cross-surface UI contracts stable so active browser work and retained desktop support can consume the same operator-facing projections.

## Key folders and files

- Shared UI read-model definitions.
- Compatibility types that bridge browser and retained desktop consumers.

## Important workflows

Use this module when a UI-facing contract must be shared across the browser dashboard and retained WPF shell.
Operations Continuity reconciliation bridging belongs here: `OperationsContinuityReconciliationBridge`
turns shared reconciliation run detail into workflow transition input, including Security Master
coverage/accounting issues as server-authored break cases so both UI surfaces consume the same close
blockers and evidence links.

## Diagrams

See `DIA-BROWSER-WORKSTATION` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-SHARED -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-SHARED -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj /p:EnableWindowsTargeting=true --logger "console;verbosity=normal"
```

## Change rules

Preserve compatibility for both browser and retained desktop consumers. Avoid surface-specific behavior that belongs in `src/Meridian.Ui/dashboard` or `src/Meridian.Wpf`.

## Related docs

- `docs/architecture/module-map.md`
- `docs/plans/web-ui-development-pivot.md`
- `docs/source/generated/source-module-index.md`
