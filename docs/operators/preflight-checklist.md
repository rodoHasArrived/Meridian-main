---
title: Operator Preflight Checklist
status: active
owner: core-team
reviewed: 2026-06-02
audience: operators
---

# Operator Preflight Checklist

Use this checklist before operator rollout, paper workflow exposure, or support handoff.

## What this is

This lane captures the minimum reproducible checks that keep Meridian in a controlled readiness posture.

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

Run the core command sequence first:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
```

Then gather evidence:

```powershell
curl http://localhost:8080/api/config/effective
curl http://localhost:8080/api/workstation/trading/readiness
curl http://localhost:8080/api/workstation/operator/inbox
```

For high-risk mutation surfaces, also confirm `401/403` behavior for unauthorized calls to:

- `/api/execution/orders/submit`
- `/api/security-master/ingest/edgar`
- `/api/loans/rebuild-all`

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
- [Deployment standard (legacy source moved from migration lane)](../../archive/docs/operations/environment-and-deployment-standard.md)
- [Operator startup and launch references](./README.md)
