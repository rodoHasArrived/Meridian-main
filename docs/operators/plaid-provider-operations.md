# Plaid Provider Operations

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-01

Plaid is Meridian's governed bank and financial-account provider family for cash balances,
depository transactions, account verification evidence, investment evidence, and sandbox-gated
transfer testing. Plaid is not a market-data adapter and does not implement `IMarketDataClient`.

## Scope

Use Plaid for:

- Linking bank and investment accounts through Plaid Link.
- Recording bank balance snapshots into fund-account evidence.
- Importing depository transactions as bank statement lines for reconciliation.
- Retaining identity/auth verification status as masked account evidence.
- Reading investment holdings and transactions as provider evidence, not ledger postings.
- Testing Plaid transfer authorization and creation in Sandbox or Development only.

Plaid does not make ledger postings authoritative. Accounting entries still require Meridian
workflow approval, reconciliation review, and the normal Books Before Broker controls.

## Prerequisites and execution context

- Use the signed-in Windows workstation with an operator allowed to manage credentials/link items
  in the intended company. Complete [preflight](preflight-checklist.md) for persistence and account
  setup. Shell examples use PowerShell 7 from the repository root for a source host.
- Select a Plaid Sandbox account and enabled products for the first test. Store provider credentials
  in the [provider vault](provider-credentials.md); item access tokens stay there as well.
- Confirm the target Meridian accounts and consent scope before linking. API reads need
  `ManageCredentials`, `ViewTrades`, or `ViewDirectLending`; link/exchange/sync mutations require
  an applicable management permission and retain the requesting operator's identity.

## Setup

1. Save the sandbox provider credentials in the shared credential surface. For an intended local
   development fallback, load `PLAID_CLIENT_ID` and `PLAID_SECRET` through the secret store into
   the host process environment, then select the sandbox environment:

```powershell
$env:PLAID_ENV = "sandbox"
```

2. Keep transfer creation disabled unless a sandbox/development transfer test is in scope:

```powershell
$env:PLAID_ENABLE_TRANSFERS = "false"
$env:PLAID_ENABLE_LIVE_TRANSFERS = "false"
```

   Also set the effective `Plaid:EnableTransfers` and `Plaid:EnableLiveTransfers` configuration to
   false. Each enabled flag combines configuration and environment with OR, so a false environment
   value cannot disable a true configuration value. `Plaid:Environment` takes precedence over
   `PLAID_ENV`; confirm the selected environment in the provider setup/readiness surface.

3. Leave the webhook base URL unset for a loopback-only workstation. Configure it only for an
   explicitly managed ingress that Plaid can reach; setting this value does not publish a local host:

```powershell
$env:PLAID_WEBHOOK_BASE_URL = "https://<public-host>"
```

4. Start the workstation host and use the browser or WPF setup surface to request a Link token,
complete Plaid Link, and exchange the returned public token through Meridian.

   For a source host, use [preflight startup](preflight-checklist.md#mandatory-command-set) in
   terminal 1 and the workstation in a separate window. Installed releases use the lifecycle
   supervisor and its generated loopback URL.

The server stores Plaid access tokens in `IProviderCredentialStore`. Normal read models contain
only item ids, account ids, institution/name/mask metadata, status, freshness, and verification
state.

Before Link starts, browser setup can search supported financial institutions through Meridian's
`/api/plaid/institutions/search` endpoint. The browser never calls Plaid directly; Meridian uses
configured Plaid credentials, asks Plaid for matching institutions, and returns institution id,
name, country, and product coverage so operators can select the intended bank. The selected
institution id is then carried into `/api/plaid/link-token` so the secure bank connection opens
against the operator's selected bank context.

For Sandbox setup, select the institution, choose **Open secure bank connection**, then sign in
inside the Plaid Link browser modal with Plaid's standard Sandbox credentials:

```text
username: user_good
password: pass_good
```

These are test credentials only and should appear in operator Sandbox guidance, not production
customer-facing copy.

When Link succeeds, the browser receives Plaid's temporary `public_token` and selected account
metadata from the Link callback. The browser immediately posts that evidence to Meridian's
`/api/plaid/public-token/exchange` endpoint; only the server exchanges the public token for an
access token, and the resulting access token remains in `IProviderCredentialStore`.

## Sync Procedure

After Link exchange, run item sync from the shared Plaid endpoint flow.

- Balance sync records `RecordAccountBalanceSnapshotRequest` evidence with Plaid source/freshness
  metadata.
- Transaction sync uses Plaid cursor state and imports added/modified transactions as
  `BankStatementLineDto` batches.
- Removed transactions are observed through the sync result and must be handled idempotently by the
  reconciliation workflow.
- Investment sync records provider evidence counts and must not post ledger entries automatically.
- Identity/auth sync updates account verification status without showing full account or routing
  numbers.

Webhooks enqueue lightweight item events only. Operators should expect workers or manual sync to
poll Plaid APIs after webhook receipt; duplicate and out-of-order webhooks are safe.

## Transfer Guardrails

Plaid transfer creation remains blocked unless all of these are true:

- `PlaidOptions.EnableTransfers` is true.
- The environment is Sandbox or Development, or production has explicit live-transfer readiness
  enabled.
- Meridian can verify an approved payment workflow for the requested entity, currency, and amount.
- Plaid transfer authorization returns approved before transfer creation.

Never enable live transfers from configuration alone. Production transfer readiness requires
separate treasury, compliance, and operational sign-off before `EnableLiveTransfers` is allowed.

## Operator Checks

- Confirm item status is `Linked` and consent has not been revoked or expired.
- Confirm linked accounts are mapped to the intended Meridian fund/bank accounts.
- Confirm balance and transaction freshness before reconciliation.
- Confirm transfer-disabled reasons are visible before any treasury testing.
- Do not expose full account numbers, routing numbers, raw auth payloads, or Plaid access tokens in
  screenshots, logs, support bundles, or operator notes.

## Validation

For source-level regression checks, use the contention-aware runner from the repository root:

```powershell
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~Plaid" --queue
```

These tests are implementation evidence, not verification of a deployed bank connection. For the
configured host, use [preflight sign-in](preflight-checklist.md#authenticated-evidence-collection)
in terminal 2, then inspect the linked items and accounts:

```powershell
Invoke-RestMethod "$meridianBaseUrl/api/plaid/items" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/plaid/accounts" -WebSession $operatorSession
```

## Expected results and recovery

The selected sandbox item is linked, account mappings identify the intended Meridian accounts,
and the sync result contains current balance/transaction evidence. Link success alone is not a
completed sync or a ledger posting. Retain the item/account identifiers, environment, consent,
sync time/result, and sanitized error status.

| Symptom | Next action |
| --- | --- |
| `401/403` | Repair the session or operator permissions using [preflight](preflight-checklist.md#failure-and-recovery). |
| Missing credentials or wrong environment | Correct the selected vault record and effective Plaid configuration; restart after changing host environment variables. |
| Revoked/expired consent or rejected item sync | Restore consent through the provider connection flow, confirm the existing item/account mapping, then repeat sync and reconciliation checks. |
| Stale balances or incomplete transaction evidence | Retain the sync error/cursor context and keep reconciliation blocked until a successful sync covers the required period. |
| Transfer remains blocked | Read the specific readiness/approval failure. A sandbox connection or true configuration flag does not supply missing payment approval. |
