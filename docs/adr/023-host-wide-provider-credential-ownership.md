# ADR-023: Credential Ownership for Host-Wide Runtime Providers

**Status:** Proposed
**Date:** 2026-09-30
**Owner:** core-team
**Reviewed:** 2026-09-30
**Deciders:** core-team (review pending)
**Supersedes:** —
**Superseded by:** —

## Context

A Meridian host builds its runtime providers once, for everyone it serves:

- `ProviderFactory` builds one provider for each registration in `ProviderCapabilityDescriptorCatalog`,
  which means one per provider family and capability. The capabilities are streaming, backfill, symbol
  search, corporate actions, options chains and brokerage. It reads top-level configuration, not
  individual `DataSources.Sources` rows, and a single `IProviderCredentialResolver` serves the whole
  factory.
- `AddProviderServices` separately discovers plugin registrations and resolves their implementations
  directly from DI, outside `ProviderFactory`.

The market-data output (quotes, trades, bars, corporate actions, option chains written to storage) is
host-wide: every tenant the host serves reads it.

PR #2931 added credentials scoped to a retained connection (tenant, connection, external account,
environment). It also added a scope-bound `StoredProviderCredentialResolver` constructor that never
falls back to provider-wide records, configuration or environment. That constructor has no production
caller yet, so credentials saved on a connection are never used by runtime providers.

Wiring it needs an answer to one question first: **whose credentials may a provider that serves the
whole host use?** Facts in the current code shape the answer:

1. **The output is shared.** If tenant A's vendor account powers a host-wide stream, every tenant on
   the host consumes data fetched under A's entitlements, billing, rate limits and licence terms.
2. **Hosts declare a tenant posture.** `TenantScopeEnforcementMode` is either `DeploymentBoundary` (one
   company per deployment is the actual control) or `FailClosed` (a shared deployment where
   cross-tenant reads are refused).
3. **The provider-wide record is writable by tenant roles.** A credential `PUT` without `connectionId`
   writes it (`ProviderConnectionEndpoints.ResolveConnectionService`) and needs only
   `ManageCredentials`, which the built-in Admin and Developer roles hold.
4. **No permission establishes host authority.** A tenant Admin holds `ManageUsers`, and account
   validation (`UserAccountStore.ValidateAccountRequest`) accepts permission-name overrides. A tenant
   Admin could therefore grant any new permission to an account they control.
5. **Credentials can come from outside the resolver.**
   - Module overlays: `WithModuleCredentials` returns module sidecar or environment values before the
     resolver's.
   - Post-resolver fallbacks inside factories: for example, Alpaca's factory passes resolver results
     through `FirstNonBlank(...)` with `AlpacaCredentialEnvironment.Resolve(...)`, which can recover
     configuration or environment secrets.
6. **Configuration loading forgives errors.** `ConfigStore.LoadConfig` substitutes a default
   `AppConfig` when the file is malformed or unreadable.

## Decision

1. **Scope: host-wide market data.** This decision covers the host-wide market-data capabilities:
   streaming, backfill, symbol search, corporate actions and options chains, whether built by
   `ProviderFactory` or registered by a plugin.

   Execution credentials are separate and never come from a host binding. Under `FailClosed`, a
   host-wide brokerage gateway built from provider-wide credentials does not start. Execution
   credential ownership, for example tenant-bound account connections, is its own decision.

2. **One owner per credential-bearing family, fixed at startup.** A family that has a
   `ProviderCredentialCatalog` entry requiring credentials has exactly one credential owner for all of
   its host-wide market-data capabilities. That owner is fixed when the providers are built. It is
   never chosen per request and never inferred, for example from the only, first or most recent
   connection.

   A family without such an entry needs no credentials (for example Yahoo, Stooq or Edgar search). It
   runs with credential source `none` and is never refused for being unbound.

3. **Where each posture gets its credentials.**
   - **`DeploymentBoundary`:** the deployment is one company, so an unbound credential-bearing family
     keeps using the provider-wide record, as today. Bindings are optional.
   - **`FailClosed`:**
     - an unbound credential-bearing family does not start;
     - the tenant-scoped credential routes refuse provider-wide writes;
     - existing provider-wide records remain stored but unused by host-wide providers;
     - a plugin family starts only if the host operator lists it as credential-free (point 5). Plugins
       cannot be bound until a plugin ownership contract exists.

   A deployment moving to `FailClosed` binds each credential-bearing family first.

4. **Bindings live in a host-only file, loaded strictly.** Bindings map a canonical provider family ID
   to one retained connection ID. They live in a dedicated host file, `host-provider-credentials.json`
   beside the configuration, not in `AppConfig`. No HTTP endpoint or general configuration writer
   touches this file, so data-source edits cannot erase a binding. It is loaded strictly:
   - a missing file means no bindings;
   - a malformed or unreadable file makes every credential-bearing host-wide family refuse to start,
     under both postures;
   - defaults are never substituted.

5. **Only the host operator changes bindings, out of band.** Bindings and the plugin credential-free
   list change only through a host CLI command, `--host-credential-binding list|set|clear`, run on the
   host by whoever operates the process. The same pattern is used by `--fund-tenant-backfill`. No
   tenant account, role or permission can reach it, so tenant user administration cannot grant it. The
   host credential tenant is a startup setting, `MERIDIAN_HOST_CREDENTIAL_TENANT`:
   - it is read once, like `MERIDIAN_TENANT_SCOPE_ENFORCEMENT`;
   - it is refused at startup when present but malformed;
   - it is required whenever any binding exists.

6. **Validate a binding when it is set and again at every startup, and fail closed.** A binding is
   accepted, and its family started, only when all of the following hold:
   - the connection ID is unique;
   - retained ownership is complete (tenant, external account, credential environment);
   - the tenant is the host credential tenant;
   - the connection's canonical provider family equals the binding key;
   - the credential environment matches the family's configured environment;
   - the connection is enabled;
   - the scoped credential record is complete and `Verified` for its current credential generation,
     against the retained external account and environment.

   A family whose provider has no scoped verification cannot be bound until it does. Any failure stops
   that family from starting and records a diagnostic.

7. **Bound families take credentials from the scoped resolver only.** For a bound family:
   - the factory uses the scope-bound `StoredProviderCredentialResolver`;
   - module credential overlays are skipped;
   - every post-resolver fallback in the family's factories is disabled. The implementation must audit
     all factories, starting with Alpaca's `FirstNonBlank` / `AlpacaCredentialEnvironment.Resolve`
     path.

   A partial scoped record refuses to start rather than being completed from elsewhere.

8. **Binding changes are recoverably audited.** Each `set` or `clear` runs three steps under one
   correlation ID:
   1. append a `pending` event to the credential audit trail (`provider-credentials.audit.jsonl`),
      recording the actor, the provider family, and the previous and new connection IDs;
   2. write the binding file atomically;
   3. append `committed`, or `aborted` if the write failed.

   At startup, a `pending` event without an outcome is resolved against the file's actual state and
   closed as `committed (recovered)` or `aborted (recovered)`. A retry therefore never looks like a
   second change. The actor is the required `--actor` argument together with the OS identity that ran
   the command.

9. **Other tenants' connections never power host-wide providers.** A connection owned by any tenant
   other than the host credential tenant serves only tenant-bound operations: status, verification,
   setup, reconciliation and account-level flows. Per-tenant runtime providers are a separate decision,
   deferred (see Alternative 2).

10. **Report the credential source; keep the identifiers host-only.**
    - Tenant-visible provider read models and diagnostics show only the source kind: `provider-wide`,
      `host-bound`, `none` or `unbound (refused)`.
    - The bound connection ID and owning tenant appear only in the host log and in the host CLI's
      `list` output.
    - Secret values appear nowhere.

## Implementation Links

These are the current seams the implementation will change or reuse. Nothing in this ADR is
implemented yet.

| Component | Location | Purpose |
|-----------|----------|---------|
| Scope-bound resolver | `src/Meridian.Application/Services/StoredProviderCredentialResolver.cs` | Resolves one connection scope with no fallback; gains a production caller |
| Host resolver registration | `src/Meridian.Application/Composition/Features/ProviderFeatureRegistration.cs` | Currently registers only the provider-wide resolver; will select the resolver per family |
| Runtime provider construction | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs` | One provider per catalog registration; Alpaca's post-resolver fallback lives here |
| Module credential overlay | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs` | `WithModuleCredentials` / `ModuleCredentialContext`; skipped for bound families |
| Plugin registration | `src/Meridian.Infrastructure/Adapters/Core/ProviderServiceExtensions.Composition.cs` | Registers plugin families outside `ProviderFactory`; gated under `FailClosed` |
| Provider-wide credential writes | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs` | `ResolveConnectionService` without `connectionId`; refused under `FailClosed` |
| Retained ownership | `src/Meridian.Application/ProviderRouting/ProviderConnectionService.cs` | `GetCredentialScopeForTenantAsync` resolves the bound scope |
| Credential audit trail and verification | `src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs` | Existing append-only audit and per-generation verification state |
| Lenient configuration load | `src/Meridian.Application/Http/ConfigStore.cs` | `LoadConfig` substitutes defaults; the binding file must not use it |
| Account permission overrides | `src/Meridian.Identity/Infrastructure/UserAccountStore.cs` | Why no user permission can carry host authority |
| Out-of-band host command precedent | `src/Meridian.Application/Commands/FundStructureTenantBackfillCommand.cs` | Pattern for `--host-credential-binding` |
| Atomic file write | `src/Meridian.Storage/Archival/AtomicFileWriter.cs` | Writes the binding file |
| Tenant posture | `src/Meridian.Contracts/Tenancy/TenantScopeEnforcement.cs` | Pattern for the startup setting: read once, malformed values refused |

## Rationale

The question is ownership, not plumbing. A host-wide provider acts on behalf of whoever operates the
host, so its credentials must belong to that party, and the system must be able to prove it. Under
`DeploymentBoundary` the deployment is one company, so the provider-wide record meets that bar and
nothing changes for current deployments. Under `FailClosed`, tenant roles can write the provider-wide
record and can grant themselves any permission, so neither can prove host ownership.

An out-of-band host command can. So can a strictly loaded host-only file, a verified connection owned
by the declared host tenant, and a recoverable audit record. Keying bindings by provider family
matches how the factory builds providers. Removing every other credential source for bound families
makes the no-fallback guarantee hold end to end.

## Alternatives Considered

### Alternative 1: Provider-wide records only

**Pros:** no new configuration.
**Cons:** under `FailClosed` a tenant role can place its own account in the record every tenant's data
depends on.
**Why rejected:** unsafe for shared deployments. It remains the default under `DeploymentBoundary` only.

### Alternative 2: One runtime provider per tenant connection

**Pros:** true isolation; each tenant owns its own data.
**Cons:** requires tenant-partitioned pipelines, storage, subscriptions and rate limits, and a
per-connection factory redesign.
**Why rejected for now:** deferred as its own decision; it would use tenant-bound scopes, not host
bindings.

### Alternative 3: Choose credentials per request or subscription tenant

**Pros:** appears to attribute use to the requesting tenant.
**Cons:** a host-wide session cannot switch vendor accounts mid-stream, and the data is still shared.
**Why rejected:** nondeterministic, and it still lends one tenant's account to everyone.

### Alternative 4: Allow any tenant's connection through an administrator binding

**Pros:** maximum flexibility.
**Cons:** attaches a customer tenant's vendor account to data every tenant reads.
**Why rejected:** this is the cross-tenant credential use the scoped model exists to prevent.

### Alternative 5: A binding field on each data-source row

**Pros:** sits next to per-source settings.
**Cons:** rows do not correspond to runtime providers, and data-source edits rebuild rows.
**Why rejected:** the binding must sit at the factory's construction key and have its own writer.

### Alternative 6: An HTTP binding command gated by a new permission

**Pros:** manageable from the workstation.
**Cons:** tenant Admins can grant permissions to accounts they control, so the permission would not
prove host authority.
**Why rejected:** host authority has to come from outside the tenant identity model.

## Consequences

### Positive

- Runtime market-data credentials can be managed, verified and rotated as connections, with ownership
  proven at startup and every change recoverably audited.
- `DeploymentBoundary` deployments are unaffected; credential-free families are never refused.
- Under `FailClosed`, no tenant role can place a credential behind data every tenant reads, or learn
  which host connection powers it.

### Negative

- A new host file, CLI command and startup setting to support.
- A deployment moving to `FailClosed` must bind every credential-bearing family (with verified
  credentials) and list any credential-free plugins first, or those families will not start.
- Plugin families cannot be bound, and families without scoped verification cannot be bound, until
  that support exists.
- A bound family does not start after a rotation until its new credentials are verified.

### Neutral

- Existing provider-wide records remain stored under `FailClosed`, unused by host-wide providers.
- Execution credential ownership and per-tenant runtime isolation remain open decisions.

## Compliance

### Code Contracts

- A credential-bearing host-wide market-data family's credential context is either:
  - the provider-wide resolver, only under `DeploymentBoundary` and only when unbound; or
  - the scope-bound `StoredProviderCredentialResolver` for exactly one validated, verified connection
    scope, with no module overlay and no post-resolver fallback.
- Only the `--host-credential-binding` command writes `host-provider-credentials.json`, and the file
  is never loaded through a default-substituting path.

### Runtime Verification

The implementation PR must add tests that:

- bind a family and resolve only its connection's credentials, even when module sidecar,
  configuration or environment credentials exist;
- refuse to start a bound family whose scoped record is partial, rather than completing it from
  elsewhere;
- refuse a binding to another tenant's connection, an unknown or ambiguous ID, incomplete ownership, a
  provider-family or environment mismatch, a disabled connection, or missing, partial, failed or
  post-rotation unverified credentials;
- refuse a present but malformed `MERIDIAN_HOST_CREDENTIAL_TENANT` at startup;
- treat a missing binding file as no bindings, and a malformed or unreadable one as refusal for every
  credential-bearing family, under both postures;
- under `FailClosed`:
  - refuse unbound credential-bearing families, provider-wide writes through tenant routes,
    unlisted plugin families, and provider-wide brokerage gateways;
  - start credential-free families with source `none`;
- under `DeploymentBoundary`, keep unbound families on the provider-wide record;
- record `pending` and `committed` or `aborted` events with the actor, family and connection IDs,
  including recovery after a crash between the audit append and the file write;
- show only the source kind in tenant-visible read models, never the connection ID or tenant.

## Implementation Plan

1. This ADR (Proposed), for review.
2. Binding file and command: strict loader, `--host-credential-binding` with recoverable audit, the host
   credential tenant setting, and the plugin credential-free list.
3. Construction: validate and verify bindings at startup; per-family scoped resolver with module
   overlays and post-resolver fallbacks removed; `FailClosed` refusals (unbound families, plugins,
   brokerage gateways, tenant-route provider-wide writes); the tests above.
4. Surfacing: source kind in the browser and WPF provider read models; identifiers in the host log and
   CLI only.
5. Update `PRD-002` evidence in the implementation tracker and the Application README.

## References

- [PR #2931: scoped provider credential ownership](https://github.com/rodoHasArrived/Meridian-main/pull/2931)
- [Implementation and Readiness Tracker, `PRD-002`](../product/implementation-todo-list.md)
- [Application README: scoped credential resolution](../../src/Meridian.Application/README.md)
- [Fund-structure tenant backfill runbook](../operators/fund-structure-tenant-backfill.md) (out-of-band host command precedent)
