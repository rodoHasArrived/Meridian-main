# Bounded pipeline budget benchmark (PRD-112)

The [Pipeline Budget Benchmark](../../.github/workflows/pipeline-benchmark.yml) lane
measures all eight portable stages in the existing
[`PerformanceBudgetRegistry`](../../benchmarks/Meridian.Benchmarks/Budget/PerformanceBudgetRegistry.cs).
It runs for relevant pull requests and main pushes, or by manual dispatch. Workflow
changes are designated for **human governance review**. This adds evidence without
changing the canonical `quality-gate` or repository protection rules.

## Reproduce the run

Use Linux x86_64 with .NET SDK 10.0.100, runtime 10.0.0 and Python 3.11 or newer:

```bash
python3 build/scripts/ci/benchmark-pipeline.py --local
```

The [recorded profile](../../build/config/testing/pipeline-benchmark-profile.json)
fixes the Release build, runtime, GC/tiering settings, one launch, three warmups,
eight measured iterations and a 250 ms target per iteration. Restore and the initial
build are capped at five minutes each; BenchmarkDotNet's generated build is capped
at ten minutes within the twenty-minute benchmark execution cap. The hosted job
is capped at thirty-five minutes. Timeouts terminate the benchmark process group.
There is no parameter matrix or user-supplied benchmark filter.
The harness starts BenchmarkDotNet in the benchmark project directory, whose
small solution file bounds project discovery and avoids following the dashboard's
`node_modules/meridian-tools` symlink back through the entire repository.

The hosted runner label is `ubuntu-24.04`; SDK setup uses an isolated install
directory so the repository's normal SDK roll-forward cannot select a preinstalled
newer SDK. Each run records the actual CPU model,
CPU count/affinity, memory and cgroup limits, kernel/OS, runner image, `dotnet --info`
and installed runtime list; full BenchmarkDotNet reports also retain the measured
process runtime/host information. Local evidence is marked local, and dirty trees
are marked explicitly. Hosted evidence requires a clean checkout matching
`GITHUB_SHA` (the tested merge commit on a pull request).

Hosted CPUs and contention can vary: compare equivalent recorded profiles before
claiming a regression or improvement. A violation still fails the lane. Never
relax the existing thresholds to accommodate a noisy runner. Establishing a
dedicated long-term reference host remains a separate infrastructure decision.

## Measurement boundary

[`PipelineBudgetBenchmarks`](../../benchmarks/Meridian.Benchmarks/PipelineBudgetBenchmarks.cs)
uses fixed timestamps and fixtures matching the existing allocation tests:

| Stages | Measured work |
| --- | --- |
| `DedupKey_CacheHit`, `DedupKey_CacheMiss` | Seeded in-memory ledger lookup and fresh key computation with a warm prefix cache. Persistence is outside these stage budgets. |
| `WalChecksum_Small`, `WalChecksum_Medium_1KB`, `WalChecksum_Large_4KB` | Production checksum core for 64/900/4096-byte ASCII payloads, using the existing benchmark hook. Final hex-string materialization and disk I/O are excluded by the existing allocation contract. The current implementation's 4608-byte stack buffer covers these inputs. |
| `NewlineScan_Portable` | Portable `SearchValues` scan over a 256-byte buffer with fixed 128-byte lines. |
| `AlpacaParse_Trade_SourceGenerated`, `AlpacaParse_Quote_SourceGenerated` | Production source-generated parsing of fixed wire-message fixtures. |

Setup and cleanup are outside measured operations. Every method name matches its
budget exactly. The existing SIMD exclusion remains; no portable budget is waived.
Budgets are exported directly from the registry without copied or adjusted limits.
Mean nanoseconds and allocated bytes per operation are validated; the receipt also
reports `1e9 / mean_ns` as derived stage operations/second. This is not end-to-end
pipeline throughput. Sustained-load soak, backpressure, disk throughput, long-run
resource stability and Python skip inventory remain separate PRD-112 slices.

## Evidence and failure behavior

Evidence lives under
`artifacts/pipeline-benchmark/<commit>/<run-id>-<attempt>/` and is retained for 30 days
in an artifact named with the commit, Actions run ID and attempt. Every invocation
uses a new directory; an existing hosted run directory is rejected.

- `profile.json` records the requested profile; `run.json` records hardware/runtime,
  commit, dirty state, exact commands, command exit codes, timings, conclusion and
  SHA-256 hashes of retained files.
- `bdn/perf-budgets.json` contains all unchanged registry budgets.
- `bdn/results/*-report-full.json` retains raw samples, statistics, allocations and
  BenchmarkDotNet host details; other BDN exports and logs remain alongside it.
- `budget-evidence.json` is the existing validator's per-stage result;
  `budget-validation.log` retains its output even when malformed or absent input
  prevents a JSON verdict. Restore, build and benchmark logs are also retained.

The harness always invokes `build/scripts/validate_budget.py --fail-on-violation`
with `--json-output`, including after failed setup/build/benchmark commands.
Any command failure, timeout, absent stage, invalid measurement, incomplete full
report, missing validator evidence or exceeded allocation/latency budget fails the
lane. Full-report completeness also requires exactly one row per selected stage,
so duplicate or unrelated results cannot satisfy missing coverage. Upload runs on
failure too; no measurements or validator verdict are fabricated.

## Retention rationale and storage measurement

This bounded lane produces routine regression diagnostics, not an adopted long-term
reference-host baseline or release/recovery certification package. PRD-112 does not
specify a benchmark retention minimum. Thirty days provides a conservative investigation
window for both passing and failing runs without applying the separate 90-day
[certification evidence contract](ci-cd-optimization.md) to every benchmark attempt.
The complete payload, commit/run/attempt identity, hashes and failure upload are unchanged.
Any run adopted as a baseline or certification dependency must be preserved separately,
with its full provenance, under an approved evidence policy before this artifact expires.
Human governance review remains required before workflow merge/activation; this policy
does not shorten existing artifact expiries or change certification/recovery retention.

The October 6 observation of
[run 37538763559](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37538763559)
measured artifact `11447312906` at **44,679 compressed bytes** (15 files, 348,059 bytes
uncompressed), bound to tested merge commit `afa03f69ca08cf50eae9bb9d2683d73ba93302e0`.
The generated benchmark build timed out and no benchmarks executed; the validator
rejected incomplete measurements. This measures failure evidence only, not the size of
a complete successful run. Its existing 90-day expiry is January 4, 2027.

Following the [Actions storage audit](actions-storage-audit-2026-10-05.md), use compressed
artifact API `size_in_bytes` and observed upload frequency: steady retained bytes are
approximately `average bytes per upload × uploads per day × retention days`. Count
PR synchronizations, main pushes, manual dispatches and rerun attempts, including failures.
There is no schedule; the first two runs cannot establish a representative daily cadence.

| Illustrative uploads/day, each equal to the observed incomplete artifact | 90 days | 30 days | Reduction |
| --- | ---: | ---: | ---: |
| 1 | 3.835 MiB | 1.278 MiB | 2.557 MiB |
| 5 | 19.174 MiB | 6.391 MiB | 12.783 MiB |

These are failure-size scenarios, not a successful-run forecast or account billing estimate
(MiB = 2^20 bytes). At equal size and cadence, 90 to 30 days reduces steady retained
storage by 66.7%. Before activation, record representative complete-run artifact sizes
and an observed cadence window; calculate successful and failed uploads separately if
their sizes differ. No complete hosted measurement archive was available in this snapshot.
GitHub accrues storage hourly, so shorter retention reduces future exposure and does not
reverse already accrued usage; see [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions).

Harness regression coverage uses a fake .NET executable and the **real validator**
to exercise the command boundary independently of machine speed:

```bash
python3 -m unittest discover -s tests/scripts -p test_benchmark_pipeline.py
python3 -m pip install pytest==8.4.2
python3 -m pytest build/scripts/tests/test_validate_budget.py -q
```
