using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// A WAL directory has exactly one writer. The lock is taken by <see cref="WriteAheadLog.InitializeAsync"/>
/// and by a repair on an uninitialized instance, and released on disposal; read-only enumeration
/// never takes it.
/// </summary>
public sealed class WriteAheadLogDirectoryLockTests : TempDirectoryAsyncTestBase
{
    private static WalOptions Options(WalCorruptionMode mode = WalCorruptionMode.Alert) => new()
    {
        SyncMode = WalSyncMode.NoSync,
        CorruptionMode = mode,
        WriterLockTimeout = TimeSpan.FromMilliseconds(100)
    };

    [Fact]
    public async Task InitializeAsync_DirectoryHeldByLiveWriter_ThrowsAndLeavesTheWriterWorking()
    {
        await using var first = new WriteAheadLog(TestDataRoot, Options());
        await first.InitializeAsync();
        await first.AppendAsync(new { Index = 1 }, "trade");

        await using var second = new WriteAheadLog(TestDataRoot, Options());
        var act = () => second.InitializeAsync();

        (await act.Should().ThrowAsync<WalDirectoryInUseException>())
            .Which.Should().BeAssignableTo<IOException>();
        Directory.GetFiles(TestDataRoot, "*.wal").Should().ContainSingle(
            "the refused writer must not have started a segment of its own");

        await first.AppendAsync(new { Index = 2 }, "trade");
        await first.FlushAsync();
        (await CountUncommittedAsync(first)).Should().Be(2);
    }

    [Fact]
    public async Task InitializeAsync_AfterPreviousWriterDisposed_Succeeds()
    {
        await using (var first = new WriteAheadLog(TestDataRoot, Options()))
        {
            await first.InitializeAsync();
            await first.AppendAsync(new { Index = 1 }, "trade");
        }

        await using var second = new WriteAheadLog(TestDataRoot, Options());
        await second.InitializeAsync();

        (await CountUncommittedAsync(second)).Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_FailsInHaltMode_ReleasesTheDirectory()
    {
        await SeedClosedSegmentWithGarbageAsync();

        await using (var halted = new WriteAheadLog(TestDataRoot, Options(WalCorruptionMode.Halt)))
        {
            var act = () => halted.InitializeAsync();
            await act.Should().ThrowAsync<InvalidDataException>();

            // Still undisposed: the failed start-up alone must have released the directory,
            // otherwise the repair the error message asks for could never run.
            await using var repair = new WriteAheadLog(TestDataRoot, Options());
            var result = await repair.RepairAsync();
            result.CorruptedRecords.Should().Be(1);
        }
    }

    [Fact]
    public async Task RepairAsync_UninitializedInstanceWhileWriterIsLive_ThrowsAndChangesNothing()
    {
        await SeedClosedSegmentWithGarbageAsync();
        await using var writer = new WriteAheadLog(TestDataRoot, Options());
        await writer.InitializeAsync();
        var before = Snapshot();

        await using var repair = new WriteAheadLog(TestDataRoot, Options());
        var act = () => repair.RepairAsync();

        await act.Should().ThrowAsync<WalDirectoryInUseException>();
        Snapshot().Should().BeEquivalentTo(before);
        Directory.Exists(Path.Combine(TestDataRoot, "quarantine")).Should().BeFalse();
    }

    [Fact]
    public async Task RepairAsync_DryRunWhileWriterIsLive_ScansWithoutTakingTheDirectory()
    {
        await SeedClosedSegmentWithGarbageAsync();
        await using var writer = new WriteAheadLog(TestDataRoot, Options());
        await writer.InitializeAsync();
        var before = Snapshot();

        await using var scanner = new WriteAheadLog(TestDataRoot, Options());
        var result = await scanner.RepairAsync(dryRun: true);

        result.CorruptedRecords.Should().Be(1);
        result.RepairedFiles.Should().Be(0);
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task RepairAsync_UninitializedInstance_ReleasesTheDirectoryAfterwards()
    {
        await SeedClosedSegmentWithGarbageAsync();

        await using (var repair = new WriteAheadLog(TestDataRoot, Options()))
        {
            await repair.RepairAsync();

            await using var writer = new WriteAheadLog(TestDataRoot, Options());
            await writer.InitializeAsync();
        }
    }

    [Fact]
    public async Task GetUncommittedRecordsAsync_UninitializedReaderBesideLiveWriter_StillReads()
    {
        await using var writer = new WriteAheadLog(TestDataRoot, Options());
        await writer.InitializeAsync();
        await writer.AppendAsync(new { Index = 1 }, "trade");
        await writer.FlushAsync();

        await using var reader = new WriteAheadLog(TestDataRoot, Options());

        (await CountUncommittedAsync(reader)).Should().Be(1);
    }

    private static async Task<int> CountUncommittedAsync(WriteAheadLog wal)
    {
        var count = 0;
        await foreach (var _ in wal.GetUncommittedRecordsAsync())
            count++;
        return count;
    }

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(TestDataRoot, "*.wal").ToDictionary(
            path => Path.GetFileName(path),
            path =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            });

    // Two valid records and one complete garbage line in a segment that is then closed.
    private async Task SeedClosedSegmentWithGarbageAsync()
    {
        await using (var seed = new WriteAheadLog(TestDataRoot, Options()))
        {
            await seed.InitializeAsync();
            await seed.AppendAsync(new { Index = 1 }, "trade");
            await seed.AppendAsync(new { Index = 2 }, "trade");
            await seed.FlushAsync();
        }

        var segment = Directory.GetFiles(TestDataRoot, "*.wal").Single();
        await File.AppendAllTextAsync(segment, "GARBAGE|not-a-timestamp|bad|bad|{}\n");
    }
}
