using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Platform.Results;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Exit codes and on-disk effects of the --wal-repair CLI handler.
/// </summary>
public sealed class WalRepairCommandTests : TempDirectoryAsyncTestBase
{
    private const string GarbageLine = "GARBAGE|not-a-timestamp|bad|bad|{}\n";

    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    private string WalDirectory => Path.Combine(TestDataRoot, "_wal");

    private WalRepairCommand CreateCommand() => new(TestDataRoot, Logger);

    [Fact]
    public void CanHandle_WalRepairFlag_ReturnsTrue()
    {
        CreateCommand().CanHandle(["--wal-repair"]).Should().BeTrue();
        CreateCommand().CanHandle(["--help"]).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_NoWalDirectory_Succeeds()
    {
        var result = await CreateCommand().ExecuteAsync(["--wal-repair"]);

        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_CleanWal_SucceedsAndDoesNotStartASegment()
    {
        await SeedSegmentAsync(string.Empty);

        var result = await CreateCommand().ExecuteAsync(["--wal-repair"]);

        result.ExitCode.Should().Be(0);
        Directory.GetFiles(WalDirectory, "*.wal").Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_OnlyAnIncompleteFinalWrite_Succeeds()
    {
        var segment = await SeedSegmentAsync("3|2026-11-25T00:00:00.0000000Z|tra");
        var original = await File.ReadAllBytesAsync(segment);

        var result = await CreateCommand().ExecuteAsync(["--wal-repair"]);

        result.ExitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(segment)).Should().Equal(original);
    }

    [Fact]
    public async Task ExecuteAsync_CorruptRecord_RepairsQuarantinesAndReportsDataCorruption()
    {
        var segment = await SeedSegmentAsync(GarbageLine);
        var original = await File.ReadAllBytesAsync(segment);
        var report = Path.Combine(TestDataRoot, "report.txt");

        var result = await CreateCommand().ExecuteAsync(["--wal-repair", "--output", report]);

        result.ExitCode.Should().Be(ErrorCode.DataCorruption.ToExitCode());
        (await File.ReadAllTextAsync(segment)).Should().NotContain("GARBAGE");
        var quarantined = Directory.GetFiles(Path.Combine(WalDirectory, "quarantine")).Should().ContainSingle().Subject;
        (await File.ReadAllBytesAsync(quarantined)).Should().Equal(original);
        (await File.ReadAllTextAsync(report)).Should().Contain(quarantined).And.Contain("corrupt record");
    }

    [Fact]
    public async Task ExecuteAsync_DryRunWithCorruptRecord_ReportsWithoutModifying()
    {
        var segment = await SeedSegmentAsync(GarbageLine);
        var original = await File.ReadAllBytesAsync(segment);
        var report = Path.Combine(TestDataRoot, "report.txt");

        var result = await CreateCommand().ExecuteAsync(["--wal-repair", "--dry-run", "--output", report]);

        result.ExitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(segment)).Should().Equal(original);
        Directory.Exists(Path.Combine(WalDirectory, "quarantine")).Should().BeFalse();
        (await File.ReadAllTextAsync(report)).Should().Contain($"{Path.GetFileName(segment)}:4");
    }

    [Fact]
    public async Task ExecuteAsync_WalHeldByLiveWriter_RefusesAndChangesNothing()
    {
        var segment = await SeedSegmentAsync(GarbageLine);
        var original = await File.ReadAllBytesAsync(segment);
        await using var writer = new WriteAheadLog(WalDirectory, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await writer.InitializeAsync();

        var result = await CreateCommand().ExecuteAsync(["--wal-repair"]);

        result.ExitCode.Should().Be(ErrorCode.StorageError.ToExitCode());
        result.ExitCode.Should().NotBe(0);
        (await File.ReadAllBytesAsync(segment)).Should().Equal(original);
        Directory.Exists(Path.Combine(WalDirectory, "quarantine")).Should().BeFalse();
    }

    // Two valid records in one closed segment, followed by suffix verbatim.
    private async Task<string> SeedSegmentAsync(string suffix)
    {
        await using (var seed = new WriteAheadLog(WalDirectory, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await seed.InitializeAsync();
            await seed.AppendAsync(new { Index = 1 }, "trade");
            await seed.AppendAsync(new { Index = 2 }, "trade");
            await seed.FlushAsync();
        }

        var segment = Directory.GetFiles(WalDirectory, "*.wal").Single();
        await File.AppendAllTextAsync(segment, suffix);
        return segment;
    }
}
