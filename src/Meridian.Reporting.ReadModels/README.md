---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-REPORTING-READMODELS
path: src/Meridian.Reporting.ReadModels
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-11-12
---

# src/Meridian.Reporting.ReadModels

## Purpose

Owns reporting comparisons, report access evaluation, journal-backed tax projections,
partners-capital reads, and immutable ledger-population replay shared by both workstations.

## Layer responsibility

This is a leaf read-model adapter over Contracts, Ledger, Reporting, and Storage.Accounting.
It references no UI, host, provider adapter, or workstation orchestration assembly.
Endpoint mapping and service composition remain in Ui.Shared.

## Key folders and files

The project links seven source files under `Meridian.Ui.Shared/Services` while owning their
compilation. Ui.Shared excludes those files and forwards their public types to this assembly.
Existing namespaces and constructor signatures remain intact. A source edit to these readers
can build this project without compiling the larger workstation orchestration layer.

## Important workflows

Retained income comparisons preserve authenticated tenant/company/actor scope and immutable
artifact receipts. Journal-based readers preserve the ledger's retained accounting evidence;
unavailable or inconsistent evidence remains unavailable. Extraction changes ownership only.

This bounded extraction does not claim that all accounting, reporting, or fund-operation
orchestration has left Ui.Shared. The remaining services still compose cross-domain workflows.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-REPORTING-READMODELS -->
| Roadmap item | Title |
| --- | --- |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-REPORTING-READMODELS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Reporting.ReadModels/Meridian.Reporting.ReadModels.csproj
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ReportingReadModelBoundaryTests|FullyQualifiedName~ReportingIncomeComparisonServiceTests|FullyQualifiedName~LedgerDisposalTaxReadServiceTests|FullyQualifiedName~ReportAccessPolicyEvaluatorTests|FullyQualifiedName~ReportPeriodRangeTests"
```

## Change rules

Keep read models independent of Ui.Shared. Preserve scope checks, evidence validation,
retained population hashes, and exact posted-journal identities.

## Related docs

- `docs/architecture/module-map.md`
- `docs/engineering/plans/review-remediation-2026-11-12.md`
