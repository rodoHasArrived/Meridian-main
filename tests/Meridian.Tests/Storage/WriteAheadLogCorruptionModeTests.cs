using FluentAssertions;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for WAL corruption response modes introduced in fix 4.3 of the
/// March-2026 high-impact improvement brainstorm document.
/// </summary>
public sealed class WriteAheadLogCorruptionModeTests : IAsyncDisposable
{
    private readonly string _walDir;

    public WriteAheadLogCorruptionModeTests()
    {
        _walDir = Path.Combine(Path.GetTempPath(), $"mdc_wal_corrupt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_walDir);
    }

    public async ValueTask DisposeAsync()
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_walDir))
                {
                    Directory.Delete(_walDir, recursive: true);
                }

                // If delete succeeded or directory no longer exists, cleanup is done.
                if (!Directory.Exists(_walDir))
                {
                    return;
                }
            }
            catch (IOException ex) when (attempt < 4)
            {
                lastException = ex;
                await Task.Delay(20);
            }
            catch (UnauthorizedAccessException ex) when (attempt < 4)
            {
                lastException = ex;
                await Task.Delay(20);
            }
        }

        if (Directory.Exists(_walDir))
        {
            throw lastException ?? new IOException($"Failed to delete WAL temp directory '{_walDir}' after 5 attempts.");
        }
    }

    // ── WalCorruptionMode.Skip (default / legacy) ─────────────────────────

    [Fact]
    public async Task CorruptionMode_Skip_RecoveryCompletesWithoutException()
    {
        // Arrange — write a valid WAL with one good record, then inject a corrupt line.
        await WriteSeedWalAsync(seedRecords: 2, corruptLines: 1);

        // Act — use default Skip mode; must not throw even though corruption is present.
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        var act = async () => await wal.InitializeAsync();

        // Assert
        await act.Should().NotThrowAsync();
        // InitializeAsync reads the WAL file twice (recovery pass + sequence-scan pass),
        // so each corrupt line is counted twice in CorruptedRecordCount.
        wal.CorruptedRecordCount.Should().Be(2,
            "the single corrupt line is encountered in both the recovery pass and the sequence-scan pass");
        wal.LastRecoveryEventCount.Should().Be(2, "only the 2 valid records should be recovered");
    }

    [Fact]
    public async Task CorruptionMode_Skip_CorruptionDetectedEvent_IsNotFired()
    {
        // Arrange
        await WriteSeedWalAsync(seedRecords: 1, corruptLines: 1);

        var eventFired = false;
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip
        });
        wal.CorruptionDetected += _ => eventFired = true;

        // Act
        await wal.InitializeAsync();

        // Assert — Skip mode must NOT fire the event.
        eventFired.Should().BeFalse("CorruptionDetected is only fired in Alert mode");
    }

    // ── WalCorruptionMode.Alert ──────────────────────────────────────────

    [Fact]
    public async Task CorruptionMode_Alert_RecoveryCompletesWithoutException()
    {
        // Arrange
        await WriteSeedWalAsync(seedRecords: 3, corruptLines: 2);

        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        var act = async () => await wal.InitializeAsync();

        // Assert — Alert mode still continues recovery without throwing.
        await act.Should().NotThrowAsync();
        wal.LastRecoveryEventCount.Should().Be(3, "valid records should still be recovered");
    }

    [Fact]
    public async Task CorruptionMode_Alert_CorruptionDetectedEvent_IsFiredWithCount()
    {
        // Arrange
        await WriteSeedWalAsync(seedRecords: 2, corruptLines: 3);

        long? reportedCount = null;
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        wal.CorruptionDetected += count => reportedCount = count;

        // Act
        await wal.InitializeAsync();

        // Assert
        reportedCount.Should().NotBeNull("CorruptionDetected must be raised in Alert mode");
        reportedCount.Should().Be(3,
            "the event argument must be the number of corrupted records in the recovery pass");
    }

    [Fact]
    public async Task CorruptionMode_Alert_NoCorruption_EventNotFired()
    {
        // Arrange — a clean WAL with no corruption.
        await WriteSeedWalAsync(seedRecords: 5, corruptLines: 0);

        var eventFired = false;
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        wal.CorruptionDetected += _ => eventFired = true;

        // Act
        await wal.InitializeAsync();

        // Assert
        eventFired.Should().BeFalse("CorruptionDetected must not fire when there is no corruption");
    }

    // ── WalCorruptionMode.Halt ───────────────────────────────────────────

    [Fact]
    public async Task CorruptionMode_Halt_ThrowsInvalidDataException()
    {
        // Arrange
        await WriteSeedWalAsync(seedRecords: 2, corruptLines: 1);

        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Halt
        });

        // Act
        var act = async () => await wal.InitializeAsync();

        // Assert — Halt mode must throw so the application fails fast.
        await act.Should().ThrowAsync<InvalidDataException>(
            "Halt mode must throw when corruption is detected so operators are forced to review");
    }

    [Fact]
    public async Task CorruptionMode_Halt_CleanWal_DoesNotThrow()
    {
        // Arrange — clean WAL, no corruption.
        await WriteSeedWalAsync(seedRecords: 3, corruptLines: 0);

        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Halt
        });

        // Act
        var act = async () => await wal.InitializeAsync();

        // Assert
        await act.Should().NotThrowAsync(
            "Halt mode should only throw when corruption is actually present");
    }

    // ── WalOptions defaults ──────────────────────────────────────────────

    [Fact]
    public void WalOptions_DefaultCorruptionMode_IsSkip()
    {
        // Existing behaviour is preserved by default so no existing deployments break.
        var options = new WalOptions();
        options.CorruptionMode.Should().Be(WalCorruptionMode.Skip);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a seed WAL file with <paramref name="seedRecords"/> valid records
    /// followed by <paramref name="corruptLines"/> lines of garbage.
    /// </summary>
    private async Task WriteSeedWalAsync(int seedRecords, int corruptLines)
    {
        // Write valid records using a WAL instance, then explicitly dispose it before
        // touching the file directly. The WAL holds the file open for writing, so
        // File.AppendAllLinesAsync would fail on Windows if we inject while it is alive.
        await using (var seed = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync }))
        {
            await seed.InitializeAsync();
            for (int i = 0; i < seedRecords; i++)
                await seed.AppendAsync(new { Index = i, Data = $"record-{i}" }, "trade");
            await seed.FlushAsync();
            // Do NOT commit so the records survive as "uncommitted" data to recover.
        }

        // Inject corrupt lines directly into the .wal file — seed is now fully closed.
        if (corruptLines > 0)
        {
            // Find the file that was just created.
            var walFiles = Directory.GetFiles(_walDir, "*.wal").OrderBy(f => f).ToArray();
            walFiles.Should().NotBeEmpty("seed must have created at least one .wal file");
            var walFile = walFiles[^1];

            // Append garbage lines that will fail checksum validation.
            var corrupt = Enumerable.Range(0, corruptLines)
                .Select(i => $"CORRUPTED-LINE-{i}|bad-timestamp|bad-type|bad-checksum|bad-payload");
            await File.AppendAllLinesAsync(walFile, corrupt);
        }
    }
}
