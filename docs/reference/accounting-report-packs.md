# Governed Accounting Reporting

**Owner:** Accounting / Fund Operations
**Scope:** Certified reporting runs, immutable artifacts, lifecycle governance, scheduling, and distribution
**Status:** Canonical production contract
**Reviewed:** 2026-07-15

---

## Purpose

Meridian reporting turns an exact, server-owned accounting view into a retained report package. A
report is not releasable merely because rendering succeeded. The canonical path certifies its
point-in-time inputs, creates a governed `Draft`, and advances through independent review before
the immutable package can be distributed.

The authoritative lifecycle is:

```text
Draft -> Validated -> InReview -> Approved -> Released
```

`Released` is terminal. A correction to a released report requires a retained restatement request,
independent approval, and a newly certified revision in the same series. The predecessor is never
rewritten.

## Certified run contract

`ReportingRunRequestDto` carries the shared browser/WPF/server parameter envelope:

- approved template name and version
- fund plus entity, portfolio, or investor scope
- reporting period and as-of date
- ledger book id or code
- accounting basis and presentation currency
- consolidation level
- PDF, XLSX, CSV, or Evidence Vault output
- Draft or Final finality
- supporting-schedule and evidence-appendix selections
- approved template parameters and dimensional filters

The HTTP boundary rejects caller-supplied dataset rows and caller-controlled restatement flags for
governed runs. The server normalizes every parameter, resolves the exact ledger book and accounting
period, validates fund tenancy and historical fund structure, and captures durable journal rows at
the end of the selected `asOfDate` in UTC (`23:59:59.9999999Z`). Fund structure is resolved at that
same UTC cutoff. Future postings and future-effective structure are excluded.

The resulting certification retains:

- immutable tenant, organization, company, fund, book, period, and access-policy snapshots
- normalized parameter JSON plus SHA-256 hash
- authoritative source checkpoint id/hash, highest journal sequence, and exact row count
- exact close/reconciliation checkpoint id/hash and evidence references
- deterministic snapshot id/hash
- server-owned readiness checks and their evidence hash

Presentation-currency conversion fails closed until an authoritative FX snapshot is available.
Final output also requires the exact canonical `HardClosed` period state and an evidence appendix;
`SoftClosed` is not sufficient.

## Readiness

`POST /api/fund-structure/reporting/runs/readiness` evaluates the same request contract used by run
creation. It returns `canGenerateDraft`, `canGenerateFinal`, normalized parameters, individual
checks, blocking reasons, and an evidence hash.

Readiness is finality-aware. A final-only issue can leave a Draft eligible while blocking Final.
Missing authoritative ledger data, invalid scope, an unavailable dependency, unresolved required
template parameters, absent exact reconciliation/close evidence, or missing required evidence
blocks the applicable finality. The run command repeats certification and never trusts an earlier
client-visible readiness result as authority.

The exact final-reporting reconciliation receipt is retained as part of hard-close finalization.
If the ledger period commits as `HardClosed` but receipt retention fails, Final remains blocked and
the same hard-close finalization must be retried idempotently. The period must not be reopened and a
receipt must not be fabricated. See [Governed Reporting Operations](../operators/governed-reporting-operations.md#recover-a-pending-hard-close-evidence-handoff).

## Governance and maker-checker

The server maps authenticated permissions to narrow reporting capabilities:

| Workstation permission | Governed capabilities |
| --- | --- |
| `ManageReporting` | create, execute, validate, submit, request restatement |
| `ApproveReporting` | approve run, approve restatement |
| `DeliverReporting` | release run and manage distribution |

Maker-checker rules remain effective for administrators:

- the creator/preparer cannot approve the run
- the approver cannot release the same run
- the restatement requester cannot approve that request
- tenant, company, and immutable access-policy checks cannot be bypassed

Private and restricted reports retain their exact owner, owner-access flag, and typed user, group,
and company principal snapshot. Principal kind is part of authority: a user id that happens to equal
a group id does not satisfy that group grant. Reviewers and release operators must have been
included in that immutable audience and must hold the relevant permission. No later role or group
change silently widens a retained report.

Canonical run responses include normalized parameters and server-owned action availability with a
blocked reason and expected version. Browser and WPF clients render those decisions rather than
inferring authorization from lifecycle labels.

## Immutable artifacts

After a certified manifest succeeds, Meridian deterministically renders and retains:

- the canonical retained manifest
- one primary PDF, XLSX, CSV, or Evidence Vault artifact
- an exact certified-source CSV schedule
- optional supporting schedules and evidence appendix
- declared report-writer grid artifacts

Primary outputs contain certified ledger values, not only metadata. Artifact declarations must
exactly match produced bytes. Bytes are content-addressed and stored once; catalog metadata binds
each artifact to the immutable run scope, revision, snapshot, manifest hash, content hash, byte
length, filename, and content type.

Retention, verification, access, denial, and integrity failures append audit evidence. Only a run
certified with `Final` finality can be released; a Draft-finality output may be inspected and
validated, but it cannot bypass final-only readiness or distribution controls. Release rereads the
retained canonical manifest and every declared artifact, requires their descriptor sets, scope,
hashes, and byte lengths to agree exactly, and verifies exact bytes before writing the release receipt.
Downloads and distribution repeat the integrity check and fail closed if catalog state, hashes,
sizes, scope, or bytes disagree.

## Secure distribution

Only a `Released` run can be queued or granted to a recipient. Distribution uses durable jobs and
server-reported transport capabilities. `secure-portal` is always local and authenticated. The
optional HTTP notification relay is enabled only when its endpoint, bearer credential, external
access base URI, receipt HMAC secret, delivery-grant HMAC secret, and an exact tenant/company/
principal/transport recipient-directory binding are all configured. A caller-supplied destination
is only an assertion and must equal that server-resolved binding.

The production adapters in this contract are `secure-portal` and `http-relay`. Retained delivery
mode labels such as Evidence Vault or Internal Route are compatibility metadata; they do not name
additional transport adapters. Operators and clients must use the transport capability catalog as
the authority for what can actually dispatch.

Delivery state is retained as `Queued`, `Dispatching`, `RetryScheduled`, `Sent`, `Delivered`,
`Blocked`, or `Failed`. Leases prevent double dispatch, payload-based idempotency prevents a reused
distribution id from changing content, transient failures use bounded retry/backoff, and provider
receipts are authenticated and append-only.

Recipient access grants:

- use an opaque bearer; direct grants are random, while relay-attempt grants are reproducibly
  derived from a deployment-owned HMAC key and durable attempt identity so crash recovery can
  recreate the same link without persisting plaintext; only the SHA-256 hash is stored
- bind tenant, governed run, package, audience, artifacts, expiry, and maximum uses
- can be revoked without changing their immutable authority scope
- return the bearer once in a URL fragment, never in a query string or retained response history
- verify release, catalog binding, immutable scope, and exact bytes before consuming a use
- exchange the bearer in a no-store POST body for one audited exact-byte download

Grant list/detail responses never return the bearer. Legacy query-token package and artifact routes
return `410 Gone`.

### HTTP relay contract

The relay endpoint must be HTTPS outside loopback development and cannot contain user information,
a query, or a fragment. The external access base URI has the same token-free HTTPS constraint. The
relay bearer credential must contain at least 32 characters. Both HMAC secrets are base64 text that
decodes to at least 32 bytes.

`MERIDIAN_REPORTING_RECIPIENT_DESTINATIONS_JSON` is a JSON array of exact bindings. There is no
tenant, company, principal, principal-kind, or transport fallback, and duplicate exact keys fail
host startup. `principalKind` must be `User`, `Group`, or `Company`; omitting it retains the legacy
`User` default, so production configuration should always supply it explicitly.

```json
[
  {
    "tenantId": "tenant-alpha",
    "companyId": "company-alpha",
    "principalId": "investor-relations",
    "principalKind": "Group",
    "transportId": "http-relay",
    "destination": "recipient-directory-handle"
  }
]
```

Meridian sends an HTTPS `POST` with `Authorization: Bearer <configured credential>`, an
`Idempotency-Key` header, and this camel-case JSON body:

| Field | Contract |
| --- | --- |
| `tenantId` | Immutable tenant scope. |
| `packageId` | Content-derived released-package identity. |
| `destination` | Server-resolved exact recipient-directory value. |
| `subject`, `body` | Token-free notification content. |
| `recipientAccessUri` | One-time fragment-bearer link; the relay must not log, persist beyond delivery need, rewrite, or move it into a query string. |
| `idempotencyKey` | Stable provider-attempt key; an exact replay must not send a second notification. |
| `deliveryJobId` | Durable Meridian job correlation. |
| `receiptCallbackPath` | Relative callback path for this transport and job. |

A successful relay response is a bounded JSON object with a non-empty `providerMessageId` of at
most 256 characters and an optional `code` of at most 128 characters. A missing, oversized, or
invalid response leaves the provider outcome unknown; Meridian retains and replays the same attempt
idempotency key and access credential rather than issuing a second one.

Provider callbacks post `SecureReportingDeliveryReceiptCommand` to the advertised callback path.
The body requires `providerEventId`, `occurredAtUtc`, and a `providerReference` equal to the retained
`providerMessageId`. The JSON `kind` field uses the numeric enum value: `Accepted` is `0`, `Sent` is
`2`, `Delivered` is `3`, `Bounced` is `6`, and `Rejected` is `7`; all other values are rejected. The
callback supplies Unix seconds in
`X-Meridian-Reporting-Timestamp` and a lowercase
hex HMAC-SHA256 in `X-Meridian-Reporting-Signature` (an optional `sha256=` prefix is accepted).
Timestamps outside five minutes of host UTC are rejected.

The signed UTF-8 message concatenates these fields in order: timestamp, trimmed transport id,
trimmed job id, trimmed provider event id, numeric receipt-kind value, `occurredAtUtc` normalized to
UTC round-trip format, trimmed provider reference, trimmed evidence reference, and trimmed detail.
Each non-null value is framed as `<UTF-8 byte length>:<value>`; null is `-1:`. The relay computes
HMAC-SHA256 over that exact concatenation with the decoded receipt secret. Query parameters and an
`Authorization` header are rejected on the callback route.

## Scheduling

Active schedules must retain the complete canonical run parameters and immutable tenant/company
ownership. The server worker leases due schedules, certifies a new run, and creates a
`Succeeded/Draft` governed handoff. It does not approve or release on behalf of a human.

Each active delivery target names an explicit `recipientPrincipalId` and
`recipientPrincipalKind` (`User`, `Group`, or `Company`). Upsert validates that typed target against
the immutable template access policy and, for external delivery, the exact server-owned recipient
directory binding before committing the schedule. The server also resolves and persists the
effective delivery mode at upsert, so a later catalog change cannot turn an approved internal
target into external egress. The ordered target declarations are retained
under `deliveryTargetsSnapshotHash`; the release handoff binds the same kind, id, access-policy
hash, and target hash. Missing, ambiguous, changed, or unconfigured targets fail before execution
and are never replaced with the hosted worker identity. Editing the target snapshot discards old
pending handoffs immediately while retaining already-enqueued immutable audit history; worker
discovery and final queue marking both rebind a pending handoff to the current snapshot.

After independent validation, approval, and release, the durable handoff queues distribution once.
Retries and restarts recover the same run and handoff without creating duplicate manifests or
delivery jobs. A failing schedule remains truthful and retryable without preventing other tenants'
due work from continuing. Public `POST /schedules/run-due` is retired; due execution is internal.

## Canonical routes

### Runs and governance

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/fund-structure/reporting/runs/readiness` | Normalize parameters and evaluate blocking readiness. |
| `POST` | `/api/fund-structure/reporting/runs` | Certify, render, retain, and create the governed Draft. |
| `GET` | `/api/fund-structure/reporting/runs/{runId}` | Read one tenant/access-filtered governed run. |
| `POST` | `/api/fund-structure/reporting/runs/{runId}/govern` | Recover governance for an already completed certified manifest. |
| `POST` | `/api/fund-structure/reporting/runs/{runId}/validate` | Validate with `expectedVersion`. |
| `POST` | `/api/fund-structure/reporting/runs/{runId}/submit` | Submit for independent review. |
| `POST` | `/api/fund-structure/reporting/runs/{runId}/approve` | Record independent approval and decision note. |
| `POST` | `/api/fund-structure/reporting/runs/{runId}/release` | Verify retained bytes and release. |
| `GET` | `/api/fund-structure/reporting/runs/series/{seriesId}` | Read revision and restatement history. |
| `GET/POST` | `/api/fund-structure/reporting/runs/{runId}/restatement-requests` | Discover or request restatements. |
| `GET` | `/api/fund-structure/reporting/runs/restatement-requests/{requestId}` | Read one accessible request. |
| `POST` | `/api/fund-structure/reporting/runs/restatement-requests/{requestId}/approve` | Approve and create a newly certified Draft revision. |

### Distribution

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/fund-structure/reporting/distribution/transports` | Caller-specific transport and action capability catalog. |
| `POST` | `/api/fund-structure/reporting/distribution/deliveries` | Queue a release-gated durable delivery. |
| `GET` | `/api/fund-structure/reporting/distribution/deliveries/{jobId}` | Read one delivery and receipts. |
| `GET` | `/api/fund-structure/reporting/distribution/packages/{runId}/deliveries` | List delivery history for a run. |
| `POST` | `/api/fund-structure/reporting/distribution/access-grants` | Issue one scoped recipient grant. |
| `GET` | `/api/fund-structure/reporting/distribution/access-grants/{grantId}` | Read non-secret grant state. |
| `GET` | `/api/fund-structure/reporting/distribution/packages/{runId}/access-grants` | List non-secret grant history. |
| `POST` | `/api/fund-structure/reporting/distribution/access-grants/{grantId}/revoke` | Revoke recipient access. |
| `GET` | `/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}` | Authenticated, audited exact-byte download. |
| `GET` | `/portal/reporting/secure/packages/{runId}` | Authenticated portal entry for a released package. |
| `GET/POST` | `/portal/reporting/access-grants/{grantId}/exchange` | Token-safe recipient landing and body exchange. |
| `POST` | `/hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts` | Authenticate and retain an immutable provider receipt. |

### Schedules

| Method | Route | Purpose |
| --- | --- | --- |
| `GET/POST` | `/api/fund-structure/reporting/schedules` | List or upsert tenant-bound schedules. |
| `POST` | `/api/fund-structure/reporting/schedules/{scheduleId}/pause` | Pause execution. |
| `POST` | `/api/fund-structure/reporting/schedules/{scheduleId}/resume` | Resume only when canonical parameters are complete. |
| `POST` | `/api/fund-structure/reporting/schedules/{scheduleId}/run` | Create an immediate governed Draft handoff. |

## Persistence and configuration

Production reporting spans PostgreSQL authority and two integrity-validated files under the
resolved `DataRoot`. The state owners are:

| State | Authoritative location |
| --- | --- |
| Immutable bytes/catalog, artifact audit, governance/restatements/audit, close/reconciliation receipts, grants, delivery jobs, and provider receipts | PostgreSQL reporting schema. |
| Certified orchestration manifests and their run audit snapshot | `<DataRoot>/workstation/reporting/runs/reporting-runs.json` (`FileReportingRunStore`, schema `meridian.reporting.run-store.v2`). |
| Schedules and restart-safe release/delivery handoffs | `<DataRoot>/workstation/reporting/reporting-schedules.json` (`FileReportingScheduleStore`). |
| Custom template and starter-kit state | `<DataRoot>/workstation/reporting/report-templates.json` and `reporting-starter-kit.json`. |
| Legacy report-pack workflow and delivery records | `<DataRoot>/workstation/reporting/report-pack-workflows.json` and `report-pack-deliveries.json`; historical compatibility only, never release or delivery authority. |

PostgreSQL reporting authority uses:

- `MERIDIAN_REPORTING_CONNECTION_STRING` (falls back to `MERIDIAN_LEDGER_CONNECTION_STRING`)
- `MERIDIAN_REPORTING_SCHEMA` (defaults to `reporting`)

Authoritative certification also requires the durable ledger, tenancy registry, and fund-structure
services. In normal production composition the ledger is enabled by
`MERIDIAN_LEDGER_CONNECTION_STRING`; configuring a separate reporting database does not replace
that source dependency.

PostgreSQL migrations retain immutable artifact blobs/catalogs, governance aggregates and hash-chain
audit, close/reconciliation evidence, access grants, delivery jobs, and receipts. If production
reporting persistence or authoritative ledger dependencies are absent, material certification,
governance, release, and distribution operations return unavailable or blocked; they do not fall
back to fixtures or caller rows.

Governance persistence uses explicit format versions. PostgreSQL migration `008` and the file-store
v2 envelopes keep structurally valid pre-hardening v1 state as verified, immutable legacy evidence.
Legacy state is discoverable/exportable only through exact tenant, organization, nullable-company,
retained-access, and recovery authority; it cannot be hydrated as a current run, mutated, released,
or executed as a schedule. One legacy entry does not poison unrelated v2 reads or workers. Missing
facts are never inferred: operators preserve/archive the exact v1 payload and create a freshly
certified v2 run or recaptured v2 schedule. Checksum, raw/index scope, or archived-payload mismatch
is corruption, not a recoverable legacy record.

Optional relay configuration:

- `MERIDIAN_REPORTING_HTTP_RELAY_ENDPOINT`
- `MERIDIAN_REPORTING_HTTP_RELAY_BEARER_TOKEN`
- `MERIDIAN_REPORTING_RELAY_RECEIPT_HMAC_SECRET` (base64, at least 256 bits)
- `MERIDIAN_REPORTING_DELIVERY_GRANT_HMAC_SECRET` (base64, at least 256 bits)
- `MERIDIAN_REPORTING_RECIPIENT_DESTINATIONS_JSON` (exact tenant/company/principal/transport bindings)
- `MERIDIAN_REPORTING_EXTERNAL_ACCESS_BASE_URI`
- `MERIDIAN_REPORTING_DELIVERY_WORKER_ID`
- `MERIDIAN_REPORTING_DELIVERY_POLL_SECONDS`

The resolved reporting files and PostgreSQL schema must be backed up and recovered as one reporting
state set. Both file stores fail closed on unreadable or invalid state; there is no supported
hand-edit or repair endpoint. The supported operator procedure is
[Governed Reporting Operations](../operators/governed-reporting-operations.md).

## Operator surfaces

The browser run-detail route is `/workstation/reporting/runs/detail?runId={runId}`. It shows
normalized parameters, immutable scope/access/snapshot evidence, readiness, lifecycle actions,
artifacts, audit history, series/restatement history, transport capability, delivery receipts, and
grant state. The WPF Reporting governance workbench consumes the same shared contracts and action
availability.

Legacy report-pack mutation, publication, restatement, synthetic delivery, query-token package,
and public due-schedule routes return `410 Gone`. Selected legacy report-pack reads remain only for
historical compatibility and are tenant-filtered; they are not an authoritative mutation path.

## Implementation anchors

- Domain contracts and lifecycle: `src/Meridian.Reporting/`
- Shared API DTOs and routes: `src/Meridian.Contracts/Reporting/` and `src/Meridian.Contracts/Api/UiApiRoutes.cs`
- Certification and application coordination: `src/Meridian.Ui.Shared/Services/Reporting*`
- Canonical endpoints: `src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs`
- Distribution endpoints: `src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs`
- Durable reporting storage: `src/Meridian.Storage/Reporting/`
- Browser surface: `src/Meridian.Ui/dashboard/src/screens/report-run-governance-screen.tsx`
- WPF surface: `src/Meridian.Wpf/Features/Reporting/`
