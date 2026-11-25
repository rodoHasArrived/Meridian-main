# Meridian.Workstation.Abstractions

ASP.NET-free seams shared by the workstation web adapter (`Meridian.Ui.Shared`), the domain
service projects being extracted from it, and the WPF host.

Contents:

- `Contracts/`: workstation DTOs and enums (operations continuity, family office, covered call,
  OMS integration, execution simulation).
- `Endpoints/WorkstationTenantContextSeam.cs`: `WorkstationTenantContext` and
  `IWorkstationTenantContextAccessor`. The HTTP implementation
  (`HttpContextWorkstationTenantContextAccessor`) stays in `Meridian.Ui.Shared`.
- `Serialization/`: source-generated JSON contexts whose payload types live here or in
  `Meridian.Contracts`.

Types keep their original `Meridian.Ui.Shared.*` namespaces while the decomposition is in
progress, so callers did not change. See
[the decomposition plan](../../docs/engineering/plans/ui-shared-decomposition-plan.md).

Rules, enforced by `tests/Meridian.Tests/Architecture/WorkstationAbstractionsBoundaryTests.cs`:
no ASP.NET Core references, and no dependency on `Meridian.Ui.Shared`.
