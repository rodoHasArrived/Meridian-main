using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// The WAL does not build on a directory entry it could not make durable: a segment whose entry
/// failed to sync is not written to, and a segment is not deleted on the strength of an archive
/// whose entry failed to sync.
/// </summary>
public sealed class WriteAheadLogDirectorySyncTests : TempDirectoryAsyncTestBase
{
    private const int Eio = 5;

    [Fact]
    public async Task InitializeAsync_SegmentEntryCannotBeMadeDurable_Throws()
    {
        AtomicFileWriter.DirectorySyncFault.Value = _ => Eio;
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.EveryWrite });

        var act = () => wal.InitializeAsync();

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task Rotation_SegmentEntryCannotBeMadeDurable_FailsTheAppendThenRecoversEveryRecord()
    {
        var failing = false;
        AtomicFileWriter.DirectorySyncFault.Value = _ => failing ? Eio : null;
        await using (var wal = new WriteAheadLog(TestDataRoot, new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            MaxWalFileSizeBytes = 1
        }))
        {
            await wal.InitializeAsync();
            await wal.AppendAsync(new { Index = 1 }, "trade");

            failing = true;
            var act = () => wal.AppendAsync(new { Index = 2 }, "trade");
            await act.Should().ThrowAsync<IOException>();

            // The disk recovers: the writer abandons the untrusted segment and carries on.
            failing = false;
            await wal.AppendAsync(new { Index = 3 }, "trade");
            await wal.FlushAsync();
        }

        await using var recovered = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await recovered.InitializeAsync();
        var sequences = new List<long>();
        await foreach (var record in recovered.GetUncommittedRecordsAsync())
            sequences.Add(record.Sequence);

        sequences.Should().OnlyHaveUniqueItems();
        sequences.Should().Contain(1, "the record acknowledged before the failure must survive");
        sequences.Should().HaveCountGreaterThanOrEqualTo(2, "the record appended after recovery must survive too");
    }

    [Fact]
    public async Task TruncateAsync_ArchiveEntryCannotBeMadeDurable_KeepsTheWalSegment()
    {
        var archiveDirectory = Path.Combine(TestDataRoot, "archive");
        var failing = false;
        AtomicFileWriter.DirectorySyncFault.Value = directory =>
            failing && string.Equals(directory, archiveDirectory, StringComparison.Ordinal) ? Eio : null;
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            MaxWalFileSizeBytes = 1,
            ArchiveAfterTruncate = true
        });
        await wal.InitializeAsync();
        var first = await wal.AppendAsync(new { Index = 1 }, "trade");
        await wal.AppendAsync(new { Index = 2 }, "trade");
        await wal.FlushAsync();
        await wal.CommitAsync(first.Sequence);
        var segmentsBefore = Directory.GetFiles(TestDataRoot, "*.wal").Length;

        failing = true;
        await wal.TruncateAsync(first.Sequence);

        Directory.GetFiles(TestDataRoot, "*.wal").Should().HaveCount(segmentsBefore,
            "a segment must not be deleted while its archive may not survive power loss");

        failing = false;
        await wal.TruncateAsync(first.Sequence);

        Directory.GetFiles(TestDataRoot, "*.wal").Length.Should().BeLessThan(segmentsBefore,
            "once the archive can be made durable the committed segment is removed");
    }
}
