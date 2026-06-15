---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-IDENTITY
path: src/Meridian.Identity
status: active
owner_lane: Identity and Access
last_reviewed: 2026-06-09
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
- `FundStructure/FundAccountTraversalQueryService.cs` - cached Fund -> Owns ->
  Account traversal query used by scoped-access and fund-account endpoint consumers.
- `Infrastructure/RolePermissionProfileStore.cs` - file-backed custom role-profile catalog and audit-event persistence.
- `Infrastructure/UserAccountStore.cs` - file-backed user account persistence, audit-event persistence, hash policy, and company-scoped account metadata.
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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ScopedAccessServiceTests|FullyQualifiedName~FundAccountTraversalQueryServiceTests" --logger "console;verbosity=normal" /nr:false /p:EnableWindowsTargeting=true /p:UseSharedCompilation=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~RoleAuthorizationTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --filter FullyQualifiedName~SensitiveActionPolicyTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
```

### API and contract notes

Identity contracts publish role/profile, permission, and scoped-access DTOs through
`Meridian.Identity.Auth`. `LoginSessionService`, `UserProfileRegistry`, and
`FileRolePermissionProfileStore`, and `FileUserAccountStore` own session state, hash-backed user
profiles, governed account administration, account/session audit evidence, company ids for
company-scoped access policies, and custom role-profile permission persistence. `UserAccountDto`,
`UserAccountUpsertRequestDto`, and `UserAccountAuditEventDto` carry `CompanyId`, while
`UserProfileRegistry` preserves the same company id on authenticated profiles.
Scoped-access assignment DTOs carry role, permission, scope, effective-date, approval-limit,
segregation-of-duties rule, version, revocation, and audit-event metadata so authority governance
stays explicit across browser, desktop, endpoint, and policy consumers.
`FundStructureAccessScopeLineageProvider` is Identity-owned
and consumes the shared `Meridian.Contracts.Services.IFundStructureService` contract supplied by
Application composition, so scoped authorization can resolve ancestry without depending on
Application-only identity adapters. `FundAccountTraversalQueryService` also lives here under the
`Meridian.Identity` namespace; it consumes only shared `IFundStructureService` and cache
abstractions so endpoints can reuse the authoritative Fund -> Owns -> Account traversal without
keeping the implementation in Application. Cross-module endpoint, F#, browser, and WPF consumers
should reference Identity rather than reintroducing auth DTOs or identity state under
`Meridian.Contracts` or `Meridian.Ui.Shared`. The WPF desktop startup flow now consumes
`UserProfileRegistry` and `LoginSessionService` through `DesktopAuthenticationSession`, so desktop
operator login uses the same governed account store, `MDC_USERS` password hashes, and legacy
`MDC_USERNAME` / `MDC_PASSWORD_HASH` bootstrap source as the browser workstation without moving
credential storage into WPF.

### Migration and archive notes

`ScopedAccessService`, `IScopedAccessAssignmentService`, `IScopedAuthorizationService`,
`IScopedAccessAssignmentStore`, `FileScopedAccessAssignmentStore`, and
`PostgresScopedAccessAssignmentStore` moved from `src/Meridian.Application/Auth` into this module.
`RolePermissions`, `UserPermission`, `UserRole`, scoped-access assignment DTOs, and role-profile
request/result DTOs moved from `src/Meridian.Contracts/Auth` into this module. `LoginSessionService`,
`UserProfileRegistry`, `AuthenticationModeResolver`, `IRolePermissionProfileStore`, and
`FileRolePermissionProfileStore` moved from `src/Meridian.Ui.Shared` into this module.
`FundAccountTraversalQueryService` moved from `src/Meridian.Application/FundStructure` into this
module. Remaining Identity migration work is endpoint/middleware adapters and browser identity
presentation refinements.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
