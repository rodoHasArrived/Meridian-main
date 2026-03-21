using FluentAssertions;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class WriteAheadLogTests : IAsyncDisposable
{
    private readonly string _walDir;

    public WriteAheadLogTests()
    {
        _walDir = Path.Combine(Path.GetTempPath(), $"mdc_wal_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_walDir);
    }

    public async ValueTask DisposeAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_walDir))
                    Directory.Delete(_walDir, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { await Task.Delay(20); }
            catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(20); }
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesWalFile()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await wal.InitializeAsync();

        Directory.GetFiles(_walDir, "*.wal").Should().HaveCount(1);
    }

    [Fact]
    public async Task AppendAsync_ReturnsRecordWithIncreasingSequence()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var r1 = await wal.AppendAsync(new { Symbol = "SPY", Price = 450.0 }, "trade");
        var r2 = await wal.AppendAsync(new { Symbol = "AAPL", Price = 180.0 }, "trade");

        r2.Sequence.Should().BeGreaterThan(r1.Sequence);
    }

    [Fact]
    public async Task AppendAsync_SetsRecordType()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var record = await wal.AppendAsync("test data", "marker");

        record.RecordType.Should().Be("marker");
    }

    [Fact]
    public async Task AppendAsync_SetsNonEmptyChecksum()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var record = await wal.AppendAsync("hello", "test");

        record.Checksum.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AppendAsync_SetsTimestampNearNow()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var before = DateTime.UtcNow;
        var record = await wal.AppendAsync("data", "test");
        var after = DateTime.UtcNow;

        record.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task CommitAsync_WritesCommitMarker()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
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
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        await wal.AppendAsync("data1", "trade");
        await wal.AppendAsync("data2", "trade");
        await wal.FlushAsync();

        // Don't commit - records should be uncommitted
        // Need to read from a new WAL instance to verify recovery
        await wal.DisposeAsync();

        await using var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        // Don't call InitializeAsync to avoid creating new file
        var uncommitted = new List<WalRecord>();
        await foreach (var record in wal2.GetUncommittedRecordsAsync())
        {
            uncommitted.Add(record);
        }

        uncommitted.Should().HaveCount(2, "exactly 2 records were appended and none were committed");
    }

    [Fact]
    public async Task FlushAsync_WithNoWriter_DoesNotThrow()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
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

        await using var wal = new WriteAheadLog(_walDir, options);
        await wal.InitializeAsync();

        // Write enough to trigger rotation
        for (int i = 0; i < 20; i++)
        {
            await wal.AppendAsync($"large-payload-data-{i}-{new string('x', 50)}", "data");
        }
        await wal.FlushAsync();

        var walFilesBefore = Directory.GetFiles(_walDir, "*.wal");

        // Commit everything and truncate
        var lastRecord = await wal.AppendAsync("final", "marker");
        await wal.CommitAsync(lastRecord.Sequence);
        await wal.TruncateAsync(lastRecord.Sequence);

        var walFilesAfter = Directory.GetFiles(_walDir, "*.wal");
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

        await using var wal = new WriteAheadLog(_walDir, options);
        await wal.InitializeAsync();

        for (int i = 0; i < 20; i++)
        {
            await wal.AppendAsync($"payload-{i}-{new string('x', 50)}", "data");
        }
        await wal.FlushAsync();

        var lastRecord = await wal.AppendAsync("final", "marker");
        await wal.CommitAsync(lastRecord.Sequence);
        await wal.TruncateAsync(lastRecord.Sequence);

        var archiveDir = Path.Combine(_walDir, "archive");
        Directory.Exists(archiveDir).Should().BeTrue(
            "archive directory should be created when ArchiveAfterTruncate is true");
        Directory.GetFiles(archiveDir, "*.gz").Should().NotBeEmpty(
            "truncated WAL files should be archived as .gz");
    }

    [Fact]
    public async Task MultipleAppendAndCommit_MaintainsSequenceOrder()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
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

    [Fact]
    public async Task WalRecord_DeserializePayload_WorksForSimpleTypes()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var record = await wal.AppendAsync("hello world", "string-data");

        var deserialized = record.DeserializePayload<string>();
        deserialized.Should().Be("hello world");
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        await wal.DisposeAsync();
        var act = () => wal.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }
}
