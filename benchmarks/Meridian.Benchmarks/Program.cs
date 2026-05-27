using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Meridian.Benchmarks;

const string VelocityJob = "velocity";
var benchmarkConfig = ResolveBenchmarkConfig(args);

// Handle --export-budgets flag: serialize the budget registry to perf-budgets.json
// inside the artifacts directory and exit. This is called by CI after building.
var exportBudgetsIdx = Array.IndexOf(args, "--export-budgets");
if (exportBudgetsIdx >= 0)
{
    // Remove the custom flag before passing args to BDN
    var bdnArgs = args.Where((_, i) => i != exportBudgetsIdx).ToArray();

    // Determine output path from --artifacts flag, or default to current directory
    var artifactsIdx = Array.IndexOf(bdnArgs, "--artifacts");
    var artifactsDir = artifactsIdx >= 0 && artifactsIdx + 1 < bdnArgs.Length
        ? bdnArgs[artifactsIdx + 1]
        : "./BenchmarkDotNet.Artifacts";

    var budgetOutputPath = Path.Combine(artifactsDir, "perf-budgets.json");
    PerformanceBudgetRegistry.ExportJson(budgetOutputPath);
    Console.WriteLine($"[perf-budgets] Exported {PerformanceBudgetRegistry.All.Count} budget entries to {budgetOutputPath}");

    // Run benchmarks normally after exporting budgets
    var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(bdnArgs, benchmarkConfig);

    // Auto-save results to local history for developer offline comparison
    var store = new BenchmarkResultStore();
    foreach (var summary in summaries)
    {
        var resultDir = Path.Combine(artifactsDir, "results");
        if (!Directory.Exists(resultDir))
            continue;

        var pattern = $"*{summary.Title}*-report.json";
        var jsonFile = Directory.GetFiles(resultDir, pattern).FirstOrDefault();
        if (jsonFile != null)
            store.Save(summary.Title, jsonFile);
    }
}
else
{
    // Standard run — no budget export
    var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, benchmarkConfig);

    // Auto-save results to local history for developer offline comparison
    var store = new BenchmarkResultStore();
    var artifactsIdx = Array.IndexOf(args, "--artifacts");
    var artifactsDir = artifactsIdx >= 0 && artifactsIdx + 1 < args.Length
        ? args[artifactsIdx + 1]
        : "./BenchmarkDotNet.Artifacts";
    var resultDir = Path.Combine(artifactsDir, "results");

    if (Directory.Exists(resultDir))
    {
        foreach (var summary in summaries)
        {
            var pattern = $"*{summary.Title}*-report.json";
            var jsonFile = Directory.GetFiles(resultDir, pattern).FirstOrDefault();
            if (jsonFile != null)
                store.Save(summary.Title, jsonFile);
        }
    }
}

static IConfig? ResolveBenchmarkConfig(string[] args)
{
    return HasVelocityJob(args) ? CreateVelocityBenchmarkConfig() : null;
}

static bool HasVelocityJob(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (string.Equals(arg, "--job", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length && string.Equals(args[i + 1], VelocityJob, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            continue;
        }

        const string jobPrefix = "--job=";
        if (arg.StartsWith(jobPrefix, StringComparison.OrdinalIgnoreCase)
            && string.Equals(arg.AsSpan(jobPrefix.Length).ToString(), VelocityJob, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static IConfig CreateVelocityBenchmarkConfig()
{
    var velocityJob = Job.ShortRun
        .WithId(VelocityJob)
        .WithWarmupCount(12)
        .WithMinIterationCount(20)
        .WithMaxIterationCount(120)
        .WithMaxRelativeError(0.015);

    return ManualConfig.Create(DefaultConfig.Instance).AddJob(velocityJob);
}
