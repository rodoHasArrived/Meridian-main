# UFL Asset Profile Template

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, application, and workstation contributors
**Last Updated:** 2026-05-29
**Status:** active template

## Summary

Use this template when creating or converting UFL asset profiles. Existing `*-target-state-v2.md` filenames may remain for compatibility, but each asset should read like a capability profile over the shared UFL model.

Asset profiles may retain detailed target-state sections as implementation notes, but the top-level profile is authoritative for maturity, evidence, provider boundary, and next milestone claims.

## Evidence Boundary

### Implemented

- Name exact code, endpoints, tests, or docs evidence that exists today.
- Do not list target-state behavior here unless it is implemented and validated.

### Partially Implemented

- Name mixed or incomplete surfaces, such as reference endpoints without lifecycle projections.
- Explain what is present and what is missing.

### Target-State Only

- Name planned behaviors, projections, workflows, and APIs that are not delivered.

### Explicitly Out of Scope

- Name behavior this package should not own.
- Include pricing, risk, margin methodology, or mobile workflows unless the asset profile explicitly owns them.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L1 | `SecurityKind.X` and mapping exist. | Canonical profile-specific terms. | F# validation and C# mapping tests. |
| ProjectionRebuild | L0 | none. | Rebuildable projection with metadata. | projection and checkpoint tests. |

## Current Maturity

State the current level using [UFL Capability Model](ufl-capability-model.md):

- `L0` Cataloged
- `L1` Canonical Terms
- `L2` Reference Read
- `L3` Projection Safe
- `L4` Operational Workflow
- `L5` Accounting/Reconciliation Integrated

Use `partial` when evidence is mixed. Example: `L1/L2 partial`.

## Next Milestone Contract

**Goal:** one sentence describing the next maturity step.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs`
- `src/Meridian.Contracts/SecurityMaster/`
- `tests/Meridian.Tests/...`

**Acceptance evidence:**

- F# validation tests
- C# mapping tests
- endpoint contract tests
- projection/rebuild tests when advancing to L3
- docs/status evidence packet when claiming delivered maturity

**Exit criteria:** no target-state claim is marked delivered without named code and test evidence.

## Legacy Target-State Detail

If a converted package keeps earlier architecture, DDL, event, workflow, or ticket sections, treat those sections as target-state design notes. Do not use them as delivered evidence unless the profile names the current code, endpoint, test, or docs artifact that proves the claim.

## Provider Payload Boundary

Provider payloads may be retained as evidence, import source, or troubleshooting context. Downstream UFL workflows must consume canonical Security Master identities, canonical terms, canonical aliases, and canonical projections.

## Related Documents

- [UFL Capability Model](ufl-capability-model.md)
- [UFL Conformance Matrix](ufl-conformance-matrix.md)
- [UFL Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md)
