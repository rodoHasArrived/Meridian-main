# Actions workflow follow-up — 2026-10-15

**Status:** archived evidence; administrator follow-up remains open
**Owner:** core-team
**Reviewed:** 2026-10-15

A fresh, bounded Actions sample has complete run, attempt, job and check evidence. It does
not establish a current hosted-capacity bottleneck or improvement against the October 5
baseline. The sampled PR jobs usually started within a few seconds; main-push jobs waited
longer. The historical IB runtime job finished without ever receiving a runner.

## Collection scope and limits

[Source snapshot](actions-workflow-source-2026-10-15.json.gz),
[full report](actions-workflow-report-2026-10-15.json.gz),
[diagnostic policy](actions-workflow-policy-2026-10-15.json) and
[audit and operational evidence](actions-workflow-audit-2026-10-15.json) retain the inputs,
checksums, live verification receipts, access errors and separate queue/rules observations.
Snapshots use deterministic gzip compression. Collection used read-only connected GitHub
requests replayed through `build/scripts/ci/collect-actions.py`; no settings were changed.

The fixed creation window was **October 15, 22:00:00–22:59:59 UTC, inclusive**, chosen before
examining results. All **59 runs, 59 attempts and 148 job rows** were retained, including
unsuccessful work. All advertised run/job/check pages were fetched. Final live run-detail
reads at 23:16:29–23:16:34 UTC confirmed that every run still had one attempt. All runs were
completed; there were no retries in this window. This is a snapshot as observed, not a claim
that these runs cannot be retried later.

The collector correctly exits **1** with `collection.complete=false`: classic protection is
inaccessible, effective branch rules and the workflow catalog are blocked by the connector,
and the main push lacks verified `push.before` boundaries. `runDataComplete` and
`checkDataComplete` are true. Exact PR change scopes were recovered from immutable synthetic
merge references and verified parents; all 49 PR workflow records have complete change scope.
The ten main-push records are descriptive only. The workflow inventory covers every workflow
seen in the window, not the inaccessible repository-wide catalog.

The diagnostic check roster preserves the four documented core identities: `quality-gate`,
`Secret Scan`, `Analyze csharp` and `Analyze javascript-typescript`, bound to the GitHub
Actions app and their owning workflows. This roster is **not proof of enforced branch
requirements**. The integration companion is assessed separately below. No baseline/rollout
comparison windows or historical requirement-equivalence evidence are supplied; the report
therefore emits no reduction percentage. The original 25% execution and 30% runner-minute
targets remain unevaluated.

## Queue, execution and unsuccessful work

| Measurement | Pull requests | Main push |
| --- | ---: | ---: |
| Distinct changes | 6 | 1 |
| Workflow runs / attempts | 49 / 49 | 10 / 10 |
| Successful / failed / cancelled workflow attempts | 37 / 3 / 9 | 10 / 0 / 0 |
| Measured job queue/dependency waits | 114 | 25 |
| Median / p95 job queue/dependency wait | 2s / 3s | 67s / 327s |
| Accounted runner minutes, all outcomes | 489.60 | 124.13 |
| Runner minutes in failed workflow attempts | 39.77 | 0 |
| Runner minutes in cancelled workflow attempts | 152.52 | 0 |

Nine skipped jobs have unavailable raw execution durations but proven zero new runner cost.
All attempt runner totals are accounted for. These minutes sum execution across jobs and
include work in unsuccessful attempts; they do not apply billing rounding or multipliers.
Created-to-started waits include dependency waiting and cannot isolate scheduler capacity.
The cancelled attempts consumed measurable execution, but this snapshot does not prove their
cancellation causes or that all consumed work was avoidable.

Only **two successful PR changes** qualify for the diagnostic completed-check cohort. Their
median documented-check completion was **20.53 minutes**, quality-gate latency **14.46
minutes**, and quality-gate execution **13.61 minutes**. Execution is the union of the four
canonical validation lanes and gate, excluding idle gaps. The one main-push change completed
the documented check roster in **23.80 minutes**. These small, different cohorts do not support
an improvement claim against the earlier measurement or a tuning-default change.

Across all events, the measured waits split by requested runner labels were:

| Requested labels | Jobs | Median | p95 |
| --- | ---: | ---: | ---: |
| `ubuntu-latest` | 120 | 2s | 219s |
| `windows-latest` | 15 | 2s | 241s |
| `ubuntu-24.04` | 4 | 2s | 85s |

p95 uses nearest rank; it is the maximum for the smaller rows. Requested labels and assigned
runner names are observable; available owner-wide concurrency and purchased capacity are not.

## Integration companion and enforcement evidence

The bounded sample contains seven Meridian CI runs. Every one reported `integration-gate`:
six passed, and one failed after its PostgreSQL integration job was cancelled.

| Evidence | Result |
| --- | --- |
| [PR run 38819231543](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38819231543) | PostgreSQL integration, integration-gate and quality-gate succeeded. |
| [PR run 38819536740](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38819536740) | PostgreSQL integration, integration-gate and quality-gate succeeded. |
| [PR run 38818818951](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38818818951) | Integration job `116710082599` was cancelled; integration-gate job `116711560336` reported failure at 22:45:42 UTC. |

This provides live PR success and cancellation-failure evidence. It does not prove branch
blocking, skipped/missing-result behavior, or merge-group success. A separate repository run
query with `event=merge_group` returned zero records at 23:17:31 UTC; the retained API response
is evidence of that query result, not a guarantee about deleted or unavailable history.

The readable active ruleset [20768244](https://github.com/rodoHasArrived/Meridian-main/rules/20768244)
applies to all branches and contains deletion and non-fast-forward rules. Its readable
configuration supplies no required-status-check inventory. Classic protection and required
status-check endpoints returned **403 Resource not accessible by integration**. Effective
branch-rule reads were rejected by the connector. Consequently, administrators must still
verify the actual required check names/app bindings and complete reviewed integration-gate
rollout. Repository rules and protection settings were not modified.

## IB runner and stale queue follow-up

[IB runtime run 37269613950](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37269613950),
previously observed waiting on October 5, finished **cancelled on October 6 at 05:51:17 UTC**.
Its only job was created October 5 at 05:51:16 UTC: **24 hours and one second** elapsed without
an assigned runner (`runner_id=0`, empty runner name, no steps). The API supplies a synthetic
start equal to creation, which is not execution evidence. Requested labels remain
`self-hosted, Windows`; these facts do not identify whether service health, label/group
eligibility or the protected paper environment caused the wait.

At **23:17:31 UTC on October 15**, complete queued and waiting queries returned two queued
runs and zero waiting runs. Neither queued run was IB runtime. The two March 27 records,
[23662997318](https://github.com/rodoHasArrived/Meridian-main/actions/runs/23662997318) and
[23662972493](https://github.com/rodoHasArrived/Meridian-main/actions/runs/23662972493), still
returned zero job rows. These separate requests are not an atomic runner-capacity snapshot.
Runner inventory remained unavailable through the connector.

| Open follow-up | Owner | Evidence needed to close |
| --- | --- | --- |
| Historical IB non-assignment | Runner administrator | Service health, exact labels/group access and protected-environment eligibility; a successful protected paper smoke run. |
| Two stale queued records | Actions administrator | Determine whether the records represent live work and resolve them through normal administration. |
| Hosted scheduling/capacity | Actions administrator with core-team | Owner-wide capacity/usage and a larger attempt-complete event/runner sample; current evidence does not establish a binding quota. |
| Required-check and companion rollout | Repository administrator and human governance reviewer | Effective protection inventory, app-bound identities, successful merge-group evidence and reviewed requirement activation. |

## Reproduce the report

```bash
python build/scripts/ci/ci-metrics.py \
  --input docs/engineering/actions-workflow-source-2026-10-15.json.gz \
  --comparison docs/engineering/actions-workflow-policy-2026-10-15.json \
  --output artifacts/actions-workflow-followup/report.json \
  --markdown artifacts/actions-workflow-followup/report.md
```

The regenerated JSON was byte-for-byte identical to the retained report. Hashes for compressed
and uncompressed snapshots are recorded in the audit file. These observations extend the
[October 5 measurement](actions-rollout-measurement-2026-10-05.md); ongoing ownership and
rollout procedures remain in [CI/CD ownership and rollout](ci-cd-optimization.md).
