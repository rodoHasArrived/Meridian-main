using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Sequence numbers are never reused after a restart, including when the highest-numbered
/// records no longer validate, while an undamaged WAL keeps contiguous numbering.
/// </summary>
public sealed class WriteAheadLogSequenceRecoveryTests : TempDirectoryAsyncTestBase
{
    private static WalOptions Options(WalCorruptionMode mode) => new()
    {
        SyncMode = WalSyncMode.NoSync,
        CorruptionMode = mode
    };

    [Theory]
    [InlineData(WalCorruptionMode.Alert)]
    [InlineData(WalCorruptionMode.Skip)]
    public async Task Restart_HighestRecordsCorrupted_NewSequencesAreAboveEverySequenceOnDisk(WalCorruptionMode mode)
    {
        var segment = await SeedAsync(records: 5);
        // Break the checksums of the last two records (sequences 4 and 5) in place.
        var lines = await File.ReadAllLinesAsync(segment);
        for (var i = lines.Length - 2; i < lines.Length; i++)
        {
            var parts = lines[i].Split('|', 5);
            parts[3] = new string('0', parts[3].Length);
            lines[i] = string.Join('|', parts);
        }
        await File.WriteAllTextAsync(segment, string.Join('\n', lines) + "\n");

        await using var wal = new WriteAheadLog(TestDataRoot, Options(mode));
        await wal.InitializeAsync();
        var next = await wal.AppendAsync(new { Index = 6 }, "trade");

        next.Sequence.Should().BeGreaterThan(5,
            "sequences 4 and 5 were issued and are still on disk in the damaged lines");
    }

    [Fact]
    public async Task Restart_DamagedSegmentWithForeignName_StillSkipsPastItsLines()
    {
        var segment = await SeedAsync(records: 3);
        var foreign = Path.Combine(TestDataRoot, "imported.wal");
        File.Move(segment, foreign);
        var lines = await File.ReadAllLinesAsync(foreign);
        var parts = lines[^1].Split('|', 5);
        parts[3] = new string('0', parts[3].Length);
        lines[^1] = string.Join('|', parts);
        await File.WriteAllTextAsync(foreign, string.Join('\n', lines) + "\n");

        await using var wal = new WriteAheadLog(TestDataRoot, Options(WalCorruptionMode.Alert));
        await wal.InitializeAsync();
        var next = await wal.AppendAsync(new { Index = 4 }, "trade");

        next.Sequence.Should().BeGreaterThan(3);
    }

    [Fact]
    public async Task Restart_HealthyWal_ContinuesContiguously()
    {
        await SeedAsync(records: 3);

        await using var wal = new WriteAheadLog(TestDataRoot, Options(WalCorruptionMode.Alert));
        await wal.InitializeAsync();
        var next = await wal.AppendAsync(new { Index = 4 }, "trade");

        next.Sequence.Should().Be(4);
    }

    [Fact]
    public async Task Restart_OnlyAnIncompleteFinalWrite_ContinuesContiguously()
    {
        var segment = await SeedAsync(records: 3);
        await File.AppendAllTextAsync(segment, "4|2026-11-25T00:00:00.0000000Z|tra");

        await using var wal = new WriteAheadLog(TestDataRoot, Options(WalCorruptionMode.Alert));
        await wal.InitializeAsync();
        var next = await wal.AppendAsync(new { Index = 4 }, "trade");

        next.Sequence.Should().Be(4, "an unacknowledged torn write does not consume a sequence");
    }

    private async Task<string> SeedAsync(int records)
    {
        await using (var seed = new WriteAheadLog(TestDataRoot, Options(WalCorruptionMode.Alert)))
        {
            await seed.InitializeAsync();
            for (var i = 1; i <= records; i++)
                await seed.AppendAsync(new { Index = i }, "trade");
            await seed.FlushAsync();
        }

        return Directory.GetFiles(TestDataRoot, "*.wal").Single();
    }
}
