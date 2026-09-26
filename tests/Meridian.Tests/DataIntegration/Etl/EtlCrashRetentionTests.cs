using System.Diagnostics;
using System.Text.Json;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.ProcessTestHelper;
using Meridian.Storage.Coordination;
using Meridian.Storage.Etl;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.DataIntegration.Etl;

/// <summary>Protects partner source retention when a process dies before required ETL commits finish.</summary>
public sealed class EtlCrashRetentionTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("staged", false, false, false)]
    [InlineData("flushed", true, false, false)]
    [InlineData("catalog", true, true, false)]
    [InlineData("export-written", true, true, true)]
    [InlineData("durable-flushed", true, false, false)]
    public async Task ProcessKilled_BetweenRequiredStages_RetainsSourceWithoutAdvancingCheckpoint(
        string stage, bool flushed, bool catalogCommitted, bool exportWritten)
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-etl-crash-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        var source = Path.Combine(input, "trades.csv");
        var evt = MarketScenarioBuilder.BuildSessionOpen(["AAPL"], DateTimeOffset.Parse("2026-01-05T14:30:00Z"), 1, 0).Single();
        var trade = (Trade)evt.Payload;
        var csv = "timestamp,symbol,price,size\n" + FormattableString.Invariant($"{evt.Timestamp:O},{evt.Symbol},{trade.Price},{trade.Size}\n");
        await File.WriteAllTextAsync(source, csv);
        var ready = Path.Combine(root, "ready");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "exec", "--depsfile", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.deps.json"),
            "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.runtimeconfig.json"),
            typeof(ProcessTestHelperMarker).Assembly.Location, "etl-crash-stage", root, ready, stage
        })
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("ETL child did not start.");
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            // The atomically renamed ready file is the cross-process stage barrier.
            while (!File.Exists(ready))
            {
                Assert.False(child.HasExited, child.HasExited ? await errors : "");
                await Task.Delay(20, timeout.Token);
            }
            var jobId = await File.ReadAllTextAsync(ready, timeout.Token);
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(timeout.Token);

            Assert.Equal(csv, await File.ReadAllTextAsync(source, timeout.Token));
            Assert.Equal(csv, await File.ReadAllTextAsync(Path.Combine(root, "_etl", "staging", jobId, "trades.csv"), timeout.Token));
            Assert.Null(await new EtlAuditStore(root).LoadCheckpointAsync(jobId, timeout.Token));
            Assert.Equal(exportWritten, File.Exists(Path.Combine(root, "export.csv")));
            var records = Directory.GetFiles(Path.Combine(root, "normalized"), "*.jsonl", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "_dedup" + Path.DirectorySeparatorChar))
                .SelectMany(File.ReadAllLines).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Equal(flushed ? 1 : 0, records.Length);
            foreach (var record in records)
            {
                using var parsed = JsonDocument.Parse(record);
                Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
            }
            var manifest = await File.ReadAllTextAsync(Path.Combine(root, "normalized", "_catalog", "manifest.json"), timeout.Token);
            var catalog = JsonSerializer.Deserialize<StorageCatalog>(manifest)!;
            Assert.Equal(catalogCommitted ? 1 : 0, catalog.Statistics.TotalFiles);
            if (stage == "durable-flushed")
            {
                // Observe actual lease expiry after process death, without rewriting lease state.
                var store = new SharedStorageCoordinationStore(new CoordinationConfig(), root);
                while ((await store.GetLeaseAsync($"jobs/etl/{jobId}", timeout.Token)) is { } lease &&
                       lease.ExpiresAtUtc > DateTimeOffset.UtcNow)
                    await Task.Delay(20, timeout.Token);
                start.ArgumentList[^1] = "durable-restart";
                using var restarted = Process.Start(start) ?? throw new InvalidOperationException("ETL restart did not start.");
                var restartErrors = restarted.StandardError.ReadToEndAsync();
                try
                {
                    await restarted.WaitForExitAsync(timeout.Token);
                    Assert.True(restarted.ExitCode == 0, await restartErrors);
                    Assert.False(File.Exists(source));
                    Assert.NotNull(await new EtlAuditStore(root).LoadCheckpointAsync(jobId, timeout.Token));
                    Assert.Equal("1", await File.ReadAllTextAsync(Path.Combine(root, "restart-deduplicated"), timeout.Token));
                    var storedTrades = Directory.GetFiles(Path.Combine(root, "normalized"), "*.jsonl", SearchOption.AllDirectories)
                        .Where(path => !path.Contains(Path.DirectorySeparatorChar + "_dedup" + Path.DirectorySeparatorChar))
                        .SelectMany(File.ReadAllLines).Where(line => !string.IsNullOrWhiteSpace(line));
                    Assert.Single(storedTrades);
                    var completedCatalog = JsonSerializer.Deserialize<StorageCatalog>(await File.ReadAllTextAsync(
                        Path.Combine(root, "normalized", "_catalog", "manifest.json"), timeout.Token))!;
                    Assert.Equal(1, completedCatalog.Statistics.TotalFiles);
                }
                finally
                {
                    if (!restarted.HasExited)
                        restarted.Kill(entireProcessTree: true);
                    await restarted.WaitForExitAsync();
                }
            }
        }
        finally
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
