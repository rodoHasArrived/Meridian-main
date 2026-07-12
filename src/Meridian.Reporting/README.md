---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-REPORTING
path: src/Meridian.Reporting
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-07-10
---

# src/Meridian.Reporting

## Purpose

Physical bounded-context module project for report packs, governed exports, reporting run
contracts, template catalogs, Security Master-enriched report generation, NAV attribution,
orchestration, publication, restatement, distribution, no-code report-writer grid rendering, and
reporting ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Reporting` - registered source module root.
- `DefaultReportingTemplateCatalog.cs` - canonical investor statement, SEC filing, shadow NAV,
  performance, holdings, capital account, board packet, audit package, and certified-dataset
  template metadata.
- `ReportingStarterKitCatalog.cs` - starter reporting-desk kit records that map operator
  archetypes to enabled template ids, hub layout ids, default periods, and draft schedule seeds.
- `ReportingContracts.cs` - reporting run, schedule, lineage, template, approval, manifest, and
  audit contracts.
- `ReportingOrchestrationService.cs` - deterministic report run execution, due-schedule handling,
  lineage rendering, approval transitions, retry/failure state, and run-store persistence handoff.
- `ReportWriterGridEngine.cs` - governed no-code grid renderer for detail, pivot, Top-N,
  contribution, saved-filter, and formula-backed report-writer tables over supplied dataset rows.
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
and WPF fund-operation views. `ReportGenerationService` retains the canonical ledger dimension
envelope from dimensioned fund-ledger lines on generated trial-balance rows so downstream
report-pack evidence artifacts do not reconstruct accounting scope from account names or route
context. `ReportWriterGridEngine` renders governed template grid definitions without script
execution: row dimensions, column-field cross-tabs for pivot grids, aggregate
metrics, Top-N limits, contribution percentages, and bounded arithmetic formulas are evaluated
against caller-supplied dataset rows with structured warnings for missing or non-numeric inputs.
The canonical envelope now preserves optional `PositionId` alongside `InstrumentId`, keeping
same-security book positions distinct in generated reporting evidence without treating a projected
position or balance as an accounting fact. The immutable journal remains authoritative.
Contribution grids generate `contributionPercent` and `contributionAbsPercent` after aggregation,
using absolute metric exposure as the denominator so offsetting winners and laggards still produce a
signed and absolute percentage-of-P&L breakdown. Report-writer formulas can reference those generated
contribution fields without requiring authors to add duplicate metrics.
Saved grid filters are applied before aggregation, cross-tab expansion, Top-N, contribution, and
formula rendering. Rendered grids also include input/output
row counts, filtered-input counts, source-field lists, metric source mappings, formula dependency
lineage, filter lineage, data-dictionary fields, and validation checks so report-writer previews
and downstream exports can retain a source-backed audit trace without relying on UI-local proof
synthesis. Formula lineage includes brace references, bare identifier references, and `total(...)`
references, while the evaluator supports nested `abs(...)`, `min(...)`, `max(...)`,
`safeDivide(numerator, denominator[, fallback])`, `percent(numerator, denominator[, fallback])`,
`basisPoints(numerator, denominator[, fallback])`, and `round(value[, decimals])` expressions for
guarded P&L, exposure, contribution, return, and basis-point calculations. Custom formula grids
therefore retain the same source evidence that was used for evaluation, and formula data-dictionary
rows retain those source-field pointers while still marking the column as generated.
Generated Reporting run manifests also retain the report-writer grid
artifact metadata generated from approved template definitions, including grid title, kind,
artifact URI, dimension count, metric count, and formula count, while shared UI projections derive
validation summary counts from retained rendered-grid lineage and warnings so delivery packages and shared UI
read models do not need to parse artifact strings to explain no-code grid evidence. When ad-hoc or
scheduled run contracts include dataset rows, the same manifest also retains rendered grid columns,
rows, warnings, and lineage so generated run delivery artifacts can carry source-backed pivot,
Top-N, contribution, and formula output instead of only grid descriptors. Repeated generated runs
for the same job/as-of pair now retain a stable run series id, versioned run attempt ordinal, prior
run id, retry reason, and report-writer grid diffs generated by `ReportSnapshotDiffEngine`, so
downstream review surfaces can compare latest generated, latest approved, and prior line-level
output without inferring attempt history from a bare run id. Scheduled run contracts
can also carry a selected branding theme id and normalized `ReportBrandingThemeDto`; the resulting
manifest keeps that theme with generated report-writer packages so recurring PDF/XLSX/CSV
distribution can prove the firm styling used for the run. Run contracts and manifests can also
carry the resolved `ReportAccessPolicyDto`, allowing downstream delivery evidence to preserve
private, restricted group/company, or company-wide report entitlements for generic Reporting runs
instead of widening generated-run packages to a default audience. Reporting template families now cover
investor, SEC, shadow NAV, performance, holdings, capital-account, board, audit, certified-dataset,
and custom report packs; shared UI services layer schedule persistence, delivery history, template
grid render calls, and rendered HTML/PDF artifacts on top of those module contracts without moving
orchestration ownership out of Reporting. Starter reporting-desk kits remain catalog data in this
module: each kit references existing template ids, a default Reporting hub layout id, a default
period, and schedule seeds that UI Shared provisions through the normal schedule service as editable
drafts.

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
