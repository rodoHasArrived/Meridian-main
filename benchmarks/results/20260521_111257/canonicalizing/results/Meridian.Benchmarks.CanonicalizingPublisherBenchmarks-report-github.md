```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8457/25H2/2025Update/HudsonValley2)
11th Gen Intel Core i5-1135G7 2.40GHz (Max: 2.42GHz), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.204
  [Host] : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                   | Mean | Error | Ratio | RatioSD | Rank | Alloc Ratio |
|------------------------- |-----:|------:|------:|--------:|-----:|------------:|
| TryPublish_CanonicalOnly |   NA |    NA |     ? |       ? |    ? |           ? |

Benchmarks with issues:
  CanonicalizingPublisherBenchmarks.TryPublish_CanonicalOnly: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
