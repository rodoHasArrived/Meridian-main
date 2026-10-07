# Managed/native checksum candidate component profile

The two standalone projects in `/workspace/wal-hash-candidates` compile the same retained `Program.cs` against `Blake3` 3.0.2 (managed) and `Blake3.Native` 3.0.2 (official Rust implementation wrapper). Neither project changes production code or the portable benchmark suite.

Each compares platform SHA-256 one-shot, a reused SHA-256 instance, and BLAKE3 one-shot with exactly 32 output bytes. Paths measure raw payload hashing, hashing the pre-encoded complete WAL record, and full original production record encoding plus hashing. Payloads contain 64, 900, and 4096 ASCII characters. Fixed cases reproduce the portable benchmark record; varied cases cycle through 256 preallocated fixtures with different sequences, timestamps, and payload content. No input or result memoization is used.

The full-core source retains the original 4608-byte stack buffer, UTF-8 byte counts, formatting, pool fallback, and encoding order. Fixture creation, native loading, warmup, official-vector validation, and measurement output are outside measured operations. Every repetition reports elapsed mean nanoseconds and actual managed bytes allocated per call via `GC.GetAllocatedBytesForCurrentThread`.

Before timing, each project verifies nine official BLAKE3 vectors (empty input, block boundaries, chunk boundaries, and 4096 bytes) and independently checks that the full-core digest equals hashing the reference UTF-8 encoded record for all 256 fixtures.

Build commands already run:

```sh
/workspace/dotnet-10.0.100/dotnet build /workspace/wal-hash-candidates/managed/Profile.csproj -c Release
/workspace/dotnet-10.0.100/dotnet build /workspace/wal-hash-candidates/native/Profile.csproj -c Release
```

Run sequentially only after the portable baseline completes and competing CPU-intensive work stops:

```sh
/workspace/dotnet-10.0.100/dotnet /workspace/wal-hash-candidates/native/bin/Release/net10.0/Profile.dll 100000 5 > native-measurements.csv 2> native-run.log
/workspace/dotnet-10.0.100/dotnet /workspace/wal-hash-candidates/managed/bin/Release/net10.0/Profile.dll 100000 5 > managed-measurements.csv 2> managed-run.log
```

The component profiles explain candidate performance. The unchanged portable BenchmarkDotNet suite remains the acceptance gate.
