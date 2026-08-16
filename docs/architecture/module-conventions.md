# Module Conventions

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-07-06

This document codifies the structural conventions that keep Meridian a healthy **modular operational
monolith** ([ADR-017](../adr/017-modular-operational-monolith.md)) as it grows. It captures patterns
that already exist in the codebase but were previously undocumented, so that in-flight decomposition
work and new capabilities follow **one** convention instead of diverging. It complements — and does
not replace — [`layer-boundaries.md`](layer-boundaries.md), [`module-map.md`](module-map.md), and
[`core-extensibility-model.md`](core-extensibility-model.md).

Treat these as the required target shape when carving up existing god files or adding new capability
surfaces.

## 1. Capability endpoint groups

The workstation API is organized as **capability-owned endpoint groups**, not one endpoint class.

- Each group is a static class exposing a single `Map…` extension over `IEndpointRouteBuilder` /
  `WebApplication` that registers one `MapGroup("/api/workstation/<capability>")` route group.
  See the established per-feature files under `src/Meridian.Ui.Shared/Endpoints/` (e.g.
  `FundStructureEndpoints.cs`, `FundAccountEndpoints.cs`, `Compliance/ComplianceEndpoints.cs`).
- Every route group applies the shared tenant seam via `RequireWorkstationTenantScope()` so
  authorization is uniform across capabilities.
- A capability group owns its request/response DTOs (declared in `src/Meridian.Contracts`) and
  delegates all logic to injected services — endpoints stay thin (bind → authorize → call service →
  project DTO).

**Anti-pattern (being retired):** accreting handlers onto a single `partial class` such as
`WorkstationEndpoints`. New handlers belong in a capability group, not a new partial of the monolith.
Decomposition of `WorkstationEndpoints.cs` into independent capability groups is tracked refactor work.

## 2. Request-scoped tenant context

Tenant, company, actor, and permission state is resolved once per request and consumed everywhere.

- `WorkstationTenantContext` (record) + `IWorkstationTenantContextAccessor` /
  `HttpContextWorkstationTenantContextAccessor` in
  `src/Meridian.Ui.Shared/Endpoints/WorkstationTenantContext.cs` are the single source of request
  identity. Endpoint groups read the tenant off the accessor rather than re-parsing auth state.
- Services that need tenant isolation accept the resolved tenant id, not raw `HttpContext`.

## 3. Scoped access authorization

Fund/account traversal authorization flows through `src/Meridian.Identity/Application/ScopedAccessServices.cs`:
`IScopedAuthorizationService`, `IAccessScopeLineageProvider`, and `AccessScopeRef`. Capability groups
that expose fund-structure or account-scoped data must authorize through these services (see
`FundStructureEndpointAuthorizationTests`, `FundAccountEndpointAuthorizationTests`) instead of
hand-rolling scope checks.

## 4. Tenant-scoped store factory spine

Stateful subsystems that must isolate tenants use a **store factory** convention rather than threading
tenant ids through every storage call:

- A store implements both its data interface and a `…TenantStoreFactory` with `ForTenant(tenantId)`
  returning a store rooted at a per-tenant path. The canonical example is
  `FileProviderIntegrationManifestStore` (`IProviderIntegrationManifestStore` +
  `IProviderIntegrationTenantManifestStoreFactory`) in `src/Meridian.Storage/Integrations/`.
- Services expose a paired signature — `(request, ct)` and `(tenantId, request, ct)` — and resolve
  the tenant-scoped store internally. Keep this pairing consistent when extending a subsystem.

The provider-integration cluster follows a **planner → orchestrator → transport/dry-run over a
manifest store** shape (see [`provider-integration-manifest-runtime.md`](provider-integration-manifest-runtime.md)).
When extending it, reuse the existing seams and composition root
(`src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs`) rather than adding a
parallel service with overlapping responsibility.

## 5. Composition and registration

Services are registered through the feature registration seam
(`src/Meridian.Application/Composition/Features/*`) and `*ServiceCollectionExtensions` helpers, wired
behind interfaces. New services are added to the relevant feature registration, not to an ad-hoc
container setup, so composition stays discoverable and testable.

## 6. File-size ratchet (no new god files)

To stop the monoliths this document exists to unwind from re-forming, CI runs a **no-new-god-file
ratchet**: `build/scripts/ci/check-file-size.py` (invoked from `.github/workflows/ci.yml`).

- Any hand-authored production source file over **2000 lines** that is not in
  `build/config/file-size-baseline.json` fails CI.
- Baselined files are frozen at their recorded line count: they may shrink freely but not grow.
- After a decomposition, lock the reduction in with
  `python3 build/scripts/ci/check-file-size.py --tighten-baseline` — it only ever lowers caps and
  retains a working buffer above each file's current size.
- Legitimately *raising* a cap requires `python3 build/scripts/ci/check-file-size.py --update-baseline`
  and a justification in review — the diff makes the tracked debt visible.

The intent is directional: every baselined file is a decomposition target, and the ratchet only ever
tightens.

## Related

- [ADR-017: Modular Operational Monolith](../adr/017-modular-operational-monolith.md)
- [Layer Boundaries](layer-boundaries.md)
- [Module Map](module-map.md)
- [Core Extensibility Model](core-extensibility-model.md)
- [Provider Integration Manifest Runtime](provider-integration-manifest-runtime.md)
- [Evidence Workflow Fabric](evidence-workflow-fabric.md)
