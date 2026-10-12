```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 4.31GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean        | Error      | StdDev    | Gen0   | Allocated |
|---------------------------------- |------------:|-----------:|----------:|-------:|----------:|
| DedupKey_CacheHit                 |  42.3851 ns |  1.5724 ns | 0.8224 ns |      - |         - |
| DedupKey_CacheMiss                | 326.9993 ns |  5.8910 ns | 3.0811 ns | 0.0065 |     128 B |
| WalChecksum_Small                 | 154.6903 ns |  7.2262 ns | 3.7794 ns |      - |         - |
| WalChecksum_Medium_1KB            | 170.5015 ns | 17.9149 ns | 7.9543 ns |      - |         - |
| WalChecksum_Large_4KB             | 357.7136 ns |  3.8486 ns | 1.7088 ns |      - |         - |
| NewlineScan_Portable              |   0.7029 ns |  0.0335 ns | 0.0149 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated | 367.0780 ns |  9.7268 ns | 5.0873 ns | 0.0228 |     384 B |
| AlpacaParse_Quote_SourceGenerated | 468.8294 ns | 13.7843 ns | 7.2095 ns | 0.0270 |     472 B |
