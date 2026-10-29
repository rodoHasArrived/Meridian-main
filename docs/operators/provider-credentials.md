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
   selects that owned connection for host provider construction. Restart through the lifecycle
   supervisor, then verify. Editing credentials does not hot-activate an already running collector.

The host revalidates the selected tenant, connection, provider, enabled state, account and environment
before reading secrets. Owned providers without an explicit selection block construction; missing
selected credentials never fall back to another connection, provider-wide record, config or environment.
Duplicate IDs, partial ownership, disabled connections and dangling defaults block. Removing a
connection leaves its host default dangling so removal cannot silently select legacy credentials.
Entirely legacy families retain their existing fallback policy.
The retained `ownershipRequiredProviders` marker survives connection deletion, including deletion
before a host default was selected. Removing ownership cannot implicitly re-enable legacy credentials.

### Reviewed migration and rollback

Stop affected provider activity before changing ownership. Obtain a non-secret preview with
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

To undo an unrotated transfer, submit the same provider and migration identity to
`POST /api/provider-routing/connections/paper-a/credentials/rollback`. Rollback moves the retained
secret to unassigned ownership and resets verification. It does not remove the host binding or enable
runtime fallback: the selected scoped record remains unavailable. Changed/rotated/deleted scoped
generations, occupied legacy destinations, foreign owners and mismatched operations refuse rollback.
Never restore a pre-rotation vault backup: the provider may have invalidated that token. Repeated
migration or rollback commands return the retained receipt without repeating a later operation.

Use canonical provider-scoped routes for new tooling and operator automation:

| Route | Method | Purpose |
|---|---|---|
| `/api/providers/{providerId}/credentials` | `PUT` | Upsert provider credentials. |
| `/api/providers/{providerId}/credentials` | `DELETE` | Remove provider credentials. |
| `/api/providers/{providerId}/verify` | `POST` | Verify provider connectivity with current credentials. |

Compatibility routes for legacy clients remain supported under `/api/credentials/*`, but should not be used for new automation.

## Quick credential validation checklist

1. Start or inspect the host using [preflight startup](preflight-checklist.md#mandatory-command-set).
   Keep the source host in terminal 1. In terminal 2, complete
   [operator sign-in](preflight-checklist.md#authenticated-evidence-collection) to create the session.
2. Inspect masked effective configuration and the current tenant's provider connection rows:

```powershell
Invoke-RestMethod "$meridianBaseUrl/api/config/effective" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/providers/connections" -WebSession $operatorSession
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
