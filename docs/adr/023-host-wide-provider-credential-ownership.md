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
   So does `ProviderEndpoints.ConfigureProvider` without a `connectionId`, through
   `ProviderSetupService`. Each of these routes reaches the credential store's unscoped operations
   (`SaveAsync`, `SaveOAuthTokenAsync`, `DeleteAsync`) or the module credential store, through
   `ProviderConnectionLifecycleService`, `ProviderModuleSetupService`, `ProviderSetupService` and
   others. They need only `ManageCredentials` or `ManageProviders`, which the built-in tenant roles
   hold.
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

   Execution is out of scope and never uses a host binding. Execution credential ownership, for
   example tenant-bound account connections, is its own decision. Until that decision exists, a
   `FailClosed` host composes no execution path to an external brokerage account, whatever the
   credential source (a provider-wide record, configuration, environment, or an external session
   such as a TWS or IB Gateway login):
   - startup refuses live execution configuration (`Execution:Brokerage` with live execution enabled
     or a non-paper gateway), so the host stays on its paper gateways;
   - no brokerage gateway is composed, whether from the catalog through `ProviderFactory` or from the
     host's own registration (`AddHostedBrokerageGateways` in `UiServer`, including optional StockSharp
     gateways);
   - no brokerage sync adapter is composed (account catalog, portfolio or activity sync);
   - no brokerage connection service or route is composed, such as the Alpaca connect and revoke and
     the Robinhood connect, callback and revoke routes in `BrokerageConnectionEndpoints`.

   Paper execution uses no external account or credential, so it stays: the paper `IOrderGateway`,
   the order manager and `ExecutionEndpoints`, all routed only to paper gateways.

   Reviews kept finding execution registrations outside the catalog, so this is decided at host
   composition, not per site. A test checks the real `UiServer` service collection and endpoint
   table: every order and execution gateway is a paper gateway, and no brokerage gateway, sync
   adapter, or brokerage connection service or route exists.

2. **Every family is explicitly classified; each credential-bearing family has one owner, fixed at
   startup.** Every enabled family, built-in or plugin, carries one credential classification:
   - **credential-bearing:** its `ProviderCredentialCatalog` entry requires fields, or any of its
     adapters reports `IProviderMetadata.RequiresCredentials`. That property is true whenever the
     adapter declares any `ProviderCredentialFields` entry, required or not (for example NYSE);
   - **session-backed:** an explicit override for a family whose account is set by an external
     session the host cannot attribute or verify (for example `ibkr` through TWS or IB Gateway, whose
     declared fields are connection settings). It is treated as credential-bearing but cannot be
     bound;
   - **credential-free:** explicitly listed (for example Synthetic, Yahoo, Stooq and Edgar search). Its
     catalog entry requires no field, and no adapter in the family reports `RequiresCredentials`. It
     runs with credential source `none` and is never refused for being unbound.

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
     - provider-wide credentials for any host-wide market-data family (built-in or plugin) can be
       neither read nor written. This is enforced at the credential store boundary itself:
       - the unscoped reads (`ReadForProviderAsync`, `ReadOAuthTokensAsync`) return nothing for
         those families;
       - every operation that writes or deletes an unscoped record is refused, including the
         migration imports. That covers `SaveAsync`, `SaveRotatedCredentialsAsync`,
         `SaveOAuthTokenAsync`, `DeleteAsync`, `ImportLegacyAsync` and `ImportOAuthTokensAsync`. A
         reflection test fails if any unscoped write method of the store interfaces lacks the
         guard, so a new one cannot slip past it;
       - the module credential store's reads, writes and deletes are refused for those families;
       - the provider-wide `IProviderCredentialResolver` yields nothing for them.

       Every caller inherits this, whichever route, service or direct dependency it came from: the
       canonical credential and verification routes (`ProviderConnectionLifecycleService.VerifyAsync`
       reads the record and sends it to the vendor), the compatibility credential and
       connection-test routes, the provider-module routes, `ProviderEndpoints.ConfigureProvider`,
       and any code that injects the store or the provider-wide resolver directly. No unscoped
       verification or test can transmit a provider-wide credential. Scoped operations, credentials
       of non-market-data integrations (such as accounting and financial connectivity), and module
       settings that carry no credentials are unaffected;
     - the configuration writer boundary (`ConfigStore`) refuses to persist a value into any
       provider credential configuration key, using the same key list as the architecture scan in
       point 7. Routes that write credentials into `AppConfig`, such as `ConfigEndpoints.ConfigAlpaca`
       and `ProviderEndpoints.ConfigDataSources`, therefore cannot install provider-wide
       credentials either;
     - existing provider-wide records therefore remain stored but unreadable, until the deployment
       returns to `DeploymentBoundary`;
     - a plugin family starts only if the host operator lists it as credential-free (point 5). Plugins
       cannot be bound until a plugin ownership contract exists.

   A deployment moving to `FailClosed` first binds each bindable credential-bearing family. It
   disables session-backed families (such as `ibkr`) and families that cannot yet be bound, or accepts
   that they will not start.

4. **Bindings live in a host-only file, loaded strictly.** Bindings map a canonical provider family ID
   to one retained connection ID. Each binding also stores the complete scope it was validated
   against: tenant, connection, external account and credential environment. Claims (point 6) can
   therefore always be rebuilt from the host state alone, without loading configuration. They live, together with the plugin credential-free list (point 5),
   in a dedicated host file beside the configuration, not in `AppConfig`. This document calls the
   file's contents the host credential state. Each change is recorded in a journal beside it (point
   8). The host file, the journal and the host lock are named after the configuration file, so two
   configurations in one directory never share them. The configuration identity used for these
   names, for journal entries and for the vault claim is the canonical real path of the
   configuration file: rooted, with `.` and `..` resolved, symlinks resolved, and case-folded on
   case-insensitive file systems. Aliases of one file therefore always map to one identity:
   - `<config>.host-provider-credentials.json`;
   - `<config>.host-provider-credentials.journal.jsonl`;
   - `<config>.host-provider-credentials.lock`. No HTTP
   endpoint or general configuration writer touches either file, so data-source edits cannot erase a
   binding. Both are loaded strictly:
   - a missing state file and a missing journal together mean a fresh installation with no bindings,
     but only when this configuration's vault claim file (point 6) is also absent or empty. Any other
     combination is a partial loss:
     - a missing state file with a journal that records a committed state;
     - a state file with a missing journal;
     - both sidecars missing while the vault still holds a non-empty claim for this configuration.

     Either way, credential-bearing families refuse to start until `recover`. Clearing bindings is
     an explicit, journalled `clear`, never a deleted file;
   - a malformed or unreadable state file, or one whose digest differs from the journal's last
     committed digest, makes every credential-bearing host-wide family refuse to start, under both
     postures;
   - defaults are never substituted.

5. **Only the host operator changes bindings, out of band.** Bindings and the plugin credential-free
   list change only through a host CLI command, `--host-credential-binding
   list|set|clear|plugin-allow|plugin-revoke|recover`, run on the host by whoever operates the
   process. The same pattern is used by `--fund-tenant-backfill`. No
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

   Providers can also be constructed lazily after startup, for example from `ProviderRegistry`
   factories. So the credential checks run again at every resolution, not only at startup. A
   resolution is refused unless the record's current generation is `Verified`. A verification failure
   recorded after startup therefore reaches no newly constructed provider.

   Providers already constructed may hold a copy of the credential. `AlpacaHistoricalDataProvider`,
   for example, copies it into fields and HTTP headers, and several hosts may be running on one
   configuration (point 8). So **a bound connection's credential is frozen while it is bound**: there
   is never a change to propagate to providers already running in any host.
   - The store boundary refuses to save, rotate or delete the credential of any bound scope.
   - Several configurations can share one `DataRoot`, and so one vault. So bound scopes are claimed at
     the vault itself, in one claim file per configuration, in a directory beside the vault
     (`provider-credentials.bound-scopes/<configuration identity>.json`). Each configuration writes
     only its own claim file. The store refuses a mutation of any scope that any claim file names,
     so a configuration that holds the same connection unbound cannot change a scope that another
     configuration has bound.
   - The claim file is derived from the scopes stored in the committed host state, never from
     configuration. It is always written atomically, and in the safe order: a `set` writes the new
     claim before publishing the state file, and a `clear` removes the claim only after publishing
     it. A crash between the two can therefore only leave an extra claim, which freezes more, never
     less. Recovery, startup and every command then rewrite the claim file from the committed state
     under the vault lock (point 8). If the committed state cannot be read, the existing claim file
     is left as it is.
   - Claim files are parsed strictly. If any claim file in the directory is malformed, truncated or
     unreadable, the store cannot know which scopes it covers, so it refuses every scoped save,
     rotation and delete on that vault. The refusal lasts until the owning configuration's
     `recover` rewrites the file.
   - The host state records the vault location it was bound against. At startup, a bound family
     whose configured `DataRoot` resolves to a different vault refuses to start. While any binding
     exists, the configuration writer refuses to change `DataRoot` (for example through
     `ConfigEndpoints.UpdateStorage`); moving the vault requires `clear` first.
   - The configuration refuses to disable or delete a connection that is bound.

   To rotate a bound credential, the operator:
   1. stops the hosts and runs `clear`;
   2. starts a host and rotates and verifies the credential through the ordinary scoped routes,
      with the family unbound;
   3. stops the hosts and runs `set`.

   During step 2 the family follows the unbound rules of point 3. A verification failure on a bound
   credential does not change the secret, so nothing is frozen against it: it is reported in
   diagnostics, and the next startup refuses the family.

7. **Host-wide credentials come from one per-family selection; bound families use the scoped
   resolver only.** Every host-wide construction site obtains credentials from a single per-family
   host credential selection: the provider-wide resolver (only when unbound under
   `DeploymentBoundary`), the scoped resolver (when bound), or nothing (refused, or `none`). That
   includes the sites outside `ProviderFactory`: the OpenFIGI resolvers in
   `SymbolManagementFeatureRegistration` and `BackfillCoordinator`, and `PolygonCorporateActionFetcher`.

   Reviews keep finding new direct reads, so completeness is enforced rather than listed:
   - under `FailClosed`, the store boundary makes provider-wide market-data credentials unreadable
     (point 3), so a direct dependency on the store or on the provider-wide resolver obtains
     nothing;
   - an architecture test also fails when host-wide market-data construction code depends on
     `IProviderCredentialStore` or the provider-wide `IProviderCredentialResolver` other than
     through the selection;
   - an architecture test fails when production code outside the selection reads a provider
     credential environment variable or configuration key. The names come from the credential catalog, adapter
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

8. **Every change to the host credential state is one offline, journalled transaction.** This covers
   `set`, `clear`, `plugin-allow` and `plugin-revoke` alike, because the plugin credential-free list
   decides, just as a binding does, what may start under `FailClosed`.
   - **Its own journal.** The authoritative audit and recovery record is the append-only journal
     beside the state file, `<config>.host-provider-credentials.journal.jsonl`. It is not the shared
     credential audit trail. Two configurations that share a `DataRoot`, and so share a credential vault and its
     audit trail, therefore never see or recover each other's transactions. Each entry also records a
     stable identity for the host configuration (its canonical real path, see point 4), and an
     entry for a different identity makes the journal invalid.
   - **Parsed strictly.** The journal is parsed strictly. A malformed, truncated or unreadable entry,
     or an unreadable journal, makes credential-bearing families refuse to start and refuses mutation
     until `recover`. No entry is ever skipped.
   - **Offline only.** Every running host holds the host lock (`<config>.host-provider-credentials.lock`)
     in shared mode for its whole lifetime. Several hosts on one configuration, such as `SharedStorage`
     coordination instances or a workstation host beside a collector, can therefore run together.
     Every mutating command needs the lock in exclusive mode, so it fails while any host on this
     configuration is running, and no host can start while a command holds it. A change takes effect
     when the hosts next start, so no running provider keeps using state that has since changed.
     `list` only reads the files, which are always written atomically or append-only.
   - **One journal writer at a time.** Every write to the journal, whether from a mutating command or
     from recovery, is made while holding a second lock exclusively, the journal-writer lock
     (`<config>.host-provider-credentials.journal.lock`). A starting host first takes the host lock in
     shared mode, then the journal-writer lock, then runs recovery and validation, then releases the
     journal-writer lock. This is the same order every command uses, so a starting host and a command
     can never wait on each other:
     - hosts that start at the same moment therefore recover one at a time;
     - a host that joins hosts already running finds nothing pending, since every pending entry was
       resolved before they started and no command can run while they do;
     - if it does find an unresolved entry, the journal was changed outside the command, and it
       refuses to start.
   - **Lock order.** A transaction takes the host lock, then the journal-writer lock, then the
     configuration writer lock (the one
     `ConfigStore` takes, `<config>.lock`), then `provider-credentials.vault.lock`. Configuration and
     credential-store writers never take the host lock, and the implementation must show that no code
     path takes the configuration and vault locks in the reverse order. A command that cannot take a
     lock within a timeout fails without writing anything.
   - **Recover first.** Before changing anything, the transaction resolves every earlier `pending`
     entry against the current state file (see Recovery below). If any entry cannot be resolved, the
     command refuses to change the state. A later change therefore never overtakes an unresolved one.
   - **Steps.** Holding all three locks, under one correlation ID:
     1. read the current state;
     2. for `set`, run the point 6 checks against the retained connection and the credential record as
        they are now. Because the configuration and vault locks are held until step 5, no
        configuration writer can delete, disable or change the connection, and no save or rotation can
        install a new generation, between this check and the commit;
     3. append a `pending` entry to the journal. It records the actor, the host identity, the kind of
        change (binding or plugin classification), the subject (provider family or plugin ID), the
        previous and new values, and canonical digests of the complete host credential state before
        and after the change;
     4. for `set`, write the new claim file; write the state file atomically; for `clear`, then
        remove the claim. All of this runs under the vault lock already held;
     5. append the outcome, then release the locks in reverse order.
   - **Outcome from the file, not the exception.** `AtomicFileWriter` can throw after the rename has
     already published the file, for example from the directory sync. So on any write exception the
     command rereads the published file and compares the digest of the whole state:
     - if it matches the after-digest, the outcome is `committed`;
     - if it matches the before-digest, the outcome is `aborted`;
     - otherwise the entry stays `pending`, and the command exits non-zero.
   - **Recovery.** At startup and at the start of every mutating command, under the host lock and the
     journal-writer lock, each
     `pending` entry without an outcome is resolved against the state file:
     - if the digest of the whole file equals the entry's after-digest, it is closed as
       `committed (recovered)`;
     - if it equals the entry's before-digest, it is closed as `aborted (recovered)`;
     - otherwise the file was changed outside the command, even if only in an unrelated entry.
       Credential-bearing families then refuse to start, as for a malformed file, and mutation is
       refused, until the operator runs `recover`.

     After the entries are closed, recovery takes the vault lock and rewrites this configuration's
     claim file from the committed state, so a crash between the claim and the state file is
     repaired before any provider starts or any outcome is relied on. A retry therefore never looks
     like a second change.
   - **`recover`.** `recover` handles a lost state file, an unresolvable pending entry, or a mismatched
     last committed digest. It establishes a new authoritative baseline:
     1. it adopts the observed state file if it parses. If the file is malformed, it first moves it
        aside unchanged, so the evidence is kept. When the file is malformed or missing, it adopts an
        empty state with no bindings and no plugin entries, and writes that file atomically;
     2. it rewrites only this configuration's claim file, atomically, to match the adopted state,
        leaving every other configuration's claims untouched;
     3. it appends an `indeterminate` outcome for any unresolved entry, followed by a `baseline` entry.
        The `baseline` entry records the actor, the adopted state's digest, and the paths of any
        state file or journal it moved aside. That digest becomes the last committed digest.

     If the journal itself is invalid, `recover` first moves it aside unchanged, so the evidence is
     kept, and starts the new journal with the `baseline` entry. The next startup then validates
     against that baseline, and the adopted bindings still go through point 6.

   The actor is the required `--actor` argument together with the OS identity that ran the command.

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
| Adapter credential metadata | `src/Meridian.ProviderSdk/IProviderMetadata.cs` | `RequiresCredentials` (true whenever `ProviderCredentialFields` is non-empty), checked against each classification |
| OpenFIGI resolution outside the factory | `src/Meridian.Application/Composition/Features/SymbolManagementFeatureRegistration.cs`, `src/Meridian.Application/Backfill/BackfillCoordinator.cs` | Build `OpenFigiSymbolResolver` from configuration; move to the per-family selection |
| Module credential overlay and catalog brokerage gateways | `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs` | `WithModuleCredentials` / `ModuleCredentialContext`, skipped for bound families; `CreateIbBrokerageGateway` and the other catalog gateways, not composed under `FailClosed` |
| Hosted execution composition | `src/Meridian/UiServer.cs`, `src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs` | `AddHostedBrokerageGateways` registers gateways and sync adapters outside the catalog; not composed under `FailClosed` |
| Brokerage connection routes | `src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs` | Alpaca and Robinhood connect, callback and revoke routes that write unscoped credentials; not mapped under `FailClosed` |
| Lazy provider construction | `src/Meridian.Infrastructure/Adapters/Core/ProviderRegistry.cs` | Factories that resolve providers after startup; each resolution revalidates the credential generation |
| Plugin registration | `src/Meridian.Infrastructure/Adapters/Core/ProviderServiceExtensions.Composition.cs` | Registers plugin families outside `ProviderFactory`; gated under `FailClosed` |
| Provider-wide credential writes | `src/Meridian.Ui.Shared/Endpoints/ProviderConnectionEndpoints.cs`, `CredentialEndpoints.cs`, `ProviderCredentialEndpoints.cs`, `ProviderModuleEndpoints.cs` | Canonical and compatibility routes that reach unscoped credential writes; each inherits the service-seam refusal |
| Callers of unscoped credential writes | `src/Meridian.Ui.Shared/Services/ProviderConnectionLifecycleService.cs`, `src/Meridian.Ui.Shared/Services/ProviderModuleSetupService.cs`, `src/Meridian.Application/ProviderRouting/ProviderSetupService.cs` | Reach the store's unscoped operations; inherit the store-boundary refusal |
| Host execution composition | `src/Meridian/UiServer.cs` (`usesPaperGateway`), `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs` | Live execution refused under `FailClosed`; paper execution stays |
| Polygon corporate-action ingestion | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonCorporateActionFetcher.cs`, `src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs` | Process-wide hosted fetcher that reads Polygon keys directly; moves to the per-family selection |
| Retained ownership | `src/Meridian.Application/ProviderRouting/ProviderConnectionService.cs` | `GetCredentialScopeForTenantAsync` resolves the bound scope |
| Credential verification and vault lock | `src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs` | Per-generation verification state; `provider-credentials.vault.lock`, held through binding validation and commit; checks every configuration's claim file before any scoped write; `ImportLegacyAsync` and `ImportOAuthTokensAsync` guarded |
| Configuration credential and storage writers | `src/Meridian.Ui.Shared/Endpoints/ConfigEndpoints.cs` (`ConfigAlpaca`, `UpdateStorage`), `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs` (`ConfigDataSources`) | Refused at the `ConfigStore` boundary for credential keys under `FailClosed`, and for `DataRoot` while bindings exist |
| Lenient configuration load | `src/Meridian.Application/Http/ConfigStore.cs` | `LoadConfig` substitutes defaults; the binding file must not use it |
| Account permission overrides | `src/Meridian.Identity/Infrastructure/UserAccountStore.cs` | Why no user permission can carry host authority |
| Out-of-band host command precedent | `src/Meridian.Application/Commands/FundStructureTenantBackfillCommand.cs` | Pattern for `--host-credential-binding` |
| Atomic file write | `src/Meridian.Storage/Archival/AtomicFileWriter.cs` | Writes the host credential state file |
| Configuration writer lock | `src/Meridian.Application/Http/ConfigStore.cs` | `<config>.lock`, held through binding validation and commit |
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
- Under `FailClosed`, session-backed families such as `ibkr` do not run, live execution and every
  other path to an external brokerage account stay off until the execution-ownership decision exists
  (paper execution stays), and OpenFIGI enrichment stays off until OpenFIGI is bound.
- Binding changes need a restart of every host on the configuration, and the CLI refuses them while
  any is running.
- Rotating a bound credential takes two offline steps, `clear` and then `set`, with the family unbound
  in between.
- A bound family does not start after a rotation until its new credentials are verified.

### Neutral

- Existing provider-wide market-data records remain stored under `FailClosed` but unreadable, until the
  deployment returns to `DeploymentBoundary`.
- Execution credential ownership and per-tenant runtime isolation remain open decisions.

## Compliance

### Code Contracts

- A credential-bearing host-wide market-data family's credential context is either:
  - the provider-wide resolver, only under `DeploymentBoundary` and only when unbound; or
  - the scope-bound `StoredProviderCredentialResolver` for exactly one validated, verified connection
    scope, with no module overlay and no post-resolver fallback.
- Only the `--host-credential-binding` command writes `<config>.host-provider-credentials.json`, always
  while holding the host lock in exclusive mode, and the file is never loaded through a default-substituting path.
- Host-wide construction sites obtain credentials only from the per-family host credential selection.
  Architecture tests enforce this for direct store and resolver dependencies and, with a reviewed
  allowlist, for environment and configuration reads.
- Under `FailClosed`, provider-wide credential writes and deletes for host-wide market-data families
  are refused at the credential store and module credential store boundary, not in services or
  routes.
- Under `FailClosed`, the host composes no execution path to an external brokerage account: live
  execution configuration is refused, and no brokerage gateway, sync adapter, or brokerage
  connection service or route is composed from any registration path.
- Every running host holds the host lock in shared mode for its lifetime, and mutating commands need
  it in exclusive mode. Every journal write holds the journal-writer lock. Every credential resolution
  for a bound family revalidates the current generation's verification, and a bound connection's
  credential and connection record cannot be changed or removed while it is bound.
- Every enabled family has exactly one credential classification, and `credential-free` never applies
  to a family whose credential catalog entry requires a field or whose adapter metadata reports
  `RequiresCredentials`.

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
  environment variable or configuration key that is not on the allowlist, or when host-wide
  market-data construction code depends on the credential store or the provider-wide resolver
  directly;
- classify every catalog family, keep NYSE credential-bearing and `ibkr` session-backed, and fail
  when a `credential-free` family's catalog entry requires a field or any of its adapters reports
  `RequiresCredentials`;
- under `FailClosed`:
  - refuse unbound credential-bearing, session-backed and unclassified families, and unlisted plugin
    families;
  - find only paper order and execution gateways, and no brokerage gateway, brokerage sync adapter,
    or brokerage connection service or route, in the real `UiServer` service collection and endpoint
    table, including the hosted Alpaca, IB, Robinhood and StockSharp registrations; refuse startup
    with live execution configured;
  - return nothing from unscoped credential-store and module-credential reads, and refuse unscoped
    writes and deletes, for a market-data family, directly at the store and route by route. This
    covers the canonical verification route, which must not contact the vendor, the canonical
    credential route, the
    `CredentialEndpoints` save, delete and test routes, the `ProviderCredentialEndpoints` connection
    test, `ProviderEndpoints.ConfigureProvider` without a `connectionId`, and provider-module create,
    update and delete requests that carry credentials; keep scoped writes and non-market-data
    integrations working;
  - start no `PolygonCorporateActionFetcher` ingestion on an unscoped key when Polygon is unbound;
  - run the canonical symbol resolver registry-only when OpenFIGI is unbound, without reading the
    configured key, and create no `BackfillCoordinator` fallback resolver;
  - start credential-free families with source `none`;
- under `DeploymentBoundary`, keep unbound families on the provider-wide record;
- record `pending` and `committed` or `aborted` events with the actor, family and connection IDs,
  including recovery after a crash between the journal append and the file write;
- serialize concurrent `set` and `clear` commands so each event's previous connection ID is the state
  it replaced, and fail a command that cannot take the lock without writing;
- let two hosts on the same configuration run together, refuse `set`, `clear`, `plugin-allow` and
  `plugin-revoke` while either is running, and refuse to start a host while a command holds the
  lock;
- keep two configurations in the same directory on separate host files, journals and locks;
- refuse credential-bearing families when the state file exists but its journal is missing, until
  `recover`;
- leave another configuration's claims intact when one configuration runs `recover` on a shared
  `DataRoot`;
- crash between the claim file and the state file in both directions (`set` and `clear`), and show
  that the scope is never left unfrozen while a committed binding names it, and that startup
  repairs the claim;
- refuse a `DataRoot` change while bindings exist, and refuse bound families when the configured
  vault differs from the one recorded at binding time;
- refuse writes of provider credential configuration keys through `ConfigStore`, including
  `ConfigEndpoints.ConfigAlpaca` and `ProviderEndpoints.ConfigDataSources`, and refuse
  `ImportLegacyAsync` and `ImportOAuthTokensAsync` for market-data families; the reflection test
  covers every unscoped write method;
- audit plugin credential-free changes with the actor, plugin ID, and previous and new classification;
- serialize a `set` with a concurrent save or rotation of the same scope: if `set` takes the vault lock
  first, the rotation waiting behind it is then refused because the scope is bound; if the rotation
  takes it first, `set` sees the new, unverified generation and refuses to commit;
- serialize a `set` with a concurrent configuration write that deletes, disables or changes the
  connection, so the committed binding matches the connection that was validated;
- keep two configurations that share a `DataRoot` from recovering each other's transactions;
- refuse credential-bearing families when the state file is deleted after a committed change, and
  when the journal is malformed, truncated or unreadable, until `recover`;
- refuse to save, rotate or delete a bound connection's credential, and to disable or delete a bound
  connection, from any running host and from another configuration that shares the `DataRoot`; then
  rotate it through `clear`, rotation and verification, and `set`;
- start a host and a mutating command at the same time, and show that they serialize without
  deadlock: whichever takes the host lock first proceeds, and the other waits within its timeout
  and then either proceeds or is refused cleanly, never both holding it;
- start a host successfully after `recover`, for a lost state file, a malformed state file, a mismatched
  digest and an invalid journal;
- start two hosts simultaneously after a crash left a `pending` entry, and show that exactly one
  outcome is recorded and both hosts start;
- treat a pending event as changed outside the command (refusing families and mutation until
  `recover`) when only an unrelated entry of the host credential state was changed;
- after an outcome append fails without writing any bytes, have the next mutating command resolve
  the pending event first and refuse to proceed until it is resolved; when the file matches neither
  side of the event, refuse credential-bearing families and mutation until `recover` records the
  observed state;
- after an outcome append is torn, leaving a partial record, follow the invalid-journal path:
  refuse families and mutation until `recover` moves the journal aside and records a baseline;
- treat aliases of one configuration file (relative, `..`, symlinked, different case on a
  case-insensitive file system) as one identity for the sidecars, the journal and the claim;
- refuse to treat missing sidecars as a fresh installation while this configuration's vault claim is
  non-empty;
- refuse every scoped mutation on a vault while any claim file is malformed, truncated or unreadable,
  and crash during each claim rewrite path (`set`, `clear`, startup, recovery);
- rebuild claims from the stored scopes when the configuration cannot be loaded, and keep the existing
  claim when the committed state cannot be read;
- move a malformed state file aside before `recover` adopts an empty state, and record it in the
  baseline;
- run a binding change, or a startup or pre-command recovery, alongside concurrent configuration
  writes and scoped credential saves, rotations or verifications, with no lost write and no deadlock;
- record `committed` when the write throws after the rename has published the new file;
- refuse a lazily constructed provider when a verification failure was recorded for its bound record
  after startup;
- show only the source kind in tenant-visible read models, never the connection ID or tenant.

## Implementation Plan

1. This ADR (Proposed), for review.
2. Host credential state and command: strict loader for bindings and the plugin credential-free
   list, and their strictly parsed journal; `--host-credential-binding` with the host lock (shared for running hosts, exclusive for commands),
   the host, configuration, vault lock order, recover-first transactions, file-checked outcomes and
   `recover`; the host credential tenant setting.
3. Classification: a credential classification for every family, with a consistency check against
   the credential catalog and adapter metadata (adding the missing NYSE entry).
4. Construction: validate and verify bindings at startup; the per-family host credential selection
   at every construction site (including OpenFIGI's and the Polygon corporate-action fetcher), with
   module overlays and post-resolver fallbacks removed and the architecture scan in place; `FailClosed`
   refusals (unbound, session-backed and unclassified families, plugins, provider-wide writes at the
   credential store boundary); no path to an external brokerage account under `FailClosed`, with
   paper execution kept; revalidation at every resolution; the tests above.
5. Surfacing: source kind in the browser and WPF provider read models; identifiers in the host log and
   CLI only.
6. Update `PRD-002` evidence in the implementation tracker and the Application README.

## References

- [PR #2931: scoped provider credential ownership](https://github.com/rodoHasArrived/Meridian-main/pull/2931)
- [Implementation and Readiness Tracker, `PRD-002`](../product/implementation-todo-list.md)
- [Application README: scoped credential resolution](../../src/Meridian.Application/README.md)
- [Fund-structure tenant backfill runbook](../operators/fund-structure-tenant-backfill.md) (out-of-band host command precedent)
