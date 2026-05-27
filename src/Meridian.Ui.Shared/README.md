---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SHARED
path: src/Meridian.Ui.Shared
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-25
---

# src/Meridian.Ui.Shared

## Purpose

UI shared contains shared UI read models and compatibility shims for browser and desktop surfaces.

## Layer responsibility

This module owns cross-surface operator-facing projection types and shared endpoint helpers. Preserve
compatibility across `src/Meridian.Ui.Services`, `src/Meridian.Ui/dashboard`, and
`src/Meridian.Wpf`.

## Key folders and files

- `Endpoints/` - shared workstation endpoint mapping and projection helpers.
- Shared read models - DTOs and compatibility shims consumed by browser and desktop clients.
- Project metadata - UI shared dependencies and build settings.

## Important workflows

Preserve cross-surface compatibility when evolving shared read models. Keep ledger/reconciliation
source-of-truth services authoritative. Workstation endpoint registration is split by domain through
`WorkstationEndpoints.*.cs` partial files. Keep the root `WorkstationEndpoints.cs` file as the
coordinator, route new domain-specific endpoint edits to the matching partial file, and avoid
concurrent branches that both modify the root coordinator or the shared
`WorkstationEndpointsTests.cs` test body. For operations-continuity and reconciliation endpoint
changes, start with focused `MapWorkstationEndpoints_OperationsContinuity` /
`MapWorkstationEndpoints_Reconciliation` filters before broad workstation endpoint validation.
Accounting explainability and portfolio-accounting read models now route through
`WorkstationEndpoints.AccountingExplainers.cs` and shared services
`IPortfolioAccountingReadService` / `IAccountingExplainabilityService`.

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

Do not put browser-only or WPF-only product logic here. Keep shared read models compatible and route
domain-specific endpoint edits to the matching partial file.

## Related docs

- `src/Meridian.Ui.Services/README.md`
- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-module-index.md`
