# Endpoint fixture isolation and concurrency evidence

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Ordinary endpoint tests own their configuration, authentication settings, provider catalog,
temporary data, and dependency injection container through `EndpointTestFixture`. Changing one
fixture's credentials or disposing its host must not alter another fixture or the process
environment. Configure the fixture or replace its services instead of setting environment
variables or assigning `ProviderCatalog` static callbacks.

Tests that verify production environment lookup, global provider binding, or process startup
still require an xUnit collection with `DisableParallelization = true`. Keep the existing
`Sequential` and `IdentityEnvironment` boundaries for those tests. Removing a shared endpoint
collection from an ordinary class also requires checking inherited collection attributes on
`EndpointIntegrationTestBase`.

## Benchmark before changing the concurrency default

Use the same built test assembly for both modes. The benchmark selects six independent endpoint
classes covering health, negative requests, providers, status, storage, and symbols. It excludes
startup/environment tests. The selected classes must have independent xUnit collections;
parallel runner settings cannot override a shared or explicitly disabled collection.

After building `tests/Meridian.Tests/Meridian.Tests.csproj` in `Release`, run:

```bash
python build/scripts/ci/benchmark-endpoints.py \
  --assembly tests/Meridian.Tests/bin/Release/net10.0/Meridian.Tests.dll \
  --output artifacts/endpoint-concurrency/first-comparison
```

Use `--assembly` to select the actual output path when using `MeridianBuildIsolationKey`. The
script accepts `--dotnet` for an SDK outside `PATH`. It creates a new output directory and never
builds, edits source, changes runner defaults, or starts separate test processes concurrently.
Each process runs the same filtered tests once, using xUnit runsettings to compare one serial
worker with two concurrent workers.

The default comparison includes one warmup of each mode and five measured pairs. Pair order
alternates to reduce cache/order bias. All tests must pass without skips, all runs must have the
same test identity digest, and the assembly hash must remain unchanged. TRX timestamps must
show overlapping independent classes in the concurrent samples and no overlap in serial
samples. A setting change that leaves tests in one serialized collection is rejected as invalid
benchmark evidence.

Review `summary.json`, the individual TRX files, and test logs. The summary records host details,
SDK, assembly hash, test counts, raw run times, median wall/test time, and observed class overlap.
Wall time includes test-runner startup; TRX run time provides the corresponding test-host view.
Enable concurrency only after isolation tests and both modes pass, concurrent execution is
observed, and the measured benefit justifies the setting on the target runner. A faster local
run does not replace the required GitHub Actions checks. Keep serialization if the benchmark is
blocked, inconclusive, or regresses.

## Evidence for this change

On 2026-10-05, the command above was run with output directory
`artifacts/endpoint-concurrency/comparison` against the `Release` assembly on Linux x64,
.NET SDK 10.0.401. The container had a four-CPU quota (`cpu.max = 400000 100000`) and 16 GiB
memory limit; Python reported five host CPUs. No builds or other test runs competed with these
measurements. The project default remained serial during measurement; only the benchmark's
runsettings enabled the candidate's two-worker mode.

All 130 selected tests passed in each of the 12 runs: two warmups and ten measured samples.
Every run had the same test identity digest and the assembly remained unchanged. Every measured
serial run showed one active class; every concurrent run showed two overlapping classes.

| Measurement | Serial | Two workers |
| --- | ---: | ---: |
| Median wall time | 22.904 s | 14.594 s |
| Median TRX run time | 22.592 s | 14.278 s |
| Wall-time range across five measurements | 22.702–23.873 s | 14.077–15.489 s |
| Passed / failed / skipped, each run | 130 / 0 / 0 | 130 / 0 / 0 |

The measured subset reduced median wall time by **36.3% (1.57× speedup)**. The
[committed evidence receipt](evidence/endpoint-fixture-concurrency.json) retains the raw warmup
and paired timings, hardware limits, assembly hash, and test identity digest. Full TRX files and
logs remain in the local output directory.

After this benchmark and serial correctness validation, ordinary endpoint classes were released
into independent per-class xUnit collections using the existing two-worker shared runner setting.
The `Endpoint` collection retains `DisableParallelization = true` for
`EndpointTestFixtureProcessCompatibilityTests`; `PilotAcceptanceHarnessTests` remains in
`Sequential`, and identity/process-startup tests keep their existing serialization boundaries.
The 36.3% improvement applies to the six measured classes only. Full-suite concurrent correctness
and GitHub Actions are separate from that performance measurement.

The subsequent full concurrent endpoint run passed **696/696 tests with zero skips** across
50 classes, with at most two overlapping classes. TRX interval checks confirmed zero overlapping
peers for `EndpointTestFixtureProcessCompatibilityTests`, `PilotAcceptanceHarnessTests`,
`LoginSessionMiddlewarePrincipalTests`, and `OperationalProblemDetailsEndpointTests`, verifying
that their serialization boundaries remained effective. This is local correctness evidence;
the required repository gate and GitHub Actions results are recorded separately.
