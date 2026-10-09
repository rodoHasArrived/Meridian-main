```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean       | Error     | StdDev    | Gen0   | Allocated |
|---------------------------------- |-----------:|----------:|----------:|-------:|----------:|
| DedupKey_CacheHit                 |  60.250 ns | 0.1013 ns | 0.0530 ns |      - |         - |
| DedupKey_CacheMiss                | 716.495 ns | 6.1664 ns | 3.2251 ns | 0.0057 |     128 B |
| WalChecksum_Small                 | 199.240 ns | 0.6941 ns | 0.3082 ns |      - |         - |
| WalChecksum_Medium_1KB            | 253.192 ns | 0.7671 ns | 0.4012 ns |      - |         - |
| WalChecksum_Large_4KB             | 574.056 ns | 2.9535 ns | 1.3114 ns |      - |         - |
| NewlineScan_Portable              |   2.469 ns | 0.0204 ns | 0.0107 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated | 726.600 ns | 5.5418 ns | 2.8985 ns | 0.0204 |     384 B |
| AlpacaParse_Quote_SourceGenerated | 950.710 ns | 2.1659 ns | 0.9617 ns | 0.0267 |     472 B |
