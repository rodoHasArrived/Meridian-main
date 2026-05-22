# Performance Improvement TODO Plan (2026-05-21)

## Benchmark Run Summary (including higher-velocity attempts)

I ran multiple benchmark entry points, including the broad/full lane and a collector-focused lane intended for higher-throughput signal:

1. `make bench-quick`
2. `./benchmarks/run-bottleneck-benchmarks.sh`
3. `./benchmarks/run-bottleneck-benchmarks.sh --quick --filter CollectorBenchmarks`

All three fail immediately in this container with the same prerequisite error:

- `dotnet SDK not found. Install .NET 10.0 SDK first.`

So yes—this pass did attempt to run at a higher velocity/scope level, but benchmark execution is fully blocked until a .NET 10 SDK runtime is available.

## What to run first once runtime is available

Use this order to get fast signal first, then deeper coverage:

1. Fast smoke:
   - `make bench-quick`
2. Full bottleneck sweep:
   - `./benchmarks/run-bottleneck-benchmarks.sh`
3. Collector-heavy throughput focus:
   - `./benchmarks/run-bottleneck-benchmarks.sh --filter CollectorBenchmarks`
4. Pipeline throughput focus:
   - `./benchmarks/run-bottleneck-benchmarks.sh --filter EndToEndPipelineBenchmarks`
5. WAL hot-path focus:
   - `./benchmarks/run-bottleneck-benchmarks.sh --filter WalChecksumBenchmarks`
6. Budget enforcement checks:
   - `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter "Category=Performance" --logger "console;verbosity=normal"`
   - `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter "FullyQualifiedName~AllocationBudgetIntegrationTests" --logger "console;verbosity=normal"`
   - On non-Windows hosts, append `/p:EnableWindowsTargeting=true` to the above `dotnet test` commands.

Archive all outputs under `artifacts/benchmarks/<yyyy-mm-dd>/` and include machine/runtime metadata.

## Higher-velocity benchmark profile (to add next)

To explicitly support “higher velocity level” validation, add a dedicated BenchmarkDotNet job profile and make target:

- New make target (proposed): `make bench-velocity`
- Proposed command shape:
  - `dotnet run --project benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj -c Release -- --filter "*Collector*|*EndToEndPipeline*|*EventPipeline*" --job short --memory`
- Follow-up enhancement:
  - Add a custom BDN job in `benchmarks/Meridian.Benchmarks/Program.cs` for sustained high-throughput runs (longer warmup/iteration, capped max relative error), and wire it to `bench-velocity`.

## Prioritized Performance TODO Backlog

### P0 — Hot-path allocation and lock reductions

1. **Persistent dedup key path hardening (`PersistentDedupLedger`)**
   - Verify `DeduplicationKeyBenchmarks` mean ns/event and alloc bytes/event vs budget.
   - Add cache-hit vs cache-miss benchmark matrix with realistic symbol/type cardinalities.
   - Keep durability behavior identical while reducing miss-path overhead.

2. **WAL append critical section minimization (`WriteAheadLog`)**
   - Measure semaphore hold-time under contention.
   - Keep JSON/hash prep out of lock whenever possible.
   - Add N-producer append contention microbench.

3. **Depth snapshot lock-window shrink (`MarketDepthCollector`)**
   - Benchmark throughput and reader latency at 50/100/200 levels.
   - Copy minimal data under lock; build snapshots outside lock.
   - Track Gen0 collections and alloc/event before/after.

### P1 — Serialization and pipeline throughput

4. **JSONL sink budget hardening (`JsonlStorageSink`)**
   - Validate allocation-flat direct UTF-8 write path across payload sizes.
   - Add regression benchmarks for cache-access/delegate patterns.

5. **End-to-end latency envelope (`EndToEndPipelineBenchmarks`)**
   - Add workload tiers: 1k/s, 10k/s, 50k/s burst.
   - Track p50/p95 append latency and sustained events/sec.
   - Define pass/fail thresholds per tier.

6. **Collector concurrency tuning (`CollectorBenchmarks`)**
   - Split trade/quote/depth mixed-feed scenarios.
   - Isolate lock contention from allocation impact.
   - Keep CI-safe tags for non-SIMD regressions.

### P2 — Governance and regression prevention

7. **Performance budget expansion**
   - Extend `PerformanceBudgetRegistry` coverage to include contention and end-to-end scenarios.
   - Gate collector + sink composite scenarios in CI.

8. **Benchmark artifact normalization**
   - Standardize output metadata: machine, runtime, commit SHA, scenario config.
   - Produce rolling 30-day drift comparisons.

9. **Production-correlated diagnostics**
   - Map benchmarks to operator workflows (readiness/replay/reconciliation).
   - Add lightweight runtime counters to validate workload-shape assumptions.

## Two-sprint execution plan

### Sprint 1
- Execute full benchmark suite on .NET 10 runner.
- Complete P0 items; rebaseline budgets with evidence.
- Introduce `bench-velocity` command path for repeatable high-throughput checks.

### Sprint 2
- Complete P1/P2 items.
- Publish CI trend reports and refreshed bottleneck scorecard.

## Definition of done

- All benchmark commands (including velocity lane) pass in CI and on one reproducible local runner.
- No P0 scenario exceeds configured mean-time or allocation budgets.
- End-to-end benchmark tiers show no unapproved regression versus prior baseline.
- Updated report captures command set, run date, runtime, commit SHA, and before/after deltas.
