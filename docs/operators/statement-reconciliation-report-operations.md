---
title: Statement Reconciliation Report Operations
status: active
owner: financial-operations
reviewed: 2026-07-27
audience: operators
---

# Statement Reconciliation Report Operations

**Status:** active
**Owner:** Financial Operations
**Reviewed:** 2026-07-27

Use this authoritative intake adapter for retained broker or custodian statement ingestion,
server-resolved accounting scope, Evidence Vault linkage, publication into the canonical
reconciliation queue, Operations Continuity linkage, and hash-verifiable JSON/CSV reconciliation
support artifacts. It is not end-to-end statement-to-delivery automation.

The adapter automatically starts or reuses the exact non-closed Operations Continuity workflow and
publishes statement break/case obligations into the existing governed queue. It does not perform
accounting posting, ledger or close controls, reporting certification, maker-checker approval,
client PDF/XLSX rendering, release, distribution, or delivery-receipt retention. A `Completed`
response proves the bounded intake and casework handoff described below; it does not prove any of
those downstream outcomes.

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
An optional exact-scope constraint may be supplied only as the complete set `fundProfileId`,
`ledgerBookId`, `accountingPeriodId`, and `asOfDate`; partial scope input is rejected.

Start probes the canonical scoped identity plus retained pre-rename and pre-scope compatibility
identities. Exactly one matching retained workflow is reused after its input hash and tenant/company
scope are verified; a newly resolved accounting scope is bound atomically when the retained
workflow predates that field. Multiple matching authorities or a conflicting retained identity or
scope fail closed instead of selecting one by search order.

Before retaining the input, the server confirms that `fundAccountId` is active and bound to the
submitted institution and external account, resolves its authenticated tenant/company-owned fund,
selects exactly one primary ledger book, and requires one open ledger period whose dates exactly
match the statement period. A caller-supplied accounting scope is a constraint to verify, not an
authority override. Record the returned `accountingScope` and `operationsWorkflowId` with the
workflow evidence.

The start response is `201` when the reconciliation report completes immediately, `202` when
reconciliation or another durable stage remains, or `500` with the retained workflow projection
when a durable stage fails. Pre-retention account, ledger-book, or period mismatch or ambiguity
returns `409`; a missing intake-authority dependency, missing production statement authority, or
unavailable production authority returns `503` without falling back to local files. A closed or ambiguous exact
Operations workflow or incomplete queue publication fails after retention and returns the retained
`Failed` projection with `500`. Record `workflowId`, `statusRoute`, and `resumeRoute` from the
response when a workflow was retained.

## Use compatibility ingress and fetch schedules

`POST /api/workstation/reconciliation/statement-imports/commit` remains available for existing
statement-connector clients, but it is an adapter over this same workflow. It accepts the same
authenticated tenant/company, account-ownership, exact-period, and optional exact-scope
constraints. A successful `StatementImportCommitResultDto` now also carries
`statementReconciliationReportWorkflowId`, `statementReconciliationReportStatusRoute`,
`operationsWorkflowId`, and `accountingScope`. The route returns an error instead of raw-import
success when the workflow, exact accounting authority, Operations Continuity link, or canonical
casework handoff is unavailable.

Fetch schedules use
`/api/workstation/reconciliation/statement-fetch-schedules`. Saving a schedule requires
`periodStart` and `periodEnd`; the server verifies account ownership, resolves the exact fund,
ledger-book, accounting-period, and as-of scope, and retains that scope with the authenticated
tenant and company. List, run, and delete operations expose only schedules owned by that same
tenant/company. A scheduled run verifies the retained scope before remote fetch and then enters this
same statement reconciliation report workflow. It never commits through a separate raw-import
path.

### Continue the browser close handoff

After a successful file import, the browser shows a **Trusted close handoff** only when the server
returned both the exact `accountingScope` and `operationsWorkflowId`. The handoff displays the
fund profile, ledger book, accounting period, and as-of date retained by the intake authority, then
opens Operations Continuity with those identifiers in the route. Operations Continuity filters the
workflow list to the retained book and period and selects that exact workflow; it does not fall
back to the newest unrelated workflow when the requested workflow is missing or inaccessible.

Use that selected workflow to assign and resolve the projected statement cases, retain correction
and approval evidence, complete the close checklist, and publish the governed close package.
Continue to the Accounting close cockpit for the controller-gated ledger period lock: an Operations
close package is proof of the workflow transition, not by itself proof that the ledger period is
`HardClosed`. If the import result lacks either exact scope or the Operations workflow identity, the
browser keeps the handoff blocked and directs the operator back to reconciliation rather than
displaying a generic close action.

Legacy schedules without retained tenant/company, exact period, or accounting scope cannot run.
Re-create or re-save the known schedule with the same connector/account identity and exact period
so the server can bind current authority; do not edit the schedule file or infer a period from the
last-run timestamp.

## Interpret status

| Status | Operator action |
| --- | --- |
| `InputRetained`, `Importing`, `RenderingReconciliationReport` | Poll the status route; do not upload a modified file under the same business event. |
| `AwaitingReconciliation` | Work the canonical queue entries. Resolve or govern disposition and complete their source/Operations evidence handoff, then call the resume route. If a completed case was governed-reopened, current `retainedArtifacts` are empty and the superseded generation is retained in `artifactHistory`. |
| `Failed` | Preserve `failureReason`; correct the named dependency and use the existing resume route. Do not begin an unrelated replacement import. |
| `Completed` | Retrieve every retained artifact and retain the workflow response, exact accounting scope, Operations workflow link, and queue evidence. Do not treat it as posting, close, reporting certification, or delivery proof. |

The coordinator persists the input before import. In production, it checkpoints a committed import
only after the raw statement, canonical statement records, and statement-run evidence have all
been retained in the same durable reporting authority. The intake authority then starts or reuses
the exact Operations Continuity workflow, attaches stable statement evidence, and projects every
source break/case into the canonical queue with fund, book, period, and as-of scope. A retry after
those checkpoints resumes from retained state rather than repeating the import. Concurrent
processes serialize work on the same content-and-scope identity. Local file-backed compatibility
composition retains source evidence through its existing Evidence Vault bridge and does not satisfy
the production durable-composition readiness gate.

Queue replay validates an already retained destination-scoped break directly. It does not seed an
unscoped compatibility case when a process stops after the queue commit but before the statement
workflow records `operationsWorkflowId`; immutable source or scope conflicts still fail through the
queue's create-replay validation.

### Production persistence and restart authority

Production requires the PostgreSQL statement-reconciliation authority installed by reporting
migration `013_reporting_statement_reconciliation_authority.sql`. Every lookup and write uses the
complete `(tenant_id, company_id, workflow_id, document_key)` key. Document mappings refer to the
existing tenant-scoped, content-addressed `reporting_artifact_blobs`; reads verify retained size and
SHA-256 before workflow code receives bytes.

Reporting deployment readiness remains blocked until the live schema probe verifies
`reporting_statement_reconciliation_documents`,
`reporting_statement_reconciliation_document_revisions`, all six migration-owned document,
revision, and truncate-guard triggers, both UTF-8 composite-identity constraints, and the exact
`reporting-statement-reconciliation-authority:v1` compatibility marker. Composition must also
resolve the concrete `PostgresStatementReconciliationReportAuthorityStore` together with the
statement evidence retainer and workflow bound to that same PostgreSQL artifact authority. The
presence of migration 013 or a durable store registration alone is not production-readiness
evidence.

Uploaded input, statement evidence, and historical artifact generations are immutable mappings.
`workflow.json` and the current artifact mappings may advance, but each version points to immutable
blob bytes and the append-only document revision history preserves superseded hashes. A PostgreSQL
advisory lease serializes a workflow across hosts. The workflow snapshot mapping is written only
after its input, evidence, manifest, and artifact mappings, so a restart cannot observe a checkpoint
that refers to unretained bytes.

Rendering still uses the existing statement workflow and renderer. For production it operates on a
service-owned runtime cache hydrated from verified authority bytes under the exact workflow lease.
Hydration reconstructs that cache exactly and removes unlisted local remnants before continuation;
a file created by an interrupted attempt is not imported into authority on resume. Do not back up,
restore, inspect, or repair this runtime cache as workflow truth.

## Resume and retrieve artifacts

- Read current state with `GET {statusRoute}`.
- Resume a paused or failed workflow with `POST {resumeRoute}`.
- Download an artifact from the `downloadRoute` in each `retainedArtifacts` entry.

Artifact downloads are served only inside the authenticated tenant/company scope. The server hashes
the retained bytes again and refuses delivery if their length or hash differs from the current
descriptor. Only a `Completed` workflow's `retainedArtifacts` authorize download. A governed reopen
removes that current authority before any replacement render.

`artifactGeneration` is the monotonic number of the latest generated set. After a governed reopen,
the service copies every formerly current byte and the exact `manifest.json` into
`artifacts/history/generation-<number>/` with atomic file writes, writes an immutable hash-addressed
archive receipt, and then atomically checkpoints one `artifactHistory` entry in the existing
workflow/status response. That entry preserves the generation's exact descriptors, manifest length
and hash, generation and archive timestamps, and audit evidence. The descriptors' original
`downloadRoute` values are historical audit metadata; do not use them to request superseded bytes.
Current download authority remains exclusively in `retainedArtifacts`.

Resuming the same reopened checkpoint is idempotent: it reuses the existing archive receipt and
does not append another history entry. Once casework is resolved again, rendering creates the next
generation at the normal current paths while all prior generation bytes, manifests, descriptors,
hashes, and receipt evidence remain unchanged. The workflow retains:

- `statement-reconciliation-report.json` with scope, period, run, reconciliation, case lineage, and
  Evidence Vault references;
- `statement-kind-summary.csv` with canonical record counts; and
- `manifest.json` beside the artifacts with descriptors, hashes, and evidence references.

These files are not governed Reporting artifacts, a certified reporting dataset, a release
authorization, or a recipient package. Their retained Operations and queue links carry evidence
forward without changing those downstream authority boundaries.

## Handoff to governed accounting and reporting

Continue through the existing governed seams; do not create a parallel renderer or delivery path:

1. The intake adapter resolves and persists the exact fund, ledger-book, accounting-period, and
   as-of scope before importing the statement.
2. It retains the statement run and Evidence Vault identity, starts or reuses the exact Operations
   Continuity workflow, and publishes each retained source obligation into
   `IReconciliationBreakQueueRepository`.
3. Queue-owned terminal casework synchronizes the disposition back to the statement break/case and
   attaches the same evidence to Operations Continuity. This synchronization does not post, approve,
   close, release, or otherwise advance a human gate.
4. Operations Continuity and its existing reconciliation, accounting, ledger, approval, and close
   services remain authoritative for the committed close/reconciliation receipt.
5. Start a separate governed Reporting run through canonical readiness, certification, governance,
   and release. Statement JSON/CSV is support evidence only and is not a certified run input by
   itself.
6. Capital-account `Pdf`, `Xlsx`, and `ClientPackage` outputs use the same canonical renderer and
   certified-artifact producer. Standalone formats select their exact document from the canonical
   PDF/XLSX pair; `ClientPackage` retains both primary outputs from the same certified manifest.
   Secure Reporting distribution remains authoritative for grants, delivery jobs, provider
   receipts, and audited downloads after release, and it rejects a PDF-only or XLSX-only primary
   subset of a `ClientPackage`.

The automatic transition ends at scoped Operations Continuity and canonical queue handoff. Verify
posting, close, Reporting, release, and distribution independently in
[Governed Reporting Operations](./governed-reporting-operations.md).

## Recovery and escalation

1. Reuse the recorded workflow ID and status route after process restart.
2. If reconciliation is pending, resolve or disposition every linked break/case before resume.
3. If statement authority, Evidence Vault linkage, or storage was unavailable, restore PostgreSQL
   reporting authority and call resume; verify the statement-run ID did not change. A `503` is an
   availability stop, not permission to switch production to a file store.
4. If scope resolution reports no match, ambiguity, or a closed ledger period, repair the
   fund-account ownership, primary ledger-book, or exact ledger-period configuration. Reopen a
   period only through governed accounting close controls; do not choose an approximate period or
   copy an Operations workflow ID from another scope.
5. If intake reports a closed exact Operations workflow, reopen it only through governed close
   controls before retrying the statement handoff.
6. If a current artifact, historical generation, manifest, or archive-receipt hash fails, stop
   downstream delivery, retain the workflow snapshot and affected bytes, and escalate as a
   durability incident. Do not replace the bytes, descriptor, manifest, or archive receipt.
7. If access is denied, confirm the authenticated company and tenant rather than copying artifacts
   across scopes.

Local/development compatibility composition retains workflow state under
`reporting/statement-reconciliation-report/<workflowId>/`. Treat `workflow.json`, `input/`,
`artifacts/`, `artifacts/history/`, authority metadata, and the lock file as service-owned data.
Include that directory in a local-development backup only when that environment intentionally uses
file authority; operators must not edit it directly or restore a historical generation
independently of its workflow snapshot. Production recovery instead restores the PostgreSQL
statement document mappings, append-only revisions, and referenced `reporting_artifact_blobs` as
one coordinated authority. The production data-root runtime workspace is disposable cache and must
not be used as backup or recovery evidence.
The canonical casework handoff is retained separately under
`<DataRoot>/workstation/reconciliation-break-queue.json`. That integrity-validated snapshot also
retains command receipts, audit evidence, and any exact close-scope checkpoint frozen before hard
close. Preserve the adjacent `reconciliation-break-queue-audit.jsonl` legacy migration sidecar and
`reconciliation-break-queue.lock` coordination file when present, but never edit, replay, or
restore either one independently of the current queue snapshot and coordinated recovery point.
Persisted legacy `statement-report-*` workflows can resume from the retained
`reporting/statement-to-report/` directory. Canonical callers receive current Statement
Reconciliation Report routes; pre-rename HTTP and service compatibility contracts project their
original route and DTO shape directly over that same workflow.

For break/case handling, see [Reconciliation Operations](./reconciliation-operations.md). For general
receipt and artifact failure rules, see [Verified Outcome Recovery](./verified-outcome-recovery.md).
