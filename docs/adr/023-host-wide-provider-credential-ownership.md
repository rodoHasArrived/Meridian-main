# ADR-023: Credential Ownership for Host-Wide Runtime Providers

**Status:** Proposed
**Date:** 2026-09-30
**Owner:** core-team
**Reviewed:** 2026-09-30
**Deciders:** core-team (review pending)
**Supersedes:** —
**Superseded by:** —

## Context

`ProviderFactory` builds streaming and backfill providers once per host from the `DataSources`
configuration. Their output (quotes, trades, bars written to storage) is host-wide: every tenant the
host serves reads the same collected data.

PR #2931 added credentials scoped to a retained connection (tenant, connection, external account,
environment) and a scope-bound `StoredProviderCredentialResolver` constructor that never falls back to
provider-wide records, configuration or environment. That constructor has no production caller yet:

- `ProviderFeatureRegistration` registers the provider-wide resolver for the whole host.
- `DataSourceConfig` has no link to a connection, so a runtime source has no owner to resolve.
- `ProviderConnectionService.GetCredentialScopeForTenantAsync` is used only by tenant-bound flows
  (status, verify, setup) and tests.

So a credential saved on a connection is never used by runtime providers, which keep reading the
provider-wide record. Wiring the scoped resolver needs an answer to one question first: **whose
credentials may a provider that serves the whole host use?**

Two facts make the answer matter:

1. **The output is shared.** If tenant A's vendor account powers a host-wide stream, every tenant on
   the host consumes data fetched under A's entitlements, billing, rate limits and licence terms, and
   A's account appears in the vendor's audit trail for activity A did not initiate.
2. **Hosts already declare a tenant posture.** `TenantScopeEnforcementMode` is either
   `DeploymentBoundary` (one company per deployment is the actual control) or `FailClosed` (a shared
   deployment where cross-tenant reads are refused). A credential rule must hold under both.

## Decision

1. **One owner, fixed by configuration.** A host-wide provider has exactly one credential owner. That
   owner is fixed when the provider is constructed. It is never chosen per request or subscription,
   and never inferred, for example from "the only connection", the most recent connection or the
   first match.

2. **The default owner is the deployment.** Without an explicit binding, a host-wide provider keeps
   using the provider-wide vault record. That record belongs to the deployment operator, not to any
   tenant. This is today's behaviour, and it stays valid under both postures.

3. **A binding may name one connection owned by the host credential tenant.** A data source may
   instead name one retained connection through a new server-assigned field,
   `DataSourceConfig.CredentialConnectionId`. The binding is allowed only when that connection's
   retained tenant equals the host's declared credential tenant. That tenant is a new startup setting,
   `MERIDIAN_HOST_CREDENTIAL_TENANT`, read once like `MERIDIAN_TENANT_SCOPE_ENFORCEMENT` and refused at
   startup when present but malformed. Only the party that operates the host may lend its vendor
   account to data every tenant reads. The setting is required under both postures, so a binding is
   never resolved against an implicit tenant.

4. **Validate the binding at construction and fail closed.** Before a bound provider is built, the
   host resolves the scope with `GetCredentialScopeForTenantAsync(connectionId, hostCredentialTenant)`
   and checks all of the following:
   - the connection ID is unique;
   - retained ownership is complete (tenant, external account, credential environment);
   - the tenant is the host credential tenant;
   - the connection's canonical provider family matches the data source's provider;
   - the connection's credential environment matches the data source's environment (for example
     Alpaca `paper` or `live`);
   - the connection is enabled.

   Any failure stops that data source from starting and records a diagnostic. There is no fallback to
   the provider-wide record, configuration or environment; this is the scoped resolver's existing
   guarantee.

5. **Other tenants' connections never power host-wide providers.** A connection owned by any tenant
   other than the host credential tenant serves only tenant-bound operations: status, verification,
   setup, reconciliation and account-level flows. Per-tenant runtime providers with tenant-attributed
   output are a separate decision, deferred (see Alternative 2).

6. **Record the credential source, never its value.** At startup and in provider diagnostics, a bound
   provider reports its credential source: `provider-wide`, or `connection <id>` owned by
   `<tenant>`. Assigning or clearing a binding goes through the authenticated configuration-mutation
   path, so the change is attributed. Changing a binding or the host credential tenant takes effect
   on restart, like the posture switch.

## Implementation Links

These are the current seams the implementation will change or reuse. Nothing in this ADR is
implemented yet.

| Component | Location | Purpose |
|-----------|----------|---------|
| Scope-bound resolver | `src/Meridian.Application/Services/StoredProviderCredentialResolver.cs` | Resolves one connection scope with no fallback; gains a production caller |
| Host resolver registration | `src/Meridian.Application/Composition/Features/ProviderFeatureRegistration.cs` | Currently registers only the provider-wide resolver; will choose the resolver per data source |
| Runtime provider construction | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs` | Builds host-wide providers through `IProviderCredentialResolver` |
| Data source configuration | `src/Meridian.Core/Config/DataSourceConfig.cs` | Will carry the optional `CredentialConnectionId` binding |
| Retained ownership | `src/Meridian.Application/ProviderRouting/ProviderConnectionService.cs` | `GetCredentialScopeForTenantAsync` resolves the bound scope |
| Tenant posture | `src/Meridian.Contracts/Tenancy/TenantScopeEnforcement.cs` | Pattern for the new startup setting: read once, malformed values refused |

## Rationale

The question is ownership, not plumbing. A host-wide provider acts on behalf of whoever operates the
host, so its credentials must belong to that party. Keeping the provider-wide record as the default
changes nothing for current deployments. The one explicit, validated binding lets an operator manage
its runtime credentials as a first-class connection (scoped, verified, rotated and audited like any
other) without ever lending a customer tenant's account to the whole host. Tying the rule to a
declared tenant rather than to the posture keeps it deterministic even in a single-company deployment.

## Alternatives Considered

### Alternative 1: Provider-wide records only

Host-wide providers never use connection credentials.

**Pros:** no new configuration; nothing to validate.
**Cons:** runtime credentials stay outside the scoped, verified and rotated connection model, and
the unattributed provider-wide record becomes permanent.
**Why rejected:** it leaves the scoped model unusable for runtime and keeps the weaker path as the
only one. It remains the default under this decision, not the ceiling.

### Alternative 2: One runtime provider per tenant connection

Each tenant's connection gets its own provider instance, and its output is partitioned by tenant.

**Pros:** true isolation; each tenant pays for and owns its own data.
**Cons:** requires tenant-partitioned pipelines, storage, subscriptions and per-tenant rate limits,
which is a much larger change well beyond credential resolution.
**Why rejected for now:** deferred as its own decision. This ADR does not block it; per-tenant
instances would use tenant-bound scopes, not the host binding.

### Alternative 3: Choose credentials per request or subscription tenant

**Pros:** appears to attribute use to the requesting tenant.
**Cons:** a host-wide session cannot switch vendor accounts mid-stream, the data is still shared, and
the choice depends on who asked first.
**Why rejected:** nondeterministic, and it still lends one tenant's account to everyone.

### Alternative 4: Allow any tenant's connection through an administrator binding

**Pros:** maximum flexibility.
**Cons:** an administrator could attach a customer tenant's vendor account to data every tenant
reads, breaching that tenant's entitlements and licence terms.
**Why rejected:** this is exactly the cross-tenant credential use the scoped model exists to prevent.

## Consequences

### Positive

- Runtime credentials can be managed, verified and rotated as connections, with ownership checked
  at startup.
- Current deployments are unaffected; unbound data sources behave exactly as today.
- A misconfigured binding fails loudly at startup instead of silently borrowing another credential.

### Negative

- A new startup setting and configuration field to document and support.
- A bound data source does not start when its connection is disabled, rotated to a different
  environment or reassigned, until the binding or the connection is fixed.

### Neutral

- Tenant-owned connections of other tenants continue to serve only tenant-bound flows.
- Per-tenant runtime isolation remains open work (Alternative 2).

## Compliance

### Code Contracts

- A host-wide provider's `IProviderCredentialResolver` is either the provider-wide resolver or the
  scope-bound `StoredProviderCredentialResolver` for exactly one validated connection scope.
- No code path selects a connection scope for a host-wide provider without
  `DataSourceConfig.CredentialConnectionId` and the host credential tenant.

### Runtime Verification

The implementation PR must add tests that:

- bind a data source to a host-tenant connection and resolve only that connection's credentials;
- refuse a binding to another tenant's connection, an unknown or ambiguous ID, incomplete ownership, a
  provider-family mismatch, an environment mismatch, or a disabled connection, with no fallback read;
- refuse a present but malformed `MERIDIAN_HOST_CREDENTIAL_TENANT` at startup;
- keep unbound data sources on the provider-wide record;
- report the credential source in diagnostics without any secret value.

## Implementation Plan

1. This ADR (Proposed), for review.
2. Configuration and validation: add `CredentialConnectionId` and the host credential tenant setting;
   validate bindings when providers are built; build a scope-bound resolver per bound data source in
   `ProviderFeatureRegistration`; add the tests above.
3. Surfacing: show each provider's credential source in the provider status read models used by the
   browser and WPF workstations.
4. Update `PRD-002` evidence in the implementation tracker and the Application README.

## References

- [PR #2931: scoped provider credential ownership](https://github.com/rodoHasArrived/Meridian-main/pull/2931)
- [Implementation and Readiness Tracker, `PRD-002`](../product/implementation-todo-list.md)
- [Application README: scoped credential resolution](../../src/Meridian.Application/README.md)
