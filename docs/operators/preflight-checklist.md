---
title: Operator Preflight Checklist
status: active
owner: core-team
reviewed: 2026-10-05
audience: operators
---

# Operator Preflight Checklist

Use this checklist before operator rollout, paper workflow exposure, or support handoff.

## What this is

This lane captures the minimum reproducible checks that keep Meridian in a controlled readiness posture.

## Prerequisites

- For the Windows local workstation procedure, use PowerShell 7 and run source commands from the repository root after
  [local setup](../start/README.md#first-local-setup). The commands below target a source checkout.
  For an installed release, start the host through the [lifecycle supervisor](../reference/lifecycle-control-plane.md)
  and use its actual loopback URL instead of launching a second host on port 8080.
- Configure [database persistence](../reference/environment-variables.md#database-persistence)
  before starting the host. Release-bound checks need the intended PostgreSQL stores. The explicit
  development profile is for local fixtures and cannot supply production persistence evidence.
- Provision an operator account through the first-account setup or governed user configuration.
  Its company/tenant and permissions must cover the workflow being checked: configuration reads
  require `ViewConfig` or `ModifyConfig`; trading readiness requires `ViewTrades`.
  See [authentication setup](../start/README.md#persistence-and-simulation-defaults).
- Identify the data root, intended provider/account, workflow scope, and destination for sanitized
  evidence. Load credentials through the approved secret store; do not include values, session
  cookies, or login request bodies in the packet.

## Preflight matrix

| Area | Check | Required artifact |
| --- | --- | --- |
| Runtime boot | Start host in intended mode and confirm no startup-blocking errors. | Logs + API startup return path |
| Config posture | Confirm credential and workspace settings via effective config endpoint. | `GET /api/config/effective` output |
| Mutation guardrails | Confirm API auth and mutation-rate-limit controls are enabled for execution/direct-lending/security-master routes. | `MDC_API_KEY` + `MDC_DISABLE_RATE_LIMIT` posture evidence |
| Provider readiness | Verify provider rows and blockers in readiness/validation outputs. | `docs/reference/provider-validation-matrix.md` + latest wave packet |
| Reconciliation posture | Confirm there is no blocking reconciliation debt entering rollout windows. | Reconciliation policy + operator incident queue |
| Data integrity | Confirm checkpoint and backfill behavior for changed symbol/provider sets. | Backfill status outputs + checkpoint evidence |
| Packaging path | Confirm WPF/browser artifacts can be generated/started from canonical commands. | Command output logs and run artifacts |

## Mandatory command set

In **terminal 1**, run the configuration checks with the intended host environment already set:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
if ($LASTEXITCODE -ne 0) { throw 'Configuration validation failed; correct it before launch.' }
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
if ($LASTEXITCODE -ne 0) { throw 'Quick check failed; correct it before launch.' }
```

Then start the source host in the same terminal:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
```

Keep terminal 1 open while the host runs. A successful configuration check does not establish
database readiness, provider connectivity, or release acceptance.

## Authenticated evidence collection

In **terminal 2**, sign in with the provisioned operator account and retain the session only in
memory. Use the actual loopback URL for an installed release:

```powershell
$meridianBaseUrl = 'http://localhost:8080'
$operatorCredential = Get-Credential -Message 'Meridian operator account for preflight'
$loginBody = @{
    username = $operatorCredential.UserName
    password = $operatorCredential.GetNetworkCredential().Password
} | ConvertTo-Json
try {
    $loginResult = Invoke-RestMethod "$meridianBaseUrl/api/auth/login" -Method Post `
        -ContentType 'application/json' -Body $loginBody -SessionVariable operatorSession -ErrorAction Stop
    if (-not $loginResult.success) { throw 'Operator sign-in did not succeed.' }
} finally {
    Remove-Variable loginBody, operatorCredential -ErrorAction SilentlyContinue
}

Invoke-RestMethod "$meridianBaseUrl/api/auth/me" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/config/effective" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/workstation/trading/readiness" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/workstation/operator/inbox" -WebSession $operatorSession
```

The session carries the operator's company/tenant scope. An API key alone has no workstation
tenant scope, even with a broad role, and cannot replace that sign-in. For a non-workstation API
read whose permission is granted to the configured API-key role, supply the key in the header:

```powershell
# Optional alternative for this config read only; load the existing key in terminal 2 first.
if (-not $env:MDC_API_KEY) { throw 'Load the configured API key from the secret store first.' }
Invoke-RestMethod "$meridianBaseUrl/api/config/effective" `
    -Headers @{ 'X-Api-Key' = $env:MDC_API_KEY }
```

The API-key role must include `ViewConfig` or `ModifyConfig`. Do not put the key in a query string.

## Expected result

- Both configuration checks exit with code `0`, and the host starts without persistence/auth errors.
- Authenticated reads return JSON with HTTP `200`; `/api/auth/me` identifies the intended operator,
  company, and permissions. A login page or authentication error is not successful API evidence.
- Effective configuration names the expected data root/provider and shows masked credential fields.
- Readiness and inbox payloads belong to the intended scope. Review their blockers and work items;
  HTTP `200` alone does not mean the workflow is ready or that an empty inbox proves completeness.

As part of controlled validation, retain negative authorization evidence showing `401/403` for
unauthorized requests to the high-risk mutation routes below. Use the route's actual mutation
method in an isolated test environment; a `404/405` from a bare GET does not test authorization:

- `/api/execution/orders/submit`
- `/api/security-master/ingest/edgar`
- `/api/loans/rebuild-all`

## Failure and recovery

| Symptom | Next action |
| --- | --- |
| Missing governance connection or Production rejects the local profile | Correct the intended persistence/environment settings; restart from terminal 1. Do not substitute a local fixture for release evidence. |
| Connection refused or port already in use | Confirm the host/supervisor state and actual loopback URL. Use the existing host or stop the source host before relaunching; avoid two writers on the same data root. |
| Login returns `503` | Finish account provisioning; required authentication has no configured account. |
| API returns `401`, or login returns `401/429` | Confirm the credentials/session and respect any `Retry-After` interval before retrying. |
| API returns `403` | Check the operator's permission and company/tenant assignment. An API key cannot supply workstation tenant scope. |
| Readiness reports missing dependencies or blockers | Retain the response and host logs, correct the named dependency, and repeat the affected checks. Follow [Verified Outcome Recovery](verified-outcome-recovery.md) for a failed operation receipt. |

After source-host checks, stop terminal 1 with `Ctrl+C` and close the evidence shell to discard its
session. For installed releases, use supervisor stop/restart controls; closing the browser does
not stop the host. Retain sanitized results and the time, version, data root, and workflow scope
before handing off.

## Readiness gate criteria

Before any release-bound activity:

- No blocking provider rows for required workflow providers in the current packet.
- DK1 operator sign-off state is `review-ready` for the active packet date when promotion criteria apply.
- Evidence packet fields in `wave1-validation-summary` and `dk1-operator-signoff` are internally consistent.

## Rollback posture

- If a blocking readiness condition appears during preflight, stop rollout, disable affected provider routes, and document the blocker in operator inbox.
- For persistent degraded blocks, keep traffic within non-production simulation paths until corrective validation is regenerated.

## Operational notes

- Preflight language is intentionally conservative: if evidence is incomplete, treat as block by default.
- Dated evaluations can explain history, but cannot replace fresh packet-backed validation for current operator claims.

## Linked artifacts

- [Provider validation packet bundle workflow](../reference/provider-validation-evidence-schema.md)
- [Provider validation matrix](../reference/provider-validation-matrix.md)
- [Deployment standard (legacy source moved from migration lane)](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/environment-and-deployment-standard.md)
- [Operator startup and launch references](./README.md)
