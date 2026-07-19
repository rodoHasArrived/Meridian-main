using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using FsCheck.Xunit;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Baseline WAL behavior on well-formed logs: initialization, append sequencing/type/checksum/
/// timestamp, commit markers, uncommitted-record retrieval, flush, truncation and archival, and an
/// FsCheck property for ordered single-pass replay. Failure-injection coverage is split into
/// sibling suites so the "happy path" and "what happens on damage" concerns stay separately
/// readable: <see cref="WriteAheadLogCorruptionModeTests"/> exercises the configured corruption
/// response modes, and <see cref="WriteAheadLogFuzzTests"/> exercises byte-level truncation and
/// corruption recovery.
/// </summary>
public sealed class WriteAheadLogTests : TempDirectoryAsyncTestBase
{

    [Fact]
    public async Task InitializeAsync_CreatesWalFile()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await wal.InitializeAsync();

        Directory.GetFiles(TestDataRoot, "*.wal").Should().HaveCount(1);
    }

    [Fact]
    public async Task AppendAsync_ReturnsRecordWithIncreasingSequence()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var r1 = await wal.AppendAsync(new { Symbol = "SPY", Price = 450.0 }, "trade");
        var r2 = await wal.AppendAsync(new { Symbol = "AAPL", Price = 180.0 }, "trade");

        r2.Sequence.Should().BeGreaterThan(r1.Sequence);
    }

    [Fact]
    public async Task AppendAsync_SetsRecordType()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var record = await wal.AppendAsync("test data", "marker");

        record.RecordType.Should().Be("marker");
    }

    [Fact]
    public async Task AppendAsync_SetsNonEmptyChecksum()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var record = await wal.AppendAsync("hello", "test");

        record.Checksum.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AppendAsync_SetsTimestampNearNow()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var before = DateTime.UtcNow;
        var record = await wal.AppendAsync("data", "test");
        var after = DateTime.UtcNow;

        record.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task CommitAsync_WritesCommitMarker()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var r1 = await wal.AppendAsync("data1", "trade");
        await wal.CommitAsync(r1.Sequence);

        // After commit, there should be no uncommitted records
        var uncommitted = new List<WalRecord>();
        await foreach (var record in wal.GetUncommittedRecordsAsync())
        {
            uncommitted.Add(record);
        }

        uncommitted.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUncommittedRecordsAsync_ReturnsAppendedRecords_BeforeCommit()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        await wal.AppendAsync("data1", "trade");
        await wal.AppendAsync("data2", "trade");
        await wal.FlushAsync();

        // Don't commit - records should be uncommitted
        // Need to read from a new WAL instance to verify recovery
        await wal.DisposeAsync();

        await using var wal2 = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        // Don't call InitializeAsync to avoid creating new file
        var uncommitted = new List<WalRecord>();
        await foreach (var record in wal2.GetUncommittedRecordsAsync())
        {
            uncommitted.Add(record);
        }

        uncommitted.Should().HaveCount(2, "exactly 2 records were appended and none were committed");
    }

    [Fact]
    public async Task GetUncommittedRecordsAsync_WhenCancelled_ThrowsInsteadOfReturningPartialScan()
    {
        // A silently-ended scan is indistinguishable from a complete one, which lets
        // recovery replay lose records and TruncateAsync delete files that still hold
        // uncommitted data. Cancellation must surface as an exception.
        await using (var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await wal.InitializeAsync();
            await wal.AppendAsync(new { Symbol = "SPY", Price = 450.0 }, "trade");
            await wal.FlushAsync();
        }

        await using var wal2 = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in wal2.GetUncommittedRecordsAsync(cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a cancelled WAL scan must throw rather than masquerade as an empty log");
    }

    [Fact]
    public async Task FlushAsync_WithNoWriter_DoesNotThrow()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        // Do NOT initialize - writer is null

        var act = () => wal.FlushAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TruncateAsync_RemovesCommittedWalFiles()
    {
        var options = new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            MaxWalFileSizeBytes = 100, // Very small to force rotation
            ArchiveAfterTruncate = false
        };

        await using var wal = new WriteAheadLog(TestDataRoot, options);
        await wal.InitializeAsync();

        // Write enough to trigger rotation
        for (int i = 0; i < 20; i++)
        {
            await wal.AppendAsync($"large-payload-data-{i}-{new string('x', 50)}", "data");
        }
        await wal.FlushAsync();

        var walFilesBefore = Directory.GetFiles(TestDataRoot, "*.wal");

        // Commit everything and truncate
        var lastRecord = await wal.AppendAsync("final", "marker");
        await wal.CommitAsync(lastRecord.Sequence);
        await wal.TruncateAsync(lastRecord.Sequence);

        var walFilesAfter = Directory.GetFiles(TestDataRoot, "*.wal");
        walFilesAfter.Length.Should().BeLessThan(walFilesBefore.Length,
            "committed WAL files should be truncated");
    }

    [Fact]
    public async Task TruncateAsync_WithArchive_CreatesGzFile()
    {
        var options = new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            MaxWalFileSizeBytes = 100,
            ArchiveAfterTruncate = true
        };

        await using var wal = new WriteAheadLog(TestDataRoot, options);
        await wal.InitializeAsync();

        for (int i = 0; i < 20; i++)
        {
            await wal.AppendAsync($"payload-{i}-{new string('x', 50)}", "data");
        }
        await wal.FlushAsync();

        var lastRecord = await wal.AppendAsync("final", "marker");
        await wal.CommitAsync(lastRecord.Sequence);
        await wal.TruncateAsync(lastRecord.Sequence);

        var archiveDir = Path.Combine(TestDataRoot, "archive");
        Directory.Exists(archiveDir).Should().BeTrue(
            "archive directory should be created when ArchiveAfterTruncate is true");
        Directory.GetFiles(archiveDir, "*.gz").Should().NotBeEmpty(
            "truncated WAL files should be archived as .gz");
    }

    [Fact]
    public async Task TruncateAsync_UsesSegmentNameMetadata_WithoutRescanningRecords()
    {
        // Audit finding P10: truncation used to re-read and re-checksum every record of every
        // segment. Segment names embed the creation-time sequence counter, so committed-ness
        // is provable from the successor's name alone. Proof of no-scan: a corrupted record
        // in a committed segment would increment CorruptedRecordCount if read.
        var options = new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            MaxWalFileSizeBytes = 1, // every append rotates into its own segment
            ArchiveAfterTruncate = false
        };

        await using var wal = new WriteAheadLog(TestDataRoot, options);
        await wal.InitializeAsync();

        for (var i = 0; i < 4; i++)
        {
            await wal.AppendAsync($"payload-{i}", "data");
        }

        var walFiles = Directory.GetFiles(TestDataRoot, "*.wal").OrderBy(f => f, StringComparer.Ordinal).ToList();
        walFiles.Count.Should().BeGreaterThan(2, "rotation must have produced multiple segments");

        // Corrupt a record in a completed segment: flip the payload so the stored checksum
        // no longer matches. Metadata-based truncation must not notice.
        string? tamperedSegment = null;
        foreach (var walFile in walFiles.Take(walFiles.Count - 1)) // never touch the active tail
        {
            var lines = await File.ReadAllLinesAsync(walFile);
            var recordIndex = Array.FindIndex(lines, l => l.Contains("payload-", StringComparison.Ordinal));
            if (recordIndex < 0)
                continue;

            lines[recordIndex] = lines[recordIndex].Replace("payload-", "tampered-", StringComparison.Ordinal);
            await File.WriteAllLinesAsync(walFile, lines);
            tamperedSegment = walFile;
            break;
        }

        tamperedSegment.Should().NotBeNull("a completed segment holding a record is required for the no-scan proof");

        var last = await wal.AppendAsync("final", "marker");
        await wal.CommitAsync(last.Sequence);
        await wal.TruncateAsync(last.Sequence);

        Directory.GetFiles(TestDataRoot, "*.wal").Should().ContainSingle(
            "every completed segment is provably committed from its successor's base sequence");
        wal.CorruptedRecordCount.Should().Be(0,
            "metadata-based truncation must not read (and re-checksum) segment records");
    }

    [Fact]
    public async Task TruncateAsync_KeepsSegmentsHoldingRecordsAboveThroughSequence()
    {
        var options = new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            MaxWalFileSizeBytes = 1, // every append rotates into its own segment
            ArchiveAfterTruncate = false
        };

        await using var wal = new WriteAheadLog(TestDataRoot, options);
        await wal.InitializeAsync();

        var first = await wal.AppendAsync("committed-payload", "data");
        var second = await wal.AppendAsync("uncommitted-payload-a", "data");
        await wal.AppendAsync("uncommitted-payload-b", "data");
        await wal.FlushAsync();

        await wal.CommitAsync(first.Sequence);
        await wal.TruncateAsync(first.Sequence);

        var uncommitted = new List<long>();
        await foreach (var record in wal.GetUncommittedRecordsAsync())
        {
            uncommitted.Add(record.Sequence);
        }

        uncommitted.Should().Contain(new[] { second.Sequence, second.Sequence + 1 },
            "segments with records above the committed sequence must survive truncation");
    }

    [Fact]
    public async Task TruncateAsync_ForeignNamedFileWithValidRecords_IsTruncatedViaScanFallback()
    {
        var options = new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            MaxWalFileSizeBytes = 1,
            ArchiveAfterTruncate = false
        };

        await using var wal = new WriteAheadLog(TestDataRoot, options);
        await wal.InitializeAsync();

        await wal.AppendAsync("first-payload", "data");
        await wal.AppendAsync("second-payload", "data");

        // Copy a completed, well-formed segment under a name the metadata parser rejects:
        // eligibility must fall back to the record scan and still truncate it once committed.
        string? completedSegment = null;
        foreach (var walFile in Directory.GetFiles(TestDataRoot, "wal_*.wal").OrderBy(f => f, StringComparer.Ordinal))
        {
            if ((await File.ReadAllTextAsync(walFile)).Contains("first-payload", StringComparison.Ordinal))
            {
                completedSegment = walFile;
                break;
            }
        }

        completedSegment.Should().NotBeNull("the rotated segment holding the first record must exist");
        var foreignPath = Path.Combine(TestDataRoot, "legacy-import.wal");
        File.Copy(completedSegment!, foreignPath);

        var last = await wal.AppendAsync("final", "marker");
        await wal.CommitAsync(last.Sequence);
        await wal.TruncateAsync(last.Sequence);

        File.Exists(foreignPath).Should().BeFalse(
            "a fully committed file with an unparsable name must still truncate via the scan fallback");
        Directory.GetFiles(TestDataRoot, "*.wal").Should().ContainSingle(
            "only the active segment should remain");
    }

    [Fact]
    public async Task MultipleAppendAndCommit_MaintainsSequenceOrder()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var sequences = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var record = await wal.AppendAsync($"event-{i}", "trade");
            sequences.Add(record.Sequence);
        }

        sequences.Should().BeInAscendingOrder();
        sequences.Distinct().Should().HaveCount(10, "all sequences should be unique");
    }

    [Property(MaxTest = 75)]
    public void Scenario_WalReplay_GeneratedUncommittedStreamsReplayOnceInSequence(int recordCountSeed, int duplicateModuloSeed)
    {
        Scenario_WalReplay_GeneratedUncommittedStreamsReplayOnceInSequenceAsync(recordCountSeed, duplicateModuloSeed)
            .GetAwaiter()
            .GetResult();
    }

    [Fact]
    public async Task WalRecord_DeserializePayload_WorksForSimpleTypes()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var record = await wal.AppendAsync("hello world", "string-data");

        var deserialized = record.DeserializePayload<string>();
        deserialized.Should().Be("hello world");
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        await wal.DisposeAsync();
        var act = () => wal.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    private async Task Scenario_WalReplay_GeneratedUncommittedStreamsReplayOnceInSequenceAsync(
        int recordCountSeed,
        int duplicateModuloSeed)
    {
        var scenarioDir = Path.Combine(TestDataRoot, $"property_{Guid.NewGuid():N}");
        Directory.CreateDirectory(scenarioDir);
        var recordCount = Bound(recordCountSeed, minInclusive: 1, maxInclusive: 120);
        var duplicateModulo = Bound(duplicateModuloSeed, minInclusive: 1, maxInclusive: 12);

        await using (var wal = new WriteAheadLog(scenarioDir, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await wal.InitializeAsync();
            for (var i = 0; i < recordCount; i++)
            {
                await wal.AppendAsync($"event-{i % duplicateModulo}", i % 2 == 0 ? "trade" : "quote");
            }

            await wal.FlushAsync();
        }

        List<WalRecord> replayed;
        await using (var recovery = new WriteAheadLog(scenarioDir, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await recovery.InitializeAsync();
            replayed = await ReadUncommittedAsync(recovery);
            replayed.Should().HaveCount(recordCount);
            replayed.Select(record => record.Sequence).Should().BeInAscendingOrder();
            replayed.Select(record => record.Sequence).Distinct().Should().HaveCount(recordCount);

            await recovery.CommitAsync(replayed[^1].Sequence);
        }

        await using (var secondRecovery = new WriteAheadLog(scenarioDir, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await secondRecovery.InitializeAsync();
            var afterCommit = await ReadUncommittedAsync(secondRecovery);
            afterCommit.Should().BeEmpty("committed WAL records must not replay a second time");
        }
    }

    private static async Task<List<WalRecord>> ReadUncommittedAsync(WriteAheadLog wal)
    {
        var records = new List<WalRecord>();
        await foreach (var record in wal.GetUncommittedRecordsAsync())
        {
            records.Add(record);
        }

        return records;
    }

    private static int Bound(int seed, int minInclusive, int maxInclusive)
    {
        var width = (long)maxInclusive - minInclusive + 1L;
        return minInclusive + (int)(Math.Abs((long)seed) % width);
    }
}
