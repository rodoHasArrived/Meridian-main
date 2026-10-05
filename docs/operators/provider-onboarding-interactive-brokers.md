# Provider Onboarding: Interactive Brokers

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-22

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

## Prerequisites and execution context

- Run source build commands in PowerShell 7 from the repository root with the pinned .NET SDK.
  For a workstation launch, complete [preflight](preflight-checklist.md) for persistence and a
  signed-in operator with the required provider/workflow permissions.
- Obtain the official SDK separately and identify the actual `CSharpAPI.csproj` or `CSharpAPI.dll`.
  The SDK and provider credentials do not belong in source control.
- Start a paper TWS or IB Gateway session and confirm its configured socket port before running
  the connectivity script. TWS normally uses `7497`; IB Gateway normally uses `4002`.
- Run host checks in a second terminal using the
  [authenticated session](preflight-checklist.md#authenticated-evidence-collection). A build or
  successful TCP connection alone does not verify account identity, entitlements, or order routing.

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

- Default: no official SDK; the standard market-data client uses its simulator. This does not
  establish a TWS/Gateway connection or validate a real account.
- Smoke: compile-only verification of IB API path.
- Vendor: native IB connectivity with local API SDK.

Use vendor mode for operational validation and evidence, never as a blind default in production.

## Build commands

```powershell
# Standard build (no official SDK)
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true
# Compile-only API stub; choose this separately from vendor mode
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true -p:EnableIbApiSmoke=true
```

Choose the intended build mode; smoke and vendor modes must not be mixed. Use the vendor command
below with an explicit SDK input instead of assuming that `EnableIbApiVendor=true` locates one.

### Supported official-SDK runtime lane

The release configuration remains opt-in: `EnableIbApiVendor` and derived runtime integration both
default to `false`. Vendor mode is supported only with an official `CSharpAPI.csproj` or
`CSharpAPI.dll`; it fails closed if neither resolves. Run the same build-and-connectivity check used
by the protected paper integration lane with one SDK input and a paper TWS/Gateway socket:

```powershell
$ibApiProjectPath = Read-Host 'Full path to the official CSharpAPI.csproj'
pwsh scripts/dev/build-ibapi-vendor.ps1 `
  -IBApiProjectPath $ibApiProjectPath `
  -SmokeHost '127.0.0.1' `
  -SmokePort 7497
```

`build-ibapi-vendor.ps1` compiles against the official SDK and verifies only TCP reachability to
the specified paper socket. It does not authenticate, request market data, or place an order.
Use `-IBApiDllPath` instead of `-IBApiProjectPath` for an official DLL, and change `-SmokePort` to
the actual paper socket port. Do not supply both SDK inputs.
See [Interactive Brokers API Compatibility](../reference/interactive-brokers-api-compatibility.md)
for the tested-version evidence and protected GitHub Actions environment contract.

## TWS / Gateway validation

In TWS/Gateway:

- enable socket API clients,
- allow localhost,
- confirm the paper socket port (`7497` for TWS or `4002` for IB Gateway unless customized),
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

## Expected results and recovery

The vendor script must exit successfully after both compilation and TCP reachability checks.
Then verify the intended paper account, provider status, and entitlement-dependent operations
through the workstation; keep this evidence separate from compile/smoke output.

| Failure | Next action |
| --- | --- |
| Missing SDK or mixed smoke/vendor flags | Correct the project/DLL path or select one mode, then rebuild. |
| TCP timeout or connection refused | Confirm paper TWS/Gateway is running, socket API is enabled, localhost is allowed, and the script uses that application's configured port. |
| Connected socket but missing data or account access | Inspect provider diagnostics and entitlements; TCP reachability does not prove an authenticated API session. |
| Host still reports simulation | Confirm the launched host includes the vendor-enabled dependency build; preserve the reported mode rather than treating simulated output as provider proof. |
| Flex fetch or parsing failure | Verify the `ib-flex` vault record and query scope, retain sanitized failure evidence, and follow [statement reconciliation](statement-reconciliation-report-operations.md). |

## Runbook links

- [Provider Credential Operations](./provider-credentials.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Failover and Recovery](./failover-and-recovery.md)
- [Provider Integration Status](../reference/provider-integration-status.md)
- [Provider Validation Matrix](../reference/provider-validation-matrix.md)
- [Provider Validation Evidence Schema](../reference/provider-validation-evidence-schema.md)
- [Interactive Brokers API Compatibility](../reference/interactive-brokers-api-compatibility.md)

## Source and archive

- Legacy source archived at [archive/docs/providers/interactive-brokers-setup.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/interactive-brokers-setup.md)
