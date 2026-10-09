```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
AMD EPYC 9V74 2.60GHz, 1 CPU, 5 logical and 5 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean       | Error       | StdDev      | Gen0   | Allocated |
|---------------------------------- |-----------:|------------:|------------:|-------:|----------:|
| DedupKey_CacheHit                 |  63.616 ns |   7.2679 ns |   3.2270 ns |      - |         - |
| DedupKey_CacheMiss                | 935.735 ns | 253.0905 ns | 112.3738 ns | 0.0042 |     128 B |
| WalChecksum_Small                 | 223.096 ns |  31.5334 ns |  16.4926 ns |      - |         - |
| WalChecksum_Medium_1KB            | 269.983 ns |  33.1524 ns |  14.7199 ns |      - |         - |
| WalChecksum_Large_4KB             | 570.700 ns |  66.2036 ns |  29.3948 ns |      - |         - |
| NewlineScan_Portable              |   1.977 ns |   0.2701 ns |   0.1199 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated | 702.937 ns | 138.3685 ns |  61.4365 ns | 0.0224 |     384 B |
| AlpacaParse_Quote_SourceGenerated | 835.951 ns |  89.9392 ns |  39.9336 ns | 0.0256 |     472 B |
