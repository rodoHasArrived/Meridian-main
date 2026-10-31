---
title: Provider Credential Operations
status: active
owner: core-team
reviewed: 2026-06-02
audience: operators
---

# Provider Credential Operations

This canonical operator guide covers credential entry, rotation, verification, and repair behavior for supported providers.

## Scope

This page is for operator procedures only:

- Where to set provider credentials.
- How to validate that credentials are being picked up by the runtime.
- What to do when credentials are invalid, missing, or mis-scoped.
- When to escalate to secret-management or platform support.

Lookup surfaces (what credentials exist, names, and binding paths) are maintained in:

- [Environment Variables](../reference/environment-variables.md)
- [Provider Validation Evidence Schema](../reference/provider-validation-evidence-schema.md)
- [Provider Validation Matrix](../reference/provider-validation-matrix.md)
- [Provider Integration Status](../reference/provider-integration-status.md)

## Credential Surfaces

Catalog-managed providers use Meridian's encrypted credential store. A stored/scoped record owns
its complete credential set; missing fields are not silently filled from another account's
environment or configuration. Environment fallback is a Development/Test or explicitly enabled
migration path and is disabled by default for packaged/customer builds. See
[environment controls](../reference/environment-variables.md).

Desktop initial setup and Backfill open **Manage owned credentials** in Settings. Select an existing
connection explicitly before saving, rotating, verifying or removing credentials. These entry points
do not read, replace or delete legacy user-environment secrets. The editor rechecks the retained
provider, connection, tenant, external account and environment before each command; changed ownership
requires reloading the connection list. Workstation configuration and credential changes do not
activate a host runtime binding.


## Prerequisites and execution context

- Use the Windows workstation's Settings/provider-connection surface while signed in as an operator
  with `ManageCredentials` and the intended company/tenant. Select the existing connection before
  changing secrets; provider-wide and connection-scoped credentials are different records.
  The effective-configuration check below additionally needs `ViewConfig` or `ModifyConfig`.
- Complete [preflight](preflight-checklist.md) for persistence, startup, and authentication. Source
  commands use PowerShell 7 from the repository root; installed hosts use their lifecycle supervisor.
- Obtain replacement credentials and account/environment identity from the provider through the
  approved secret-management process. Keep the old credential available only as provider policy
  permits until verification establishes which account the replacement reaches.

### Common credential patterns

- Use the provider vault for installed deployments; use the documented provider-specific variables
  only when the development or migration fallback is intended.
- Avoid storing secrets in repository files, logs, or user shell history.
- For IBKR simulation builds, no credentials are required: a build without the `IBAPI` vendor
  SDK routes `IBMarketDataClient` to its bundled simulator. Verify with local replay paths.
  Live TWS/Gateway connectivity is configured under `IB`/`IBClientPortal`, not through any
  connector surface in config.
- For IB Flex statement fetches, store `Token` and `QueryId` under the `ib-flex` provider id. These
  credentials are separate from TWS/Gateway socket configuration and must remain in the credential
  vault.
- For Plaid, configure `PLAID_ENV`, `PLAID_CLIENT_ID`, and `PLAID_SECRET`, but keep access tokens
  and item secrets in the Meridian credential store only. Plaid access tokens must not be written
  to user environment variables, docs, support bundles, or logs.

See concrete variable names and binding keys in [Environment Variables](../reference/environment-variables.md).

## Canonical credential mutation routes

### Explicit connection ownership and host selection

Ownership administration requires `ManageCredentials`, `ManageProviders`, and `ModifyConfig`, an
authenticated operator, and a server-resolved tenant. Requests cannot supply their tenant or actor.
These shared commands are available to either workstation; Settings credential editing still selects
an already-owned connection.

1. `POST /api/provider-routing/connections` with `{ "connection": { "connectionId": "paper-a",
   "providerFamilyId": "alpaca", "displayName": "Paper account A", "externalAccountId": "reviewed-account-id" },
   "environment": "paper" }`. Use the provider's actual account identity. This creates configuration,
   not a verified connection; it never claims a pre-existing unassigned connection automatically.
2. Enter secrets through the existing selected-connection credential route, or explicitly migrate a
   retained provider-wide vault record below. No secret belongs in ownership requests.
3. `PUT /api/provider-routing/connections/paper-a/runtime-credentials` with `{ "providerId": "alpaca" }`
   selects that owned connection for host provider construction. Keep collection, imports and trading
   stopped while reconstructing provider instances through the lifecycle supervisor. Verify the
   selected connection's actual provider account and environment before an operator releases activity.
   Binding and vault scope identify the intended owner; neither proves which upstream account a secret
   reaches. Editing credentials does not hot-activate an already running collector.

The host revalidates the selected tenant, connection, provider, enabled state, account and environment
before reading secrets. Owned providers without an explicit selection block construction; missing
selected credentials never fall back to another connection, provider-wide record, config or environment.
Duplicate IDs, partial ownership, disabled connections and dangling defaults block. Removing a
connection leaves its host default dangling so removal cannot silently select legacy credentials.
Entirely legacy families retain their existing fallback policy.
The retained `ownershipRequiredProviders` marker survives connection deletion, including deletion
before a host default was selected. Removing ownership cannot implicitly re-enable legacy credentials.
Missing authoritative configuration blocks credential access. Restore the reviewed configuration,
including ownership markers and runtime selections, before retrying; an empty replacement config is
not a recovery procedure. A dangling runtime selection also blocks legacy setup and credential commands.

Default QuickBooks, Xero and NetSuite accounting import adapters are legacy-only. Once their provider
family requires ownership, they report a disabled runtime and refuse credential access, including
after rollback. Their import requests do not yet select a tenant-authorized connection. Explicit
selected-connection verification remains available, but successful verification does not enable these
default import adapters. Tenant-aware accounting import selection and its independent review remain
rollout prerequisites. Separate OAuth-token runtime cutover also requires provider-specific evidence.
The default Plaid workstation runtime is also legacy-only: owned families refuse link, item and transfer
credential access before provider calls or fallback to configured options. A tenant-authorized Plaid
item/connection runtime is required before that family can resume activity under owned credentials.
The same restriction applies to default IB Flex remote statement retrieval and provider-module
compatibility testing. IB Flex file import remains available without remote credentials. Module tests
refuse before configuring or probing a provider, and module credential status cannot borrow environment
values for an owned family. Legacy sidecar import validates every provider in its batch before any
credential is imported; ownership conflicts preserve the sidecar for operator investigation.

### Reviewed migration and rollback

Stop affected provider activity and drain in-flight requests before changing ownership. The
configuration lease protects each credential access; it cannot revoke secrets already read by a
provider client or cancel an upstream request. Obtain a non-secret preview with
`GET /api/provider-routing/connections/paper-a/credentials/migration-preview?providerId=alpaca`.
Review provider, account and environment, and retain `auditMetadata.credentialGeneration`.
Environment-only fallback is not a retained vault generation and cannot be migrated by this command.

Submit `POST /api/provider-routing/connections/paper-a/credentials/migrate` with
`{ "providerId": "alpaca", "migrationId": "your-unique-operation-id", "expectedGeneration": "reviewed-generation" }`.
The command moves the complete retained record, resets verification, and records the authenticated
actor. The destination must be empty; environment/account must match retained evidence. Separate
legacy OAuth tokens require a provider-specific transfer and block this operation; they are never
copied or silently left active. No upstream token exchange occurs here.

Transfer and retry receipt publish in one encrypted vault generation. Both recovery generations
contain the transfer; corruption recovery cannot revive the unassigned copy. The audit journal
retains deferred appends for replay, with `auditPending` status. Retry the same command and operation
identity after a lost response; do not create a new identity to bypass a conflict.
An interrupted recovery-copy publication is repaired from the current committed generation by that
retry, including any later credential replacement. The retry neither restores the original secret nor
repeats the transfer audit event. If recovery-copy repair fails, keep activity stopped and correct the
storage fault before retrying. A failed publication must retain its last readable generation.

To undo an unrotated transfer, submit the same provider and migration identity to
`POST /api/provider-routing/connections/paper-a/credentials/rollback`. Rollback moves the retained
secret to unassigned ownership and resets verification. It does not remove the host binding or enable
runtime fallback: the selected scoped record remains unavailable. Changed/rotated/deleted scoped
generations, occupied legacy destinations, foreign owners and mismatched operations refuse rollback.
Never restore a pre-rotation vault backup: the provider may have invalidated that token. Repeated
migration or rollback commands return the retained receipt without repeating a later operation.

### Recovery after rollback

Rollback intentionally leaves the selected connection without credentials. Use the following recovery
sequence while affected provider activity remains stopped:

1. Retain the rollback receipt and the reviewed tenant, provider, connection, external account,
   environment and runtime binding. Read
   `GET /api/providers/connections?connectionId=paper-a` in the intended tenant session. Confirm that
   this selected connection is unavailable. The aggregate provider catalog may still describe the
   unassigned record; it is not evidence that the selected runtime is usable.
2. Decide which credential is authoritative. If the returned unassigned secret is still valid for the
   intended owner, obtain a **fresh migration preview** and review its current generation. Submit a
   **new, explicitly reviewed migration identity** to transfer that generation into the empty owned
   scope. Reusing the original rolled-back identity only returns its receipt and will not transfer
   again. A new identity is appropriate for this new recovery decision, never to bypass an uncertain
   result or a generation conflict.
3. If replacement credentials are required, save them through Settings with the connection selected,
   or through `PUT /api/providers/alpaca/credentials?connectionId=paper-a`. Do not repair an owned
   connection through a provider-wide route, environment variable or old vault backup. If rollback
   was refused because credentials changed or rotated, preserve the current scoped generation and
   repair that connection; do not attempt to restore the original token.
4. Check the retained connection and host binding again. If ownership needs correction, use the
   explicit owned-connection and binding commands with the reviewed identity. Retain before/after
   configuration evidence and operator attribution; the vault migration receipt does not separately
   audit the host-binding change. Reconstruct affected provider instances while activity stays stopped.
5. Verify through `POST /api/providers/alpaca/verify?connectionId=paper-a` and reread the selected
   connection. Retain actual upstream account/environment evidence and the credential generation used
   for verification. Resolve pending audit or storage failures, then obtain the authorized operator's
   release decision. The legacy-only accounting, Plaid, IB Flex remote-fetch and module-test paths
   described above remain unavailable for owned families.

For interrupted commands, first retry the original provider, scope, migration identity and expected
generation. Do not infer rollback, successful verification or permission to resume from a transport
error, an immutable retry receipt, or a successful migration response alone. A concurrent ownership
change requires reloading and independently reviewing the retained connection before a new command.

Use canonical provider-scoped routes for new tooling and operator automation:

| Route | Method | Purpose |
|---|---|---|
| `/api/providers/{providerId}/credentials?connectionId={connectionId}` | `PUT` | Save credentials for the selected owned connection. |
| `/api/providers/{providerId}/credentials?connectionId={connectionId}` | `DELETE` | Remove credentials only from the selected owned connection. |
| `/api/providers/{providerId}/verify?connectionId={connectionId}` | `POST` | Verify the selected connection's current credential generation. |

Omit `connectionId` only for an entirely legacy provider family. Compatibility routes under
`/api/credentials/*` do not bypass ownership requirements and should not be used for new automation.

## Quick credential validation checklist

1. Start or inspect the host using [preflight startup](preflight-checklist.md#mandatory-command-set).
   Keep the source host in terminal 1. In terminal 2, complete
   [operator sign-in](preflight-checklist.md#authenticated-evidence-collection) to create the session.
2. Inspect masked effective configuration and the current tenant's provider connection rows:

```powershell
Invoke-RestMethod "$meridianBaseUrl/api/config/effective" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/providers/connections?connectionId=paper-a" -WebSession $operatorSession
```

3. In Settings, save or rotate the selected connection's credentials, then use its verification
   action. Confirm the expected account and environment. These mutations use the session and CSRF
   protections; an API key alone does not supply the required company/tenant scope.

4. If startup logs show credential warnings, stop and correct the configuration before enabling paper/live workflow.

5. For source/test evidence before promotion, run the Wave 1 provider packet automation from the
   repository root. It builds and runs the registered test slices; it does not replace a credentialed
   account verification or create human operator sign-off:

```powershell
pwsh ./scripts/dev/run-wave1-provider-validation.ps1
```

6. Require DK1 operator sign-off artifacts before paper/live rollout:

- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`

## Credential incident workflow

- **Expired / rejected credentials**: rotate the selected vault record, verify the connection, and
  rerun the impacted provider tests. Update environment values only for an intended fallback path.
- **Wrong account / entitlement**: validate account binding through provider integration tests and status surfaces; isolate by provider and disable non-essential routing during triage.
- **Configuration precedence issues**: identify the selected connection and credential source first;
  a stored record is not repaired by setting a different environment alias.
- **Persistent startup mis-read**: retain the sanitized connection status, repair the selected
  credential/configuration source, restart if environment settings changed, and verify again.
- **`401/403`**: reauthenticate or correct the operator's permission/company assignment using
  [preflight recovery](preflight-checklist.md#failure-and-recovery).

## Expected result

The selected connection reports a successful verification for the intended provider account and
environment, and required readiness blockers clear. The effective-config endpoint reports masked
configuration, but its `env:...` annotations alone do not establish which vault record a scoped
provider used. Retain connection identity, verification time/outcome, and the sanitized readiness
response; a passing test packet alone does not prove live credentials are usable.

## Evidence required for operator handoff

For any credential-impacting change, include:

- Validation packet and summary for the changed provider.
- Readiness/inbox snapshot evidence in the same run.
- Missing or partial sign-off owners in the DK1 packet if applicable.

For ownership migration or rollback, also retain:

| Evidence | Required observation |
| --- | --- |
| Account and environment | The selected connection's actual provider response matches the intended external account and environment; declared scope alone is insufficient. |
| Generation and operations | Preview generation, immutable migration/rollback receipts, replacement generation when applicable, and completed audit replay agree. |
| Reconstruction | Affected activity was stopped, in-flight requests drained and old provider instances discarded; reconstructed instances use the retained host binding. |
| Recovery drill | On the supported Windows user/host, interrupted publication and restart preserve the selected ownership, current replacement and recovery copy; no unassigned/config/environment fallback occurs. |
| Refusals | Wrong tenant/account, concurrent ownership change, stale generation and rollback after replacement refuse without changing unrelated records. |
| Runtime eligibility | Owned accounting imports, Plaid activity, IB Flex remote retrieval and module compatibility tests remain blocked until their tenant-aware runtime paths are implemented and reviewed. |

Use this in the support/evidence handoff index for promotion decisions.

## Plaid-specific handling

Plaid is a credential-managed provider family for bank, cash, reconciliation, identity/auth,
investment evidence, and sandbox transfer testing. Operator setup and sync behavior lives in
[Plaid Provider Operations](./plaid-provider-operations.md).

Production Plaid credentials do not imply live transfer approval. Live transfers require a separate
readiness flag plus treasury and compliance sign-off before transfer creation is allowed.

## Legacy links moved into canonical lane

- [Provider credential management (legacy source)](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/provider-credential-management.md)
- [Interactive Brokers setup archive](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/interactive-brokers-setup.md)
- [Alpaca setup archive](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/alpaca-setup.md)
