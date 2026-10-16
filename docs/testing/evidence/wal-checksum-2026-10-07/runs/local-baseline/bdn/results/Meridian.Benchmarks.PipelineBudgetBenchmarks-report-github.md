```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
AMD EPYC 9V74 2.60GHz, 1 CPU, 5 logical and 5 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean         | Error        | StdDev      | Gen0   | Allocated |
|---------------------------------- |-------------:|-------------:|------------:|-------:|----------:|
| DedupKey_CacheHit                 |    56.792 ns |     5.811 ns |   2.5802 ns |      - |         - |
| DedupKey_CacheMiss                |   847.912 ns |   124.660 ns |  55.3498 ns | 0.0061 |     128 B |
| WalChecksum_Small                 |   854.762 ns |    73.877 ns |  32.8020 ns |      - |         - |
| WalChecksum_Medium_1KB            | 1,603.454 ns |   354.937 ns | 185.6387 ns |      - |         - |
| WalChecksum_Large_4KB             | 5,053.999 ns | 1,665.128 ns | 870.8941 ns |      - |         - |
| NewlineScan_Portable              |     1.645 ns |     1.097 ns |   0.5738 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated |   733.887 ns |   106.158 ns |  47.1348 ns | 0.0226 |     384 B |
| AlpacaParse_Quote_SourceGenerated | 1,104.177 ns |   240.394 ns | 106.7365 ns | 0.0249 |     472 B |
