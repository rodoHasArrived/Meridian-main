# Governed Reporting Operations

**Status:** active
**Owner:** Accounting / Fund Operations
**Reviewed:** 2026-07-27

This runbook is the production operator procedure for certified reporting runs, hard-close evidence,
immutable reporting state, schedules, access grants, and secure delivery. Contract fields and wire
formats are defined in [Governed Accounting Reporting](../reference/accounting-report-packs.md).

## Scope

- production persistence and authoritative-source prerequisites
- preflight checks for run certification, governance, and distribution
- recovery of a hard close whose reporting evidence handoff is pending
- restart and corruption recovery across PostgreSQL, auxiliary reporting state, statement
  workflows, and the canonical reconciliation queue
- HTTP relay, receipt, access-grant, and secret-handling procedures

This runbook does not make legacy report-pack mutation routes authoritative. Those routes remain
retired, and fixture or caller-supplied rows are not a production recovery mechanism.

## Reporting capability gate

`GET /api/workstation/reporting` is the independent Reporting workspace capability. It returns a
typed `WorkstationReportingPayload`; it does not reuse an Accounting payload or a fixture fallback.
The route returns `503 Service Unavailable` unless the deployment capability proves every component
below:

- PostgreSQL reporting governance and its maker-checker coordinator
- the exact PostgreSQL accounting-period consistency gate shared by Final release and governed reopen
- PostgreSQL artifact bytes, catalog, access audit, and artifact vault
- PostgreSQL close/reconciliation evidence and its immutable database controls
- one integrity-verified reconciliation queue shared by statement casework, Operations Continuity,
  hard close, and Final certification
- PostgreSQL certified run manifests and run audit
- PostgreSQL reporting schedules
- running server-owned schedule and secure-delivery workers with valid worker options
- PostgreSQL access grants, delivery jobs, and retained receipts
- atomic PostgreSQL grant-use and download-receipt accounting for every released package artifact
- an exact-scope recipient destination directory
- the canonical client PDF/XLSX renderer, deterministic certified-artifact producer, and durable
  checkpoint-bound ledger presentation source
- a complete reporting schema probe plus the current process's successful checksummed-migration
  receipt
- an exact application/schema compatibility marker for migration
  `012_reporting_access_grant_artifact_consumption.sql`; a missing or mismatched marker blocks
  delivery
- successful ledger, fund-account, and fund-structure migration receipts for the authoritative
  source graph

The same capability is exposed in a successful Reporting payload as `deploymentCapability`,
including a top-level durable-reconciliation-evidence signal. The schema probe verifies required
tables, immutable-control trigger bindings, operational columns, the checksummed migration-ledger
key, and immediate predicate-compatible unique/idempotency keys. Local file run, schedule,
custom-template, and starter-kit stores are development compatibility only and deliberately leave
this gate blocked. Legacy file workflow and delivery-history repositories are never registered by
the default host composition and are accepted only when an explicit compatibility caller supplies
them. Do not infer
Reporting availability from Accounting workspace health, a configured connection string alone, a
rendered preview, or the presence of retained JSON files.

## Production prerequisites

Before enabling governed reporting, confirm all of the following:

1. Authentication supplies a server-resolved actor, tenant, company, permission set, and reporting
   group membership. Do not use a client-supplied actor or global override to repair access.
2. `MERIDIAN_LEDGER_CONNECTION_STRING` enables the durable ledger, accounting-period, book,
   tenancy, and hard-close source graph. Configure `MERIDIAN_LEDGER_SCHEMA` when it is not `ledger`.
3. `MERIDIAN_REPORTING_CONNECTION_STRING` points to the production reporting database, or its
   documented fallback to the ledger connection is intentional. Configure
   `MERIDIAN_REPORTING_SCHEMA` when it is not `reporting`.
4. An authenticated `GET /api/workstation/reporting` returns `200` with
   `deploymentCapability.isReady = true`. Preserve its component summaries with deployment
   evidence. A `503` is a deployment blocker, not an empty Reporting workspace.
5. The resolved `DataRoot` is durable and backed up. Include any explicitly enabled
   local/development `<DataRoot>/workstation/reporting/` compatibility state and any retained
   `<DataRoot>/reporting/statement-reconciliation-report/` preprocessing workflows, plus the
   `<DataRoot>/workstation/reconciliation-break-queue.json` snapshot and adjacent queue sidecars,
   in the coordinated reporting recovery set.
6. Production does not run fixture or in-memory governance, or file-backed run, schedule,
   custom-template, starter-kit, workflow, or delivery compatibility stores, in place of the
   durable ledger, fund-structure, reporting, and fund-account dependencies.
7. If external notification is enabled, all relay, recipient-directory, external-access, receipt
   HMAC, and delivery-grant HMAC settings pass the
   [HTTP relay contract](../reference/accounting-report-packs.md#http-relay-contract).

Production service registration fails when neither `MERIDIAN_REPORTING_CONNECTION_STRING` nor its
documented ledger fallback is configured. With a database configured, the host applies checksummed
Reporting migrations under a schema-scoped advisory lock before it starts the HTTP listener or
constructs hosted workers. Startup also discards process cache and integrity-reloads the canonical
reconciliation queue before accepting Reporting work. An unreachable database, migration failure,
checksum mismatch, or corrupt queue snapshot fails
startup; do not bypass the migration ledger or edit an applied migration.

### Migration 012 application-version barrier

Migration `012_reporting_access_grant_artifact_consumption.sql` changes the access-grant
consumption write contract. It is not safe for DB-first rolling deployment while any pre-012
process can exchange or consume reporting grants. The database's retained `NULL`-to-`NULL`
consumption fence rejects a pre-012 use-count update instead of losing artifact identity, and the
insert fence rejects a new grant that omits tracked artifact state. Those failures are safety
stops—not mixed-version compatibility. Migration `012` also replaces the pre-012 access-grant
trigger name with a versioned trigger, so the pre-012 deployment probe reports the upgraded schema
as incomplete instead of returning a legacy green readiness result.

Use this sequence:

1. Stop new reporting delivery and drain active delivery work to an operator-reviewed boundary.
2. Drain and stop every pre-012 host. Confirm no prior binary can reach the reporting database.
3. Start one 012-aware host and let its checksummed migration runner apply `012`; do not apply the
   SQL manually or in advance of the binary cutover.
4. Confirm `/api/workstation/reporting` reports both `application-schema-compatibility` and
   `delivery` ready. The compatibility component verifies the exact migration filename/checksum
   pair and its consumed-artifact column, trigger, and constraint shape.
5. Start the remaining 012-aware hosts, then resume delivery with a controlled released-package
   canary.

If cutover fails after `012` is applied, keep delivery stopped. Do not restart a pre-012 binary
against the upgraded schema. Deploy a corrected 012-aware binary or execute the approved
coordinated database restore procedure.

After migrations succeed, a missing recipient binding, client-document dependency, schema-probe
requirement, or other noncanonical authority leaves the production reporting lifecycle
`Required/NotReady`. Reporting reads and mutations, including run creation and schedule commands,
return `503`; the host does not fall back to file authority. Local/development can start on file
run/schedule compatibility stores with a `Degraded` reporting lifecycle, but the same authoritative
Reporting routes stay blocked until every deployment component is ready.

## Preflight

Use an authenticated operator in the intended tenant and company scope.

1. Open the Reporting workspace and confirm the canonical run-detail surface loads without fixture
   banners or service-unavailable state.
2. Evaluate `POST /api/fund-structure/reporting/runs/readiness` for the exact template, fund, book,
   period, as-of date, basis, currency, consolidation, output, and finality to be used. Preserve the
   returned blocking reasons and evidence references with the deployment evidence.
3. Confirm Draft and Final eligibility separately. A Draft-ready result is not proof that a Final
   report can be certified or released.
4. For Final, verify the period is `HardClosed`, the exact close/reconciliation receipt is retained,
   the evidence appendix is selected, the presentation currency needs no unavailable FX source,
   and the exact fund/book/period/as-of source contains ledger rows.
5. Read `/api/fund-structure/reporting/distribution/transports`. Treat `isInfrastructureReady`,
   `isReady`, and the disabled reason codes as the transport truth; do not infer availability from
   a retained delivery-mode label.
6. If external delivery is enabled, use an approved released canary package and controlled recipient
   binding. Confirm one durable job, a non-secret grant record, relay acceptance with a bounded
   provider message id, and an authenticated terminal receipt. Do not paste the one-time link into
   logs, tickets, screenshots, or retained notes.

## Canonical statement-to-delivery handoff

The [Statement Reconciliation Report](./statement-reconciliation-report-operations.md) is the
authoritative intake adapter into Meridian's existing casework and close spine. Its JSON/CSV output
is reconciliation support evidence, not a governed Reporting run. Keep the full chain on these
existing authorities:

1. Before retaining input, the server verifies the active fund account and statement source, fund
   tenancy, one primary ledger book, one exact matching open accounting period, and its period-end
   as-of date. Missing, ambiguous, closed, or mismatched scope fails closed.
2. The coordinator retains the statement, committed import, and Evidence Vault lineage. The
   `IStatementReconciliationIntakeAuthority` then starts or reuses the one non-closed Operations
   Continuity workflow for the exact fund-account, ledger-book, and accounting-period scope,
   attaches stable statement evidence, and publishes every source break/case obligation into the
   canonical reconciliation queue.
3. Governed queue commands remain the casework authority. Terminal dispositions synchronize back to
   the statement-owned break/case and append the same evidence to Operations Continuity without
   advancing posting, approval, or close gates. The statement workflow remains
   `AwaitingReconciliation` until every source obligation has exactly one scoped queue item and its
   source/Operations handoff is complete.
4. Accounting posting, ledger evidence, approvals, close readiness, and the committed
   close/reconciliation receipt remain owned by Operations Continuity and its established bridges.
5. Start a separate governed Reporting run through the canonical readiness and certification
   services. Final certification requires the committed close/reconciliation receipt; a completed
   statement workflow does not substitute for it.
6. For a capital-account `Pdf`, `Xlsx`, or `ClientPackage`, use the deterministic
   certified-artifact producer and canonical client-document renderer. The verified
   checkpoint-bound `LedgerFinancialReportPack` passes through the existing
   `LedgerClientReportExportService`/`FinancialReportDocumentRenderer` seam once; do not reconstruct
   partners-capital tables in a Reporting-specific renderer. A standalone format retains only its
   corresponding canonical document. `ClientPackage` must retain exactly one `<runId>.pdf` and one
   `<runId>.xlsx`; release fails closed if either primary artifact is missing or duplicated.
7. After independent maker-checker approval and release, use secure Reporting distribution. Retain
   the durable delivery job, scoped grant state, provider receipt, and audited download receipt as
   applicable. A `ClientPackage` distribution must select both released primary documents; it may
   include additional released artifacts but cannot deliver a PDF-only or XLSX-only subset.

Statement workflow `Completed` proves the bounded intake, reconciliation-casework handoff, and
hash-verified support artifacts. It does not mean posted, closed, certified, approved, released, or
delivered.

## Recover a pending hard-close evidence handoff

Hard-close finalization commits the ledger-period state before it retains the exact reporting
reconciliation receipt. If the close commits but the second step fails,
`ReportingCloseEvidenceHandoffException` reports that final-reporting evidence retention is pending.
The period is intentionally left `HardClosed`; Final reporting remains blocked.

Recovery is idempotent:

1. Keep the exact tenant, company, fund, ledger book, period, and close context unchanged.
2. Preserve `<DataRoot>/workstation/reconciliation-break-queue.json`. Its retained close-scope
   record contains the immutable queue head and checkpoint hash frozen before the ledger commit.
   Do not clear the `Closing` state, edit a disposition, or reconstruct the head from current queue
   rows.
3. Restore authoritative ledger reads and correct any reporting dependency outage that prevented
   checkpoint sealing or receipt retention.
4. Invoke the same governed **Finalize hard close** command again for that period. Recovery acquires
   the exclusive `reconciliation-break-queue.lock` fence, rotates the retained lease token, and
   preserves and re-hashes the exact frozen scope/items; it never rereads the live queue head.
5. The bridge rereads the authoritative ledger while holding that lease:
   - `HardClosed` seals or reuses the exact checkpoint and retries the reporting-evidence receipt.
   - A confirmed non-hard-closed postcondition explicitly abandons the pre-commit freeze, durably
     reopens scoped casework, and surfaces the original close failure.
   - An unreadable or ambiguous ledger outcome leaves `Closing` retained. Restore ledger authority
     and retry; do not infer whether commit occurred.
6. Re-evaluate Final reporting readiness and confirm the close/reconciliation check now names the
   retained receipt and evidence references.

Dispose and process death deliberately leave an ambiguous `Closing` freeze in place. Do not delete
it, reopen a `HardClosed` period, generate a replacement completion id, hand-insert a receipt, or
switch to a Draft and later relabel it Final. Any of those actions would break the retained
close-to-report lineage.

## Delivery and receipt triage

- `Queued`, `Dispatching`, and `RetryScheduled` are active durable states. Let the hosted worker use
  the retained lease, attempt number, idempotency key, and access-grant id.
- `Sent` means the transport accepted the notification; it does not prove recipient delivery.
  `Delivered` requires retained delivery evidence.
- `Blocked` is not permission to create a replacement notification. Inspect the exact disabled or
  last-error code first. An unknown relay outcome can retain a live grant and must be reconciled by
  the same provider callback or exact retry.
- `RELAY_OUTCOME_UNKNOWN`, `PROVIDER_MESSAGE_ID_INVALID`, a relay timeout, or cancellation after the
  provider call may mean the notification was accepted. Do not issue a new distribution id or a new
  grant; the worker reuses the same attempt identity and credential.
- A verified terminal `Bounced` or `Rejected` provider receipt is committed first to the append-only
  receipt inbox and moves the job to `Failed`; grant exchange then fails closed. Meridian revokes the
  linked grant idempotently. If revocation cannot converge, the callback returns retryable `503` and
  the hosted reconciliation worker retries from the retained receipt. Do not delete/repost the job
  or manually mark it delivered. Retryable attempt-level `Failed` evidence such as
  `RELAY_OUTCOME_UNKNOWN` is not a terminal bounce and does not revoke a link that later succeeds.
- Artifact-integrity failures return an unavailable response and append evidence. Quarantine the
  affected run/package from release, download, and distribution until the authoritative reporting
  state is recovered; never regenerate bytes under the same artifact id.

The provider callback must use the exact job-specific callback path, provider message reference,
timestamp, and signature described in the reference contract. Query parameters and Authorization
headers are deliberately rejected on that endpoint.

## Backup and recovery

The coordinated reporting recovery set crosses the production authority and auxiliary local state:

- the configured PostgreSQL reporting schema, including governance, artifact, run, schedule,
  access-grant, delivery-job, and receipt state
- any explicitly enabled local/development `<DataRoot>/workstation/reporting/` compatibility
  directory, including custom templates, starter-kit state, and retained compatibility history;
  production does not register those file repositories
- `<DataRoot>/reporting/statement-reconciliation-report/` when statement preprocessing workflows
  are in use
- `<DataRoot>/workstation/reconciliation-break-queue.json`, whose current integrity-validated
  snapshot includes queue items, audit and idempotency receipts, plus the durable `Closing` or
  `HardClosed` close-scope checkpoint
- the adjacent `reconciliation-break-queue-audit.jsonl` legacy migration sidecar and
  `reconciliation-break-queue.lock` service-owned mutation-coordination file when present; neither
  is a substitute for the current queue snapshot and neither may be edited or replayed independently

Production run and schedule authority does not come from `FileReportingRunStore` or
`FileReportingScheduleStore`. Do not restore their local snapshots as a substitute for the
PostgreSQL `reporting_run_snapshots` and `reporting_schedule_snapshots` state.

Back up those locations at one approved recovery point. Encrypt the backup and restrict it as
financial-reporting evidence: the files contain certified rows, parameters, scope, schedules, and
recipient metadata even though plaintext access bearers are not retained.

If a reporting file is unreadable or fails integrity validation:

1. Stop the host so workers cannot advance schedules or deliveries during recovery.
2. Preserve the critical error type and affected path, without copying report contents or recipient
   data into a ticket.
3. Restore the affected state from the coordinated recovery set. Restore only one persistence
   component when retained run, package, schedule, handoff, checkpoint, and content-hash evidence
   proves it agrees with every other component; otherwise restore the PostgreSQL schema and
   applicable local directories from the same recovery point. Do not hand-edit JSON, delete a
   corrupt file to force an empty state, or mix files from unrelated recovery points.
4. Restore the original service-account ownership and least-privilege file permissions.
5. Start the host and confirm migrations complete without drift, PostgreSQL run and schedule reads
   succeed, immutable run/package identities agree, `/api/workstation/reporting` reports a ready
   deployment capability, and the transport catalog is truthful.
6. Re-evaluate readiness for one retained run and observe one controlled schedule or delivery retry
   before reopening normal report operations.

There is no supported reporting-state repair endpoint or reconciliation CLI. If no coordinated
known-good recovery set exists, keep material reporting operations stopped and escalate rather than
constructing replacement state.

## Pre-hardening v1 state

Migration `008` and the local file-store v2 envelopes distinguish committed pre-hardening state
from corruption. Before deploying them, take the coordinated PostgreSQL and reporting-directory
backup described above. Existing PostgreSQL governance rows are stamped v1; an older application
that omits an explicit format version is rejected. Structurally valid unversioned run and schedule
files can be inventoried as v1, but legacy schedules are frozen and never enter the hosted worker.
This compatibility procedure does not make file-backed run or schedule state a production
authority.

When a verified-legacy/read-only exception is reported:

1. Preserve the exact database row or file payload and its reported raw/canonical hashes. Do not
   add missing fields, recompute its authority, or relabel it v2.
2. Use only the approved recovery authority for the exact tenant, organization, and nullable
   company scope. Company-scoped operators cannot export company-null or cross-company evidence;
   tenant administrators do not bypass those boundaries.
3. Retain or archive the v1 payload with the authorized actor, reason, timestamp, and integrity
   receipt. A mixed store must preserve every v2 entry byte-for-byte and archive only legacy entries.
4. Create a newly certified v2 run, or recapture a schedule with canonical parameters, immutable
   access policy, execution principal, and explicit typed delivery targets. Do not promote the v1
   object or reuse its lifecycle approval as current authority.

The repository currently has no end-user API or CLI for legacy inventory/export/archive. Escalate
the controlled recovery operation to the reporting storage owner; do not edit PostgreSQL or JSON by
hand. A checksum, archived-payload, or raw/index tenant/organization/company mismatch is corruption
and must follow coordinated restore, not the legacy recertification path.

## Secret handling and rotation

- Store the relay bearer, receipt HMAC secret, and delivery-grant HMAC secret in the deployment
  secret manager. Do not put them in repository files, local reporting JSON, logs, or support
  packets.
- Keep both HMAC secrets stable across in-flight jobs and retries. The receipt secret authenticates
  callbacks; the grant secret deterministically recreates the same recipient credential after a
  crash without storing plaintext.
- The current host accepts one active receipt secret and one active grant secret. It has no dual-key
  rollover window. For rotation, use a maintenance window: stop new external queueing, reconcile all
  unknown/retry states, allow in-flight callbacks to settle, revoke or expire outstanding grants as
  policy requires, update the relay and host together, restart, and run a controlled canary.
- Treat a recipient destination as deployment-owned identity data. It may be an address or provider
  handle, but it must not contain `token=`, `#token`, or bearer-shaped material.

## Current residual limitations (P1)

- Statement intake automatically starts or reuses the exact Operations Continuity workflow and
  publishes scoped reconciliation obligations, but posting, human close gates, certified Reporting
  runs, release, and delivery remain separate governed actions.
- Coordinated PostgreSQL/file backup drills and state-reconciliation tooling are manual; there is no
  automated repair command.
- Receipt and delivery-grant HMAC rotation is single-key and maintenance-window based.
- Dedicated Evidence Vault and Internal Route transport adapters are not registered; those retained
  mode labels resolve through the local secure-portal path unless a future server capability says
  otherwise.
- Worker SLOs, alerts, retention/purge policy, and high-availability leadership remain
  deployment-owned follow-up work. Do not represent them as built-in reporting controls.

## Related references

- [Governed Accounting Reporting](../reference/accounting-report-packs.md)
- [Database Schema](../reference/database-schema.md)
- [Ledger Journal Store](../reference/ledger-journal-store.md)
- [Reconciliation Operations](./reconciliation-operations.md)
- [Statement Reconciliation Report Operations](./statement-reconciliation-report-operations.md)
- [Failover and Recovery](./failover-and-recovery.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
