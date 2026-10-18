# Lossless LZ4 plus SHA-256 candidate diagnostic

Standalone diagnostic project/source and every repetition are retained here. This candidate was investigated and rejected; it is not production code.

```sh
/workspace/dotnet-10.0.100/dotnet build /workspace/wal-hash-candidates/lz4/Profile.csproj -c Release
DOTNET_TieredCompilation=0 /workspace/dotnet-10.0.100/dotnet /workspace/wal-hash-candidates/lz4/bin/Release/net10.0/Profile.dll 10000 3 > measurements.csv 2> run.log
```

The matrix covers ASCII and representative trade/book JSON at 64, 900, and 4096 bytes; fixed records and 256 varied records; original canonical text versus length-prefixed binary metadata; explicit stack clearing versus `[SkipLocalsInit]`; raw encoding, always LZ4, and raw-through-512-bytes followed by LZ4; and reused SHA-256 versus reused IncrementalHash. Every case warms 10,000 calls and retains three separate 10,000-call measurements. Mode and original input length are included in the hashed bytes to make raw/compressed alternatives disjoint. No input or result caching is used.

The normal-init control explicitly clears the stack buffer inside the same `[SkipLocalsInit]` method. The earlier candidate profiler retains the original compiler-zeroed method. This matrix is an exploratory component comparison, not the portable acceptance suite.

All **864 measurement rows allocated 0 managed bytes**. No measured variant meets the three existing WAL budgets. With binary metadata and skipped local initialization, always-LZ4 plus reused SHA-256 gave fixed ASCII medians of **1849 / 2137 / 2807 ns** and fixed JSON medians of **2592 / 4152 / 11405 ns**. Reused IncrementalHash did not provide a stable material improvement on this noisy runner.

K4os.Compression.LZ4 1.3.8 uses internal `LL32` or `LL64` encoders selected through process-global `LZ4Codec.Enforce32`; its public API has no per-call architecture-fixed encoder. Before timing, the diagnostic performed **2048 successful lossless roundtrips** at 64, 900, 4096, and 70000 bytes but observed **346 differences in compressed bytes between the x32 and x64 encoders**. Different valid compressed representations cannot serve as canonical checksum input without fixing the encoding algorithm. This architecture issue independently disqualifies the general LZ4 approach.

`measurements.csv` retains every timing/allocation result; `summary.csv` contains per-case medians and full ranges. JSON cases vary numeric market-book fields, while ASCII varied cases change both payload ends as well as sequence and timestamp. All fixtures are allocated outside timing.
