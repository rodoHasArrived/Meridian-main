using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Repair never destroys evidence: the original of every rewritten segment is kept in the
/// quarantine folder, and an interrupted final write is reported as such, not as corruption.
/// </summary>
public sealed class WriteAheadLogRepairQuarantineTests : TempDirectoryAsyncTestBase
{
    private const string GarbageLine = "GARBAGE|not-a-timestamp|bad|bad|{}\n";

    private static WalOptions Options() => new() { SyncMode = WalSyncMode.NoSync };

    [Fact]
    public async Task RepairAsync_RewritesCorruptSegment_KeepsOriginalBytesInQuarantine()
    {
        var segment = await SeedSegmentAsync(validRecords: 3, suffix: GarbageLine);
        var original = await File.ReadAllBytesAsync(segment);

        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        var result = await wal.RepairAsync();

        result.CorruptedRecords.Should().Be(1);
        result.ValidRecords.Should().Be(3);
        result.RepairedFiles.Should().Be(1);
        var quarantined = result.QuarantinedFiles.Should().ContainSingle().Subject;
        Path.GetDirectoryName(quarantined).Should().Be(Path.Combine(TestDataRoot, "quarantine"));
        Path.GetFileName(quarantined).Should().StartWith(Path.GetFileName(segment)).And.EndWith(".quarantined");
        (await File.ReadAllBytesAsync(quarantined)).Should().Equal(original);

        (await File.ReadAllTextAsync(segment)).Should().NotContain("GARBAGE");
        Directory.GetFiles(TestDataRoot, "*.repair").Should().BeEmpty();
        result.Findings.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new WalRepairFinding(Path.GetFileName(segment), 5, WalRepairFindingKind.CorruptRecord, null));
    }

    [Fact]
    public async Task RepairAsync_RepairedSegment_RecoversOnlyItsValidRecordsAndIgnoresQuarantine()
    {
        await SeedSegmentAsync(validRecords: 3, suffix: GarbageLine);
        await using (var repair = new WriteAheadLog(TestDataRoot, Options()))
            await repair.RepairAsync();

        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        await wal.InitializeAsync();

        wal.CorruptedRecordCount.Should().Be(0);
        var sequences = new List<long>();
        await foreach (var record in wal.GetUncommittedRecordsAsync())
            sequences.Add(record.Sequence);
        sequences.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task RepairAsync_OnlyAnIncompleteFinalWrite_ReportsNoCorruptionAndLeavesFileUntouched()
    {
        var segment = await SeedSegmentAsync(validRecords: 3, suffix: "4|2026-11-25T00:00:00.0000000Z|tra");
        var original = await File.ReadAllBytesAsync(segment);

        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        var result = await wal.RepairAsync();

        result.CorruptedRecords.Should().Be(0);
        result.IncompleteTailRecords.Should().Be(1);
        result.ValidRecords.Should().Be(3);
        result.TotalRecords.Should().Be(3);
        result.RepairedFiles.Should().Be(0);
        result.QuarantinedFiles.Should().BeEmpty();
        result.Findings.Should().ContainSingle().Which.Kind.Should().Be(WalRepairFindingKind.IncompleteTail);
        (await File.ReadAllBytesAsync(segment)).Should().Equal(original);
        Directory.Exists(Path.Combine(TestDataRoot, "quarantine")).Should().BeFalse();
    }

    [Fact]
    public async Task RepairAsync_DryRun_ValidatesCurrentFormatChecksumsAndChangesNothing()
    {
        // A complete line whose fields parse but whose checksum is wrong: only checksum
        // validation can find it.
        var segment = await SeedSegmentAsync(
            validRecords: 2,
            suffix: "3|2026-11-25T00:00:00.0000000Z|trade|deadbeef|{\"Index\":3}\n");
        var original = await File.ReadAllBytesAsync(segment);
        (await File.ReadAllLinesAsync(segment))[0].Should().StartWith("MDCWAL02");

        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        var result = await wal.RepairAsync(dryRun: true);

        result.ValidRecords.Should().Be(2);
        result.CorruptedRecords.Should().Be(1);
        result.RepairedFiles.Should().Be(0);
        result.QuarantinedFiles.Should().BeEmpty();
        result.Findings.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new WalRepairFinding(Path.GetFileName(segment), 4, WalRepairFindingKind.CorruptRecord, 3));
        (await File.ReadAllBytesAsync(segment)).Should().Equal(original);
        Directory.Exists(Path.Combine(TestDataRoot, "quarantine")).Should().BeFalse();
        Directory.GetFiles(TestDataRoot, "*.repair").Should().BeEmpty();
    }

    [Fact]
    public async Task RepairAsync_LeftoverRepairTempFile_IsRemoved()
    {
        var segment = await SeedSegmentAsync(validRecords: 1, suffix: string.Empty);
        var leftover = segment + ".repair";
        await File.WriteAllTextAsync(leftover, "half-written");

        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        var result = await wal.RepairAsync();

        result.CorruptedRecords.Should().Be(0);
        File.Exists(leftover).Should().BeFalse();
    }

    [Fact]
    public async Task RepairAsync_CancelledBeforeScan_ThrowsAndLeavesSegmentUntouched()
    {
        var segment = await SeedSegmentAsync(validRecords: 3, suffix: GarbageLine);
        var original = await File.ReadAllBytesAsync(segment);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        var act = () => wal.RepairAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(segment)).Should().Equal(original);
    }

    // Writes validRecords records into one closed segment, then appends suffix verbatim.
    private async Task<string> SeedSegmentAsync(int validRecords, string suffix)
    {
        await using (var seed = new WriteAheadLog(TestDataRoot, Options()))
        {
            await seed.InitializeAsync();
            for (var i = 1; i <= validRecords; i++)
                await seed.AppendAsync(new { Index = i }, "trade");
            await seed.FlushAsync();
        }

        var segment = Directory.GetFiles(TestDataRoot, "*.wal").Single();
        await File.AppendAllTextAsync(segment, suffix);
        return segment;
    }
}
