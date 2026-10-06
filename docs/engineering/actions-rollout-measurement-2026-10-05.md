# Actions rollout measurement — 2026-10-05

**Status:** measurement collected; original improvement targets unestablished
**Owner:** core-team
**Reviewed:** 2026-10-05

The report contains **24 comparable completed PR changes after rollout**, but only **6 matched baseline/rollout pairs**. The original 25% execution-time and 30% runner-minute targets remain **unevaluated**. No savings are claimed. Hosted concurrency remains two .NET processes and eight browser files per batch; local .NET remains sequential.

## Scope and evidence

[Source snapshot](actions-rollout-source-2026-10-05.json.gz), [full report](actions-rollout-report-2026-10-05.json.gz), and [comparison policy](actions-rollout-policy-2026-10-05.json) preserve the measured inputs, attempt accounting, change classifications, exclusions and exact match membership. The JSON snapshots use deterministic gzip compression.

- Baseline: September 26, 00:00:00 UTC through September 28, 21:28:53 UTC, inclusive.
- Rollout observations: October 3–4 UTC, both complete days. These disjoint windows are not a continuous history.
- The first consolidation stage merged September 28 at 21:28:54 UTC; all five stages were merged by September 29 at 02:13:13 UTC. Both measurement windows avoid the phased rollout.
- October 4 alone supplied 18 comparable successful PR changes. October 3 was added as an adjacent complete day to reach the minimum sample count, before inspecting improvement results.
- Collected 1,216 distinct workflow runs, 1,220 attempts and 3,109 job records, including failures and cancellations. Every run page and every attempt-specific job page in the selected windows was retrieved; no run, attempt, job or check-data collection gaps remain.

The four measured checks are `quality-gate`, `Secret Scan`, `Analyze csharp` and `Analyze javascript-typescript`, bound to the GitHub Actions app (15368) and their owning workflows. These are the documented check set, **not a verified inventory of every historically enforced branch requirement**. Classic protection returned 403, and the connector blocked the workflow catalog and effective branch-rule endpoints. Relevant workflows are retained from the complete run inventory. Current readable rulesets do not establish historical check equivalence.

Historical PR change scopes were verified from the exact tested synthetic merge commits: 71 baseline PR heads through retained checkout logs, and all 47 rollout PR heads through reusable-workflow references. Log hashes, source jobs, checkout excerpts and verified parents are retained. Mutable current PR associations and reused branch names were not accepted as historical base evidence. Main-push metadata lacks verified `push.before` boundaries, so main pushes remain descriptive observations and never enter the savings comparison.

## Post-rollout PR measurements

The following distributions describe the 24 comparable successful post-rollout changes, including the 18 without baseline matches. They are descriptive measurements, not estimates of improvement. Each change counts once across its workflows; reruns do not add samples.

| Metric | Median | p95 |
| --- | ---: | ---: |
| Until the entire documented check set finishes (minutes) | 58.12 | 74.22 |
| Quality-gate creation-to-completion latency (minutes) | 58.12 | 74.22 |
| Quality-gate execution, excluding idle gaps (minutes) | 14.87 | 21.98 |
| Quality-gate first-start-to-completion elapsed time (minutes) | 21.48 | 67.60 |
| Individual job queue/dependency wait (seconds; 368 jobs) | 117.00 | 3,185.00 |
| Sum of job queue/dependency waits per change (minutes) | 168.14 | 747.87 |
| Aggregate runner minutes per change | 56.46 | 84.83 |

p95 uses the nearest-rank method. Required-check and quality-gate latency include queues and retries; quality-gate execution is the union of execution intervals for its four canonical lanes and aggregation job, counting overlap once and excluding queue-only or manual-retry gaps. Queue/dependency waits sum job waits and can exceed wall-clock completion time. Runner minutes sum execution across every relevant workflow and attempt, without billing rounding or multipliers.

## Post-rollout main-push observations

Five main-push changes completed in the post window. Their documented checks completed, but each change had unsuccessful work in another relevant workflow. The following distributions include that unsuccessful work and are separate from the successful PR cohort. Missing whole-push boundaries and the different outcomes prevent a matched comparison. With five observations, nearest-rank p95 is the maximum.

| Metric | Median | p95 |
| --- | ---: | ---: |
| Until the entire documented check set finishes (minutes) | 14.48 | 21.32 |
| Quality-gate creation-to-completion latency (minutes) | 12.67 | 14.48 |
| Quality-gate execution, excluding idle gaps (minutes) | 12.57 | 14.42 |
| Individual job queue/dependency wait (seconds; 107 jobs) | 2.00 | 4.00 |
| Sum of job queue/dependency waits per change (minutes) | 1.47 | 1.87 |
| Aggregate runner minutes per change, including unsuccessful work | 87.95 | 101.28 |

These observations correspond to commits `06e785246d`, `36bf297586`, `3f971a3679`, `94c53780d5` and `b50b61114b`; the full report retains their workflow and attempt evidence.

## Separate event and unsuccessful-attempt accounting

Counts below include all outcomes, not just the successful comparison cohort. A successful change requires the documented checks and every relevant workflow to succeed. Unknown durations remain unavailable. Known minutes are lower bounds that retain the measured jobs of partially observed attempts.

| Period / event | Completed changes | All-workflow successful changes | Comparable changes | Attempts | Known runner minutes | Attempts with unknown total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline / pull_request | 78 | 35 | 32 | 646 | 4,769.27 | 0 |
| baseline / main_push | 15 | 11 | 0 | 129 | 1,675.70 | 0 |
| rollout / pull_request | 47 | 24 | 24 | 290 | 2,454.82 | 4 |
| rollout / main_push | 5 | 0 | 0 | 38 | 423.67 | 0 |

| Period / event | Outcome bucket | Attempts | Runner minutes | Known minutes (lower bound) |
| --- | --- | ---: | ---: | ---: |
| baseline / pull_request | failures | 46 | 237.15 | 237.15 |
| baseline / pull_request | cancellations | 112 | 821.60 | 821.60 |
| baseline / pull_request | retries | 1 | 0.80 | 0.80 |
| baseline / pull_request | failureCancellationOrRetry | 158 | 1,058.75 | 1,058.75 |
| baseline / main_push | failures | 4 | 64.45 | 64.45 |
| baseline / main_push | cancellations | 4 | 68.43 | 68.43 |
| baseline / main_push | retries | 1 | 9.72 | 9.72 |
| baseline / main_push | failureCancellationOrRetry | 9 | 142.60 | 142.60 |
| rollout / pull_request | failures | 28 | 276.63 | 276.63 |
| rollout / pull_request | cancellations | 46 | unavailable | 202.30 |
| rollout / pull_request | retries | 1 | 0.90 | 0.90 |
| rollout / pull_request | failureCancellationOrRetry | 74 | unavailable | 478.93 |
| rollout / main_push | failures | 6 | 71.78 | 71.78 |
| rollout / main_push | cancellations | 0 | 0.00 | 0.00 |
| rollout / main_push | retries | 1 | 17.62 | 17.62 |
| rollout / main_push | failureCancellationOrRetry | 7 | 89.40 | 89.40 |

Runner assignment and full step metadata distinguish synthetic starts from actual execution. Confirmed terminal jobs that never started, explicit skips and verified copied retry receipts add zero new runner cost while raw execution timing remains unavailable. Three copied baseline main-push receipts match their original execution under different job IDs; that execution is charged only once. These accounting corrections do not establish rollout savings.

Retry minutes overlap failures and cancellations. Use `failureCancellationOrRetry` for their union; do not add those three buckets. The machine report also retains individual unsuccessful attempts, pending/unknown outcomes and other events. Successful main-push latency can be inspected there; absent whole-push boundaries prevent category-matched main-push comparison.

## Original targets and comparable categories

There are 32 comparable baseline PR changes and 24 comparable rollout PR changes. Strict matching yields 6 pairs, leaving 26 baseline and 18 rollout observations unmatched. Matching requires the same event, exact-diff area category and file-count band, required-check identities, runner classes, attempt pattern and optional specialist workflow/job coverage. Exact paths must also agree across the workflows describing each individual change.

| Matched category | Pairs |
| --- | ---: |
| ci-tooling+docs / 6-20 files | 1 |
| docs / 1-5 files | 1 |
| docs / 6-20 files | 4 |

| Original target | Outcome |
| --- | --- |
| 25% lower median quality-gate execution | Unevaluated: fewer than 20 matched pairs and unverified historical required-check equivalence. |
| 30% fewer aggregate runner minutes | Unevaluated for the same reasons; unmatched samples are excluded from comparison. |

The report emits no reduction percentage for either target. Changed specialist coverage is not treated as a saving. Twenty post observations alone do not supply a matched baseline or prove causality. These rollout targets remain separate from the five-pair, 15% benchmark adoption criterion.

## The 24 completed post-rollout observations

Each link opens its Meridian CI workflow run. The source snapshot retains all companion workflows and attempts.

| PR | Commit | Meridian CI run | Category | All checks (min) | Runner minutes |
| ---: | --- | --- | --- | ---: | ---: |
| 3051 | `163a2c9d4b` | [37080333892](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37080333892) | docs+dotnet / 6-20 files | 18.85 | 56.70 |
| 3048 | `20cf94e58d` | [37081394392](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37081394392) | ci-tooling+docs+dotnet+other / 101+ files | 19.85 | 79.98 |
| 3051 | `8c98d0b9fc` | [37085477287](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37085477287) | docs+dotnet / 1-5 files | 15.55 | 52.13 |
| 3052 | `3a1ac72db2` | [37086067445](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37086067445) | docs+dotnet+other / 21-100 files | 14.45 | 71.70 |
| 3049 | `5c33a38a04` | [37086209059](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37086209059) | ci-tooling+docs / 6-20 files | 20.08 | 61.22 |
| 3053 | `d10a6df51c` | [37135880097](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37135880097) | docs / 6-20 files | 20.05 | 55.38 |
| 3031 | `cf00a266cc` | [37166220392](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166220392) | no-file-change / 0 files | 64.52 | 54.35 |
| 2981 | `7f494ad0a1` | [37166256028](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166256028) | dotnet / 1-5 files | 65.78 | 67.20 |
| 3011 | `b75081030f` | [37166294571](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166294571) | docs / 1-5 files | 57.23 | 55.40 |
| 3037 | `36418a6cb8` | [37166294306](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166294306) | docs / 6-20 files | 65.55 | 49.50 |
| 3051 | `41672edd28` | [37166348536](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166348536) | docs+dotnet / 1-5 files | 70.15 | 55.97 |
| 3030 | `1cfd6a4ee5` | [37166480434](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166480434) | docs / 1-5 files | 77.20 | 48.65 |
| 2903 | `95b249be09` | [37166630150](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166630150) | browser+docs / 1-5 files | 59.00 | 61.80 |
| 3016 | `35b86338ab` | [37166756770](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166756770) | docs+dotnet / 6-20 files | 68.47 | 71.18 |
| 2307 | `d973ff3562` | [37166807169](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166807169) | browser+docs / 101+ files | 56.83 | 73.58 |
| 2930 | `7cc3e1410c` | [37166807830](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166807830) | docs+dotnet / 21-100 files | 61.97 | 60.37 |
| 2896 | `0aedef5413` | [37166899712](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166899712) | browser+docs+dotnet / 101+ files | 72.00 | 89.32 |
| 3049 | `4d1d77398d` | [37166904733](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37166904733) | ci-tooling+docs / 6-20 files | 74.22 | 53.78 |
| 2897 | `8608f6ccff` | [37167014124](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37167014124) | browser+desktop+docs+dotnet+other / 101+ files | 67.92 | 84.83 |
| 3012 | `1ba5f5d16b` | [37167379577](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37167379577) | docs / 6-20 files | 64.67 | 51.82 |
| 3055 | `33983829e7` | [37192008493](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37192008493) | docs+dotnet / 6-20 files | 20.45 | 66.32 |
| 3051 | `4b15bc26f4` | [37194696679](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37194696679) | docs+dotnet / 1-5 files | 19.93 | 54.32 |
| 3051 | `6af4f054cd` | [37195998121](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37195998121) | docs+dotnet / 1-5 files | 19.88 | 55.37 |
| 3056 | `5800e85b57` | [37216188225](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37216188225) | docs / 6-20 files | 20.43 | 56.22 |

## Five-pair benchmark and validation evidence

[Benchmark evidence](actions-local-benchmarks-2026-10-05.json) retains all 20 measured variants, exact discovery digests, counts, timing, provenance, decisions and both unsuccessful setup attempts.

The five .NET pairs (2/4 processes) and five browser pairs (8/16 files per batch) ran on one local Linux executor at test-source commit `e5783987f743bea998aaa59c53c44490a7c1f3ce`. The executor had a four-CPU quota and 16 GiB memory limit, .NET SDK 10.0.100 and Node v24.19.0. Vitest retained two workers. Pair order alternated, and measured variants ran sequentially. The benchmark working tree contained this collector/report change; selected .NET and browser test sources matched the recorded commit. These benchmarks measure that fixed commit; canonical CI validates subsequent integration with main.

GitHub-hosted benchmark dispatch was unavailable in this session. These local measurements cannot authorize hosted-default promotion or establish the hosted rollout targets. `testSeconds` excludes .NET build/restore; the JSON separately preserves complete command durations. Browser dependencies were restored once with `npm ci` outside every measured interval.

### .NET five-pair results

10 successful and 0 unsuccessful measured variants.
Every variant recorded 18,444 passed, 0 failed, 5 skipped and 0 unknown outcomes.

| Pair | Execution order | Baseline test seconds | Candidate test seconds | Exact discovery matches |
| ---: | --- | ---: | ---: | --- |
| 1 | baseline then candidate | 423.41 | 323.30 | no |
| 2 | candidate then baseline | 446.62 | 320.62 | no |
| 3 | baseline then candidate | 449.85 | 351.61 | no |
| 4 | candidate then baseline | 462.91 | 355.48 | no |
| 5 | baseline then candidate | 470.20 | 360.06 | no |

A valid median paired improvement is unavailable. No reduction percentage is reported for these samples.
Adoption eligible: **no**.

- A valid median paired improvement is unavailable.
- Local benchmark evidence cannot promote hosted defaults.
- Paired testIdentityDigest evidence is absent or differs.

All five .NET pairs differ in three theory display names generated from random `SubjectId` GUIDs in `SecurityMasterAndVaultEvidenceContributorTests`. Exact differing names and original digests are saved in the JSON. Equal passing counts do not satisfy the established exact-discovery criterion; no identity normalization or test change was applied.

### Browser five-pair results

10 successful and 0 unsuccessful measured variants.
Every variant recorded 3,389 passed, 0 failed, 0 skipped and 0 unknown outcomes.

| Pair | Execution order | Baseline test seconds | Candidate test seconds | Exact discovery matches |
| ---: | --- | ---: | ---: | --- |
| 1 | baseline then candidate | 223.08 | 204.62 | yes |
| 2 | candidate then baseline | 231.84 | 202.26 | yes |
| 3 | baseline then candidate | 222.31 | 198.72 | yes |
| 4 | candidate then baseline | 222.12 | 195.69 | yes |
| 5 | baseline then candidate | 228.45 | 204.01 | yes |

The matched local median paired test-time reduction was **10.70%**. This is a local test-time diagnostic, not a hosted runner-minute or rollout savings estimate.
Adoption eligible: **no**.

- Local benchmark evidence cannot promote hosted defaults.
- Median paired test-time reduction is below 15%.

### Unsuccessful setup attempts and validation

| Setup attempt | Outcome and retained evidence |
| ---: | --- |
| 1 | Cancelled after the SDK installed outside the system path could not be found by a QuantScript apphost. The missing `DOTNET_ROOT` was corrected. Partial TRX evidence is retained; complete timing is unavailable. |
| 2 | Baseline: 18,440 passed, 4 failed, 5 skipped; 453.30 test seconds. The container's PID 1 did not reap orphaned descendants, affecting four process-cleanup tests. Its candidate was cancelled; partial evidence and an elapsed-time lower bound are retained. |

The complete five-pair series used `DOTNET_ROOT` and `tini -s` as a child subreaper. A focused seven-test process-runner check passed before restarting the full series. Neither unsuccessful setup was silently replaced with a successful sample; both remain outside the comparable pairs.

Focused collector, metrics, benchmark and related CI checks passed **167 tests**. Actionlint, workflow hygiene, lane-manifest and skip-register checks also passed. Independent review reproduced the archived Actions report exactly from its saved source and policy. The accompanying pull request records the canonical `bash scripts/ci.sh` and hosted integration-check results.

Hosted .NET concurrency remains **2**, local .NET concurrency **1**, browser batch size **8**, and Vitest workers **2**. No production tuning default was changed.

## Reproduce the report

```bash
python build/scripts/ci/ci-metrics.py \
  --input docs/engineering/actions-rollout-source-2026-10-05.json.gz \
  --comparison docs/engineering/actions-rollout-policy-2026-10-05.json \
  --output artifacts/actions-rollout/report.json \
  --markdown artifacts/actions-rollout/report.md
```

For fresh collection, see [CI/CD measurement and promotion](ci-cd-optimization.md#measurement-and-promotion). Collection remains read-only. No workflows, branch rules, GitHub Actions artifacts, concurrency defaults or batch defaults were changed.
