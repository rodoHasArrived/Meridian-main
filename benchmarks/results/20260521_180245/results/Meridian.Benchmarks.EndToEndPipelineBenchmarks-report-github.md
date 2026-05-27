```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                  | EventCount | Mean         | Error        | StdDev     | Ratio | RatioSD | Rank | Gen0      | Gen1     | Gen2     | Allocated   | Alloc Ratio |
|---------------------------------------- |----------- |-------------:|-------------:|-----------:|------:|--------:|-----:|----------:|---------:|---------:|------------:|------------:|
| Stage1_2_Channel_BatchDrain             | 1000       |     85.93 μs |    13.259 μs |   0.727 μs |  0.80 |    0.01 |    1 |    0.8545 |        - |        - |    13.73 KB |        0.86 |
| Stage1_ChannelOnly                      | 1000       |    106.83 μs |     7.604 μs |   0.417 μs |  1.00 |    0.00 |    2 |    0.9766 |        - |        - |    16.04 KB |        1.00 |
| Stage1_2_3_Utf8Bytes_Serialize          | 1000       |  1,265.63 μs |   330.372 μs |  18.109 μs | 11.85 |    0.15 |    3 |   46.8750 |        - |        - |   790.19 KB |       49.25 |
| Stage1_2_3_Channel_BatchDrain_Serialize | 1000       |  1,325.33 μs |   104.816 μs |   5.745 μs | 12.41 |    0.06 |    3 |   74.2188 |   1.9531 |        - |  1226.71 KB |       76.45 |
| Stage1_2_3_4_FullPipeline_MemoryStream  | 1000       |  1,654.83 μs |   252.454 μs |  13.838 μs | 15.49 |    0.12 |    4 |  250.0000 | 250.0000 | 250.0000 |   2047.2 KB |      127.59 |
|                                         |            |              |              |            |       |         |      |           |          |          |             |             |
| Stage1_2_Channel_BatchDrain             | 10000      |    670.35 μs |    53.883 μs |   2.954 μs |  0.80 |    0.02 |    1 |    6.8359 |   0.9766 |        - |   117.27 KB |        0.77 |
| Stage1_ChannelOnly                      | 10000      |    836.17 μs |   349.184 μs |  19.140 μs |  1.00 |    0.03 |    2 |    9.7656 |   6.8359 |   6.8359 |   152.65 KB |        1.00 |
| Stage1_2_3_Utf8Bytes_Serialize          | 10000      | 11,640.68 μs |   456.721 μs |  25.034 μs | 13.93 |    0.28 |    3 |  484.3750 |  31.2500 |  31.2500 |  7990.28 KB |       52.34 |
| Stage1_2_3_Channel_BatchDrain_Serialize | 10000      | 12,391.12 μs | 4,255.177 μs | 233.241 μs | 14.82 |    0.38 |    3 |  750.0000 |  31.2500 |  31.2500 | 12371.31 KB |       81.04 |
| Stage1_2_3_4_FullPipeline_MemoryStream  | 10000      | 15,572.53 μs | 3,677.168 μs | 201.558 μs | 18.63 |    0.43 |    4 | 1656.2500 | 906.2500 | 906.2500 | 17703.72 KB |      115.98 |
