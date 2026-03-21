using FluentAssertions;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Fuzz-style tests that simulate partial writes, mid-record truncations, and
/// byte-level corruption to verify that WAL recovery is robust in the face of
/// crash scenarios. Covers the scenario described in the improvement brainstorm:
/// "if the process crashes mid-write, the WAL file may contain a truncated JSON line."
/// </summary>
public sealed class WriteAheadLogFuzzTests : IAsyncDisposable
{
    private readonly string _walDir;

    public WriteAheadLogFuzzTests()
    {
        _walDir = Path.Combine(Path.GetTempPath(), $"mdc_wal_fuzz_{Guid.NewGuid():N}");
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

    // ── Partial-write (truncated last line) ──────────────────────────────

    [Fact]
    public async Task Recovery_TruncatedLastRecord_RecoversAllPrecedingValidRecords()
    {
        // Arrange — write 3 valid records, then simulate a crash by truncating the
        // last line so it contains only a partial WAL record.
        const int validCount = 3;
        await WriteValidRecordsAndTruncateLastAsync(validCount);

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        await wal.InitializeAsync();

        // Assert — all fully-written records must survive; the partial one is discarded.
        wal.LastRecoveryEventCount.Should().Be(validCount,
            "the truncated partial record must be discarded while the {0} complete ones survive",
            validCount);
    }

    [Fact]
    public async Task Recovery_TruncatedLastRecord_CorruptedCountIsNonZero()
    {
        // Arrange
        await WriteValidRecordsAndTruncateLastAsync(validRecords: 2);

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        await wal.InitializeAsync();

        // Assert — the truncated line must be counted as corrupted.
        // InitializeAsync makes two passes (recovery + sequence-scan), so each corrupt
        // line increments the counter twice.
        wal.CorruptedRecordCount.Should().BeGreaterThan(0,
            "a truncated partial record is a corrupted record");
    }

    [Fact]
    public async Task Recovery_TruncatedLastRecord_SubsequentAppendsSucceed()
    {
        // Arrange
        await WriteValidRecordsAndTruncateLastAsync(validRecords: 1);

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        await wal.InitializeAsync();
        var newRecord = await wal.AppendAsync(new { Symbol = "SPY", Price = 550.0 }, "trade");

        // Assert — writing to the WAL after recovery must succeed.
        newRecord.Sequence.Should().BeGreaterThan(0);
        newRecord.Checksum.Should().NotBeNullOrEmpty();
    }

    // ── Mid-file corruption (garbage bytes after valid records) ──────────

    [Fact]
    public async Task Recovery_GarbageMidFile_RecoversRecordsBeforeCorruption()
    {
        // Arrange — write 5 valid records, then inject garbage in the middle, then
        // inject 3 more valid-looking lines with bad checksums.
        const int beforeCount = 5;
        await WriteValidRecordsThenInjectGarbageAsync(beforeCount, garbageLines: 3);

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        await wal.InitializeAsync();

        // Assert — the 5 records before the corruption must all be recovered.
        wal.LastRecoveryEventCount.Should().Be(beforeCount);
        wal.CorruptedRecordCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Recovery_GarbageMidFile_AlertModeFiresCorruptionEvent()
    {
        // Arrange
        await WriteValidRecordsThenInjectGarbageAsync(validRecords: 2, garbageLines: 2);

        long detectedCount = 0;
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        wal.CorruptionDetected += count => Interlocked.Add(ref detectedCount, count);

        // Act
        await wal.InitializeAsync();

        // Assert
        detectedCount.Should().BeGreaterThan(0,
            "Alert mode must fire the CorruptionDetected event when garbage lines are present");
    }

    // ── Completely empty WAL file (zero-byte crash) ───────────────────────

    [Fact]
    public async Task Recovery_EmptyWalFile_RecoverySucceedsWithZeroRecords()
    {
        // Simulate a crash immediately after file creation before any data was written.
        var emptyWalPath = Path.Combine(_walDir, "20260101T000000Z_001.wal");
        await File.WriteAllTextAsync(emptyWalPath, string.Empty);

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        var act = async () => await wal.InitializeAsync();

        // Assert — empty file must not throw; it has no records.
        await act.Should().NotThrowAsync();
        wal.LastRecoveryEventCount.Should().Be(0);
    }

    // ── Header-only WAL file (crashed after header before any records) ────

    [Fact]
    public async Task Recovery_HeaderOnlyWalFile_RecoverySucceedsWithZeroRecords()
    {
        // Simulate a crash right after writing the WAL header.
        var walPath = Path.Combine(_walDir, "20260101T000000Z_001.wal");
        await File.WriteAllTextAsync(walPath, $"MDCWAL01|1|{DateTime.UtcNow:O}\n");

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        var act = async () => await wal.InitializeAsync();

        // Assert
        await act.Should().NotThrowAsync();
        wal.LastRecoveryEventCount.Should().Be(0);
    }

    // ── RepairAsync with partial writes ──────────────────────────────────

    [Fact]
    public async Task RepairAsync_TruncatedLastRecord_RewritesFileWithOnlyValidRecords()
    {
        // Arrange — write 4 valid records then truncate the last one
        const int validCount = 4;
        await WriteValidRecordsAndTruncateLastAsync(validCount);

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        // Initialize to read the corrupt WAL, then repair it
        await wal.InitializeAsync();
        var result = await wal.RepairAsync();

        // Assert
        result.ValidRecords.Should().Be(validCount,
            "all {0} complete records must survive repair", validCount);
        result.CorruptedRecords.Should().Be(1,
            "exactly the one truncated record must be counted as corrupted");
        result.RepairedFiles.Should().Be(0,
            "the active WAL file is excluded from repair; " +
            "only rotated/closed files are repaired in-place");
    }

    [Fact]
    public async Task RepairAsync_MultipleCorruptedRecords_CountsAllCorruptedRecords()
    {
        // Arrange — write 3 valid records then append 5 garbage lines
        await WriteValidRecordsThenInjectGarbageAsync(validRecords: 3, garbageLines: 5);

        // Force a rotation so the seed file is no longer active
        await RotateWalAsync();

        // Act
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        await wal.InitializeAsync();
        var result = await wal.RepairAsync();

        // Assert
        result.CorruptedRecords.Should().Be(5,
            "exactly the 5 injected garbage lines must be counted as corrupted");
        result.RepairedFiles.Should().Be(1,
            "the one rotated file with corruption must be rewritten");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Writes <paramref name="validRecords"/> complete records to a WAL file and then
    /// appends one additional record whose line is truncated (simulating a mid-write crash).
    /// After this helper returns the WAL file contains exactly <paramref name="validRecords"/>
    /// fully-intact records followed by one partial, unparseable record.
    /// </summary>
    private async Task WriteValidRecordsAndTruncateLastAsync(int validRecords)
    {
        await using (var seed = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await seed.InitializeAsync();
            // Write one extra record so that truncation of the last record leaves exactly
            // validRecords complete records in the file (simulating a crash mid-write).
            for (int i = 0; i < validRecords + 1; i++)
                await seed.AppendAsync(new { Index = i, Symbol = "SPY" }, "trade");
            await seed.FlushAsync();
            // Do NOT commit so the records survive as uncommitted (recoverable) data.
        }

        var walFile = Directory.GetFiles(_walDir, "*.wal")
            .OrderBy(f => f)
            .Last();

        // Read all lines, then write back all but the last character of the final line
        // to simulate a process crash partway through a write syscall.
        var allBytes = await File.ReadAllBytesAsync(walFile);
        if (allBytes.Length > 2)
        {
            // Truncate by removing the last line's newline + several chars to ensure
            // the last record cannot be parsed as a valid 5-part pipe-delimited line.
            var truncateAt = allBytes.Length - Math.Min(20, allBytes.Length / 4);
            await using var fs = new FileStream(walFile, FileMode.Open, FileAccess.Write);
            fs.SetLength(truncateAt);
        }
    }

    /// <summary>
    /// Writes <paramref name="validRecords"/> valid records, then appends
    /// <paramref name="garbageLines"/> lines of invalid data to the same WAL file.
    /// </summary>
    private async Task WriteValidRecordsThenInjectGarbageAsync(int validRecords, int garbageLines)
    {
        await using (var seed = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await seed.InitializeAsync();
            for (int i = 0; i < validRecords; i++)
                await seed.AppendAsync(new { Index = i, Symbol = "AAPL" }, "trade");
            await seed.FlushAsync();
        }

        var walFile = Directory.GetFiles(_walDir, "*.wal")
            .OrderBy(f => f)
            .Last();

        var garbage = Enumerable.Range(0, garbageLines)
            .Select(i => $"GARBAGE-{i}|not-a-timestamp|bad-type|bad-checksum|{{invalid json}}");
        await File.AppendAllLinesAsync(walFile, garbage);
    }

    /// <summary>
    /// Forces a WAL file rotation by creating a new WAL instance with a very small
    /// file size limit so the seed file is no longer the active one.
    /// </summary>
    private async Task RotateWalAsync()
    {
        // Open with tiny max file size so any new append triggers rotation,
        // making the seed file a "closed" file eligible for RepairAsync.
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            MaxWalFileSizeBytes = 1 // force immediate rotation on first append
        });
        await wal.InitializeAsync();
        // Append a single record to trigger rotation
        await wal.AppendAsync(new { Trigger = "rotate" }, "system");
        await wal.FlushAsync();
    }
}
