# Operator Runbook

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-01

This page is the canonical response procedure for every Meridian alert. Each section below is the
link target of a Prometheus `runbook_url` annotation and of an `AlertRunbookEntry` in
`src/Meridian.Platform/Monitoring/Core/AlertRunbookRegistry.cs`.

Those links are enforced: `build/scripts/ci/validate-observability-contract.py` fails the build
when an alert links to a section that does not exist here, so an operator paged at 03:00 cannot
land on a missing anchor.

Objectives, thresholds, and error budgets are in
[Service Level Objectives](./service-level-objectives.md). Recovery procedure is in
[Failover and Recovery](./failover-and-recovery.md).

## Before You Start

Confirm these three things before acting on any alert:

1. **Is the market open?** Several alerts are expected outside session hours. Check the trading
   calendar before treating an event gap as an incident.
2. **What does the host say?** `GET /health/detailed` reports per-check state and is the fastest
   discriminator between "process is unhealthy" and "one dependency is degraded".
3. **Is this one alert or a cascade?** A provider disconnect produces freshness, gap, and quality
   alerts within minutes. Resolve the upstream cause, not each symptom.

## Diagnostics Bundle

Collect this before escalating, and attach it to the incident record:

| Item | Source |
| --- | --- |
| Health detail | `GET /health/detailed` |
| Current metrics scrape | `GET /metrics` |
| Provider connection state | `GET /api/providers/status` |
| Backpressure and queue state | `GET /api/backpressure` |
| SLA violations by symbol | `GET /api/sla/violations` |
| Recent host logs | `journalctl -u meridian --since '1 hour ago'` |
| Lifecycle receipts | the data root's lifecycle receipt directory |

A bundle that omits the metrics scrape is not sufficient for post-incident review: it removes the
only record of what the alert actually saw.

## Application Down

Alert: `MeridianDown` — severity critical, priority P1, objective
[SLO-AV-001](./service-level-objectives.md#slo-av-001).

The scrape target is unreachable. The process cannot report its own unavailability, so this alert
comes from Prometheus' own `up` series.

**Probable causes:** process crashed; host unreachable; port blocked by firewall; killed by the
OS out-of-memory reaper.

**Immediate actions** — supported local-workstation topology
([ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md)):

1. Check the per-user lifecycle supervisor state and the most recent lifecycle receipts under
   `%LOCALAPPDATA%\Meridian\service\receipts` — a receipt records why the last transition ended.
2. Read `%LOCALAPPDATA%\Meridian\service\logs\lifecycle-supervisor.log` for the exit reason.
3. Check disk space and memory — an exhausted data root and an OOM kill both present as a crash.
4. Restart through the supervisor, then watch the first two minutes of the log for a repeat.

See the [Lifecycle Control Plane reference](../reference/lifecycle-control-plane.md) for the
supervisor commands, receipt locations, and state model.

On an experimental container or systemd deployment — outside the supported envelope — the
equivalent first two steps are `systemctl status meridian` and `journalctl -u meridian -n 200`.

**Resolved when:** the health endpoint returns 200 within 30 seconds of restart and stays up for
one full scrape interval.

**Escalate if:** the process crashes again within 10 minutes of restart. Two crashes is a
recurring fault, not a transient one — capture the diagnostics bundle before the third restart.

## Unhealthy Status

Alert: `MeridianUnhealthy` — severity warning, priority P2, objective
[SLO-ING-002](./service-level-objectives.md#slo-ing-002).

The process is up and scraping, but it is discarding more than 5% of events — well past the 1%
critical threshold that [High drop rate](#high-drop-rate) pages on. This is a degradation, not an
outage, but it is losing data.

This alert reads `mdc_drop_rate_percent`, not a composite health score: the SLA gauges have no
writer, so a score-based trigger would fire permanently. See
[Objectives awaiting instrumentation](./service-level-objectives.md#objectives-awaiting-instrumentation).

**Probable causes:** storage sink blocking; pipeline backpressure; a provider burst exceeding
processing capacity; dependency timeout.

**Immediate actions**

1. `GET /api/backpressure` and read the queue utilization — the drop rate is a consequence.
2. `GET /health/detailed` and identify which specific checks are failing.
3. Review recent error logs for the failing check's subsystem.
4. Verify provider connectivity before assuming a local fault.

**Resolved when:** the drop rate returns below 1% **and** the window in which events were dropped
has been backfilled. Follow [High drop rate](#high-drop-rate) for the backfill step; a recovered
rate alone still leaves a hole in the record.

## High Drop Rate

Alert: `MeridianHighDropRate` — severity warning, priority P2, objective
[SLO-ING-002](./service-level-objectives.md#slo-ing-002).

The pipeline is discarding events under backpressure. Dropped events are not recoverable from the
live stream; they must be backfilled.

**Probable causes:** storage sink blocking on slow disk I/O; pipeline queue at capacity; more
subscriptions than the host can process.

**Immediate actions**

1. `GET /api/backpressure` and read the queue utilization.
2. Check disk I/O latency on the data root — a slow sink is the most common cause.
3. Reduce symbol subscriptions if the host is genuinely oversubscribed.
4. Check the configured `EventPipeline` channel capacity against the observed inbound rate.

**After recovery:** record the drop window and schedule a gap-fill backfill over it. Closing the
alert without backfilling leaves a permanent hole in the record.

## Pipeline Backpressure

Alert: `MeridianPipelineBackpressure` — severity warning, priority P2.

Sustained absolute event drops. This is the same failure mode as
[High drop rate](#high-drop-rate) seen as a count rather than a proportion; a low percentage of a
very high inbound rate still loses a large number of events.

**Probable causes:** consumer slower than producer; storage write latency; burst of market data.

**Immediate actions**

1. Check storage write latency metrics.
2. Verify disk health on the data root.
3. Pause non-critical subscriptions to shed load.

## No Events

Alert: `MeridianNoEventsPublished` — severity warning, priority P2, objective
[SLO-DC-002](./service-level-objectives.md#slo-dc-002).

Nothing has been published for the alert window.

**Probable causes:** all providers disconnected; market closed; subscription failure; network
outage.

**Immediate actions**

1. **Check market hours first.** Outside session hours this alert is expected and is not an
   incident.
2. `GET /api/providers/status` for connection state.
3. Test provider connectivity directly before restarting anything.
4. Re-subscribe if the connection is up but no subscriptions are active.

## Provider Disconnected

Alert: `MeridianProviderDisconnected` — severity warning, priority P2, objective
[SLO-PC-001](./service-level-objectives.md#slo-pc-001).

The global provider circuit breaker is open: resubscription has failed repeatedly and the client
has backed off.

**Probable causes:** API key expired or invalid; provider service outage; network connectivity;
rate limit exceeded.

**Immediate actions**

1. Check the provider's own status page before investigating locally.
2. Verify API credentials have not expired — see
   [Provider Credentials and Access](./provider-credentials.md).
3. Check rate limit counters; a breached limit looks identical to an outage from inside.
4. Trigger a manual reconnect, or fail over to the backup provider.

**Resolved when:** `mdc_circuit_breaker_state` returns to 0 and `mdc_symbols_circuit_open` reaches
zero.

## High Latency

Alert: `MeridianHighProviderLatency` — severity warning, priority P3, objective
[SLO-ING-001](./service-level-objectives.md#slo-ing-001).

P99 event processing latency is above the critical threshold. Treat this as a leading indicator:
if it persists, drops follow.

**Probable causes:** provider under load; network congestion; DNS resolution delays; WebSocket
reconnection overhead.

**Immediate actions**

1. Check provider latency trends at `GET /api/providers/latency`.
2. Verify network path quality to the provider endpoint.
3. Consider switching to the backup provider if the degradation is provider-side.

## Storage Write Errors

Alert: `MeridianStorageWriteErrors` — severity critical, priority P1, objective
[SLO-ST-001](./service-level-objectives.md#slo-st-001).

Corrupted write-ahead log records were encountered. Committed data was not durably retained. This
is an integrity incident at any volume.

**Probable causes:** disk full; filesystem permissions; I/O errors; WAL corruption; storage path
misconfigured.

**Immediate actions**

1. **Check disk space immediately.** A full data root is the most common cause and the fastest fix.
2. Verify storage path ownership and permissions.
3. Check WAL integrity and capture the corrupted records before any repair — they are the only
   evidence of what was lost.
4. Review storage error logs for the root cause.

**Do not** truncate or delete the WAL to clear the alert. That destroys the record of what failed
to persist. Follow [Failover and Recovery](./failover-and-recovery.md) instead.

**Resolved when:** the affected window has been reconciled **and** a subsequent restart completes
recovery with zero corrupted records. The alert reads the counter total, not a rate, so it stays
firing for the life of the process that found the damage — it will not go quiet on its own while
the corruption is still unreconciled.

## Low Data Quality

Alert: `MeridianLowDataQuality` — severity warning, priority P3, objective
[SLO-DC-001](./service-level-objectives.md#slo-dc-001).

The validation pass rate has fallen below threshold.

**Probable causes:** data gaps from a provider outage; stale quotes; sequence errors; bad tick
data from the provider.

**Immediate actions**

1. Read the rejection breakdown by error type before acting — a single malformed field and a broad
   schema drift look identical in the aggregate.
2. `GET /api/quality/gaps` for gap analysis.
3. `GET /api/quality/comparison/{symbol}` to compare across providers for an affected symbol and
   establish whether the fault is provider-specific. The symbol segment is required; the route
   does not exist without it.
4. Trigger a gap-fill backfill if the cause was an outage.

## Freshness SLA Violation

Alert: `MeridianDataFreshnessViolation` — severity critical, priority P1, objective
[SLO-DF-001](./service-level-objectives.md#slo-df-001).

One or more symbols have exceeded their freshness threshold. Downstream valuation and reporting
read stale prices until this clears.

**Probable causes:** provider stream stalled; subscription dropped; processing pipeline blocked.

**Immediate actions**

1. `GET /api/sla/violations` to identify the affected symbols.
2. Check provider connection status — a stalled stream often reports as connected.
3. Verify the subscription is still active for the affected symbols.
4. Check pipeline queue utilization to rule out a local block.
5. Re-subscribe the affected symbols.

**Resolved when:** freshness age drops below the configured threshold for every affected symbol.

## SLA Compliance

Alert: `MeridianSlaComplianceLow` — severity warning, priority P2.

The composite freshness score is below 95. This is an aggregate signal: it usually means several
symbols are degraded rather than one being broken.

**Probable causes:** multiple provider degradations; systematic processing delays; infrastructure
issues.

**Immediate actions**

1. `GET /api/sla/violations` for the affected symbol list.
2. `GET /api/sla/metrics` for the trend — a slow decline and a step change have different causes.
3. Evaluate provider health across all active providers, not just the primary.

## Incident Response

**Severity and priority**

| Priority | Meaning | Response |
| --- | --- | --- |
| P1 | Data loss, integrity failure, or full unavailability. | Respond immediately. Page the owning lane. |
| P2 | Degradation with ongoing risk to completeness. | Respond within the session. Record in close evidence. |
| P3 | Degradation without immediate data risk. | Triage at the next working block. |

**Procedure**

1. **Acknowledge** the alert so a second responder does not duplicate the work.
2. **Collect** the [diagnostics bundle](#diagnostics-bundle) before making changes — restarting
   first destroys the evidence of why it failed.
3. **Stabilize** using the alert's section above.
4. **Record** the incident: what fired, what was observed, what was changed, and what remains.
5. **Reconcile** any window where events were dropped or storage failed. An alert that cleared is
   not the same as a record that is complete.
6. **Review** with the owning lane named in
   [Service Level Objectives](./service-level-objectives.md#ownership).

**Ownership routing**

| Alert | Owning lane |
| --- | --- |
| `MeridianDown`, `MeridianUnhealthy` | Runtime Host |
| `MeridianHighDropRate`, `MeridianPipelineBackpressure`, `MeridianHighProviderLatency` | Provider Platform |
| `MeridianNoEventsPublished`, `MeridianLowDataQuality`, `MeridianDataFreshnessViolation`, `MeridianSlaComplianceLow` | Provider Platform |
| `MeridianProviderDisconnected` | Provider Platform |
| `MeridianStorageWriteErrors` | Storage |

## Troubleshooting

For problems that did not arrive as an alert:

| Symptom | Start here |
| --- | --- |
| Workstation will not start | [Browser Workstation Installer](./browser-workstation-installer.md) |
| A terminal receipt reports failed, blocked, or warning | [Verified Outcome Recovery](./verified-outcome-recovery.md) |
| Reconciliation breaks will not close | [Reconciliation Operations](./reconciliation-operations.md) |
| Report pack will not release | [Governed Reporting Operations](./governed-reporting-operations.md) |
| Provider credentials rejected | [Provider Credentials and Access](./provider-credentials.md) |
| Backfill gaps remain after recovery | [Provider Backfill Operations](./provider-backfill-operations.md) |

## Related

- [Service Level Objectives](./service-level-objectives.md) — objectives, thresholds, RTO/RPO.
- [Failover and Recovery](./failover-and-recovery.md) — recovery procedure and drill evidence.
- [Operator Preflight Checklist](./preflight-checklist.md) — readiness gate before a release.
- [Live Execution Controls](../operations/live-execution-controls.md) — execution kill-switch posture.
