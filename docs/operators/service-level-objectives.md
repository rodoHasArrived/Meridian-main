# Service Level Objectives

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-01

This page is the canonical operator definition of Meridian's service level indicators (SLIs),
service level objectives (SLOs), error budgets, and recovery objectives.

Every objective below names the exact Prometheus series that measures it, the alert rule that
pages on it, and the runbook section that resolves it. Those three links are enforced
mechanically: `build/scripts/ci/validate-observability-contract.py` fails the build when an
objective measures a metric the exporter does not emit, names an alert that does not exist, or
links to a runbook section that does not resolve.

## How to Read an Objective

| Field | Meaning |
| --- | --- |
| Metric | The Prometheus series scraped from `/metrics`. This is the only measurement source. |
| Target | The value the objective commits to. Crossing it consumes error budget. |
| Critical | The value at which the objective is violated and the linked alert pages. |
| Window | The measurement window the target applies over. |
| Error budget | The share of the budget window the objective may spend outside target. |

Direction is inferred from the two thresholds: when `Target` is below `Critical`, lower values
are better (latency, drop rate, error counts); otherwise higher values are better (uptime,
pass rate).

The runtime mirror of this table is `SloDefinitionRegistry` in
`src/Meridian.Platform/Monitoring/Core/SloDefinitionRegistry.cs`, which is the authority when the
two disagree.

The observability contract gate checks the *linkage* between them — that each objective's metric
is one the exporter emits, that its alert rule exists, and that its runbook and SLO-document
anchors resolve. It does **not** compare the numeric target, critical threshold, unit, measurement
window, or error budget in this table against the registry, so those values can drift without
failing a build. Treat them as reviewed documentation, not machine-verified facts, and change both
sides together.

## Recovery Objectives

These apply to the supported local-workstation topology defined in
[ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md), recovered as one unit
by `build/scripts/recovery/invoke-production-recovery.ps1`.

| Objective | Commitment | Status |
| --- | --- | --- |
| RPO (Recovery Point Objective) | 1 hour — at most one hour of committed work may be lost. | **Stated, not yet measured.** See below. |
| RTO (Recovery Time Objective) | 2 hours — from declared loss to a verified, reconciled, operator-accepted restore. | **Stated, not yet measured.** See below. |

### What the drill measures today, and what it does not

`invoke-production-recovery.ps1` writes `measuredRpoSeconds` and `measuredRtoSeconds` into its
receipt and fails the drill when they exceed `-MaximumRpoSeconds` (default 3600) or
`-MaximumRtoSeconds` (default 7200). Those field names are aspirational: in Drill mode the script
computes

- `measuredRpoSeconds` as `backupCompleted - operationStarted` — how long taking a **fresh** backup
  took, not the age of the last recoverable point;
- `measuredRtoSeconds` as `restored - backupCompleted` — how long `Invoke-Restore` took, starting
  after that new backup and stopping the moment the call returns.

Neither figure includes detection, decision, reconciliation, or operator acceptance, and the RPO
figure cannot express data loss at all because the backup it times is taken during the drill. A
fast backup and a fast restore therefore certify these thresholds without demonstrating either
objective. Read a passing drill as **"backup and restore complete within budget"** — a necessary
condition for the objectives above, and a useful regression guard, but not evidence of them.

Closing this needs the script to record the timestamp of the last recoverable point before the
simulated loss, and to run the clock from declared loss through operator acceptance. Until then
these two rows stay open in `PRD-111`, and no drill receipt should be cited as RPO/RTO evidence.

The drill does produce a dated receipt that the `Production Certification` workflow uploads, and a
run that produces no receipt does not count as a drill. Recovery procedure lives in
[Failover and Recovery](./failover-and-recovery.md).

## Ingestion Plane

### SLO-ING-001

**Event processing latency (P99)** — how long the pipeline takes to process one event end to end.

| Field | Value |
| --- | --- |
| Metric | `mdc_processing_latency_microseconds` (histogram) |
| Target | 1000 microseconds |
| Critical | 5000 microseconds |
| Window | 5 minutes |
| Error budget | 0.1% over 30 days |
| Alert | `MeridianHighProviderLatency` |
| Runbook | [High latency](./operator-runbook.md#high-latency) |

Sustained P99 above the critical threshold means the pipeline cannot keep pace with the inbound
stream; drops follow, so treat this as a leading indicator for SLO-ING-002.

### SLO-ING-002

**Event drop rate** — the share of events the pipeline discards under backpressure.

| Field | Value |
| --- | --- |
| Metric | `mdc_drop_rate_percent` (gauge, 0–100) |
| Target | 0.1 percent |
| Critical | 1.0 percent |
| Window | 24 hours |
| Error budget | 0.1% over 30 days |
| Alert | `MeridianHighDropRate` |
| Runbook | [High drop rate](./operator-runbook.md#high-drop-rate) |

A dropped event is unrecoverable from the live stream; it must be backfilled. Any sustained
non-zero drop rate is a data-completeness incident, not a performance nuisance.

## Data Completeness Plane

### SLO-DC-001

**Validation pass rate** — the share of ingested events that clear the validation stage.

| Field | Value |
| --- | --- |
| Metric | `mdc_validation_pass_rate_percent` (gauge, 0–100) |
| Target | 95 percent |
| Critical | 80 percent |
| Window | 24 hours |
| Error budget | 5% over 30 days |
| Alert | `MeridianLowDataQuality` |
| Runbook | [Low data quality](./operator-runbook.md#low-data-quality) |

Rejected events are counted by `mdc_validation_rejected_total`, labelled by error type. Always
read the label breakdown before acting: a single malformed provider field and a broad schema
drift present identically in the aggregate.

### SLO-DC-002

**Maximum data gap** — the longest interval a symbol may go without a new event.

| Field | Value |
| --- | --- |
| Metric | `mdc_sla_freshness_milliseconds` (histogram, per symbol) |
| Target | 300000 milliseconds (5 minutes) |
| Critical | 600000 milliseconds (10 minutes) |
| Window | 1 hour |
| Alert | `MeridianNoEventsPublished` |
| Runbook | [No events](./operator-runbook.md#no-events) |

Evaluate during market hours only. Outside session hours a growing gap is expected and is not a
violation.

## Availability Plane

### SLO-AV-001

**Service uptime** — the share of market hours the workstation host is reachable and scraping.

| Field | Value |
| --- | --- |
| Metric | `up` (Prometheus built-in, per scrape target) |
| Target | 0.999 |
| Critical | 0.995 |
| Window | 30 days |
| Error budget | 0.1% over 30 days |
| Alert | `MeridianDown` |
| Runbook | [Application down](./operator-runbook.md#application-down) |

This is the only objective measured by a Prometheus built-in rather than an exported Meridian
series: if the process is down, it cannot export its own unavailability.

## Data Freshness Plane

### SLO-DF-001

**Data freshness (P95)** — how stale the newest event per symbol is allowed to be.

| Field | Value |
| --- | --- |
| Metric | `mdc_sla_freshness_milliseconds` (histogram, per symbol) |
| Target | 60000 milliseconds (1 minute) |
| Critical | 300000 milliseconds (5 minutes) |
| Window | 5 minutes |
| Alert | `MeridianDataFreshnessViolation` |
| Runbook | [Freshness SLA violation](./operator-runbook.md#freshness-sla-violation) |

The count of symbols currently outside threshold is exported separately as
`mdc_sla_violation_symbols`; the composite score is `mdc_sla_freshness_score`.

## Storage Plane

### SLO-ST-001

**Zero write-integrity failures** — the durability path must not lose or corrupt records.

| Field | Value |
| --- | --- |
| Metric | `mdc_wal_recovery_corrupted_records_total` (counter) |
| Target | 0 |
| Critical | 1 |
| Window | 5 minutes |
| Alert | `MeridianStorageWriteErrors` |
| Runbook | [Storage write errors](./operator-runbook.md#storage-write-errors) |

This objective has no error budget by design. A corrupted write-ahead log record means committed
data was not durably retained, which is an integrity incident regardless of volume.

The counter is advanced only by the startup recovery pass, so `MeridianStorageWriteErrors` alerts
on the running total rather than a rate: a rate window would fall back to zero minutes after
recovery and clear a P1 integrity alert while the damage was still unreconciled.

## Provider Connectivity Plane

### SLO-PC-001

**Provider circuit remains closed** — the streaming provider stays usable.

| Field | Value |
| --- | --- |
| Metric | `mdc_circuit_breaker_state` (gauge; 0 = closed, 1 = open, 2 = half-open) |
| Target | 0 |
| Critical | 1 |
| Window | 30 days |
| Alert | `MeridianProviderDisconnected` |
| Runbook | [Provider disconnected](./operator-runbook.md#provider-disconnected) |

An open breaker means resubscription has failed repeatedly and the client has stopped retrying at
full rate. Half-open (2) is also a violation: the provider is still unusable and resubscription is
only probing, so `MeridianProviderDisconnected` fires on any value at or above the critical
threshold rather than on equality with `Open`. Per-symbol breaker counts are exported as
`mdc_symbols_circuit_open`, and symbols held back by rate limiting as `mdc_symbols_in_cooldown`.

## Objectives Awaiting Instrumentation

Three metrics are declared by the exporter but never written, so objectives and alerts that
depend on them cannot report truthfully today. This is recorded here rather than hidden because a
metric that exists but is never updated looks identical, to a validator, to one that works.

| Metric | Writer | Consequence |
| --- | --- | --- |
| `mdc_sla_freshness_score`, `mdc_sla_violation_symbols`, and the other `mdc_sla_*` series | `PrometheusMetrics.UpdateSlaMetrics` has no caller | SLO-DF-001 and the composite compliance objective are unmonitored. `MeridianDataFreshnessViolation` and `MeridianSlaComplianceLow` carry a `> 0` guard that suppresses them rather than firing permanently against a default-zero gauge. |
| `mdc_processing_latency_microseconds` | `PrometheusMetrics.RecordProcessingLatency` has no caller on the event path | The histogram's buckets never fill. SLO-ING-001 is alerted from `mdc_average_latency_microseconds`, a mean rather than the documented P99. |

Closing this requires wiring `DataFreshnessSlaMonitor` into the periodic metrics exporter and
recording per-event latency on the pipeline path. Until then, treat these two objectives as
declared but not measured, and do not read a quiet alert as evidence of health.

Two further objectives are measured over the wrong window: `mdc_drop_rate_percent` and
`mdc_validation_pass_rate_percent` are computed from process-lifetime totals, not the rolling
windows SLO-ING-002 and SLO-DC-001 declare, so a long clean history can mask a current burst and
an early burst can hold an alert on after recovery.

## Error Budget Policy

Error budget is consumed whenever an objective is outside target and is measured over the
objective's budget window.

| Budget consumed | Response |
| --- | --- |
| Below 50% | Normal operation. Feature work proceeds. |
| 50%–100% | The owning lane reviews the burn at the next close and records a mitigation. |
| Exhausted | Change freeze on the owning subsystem until the burn stops, tracked as an incident. |

Budget state is not currently computed automatically. Until it is, the owning lane reads the
objective's series over its budget window and records the result in the close evidence.

## Ownership

| Plane | Owning lane |
| --- | --- |
| Ingestion, Data Completeness, Data Freshness | Provider Platform |
| Availability | Runtime Host |
| Storage | Storage |
| Provider Connectivity | Provider Platform |
| Recovery objectives (RPO/RTO) | SRE/Operations |

Incident ownership, escalation, and severity definitions are in
[Incident response](./operator-runbook.md#incident-response).

## Related

- [Operator Runbook](./operator-runbook.md) — the response procedures every alert links to.
- [Failover and Recovery](./failover-and-recovery.md) — recovery procedure and drill evidence.
- [Operator Preflight Checklist](./preflight-checklist.md) — readiness gate before a release.
