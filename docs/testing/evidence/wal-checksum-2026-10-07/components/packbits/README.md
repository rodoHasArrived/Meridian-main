# Exact production PackBits/BLAKE3 core profile

`Profile.csproj` directly links the production `src/Meridian.Storage/Archival/WalChecksum.cs` and references Blake3.Native 3.0.2. `WalChecksum.cs` is the exact source snapshot compiled for these measurements. Source SHA-256 before and after profiling was unchanged: `d184f707b85e6b6830ae4d54c691fb297ccaa6fef6068ee3e1f99a19b798d3bb`. The Git HEAD, source hashes, timestamps, cgroup counters, and exact commands are retained in `run-environment.json`.

Every case warmed 50,000 calls, then retained three separate 50,000-call measurements. `DOTNET_TieredCompilation=0` ensured optimized code before warmup. Inputs/output checks and fixture allocations are outside timing. Digest bytes are consumed into a checksum sink during timing; neither inputs nor results are cached.

All **36 measurement rows allocated 0 managed bytes**.

| Fixture | Payload bytes | Pattern | Median ns | Min–max ns | Existing ASCII budget ns |
|---|---:|---|---:|---:|---:|
| ASCII | 64 | Fixed | 202 | 194–212 | 400 |
| ASCII | 64 | Varied 256 | 209 | 208–216 | 400 |
| ASCII | 900 | Fixed | 296 | 289–321 | 600 |
| ASCII | 900 | Varied 256 | 345 | 309–395 | 600 |
| ASCII | 4096 | Fixed | 887 | 861–967 | 1200 |
| ASCII | 4096 | Varied 256 | 862 | 843–901 | 1200 |
| JSON | 64 | Fixed | 320 | 279–331 | — |
| JSON | 64 | Varied 256 | 359 | 308–400 | — |
| JSON | 900 | Fixed | 1447 | 1255–1586 | — |
| JSON | 900 | Varied 256 | 1338 | 1289–1355 | — |
| JSON | 4096 | Fixed | 2602 | 2556–2729 | — |
| JSON | 4096 | Varied 256 | 2919 | 2713–3234 | — |

The exact production core meets the three existing ASCII fixture budgets in this component profile, with changing input as well as fixed input. Representative market-book JSON retains longer digest input and does not obtain the same compression benefit. These additional JSON timings are diagnostic results, not portable stage passes.

Before timing, an independently written scalar canonical PackBits oracle matched production output for **144 adversarial fixtures**, including literal boundaries, SIMD scan boundaries, long-run remainders, alternating bytes, and random bytes. Production record framing plus digest output matched the independent binary-framing/PackBits oracle for **1536 records**. The official BLAKE3 empty-input vector also passed. No encoder correctness issue was identified in this review.

The complete repetitions are in `measurements.csv`; medians and full ranges are in `summary.csv`. Source, build log, and correctness log are retained alongside. The unchanged portable BenchmarkDotNet suite remains the acceptance gate.
