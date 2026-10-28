# Credential ownership — bounded implementation and remaining P0

**Status:** local implementation; PRD-002 remains open
**Source baseline:** `a5b2589f0395d054bec0b59672a990419ef237ed`

The October 27 determination is historical. At this baseline, browser Settings and the desktop
add-provider wizard already use explicit retained owned connections. This change addresses
additional paths found during current-source inspection:

- Legacy provider setup treats a retained credential environment as scoped intent even after
  tenant/account fields are lost. It refuses rather than falling through to provider-wide status/save.
  Three partial-loss regression cases assert no vault calls and byte-identical configuration.
- Desktop initial setup and Backfill redirect credential work to the existing owned credential
  editor. They no longer read/write user environment secrets. Existing legacy values are preserved.
- Desktop credential management retains the complete selected ownership tuple, rechecks inventory
  before save/delete/verification, clears secret editors on selection changes and refuses a foreign
  provider status schema. This is a client preflight; it does not establish an atomic server-side
  expected-scope fence across deletion/recreation of the connection between requests.
- Scoped vault verification requires a nonblank expected credential generation. Delayed success
  and failure without that generation refuse before storage/audit mutation. Exact generation
  comparison remains under the vault writer lock. Provider-wide compatibility behavior is unchanged.

`ScopedCredentialRecoveryTests` adds real file-store proof that legacy import/verification does not
create or verify an owned record; conditionally rotated replacements survive primary corruption;
stale rotation/verification cannot overwrite or verify replacements; scoped deletion sanitizes
recovery while preserving another owner and the unassigned record. This is recovery/isolation
proof, not an explicit legacy ownership-transfer or host-binding migration implementation.

## P0 criteria still unimplemented

[ADR-023](../../../adr/023-host-wide-provider-credential-ownership.md) remains Proposed. There is no
host-only binding authority, identity/state/journal/checkpoint protocol or supported offline
migration/rollback/repair command. The default runtime registers the provider-wide resolver in
`ProviderFeatureRegistration` and global OAuth refresh in `CredentialFeatureRegistration`.
Direct OpenFIGI construction in `SymbolManagementFeatureRegistration` / `BackfillCoordinator`
and Polygon's hosted fetcher in `StorageFeatureRegistration` also need the same authority.
Connection ownership creation/migration and retained verification transfer must not be inferred
from the first/only/most recent account. Plugin/session/execution and configuration/store guards
must be applied together; a resolver-only guard would leave bypasses.

These remaining paths prevent end-to-end PRD-002 closure and release-blocker removal. Existing
scoped vault protection tags support fail-closed older-binary behavior; restoring an arbitrary
old vault/state copy is not a safe substitute for the proposed rollback protocol. No release,
roadmap, operator-acceptance or signing status is promoted by these bounded fixes.

## Validation

Coordinator-owned combined .NET run and full local gate are recorded in the task receipt when
finished. [Workstation evidence](credential-ui.md) separately records browser component checks,
Windows cross-compilation and the lack of live Windows execution. [Operator target recommendation](operator-target.md)
provides the next concrete environment choice. Publication requires an explicit user request
under the applied Meridian UI skill; this work is retained locally.
