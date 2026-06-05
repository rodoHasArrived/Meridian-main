---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-ENTITIES
path: src/Meridian.Entities
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-04
---

# src/Meridian.Entities

## Purpose

Physical bounded-context module project for organizations, funds, accounts, entities, assignments, and hierarchy ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Entities` - registered source module root.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

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
```

## Optional conditional sections

Add only the sections that apply to this module:

- `### Plans and roadmap`
- `### End-user value`
- `### Benchmarks and performance`
- `### Operational evidence`
- `### Security and credentials`
- `### API and contract notes`
- `### Migration and archive notes`

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
