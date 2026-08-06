using Meridian.QuantScript.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meridian.QuantScript.Tests;

public sealed class QuantScriptCompositionAndLaunchTests
{
    [Fact]
    public void AddMeridianQuantScript_BindsDocumentedIsolationAndQuotaConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QuantScript:MaxConcurrentWorkers"] = "3",
                ["QuantScript:MaxQueuedWorkerRequests"] = "5",
                ["QuantScript:MaxWorkerMemoryBytes"] = (768L * 1024 * 1024).ToString(),
                ["QuantScript:MaxWorkerCpuTimeSeconds"] = "45",
                ["QuantScript:MaxHostRpcCallsPerRun"] = "42",
                ["QuantScript:MaxHostRpcRecordsPerRun"] = "1234",
                ["QuantScript:MaxHostRpcResponseBytesPerRun"] = "65536",
                ["QuantScript:MaxHostRpcSymbolsPerRun"] = "7",
                ["QuantScript:MaxHostRpcDateRangeDays"] = "90"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddMeridianQuantScript(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuantScriptOptions>>().Value;
        options.MaxConcurrentWorkers.Should().Be(3);
        options.MaxQueuedWorkerRequests.Should().Be(5);
        options.MaxWorkerMemoryBytes.Should().Be(768L * 1024 * 1024);
        options.MaxWorkerCpuTimeSeconds.Should().Be(45);
        options.MaxHostRpcCallsPerRun.Should().Be(42);
        options.MaxHostRpcRecordsPerRun.Should().Be(1234);
        options.MaxHostRpcResponseBytesPerRun.Should().Be(65536);
        options.MaxHostRpcSymbolsPerRun.Should().Be(7);
        options.MaxHostRpcDateRangeDays.Should().Be(90);
    }

    [Fact]
    public void CreateStartInfo_WorkerDllFallbackUsesWorkerDirectoryNotDotnetDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quant-worker-layout-{Guid.NewGuid():N}");
        var workerDirectory = Path.Combine(root, "workers", "quant-script");
        var hostDirectory = Path.Combine(root, "dotnet");
        Directory.CreateDirectory(workerDirectory);
        Directory.CreateDirectory(hostDirectory);
        var assembly = Path.Combine(workerDirectory, "Meridian.QuantScript.Worker.dll");
        var runtimeConfig = Path.ChangeExtension(assembly, ".runtimeconfig.json");
        var deps = Path.ChangeExtension(assembly, ".deps.json");
        var dotnetHost = Path.Combine(hostDirectory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        try
        {
            File.WriteAllBytes(assembly, []);
            File.WriteAllText(runtimeConfig, "{}");
            File.WriteAllText(deps, "{}");
            File.WriteAllBytes(dotnetHost, []);

            var startInfo = WorkerLaunchResolver.CreateStartInfo(
                new QuantScriptOptions
                {
                    WorkerAssemblyPath = assembly,
                    WorkerRuntimeConfigPath = runtimeConfig,
                    WorkerDepsFilePath = deps,
                    WorkerDotNetHostPath = dotnetHost
                },
                "request-pipe",
                "response-pipe");

            startInfo.FileName.Should().Be(Path.GetFullPath(dotnetHost));
            startInfo.WorkingDirectory.Should().Be(Path.GetFullPath(workerDirectory));
            startInfo.ArgumentList.Should().ContainInOrder(
                "exec",
                "--runtimeconfig",
                Path.GetFullPath(runtimeConfig),
                "--depsfile",
                Path.GetFullPath(deps),
                Path.GetFullPath(assembly));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateStartInfo_DefaultBuildOrPublishLayoutResolvesSidecar()
    {
        var sidecarDirectory = Path.Combine(AppContext.BaseDirectory, "workers", "quant-script");
        Directory.Exists(sidecarDirectory).Should().BeTrue("the test build imports the worker sidecar target");

        var startInfo = WorkerLaunchResolver.CreateStartInfo(
            new QuantScriptOptions(),
            "request-pipe",
            "response-pipe");

        startInfo.WorkingDirectory.Should().Be(Path.GetFullPath(sidecarDirectory));
        startInfo.ArgumentList.Should().Contain("--isolated-worker");
    }
}
