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
| DedupKey_CacheHit                 |  59.251 ns | 0.2390 ns | 0.1061 ns |      - |         - |
| DedupKey_CacheMiss                | 835.531 ns | 8.4600 ns | 3.7563 ns | 0.0067 |     128 B |
| WalChecksum_Small                 | 196.866 ns | 0.6275 ns | 0.2786 ns |      - |         - |
| WalChecksum_Medium_1KB            | 250.886 ns | 0.3252 ns | 0.1444 ns |      - |         - |
| WalChecksum_Large_4KB             | 649.619 ns | 0.6881 ns | 0.3599 ns |      - |         - |
| NewlineScan_Portable              |   2.432 ns | 0.0069 ns | 0.0031 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated | 728.788 ns | 7.5111 ns | 3.9285 ns | 0.0205 |     384 B |
| AlpacaParse_Quote_SourceGenerated | 911.533 ns | 7.6055 ns | 3.9778 ns | 0.0255 |     472 B |
