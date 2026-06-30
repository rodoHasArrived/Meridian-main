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
            new MigrationOptions(DeleteSource: false, VerifyChecksum: true, ParallelFiles: 1, OnProgress: progress.Add));

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
    public async Task Scenario_VerifiedProviderRollover_DeleteSourceOnlyAfterChecksumVerified()
    {
        var sourceDirectory = Path.Combine(_hot, "polygon", "SPY", "HistoricalBar");
        Directory.CreateDirectory(sourceDirectory);
        var sourceFile = Path.Combine(sourceDirectory, "2026-06-29.jsonl");
        await File.WriteAllTextAsync(sourceFile, "{\"symbol\":\"SPY\",\"close\":500.12}");

        var result = await _sut.MigrateAsync(
            sourceFile,
            StorageTier.Warm,
            new MigrationOptions(DeleteSource: true, VerifyChecksum: true, ParallelFiles: 1));

        result.Success.Should().BeTrue();
        result.FilesProcessed.Should().Be(1);
        result.FilesFailed.Should().Be(0);
        result.Errors.Should().BeEmpty();
        File.Exists(sourceFile).Should().BeFalse("verified rollover should delete the hot-tier file only after target checksum validation");

        var targetFile = Path.Combine(_warm, "hot", "polygon", "SPY", "HistoricalBar", "2026-06-29.jsonl.gz");
        File.Exists(targetFile).Should().BeTrue();
        var migratedPayload = await ReadGzipTextAsync(targetFile);
        migratedPayload.Should().Be("{\"symbol\":\"SPY\",\"close\":500.12}");
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

    [Fact]
    public async Task Scenario_TierObservability_StatisticsReportHotAndWarmEvidenceInventory()
    {
        var firstHotFile = Path.Combine(_hot, "alpaca", "AAPL", "Trade", "2026-06-28.jsonl");
        var secondHotFile = Path.Combine(_hot, "alpaca", "MSFT", "Trade", "2026-06-28.jsonl");
        var warmFile = Path.Combine(_warm, "polygon", "SPY", "HistoricalBar", "2026-05-01.jsonl.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(firstHotFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondHotFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(warmFile)!);
        await File.WriteAllTextAsync(firstHotFile, "first-hot");
        await File.WriteAllTextAsync(secondHotFile, "second-hot");
        await File.WriteAllTextAsync(warmFile, "warm");
        var oldest = DateTime.UtcNow.AddDays(-6);
        var newest = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(firstHotFile, oldest);
        File.SetLastWriteTimeUtc(secondHotFile, newest);
        File.SetLastWriteTimeUtc(warmFile, DateTime.UtcNow.AddDays(-30));

        var statistics = await _sut.GetTierStatisticsAsync();

        statistics.TierInfo.Should().ContainKeys(StorageTier.Hot, StorageTier.Warm);
        var hotInfo = statistics.TierInfo[StorageTier.Hot];
        hotInfo.FileCount.Should().Be(2);
        hotInfo.TotalBytes.Should().Be(new FileInfo(firstHotFile).Length + new FileInfo(secondHotFile).Length);
        hotInfo.OldestFile.Should().BeCloseTo(oldest, TimeSpan.FromSeconds(2));
        hotInfo.NewestFile.Should().BeCloseTo(newest, TimeSpan.FromSeconds(2));

        var warmInfo = statistics.TierInfo[StorageTier.Warm];
        warmInfo.FileCount.Should().Be(1);
        warmInfo.TotalBytes.Should().Be(new FileInfo(warmFile).Length);
    }

    [Fact]
    public async Task Scenario_TierClassification_UsesConfiguredRetentionBoundaries()
    {
        var recentFile = Path.Combine(_hot, "recent.jsonl");
        var warmCandidate = Path.Combine(_hot, "warm-candidate.jsonl");
        var archiveCandidate = Path.Combine(_hot, "archive-candidate.jsonl");
        await File.WriteAllTextAsync(recentFile, "recent");
        await File.WriteAllTextAsync(warmCandidate, "warm");
        await File.WriteAllTextAsync(archiveCandidate, "archive");
        File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(warmCandidate, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(archiveCandidate, DateTime.UtcNow.AddDays(-120));

        _sut.DetermineTargetTier(recentFile).Should().Be(StorageTier.Hot);
        _sut.DetermineTargetTier(warmCandidate).Should().Be(StorageTier.Warm);
        _sut.DetermineTargetTier(archiveCandidate).Should().Be(StorageTier.Archive);
    }

    private static async Task<string> ReadGzipTextAsync(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync();
    }
}
