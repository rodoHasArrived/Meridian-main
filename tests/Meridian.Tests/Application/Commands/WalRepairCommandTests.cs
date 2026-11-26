using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Core.Config;
using Meridian.Platform.Results;
using Meridian.Storage.Archival;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

public sealed class WalRepairCommandTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-wal-repair-{Guid.NewGuid():N}");

    public WalRepairCommandTests() => Directory.CreateDirectory(_dataRoot);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task Repair_WhileAWorkloadOwnsItsWal_IsRefusedAndChangesNoFiles()
    {
        var collectorDir = Path.Combine(_dataRoot, "_wal", "collector");
        await using var running = new WriteAheadLog(collectorDir);
        await running.InitializeAsync();
        await running.AppendAsync("live", "Trade");
        await running.FlushAsync();
        var before = Directory.GetFiles(collectorDir, "*.wal")
            .ToDictionary(static f => Path.GetFileName(f)!, static f => File.ReadAllBytes(f));

        var result = await RunAsync("--wal-repair");

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ErrorCode.FileAccessDenied);
        Directory.GetFiles(collectorDir, "*.wal").ToDictionary(static f => Path.GetFileName(f)!, static f => File.ReadAllBytes(f))
            .Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Repair_OfAnIdleWorkload_Succeeds()
    {
        var backfillDir = Path.Combine(_dataRoot, "_wal", "backfill");
        await using (var stopped = new WriteAheadLog(backfillDir))
        {
            await stopped.InitializeAsync();
            await stopped.AppendAsync("historical", "Bar");
        }

        var result = await RunAsync("--wal-repair", "--workload", "backfill");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Repair_WithUnknownWorkload_FailsValidation()
    {
        var result = await RunAsync("--wal-repair", "--workload", "nightly");

        result.Error.Should().Be(ErrorCode.ValidationFailed);
    }

    [Fact]
    public void ResolveWalDirectories_IncludesEveryWorkloadAndUnadoptedLegacySegments()
    {
        var legacy = Path.Combine(_dataRoot, "_wal");
        Directory.CreateDirectory(Path.Combine(legacy, "collector"));
        Directory.CreateDirectory(Path.Combine(legacy, "backfill"));

        WalRepairCommand.ResolveWalDirectories(_dataRoot, onlyWorkload: null)
            .Should().Equal(Path.Combine(legacy, "collector"), Path.Combine(legacy, "backfill"));

        File.WriteAllText(Path.Combine(legacy, "wal_20260101_000000_000000000000.wal"), "MDCWAL02|2|2026-01-01T00:00:00.0000000Z\n");
        WalRepairCommand.ResolveWalDirectories(_dataRoot, onlyWorkload: null).Should().EndWith(legacy);
    }

    private async Task<CliResult> RunAsync(params string[] args)
    {
        var command = new WalRepairCommand(new AppConfig(DataRoot: _dataRoot), new LoggerConfiguration().CreateLogger());
        return await command.ExecuteAsync(args);
    }
}
