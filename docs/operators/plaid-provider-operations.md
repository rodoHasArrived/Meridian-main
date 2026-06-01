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

## Setup

1. Configure provider credentials through the shared provider credential surface or environment:

```powershell
$env:PLAID_ENV = "sandbox"
$env:PLAID_CLIENT_ID = "<client-id>"
$env:PLAID_SECRET = "<sandbox-or-development-secret>"
```

2. Keep transfer creation disabled unless a sandbox/development transfer test is in scope:

```powershell
$env:PLAID_ENABLE_TRANSFERS = "false"
```

3. Configure the webhook base URL only when the Meridian host is reachable by Plaid:

```powershell
$env:PLAID_WEBHOOK_BASE_URL = "https://<public-host>"
```

4. Start the workstation host and use the browser or WPF setup surface to request a Link token,
complete Plaid Link, and exchange the returned public token through Meridian.

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

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Debug --no-restore --filter "Plaid"
python build/scripts/docs/validate-source-readmes.py --summary
python build/scripts/docs/validate-doc-hashes.py --summary
```
