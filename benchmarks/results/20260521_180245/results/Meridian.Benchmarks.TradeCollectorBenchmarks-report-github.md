```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.63GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  InvocationCount=1  IterationCount=3  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  

```
| Method                     | TradeCount | Mean      | Error      | StdDev    | Ratio | RatioSD | Rank | Allocated   | Alloc Ratio |
|--------------------------- |----------- |----------:|-----------:|----------:|------:|--------:|-----:|------------:|------------:|
| ProcessTrades_SingleSymbol | 1000       |  2.014 ms |  0.8774 ms | 0.0481 ms |  0.87 |    0.03 |    1 |   902.57 KB |        0.70 |
| ProcessTrades_WithGaps     | 1000       |  2.289 ms |  0.8778 ms | 0.0481 ms |  0.98 |    0.04 |    1 |  1295.74 KB |        1.00 |
| ProcessTrades_MultiSymbol  | 1000       |  2.328 ms |  1.5515 ms | 0.0850 ms |  1.00 |    0.04 |    1 |  1291.88 KB |        1.00 |
|                            |            |           |            |           |       |         |      |             |             |
| ProcessTrades_SingleSymbol | 10000      | 23.357 ms |  4.3028 ms | 0.2358 ms |  0.90 |    0.04 |    1 |  8847.88 KB |        0.70 |
| ProcessTrades_WithGaps     | 10000      | 25.229 ms | 25.6589 ms | 1.4064 ms |  0.97 |    0.06 |    1 | 12616.05 KB |        1.00 |
| ProcessTrades_MultiSymbol  | 10000      | 25.970 ms | 21.3817 ms | 1.1720 ms |  1.00 |    0.05 |    1 | 12612.13 KB |        1.00 |
