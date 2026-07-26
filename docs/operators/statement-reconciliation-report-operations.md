---
title: Statement Reconciliation Report Operations
status: active
owner: financial-operations
reviewed: 2026-07-26
audience: operators
---

# Statement Reconciliation Report Operations

Use this bounded preprocessing workflow for retained broker or custodian statement ingestion,
Evidence Vault linkage, reconciliation casework gating, and hash-verifiable JSON/CSV reconciliation
support artifacts. It is not a statement-to-delivery workflow.

The workflow does not perform accounting posting, ledger or close controls, reporting
certification, maker-checker approval, client PDF/XLSX rendering, release, distribution, or
delivery-receipt retention. A `Completed` response proves only that this preprocessing workflow
retained its source and evidence, reported no remaining breaks or open cases, and retained the two
declared reconciliation artifacts.

## Start the workflow

Send authenticated `multipart/form-data` to
`POST /api/workstation/reconciliation/statement-reconciliation-report`. The workstation session
supplies the tenant, company, and operator identity; never send an actor or tenant override in the
form.

Required form fields:

- `file`: non-empty statement file accepted by the statement-connector ingress policy.
- `sourceInstitution`: broker or custodian name.
- `fundAccountId`: Meridian fund-account scope.
- `externalAccountId`: provider-side account scope.
- `periodStart` and `periodEnd`: `YYYY-MM-DD`; end must not precede start.

Optional fields are `sourceKind` (`broker` by default), `connectorId`, `mappingProfileId`, and
`toleranceProfileId`. Connector, mapping, and tolerance policy are part of the workflow identity, so
changing one creates a distinct workflow even when the statement bytes are unchanged.

The start response is `201` when the reconciliation report completes immediately, `202` when
reconciliation or another durable stage remains, or `500` with the retained workflow projection
when a stage fails. Record `workflowId`, `statusRoute`, and `resumeRoute` from the response.

## Interpret status

| Status | Operator action |
| --- | --- |
| `InputRetained`, `Importing`, `RenderingReconciliationReport` | Poll the status route; do not upload a modified file under the same business event. |
| `AwaitingReconciliation` | Follow linked break/case evidence, resolve or govern disposition, then call the resume route. |
| `Failed` | Preserve `failureReason`; correct the named dependency and use the existing resume route. Do not begin an unrelated replacement import. |
| `Completed` | Retrieve every retained artifact and retain the workflow response as preprocessing evidence. Do not treat it as reporting certification or delivery proof. |

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

These files are not governed Reporting artifacts, a certified reporting dataset, a release
authorization, or a recipient package. No endpoint in this workflow promotes them into those
authorities.

## Handoff to governed accounting and reporting

Continue through the existing governed seams; do not create a parallel renderer or delivery path:

1. Keep the statement run, Evidence Vault identity, break/case ids, artifact hashes, and workflow id
   together as preprocessing evidence.
2. Use Operations Continuity and its reconciliation bridge for governed reconciliation casework,
   accounting/ledger evidence, approvals, close readiness, and the committed close/reconciliation
   receipt.
3. Use the canonical Reporting readiness, run certification, governance, and release lifecycle for
   the client report. The Statement Reconciliation Report JSON/CSV output is support evidence only;
   it is not automatically selected as a certified run input.
4. Use the canonical client-document renderer for PDF/XLSX output and secure Reporting distribution
   for recipient grants, delivery jobs, provider receipts, and audited downloads.

There is no automatic transition from this workflow into Operations Continuity, a governed
Reporting run, or secure distribution. Verify each downstream authority independently in
[Governed Reporting Operations](./governed-reporting-operations.md).

## Recovery and escalation

1. Reuse the recorded workflow ID and status route after process restart.
2. If reconciliation is pending, resolve or disposition every linked break/case before resume.
3. If Evidence Vault or storage was unavailable, restore that dependency and call resume; verify the
   statement-run ID did not change.
4. If an artifact hash fails, stop downstream delivery, retain the workflow snapshot and affected
   bytes, and escalate as a durability incident. Do not replace the bytes or edit the manifest.
5. If access is denied, confirm the authenticated company and tenant rather than copying artifacts
   across scopes.

The server data root retains new workflow state under `reporting/statement-reconciliation-report/<workflowId>/`.
Treat `workflow.json`, `input/`, `artifacts/`, and the lock file as service-owned data. Include that
directory in the supported data-root backup and restore drill; operators must not edit it directly.
Persisted legacy `statement-report-*` workflows can resume from the retained
`reporting/statement-to-report/` directory. Canonical callers receive current Statement
Reconciliation Report routes; pre-rename HTTP and service compatibility contracts project their
original route and DTO shape directly over that same workflow.

For break/case handling, see [Reconciliation Operations](./reconciliation-operations.md). For general
receipt and artifact failure rules, see [Verified Outcome Recovery](./verified-outcome-recovery.md).
