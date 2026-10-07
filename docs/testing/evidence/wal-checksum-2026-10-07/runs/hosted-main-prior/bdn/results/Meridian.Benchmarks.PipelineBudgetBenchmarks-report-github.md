```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean         | Error      | StdDev    | Gen0   | Allocated |
|---------------------------------- |-------------:|-----------:|----------:|-------:|----------:|
| DedupKey_CacheHit                 |    58.484 ns |  0.1086 ns | 0.0568 ns |      - |         - |
| DedupKey_CacheMiss                |   800.227 ns |  9.2692 ns | 4.1156 ns | 0.0032 |     128 B |
| WalChecksum_Small                 |   774.554 ns |  1.6104 ns | 0.7150 ns |      - |         - |
| WalChecksum_Medium_1KB            | 1,460.253 ns |  5.5696 ns | 2.9130 ns |      - |         - |
| WalChecksum_Large_4KB             | 4,069.947 ns | 16.6312 ns | 7.3844 ns |      - |         - |
| NewlineScan_Portable              |     1.406 ns |  0.0093 ns | 0.0049 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated |   736.834 ns |  3.6225 ns | 1.8947 ns | 0.0148 |     384 B |
| AlpacaParse_Quote_SourceGenerated |   971.836 ns |  3.2815 ns | 1.4570 ns | 0.0156 |     472 B |
