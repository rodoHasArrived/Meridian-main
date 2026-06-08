---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-REPORTING
path: src/Meridian.Reporting
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-06-07
---

# src/Meridian.Reporting

## Purpose

Physical bounded-context module project for report packs, governed exports, reporting run
contracts, template catalogs, Security Master-enriched report generation, NAV attribution,
orchestration, publication, restatement, distribution, and reporting ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Reporting` - registered source module root.
- `DefaultReportingTemplateCatalog.cs` - canonical investor statement, SEC filing, shadow NAV,
  performance, holdings, capital account, board packet, audit package, and certified-dataset
  template metadata.
- `ReportingContracts.cs` - reporting run, schedule, lineage, template, approval, manifest, and
  audit contracts.
- `ReportingOrchestrationService.cs` - deterministic report run execution, due-schedule handling,
  lineage rendering, approval transitions, retry/failure state, and run-store persistence handoff.
- `ReportGenerationService.cs` - trial-balance report-pack generation with Security Master
  enrichment, lookup-quality classification, and asset-class section grouping.
- `NavAttributionService.cs` - fund/entity/sleeve/vehicle NAV attribution over ledger snapshots
  with optional Security Master classification.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes. Report-template metadata, report-pack generation, NAV attribution, run contracts, deterministic section rendering, governed report-run orchestration, approval transitions, audit entries, and run persistence handoff live here so UI Shared and UI Services do not own reporting behavior.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-REPORTING -->
| Roadmap item | Title |
| --- | --- |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-REPORTING -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Reporting/Meridian.Reporting.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ReportingOrchestrationServiceTests|FullyQualifiedName~ReportPackWorkflowServiceTests|FullyQualifiedName~ReportGenerationServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### API and contract notes

`IReportingOrchestrationService`, `IReportingTemplateCatalog`, `IReportingSectionRenderer`,
`IReportingRunStore`, `ReportGenerationService`, and `NavAttributionService` publish the Reporting
module seams consumed by UI Shared report-pack workflows, UI Services reporting status projections,
and WPF fund-operation views. Reporting template families now cover investor, SEC, shadow NAV,
performance, holdings, capital-account, board, audit, certified-dataset, and custom report packs;
shared UI services layer schedule persistence, delivery history, and rendered HTML/PDF artifacts on
top of those module contracts without moving orchestration ownership out of Reporting.

### Migration and archive notes

Reporting template metadata, run contracts, deterministic section rendering, orchestration,
approval transition, audit-entry, and run-store seams moved out of the legacy Application reporting
folder into this module. UI Shared and UI Services consume these module services but do not own the
reporting behavior. Report-pack generation and NAV attribution also moved from
`Meridian.Application.Services` into this module and now depend on the contracts-owned Security
Master query seam.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
