# Provider Onboarding: Alpaca

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This is the canonical operator procedure lane for configuring and validating Alpaca provider onboarding in Meridian.

## Scope

- credential setup and storage,
- paper-first validation,
- feed selection and reconciliation impact,
- operator-level validation handoff and rollback behavior.

## Quick operator flow

1. Create paper account and API credentials in the Alpaca dashboard.
2. Save credentials with the `paper` environment in the provider credential store, or use the local development fallback below.
3. Complete the persistence and authentication prerequisites, then start Meridian in workstation mode.
4. Confirm effective configuration and provider status.
5. Run the provider validation packet check before changing the approved trading environment or market-data feed.

## Prerequisites

- Active Alpaca account (paper for development).
- Paper-account credentials available from the approved secret store.
- A completed [local setup](../start/README.md#first-local-setup), an explicit
  [persistence profile](../reference/environment-variables.md#database-persistence), and an operator
  account with the configuration/provider/workstation permissions needed for this check.
- Windows local workstation with PowerShell 7; run source commands from the repository root. These commands are for a source
  checkout. Installed releases use the [installer and lifecycle supervisor](browser-workstation-installer.md).
- The intended host URL, data root, and account identity. The examples use `http://localhost:8080`.

## Configuration

For installed or customer deployments, use **Settings → Alpaca provider setup** to save the paper
credentials in Meridian's encrypted credential store and verify the account. Environment fallback
is disabled by default in packaged/customer builds. A stored credential record has its own trading
environment; changing shell variables does not replace that record.

For a source checkout using the Development/Test credential fallback, load `ALPACA_KEY_ID` and
`ALPACA_SECRET_KEY` into the host process environment from your secret store. Do not paste secret
values into this document or shell history. In **terminal 1**, select both the trading environment
and market-data settings explicitly:

```powershell
$env:ALPACA_TRADING_ENVIRONMENT = "paper"
$env:MDC_ALPACA_SANDBOX = "true"
$env:MDC_ALPACA_FEED = "iex"
```

`ALPACA_TRADING_ENVIRONMENT` selects the environment for Trading API credentials. The separate
`MDC_ALPACA_SANDBOX` override sets `Alpaca:UseSandbox` for the configured market-data client.
`ALPACA_PAPER` is not recognized. See the [environment reference](../reference/environment-variables.md#alpaca-provider)
for exact names, defaults, and credential precedence.

Recommended defaults:

- Start with the `paper` trading environment and `Alpaca:UseSandbox=true`.
- Begin with `Alpaca:Feed=iex` for verification.
- Only change to `sip` when the account has the required entitlement and packet/reconciliation
  checks pass. Feed selection and live trading approval are separate decisions.
- If this host should collect Alpaca streams, select `DataSource=Alpaca` in the applicable runtime
  configuration. Saving credentials alone does not switch an existing collector's provider.

## Mandatory validation sequence

1. In terminal 1, follow the [preflight startup sequence](preflight-checklist.md#mandatory-command-set)
   with the configured persistence, credentials, and Alpaca settings. Leave the host running.
2. In **terminal 2**, follow [authenticated evidence collection](preflight-checklist.md#authenticated-evidence-collection)
   to create `$operatorSession` and `$meridianBaseUrl`, then inspect the effective settings:

   ```powershell
   $effectiveConfig = Invoke-RestMethod "$meridianBaseUrl/api/config/effective" -WebSession $operatorSession
   $effectiveConfig.entries | Where-Object { $_.key -like 'alpaca.*' }
   Invoke-RestMethod "$meridianBaseUrl/api/workstation/operator/inbox" -WebSession $operatorSession
   ```

3. Use the provider setup screen's **Verify** action for the saved paper account. Account verification
   makes a provider request; it is separate from a configuration-only check and does not authorize orders.

## Expected result

- Effective configuration reports `alpaca.useSandbox=True` and `alpaca.feed=iex`; key material is
  masked. Confirm the credential source and paper-account identity in the provider verification result.
- Confirm provider readiness rows are non-blocking for required workflow.
- Confirm no active critical break introduced by feed mode.

## Failure and recovery

- **Startup rejects missing persistence or authentication:** follow the
  [preflight failure table](preflight-checklist.md#failure-and-recovery) before retrying.
- **Wrong environment or account:** stop provider validation, correct the saved credential record or
  local environment source, restart the source host after environment changes, and verify again.
- **Credential or entitlement rejection:** rotate the rejected credential or select an entitled feed;
  retain the sanitized failure and rerun the provider checks before promotion.
- **Readiness remains blocked:** keep the workflow in paper validation and retain the blocker in the
  operator handoff. A successful connection check alone is not promotion evidence.

## Evidence requirements

Before production promotion, require:

- `wave1-validation-summary` row for Alpaca provider,
- `dk1-operator-signoff.json` when the provider is part of release scope,
- one complete readiness packet with status timestamp and approver.

Keep evidence packets under sanitized paths used by team policy (for example: `artifacts/provider-validation/...`).

## Runbook links

- [Provider Credential Operations](./provider-credentials.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Provider Integration Status](../reference/provider-integration-status.md)
- [Provider Validation Matrix](../reference/provider-validation-matrix.md)
- [Provider Validation Evidence Schema](../reference/provider-validation-evidence-schema.md)

## Source and archive

- Legacy source archived at [archive/docs/providers/alpaca-setup.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/alpaca-setup.md)
