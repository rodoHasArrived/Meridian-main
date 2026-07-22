---
title: Statement-to-Report Operations
status: active
owner: financial-operations
reviewed: 2026-07-20
audience: operators
---

# Statement-to-Report Operations

Use this workflow for the supported golden path from a broker or custodian statement to retained,
hash-verifiable reconciliation report artifacts. It creates an authoritative statement and
reconciliation report; it does not invent accounting balances, NAV, or financial-statement values.

## Start the workflow

Send authenticated `multipart/form-data` to
`POST /api/workstation/reconciliation/statement-to-report`. The workstation session supplies the
tenant, company, and operator identity; never send an actor or tenant override in the form.

Required form fields:

- `file`: non-empty statement file accepted by the statement-connector ingress policy.
- `sourceInstitution`: broker or custodian name.
- `fundAccountId`: Meridian fund-account scope.
- `externalAccountId`: provider-side account scope.
- `periodStart` and `periodEnd`: `YYYY-MM-DD`; end must not precede start.

Optional fields are `sourceKind` (`broker` by default), `connectorId`, `mappingProfileId`, and
`toleranceProfileId`. Connector, mapping, and tolerance policy are part of the workflow identity, so
changing one creates a distinct workflow even when the statement bytes are unchanged.

The start response is `201` when the report completes immediately or `202` when reconciliation or
another durable stage remains. Record `workflowId`, `statusRoute`, and `resumeRoute` from the
response.

## Interpret status

| Status | Operator action |
| --- | --- |
| `InputRetained`, `Importing`, `RenderingReport` | Poll the status route; do not upload a modified file under the same business event. |
| `AwaitingReconciliation` | Follow linked break/case evidence, resolve or govern disposition, then call the resume route. |
| `Failed` | Preserve `failureReason`; correct the named dependency and use the existing resume route. Do not begin an unrelated replacement import. |
| `Completed` | Retrieve every retained artifact and retain the workflow response with downstream evidence. |

The coordinator persists the input before import and checkpoints a committed import before Evidence
Vault linkage or rendering. A retry after those checkpoints resumes from retained state rather than
repeating the import. Concurrent processes serialize work on the same content-and-scope identity.

## Resume and retrieve artifacts

- Read current state with `GET {statusRoute}`.
- Resume a paused or failed workflow with `POST {resumeRoute}`.
- Download an artifact from the `downloadRoute` in each `retainedArtifacts` entry.

Artifact downloads are served only inside the authenticated tenant/company scope. The server hashes
the retained bytes again and refuses delivery if they differ from `contentHashSha256`. The workflow
retains:

- `statement-reconciliation-report.json` with scope, period, run, reconciliation, case lineage, and
  Evidence Vault references;
- `statement-kind-summary.csv` with canonical record counts; and
- `manifest.json` beside the artifacts with descriptors, hashes, and evidence references.

## Recovery and escalation

1. Reuse the recorded workflow ID and status route after process restart.
2. If reconciliation is pending, resolve or disposition every linked break/case before resume.
3. If Evidence Vault or storage was unavailable, restore that dependency and call resume; verify the
   statement-run ID did not change.
4. If an artifact hash fails, stop downstream delivery, retain the workflow snapshot and affected
   bytes, and escalate as a durability incident. Do not replace the bytes or edit the manifest.
5. If access is denied, confirm the authenticated company and tenant rather than copying artifacts
   across scopes.

The server data root retains workflow state under `reporting/statement-to-report/<workflowId>/`.
Treat `workflow.json`, `input/`, `artifacts/`, and the lock file as service-owned data. Include that
directory in the supported data-root backup and restore drill; operators must not edit it directly.

For break/case handling, see [Reconciliation Operations](./reconciliation-operations.md). For general
receipt and artifact failure rules, see [Verified Outcome Recovery](./verified-outcome-recovery.md).
