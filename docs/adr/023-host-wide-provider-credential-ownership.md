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
   - **One posture per configuration.** Hosts sharing a configuration share its output, so they must
     agree on the posture. The committed host state records the posture, which only the offline
     `posture DeploymentBoundary|FailClosed` command changes. A host whose
     `MERIDIAN_TENANT_SCOPE_ENFORCEMENT` differs from the committed posture refuses to start at all,
     before any provider is built. A `FailClosed` host also refuses to start until committed host
     state exists, so moving to `FailClosed` always begins with `posture FailClosed`, which creates
     the identity if needed. A `DeploymentBoundary` host with no committed state is unconstrained,
     as today.
   - **`DeploymentBoundary`:** the deployment is one company, so an unbound credential-bearing family
     keeps using the provider-wide record, as today. Bindings are optional.
   - **`FailClosed`:**
     - an unbound credential-bearing, session-backed or unclassified family does not start. For
       OpenFIGI, this means the canonical symbol resolver runs registry-only and `BackfillCoordinator`
       creates no fallback resolver;
     - provider-wide credentials for any host-wide market-data family (built-in or plugin) can be
       neither read nor written. This is enforced at the credential store boundary itself:
       - the unscoped reads (`ReadForProviderAsync`, `ReadOAuthTokensAsync`) return nothing for
         those families, and `GetStatusAsync` reports them as not configured, with no key preview,
         field list, environment or verification metadata;
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
     - the configuration writer boundary (`ConfigStore`) compares the current and proposed
       documents and refuses any provider credential configuration key whose value is newly set or
       changed to a nonblank value. It uses the same key list as the architecture scan in point 7.
       Unchanged values left over from before the move to `FailClosed`, and their removal, are
       allowed, so unrelated configuration saves keep working. Routes that write credentials into
       `AppConfig`, such as `ConfigEndpoints.ConfigAlpaca` and `ProviderEndpoints.ConfigDataSources`,
       therefore cannot install provider-wide credentials either;
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
   configurations in one directory never share them. The configuration identity used for journal
   entries and for the vault bound marks (point 6) is not a path. It is a random identifier,
   created only by the first mutating command (which holds the host lock exclusively, so two
   creations cannot race) and stored in the host state file and in every journal entry. A host
   with no state yet has no identity and takes no identity lock. Each checkpoint (point 6) also
   records the configuration paths that hosts of that identity have used, as a hint for detecting
   lost sidecars. Hosts that reach
   the same shared configuration through different mount paths, such as `/mnt/meridian` and
   `Z:\meridian` under `SharedStorage`, therefore share one identity. The vault is identified the
   same way, by an identifier stored in the vault, not by its path. The sidecar files are located
   next to whatever path the host opened the configuration through:
   - `<config>.host-provider-credentials.json`;
   - `<config>.host-provider-credentials.journal.jsonl`;
   - `<config>.host-provider-credentials.lock`. No HTTP
   endpoint or general configuration writer touches either file, so data-source edits cannot erase a
   binding. Both are loaded strictly:
   - a missing state file and a missing journal together mean a fresh installation with no bindings
     only when the vault holds no checkpoint that some configuration has left unclaimed. A path
     match is not required, because shared configurations are opened through different mount paths.
     If the vault holds any unclaimed checkpoint, the host refuses credential-bearing families
     until the operator either adopts an identity with `recover --identity` or confirms a fresh start
     with `recover --fresh`. Any other combination is also a partial loss:
     - a missing state file with a journal that records a committed state;
     - a state file with a missing journal.

     Either way, credential-bearing families refuse to start until `recover`. Clearing bindings is
     an explicit, journalled `clear`, never a deleted file;
   - a malformed or unreadable state file, or one whose digest differs from the journal's last
     committed digest, makes the host credential state untrusted (below);
   - defaults are never substituted.

   **Untrusted state.** Every integrity failure in this document puts the host credential state in
   one condition, untrusted. That covers a malformed or missing piece, a digest mismatch, a rollback
   (point 6), an invalid journal, or an unresolved pending entry. While the state is untrusted,
   under both postures, until `recover` runs:
   - every credential-bearing host-wide family refuses to start;
   - every plugin credential-free entry is ignored, so no listed plugin starts under `FailClosed`;
   - the configuration writer refuses the guarded changes (a bound connection's disable or delete,
     and any `DataRoot` change), treating every connection as possibly bound;
   - the committed posture is unknown, so the host applies the full `FailClosed` composition
     whatever `MERIDIAN_TENANT_SCOPE_ENFORCEMENT` says: no live execution, no brokerage gateway,
     sync adapter or brokerage connection route (point 1), and no provider-wide credential. The same
     applies to a partial loss, which also hides the committed posture.

   This one rule replaces case-by-case handling, so a new integrity failure cannot leave one of
   these paths open.

5. **Only the host operator changes bindings, out of band.** Bindings and the plugin credential-free
   list change only through a host CLI command, `--host-credential-binding
   list|set|clear|plugin-allow|plugin-revoke|posture|recover|repair-vault|reset|upgrade`, run on the host by whoever operates the
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
   - Several configurations can share one `DataRoot`, and so one vault. So the freeze is recorded on
     the scoped credential record itself, inside the vault: each record carries a set of bound marks,
     one per configuration that has bound it, keyed by that configuration's identity. The store
     refuses to mutate a record while its set is non-empty, so a configuration that holds the same
     connection unbound cannot change a scope that another configuration has bound. Because the
     mark lives in the same atomically written vault file as the secret, it cannot be deleted, lost
     or misnamed on its own: removing it means removing the record, and the vault's own integrity
     rules already cover a damaged vault.
   - Marks are derived from the scopes stored in the committed host state, never from configuration,
     and are always written under the vault lock in the safe order. A `set` adds its mark before
     publishing the state file, and a `clear` removes it only after publishing it. A crash between
     the two can therefore only leave an extra mark, which freezes more, never less.
   - **Checkpoint.** Every committed change, including a plugin-list change, increments the state's
     generation. The vault also keeps one checkpoint per configuration identity, recording the last
     committed generation and digest; it is updated under the vault lock after the state file is
     published. The state is untrusted (point 4) when either holds:
     - its generation is older than its checkpoint's, meaning it was rolled back;
     - its generation equals the checkpoint's but its digest differs, meaning a divergent copy was
       restored.

     A state one generation ahead of its checkpoint is repaired forward only when the journal's last
     entry is that generation's unresolved `pending` entry, which proves the checkpoint write was
     interrupted. A `committed` outcome never authorizes it: every path that records `committed`
     (step 5, the file-checked outcome and recovery's `committed (recovered)`) first writes the
     checkpoint under the vault lock, so a committed generation with a lagging checkpoint can only
     mean a rolled-back vault. Otherwise the vault itself was rolled back, for example restored from an older
     copy that still parses, and the state is untrusted. A missing checkpoint for an identity that
     has committed state counts as generation 0, so a vault that predates the identity is caught by
     the same rule. Rollback detection therefore covers every change, and covers the vault as well
     as the sidecars.
   - **Who removes marks.** Each mark records the generation that added it. Only a mutating command
     or `recover` removes marks, while holding the identity lock (point 8) exclusively.
     - Such a command removes a mark that no committed binding names. When recovery closes a pending
       entry as `aborted (recovered)`, it also removes the mark carrying that entry's generation.
     - The first host to start takes the identity lock exclusively, runs recovery (including any
       mark cleanup) and validation, then moves to shared mode. If the platform cannot downgrade a
       lock atomically, the host takes the shared lock and then repeats validation against the
       checkpoint. If anything changed in the gap, it starts again from recovery, so a command
       queued in between can never leave it running on stale validation. A host
       that joins hosts already running takes it shared and only adds missing marks. Nothing can
       be left to clean up while hosts run, because no command can run then.
     - If the committed state cannot be read, the existing marks are left as they are.
   - **Vault backup.** The store's fallback to its rolling vault backup could restore an older
     generation of marks, checkpoints and verification state. So when the store loads from the
     backup, it first writes a durable quarantine marker beside the vault. While the marker exists:
     - every bound mark, checkpoint and verification status is unknown;
     - every vault mutation is refused, scoped or unscoped, including verification updates;
     - every vault read of a credential is refused, scoped or unscoped, so no family, bound or
       unbound and under either posture, starts on a vault credential, and every resolution is
       refused. Only a provider-wide credential supplied directly by the environment or the
       configuration, outside the vault, is unaffected under `DeploymentBoundary`.

     Only an explicit repair (`--host-credential-binding repair-vault`, run offline) clears the
     marker. It reconciles every configuration identity recorded in the vault, and sets every
     scoped record's verification to unverified, because the backup's `Verified` values may be
     stale. A bound family starts again only after its credential is verified anew. It also flags
     every provider-wide record as restored, because the backup may hold a secret that was since
     rotated. A flagged record is not used until the operator saves it again or verifies it, which
     clears the flag.
     - Because repair touches every identity, not only its own, it is stop-the-world for the
       `DataRoot`. It first takes the `DataRoot` lock exclusively (point 8), which no running host
       of any configuration, with or without an identity, can be holding. A backup can also predate
       an identity, so the vault alone does not list every identity in use. Repair therefore enumerates identities from both the vault and the identity lock files
       beside it, which are never removed and which every running host holds. If that directory
       cannot be listed, it refuses. Before reconciling, it takes every one of those identity locks
       exclusively, without waiting and in identifier order. If any of them is held, by a
       running host or a command of any configuration, it refuses and changes nothing. It keeps all
       of them until it finishes, so no host can start against an identity it is changing.
   - **Vault revision.** Checkpoints only move when host state commits, but the vault also changes
     on its own, for example when `RecordScopedVerificationAsync` records a verification failure. So
     the vault carries a revision that every write increments, and the store's audit log
     (`provider-credentials.audit.jsonl`, appended after each vault write today) records the
     revision each write produced and a digest of the vault contents it wrote. Each write is
     bracketed in the log, like the host journal: before publishing the vault, the writer durably
     appends an `intent` entry carrying the action, the actor, the new revision and the new digest,
     and after publishing it appends the matching `done` entry. When the store loads the vault, it
     compares the vault's revision and digest with the last audit entry:
     - equal revision and equal digest is consistent;
     - equal revision with a different digest means a divergent copy of the vault was restored, and
       is a mismatch like the ones below;
     - the vault exactly one ahead may be a writer still between its vault write and its audit
       append, because writers hold `provider-credentials.vault.lock` across both and today's
       readers take no lock;
     - the vault behind the audit log means a valid older vault was restored; the vault ahead by
       more than one means the audit log was rolled back.

     A reader never acts on a mismatch itself. It takes the vault lock and rechecks, and only the
     result under the lock counts. Under the lock no writer is mid-write. A vault one ahead is then
     repaired forward only when the last audit entry is an unfinished `intent` naming exactly that
     revision and digest, which proves a crash after publishing; the store appends the `done` entry
     from the intent's own payload, marked as recovered, so the original action and actor survive.
     An unfinished intent whose revision the vault never reached is closed as aborted. A vault one
     ahead with no matching intent means the audit log lost an entry, and is a mismatch. Every other
     mismatch is handled exactly like a backup fallback: the store writes the durable quarantine
     marker, and only `repair-vault` clears it. A restore of the vault alone that keeps the
     checkpoint generation but reverts a verification result is therefore caught too.
   - **Vault format.** Bound marks, checkpoints, the vault revision and the vault identifier change
     the vault format. So
     the vault takes a new envelope version that older binaries reject rather than ignore and
     rewrite, and the primary and backup are upgraded together. The upgrade is stop-the-world: an
     offline `upgrade` command, run with every host on the `DataRoot` stopped, converts the vault
     and writes a format marker beside it. Hosts and commands of this version refuse to create host
     state until the marker exists.
     - The upgrade changes three files, so it is journalled like every other change. Before
       touching anything, `upgrade` durably appends a `pending` entry to
       `provider-credentials.upgrade.jsonl` beside the vault, recording the from and to versions and
       the digests of the vault and backup before conversion. It then converts the backup, converts
       the primary and writes the marker, each atomically and each idempotent, and finally appends
       `committed`.
     - While that journal has an unfinished entry, the gate refuses every host and every command
       except `upgrade`, which resumes from the first step whose file is not yet in the target
       version. Only the resuming `upgrade` accepts the mixed versions its own journal entry
       explains; any other mix of marker and envelope versions still makes the gate refuse.
     - An older binary cannot write protocol state. Today's store already refuses a vault whose
       format version is newer than it supports (`FileProviderCredentialStore.LoadVaultFromFileAsync`
       throws `NotSupportedException`, which is not treated as corruption, so it never falls back to
       the backup or rewrites the vault), and older binaries never touch the sidecars.
     - An older binary can still run, though. `ProviderFactory.CreateProviders` catches
       provider-construction failures and continues, and credential-free providers and the
       configuration and environment construction paths never open the vault. No in-band check can
       stop an executable that predates the check.
     - So the gate ships first. The implementation's first release adds a process-level startup
       gate, run before any provider is built. It resolves `DataRoot` strictly, from the
       configuration and any environment override, and never through the lenient
       `ConfigStore.LoadConfig`. If a configuration file exists but cannot be strictly parsed, the
       gate cannot know where the vault is, so it refuses to start. It also checks the default root
       and the last known vault location recorded in the host state, when they differ. It does not
       trust the marker alone, because a marker can be lost or restored on its own. It reads the format version from the marker and from the
       unencrypted envelope headers of the vault and its backup, takes the highest, and refuses to
       start at all if that is newer than it supports. It also refuses when the marker is missing
       but either envelope is in the new format, and when the marker and the envelopes disagree.
       The protocol may be enabled with `upgrade` only once every host on the `DataRoot` runs at
       least that release, and `upgrade` records that minimum in the marker and in both envelope
       headers. From then on, every release that can run against the vault refuses a newer one,
       and losing or rolling back the marker alone changes nothing.
     - Running a binary older than the gate release against an upgraded `DataRoot` cannot be
       prevented in-band. The deployment runbook forbids it: it replaces the executables before
       `upgrade` and pins the minimum version, the same way it forbids running two releases from
       different `DataRoot` copies.
   - The host state records the identifier of the vault it was bound against and its last known
     location. `list`, `recover` and `repair-vault` accept `--vault-path` to find a vault at a custom
     location when the configuration cannot be loaded. Each checks the vault's stored identifier
     before using it whenever surviving state records one. With both sidecars lost, `list
     --vault-path` shows that vault's checkpoints, `repair-vault --vault-path` clears a quarantine
     on it, and `recover --vault-path --identity` adopts one of them.
   - The host state records the identifier of the vault it was bound against. At startup, a bound
     family whose configured `DataRoot` resolves to a vault with a different identifier refuses to
     start. While any committed host credential state exists, the configuration writer refuses to
     change `DataRoot` (for example through `ConfigEndpoints.UpdateStorage`). That covers bindings
     and plugin entries alike, since the checkpoint lives in the vault. Moving the vault therefore
     requires `reset`, an offline command that runs once no bindings or plugin entries remain:
     1. it journals the reset;
     2. it moves the closed journal aside unchanged, as the permanent audit record;
     3. it writes a retirement file beside the identity lock file
        (`provider-credentials.identity-<identifier>.retired`), durably and before anything else
        changes, recording the actor, the time and the archived journal's path. Like the lock file,
        it is never removed, and it is outside the vault, so no vault backup can predate it away;
     4. it replaces this configuration's checkpoint in the vault with a reset tombstone carrying the
        same record;
     5. it removes the state file.

     It never removes the identity lock file, because unlinking a held lock would let another
     process lock a new file at the same path. Identity lock files stay permanently. A tombstoned
     identity does not block a fresh installation, so the configuration is then fresh and
     `DataRoot` can change.

     A tombstone is terminal for its identity. A fresh installation always creates a new random
     identity, so nothing legitimate claims a tombstoned one again. Any state file or journal that
     names a tombstoned identity, such as a copied configuration's sidecars that stayed offline
     while the other copy reset, is untrusted (point 4), whatever its generation. Its host adds no
     marks and its bound families refuse to start. Only `recover --new-identity` or
     `recover --fresh` brings that configuration back, and neither revives the old bindings. `recover
     --identity` refuses a tombstoned identity. An identity counts as tombstoned when either the
     vault tombstone or the retirement file exists. `repair-vault` recreates any vault tombstone
     that a backup fallback lost from its retirement file, so a stale backup never brings a retired
     identity back.
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
     stable identity for the host configuration (the stored identifier, see point 4), and an entry
     for a different identity makes the journal invalid.
   - **Parsed strictly.** The journal is parsed strictly. A malformed, truncated or unreadable entry,
     or an unreadable journal, makes credential-bearing families refuse to start and refuses mutation
     until `recover`. No entry is ever skipped.
   - **Offline only.** Every running host holds the host lock (`<config>.host-provider-credentials.lock`)
     in shared mode for its whole lifetime. Several hosts on one configuration, such as `SharedStorage`
     coordination instances or a workstation host beside a collector, can therefore run together.
     Every mutating command needs the lock in exclusive mode, so it fails while any host on this
     configuration is running, and no host can start while a command holds it. A change takes effect
     when the hosts next start, so no running provider keeps using state that has since changed.
     The vault holds a second lock for each configuration identity, beside the vault
     (`provider-credentials.identity-<identifier>.lock`, a filesystem-safe random identifier). Every
     running host of that identity holds it in shared mode, and mutating commands and `recover` need
     it exclusively, after the host lock. A copied configuration, which keeps the original's
     identity, therefore cannot change or remove marks while any host of the original runs. Its
     state also falls behind the shared checkpoint as soon as either side commits, which makes it
     untrusted. To make an intentional copy, run `recover --new-identity` on it. That gives the copy
     a fresh identity with empty state and touches none of the original's marks.
     `list` takes the host lock in shared mode and the journal-writer lock, in the documented order,
     for the moment it reads the state file and the journal. It therefore never sees a partly
     appended journal entry.
   - **One journal writer at a time.** Every write to the journal, whether from a mutating command or
     from recovery, is made while holding a second lock exclusively, the journal-writer lock
     (`<config>.host-provider-credentials.journal.lock`). A starting host first takes the host lock in shared mode, then
     the identity lock (exclusively if it is the first host, see point 6), then the journal-writer lock, then runs recovery and validation, then releases the
     journal-writer lock. This is the same order every command uses, so a starting host and a command
     can never wait on each other:
     - hosts that start at the same moment therefore recover one at a time;
     - a host that joins hosts already running finds nothing pending, since every pending entry was
       resolved before they started and no command can run while they do;
     - if it does find an unresolved entry, the journal was changed outside the command, and it
       refuses to start.
   - **`DataRoot` lock.** Every host process, from the gate release on, takes
     `provider-credentials.hosts.lock` beside the vault in shared mode before anything else,
     including the startup gate, and holds it for its lifetime. That includes a host with no
     committed state and no identity. Every command takes it shared too. `repair-vault` and
     `upgrade` take it exclusively, without waiting, and refuse if any host or command holds it, so
     both are stop-the-world for the whole `DataRoot` and not just for the identities they can find.
     Like the identity lock files, it is never removed.
   - **Lock order.** A transaction takes the `DataRoot` lock, then the host lock, then the identity lock, then the
     journal-writer lock, then the configuration writer lock (the one
     `ConfigStore` takes, `<config>.lock`), then `provider-credentials.vault.lock`. `repair-vault`
     takes every recorded identity lock, in identifier order and without waiting, where others take
     one. Configuration and
     credential-store writers never take the host lock, and the implementation must show that no code
     path takes the configuration and vault locks in the reverse order. Because the `DataRoot` lock is
     chosen before the configuration lock is held, a command rereads `DataRoot` strictly once it holds
     the configuration lock. If it no longer resolves to the root whose locks it holds, the command
     writes nothing, releases every lock and starts again from the new root. A command that cannot take a
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
     3. append a `pending` entry to the journal and flush it durably (an fsync or equivalent, plus
        the directory entry when the journal is first created) before anything is published. It
        records the actor, the host identity, the kind of change (binding, plugin classification or posture),
        the subject (provider family, plugin ID, or the configuration for a posture change), the previous
        and new values, the new state
        generation, and canonical digests of the complete host credential state before and after the
        change. Outcome entries are flushed the same way;
     4. for `set`, add this configuration's bound mark to the new scope; write the state file
        atomically; then remove the mark from any scope the state no longer names (the cleared
        scope, or the scope a `set` replaced); then update this configuration's checkpoint. All of
        this runs under the vault lock and the exclusive identity lock already held;
     5. append the outcome, then release the locks in reverse order.
   - **Outcome from the file, not the exception.** `AtomicFileWriter` can throw after the rename has
     already published the file, for example from the directory sync. So on any write exception the
     command rereads the published file and compares the digest of the whole state:
     - if it matches the after-digest, the command writes the checkpoint, then records `committed`;
     - if it matches the before-digest, the outcome is `aborted`;
     - otherwise the entry stays `pending`, and the command exits non-zero.
   - **Recovery.** At startup and at the start of every mutating command, under the host lock and the
     journal-writer lock, each
     `pending` entry without an outcome is resolved against the state file:
     - if the digest of the whole file equals the entry's after-digest, recovery writes the
       checkpoint under the vault lock, then closes it as `committed (recovered)`;
     - if it equals the entry's before-digest, it is closed as `aborted (recovered)`;
     - otherwise the file was changed outside the command, even if only in an unrelated entry.
       Credential-bearing families then refuse to start, as for a malformed file, and mutation is
       refused, until the operator runs `recover`.

     After the entries are closed, recovery takes the vault lock and reconciles this configuration's
     bound marks with the committed state. A crash between the mark and the state file is
     therefore repaired before any provider starts or any outcome is relied on. A retry therefore never looks
     like a second change.
   - **`recover`.** `recover` handles a lost state file, an unresolvable pending entry, or a mismatched
     last committed digest. It establishes a new authoritative baseline:
     1. it adopts the observed state file if it parses. If the file is malformed, it first moves it
        aside unchanged, so the evidence is kept. When the file is malformed or missing, it adopts an
        empty state with no bindings and no plugin entries, and writes that file atomically. That
        state takes the last committed posture the surviving journal records. When no valid journal
        records one, it takes `FailClosed`, never the environment's value, and the `baseline` entry
        records that the posture was defaulted. Moving back to `DeploymentBoundary` is then a
        separate, journalled `posture` transaction;
     2. it reconciles only this configuration's bound marks with the adopted state, under the vault
        lock, leaving every other configuration's marks untouched. It takes this configuration's
        identity from the surviving state file or journal. If both are lost, the operator passes it
        with `--identity`, choosing from the checkpoints that `list` enumerates. For every
        identity, `list` shows its checkpoint generation, its path hints, and any bound marks,
        including identities that have a checkpoint but no mark. Until then,
        orphaned marks keep freezing their scopes, which fails safe;
     3. it appends an `indeterminate` outcome for any unresolved entry, followed by a `baseline` entry.
        It also sets this configuration's checkpoint to the adopted state's generation and digest.
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

11. **Integrity invariants are normative; the mechanism above is a reference design.** Points 4, 6
    and 8 describe one mechanism: files, locks, marks, checkpoints and a journal. The decision is the
    following invariants, which the implementation must satisfy and prove with the tests below. It
    may refine the mechanism, and wherever the mechanism and an invariant disagree, the invariant
    wins.
    1. **No unfrozen bound credential.** While any host, of any configuration or version, may hold a
       bound credential, that credential's record cannot be saved, rotated or deleted.
    2. **No silent rollback.** Any rollback or divergent restore of the state file, the journal,
       the vault or the store's audit log, relative to the others, is detected and makes the state
       untrusted or quarantines the vault. That includes a vault change that moves no checkpoint,
       such as a verification result. It is never repaired forward unless an unresolved `pending`
       entry proves an interrupted write. Files restored together to the same older point leave no
       evidence between themselves. That applies to the whole set, and also to the vault and its
       audit log restored together, which can revert a verification result without moving any
       checkpoint. Any restore of files in the vault directory or of the sidecars, alone or
       together, is therefore an operator action governed by the restore runbook. After any such
       restore the operator must run `repair-vault`, which resets every verification to unverified
       (invariant 4), before any bound family can start. A deployment that needs automatic detection can add a monotonic
       anchor outside the restorable set; that is out of scope here.
    3. **No silent fresh start.** Losing host credential state never yields a fresh installation
       while the vault holds evidence of earlier committed state, unless the operator explicitly
       confirms it.
    4. **No stale verification.** After any vault repair or fallback, a bound family starts only
       once its credential has been verified anew.
    5. **No gap between validation and running.** A host never runs on validation that a command
       could have invalidated between the check and the start.
    6. **Every state is reachable and removable offline.** The operator can recover from any
       integrity failure and remove all host credential state without deleting files by hand.
    7. **One version at a time.** A binary of a different protocol version never writes the vault
       or the sidecars, and every release from the gate release on refuses to start against a
       newer protocol. Running a binary older than the gate release is excluded by the deployment
       runbook, not in-band.

    A review finding about the mechanism is resolved by showing which invariant it violates, and
    the implementation PR must add a test for that case.

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
| Credential verification and vault lock | `src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs` | Per-generation verification state; `provider-credentials.vault.lock`, held through binding validation and commit; stores each record's bound marks and refuses a scoped write while any are present; `ImportLegacyAsync` and `ImportOAuthTokensAsync` guarded |
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
- A deployment moving to `FailClosed` must run `posture FailClosed`, bind every bindable
  credential-bearing family (with verified credentials) and list any credential-free plugins first,
  or those families will not start. Every host of the configuration must then start with the same
  posture.
- From the gate release on, a host whose configuration file exists but cannot be strictly parsed
  refuses to start, instead of falling back to defaults.
- After a vault fallback, restored provider-wide records must be saved again or verified before use,
  under both postures.
- Plugin families cannot be bound, and families without scoped verification cannot be bound, until
  that support exists.
- Under `FailClosed`, session-backed families such as `ibkr` do not run, live execution and every
  other path to an external brokerage account stay off until the execution-ownership decision exists
  (paper execution stays), and OpenFIGI enrichment stays off until OpenFIGI is bound.
- Upgrading to this protocol is stop-the-world: every host on a `DataRoot` stops while `upgrade`
  converts the vault. It takes two releases: the startup gate ships first, and `upgrade` runs only
  once every host runs at least that release. Binaries older than the gate release are kept off
  the `DataRoot` by the deployment runbook, because nothing in-band can stop them.
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

- The integrity invariants in decision point 11 bind the implementation. Each has at least one
  failing-first test below.
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
- treat a missing state file as no bindings only when the journal is also missing and the vault holds
  no unclaimed checkpoint, including one with no bound mark left after a plugin-only change or the
  last `clear`; treat each other combination as a partial loss, and a malformed or unreadable file
  as untrusted state, under both postures;
- while the state is untrusted, refuse credential-bearing families, ignore plugin credential-free
  entries (including when a digest mismatch comes only from a plugin-list edit), and refuse a
  bound-connection disable or delete and any `DataRoot` change;
- restore an older but internally consistent state file and journal, after a binding change and
  after a plugin-only change, and show the checkpoint marks the state as untrusted and no mark is
  removed;
- crash a `set` after its mark is written but before the state file is published, and show recovery
  closes it as aborted and removes that mark;
- crash a `set` after its mark and a `clear` before removing its mark, and show the first host to
  start removes the surplus mark under the exclusive identity lock;
- start two hosts for the first time together and show only a mutating command creates the identity;
- refuse a `DataRoot` change when only plugin entries are committed;
- reject the new vault format from an older binary, rather than rewriting it without marks;
- restore a divergent state at the same generation and show the digest mismatch makes it untrusted;
- after a backup fallback, refuse every vault mutation, including unscoped and verification writes,
  across a restart, until `repair-vault`;
- lose both sidecars after a plugin-only change and show the path hint makes it a partial loss;
- replace a binding with `set` and show the previous scope's mark is removed;
- lose both sidecars and open the configuration through a different mount path, and show the
  unclaimed checkpoint blocks a fresh start until `recover --identity` or `recover --fresh`;
- restore an older vault that still parses while the state is one generation ahead with no matching
  journal entry, and show the state is untrusted rather than repaired forward;
- after `repair-vault`, refuse a bound family until its credential is verified again;
- queue a copy's `clear` behind the first host's exclusive identity lock and show the host
  re-validates instead of running on stale validation;
- run `reset` once no bindings or plugin entries remain, then change `DataRoot`; show the archived
  journal, the vault tombstone and the retirement file remain, and the identity lock file is kept;
- copy a configuration with its sidecars, clear and `reset` one copy, then start the other copy;
  show its state is untrusted because the identity is tombstoned, it recreates no mark, its bound
  family refuses to start, and only `recover --new-identity` or `recover --fresh` brings it back;
- run `repair-vault` while a host of another configuration on the same `DataRoot` is running, and
  show it refuses without changing any mark, checkpoint or verification status;
- run `repair-vault` and `upgrade` while a `DeploymentBoundary` host with no committed state and
  no identity runs on the same `DataRoot`, and show both refuse because of the `DataRoot` lock;
- corrupt the state file after `posture FailClosed`, start a host with
  `MERIDIAN_TENANT_SCOPE_ENFORCEMENT=DeploymentBoundary` and live execution configured, and show it
  composes no brokerage gateway, sync adapter or brokerage connection route and reads no
  provider-wide credential until `recover`;
- crash `posture` after publishing the state file and before the checkpoint update, and show
  recovery writes the checkpoint before closing it as `committed (recovered)`; restore an older
  state with the other posture, and show the checkpoint makes it untrusted;
- fall back to a backup that predates an identity's first checkpoint while a host of that identity
  runs, and show `repair-vault` finds the identity from its lock file and refuses; once the host
  stops and repair completes, show that identity's state is untrusted because its checkpoint is
  missing;
- lose both sidecars after a plugin-only change and show `list` enumerates the surviving checkpoint
  identity for `recover --identity`;
- restore the state file, journal and vault together to an older point, and show `repair-vault`
  is required and leaves every bound family unverified until verified anew;
- refuse to create host state before `upgrade`, and show an older binary refuses to open or rewrite an
  upgraded vault;
- start a gate-release host against a `DataRoot` whose format marker is newer than it supports,
  and show the process refuses to start before any provider, including credential-free ones, is
  built;
- refuse `upgrade` until the marker can record a gate-release minimum, and show a host below that
  minimum refuses to start afterwards;
- restore a valid older primary vault after a recorded verification failure, with the checkpoint
  generation unchanged, and show the vault revision behind the audit log quarantines the vault and
  the bound family refuses to start until `repair-vault` and a new verification;
- crash between a vault write and its audit append, and show the store completes the append from
  the intent, keeping the original action and actor, instead of quarantining;
- remove the latest revision's `intent` and `done` entries from the end of the audit log offline,
  and show the vault one ahead with no matching intent is quarantined rather than repaired forward;
- remove only the latest `done` entry offline, and show the store completes it from the matching
  unfinished intent, as for a crash after publishing;
- read the vault while a writer is between its vault write and its audit append, and show the
  reader neither completes the append nor quarantines, and the audit log gets exactly one entry
  for that revision;
- after `upgrade`, delete the format marker, and separately restore it to the gate release's
  value, and show a gate-release host still refuses to start because the vault envelope is newer;
- `reset` an identity, then fall back to a vault backup taken before the reset; show
  `repair-vault` recreates the tombstone from the retirement file and a stale copy's `recover
  --identity` is refused;
- restore the vault and its audit log together to before a recorded verification failure, and show
  the restore runbook's required `repair-vault` leaves the bound family unverified;
- write the state file and then crash before the checkpoint update, and show recovery writes the
  checkpoint before closing the entry as `committed (recovered)`; then restore an older vault
  whose checkpoint lags a `committed` entry, and show the state is untrusted rather than repaired
  forward;
- recover with `--vault-path` when the configuration cannot be loaded and the vault is at a custom
  location;
- quarantine a custom-location vault while the configuration is unreadable and both sidecars are
  lost, and show `repair-vault --vault-path` clears it after checking the vault's identifier;
- crash `upgrade` after each publication boundary (backup, primary, marker), and show every host and
  other command refuses while `upgrade` resumes and completes from its journal;
- change `DataRoot` while a first `set`, `posture` or plugin command waits for the configuration
  lock, and show the command writes nothing against the old root and restarts against the new one;
- lose both sidecars after a committed `FailClosed` posture, run `recover --fresh` with the
  environment set to `DeploymentBoundary`, and show the baseline takes `FailClosed` and records the
  default;
- lose both sidecars with an unreadable configuration and a custom `DataRoot`, and show
  `list --vault-path` enumerates the checkpoints and `recover --vault-path --identity` adopts one;
- start a gate-release host whose configuration file cannot be strictly parsed and whose `DataRoot`
  is custom, and show it refuses to start rather than checking the default root only;
- restore a divergent copy of the vault at the same revision, and show the digest mismatch with the
  last audit entry quarantines it;
- start one host of a configuration under `DeploymentBoundary` while its committed posture is
  `FailClosed`, and the reverse, and show each refuses to start before any provider is built; show
  a `FailClosed` host with no committed state refuses to start until `posture FailClosed`;
- under `DeploymentBoundary`, fall back to a backup after an unscoped rotation; show no family reads
  a vault credential during quarantine, and after `repair-vault` the restored provider-wide record is
  not used until it is saved again or verified;
- copy a configuration with its sidecars onto the same `DataRoot`, and show the copy cannot remove
  the original's marks while the original runs, becomes untrusted once either side commits, and gets
  a fresh identity from `recover --new-identity`;
- corrupt the primary vault immediately after a `set`, and after a recorded verification failure, so
  the store loads the backup; refuse every scoped mutation and every bound startup and resolution
  until repair;
- crash after the `pending` append is acknowledged but before it is durable, and show nothing was
  published;
- run two hosts that reach one shared configuration through different mount paths, and show they
  share one identity, journal and vault identifier;
- report provider-wide market-data records as not configured from `GetStatusAsync`, the
  `CredentialEndpoints` status routes and `ProviderConnectionLifecycleService.GetConnectionsAsync`;
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
- leave another configuration's bound marks intact when one configuration runs `recover` on a shared
  `DataRoot`;
- crash between the bound mark and the state file in both directions (`set` and `clear`), and show
  that the scope is never left unfrozen while a committed binding names it, and that startup
  repairs the mark;
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
- refuse to treat missing sidecars as a fresh installation while the vault carries a bound mark for
  this configuration;
- crash during each mark reconciliation path (`set`, `clear`, startup, recovery), and show that a
  bound record is never left without its mark while a committed binding names it;
- reconcile marks from the stored scopes when the configuration cannot be loaded, and keep the
  existing marks when the committed state cannot be read;
- read with `list` while an append is deliberately paused, and show it waits rather than reporting a
  malformed journal;
- save an unrelated configuration change on a `FailClosed` host that still has legacy credential
  values in `AppConfig`, and refuse a newly set or changed credential value;
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
2. Startup gate, shipped on its own first: a host that finds a protocol format marker newer than it
   supports refuses to start, before any provider is built.
3. Host credential state and command: strict loader for bindings and the plugin credential-free
   list, and their strictly parsed journal; `--host-credential-binding` with the host lock (shared for running hosts, exclusive for commands),
   the host, configuration, vault lock order, recover-first transactions, file-checked outcomes and
   `recover`; the host credential tenant setting.
4. Classification: a credential classification for every family, with a consistency check against
   the credential catalog and adapter metadata (adding the missing NYSE entry).
5. Construction: validate and verify bindings at startup; the per-family host credential selection
   at every construction site (including OpenFIGI's and the Polygon corporate-action fetcher), with
   module overlays and post-resolver fallbacks removed and the architecture scan in place; `FailClosed`
   refusals (unbound, session-backed and unclassified families, plugins, provider-wide writes at the
   credential store boundary); no path to an external brokerage account under `FailClosed`, with
   paper execution kept; revalidation at every resolution; the tests above.
6. Surfacing: source kind in the browser and WPF provider read models; identifiers in the host log and
   CLI only.
7. Update `PRD-002` evidence in the implementation tracker and the Application README.

## References

- [PR #2931: scoped provider credential ownership](https://github.com/rodoHasArrived/Meridian-main/pull/2931)
- [Implementation and Readiness Tracker, `PRD-002`](../product/implementation-todo-list.md)
- [Application README: scoped credential resolution](../../src/Meridian.Application/README.md)
- [Fund-structure tenant backfill runbook](../operators/fund-structure-tenant-backfill.md) (out-of-band host command precedent)
