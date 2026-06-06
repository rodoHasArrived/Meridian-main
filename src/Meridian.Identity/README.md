---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-IDENTITY
path: src/Meridian.Identity
status: active
owner_lane: Identity and Access
last_reviewed: 2026-06-06
---

# src/Meridian.Identity

## Purpose

Physical bounded-context module project for identity, scoped access, fund-structure scope lineage,
role/profile, and session ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Identity` - registered source module root.
- `Contracts/Auth/` - role, permission, scoped-access assignment, and auth catalog DTOs published under `Meridian.Identity.Auth`.
- `Application/LoginSessionService.cs` - session-token lifecycle and authenticated profile lookup.
- `Application/UserProfileRegistry.cs` - environment-backed user profile loading, credential checks, and role-profile permission resolution.
- `Application/AuthenticationMode.cs` - optional/required authentication-mode resolution.
- `Application/ScopedAccessServices.cs` - scoped access assignment and authorization services.
- `Application/FundStructureAccessScopeLineageProvider.cs` - fund-structure hierarchy lineage
  provider used by scoped authorization to honor organization/fund/account ancestry.
- `Infrastructure/RolePermissionProfileStore.cs` - file-backed custom role-profile catalog and audit-event persistence.
- `Infrastructure/ScopedAccessAssignmentStore.cs` - file and PostgreSQL scoped-access assignment stores.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-IDENTITY -->
| Roadmap item | Title |
| --- | --- |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-IDENTITY -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Identity/Meridian.Identity.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~ScopedAccessServiceTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~RoleAuthorizationTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --filter FullyQualifiedName~SensitiveActionPolicyTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
```

### API and contract notes

Identity contracts publish role/profile, permission, and scoped-access DTOs through
`Meridian.Identity.Auth`. `LoginSessionService`, `UserProfileRegistry`, and
`FileRolePermissionProfileStore` own session state, environment-backed user profiles, and custom
role-profile permission persistence. `FundStructureAccessScopeLineageProvider` is Identity-owned
and consumes the shared `Meridian.Contracts.Services.IFundStructureService` contract supplied by
Application composition, so scoped authorization can resolve ancestry without depending on
Application-only identity adapters. Cross-module endpoint, F#, browser, and WPF consumers should
reference Identity rather than reintroducing auth DTOs or identity state under `Meridian.Contracts`
or `Meridian.Ui.Shared`.

### Migration and archive notes

`ScopedAccessService`, `IScopedAccessAssignmentService`, `IScopedAuthorizationService`,
`IScopedAccessAssignmentStore`, `FileScopedAccessAssignmentStore`, and
`PostgresScopedAccessAssignmentStore` moved from `src/Meridian.Application/Auth` into this module.
`RolePermissions`, `UserPermission`, `UserRole`, scoped-access assignment DTOs, and role-profile
request/result DTOs moved from `src/Meridian.Contracts/Auth` into this module. `LoginSessionService`,
`UserProfileRegistry`, `AuthenticationModeResolver`, `IRolePermissionProfileStore`, and
`FileRolePermissionProfileStore` moved from `src/Meridian.Ui.Shared` into this module. Remaining
Identity migration work is endpoint/middleware adapters and browser/WPF identity presentation.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
