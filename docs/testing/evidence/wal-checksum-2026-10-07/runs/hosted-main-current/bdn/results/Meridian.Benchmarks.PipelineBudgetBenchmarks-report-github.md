```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 2.30GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean          | Error      | StdDev    | Gen0   | Allocated |
|---------------------------------- |--------------:|-----------:|----------:|-------:|----------:|
| DedupKey_CacheHit                 |    64.1195 ns |  0.0677 ns | 0.0301 ns |      - |         - |
| DedupKey_CacheMiss                |   757.5786 ns |  1.2060 ns | 0.4301 ns |      - |     128 B |
| WalChecksum_Small                 |   729.6985 ns |  1.6236 ns | 0.8492 ns |      - |         - |
| WalChecksum_Medium_1KB            | 1,296.2007 ns |  8.4449 ns | 3.7496 ns |      - |         - |
| WalChecksum_Large_4KB             | 3,586.0055 ns | 16.1925 ns | 8.4690 ns |      - |         - |
| NewlineScan_Portable              |     0.9844 ns |  0.0048 ns | 0.0021 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated |   599.5364 ns |  1.6084 ns | 0.7141 ns | 0.0024 |     384 B |
| AlpacaParse_Quote_SourceGenerated |   744.5017 ns |  2.4068 ns | 1.0686 ns | 0.0030 |     472 B |
