# Governed Reporting Operations

**Status:** active
**Owner:** Accounting / Fund Operations
**Reviewed:** 2026-07-15

This runbook is the production operator procedure for certified reporting runs, hard-close evidence,
immutable reporting state, schedules, access grants, and secure delivery. Contract fields and wire
formats are defined in [Governed Accounting Reporting](../reference/accounting-report-packs.md).

## Scope

- production persistence and authoritative-source prerequisites
- preflight checks for run certification, governance, and distribution
- recovery of a hard close whose reporting evidence handoff is pending
- restart and corruption recovery across PostgreSQL and local reporting state
- HTTP relay, receipt, access-grant, and secret-handling procedures

This runbook does not make legacy report-pack mutation routes authoritative. Those routes remain
retired, and fixture or caller-supplied rows are not a production recovery mechanism.

## Production prerequisites

Before enabling governed reporting, confirm all of the following:

1. Authentication supplies a server-resolved actor, tenant, company, permission set, and reporting
   group membership. Do not use a client-supplied actor or global override to repair access.
2. `MERIDIAN_LEDGER_CONNECTION_STRING` enables the durable ledger, accounting-period, book,
   tenancy, and hard-close source graph. Configure `MERIDIAN_LEDGER_SCHEMA` when it is not `ledger`.
3. `MERIDIAN_REPORTING_CONNECTION_STRING` points to the production reporting database, or its
   documented fallback to the ledger connection is intentional. Configure
   `MERIDIAN_REPORTING_SCHEMA` when it is not `reporting`.
4. The resolved `DataRoot` is durable and backed up. The complete
   `<DataRoot>/workstation/reporting/` directory belongs to the reporting recovery set.
5. Production does not run fixture or in-memory governance in place of the durable ledger,
   fund-structure, reporting, and fund-account dependencies.
6. If external notification is enabled, all relay, recipient-directory, external-access, receipt
   HMAC, and delivery-grant HMAC settings pass the
   [HTTP relay contract](../reference/accounting-report-packs.md#http-relay-contract).

The host applies checksummed Reporting migrations under a schema-scoped advisory lock when the
durable reporting services are resolved. A migration checksum mismatch or unavailable store is a
deployment blocker; do not bypass the migration ledger or edit an applied migration.

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

## Recover a pending hard-close evidence handoff

Hard-close finalization commits the ledger-period state before it retains the exact reporting
reconciliation receipt. If the close commits but the second step fails,
`ReportingCloseEvidenceHandoffException` reports that final-reporting evidence retention is pending.
The period is intentionally left `HardClosed`; Final reporting remains blocked.

Recovery is idempotent:

1. Keep the exact tenant, company, fund, ledger book, period, and close context unchanged.
2. Correct the reporting database or dependency outage that prevented receipt retention.
3. Invoke the same governed **Finalize hard close** command again for that period. When the period is
   already `HardClosed`, the service skips a second close and retries the exact evidence receipt.
4. Re-evaluate Final reporting readiness and confirm the close/reconciliation check now names the
   retained receipt and evidence references.

Do not reopen the period, generate a replacement completion id, hand-insert a receipt, or switch to
a Draft and later relabel it Final. Any of those actions would break the retained close-to-report
lineage.

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

The reporting recovery set crosses two persistence mechanisms:

- the configured PostgreSQL reporting schema
- the entire `<DataRoot>/workstation/reporting/` directory, including the certified run snapshot,
  schedule/release-handoff snapshot, custom templates, starter-kit state, and retained legacy
  history

Back up those locations at one approved recovery point. Encrypt the backup and restrict it as
financial-reporting evidence: the files contain certified rows, parameters, scope, schedules, and
recipient metadata even though plaintext access bearers are not retained.

If a reporting file is unreadable or fails integrity validation:

1. Stop the host so workers cannot advance schedules or deliveries during recovery.
2. Preserve the critical error type and affected path, without copying report contents or recipient
   data into a ticket.
3. Restore the affected state from the coordinated recovery set. Restore only one persistence side
   when retained run, snapshot, package, schedule, handoff, checkpoint, and content-hash evidence
   proves it agrees with the other side; otherwise restore both the PostgreSQL reporting schema and
   complete local reporting directory from the same recovery point. Do not hand-edit JSON, delete
   the corrupt file to force an empty state, or mix files from unrelated recovery points.
4. Restore the original service-account ownership and least-privilege file permissions.
5. Start the host and confirm migrations complete without drift, run and schedule reads succeed,
   immutable run/snapshot/package identities agree, and the transport catalog is truthful.
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
- [Failover and Recovery](./failover-and-recovery.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
