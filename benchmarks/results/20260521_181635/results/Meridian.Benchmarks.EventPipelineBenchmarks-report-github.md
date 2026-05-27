```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                       | EventCount | Mean         | Error         | StdDev       | Ratio | RatioSD | Rank | Gen0   | Allocated | Alloc Ratio |
|----------------------------- |----------- |-------------:|--------------:|-------------:|------:|--------:|-----:|-------:|----------:|------------:|
| UnboundedChannel             | 1000       |     97.81 μs |     14.628 μs |     0.802 μs |  0.65 |    0.01 |    1 | 0.2441 |    4.7 KB |        0.65 |
| BoundedChannel_DropOldest    | 1000       |    104.92 μs |      7.884 μs |     0.432 μs |  0.70 |    0.01 |    1 | 0.9766 |  16.35 KB |        2.26 |
| BoundedChannel_Capacity50000 | 1000       |    149.37 μs |     40.939 μs |     2.244 μs |  1.00 |    0.02 |    2 | 0.2441 |   7.22 KB |        1.00 |
| BoundedChannel_Capacity10000 | 1000       |    152.42 μs |      6.090 μs |     0.334 μs |  1.02 |    0.01 |    2 | 0.2441 |   7.07 KB |        0.98 |
|                              |            |              |               |              |       |         |      |        |           |             |
| UnboundedChannel             | 10000      |    519.05 μs |    131.899 μs |     7.230 μs |  0.38 |    0.01 |    1 |      - |   6.61 KB |        0.43 |
| BoundedChannel_DropOldest    | 10000      |    841.72 μs |    846.707 μs |    46.411 μs |  0.61 |    0.03 |    2 | 7.8125 | 133.16 KB |        8.59 |
| BoundedChannel_Capacity10000 | 10000      |  1,343.16 μs |  1,091.335 μs |    59.820 μs |  0.98 |    0.04 |    3 |      - |  17.02 KB |        1.10 |
| BoundedChannel_Capacity50000 | 10000      |  1,376.83 μs |    541.089 μs |    29.659 μs |  1.00 |    0.03 |    3 |      - |   15.5 KB |        1.00 |
|                              |            |              |               |              |       |         |      |        |           |             |
| UnboundedChannel             | 100000     |  5,091.01 μs |  5,157.772 μs |   282.715 μs |  0.32 |    0.03 |    1 |      - |  12.12 KB |        0.05 |
| BoundedChannel_DropOldest    | 100000     |  7,150.10 μs |  1,991.475 μs |   109.159 μs |  0.45 |    0.04 |    2 |      - |  257.8 KB |        1.16 |
| BoundedChannel_Capacity10000 | 100000     | 15,326.26 μs | 25,281.620 μs | 1,385.771 μs |  0.97 |    0.11 |    3 |      - | 139.93 KB |        0.63 |
| BoundedChannel_Capacity50000 | 100000     | 15,894.60 μs | 29,455.297 μs | 1,614.544 μs |  1.01 |    0.12 |    3 |      - | 222.86 KB |        1.00 |
