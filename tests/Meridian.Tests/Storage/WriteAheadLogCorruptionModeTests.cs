using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for WAL corruption response modes introduced in fix 4.3 of the
/// March-2026 high-impact improvement brainstorm document. Scope is limited to how the WAL
/// <em>reacts</em> to detected corruption under each configured mode; well-formed-log behavior
/// lives in <see cref="WriteAheadLogTests"/> and raw byte-level damage generation lives in
/// <see cref="WriteAheadLogFuzzTests"/>.
/// </summary>
public sealed class WriteAheadLogCorruptionModeTests : TempDirectoryAsyncTestBase
{

    // ── WalCorruptionMode.Skip (default / legacy) ─────────────────────────

    [Fact]
    public async Task CorruptionMode_Skip_RecoveryCompletesWithoutException()
    {
        // Arrange — write a valid WAL with one good record, then inject a corrupt line.
        await WriteSeedWalAsync(seedRecords: 2, corruptLines: 1);

        // Act — use default Skip mode; must not throw even though corruption is present.
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
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
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
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

        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
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
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
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
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
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

        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
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

        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
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
    public void WalOptions_DefaultCorruptionMode_IsAlert()
    {
        // The durability backstop must never discard records silently by default:
        // Alert skips corrupt records but raises CorruptionDetected for operators.
        var options = new WalOptions();
        options.CorruptionMode.Should().Be(WalCorruptionMode.Alert);
    }

    [Theory]
    [InlineData(WalCorruptionMode.Alert, "3|partial")]
    [InlineData(WalCorruptionMode.Halt, "3|partial")]
    [InlineData(WalCorruptionMode.Alert, "3|2026-01-01T00:00:00.0000000Z|trade|0000|{\"price\":")]
    [InlineData(WalCorruptionMode.Halt, "3|2026-01-01T00:00:00.0000000Z|trade|0000|{\"price\":")]
    public async Task InitializeAsync_TornFinalWrite_RecoversCompleteRecordsWithoutCorruptionAlert(
        WalCorruptionMode mode, string tail)
    {
        await WriteSeedWalAsync(seedRecords: 2, corruptLines: 0);
        var source = Directory.GetFiles(TestDataRoot, "*.wal").Should().ContainSingle().Subject;
        await File.AppendAllTextAsync(source, tail);
        var retainedBytes = await File.ReadAllBytesAsync(source);
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = mode
        });
        var alerts = 0;
        wal.CorruptionDetected += _ => alerts++;

        await wal.InitializeAsync();

        wal.LastRecoveryEventCount.Should().Be(2);
        wal.CorruptedRecordCount.Should().Be(0, "a crash-torn suffix is distinct from a corrupt complete record");
        wal.SkippedRecordCount.Should().Be(0);
        alerts.Should().Be(0);
        (await File.ReadAllBytesAsync(source)).Should().Equal(retainedBytes, "recovery must retain the crash evidence");
        var next = await wal.AppendAsync(new { Index = 3 }, "trade");
        next.Sequence.Should().Be(3, "preceding valid sequence numbers remain authoritative");
    }

    [Fact]
    public async Task InitializeAsync_ChecksumValidFinalRecordWithoutNewline_IsRecovered()
    {
        await WriteSeedWalAsync(seedRecords: 2, corruptLines: 0);
        var source = Directory.GetFiles(TestDataRoot, "*.wal").Should().ContainSingle().Subject;
        var text = (await File.ReadAllTextAsync(source)).TrimEnd('\r', '\n');
        await File.WriteAllTextAsync(source, text);
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Halt
        });

        await wal.InitializeAsync();

        wal.LastRecoveryEventCount.Should().Be(2, "a fully checksummed record contains no torn payload to discard");
        wal.CorruptedRecordCount.Should().Be(0);
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
        await using (var seed = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync }))
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
            var walFiles = Directory.GetFiles(TestDataRoot, "*.wal").OrderBy(f => f).ToArray();
            walFiles.Should().NotBeEmpty("seed must have created at least one .wal file");
            var walFile = walFiles[^1];

            // Append garbage lines that will fail checksum validation.
            var corrupt = Enumerable.Range(0, corruptLines)
                .Select(i => $"CORRUPTED-LINE-{i}|bad-timestamp|bad-type|bad-checksum|bad-payload");
            await File.AppendAllLinesAsync(walFile, corrupt);
        }
    }
}
