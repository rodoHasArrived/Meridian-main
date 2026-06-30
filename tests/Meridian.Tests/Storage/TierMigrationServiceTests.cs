using System.IO.Compression;
using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Scenario coverage for provider-session retention rollover, guarding against tier migration losing data-file identity or deleting evidence on cancellation.
/// </summary>
public sealed class TierMigrationServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _hot;
    private readonly string _warm;
    private readonly TierMigrationService _sut;

    public TierMigrationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"meridian-tier-migration-{Guid.NewGuid():N}");
        _hot = Path.Combine(_root, "hot");
        _warm = Path.Combine(_root, "warm");
        Directory.CreateDirectory(_hot);
        Directory.CreateDirectory(_warm);

        _sut = new TierMigrationService(new StorageOptions
        {
            RootPath = _root,
            Tiering = new TieringOptions
            {
                Enabled = true,
                Tiers =
                [
                    new TierConfig
                    {
                        Name = "Hot",
                        Path = _hot,
                        MaxAgeDays = 7,
                        Format = "jsonl",
                        Compression = CompressionCodec.None
                    },
                    new TierConfig
                    {
                        Name = "Warm",
                        Path = _warm,
                        MaxAgeDays = 90,
                        Format = "jsonl",
                        Compression = CompressionCodec.Gzip
                    }
                ]
            }
        });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for Windows file handles held briefly after gzip reads.
        }
    }

    [Fact]
    public async Task Scenario_ProviderSessionRollover_GzipMigrationPreservesJsonlFileIdentity()
    {
        var sourceDirectory = Path.Combine(_hot, "alpaca", "AAPL", "Trade");
        Directory.CreateDirectory(sourceDirectory);
        var sourceFile = Path.Combine(sourceDirectory, "session.jsonl");
        await File.WriteAllTextAsync(sourceFile, "{\"symbol\":\"AAPL\",\"price\":213.45}");
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "notes.txt"), "operator note");
        var progress = new List<MigrationProgress>();

        var result = await _sut.MigrateAsync(
            sourceDirectory,
            StorageTier.Warm,
            new MigrationOptions(DeleteSource: false, VerifyChecksum: false, ParallelFiles: 1, OnProgress: progress.Add));

        result.Success.Should().BeTrue();
        result.FilesProcessed.Should().Be(1);
        result.FilesFailed.Should().Be(0);
        progress.Should().ContainSingle().Which.TotalFiles.Should().Be(1);

        var targetFile = Path.Combine(_warm, "hot", "alpaca", "AAPL", "Trade", "session.jsonl.gz");
        File.Exists(targetFile).Should().BeTrue("compressed tier rollover must keep the original .jsonl filename before adding .gz");
        var migratedPayload = await ReadGzipTextAsync(targetFile);
        migratedPayload.Should().Be("{\"symbol\":\"AAPL\",\"price\":213.45}");
        File.Exists(Path.Combine(_warm, "hot", "alpaca", "AAPL", "Trade", "notes.txt")).Should().BeFalse();
        File.Exists(sourceFile).Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_RetentionPlanning_AgedHotBackfillFilesProduceWarmMigrationActions()
    {
        var sourceDirectory = Path.Combine(_hot, "polygon", "MSFT", "HistoricalBar");
        Directory.CreateDirectory(sourceDirectory);
        var agedFile = Path.Combine(sourceDirectory, "2026-05-01.jsonl");
        var recentFile = Path.Combine(sourceDirectory, "2026-06-29.jsonl");
        await File.WriteAllTextAsync(agedFile, "aged-bars");
        await File.WriteAllTextAsync(recentFile, "recent-bars");
        File.SetLastWriteTimeUtc(agedFile, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddDays(-2));

        var plan = await _sut.PlanMigrationAsync(TimeSpan.FromDays(30));

        plan.Actions.Should().ContainSingle(action => action.SourcePath == agedFile).Which.Should().BeEquivalentTo(new
        {
            TargetTier = StorageTier.Warm,
            Reason = "Age > 7 days",
            SizeBytes = new FileInfo(agedFile).Length
        }, options => options.ExcludingMissingMembers());
        plan.Actions.Should().NotContain(action => action.SourcePath == recentFile);
        plan.EstimatedBytesToMigrate.Should().Be(new FileInfo(agedFile).Length);
    }

    [Fact]
    public async Task Scenario_MigrationCancellation_CancelledRolloverLeavesSourceEvidenceInPlace()
    {
        var sourceFile = Path.Combine(_hot, "cancelled-session.jsonl");
        await File.WriteAllTextAsync(sourceFile, "{\"symbol\":\"SPY\",\"price\":500.00}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _sut.MigrateAsync(
            sourceFile,
            StorageTier.Warm,
            new MigrationOptions(DeleteSource: true, VerifyChecksum: false, ParallelFiles: 1),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(sourceFile).Should().BeTrue();
        Directory.EnumerateFiles(_warm, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private static async Task<string> ReadGzipTextAsync(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync();
    }
}
