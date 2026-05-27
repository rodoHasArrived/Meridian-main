```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | EventCount | Mean      | Error     | StdDev   | Ratio | Rank | Gen0     | Allocated  | Alloc Ratio |
|--------------------------- |----------- |----------:|----------:|---------:|------:|-----:|---------:|-----------:|------------:|
| ProcessBatch_CanonicalOnly | 1000       |  89.23 μs | 11.892 μs | 0.652 μs |  1.00 |    1 |  10.4980 |  171.88 KB |        1.00 |
| ProcessBatch_DualWrite     | 1000       |  92.78 μs | 17.426 μs | 0.955 μs |  1.04 |    1 |  10.4980 |  171.88 KB |        1.00 |
|                            |            |           |           |          |       |      |          |            |             |
| ProcessBatch_CanonicalOnly | 10000      | 888.28 μs |  9.526 μs | 0.522 μs |  1.00 |    1 | 104.4922 | 1718.75 KB |        1.00 |
| ProcessBatch_DualWrite     | 10000      | 949.20 μs | 24.262 μs | 1.330 μs |  1.07 |    1 | 104.4922 | 1718.75 KB |        1.00 |
