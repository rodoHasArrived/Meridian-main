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
- `Compliance/ComplianceServices.cs` - compliance policy engine and immutable audit-log service.
  The policy engine gates sensitive actions on `Meridian.Identity`
  role/permission mappings (`UserRole` + `RolePermissions`) — the same mapping used by
  `Meridian.FSharp.Operations.SensitiveActionPolicy` — rather than a module-private role table.
- `Compliance/ComplianceApprovalStore.cs` - durable authoritative approval requests and
  authenticated approval decisions. Policy treats the request identifier only as a lookup key and
  verifies the retained action, object, entity, requester, expiry, and independent approvers.
- `Compliance/AccessReviewService.cs` - separate assessment and remediation paths. Remediation
  mutates the canonical identity account store and records only role removals proven by readback.

## Important workflows

Use this module when changing compliance policy checks, step-up/dual-approval requirements,
segregation-of-duties enforcement, immutable audit hash-chain behavior, or access-review record
ownership. UI endpoints and host composition should register these services, but should not own the
audit/compliance state.

Caller-authored requester or approver IDs are not approval evidence. Step-up evaluation requires a
durable approval-request record created and decided by authenticated actors, bound to the exact
governed object. Dormant-access assessment never claims a mutation; the applied-remediation path
reports `Applied`, `PartiallyApplied`, `Failed`, or `VerificationFailed` from authoritative before
and after role state.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CompliancePolicyEngineTests|FullyQualifiedName~AccessReviewServiceTests" /p:EnableWindowsTargeting=true
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
