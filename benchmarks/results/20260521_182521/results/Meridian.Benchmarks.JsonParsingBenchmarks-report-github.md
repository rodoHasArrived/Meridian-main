```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | Mean       | Error     | StdDev  | Ratio | Rank | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |-----------:|----------:|--------:|------:|-----:|-------:|----------:|------------:|
| ParseTrade_SourceGenerated |   701.5 ns |  13.65 ns | 0.75 ns |  0.84 |    1 | 0.0229 |     384 B |        1.20 |
| ParseTrade_JsonDocument    |   830.3 ns |  12.09 ns | 0.66 ns |  1.00 |    1 | 0.0191 |     320 B |        1.00 |
| ParseQuote_SourceGenerated |   918.3 ns | 108.28 ns | 5.94 ns |  1.11 |    1 | 0.0277 |     472 B |        1.48 |
| ParseQuote_JsonDocument    | 1,073.6 ns |  66.26 ns | 3.63 ns |  1.29 |    1 | 0.0210 |     376 B |        1.18 |
