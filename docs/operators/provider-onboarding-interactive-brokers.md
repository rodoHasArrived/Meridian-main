# Provider Onboarding: Interactive Brokers

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-18

This is the canonical operator procedure lane for Interactive Brokers setup and validation in Meridian.

## Scope

- local vendor/SDK placement and build mode selection,
- TWS/Gateway socket setup,
- client-portal import posture,
- Flex Web Service statement-fetch setup,
- paper-safe verification and live promotion checks.

## Quick operator flow

1. Install IB API SDK locally (not committed) into approved local vendor path.
2. Choose mode:
   - `EnableIbApiSmoke` for compile verification,
   - `EnableIbApiVendor` for native runtime.
3. Build and validate selected mode.
4. Configure socket + optional Client Portal settings.
5. Run staged connectivity and trade-flow checks before live routing.

## Flex Web Service statement setup

IB Flex statements are an accounting/reconciliation evidence path and do not require the TWS socket
session used for order routing. In Interactive Brokers, create and activate a Flex Query that includes
the accounts and currencies Meridian must reconcile. For complete Margin Control Center evidence,
include Account Information, Cash Report, Trades, Open Positions, Open Lots, Interest Details or
Accruals, Borrow Fees, Commissions, Corporate Actions, Transfers, Option Exercises/Assignments/
Expirations, and Securities Borrowed/Lent where the account is entitled to those sections.

Store the Flex token and query id in Meridian's existing credential vault under provider id
`ib-flex`, using credential names `Token` and `QueryId`. The connector submits the documented v3
request, polls the returned statement reference within a bounded window, verifies that the fetch host
is an Interactive Brokers HTTPS endpoint, and retains the raw XML before canonical mapping. Do not
put the token or query id in a schedule, mapping profile, source file, log, or support bundle.

After saving credentials, open `Accounting` -> `Import statement` -> `Scheduled fetch`, select
`IB Flex Report`, preview the canonical rows and completeness evidence, and create or run the desired
broker-classified schedule. One Flex query may return multiple accounts; Meridian keeps account and
provider-prime scope on the retained evidence and Margin Control Center rollup.

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

## Source and archive

- Legacy source archived at [archive/docs/providers/interactive-brokers-setup.md](../../archive/docs/providers/interactive-brokers-setup.md)
