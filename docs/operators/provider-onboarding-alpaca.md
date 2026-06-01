# Provider Onboarding: Alpaca

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This is the canonical operator procedure lane for configuring and validating Alpaca provider onboarding in Meridian.

## Scope

- credential setup and storage,
- paper-first validation,
- feed selection and reconciliation impact,
- operator-level validation handoff and rollback behavior.

## Quick operator flow

1. Create paper account and API credentials in the Alpaca dashboard.
2. Set credentials via secure environment variables or approved credential store.
3. Start Meridian with standard workstation mode.
4. Confirm effective configuration and provider status.
5. Run provider validation packet check before enabling production feed mode.

## Prerequisites

- Active Alpaca account (paper for development).
- Secure credentials for `ALPACA_KEY_ID` and `ALPACA_SECRET_KEY`.
- Appropriate host port and environment routing for your deployment target.

## Configuration

Use the environment-first pattern:

```powershell
$env:ALPACA_KEY_ID = "<your-api-key-id>"
$env:ALPACA_SECRET_KEY = "<your-secret-key>"
$env:ALPACA_PAPER = "true"
```

Then in runtime config, ensure provider is explicitly enabled and paper mode is intended for non-production.

Recommended defaults:

- start with `Paper=true`
- begin with `DataFeed=iex` for verification
- only increase to `sip` after stable packet + reconciliation checks

## Mandatory validation sequence

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
curl http://localhost:8080/api/config/effective
curl http://localhost:8080/api/workstation/operator/inbox
```

- Confirm credentials are loaded from environment (not repo files).
- Confirm provider readiness rows are non-blocking for required workflow.
- Confirm no active critical break introduced by feed mode.

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

- Legacy source: [docs/providers/alpaca-setup.md](../providers/alpaca-setup.md)
- Archive copy: [archive/docs/providers/alpaca-setup.md](../../archive/docs/providers/alpaca-setup.md)
