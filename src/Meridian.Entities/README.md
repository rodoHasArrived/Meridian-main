---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-ENTITIES
path: src/Meridian.Entities
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-05
---

# src/Meridian.Entities

## Purpose

Physical bounded-context module project for organizations, funds, accounts, entities, assignments, and hierarchy ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Entities` - registered source module root.
- `FundStructure/LedgerGroupingRules.cs` - ledger-group assignment type policy,
  assignment-reference normalization, and account ledger-group resolution.
- `FundStructure/IFundStructurePolicyService.cs` and `FundStructure/FundStructurePolicyService.cs`
  - ownership-link compatibility, cycle, primary-link, ownership-percent, replacement-window,
  single-operating-parent, and governance cash-flow query policy.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

Fund-structure ledger grouping rules and ownership/cash-flow policy live here as entity ownership.
Application and UI Shared consume these policies when validating ledger-group assignment mutations,
grouping accounting views, building the ledger-mapping workbench, enforcing ownership graph rules,
and validating governance cash-flow query windows.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-ENTITIES -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-ENTITIES -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Entities/Meridian.Entities.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj --filter "FullyQualifiedName~LedgerGroupingRulesTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj --filter "FullyQualifiedName~FundStructurePolicyServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~LedgerGroupingRulesTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### Migration and archive notes

`LedgerGroupingRules` moved from `src/Meridian.Application/FundStructure` into this physical design
module so fund-structure ledger-group assignment normalization and resolution are no longer owned by
the layer-oriented application project.
`IFundStructurePolicyService` and `FundStructurePolicyService` also moved from
`src/Meridian.Application/FundStructure` into this physical design module so ownership graph and
cash-flow query policy are owned by the Entities bounded context.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
