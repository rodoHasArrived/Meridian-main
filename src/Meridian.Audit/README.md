---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-AUDIT
path: src/Meridian.Audit
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-06
---

# src/Meridian.Audit

## Purpose

Physical bounded-context module project for evidence packets, immutable audit hashes, compliance
policy checks, access-review records, retained manifests, lineage, and export-verification
ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `Compliance/ComplianceModels.cs` - sensitive-action, actor, compliance request, audit-event,
  access-review, and audit-hash records.
- `Compliance/ComplianceServices.cs` - compliance policy engine, immutable audit-log service, and
  access-review service. The policy engine gates sensitive actions on `Meridian.Identity`
  role/permission mappings (`UserRole` + `RolePermissions`) — the same mapping used by
  `Meridian.FSharp.Operations.SensitiveActionPolicy` — rather than a module-private role table.

## Important workflows

Use this module when changing compliance policy checks, step-up/dual-approval requirements,
segregation-of-duties enforcement, immutable audit hash-chain behavior, or access-review record
ownership. UI endpoints and host composition should register these services, but should not own the
audit/compliance state.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-AUDIT -->
| Roadmap item | Title |
| --- | --- |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-AUDIT -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Audit/Meridian.Audit.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CompliancePolicyEngineTests" /p:EnableWindowsTargeting=true
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

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the
nearest docs when compliance, audit-hash, access-review, or retained-evidence workflow semantics
change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
