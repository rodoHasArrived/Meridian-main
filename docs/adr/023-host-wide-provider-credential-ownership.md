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
  search, symbol resolution, corporate actions, options chains and brokerage. It reads top-level
  configuration, not individual `DataSources.Sources` rows, and a single `IProviderCredentialResolver`
  serves the whole factory.
- `AddProviderServices` separately discovers plugin registrations and resolves their implementations
  directly from DI, outside `ProviderFactory`.
- Some catalogued capabilities are also built outside the factory. OpenFIGI symbol resolution is
  constructed straight from `Backfill.Providers.OpenFigi.ApiKey` in `SymbolManagementFeatureRegistration`
  (the canonical symbol-registry spine) and in the `BackfillCoordinator` fallback. The process-wide
  `PolygonCorporateActionFetcher` hosted service (registered in `StorageFeatureRegistration`) reads
  `MERIDIAN_POLYGON_API_KEY`, `POLYGON_API_KEY` or configuration directly and writes corporate actions
  into the shared Security Master.

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
3. **The provider-wide record is writable by tenant roles, through several routes.** A credential
   `PUT` without `connectionId` writes it (`ProviderConnectionEndpoints.ResolveConnectionService`). So
   do the compatibility surfaces: `CredentialEndpoints` (save, delete and test),
   `ProviderCredentialEndpoints` (connection test) and `ProviderModuleEndpoints` (module credentials).
   They reach the unscoped operations of `ProviderConnectionLifecycleService` or
   `ProviderModuleSetupService` with `ManageCredentials` or `ManageProviders`, which the built-in
   tenant roles hold.
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
7. **The credential catalog is incomplete.** `ProviderCredentialCatalog` does not describe every
   credential use:
   - NYSE has no catalog entry, yet `NyseMarketDataClient.ProviderCredentialFields` requires `ApiKey`
     and `ApiSecret`, and `NYSEOptions` reads them from environment variables;
   - the `ibkr` entry lists no required fields, because the account is whichever TWS or IB Gateway
     session is logged in, and `CreateIbBrokerageGateway` builds its gateway from `AppConfig.IB`.

## Decision

1. **Scope: host-wide market data.** This decision covers the host-wide market-data capabilities:
   streaming, backfill, symbol search, symbol resolution, corporate actions and options chains. It
   applies wherever they are built: by `ProviderFactory`, by a plugin, or at a construction site
   outside the factory such as the OpenFIGI resolvers.

   Execution credentials are separate and never come from a host binding. Under `FailClosed`, every
   host-wide brokerage capability is refused, whatever its credential source: a provider-wide record,
   configuration, environment, or an external session such as a TWS or IB Gateway login. Execution
   credential ownership, for example tenant-bound account connections, is its own decision.

2. **Every family is explicitly classified; each credential-bearing family has one owner, fixed at
   startup.** Every enabled family, built-in or plugin, carries one credential classification:
   - **credential-bearing:** its `ProviderCredentialCatalog` entry requires fields, or any of its
     adapters declares a required `ProviderCredentialFields` entry (for example NYSE);
   - **session-backed:** the account is set by an external session the host cannot attribute or
     verify (for example `ibkr` through TWS or IB Gateway). It is treated as credential-bearing but
     cannot be bound;
   - **credential-free:** explicitly listed (for example Synthetic, Yahoo, Stooq and Edgar search), and
     no adapter in the family declares a required credential field. It runs with credential source
     `none` and is never refused for being unbound.

   A family with no classification is treated as unbound credential-bearing. A credential-bearing
   family has exactly one credential owner for all of its host-wide market-data capabilities. That
   owner is fixed when the providers are built. It is never chosen per request and never inferred, for
   example from the only, first or most recent connection.

3. **Where each posture gets its credentials.**
   - **`DeploymentBoundary`:** the deployment is one company, so an unbound credential-bearing family
     keeps using the provider-wide record, as today. Bindings are optional.
   - **`FailClosed`:**
     - an unbound credential-bearing, session-backed or unclassified family does not start. For
       OpenFIGI, this means the canonical symbol resolver runs registry-only and `BackfillCoordinator`
       creates no fallback resolver;
     - provider-wide credential writes and deletes are refused at the service seam, in the unscoped
       operations of `ProviderConnectionLifecycleService` and in the credential writes of
       `ProviderModuleSetupService`. Every route inherits the refusal: the canonical route, the
       compatibility credential and connection-test routes, and the provider-module routes. Module
       settings that carry no credentials can still change;
     - existing provider-wide records remain stored but unused by host-wide providers;
     - a plugin family starts only if the host operator lists it as credential-free (point 5). Plugins
       cannot be bound until a plugin ownership contract exists.

   A deployment moving to `FailClosed` first binds each bindable credential-bearing family. It
   disables session-backed families (such as `ibkr`) and families that cannot yet be bound, or accepts
   that they will not start.

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

7. **Host-wide credentials come from one per-family selection; bound families use the scoped
   resolver only.** Every host-wide construction site obtains credentials from a single per-family
   host credential selection: the provider-wide resolver (only when unbound under
   `DeploymentBoundary`), the scoped resolver (when bound), or nothing (refused, or `none`). That
   includes the sites outside `ProviderFactory`: the OpenFIGI resolvers in
   `SymbolManagementFeatureRegistration` and `BackfillCoordinator`, and `PolygonCorporateActionFetcher`.

   Reviews keep finding new direct reads, so completeness is enforced rather than listed. An
   architecture test fails when production code outside the selection reads a provider credential
   environment variable or configuration key. The names come from the credential catalog, adapter
   `ProviderCredentialFields`, and known aliases such as `MERIDIAN_POLYGON_API_KEY`. Reads that only
   probe presence, or that serve tenant-bound flows, sit on an explicit allowlist, and each entry has
   a stated reason.

   For a bound family:
   - the selection returns the scope-bound `StoredProviderCredentialResolver`;
   - module credential overlays are skipped;
   - every post-resolver fallback in the family's factories and options types is disabled. The
     implementation must audit all of them, starting with Alpaca's `FirstNonBlank` /
     `AlpacaCredentialEnvironment.Resolve` path and `NYSEOptions`, which falls back to `NYSE_API_KEY`
     and `NYSE_API_SECRET`.

   A partial scoped record refuses to start rather than being completed from elsewhere.

8. **Binding changes are serialized and recoverably audited.** Each `set` or `clear` holds an
   exclusive host lock (`host-provider-credentials.lock`, opened for exclusive access) for its whole
   transaction, and runs these steps under one correlation ID:
   1. read the current binding;
   2. append a `pending` event to the credential audit trail (`provider-credentials.audit.jsonl`),
      recording the actor, the provider family, and the previous and new connection IDs;
   3. write the binding file atomically;
   4. append `committed`, or `aborted` if the write failed.

   Concurrent commands therefore run one after another, and each event's previous connection ID is
   the state it actually replaced. A command that cannot take the lock within a timeout fails without
   writing anything.

   At startup, under the same lock, a `pending` event without an outcome is resolved against the
   file's actual state and closed as `committed (recovered)` or `aborted (recovered)`. A retry therefore
   never looks like a second change. The actor is the required `--actor` argument together with the OS
   identity that ran the command.

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
| Capability catalog | `src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs` | The built-in families that need a credential classification |
| Credential catalog | `src/Meridian.DataIntegration/Credentials/ProviderCredentialCatalog.cs` | Required fields per family; has no NYSE entry and no required `ibkr` fields |
| Adapter credential metadata | `src/Meridian.ProviderSdk/IProviderMetadata.cs` | `ProviderCredentialFields`, checked against each classification (NYSE declares required fields) |
| OpenFIGI resolution outside the factory | `src/Meridian.Application/Composition/Features/SymbolManagementFeatureRegistration.cs`, `src/Meridian.Application/Backfill/BackfillCoordinator.cs` | Build `OpenFigiSymbolResolver` from configuration; move to the per-family selection |
| Module credential overlay and brokerage gateways | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs` | `WithModuleCredentials` / `ModuleCredentialContext`, skipped for bound families; `CreateIbBrokerageGateway` and the other gateways, refused under `FailClosed` |
| Plugin registration | `src/Meridian.Infrastructure/Adapters/Core/ProviderServiceExtensions.Composition.cs` | Registers plugin families outside `ProviderFactory`; gated under `FailClosed` |
| Provider-wide credential writes | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs`, `CredentialEndpoints.cs`, `ProviderCredentialEndpoints.cs`, `ProviderModuleEndpoints.cs` | Canonical and compatibility routes that reach unscoped credential writes; each inherits the service-seam refusal |
| Unscoped credential services | `src/Meridian.Ui.Shared/Services/ProviderConnectionLifecycleService.cs`, `src/Meridian.Ui.Shared/Services/ProviderModuleSetupService.cs` | Where provider-wide writes and deletes are refused under `FailClosed` |
| Polygon corporate-action ingestion | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonCorporateActionFetcher.cs`, `src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs` | Process-wide hosted fetcher that reads Polygon keys directly; moves to the per-family selection |
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
by the declared host tenant, and a serialized, recoverable audit record. Keying bindings by provider
family matches how the factory builds providers. Removing every other credential source for bound
families, at every construction site, makes the no-fallback guarantee hold end to end. Enforcing the
single selection with an architecture test, and refusing provider-wide writes at the service seam
rather than per route, keeps both guarantees complete as new code arrives. Classifying
every family explicitly, and checking that classification against adapter metadata, keeps an
uncatalogued credential use from passing as credential-free.

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
- A deployment moving to `FailClosed` must bind every bindable credential-bearing family (with verified
  credentials) and list any credential-free plugins first, or those families will not start.
- Plugin families cannot be bound, and families without scoped verification cannot be bound, until
  that support exists.
- Under `FailClosed`, session-backed families such as `ibkr` and every host-wide brokerage capability
  do not run, and OpenFIGI enrichment stays off until OpenFIGI is bound.
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
- Only the `--host-credential-binding` command writes `host-provider-credentials.json`, always while
  holding the host lock, and the file is never loaded through a default-substituting path.
- Host-wide construction sites obtain credentials only from the per-family host credential selection;
  an architecture test enforces this with a reviewed allowlist.
- Under `FailClosed`, provider-wide credential writes and deletes are refused inside the unscoped
  service operations, not in individual routes.
- Every enabled family has exactly one credential classification, and `credential-free` never applies
  to a family whose credential catalog entry or adapter metadata requires a field.

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
- fail the architecture scan when production code outside the selection reads a provider credential
  environment variable or configuration key that is not on the allowlist;
- classify every catalog family, keep NYSE credential-bearing and `ibkr` session-backed, and fail
  when a `credential-free` family's catalog entry or adapter metadata requires a field;
- under `FailClosed`:
  - refuse unbound credential-bearing, session-backed and unclassified families, unlisted plugin
    families, and every host-wide brokerage gateway, including `ibkr`;
  - refuse provider-wide credential writes and deletes route by route: the canonical credential
    route, the `CredentialEndpoints` save, delete and test routes, the `ProviderCredentialEndpoints`
    connection test, and provider-module create, update and delete requests that carry credentials;
  - start no `PolygonCorporateActionFetcher` ingestion on an unscoped key when Polygon is unbound;
  - run the canonical symbol resolver registry-only when OpenFIGI is unbound, without reading the
    configured key, and create no `BackfillCoordinator` fallback resolver;
  - start credential-free families with source `none`;
- under `DeploymentBoundary`, keep unbound families on the provider-wide record;
- record `pending` and `committed` or `aborted` events with the actor, family and connection IDs,
  including recovery after a crash between the audit append and the file write;
- serialize concurrent `set` and `clear` commands so each event's previous connection ID is the state
  it replaced, and fail a command that cannot take the lock without writing;
- show only the source kind in tenant-visible read models, never the connection ID or tenant.

## Implementation Plan

1. This ADR (Proposed), for review.
2. Binding file and command: strict loader, `--host-credential-binding` with a host lock and
   recoverable audit, the host credential tenant setting, and the plugin credential-free list.
3. Classification: a credential classification for every family, with a consistency check against
   the credential catalog and adapter metadata (adding the missing NYSE entry).
4. Construction: validate and verify bindings at startup; the per-family host credential selection
   at every construction site (including OpenFIGI's and the Polygon corporate-action fetcher), with
   module overlays and post-resolver fallbacks removed and the architecture scan in place; `FailClosed`
   refusals (unbound, session-backed and unclassified families, plugins, every brokerage gateway,
   provider-wide writes at the service seam); the tests above.
5. Surfacing: source kind in the browser and WPF provider read models; identifiers in the host log and
   CLI only.
6. Update `PRD-002` evidence in the implementation tracker and the Application README.

## References

- [PR #2931: scoped provider credential ownership](https://github.com/rodoHasArrived/Meridian-main/pull/2931)
- [Implementation and Readiness Tracker, `PRD-002`](../product/implementation-todo-list.md)
- [Application README: scoped credential resolution](../../src/Meridian.Application/README.md)
- [Fund-structure tenant backfill runbook](../operators/fund-structure-tenant-backfill.md) (out-of-band host command precedent)
