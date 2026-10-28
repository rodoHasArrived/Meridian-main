using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Store;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Replay;
using Meridian.Storage.Services;
using Meridian.Storage.Store;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Deterministic publication fault injection and fresh-reader/restart regressions. These
/// execute the real maintenance and replay implementations; they do not claim to simulate
/// host power loss or filesystem write reordering.
/// </summary>
public sealed class ArchiveCompressionCrashTests : TempDirectoryAsyncTestBase
{
    [Theory]
    [InlineData("StagedVerified")]
    [InlineData("Published")]
    [InlineData("PublicationSynced")]
    [InlineData("SourceDeleted")]
    [InlineData("SidecarDeleted")]
    public async Task InterruptionAtPublicationBoundary_RestartReplaysEachEventOnce(string stageName)
    {
        var path = await CreateSourceAsync();
        var expected = ExpectedLines();
        var stage = Enum.Parse<CompressionPublicationStage>(stageName);
        var checkpointReached = false;
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot })
        {
            CompressionCheckpoint = checkpoint =>
            {
                if (checkpoint == stage)
                {
                    checkpointReached = true;
                    throw new IOException($"Injected interruption at {checkpoint}");
                }
            }
        };

        var interrupted = await service.CompressAsync(new FileCompressionOptions());

        checkpointReached.Should().BeTrue();
        interrupted.Success.Should().BeFalse();
        interrupted.RecoveryCopies.Should().HaveCount(2);
        var backups = interrupted.RecoveryCopies.ToDictionary(copy => copy, File.ReadAllBytes);
        if (stage == CompressionPublicationStage.StagedVerified)
        {
            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".gz").Should().BeFalse();
        }
        if (stage is CompressionPublicationStage.Published or CompressionPublicationStage.PublicationSynced)
        {
            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".gz").Should().BeTrue();
        }

        // New reader instances, twice: no in-memory completion marker may be required.
        for (var pass = 0; pass < 2; pass++)
            for (var reader = 0; reader < 4; reader++)
                (await ReadAllAsync(Read(reader))).Should().Equal(expected);

        var restarted = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var resumed = await restarted.CompressAsync(new FileCompressionOptions());
        resumed.Success.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
        File.Exists(path + ".gz").Should().BeTrue();
        File.Exists(path + ".sha256").Should().BeFalse(
            "a source sidecar left by an interrupted cleanup must not outlive its source");
        for (var reader = 0; reader < 4; reader++)
            (await ReadAllAsync(Read(reader))).Should().Equal(expected);
        foreach (var backup in backups)
            File.ReadAllBytes(backup.Key).Should().Equal(backup.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task PublishedAndUnpublishedArtifacts_AreNotAdditionalEventStreams(int reader)
    {
        var path = await CreateSourceAsync();
        var payload = await File.ReadAllBytesAsync(path);
        await WriteGzipAsync(path + ".gz.unpublished.recompress.tmp", payload);
        await File.WriteAllBytesAsync(path + ".pre-compression.retained.bak", payload);
        await File.WriteAllBytesAsync(path + ".corrupt-lines", payload);
        await File.WriteAllBytesAsync(path + ".sha256.pre-compression.retained.bak", payload);

        (await ReadAllAsync(Read(reader))).Should().Equal(ExpectedLines());
        await WriteGzipAsync(path + ".gz", payload);
        (await ReadAllAsync(Read(reader))).Should().Equal(ExpectedLines());
        File.Exists(path + ".pre-compression.retained.bak").Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CompressionDuringReplay_KeepsResolvedHandleAndDoesNotDiscoverSecondCopy(int reader)
    {
        var path = await CreateSourceAsync();
        var expected = ExpectedLines();
        var observed = new List<string>();
        await using (var replay = Read(reader).GetAsyncEnumerator())
        {
            (await replay.MoveNextAsync()).Should().BeTrue();
            observed.Add(Serialize(replay.Current));
            var maintenance = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

            var result = await maintenance.CompressAsync(new FileCompressionOptions());

            // Windows can refuse deletion while the source is memory-mapped. A published
            // verified destination must still be retained, errors reported, and replay safe.
            File.Exists(path + ".gz").Should().BeTrue();
            if (!result.Success)
            {
                result.Errors.Should().NotBeEmpty();
                File.Exists(path).Should().BeTrue();
            }
            while (await replay.MoveNextAsync())
                observed.Add(Serialize(replay.Current));
        }
        observed.Should().Equal(expected);
        (await ReadAllAsync(Read(reader))).Should().Equal(expected);
        var restarted = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        (await restarted.CompressAsync(new FileCompressionOptions())).Success.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
        (await ReadAllAsync(Read(reader))).Should().Equal(expected);
    }

    [Fact]
    public async Task DiscoverySnapshotBeforeCleanup_ResolvesTheNewRepresentationAtOpen()
    {
        var path = await CreateSourceAsync();
        var snapshot = JsonlArchiveFiles.Enumerate(TestDataRoot);
        snapshot.Should().ContainSingle().Which.Should().Be(path);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        (await service.CompressAsync(new FileCompressionOptions())).Success.Should().BeTrue();

        await using var file = await JsonlArchiveFiles.OpenReadAsync(snapshot.Single(), CancellationToken.None);
        file.Name.Should().Be(path + ".gz");
        (await ReadAllAsync(new JsonlReplayer(path).ReadEventsAsync())).Should().Equal(ExpectedLines());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ConflictingRepresentations_RefuseBeforeFirstEventAndPreserveBoth(int reader)
    {
        var path = await CreateSourceAsync();
        await WriteGzipAsync(path + ".gz", Encoding.UTF8.GetBytes(Serialize(BuildTrade(99)) + "\n"));
        var sourceBytes = await File.ReadAllBytesAsync(path);
        var gzipBytes = await File.ReadAllBytesAsync(path + ".gz");
        var observed = new List<MarketEvent>();

        var error = await Record.ExceptionAsync(async () =>
        {
            await foreach (var evt in Read(reader))
                observed.Add(evt);
        });

        error.Should().BeOfType<IOException>().Which.Message.Should().Contain("Conflicting");
        observed.Should().BeEmpty();
        (await File.ReadAllBytesAsync(path)).Should().Equal(sourceBytes);
        (await File.ReadAllBytesAsync(path + ".gz")).Should().Equal(gzipBytes);
    }

    [Fact]
    public async Task ResumeWithDifferentDestination_PreservesOriginalAndChecksumEvidence()
    {
        var path = await CreateSourceAsync();
        await WriteGzipAsync(path + ".gz", Encoding.UTF8.GetBytes(Serialize(BuildTrade(99)) + "\n"));
        var before = Directory.GetFiles(TestDataRoot).ToDictionary(file => file, File.ReadAllBytes);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.CompressAsync(new FileCompressionOptions());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("recovery verification failed"));
        foreach (var file in before)
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
    }

    [Fact]
    public async Task RequiredBackupFailure_PreventsPublicationAndResumeCleanup()
    {
        var path = await CreateSourceAsync();
        await WriteGzipAsync(path + ".gz", await File.ReadAllBytesAsync(path));
        var blocked = Path.Combine(TestDataRoot, "blocked-backup-directory");
        await File.WriteAllTextAsync(blocked, "not a directory");
        var before = Directory.GetFiles(TestDataRoot).ToDictionary(file => file, File.ReadAllBytes);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.CompressAsync(new FileCompressionOptions(BackupPath: blocked));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Backup failed"));
        foreach (var file in before)
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CancellationDuringReplay_DoesNotAffectFreshReplay(int reader)
    {
        await CreateSourceAsync();
        using var cancelled = new CancellationTokenSource();
        await using (var replay = Read(reader, cancelled.Token).GetAsyncEnumerator())
        {
            (await replay.MoveNextAsync()).Should().BeTrue();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await replay.MoveNextAsync(); });
        }
        (await ReadAllAsync(Read(reader))).Should().Equal(ExpectedLines());
    }

    [Fact]
    public async Task SameArchiveNameInDifferentDirectories_RemainsTwoIndependentInputs()
    {
        var first = await CreateSourceAsync("alpaca");
        var second = await CreateSourceAsync("polygon");
        await WriteGzipAsync(first + ".gz", await File.ReadAllBytesAsync(first));
        await WriteGzipAsync(second + ".gz", await File.ReadAllBytesAsync(second));
        for (var reader = 0; reader < 4; reader++)
            (await ReadAllAsync(Read(reader))).Should().Equal(ExpectedLines().Concat(ExpectedLines()));
    }

    [Fact]
    public async Task UppercaseGzipSuffix_PreservesActualPathWhenSelectingRepresentation()
    {
        var path = await CreateSourceAsync();
        await WriteGzipAsync(path + ".GZ", await File.ReadAllBytesAsync(path));
        for (var reader = 0; reader < 4; reader++)
            (await ReadAllAsync(Read(reader))).Should().Equal(ExpectedLines());
    }

    [Fact]
    public async Task ChangedSourceChecksum_RefusesCompressionAndPreservesEvidence()
    {
        var path = await CreateSourceAsync();
        await File.AppendAllTextAsync(path, Serialize(BuildTrade(99)) + "\n");
        var before = Directory.GetFiles(TestDataRoot).ToDictionary(file => file, File.ReadAllBytes);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.CompressAsync(new FileCompressionOptions());

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        File.Exists(path + ".gz").Should().BeFalse();
        foreach (var file in before)
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
    }

    [Fact]
    public async Task SourceChangedAfterRequiredBackup_RefusesCompressionAndKeepsTheSource()
    {
        var path = await CreateSourceAsync();
        var late = Serialize(BuildTrade(99)) + "\n";
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot })
        {
            // A late writer commits after every backup was taken but before this file is fenced.
            CompressionCheckpoint = stage =>
            {
                if (stage == CompressionPublicationStage.BackupsCompleted)
                    File.AppendAllText(path, late);
            }
        };

        var result = await service.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: true));

        result.Success.Should().BeFalse("the recovery copy would not hold the bytes being compressed");
        result.Errors.Should().Contain(error => error.Contains("after its required backup"));
        File.Exists(path + ".gz").Should().BeFalse();
        File.ReadAllText(path).Should().EndWith(late, "the source and its late write are kept");
    }

    [Fact]
    public async Task Publication_HoldsTheArchiveLockUntilItsChecksumIsWritten()
    {
        var path = await CreateSourceAsync();
        var archive = path + ".gz";
        bool? lockedWhenPublished = null;
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot })
        {
            CompressionCheckpoint = stage =>
            {
                if (stage != CompressionPublicationStage.Published)
                    return;
                // A health sweep at this instant would see the archive before its sidecar exists.
                lockedWhenPublished = !ArchiveAppendLock.TryAcquire(archive, out var probe);
                probe?.Dispose();
            }
        };

        var result = await service.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false));

        result.Success.Should().BeTrue();
        lockedWhenPublished.Should().BeTrue("a health check must skip the archive until its checksum exists");
        File.Exists(archive + ".sha256").Should().BeTrue();
    }

    [Fact]
    public async Task ReplayOpeningAcrossCompressionAndALateAppend_ReadsTheExtendedArchive()
    {
        var path = await CreateSourceAsync();
        var late = Serialize(BuildTrade(99)) + "\n";
        var extended = await File.ReadAllTextAsync(path) + late;
        JsonlArchiveFiles.AfterSourceOpenedForTest = opened =>
        {
            if (opened != path)
                return;
            JsonlArchiveFiles.AfterSourceOpenedForTest = null;
            // Between replay's two opens, compression publishes and deletes the source, and a late
            // append extends the published archive.
            using (var archive = File.Create(path + ".gz"))
            using (var gzip = new GZipStream(archive, CompressionLevel.Fastest))
                gzip.Write(Encoding.UTF8.GetBytes(extended));
            File.Delete(path);
        };
        try
        {
            await using var opened = await JsonlArchiveFiles.OpenReadAsync(path, CancellationToken.None);
            opened.Name.Should().Be(path + ".gz");
            await using var gzip = new GZipStream(opened, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            (await reader.ReadToEndAsync()).Should().Be(extended, "the later generation is the only representation left");
        }
        finally
        {
            JsonlArchiveFiles.AfterSourceOpenedForTest = null;
        }
    }

    [Fact]
    public async Task WithdrawnGzipPath_ReplaysTheSurvivingSource()
    {
        var path = await CreateSourceAsync();

        JsonlArchiveFiles.Exists(path + ".gz").Should().BeTrue();
        (await ReadAllAsync(new JsonlReplayer(path + ".gz").ReadEventsAsync())).Should().Equal(ExpectedLines());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenPartitionPeriod_IsDeferredWhilePriorPeriodCompresses(bool canonical)
    {
        var prior = await CreateSourceAsync();
        var today = DateTime.UtcNow;
        var current = canonical
            ? Path.Combine(TestDataRoot, today.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture),
                "alpaca", "SPY-2024-01-01", "20240101_Trade.jsonl")
            : Path.Combine(TestDataRoot, $"SPY_trade_{today:yyyyMMdd}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        await AtomicFileWriter.WriteAsync(current, string.Join("\n", ExpectedLines()) + "\n");
        var currentBytes = await File.ReadAllBytesAsync(current);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false));

        File.Exists(prior + ".gz").Should().BeTrue("a closed day is compressed");
        File.Exists(prior).Should().BeFalse();
        File.Exists(current + ".gz").Should().BeFalse("the live day stays the sink's plain append target");
        (await File.ReadAllBytesAsync(current)).Should().Equal(currentBytes);
        result.Success.Should().BeTrue("deferring a live period is not a compression failure");
        result.Errors.Should().BeEmpty();
        result.FilesProcessed.Should().Be(1);
        result.Deferred.Should().ContainSingle(entry => entry.StartsWith(current) && entry.Contains("partition period is still open"));
    }

    [Theory]
    [InlineData("SPY/2026-01-02/trades.jsonl", "2026-01-03T00:00:00Z")]
    [InlineData("SPY_trade_20260102.jsonl", "2026-01-03T00:00:00Z")]
    [InlineData("SPY/2026-01-02_14/trades.jsonl", "2026-01-02T15:00:00Z")]
    [InlineData("SPY/2026-01/trades.jsonl", "2026-02-01T00:00:00Z")]
    [InlineData("2026/01/02/alpaca/SPY/Trade.jsonl", "2026-01-03T00:00:00Z")]
    [InlineData("2026/01/02/alpaca/SPY-2025-12-19/Trade.jsonl", "2026-01-03T00:00:00Z")]
    [InlineData("2026/01/02/alpaca/SPY/20251219_Trade.jsonl", "2026-01-03T00:00:00Z")]
    [InlineData("SPY/trades.jsonl", null)]
    public void PartitionPeriodEnd_ReadsThePeriodEncodedInThePath(string relative, string? expectedEnd)
    {
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var end = service.PartitionPeriodEnd(Path.Combine(TestDataRoot, relative));

        end.Should().Be(expectedEnd is null ? null : DateTimeOffset.Parse(expectedEnd, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task OpenWriter_RecentlyWrittenSource_IsDeferredAndPreserved()
    {
        var path = await CreateSourceAsync();
        var before = Directory.GetFiles(TestDataRoot).ToDictionary(file => file, File.ReadAllBytes);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        FileCompressionResult result;
        // Same sharing mode as JsonlStorageSink.EnsureStream for an active output file.
        await using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete))
            result = await service.CompressAsync(new FileCompressionOptions(
                BackupBeforeCompression: false, MinimumQuietPeriod: TimeSpan.FromMinutes(5)));

        result.Success.Should().BeTrue("a recently written file is deferred, not failed");
        result.Errors.Should().BeEmpty();
        result.Deferred.Should().ContainSingle(entry => entry.StartsWith(path));
        File.Exists(path + ".gz").Should().BeFalse();
        foreach (var file in before)
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
    }

    [Theory]
    [InlineData("StagedVerified")]
    [InlineData("PublicationSynced")]
    public async Task AppendDuringCompression_KeepsSourceAndPublishesNoIncompleteArchive(string stageName)
    {
        var path = await CreateSourceAsync();
        var stage = Enum.Parse<CompressionPublicationStage>(stageName);
        var late = Serialize(BuildTrade(99)) + "\n";
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot })
        {
            CompressionCheckpoint = checkpoint =>
            {
                if (checkpoint == stage)
                {
                    using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
                    writer.Write(Encoding.UTF8.GetBytes(late));
                }
            }
        };

        var result = await service.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false));

        result.Success.Should().BeFalse();
        File.Exists(path + ".gz").Should().BeFalse();
        File.ReadAllText(path).Should().EndWith(late);
    }

    [Fact]
    public async Task CompressedArchive_WithoutSourceChecksum_StillGetsChecksumAndDetectsTruncation()
    {
        var path = Path.Combine(TestDataRoot, "SPY_trade_20260102.jsonl");
        Directory.CreateDirectory(TestDataRoot);
        // Ordinary sink partitions carry no .sha256 of their own.
        await File.WriteAllTextAsync(path, string.Join("\n", ExpectedLines()) + "\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        (await service.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false))).Success.Should().BeTrue();

        File.Exists(path + ".gz.sha256").Should().BeTrue("every published archive gets integrity evidence");
        (await service.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true)))
            .Issues.Should().NotContain(issue => issue.Type == IssueType.ChecksumMismatch);

        await using (var archive = new FileStream(path + ".gz", FileMode.Open, FileAccess.Write))
            archive.SetLength(archive.Length / 2);

        (await service.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true)))
            .Issues.Should().Contain(issue => issue.Type == IssueType.ChecksumMismatch,
                "a truncated archive must not read as healthy");
    }

    [Fact]
    public async Task CompressedArchive_RetainsChecksumCoverageAndDetectsLaterCorruption()
    {
        var path = await CreateSourceAsync();
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        (await service.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false))).Success.Should().BeTrue();

        File.Exists(path + ".sha256").Should().BeFalse();
        var sidecar = (await File.ReadAllTextAsync(path + ".gz.sha256")).Split(' ', 2)[0];
        sidecar.Should().Be(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path + ".gz"))).ToLowerInvariant());
        (await service.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true)))
            .Issues.Should().NotContain(issue => issue.Type == IssueType.ChecksumMismatch);

        await using (var archive = new FileStream(path + ".gz", FileMode.Open, FileAccess.Write))
        {
            archive.Position = archive.Length - 1;
            archive.WriteByte(0xFF);
        }

        (await service.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true)))
            .Issues.Should().Contain(issue => issue.Type == IssueType.ChecksumMismatch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CorruptGzipAlongsideSource_RefusesBeforeFirstEvent(int reader)
    {
        var path = await CreateSourceAsync();
        await File.WriteAllBytesAsync(path + ".gz", Encoding.UTF8.GetBytes("not a gzip archive"));
        var before = Directory.GetFiles(TestDataRoot).ToDictionary(file => file, File.ReadAllBytes);
        var observed = new List<MarketEvent>();

        var error = await Record.ExceptionAsync(async () =>
        {
            await foreach (var evt in Read(reader))
                observed.Add(evt);
        });

        error.Should().NotBeNull();
        observed.Should().BeEmpty();
        foreach (var file in before)
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
    }

    private async Task<string> CreateSourceAsync(string? subdirectory = null)
    {
        var directory = subdirectory is null ? TestDataRoot : Path.Combine(TestDataRoot, subdirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "SPY_trade_20260102.jsonl");
        await AtomicFileWriter.WriteWithChecksumAsync(path,
            Encoding.UTF8.GetBytes(string.Join("\n", ExpectedLines()) + "\n"));
        return path;
    }

    private IAsyncEnumerable<MarketEvent> Read(int reader, CancellationToken ct = default) => reader switch
    {
        0 => new JsonlReplayer(TestDataRoot).ReadEventsAsync(ct),
        1 => new JsonlMarketDataStore(TestDataRoot).QueryAsync(new MarketDataQuery(), ct),
        2 => new MemoryMappedJsonlReader(TestDataRoot, new MemoryMappedReaderOptions
        { MinFileSizeForMapping = long.MaxValue, BatchSize = 1 }).ReadEventsAsync(ct),
        3 => new MemoryMappedJsonlReader(TestDataRoot, new MemoryMappedReaderOptions
        { MinFileSizeForMapping = 1, BatchSize = 1 }).ReadEventsAsync(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(reader))
    };

    private static async Task<IReadOnlyList<string>> ReadAllAsync(IAsyncEnumerable<MarketEvent> events)
    {
        var result = new List<string>();
        await foreach (var evt in events)
            result.Add(Serialize(evt));
        return result;
    }

    private static string[] ExpectedLines() => Enumerable.Range(1, 3)
        .Select(sequence => Serialize(BuildTrade(sequence))).ToArray();

    private static MarketEvent BuildTrade(long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero).AddSeconds(sequence);
        var trade = new Trade(timestamp, "SPY", 100m + sequence, 10, AggressorSide.Buy, sequence, "TEST", "XNYS");
        return MarketEvent.Trade(timestamp, "SPY", trade, "TEST", sequence);
    }

    private static string Serialize(MarketEvent evt) =>
        JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions);

    private static async Task WriteGzipAsync(string path, byte[] payload)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using (var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true))
            await gzip.WriteAsync(payload);
        file.Flush(flushToDisk: true);
    }
}
