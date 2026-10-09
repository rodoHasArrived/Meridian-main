```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean       | Error      | StdDev    | Gen0   | Allocated |
|---------------------------------- |-----------:|-----------:|----------:|-------:|----------:|
| DedupKey_CacheHit                 |  60.250 ns |  1.3816 ns | 0.6134 ns |      - |         - |
| DedupKey_CacheMiss                | 823.729 ns | 13.4863 ns | 5.9880 ns | 0.0066 |     128 B |
| WalChecksum_Small                 | 202.600 ns |  0.3551 ns | 0.1857 ns |      - |         - |
| WalChecksum_Medium_1KB            | 246.756 ns |  0.4066 ns | 0.1805 ns |      - |         - |
| WalChecksum_Large_4KB             | 581.680 ns |  2.3361 ns | 1.0372 ns |      - |         - |
| NewlineScan_Portable              |   2.481 ns |  0.0172 ns | 0.0076 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated | 729.215 ns |  8.8407 ns | 3.9253 ns | 0.0203 |     384 B |
| AlpacaParse_Quote_SourceGenerated | 931.821 ns |  2.6571 ns | 1.1798 ns | 0.0260 |     472 B |
