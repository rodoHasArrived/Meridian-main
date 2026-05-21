```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                      | Mean     | Error    | StdDev   | Ratio | Rank | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |---------:|---------:|---------:|------:|-----:|-------:|----------:|------------:|
| TryPublish_PilotFilter_Skip | 11.05 ns | 0.054 ns | 0.003 ns |  0.13 |    1 |      - |         - |        0.00 |
| TryPublish_CanonicalOnly    | 87.22 ns | 4.458 ns | 0.244 ns |  1.00 |    2 | 0.0105 |     176 B |        1.00 |
| TryPublish_DualWrite        | 90.33 ns | 9.806 ns | 0.538 ns |  1.04 |    2 | 0.0105 |     176 B |        1.00 |
| TryPublish_DualWrite_Quote  | 91.31 ns | 4.304 ns | 0.236 ns |  1.05 |    2 | 0.0105 |     176 B |        1.00 |
