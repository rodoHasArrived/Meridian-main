# Checksum candidate measurements

Measurements completed sequentially on 2026-10-07, using .NET 10.0.0 and AMD EPYC 9V74. `DOTNET_TieredCompilation=0` ensured optimized code before warmup. Each .NET case warmed 20,000 calls, then retained three independent 50,000-call measurements. C diagnostics retained three 100,000-call measurements after warmup. Cgroup throttling did not increase during the complete 50-second sequence.

All .NET measured calls allocated **0 managed bytes**. Both BLAKE3 implementations passed nine official vectors and independently matched hashing the reference encoded record. The varied cases used 256 preallocated records with changing sequence, timestamp, and payload; no result cache was involved.

## Full original production encoding plus one-shot hashing

Values are median nanoseconds per record; parentheses show all retained repetitions' minimum–maximum. The benchmark fixtures encode to 101, 942, and 4138 bytes respectively.

| Implementation | Small: 64-byte payload | Medium: 900-byte payload | Large: 4096-byte payload |
|---|---:|---:|---:|
| Existing budgets | 400 | 600 | 1200 |
| Native BLAKE3, fixed input | 322 (303–332) | 1533 (1505–1665) | 2463 (2458–2531) |
| Native BLAKE3, varied input | 311 (304–343) | 1570 (1523–1731) | 2948 (2700–2962) |
| Managed BLAKE3, fixed input | 335 (328–420) | 1678 (1552–1982) | 8020 (7817–8276) |
| Managed BLAKE3, varied input | 437 (417–509) | 1835 (1807–1864) | 7728 (7660–8183) |
| Reused SHA-256, native-project fixed input | 688 (677–709) | 1285 (1221–1481) | 4081 (3976–4200) |
| Platform SHA-256 one-shot, native-project fixed input | 718 (707–724) | 1572 (1349–1781) | 4141 (3556–4590) |

Neither BLAKE3 implementation meets all three WAL budgets on this runner. The medium record fits within one BLAKE3 chunk and receives no multi-chunk throughput advantage.

## Native SHA-256 compression floor

These deprecated low-level APIs are diagnostics only: they omit production formatting, UTF-8 encoding, managed/native transitions, and OpenSSL provider policy. They are not proposed as application code.

| Encoded input bytes | Median ns | Range ns |
|---:|---:|---:|
| 101 | 88.5 | 87.8–90.3 |
| 942 | 653.2 | 651.8–670.3 |
| 4138 | 2857.0 | 2706.9–3007.8 |

Even this SHA-256 compression floor exceeds the current medium and large budgets. Removing provider context setup alone cannot satisfy the large-stage budget on the measured runner.

The complete raw-payload, encoded-record, and full-core measurements are in `native-measurements.csv`, `managed-measurements.csv`, and `summary.csv`. Native SHA comparisons are retained in `../native/native-sha256-measurements.csv` and its `summary.csv`. `run-environment.json` records exact commands, timestamps, elapsed durations, and cgroup counters. The unchanged portable BenchmarkDotNet suite remains the acceptance gate; these diagnostics do not constitute stage passes.
