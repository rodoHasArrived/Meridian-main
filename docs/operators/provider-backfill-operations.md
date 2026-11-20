# Provider Backfill Operations

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This is the canonical operator lane for historical data backfill operations and recovery in Meridian.

## Scope

- backfill job submission and monitoring,
- provider priority and fallback behavior,
- gap remediation and quality checks,
- dry-run and evidence capture posture.

## Prerequisites

- Complete [local setup](../start/README.md#first-local-setup) and select the intended
  [persistence profile](../reference/environment-variables.md#database-persistence).
- Run the Windows workstation examples in PowerShell 7 from the repository root. The one-line `dotnet` commands also
  work in Bash. An installed release uses its supervisor-managed host and workstation controls;
  these source-checkout commands do not replace the installed host.
- Configure the requested historical provider, credentials, permitted symbols/date range, and
  data root. Provider calls can consume quota; review the preview/cost estimate first.
- For API checks, provision an operator account with `ViewHistoricalData` or the applicable
  provider/storage permission. Submitting a backfill requires `TriggerBackfill`.
- Choose one writer for the data root. Run a standalone CLI backfill after stopping a source host
  that uses that root, or submit the job through the running workstation. Do not launch both.

## Backfill operator workflow

Use this sequence for controlled backfill execution:

1. Configure provider credentials and priorities for all intended providers.
2. Run preview or cost estimate before execution and capture the preview evidence.
3. Run scoped backfill jobs (single symbol or small batches first).
4. Monitor status/progress APIs until completion or bounded retries.
5. Run quality check and gap report before archival or downstream promotion.

### Workstation quota and receipts

In **Data → Backfills → Trigger backfill**, enter the provider, symbols, and date range, then
select **Preview**. Blank daily dates use a one-year lookback ending today when no end date is
entered. The preview shows the effective range that a queued or scheduled job will use.

The quota panel reports the historical adapter's known limit and window, the expected requests
and duration, and the observed remaining budget when available. It also displays daily limits
and distinguishes provider credits from HTTP requests. Local observations
can miss usage from other clients sharing the same account. Pagination, retries, and provider
latency can change usage and timing. The duration excludes time spent waiting in the job queue
or waiting for a scheduled start. Providers without usable quota diagnostics show **Limit unavailable**.
When resuming from retained coverage, the estimate counts requests for each uncovered date range.
Coverage can change before a queued job starts.

For an over-budget plan, narrow the dates or symbols and preview again, schedule the job, choose
an administrator-approved equivalent and preview it, or **Queue with throttling**. Enter the
earliest eligible start in UTC. Queued and scheduled jobs both follow the provider budget.
The workstation keeps an execution receipt with the submission estimate, observed providers,
throttling events, and request and duration variance where measured. Expand the job under
**Workstation backfill receipts**. Live waits appear as **Throttled**.
Completed jobs also show retained batch revisions with provider and session counts. Each revision
identifies the full evidence retained under the host's data root.

The workstation queue is retained on the single host that owns the data root. Pending jobs survive
a restart. Jobs interrupted during execution are marked failed so operators can inspect partial
coverage before submitting again. Provider request counts cover observed adapter attempts.
Receipts show unavailable counts when an adapter cannot expose its individual requests.

### Provider administrator budgets

Provider administrators can set `Backfill.OperationalBudgets` in the host's `appsettings.json`.
Policies apply across workstation jobs. The dialog displays the policy and researchers submit
jobs within it. Keys are runtime historical provider IDs.

```json
{
  "Backfill": {
    "OperationalBudgets": {
      "provider-id": {
        "ReserveHeadroomPercent": 20,
        "MaxConcurrentRequests": 2,
        "ApprovedEquivalentProviders": ["alternative-provider-id"]
      }
    }
  }
}
```

`ReserveHeadroomPercent` accepts 0–99. The host rounds the backfill allowance down to whole
units with a minimum of one historical request per window. Headroom applies to both the short
window and the daily limit when reported by the adapter.
`MaxConcurrentRequests` is an optional positive cap on the provider's parallel backfill work.
Approve equivalent providers only after checking instrument coverage, entitlements, intervals,
and price adjustment conventions. The host checks approval and runtime compatibility again when
the job is dispatched. In-progress runs keep the policy loaded when they started.

## Key endpoints

- `GET /api/backfill/providers`
- `GET /api/backfill/status`
- `GET /api/backfill/progress`
- `POST /api/backfill/run/preview`
- `POST /api/backfill/cost-estimate`
- `POST /api/backfill/run`
- `GET /api/backfill/executions`
- `GET /api/backfill/statistics`

Use [preflight authentication](preflight-checklist.md#authenticated-evidence-collection) for API
reads. Workstation submissions use the signed-in operator session and its mutation protections.
`/api/backfill/progress` describes the host's coordinator activity. `/api/backfill/status` reads
the last retained immediate or CLI result from the configured data root and returns `404` when
none exists. `/api/backfill/executions` includes queued and scheduled workstation receipts for
the authenticated tenant.

## Inspect and reproduce a result

Open **Data → Backfill queue** and select a completed result. Expand **Provider provenance** to see
which providers supplied data, whether fallback occurred, the requested scope, and the effective
validation profile. Use **Load older backfill results** to page through retained runs. Research run
details and completed Quant Lab and Covered Call results use the same receipt panel.

**Export JSON sidecar** downloads the versioned receipt. **Export report** downloads its plain-text
form. A scoped connection has an opaque receipt ID. The receipt records the feed and entitlement
labels reported by the runtime and marks unavailable provenance explicitly.

**Clone this run** restores the saved request and checks the provider route before presenting a run
confirmation. Confirm again if provider availability changes during review. A missing or changed
historical route, an unretained checkpoint, or an unversioned research dataset leaves reproduction
unavailable and the original receipt inspectable.

## Provider configuration posture

Use explicit provider priority and fallback policy matching current run intent:

- Configure provider enablement and `Priority` in backfill settings.
- Enable fallback only where temporary provider failures are acceptable.
- Validate credentials for key-backed providers before production backfill.

Keep provider lookup details in Reference lane files:
- [provider-capability-matrix.md](../reference/provider-capability-matrix.md)
- [provider-validation-matrix.md](../reference/provider-validation-matrix.md)

## Data quality rules

Open **Data → Providers**, select a provider, and open **Data quality rules**. The panel shows
effective and default values, the saved profile source, and the recorded before/after audit.
Workstation backfills and gap repair read daily staleness tolerance at job start, including jobs
waiting in the workstation queue. Daily provider fallback uses the saved threshold for each candidate.
Deployment-scheduled backfills and live ingestion use their startup profiles.

Operators with `ManageProviders` and `ModifyConfig` can edit the profile or remove its override
with **Reset to deployment default**. A relaxation requires a reason and confirmation of the
affected future jobs. The panel shows saved streaming values alongside effective ones while a
host restart is pending. Saving and auditing a change use the same configuration write.

## Multi-symbol ordering and resume semantics

Current `HistoricalBackfillService` behavior is deterministic only within the configured execution
mode:

- `MaxConcurrentSymbols` caps concurrent symbol processing. If unset, the service uses the
  configured backfill job concurrency.
- When `SymbolPriorities` is supplied, lower numeric priority values process first. Priority keys are
  matched case-insensitively.
- When no symbol priorities are supplied and concurrency is `1`, input symbol order is preserved.
- Cost preview and execution trim symbols, drop blanks, and de-duplicate symbols case-insensitively
  while preserving the first-seen order and spelling, so repeated symbols do not create duplicate
  provider calls or validation rows.
- When concurrency is greater than `1`, completion order is not a stable ordering contract. Use
  per-symbol validation signals and checkpoints rather than task completion order as evidence.
- `ResumeFromCheckpoint=true` skips only dates recorded as durably written by the same provider and
  feed (`_status/backfill-symbol-coverage.json`, written after the pipeline flush succeeds). It
  fetches every uncovered part of the requested range: dates before the recorded coverage, gaps
  inside it, and dates after it. A symbol is skipped only when the whole requested range is
  covered; a warning signal is emitted when that coverage has no bar-count evidence.
- Coverage is recorded per symbol, granularity, provider and feed (for Alpaca, the feed and price
  adjustment). Changing the provider or feed fetches the range again. Coverage ends at the newest
  bar written, so later sessions are still requested.
- Date-only checkpoints (`_status/backfill-symbol-checkpoints.json`) are still written for status
  surfaces, but they are never used to skip or advance. After upgrading, the first resume of a
  range that only has such a checkpoint fetches the range again once.
- Fresh runs without the resume flag clear matching-granularity checkpoints and coverage before
  writing new evidence; they do not clear another granularity's.
- Automatic gap-analyzer remediation batches same-provider, same-window scan gaps into one
  deterministic request with symbols normalized to uppercase and sorted lexically before execution
  history is retained. Data-quality and quality-alert triggers remain single-symbol remediation
  signals.

## Execution commands

Choose one execution command for the approved scope. These examples use the configured provider;
add `--backfill-provider <provider-id>` to select a specific registered provider explicitly.
They are real backfill requests, not previews.

```powershell
# Begin with a bounded single-symbol window
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL --backfill-from 2025-06-02 --backfill-to 2025-06-06

# Catch up an explicitly chosen later interval
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL --backfill-from 2025-06-09 --backfill-to 2025-06-13

# Scoped date range
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2025-06-01 --backfill-to 2025-12-31

# Configuration/resource validation without provider connectivity checks or collection
dotnet run --project src/Meridian/Meridian.csproj -- --dry-run --offline
```

`--dry-run --offline` validates local configuration/resources; it does not produce a backfill
fetch plan or prove provider access. Use the workstation preview/cost-estimate operation for the
exact symbols and dates, and `--help backfill` for supported CLI options.

## Expected result

- The chosen CLI backfill exits with code `0` and writes its last-run result to
  `{DataRoot}/_status/backfill.json`. A nonzero exit is a failed run.
- The result's provider, symbols, dates, and timestamps match the approved request. Inspect
  `Success`, `BarsWritten`, `SkippedSymbols`, `SymbolValidationSignals`, and `Error`; successful
  process completion alone does not establish complete or correct historical coverage.
- Retain the result before a later run replaces the last-run file, then complete the quality
  checks below and the [mandatory evidence](#mandatory-evidence) packet.

## Quality and gap checks

After a standalone CLI run has exited, start the workstation host against the same config/data root
in **terminal 1** using [preflight](preflight-checklist.md#mandatory-command-set). In **terminal 2**,
complete [authenticated evidence collection](preflight-checklist.md#authenticated-evidence-collection)
to define `$meridianBaseUrl` and `$operatorSession`, then inspect the completed run and a date in
the requested window:

```powershell
Invoke-RestMethod "$meridianBaseUrl/api/backfill/status" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/quality/gaps/AAPL?date=2025-06-02" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/quality/reports/daily?date=2025-06-02" -WebSession $operatorSession
```

These are supported HTTP routes; there are no `--gap-report` or `--quality-report` CLI commands.
The quality endpoints report the host's available monitoring evidence. Empty results do not prove
that every historical bar was observed or that the requested interval is complete. Compare their
coverage with the run's per-symbol validation signals and retained output.

Before accepting outputs, also complete:

- bounded cross-source daily backfill reconciliation when an alternate provider is part of the
  acceptance evidence; batch reconciliation normalizes symbols to uppercase, de-duplicates them,
  preserves first-seen request order, filters comparison bars to the requested symbol, and reports
  clean/drift/symbol-mismatch/missing-evidence/error posture per symbol
- review execution history/lineage before promoting archive/parquet transitions.

## Failure and recovery

| Symptom | Next action |
| --- | --- |
| Local configuration validation fails | Fix the reported setting and rerun `--dry-run --offline` before provider calls. |
| Startup rejects the persistence profile, or an API returns `401/403` | Use the [preflight failure table](preflight-checklist.md#failure-and-recovery); preserve the intended data root and operator scope. |
| Provider rejects credentials, entitlement, symbols, or dates | Correct the specific input, retain the failed result, and retry a bounded window. Use [Provider Credential Operations](provider-credentials.md) for credential repair. |
| Rate limiting or provider outage | Respect retry delays and the configured fallback policy; keep original provider evidence when using an alternate source. |
| API reports an active-job conflict | Inspect `/api/backfill/progress` and let the admitted job finish or follow its cancellation procedure before resubmitting. |
| Status returns `404`, or returned dates/provider differ from this run | Verify the host and CLI used the same data root and that a completed result was written. Do not accept another run's status as proof. |
| Missing bars or failed per-symbol validation | Retain the result, investigate the affected symbols/window, then use the gap-remediation workflow below. Preserve checkpoints and failed evidence for the repair review. |

## Gap-remediation SLA posture

The current automation provides guardrails plus retained SLA classification metadata and a
queryable SLA status snapshot for gap remediation, but it is not a complete cross-provider SLA
engine. Treat these as the active operator semantics until a provider-governance workflow enforces
timers and escalation end to end:

- Detect: a gap report, data-quality gap, or quality alert must identify symbol, provider when known,
  date range, granularity, severity, and affected downstream workflow.
- Triage: critical gaps that block paper promotion, reconciliation, accounting, or governed reporting
  require same-business-day owner assignment and a retained runbook entry. Auto-remediation history
  now records `sla-tier`, `sla-due-utc`, `sla-requires-owner`, `downstream-workflow`, and
  `sla-reason` warnings for each system-triggered remediation execution so operators can distinguish
  standard repairs from critical workflow blockers. `AutoGapRemediationService.EvaluateRemediationSla`
  projects those retained fields into current `Overdue`, `DueSoon`, `Failed`, `Open`, or `Completed`
  status items with owner-assignment counts.
- Attempt: auto-remediation may run when the gap exceeds the configured minimum duration/size and is
  not under symbol/provider cooldown. Duplicate triggers are suppressed by idempotency; transient
  provider failures may retry with the same remediation key. Same-provider, same-window scan gaps
  share a batched remediation key so operators can audit one execution against the affected symbol
  set.
- Fallback: cross-provider repair must use the configured provider priority/fallback order and must
  preserve the original source/provider evidence for audit comparison.
- Prove: a gap is not closed until the rerun has execution history, per-symbol validation signals,
  bounded cross-source reconciliation evidence when fallback data is used, no wrong-symbol fallback
  rows or zero-evidence provider windows in the accepted evidence set, a retained closure decision
  with ordered review symbols, and a follow-up gap/quality check showing the affected interval is
  acceptable for the downstream workflow.
- Escalate: if provider fallback cannot close the interval or the SLA snapshot marks the remediation
  overdue, attach the failed execution evidence and route the exception to provider readiness,
  reconciliation, or reporting owners according to the blocked workflow.

## Backfill controls for operators

### Provider mode semantics

- Run with the minimal required provider set for validation.
- If a single provider becomes unavailable, use the configured fallback order explicitly.
- Review error and rotation signals before escalating.

### Recovery and throttling controls

- Honor rate-limit pressure by widening intervals rather than forcing retries.
- Use incremental backfill windows when a full range is high risk.
- Escalate unresolved critical breaks only with evidence attached.

### Cost-estimate and execution partition planning

- `POST /api/backfill/cost-estimate` returns per-provider `PartitionStrategy` and
  `AdaptivePartitions` entries for estimator-planned provider windows.
- Bounded long intraday or multi-year daily runs execute through the same adaptive windows so
  provider calls stay aligned with the previewed plan.
- Review adaptive partitions before long runs. They are planning and execution-shape evidence for
  quota, wall-clock, and batching expectations, not completion proof.
- Retain execution history, checkpoint evidence, and follow-up quality/gap checks as the proof that
  each affected interval was accepted for downstream workflows.

## Mandatory evidence

Backfill operations entering support/handover should include:

- preview output reference,
- run request/command capture,
- status/progress endpoint observations,
- quality/gap output for one representative symbol or batch,
- operator action log (start, stop, retry, completion).

## Related operator runbooks

- [Distributed Tracing](./distributed-tracing.md) for connected queue, worker, provider-fetch, and storage spans
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Reconciliation Operations](./reconciliation-operations.md)
- [Failover and Recovery](./failover-and-recovery.md)

## Source and archive

- Legacy source archived at [archive/docs/providers/backfill-guide.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/backfill-guide.md)
