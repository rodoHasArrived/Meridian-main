# Provider Onboarding: Interactive Brokers

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-22

This is the canonical operator procedure lane for Interactive Brokers setup and validation in Meridian.

## Scope

- local vendor/SDK placement and build mode selection,
- TWS/Gateway socket setup,
- client-portal import posture,
- paper-safe verification and live promotion checks.

## Quick operator flow

1. Install IB API SDK locally (not committed) into approved local vendor path.
2. Choose mode:
   - `EnableIbApiSmoke` for compile verification,
   - `EnableIbApiVendor` for native runtime.
3. Build and validate selected mode.
4. Configure socket + optional Client Portal settings.
5. Run staged connectivity and trade-flow checks before live routing.

## Setup modes

- Guidance: default runtime behavior with IB guidance only.
- Smoke: compile-only verification of IB API path.
- Vendor: native IB connectivity with local API SDK.

Use vendor mode for operational validation and evidence, never as a blind default in production.

## Build commands

```powershell
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true -p:EnableIbApiSmoke=true
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true -p:EnableIbApiVendor=true
```

Smoke and vendor modes must not be mixed.

### Supported official-SDK runtime lane

The release configuration remains opt-in: `EnableIbApiVendor` and derived runtime integration both
default to `false`. Vendor mode is supported only with an official `CSharpAPI.csproj` or
`CSharpAPI.dll`; it fails closed if neither resolves. Run the same build-and-connectivity check used
by the protected paper integration lane with one SDK input and a paper TWS/Gateway socket:

```powershell
pwsh scripts/dev/build-ibapi-vendor.ps1 `
  -IBApiProjectPath 'D:\vendor\IBApi\TWS API\source\CSharpClient\client\CSharpAPI.csproj' `
  -SmokeHost '127.0.0.1' `
  -SmokePort 7497
```

`build-ibapi-vendor.ps1` compiles against the official SDK and verifies only TCP reachability to
the specified paper socket. It does not authenticate, request market data, or place an order.
See [Interactive Brokers API Compatibility](../reference/interactive-brokers-api-compatibility.md)
for the tested-version evidence and protected GitHub Actions environment contract.

## TWS / Gateway validation

In TWS/Gateway:

- enable socket API clients,
- allow localhost,
- set paper port `7497` during initial validation,
- set live port only after explicit operator approval,
- disable read-only API if order routing is required.

## Required checks

- confirm build mode exposed by status endpoint matches intended mode,
- confirm socket readiness and Client Portal readiness when enabled,
- verify market data, historical bars, and paper-order roundtrip,
- verify that the runtime surface reports `Paper` for paper TWS/Gateway and `Live` only for a vendor-enabled live connection; a guidance or smoke build must never be promoted as live,
- import an account-scoped IB Flex report and reconcile its trades, cash transactions, fees, interest, FX conversions, and corporate actions against the API/TWS snapshot; investigate every variance before live promotion,
- ensure live routing remains disabled until paper validation is complete.

## Evidence requirements

- collect sanitized validation artifacts per provider-validation lane,
- include timestamps, mode, host/port, and evidence of command/endpoint checks,
- keep failures plus rollback actions in operator inbox.

## Runbook links

- [Provider Credential Operations](./provider-credentials.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Failover and Recovery](./failover-and-recovery.md)
- [Provider Integration Status](../reference/provider-integration-status.md)
- [Provider Validation Matrix](../reference/provider-validation-matrix.md)
- [Provider Validation Evidence Schema](../reference/provider-validation-evidence-schema.md)
- [Interactive Brokers API Compatibility](../reference/interactive-brokers-api-compatibility.md)

## Source and archive

- Legacy source archived at [archive/docs/providers/interactive-brokers-setup.md](../../archive/docs/providers/interactive-brokers-setup.md)
