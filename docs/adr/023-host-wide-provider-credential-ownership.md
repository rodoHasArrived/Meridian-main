# ADR-023: Credential Ownership for Host-Wide Runtime Providers

**Status:** Proposed
**Date:** 2026-09-30
**Owner:** core-team
**Reviewed:** 2026-09-30
**Deciders:** core-team (review pending)
**Supersedes:** —
**Superseded by:** —

## Context

`ProviderFactory` builds streaming, backfill and symbol-search providers once per host. It builds one
provider for each registration in `ProviderCapabilityDescriptorCatalog`, which means one per provider
family and capability. It reads top-level configuration, not individual `DataSources.Sources` rows, and
a single `IProviderCredentialResolver` serves the whole factory. The providers' output (quotes, trades,
bars written to storage) is host-wide: every tenant the host serves reads the same collected data.

PR #2931 added credentials scoped to a retained connection (tenant, connection, external account,
environment). It also added a scope-bound `StoredProviderCredentialResolver` constructor that never
falls back to provider-wide records, configuration or environment. That constructor has no production
caller yet: `ProviderFeatureRegistration` registers the provider-wide resolver, and nothing links a
provider family to a connection. So credentials saved on a connection are never used by runtime
providers.

Wiring the scoped resolver needs an answer to one question first: **whose credentials may a provider
that serves the whole host use?** Four facts in the current code shape the answer:

1. **The output is shared.** If tenant A's vendor account powers a host-wide stream, every tenant on
   the host consumes data fetched under A's entitlements, billing, rate limits and licence terms, and
   A's account appears in the vendor's audit trail for activity A did not initiate.
2. **Hosts declare a tenant posture.** `TenantScopeEnforcementMode` is either `DeploymentBoundary` (one
   company per deployment is the actual control) or `FailClosed` (a shared deployment where
   cross-tenant reads are refused).
3. **The provider-wide record is writable by tenant roles.** A credential `PUT` without `connectionId`
   writes the provider-wide record (`ProviderConnectionEndpoints.ResolveConnectionService`). It needs
   only `ManageCredentials`, which the built-in Admin and Developer roles hold. Under `FailClosed`,
   the provider-wide record therefore cannot be treated as belonging to the deployment.
4. **Module credential overlays take precedence.** `ProviderFactory` wraps the resolver's context in
   `WithModuleCredentials`, and `ModuleCredentialContext.Get` returns module sidecar or
   environment-derived values before the resolver's own values.

## Decision

1. **One owner per provider family, fixed by configuration.** A host-wide provider family has exactly
   one credential owner. The owner applies to every host-wide registration of that family (streaming,
   backfill, symbol search) and is fixed when the providers are built. It is never chosen per request
   or subscription, and never inferred, for example from "the only connection", the most recent
   connection or the first match.

2. **The provider-wide default applies only under `DeploymentBoundary`.** There, the deployment is one
   company, so the provider-wide record is that company's, and an unbound family keeps using it as
   today. Under `FailClosed`:
   - a host-wide family with no binding does not start, and records a diagnostic;
   - the tenant-scoped credential routes refuse to create, change or remove provider-wide records,
     because a record a tenant role can write must not power data every tenant reads;
   - existing provider-wide records remain stored but are not used by host-wide providers.

   A deployment moving to `FailClosed` binds each host-wide family first (point 3).

3. **Bindings are keyed by provider family.** A new configuration section,
   `HostProviderCredentials.Bindings`, maps a canonical provider family ID to one retained connection
   ID. It sits at the factory's actual construction key; it is deliberately not a field on
   `DataSourceConfig`, whose rows do not correspond to runtime instances. A binding is allowed only
   when the connection's retained tenant equals the host's declared credential tenant. That tenant is
   a new startup setting, `MERIDIAN_HOST_CREDENTIAL_TENANT`, read once like
   `MERIDIAN_TENANT_SCOPE_ENFORCEMENT` and refused at startup when present but malformed. It is
   required whenever any binding exists, under both postures, so a binding is never resolved against
   an implicit tenant. Only the party operating the host may lend its vendor account to data every
   tenant reads.

4. **Validate every binding at construction and fail closed.** Before a bound family's providers are
   built, the host resolves the scope with
   `GetCredentialScopeForTenantAsync(connectionId, hostCredentialTenant)` and checks all of the
   following:
   - the connection ID is unique;
   - retained ownership is complete (tenant, external account, credential environment);
   - the tenant is the host credential tenant;
   - the connection's canonical provider family equals the binding key;
   - the connection's credential environment matches the family's configured environment (for
     example Alpaca `paper` or `live`);
   - the connection is enabled.

   Any failure stops that family from starting and records a diagnostic. There is no fallback to the
   provider-wide record, configuration or environment.

5. **Bound families take credentials from the scoped resolver only.** For a bound family, the
   factory uses the scope-bound `StoredProviderCredentialResolver` and **skips module credential
   overlays** (`WithModuleCredentials`). Module sidecar and environment values would otherwise take
   precedence over the scoped context and bypass the no-fallback guarantee.

6. **Binding changes are attributed and audited.** Bindings change only through a dedicated
   authenticated command, never through general configuration or data-source writes. That command:
   - requires a new deployment-level permission that no tenant role receives;
   - appends an audit event to the credential audit trail (`provider-credentials.audit.jsonl`) before
     the configuration commits, recording the actor, the provider family, and the previous and new
     connection IDs;
   - is the only writer of the section. Every other configuration writer, including the browser and
     desktop data-source edits, preserves `HostProviderCredentials` unchanged. Clearing a binding is an
     explicit use of the same command.

   Changes take effect on restart, like the posture switch.

7. **Other tenants' connections never power host-wide providers.** A connection owned by any tenant
   other than the host credential tenant serves only tenant-bound operations: status, verification,
   setup, reconciliation and account-level flows. Per-tenant runtime providers with tenant-attributed
   output are a separate decision, deferred (see Alternative 2).

8. **Record the credential source, never its value.** At startup and in provider diagnostics, each
   host-wide family reports its credential source: `provider-wide`, or `connection <id>` owned by
   `<tenant>`, or `unbound (refused)`.

## Implementation Links

These are the current seams the implementation will change or reuse. Nothing in this ADR is
implemented yet.

| Component | Location | Purpose |
|-----------|----------|---------|
| Scope-bound resolver | `src/Meridian.Application/Services/StoredProviderCredentialResolver.cs` | Resolves one connection scope with no fallback; gains a production caller |
| Host resolver registration | `src/Meridian.Application/Composition/Features/ProviderFeatureRegistration.cs` | Currently registers only the provider-wide resolver; will select the resolver per provider family |
| Runtime provider construction | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs` | Builds one provider per catalog registration; `CreateCredentialContext` applies module overlays |
| Module credential overlay | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs` | `WithModuleCredentials` / `ModuleCredentialContext`; must be skipped for bound families |
| Provider-wide credential writes | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs` | `ResolveConnectionService` returns the provider-wide service without `connectionId`; refused under `FailClosed` |
| Retained ownership | `src/Meridian.Application/ProviderRouting/ProviderConnectionService.cs` | `GetCredentialScopeForTenantAsync` resolves the bound scope |
| Credential audit trail | `src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs` | Existing append-only audit; records binding changes |
| Role permissions | `src/Meridian.Identity/Contracts/Auth/RolePermissions.cs` | Admin and Developer hold `ManageCredentials`; the binding permission must not be granted to tenant roles |
| Tenant posture | `src/Meridian.Contracts/Tenancy/TenantScopeEnforcement.cs` | Pattern for the new startup setting: read once, malformed values refused |

## Rationale

The question is ownership, not plumbing. A host-wide provider acts on behalf of whoever operates the
host, so its credentials must belong to that party, and the system must be able to prove it. Under
`DeploymentBoundary` the deployment is one company, so the provider-wide record already meets that bar
and nothing changes for current deployments. Under `FailClosed` a tenant role can write that record,
so it cannot prove host ownership. An explicit, validated and audited binding to a connection owned by
the declared host tenant can. Keying the binding by provider family matches how the factory actually
builds providers, and skipping module overlays makes the no-fallback guarantee hold end to end rather
than only inside the resolver.

## Alternatives Considered

### Alternative 1: Provider-wide records only

Host-wide providers never use connection credentials.

**Pros:** no new configuration; nothing to validate.
**Cons:** under `FailClosed` a tenant role can place its own account in the record every tenant's data
depends on; runtime credentials stay outside the scoped, verified and rotated connection model.
**Why rejected:** unsafe for shared deployments. It remains the default under `DeploymentBoundary` only.

### Alternative 2: One runtime provider per tenant connection

Each tenant's connection gets its own provider instance, and its output is partitioned by tenant.

**Pros:** true isolation; each tenant pays for and owns its own data.
**Cons:** requires tenant-partitioned pipelines, storage, subscriptions and per-tenant rate limits, plus
a per-connection factory redesign, which is a much larger change well beyond credential resolution.
**Why rejected for now:** deferred as its own decision. This ADR does not block it; per-tenant
instances would use tenant-bound scopes, not host bindings.

### Alternative 3: Choose credentials per request or subscription tenant

**Pros:** appears to attribute use to the requesting tenant.
**Cons:** a host-wide session cannot switch vendor accounts mid-stream, the data is still shared, and
the choice depends on who asked first.
**Why rejected:** nondeterministic, and it still lends one tenant's account to everyone.

### Alternative 4: Allow any tenant's connection through an administrator binding

**Pros:** maximum flexibility.
**Cons:** an administrator could attach a customer tenant's vendor account to data every tenant reads,
breaching that tenant's entitlements and licence terms.
**Why rejected:** this is exactly the cross-tenant credential use the scoped model exists to prevent.

### Alternative 5: A binding field on each data-source row

**Pros:** sits next to the per-source settings operators already edit.
**Cons:** data-source rows do not correspond to runtime provider instances, so a row-level binding
would be ambiguous or ignored. The existing browser and desktop edits also rebuild rows, which would
silently clear it.
**Why rejected:** the binding must sit at the factory's construction key and have its own writer.

## Consequences

### Positive

- Runtime credentials can be managed, verified and rotated as connections, with ownership checked at
  startup and every change attributed.
- `DeploymentBoundary` deployments are unaffected; unbound families behave exactly as today.
- Under `FailClosed`, no tenant role can place a credential behind data that every tenant reads.
- A misconfigured binding fails loudly at startup instead of silently borrowing another credential.

### Negative

- New configuration (`HostProviderCredentials`), a new startup setting, a new deployment-level
  permission and a dedicated command to support.
- A deployment moving to `FailClosed` must bind every host-wide family first, or those families will
  not start.
- A bound family does not start when its connection is disabled, rotated to a different environment
  or reassigned, until the binding or the connection is fixed.

### Neutral

- Existing provider-wide records remain stored under `FailClosed`; they are simply not used by
  host-wide providers.
- Tenant-owned connections of other tenants continue to serve only tenant-bound flows.
- Per-tenant runtime isolation remains open work (Alternative 2).

## Compliance

### Code Contracts

- A host-wide provider family's credential context is either the provider-wide resolver wrapped in
  module overlays (only under `DeploymentBoundary` and only when unbound), or the scope-bound
  `StoredProviderCredentialResolver` for exactly one validated connection scope, with no module overlay.
- No code path selects a connection scope for a host-wide family without a `HostProviderCredentials`
  binding and the host credential tenant.
- Only the dedicated binding command writes `HostProviderCredentials`.

### Runtime Verification

The implementation PR must add tests that:

- bind a family to a host-tenant connection and resolve only that connection's credentials, even when
  module sidecar or environment credentials exist for the family;
- refuse a binding to another tenant's connection, an unknown or ambiguous ID, incomplete ownership, a
  provider-family mismatch, an environment mismatch, or a disabled connection, with no fallback read;
- refuse a present but malformed `MERIDIAN_HOST_CREDENTIAL_TENANT` at startup;
- under `FailClosed`, refuse to start an unbound host-wide family, and refuse provider-wide credential
  writes through the tenant-scoped routes;
- under `DeploymentBoundary`, keep unbound families on the provider-wide record;
- record the actor, provider family, and previous and new connection IDs for every binding change, and
  deny the change to tenant roles;
- preserve an existing binding across unrelated configuration and data-source edits;
- report the credential source in diagnostics without any secret value.

## Implementation Plan

1. This ADR (Proposed), for review.
2. Binding model and command: add `HostProviderCredentials`, the host credential tenant setting, the
   deployment-level permission, and the audited binding command. Make every other configuration writer
   preserve the section.
3. Construction: validate bindings when providers are built; select the scope-bound resolver per bound
   family and skip module overlays; refuse unbound families and tenant-route provider-wide writes under
   `FailClosed`; add the tests above.
4. Surfacing: show each family's credential source in the provider status read models used by the
   browser and WPF workstations.
5. Update `PRD-002` evidence in the implementation tracker and the Application README.

## References

- [PR #2931: scoped provider credential ownership](https://github.com/rodoHasArrived/Meridian-main/pull/2931)
- [Implementation and Readiness Tracker, `PRD-002`](../product/implementation-todo-list.md)
- [Application README: scoped credential resolution](../../src/Meridian.Application/README.md)
