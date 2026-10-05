# Architectural Decision Records (ADRs)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-09-30

Active architectural decisions are routed through:

- [engineering docs](../engineering/README.md)
- [architecture references](../architecture/README.md)
- [documentation ownership contract](../documentation-ownership.md)

Historical ADRs 001–016 were migrated to the archive during the documentation rebuild, and the
archive-migration stubs that previously mirrored them here have been removed. Full historical ADR
content is preserved in:

- [ADR archive index](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/adr/README.md)

This folder keeps current decisions and proposals. An ADR's status records decision approval,
not implementation completeness or production certification. Proposed records can have implemented
slices; consult their source references and the [readiness tracker](../product/implementation-todo-list.md)
for implementation and release evidence. Context sections preserve the problem that motivated each
record at its stated date.

| Record | Decision status |
| --- | --- |
| [017: Modular Operational Monolith](017-modular-operational-monolith.md) | Accepted |
| [018: Declarative Statement Mapping Profiles](018-declarative-statement-mapping-profiles.md) | Accepted |
| [019: Production Support Matrix and Typed Deployment Posture](019-production-support-matrix-and-deployment-posture.md) | Proposed; core-team sign-off pending |
| [020: Local Lifecycle Control Plane](020-lifecycle-control-plane.md) | Proposed |
| [021: Verified Operation Outcomes and Operational Case History](021-verified-operation-outcomes-and-case-history.md) | Proposed |
| [022: Canonical Asset-Class Homes](022-canonical-asset-class-homes.md) | Accepted |
| [023: Host-Wide Provider Credential Ownership](023-host-wide-provider-credential-ownership.md) | Proposed; implementation plan |

Use the [ADR template](_template.md) to author a new decision, retain its approval status, and
separate existing implementation evidence from planned work.
