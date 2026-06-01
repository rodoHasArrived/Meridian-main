# Provider Credential Operations

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-05-31

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

Meridian reads credentials from configuration, with environment variables taking precedence over
`appsettings*.json`.

### Common credential patterns

- Use provider-specific variables for live data and broker integrations.
- Avoid storing secrets in repository files, logs, or user shell history.
- For IBKR simulation builds, use the StockSharp connector surface in config and verify with local replay paths.
- For Plaid, configure `PLAID_ENV`, `PLAID_CLIENT_ID`, and `PLAID_SECRET`, but keep access tokens
  and item secrets in the Meridian credential store only. Plaid access tokens must not be written
  to user environment variables, docs, support bundles, or logs.

See concrete variable names and binding keys in [Environment Variables](../reference/environment-variables.md).

## Quick credential validation checklist

1. Start Meridian with your intended mode:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
```

2. Confirm effective config source:

```powershell
curl http://localhost:8080/api/config/effective
```

3. Confirm provider factory credential path includes expected source (`env:...`) rather than default/empty values.

4. If startup logs show credential warnings, stop and correct the configuration before enabling paper/live workflow.

5. Run provider validation for impacted lanes before promotion:

```powershell
./scripts/dev/run-wave1-provider-validation.ps1
```

6. Require DK1 operator sign-off artifacts before paper/live rollout:

- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`

## Credential incident workflow

- **Expired / rejected credentials**: rotate keys in the secure store, update environment, and re-run `run-wave1-provider-validation.ps1`.
- **Wrong account / entitlement**: validate account binding through provider integration tests and status surfaces; isolate by provider and disable non-essential routing during triage.
- **Configuration precedence issues**: check environment variable naming (`MDC_...` vs legacy keys) and startup effective-config sources.
- **Persistent startup mis-read**: clear stale local settings, restart process, and re-check effective config endpoint.

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

- [Provider credential management (legacy source)](../../archive/docs/operations/provider-credential-management.md)
- [Interactive Brokers setup (legacy)](../providers/interactive-brokers-setup.md)
- [Alpaca setup (legacy)](../providers/alpaca-setup.md)
