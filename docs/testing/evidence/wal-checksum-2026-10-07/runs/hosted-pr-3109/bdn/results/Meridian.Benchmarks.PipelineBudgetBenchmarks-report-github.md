```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-HDUCDG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=250ms  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean         | Error     | StdDev    | Gen0   | Allocated |
|---------------------------------- |-------------:|----------:|----------:|-------:|----------:|
| DedupKey_CacheHit                 |    58.552 ns | 0.1080 ns | 0.0480 ns |      - |         - |
| DedupKey_CacheMiss                |   703.164 ns | 2.1186 ns | 0.9407 ns | 0.0057 |     128 B |
| WalChecksum_Small                 |   673.788 ns | 0.5110 ns | 0.2269 ns |      - |         - |
| WalChecksum_Medium_1KB            | 1,329.547 ns | 0.8704 ns | 0.4553 ns |      - |         - |
| WalChecksum_Large_4KB             | 3,820.875 ns | 2.6331 ns | 1.1691 ns |      - |         - |
| NewlineScan_Portable              |     2.676 ns | 0.0115 ns | 0.0060 ns |      - |         - |
| AlpacaParse_Trade_SourceGenerated |   657.398 ns | 1.4730 ns | 0.5253 ns | 0.0211 |     384 B |
| AlpacaParse_Quote_SourceGenerated |   844.824 ns | 7.3320 ns | 3.8348 ns | 0.0272 |     472 B |
