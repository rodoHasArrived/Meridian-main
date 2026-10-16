# Deduplication key formatter component diagnostic

The source-cloned standalone project compares the full warmed key-computation path from three retained ledger snapshots:

1. Original manual scalar hexadecimal formatting.
2. Production candidate: synchronous span-bearing ref-struct state passed to `string.Create`, then `Convert.TryToHexStringLower` into the suffix.
3. Scratch-only candidate adding a per-thread reusable SHA-256 provider to (2). This hash-state change was **not applied to production**.

Only class names change when compiling the source snapshots into the profiler. Existing compiled repository DLL dependencies are referenced directly; no repository project graph is rebuilt. Source hashes, Git HEAD, commands, timing-window environment, and cgroup counters are retained in `run-environment.json`.

The same portable benchmark trade fixture is measured as fixed input, plus 256 preallocated varying trade identities. Every implementation warms 100,000 calls before seven separate 100,000-call measurements. Measurement order rotates between repetitions to reduce systematic drift. No event-key or result cache bypass is introduced: each call invokes the production key computation seam. Native hash state reuse retains only provider state, never computed digests.

| Pattern | Implementation | Median ns | Retained range ns | Allocated bytes/call |
|---|---|---:|---:|---:|
| Fixed | Original | 1170 | 929–1639 | 128 |
| Fixed | Span hex formatter | 1062 | 734–2259 | 128 |
| Fixed | Span formatter plus scratch-only SHA reuse | 967 | 697–1131 | 128 |
| Varied 256 | Original | 1127 | 1016–1602 | 128 |
| Varied 256 | Span hex formatter | 1044 | 698–1140 | 128 |
| Varied 256 | Span formatter plus scratch-only SHA reuse | 933 | 780–2294 | 128 |

The formatter saves about 83–108 ns per key in these medians while retaining the single 128-byte string allocation. This runner's wide timing ranges do not demonstrate a reproducible margin below the unchanged 800 ns portable stage budget. The authoritative portable BenchmarkDotNet lane must verify the complete candidate.

Before timing, **576 independent invariant-UTF8/SHA-256 oracle events** were checked against all three implementations, covering trades and quotes, `en-US`/`fr-FR`/`tr-TR`, Unicode venues, long pooled venue buffers, complete first-16-byte truncation, and exact lowercase hexadecimal output. All comparisons passed.

Production changes comprise only the span hex formatter and one exact legacy-key regression theory. The theory uses independently computed trade/quote golden vectors containing all 16 hexadecimal digits, three cultures, and a 768-byte UTF-8 venue. Repository tests and the full integration suite remain the root task's responsibility; no repository build or test run was performed by this diagnostic.

```sh
/workspace/dotnet-10.0.100/dotnet build /workspace/wal-hash-candidates/dedup/Profile.csproj -c Release
DOTNET_TieredCompilation=0 /workspace/dotnet-10.0.100/dotnet /workspace/wal-hash-candidates/dedup/bin/Release/net10.0/Profile.dll > measurements.csv 2> run.log
```

Focused repository checks to run with the root validation:

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PersistentDedupLedgerTests|FullyQualifiedName~AllocationBudgetIntegrationTests" -c Release /p:EnableWindowsTargeting=true
```

`DedupWalOrderingTests` and `WalEventPipelineTests` provide persistence/replay behavior coverage. `ComputeKeyForBenchmark_TradeAndQuote_PreserveLegacySha256Identity` supplies exact identity compatibility coverage that was absent from existing tests.
