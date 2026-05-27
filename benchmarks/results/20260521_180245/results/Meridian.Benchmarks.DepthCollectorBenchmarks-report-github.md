```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.63GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  InvocationCount=1  IterationCount=3  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  

```
| Method            | DepthUpdates | Mean       | Error      | StdDev   | Ratio | RatioSD | Rank | Allocated  | Alloc Ratio |
|------------------ |------------- |-----------:|-----------:|---------:|------:|--------:|-----:|-----------:|------------:|
| SnapshotRetrieval | 100          |   127.0 μs |   682.9 μs | 37.43 μs |  0.58 |    0.18 |    1 |   47.34 KB |        0.27 |
| InsertOnly        | 100          |   226.5 μs |   888.8 μs | 48.72 μs |  1.03 |    0.26 |    2 |  178.26 KB |        1.00 |
| MixedOperations   | 100          |   248.0 μs |   986.5 μs | 54.07 μs |  1.13 |    0.28 |    2 |  145.13 KB |        0.81 |
|                   |              |            |            |          |       |         |      |            |             |
| SnapshotRetrieval | 500          |   107.1 μs |   501.2 μs | 27.47 μs |  0.09 |    0.02 |    1 |   47.34 KB |        0.04 |
| InsertOnly        | 500          | 1,161.9 μs |   179.6 μs |  9.84 μs |  1.00 |    0.01 |    2 | 1338.01 KB |        1.00 |
| MixedOperations   | 500          | 1,235.9 μs | 1,515.4 μs | 83.06 μs |  1.06 |    0.06 |    2 | 1398.95 KB |        1.05 |
