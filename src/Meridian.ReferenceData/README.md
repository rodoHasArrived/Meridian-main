---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-REFERENCE-DATA
path: src/Meridian.ReferenceData
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-06-05
---

# src/Meridian.ReferenceData

## Purpose

Physical bounded-context module project for Security Master identifiers, classifications,
taxonomies, profile catalogs, and data-governance ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.ReferenceData` - registered source module root.
- `SecurityMaster/SecurityKindMapping.cs` - canonical asset-class and instrument-kind
  normalization plus descriptor-backed compatibility profiles for Security Master validation,
  provider routing, and readiness workflows.
- `SecurityMaster/SecurityAssetProfileCatalog.cs` - Security Master profile catalog
  contract plus seeded approved custom/private asset profile templates.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

Security Master asset-class mapping, instrument-kind mapping, the profile catalog contract, and
seeded approved custom/private asset profile templates live here as reference-data ownership.
Application orchestration consumes these contracts for validation, governance, operational
readiness, projection rebuilds, and UI endpoint composition.

Asset-specific contract/reference read services now live in `Meridian.Instruments`; keep this
module focused on Security Master identifier, classification, taxonomy, and profile-catalog
ownership.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-REFERENCE-DATA -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-REFERENCE-DATA -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.ReferenceData/Meridian.ReferenceData.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~SecurityKindMappingTests|FullyQualifiedName~SecurityValidationServiceTests|FullyQualifiedName~SecurityMasterIngestStatusEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### Migration and archive notes

`SecurityKindMapping`, `ISecurityAssetProfileCatalog`, `StaticSecurityAssetProfileCatalog`, and
the seeded Security Master asset-profile definitions moved from `src/Meridian.Application/SecurityMaster`
into this physical design module so pure reference-data ownership no longer stays in the
layer-oriented application project.
Certificate-of-deposit and commodity projection services were moved onward to
`src/Meridian.Instruments` with the rest of the asset-specific instrument reference services.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
