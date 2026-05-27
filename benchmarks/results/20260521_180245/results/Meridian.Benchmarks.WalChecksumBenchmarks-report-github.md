```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host] : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                       | PayloadSize | Mean | Error | Ratio | RatioSD | Rank | Alloc Ratio |
|----------------------------- |------------ |-----:|------:|------:|--------:|-----:|------------:|
| Checksum_StringConcat_Legacy | large       |   NA |    NA |     ? |       ? |    ? |           ? |

Benchmarks with issues:
  WalChecksumBenchmarks.Checksum_StringConcat_Legacy: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [PayloadSize=large]
