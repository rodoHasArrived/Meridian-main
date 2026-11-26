using FluentAssertions;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// WAL steps that acknowledge records or delete the only other copy use the strict directory
/// sync, so an unconfirmed directory entry fails that step instead of passing silently.
/// </summary>
public sealed class WriteAheadLogDirectorySyncTests : IDisposable
{
    private readonly string _walDir = Path.Combine(Path.GetTempPath(), $"meridian-wal-dirsync-{Guid.NewGuid():N}");

    public void Dispose()
    {
        AtomicFileWriter.DirectorySyncFaultInjector.Value = null;
        try
        {
            Directory.Delete(_walDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task InitializeAsync_SegmentDirectoryCannotBeSynced_FailsInsteadOfAcceptingWrites()
    {
        AtomicFileWriter.DirectorySyncFaultInjector.Value = dir => dir == _walDir ? ("fsync", 5) : null;
        await using (var wal = new WriteAheadLog(_walDir))
        {
            var initialize = () => wal.InitializeAsync();
            await initialize.Should().ThrowAsync<DirectorySyncException>();
        }

        AtomicFileWriter.DirectorySyncFaultInjector.Value = null;
        await using var recovered = new WriteAheadLog(_walDir);
        await recovered.InitializeAsync();
        (await recovered.AppendAsync("after", "Trade")).Sequence.Should().Be(1);
    }

    [Fact]
    public async Task Rotation_NewSegmentCannotBeSynced_FaultsAndTheRetryKeepsEveryRecord()
    {
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            MaxWalFileSizeBytes = 1, // every append rotates
        });
        await wal.InitializeAsync();
        await wal.AppendAsync("first", "Trade");

        AtomicFileWriter.DirectorySyncFaultInjector.Value = dir => dir == _walDir ? ("fsync", 5) : null;
        var append = () => wal.AppendAsync("second", "Trade");
        await append.Should().ThrowAsync<Exception>("an unconfirmed segment entry must not acknowledge records");

        AtomicFileWriter.DirectorySyncFaultInjector.Value = null;
        await wal.AppendAsync("second", "Trade");

        var payloads = new List<string>();
        await foreach (var record in wal.GetUncommittedRecordsAsync())
            payloads.Add(record.DeserializePayload<string>()!);
        payloads.Should().Equal("first", "second");
    }

    [Fact]
    public async Task TruncateAsync_ArchiveDirectoryCannotBeSynced_KeepsTheOriginalSegment()
    {
        // Every append rotates, so the committed record's segment is closed and truncatable.
        await using var wal = new WriteAheadLog(_walDir, new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            MaxWalFileSizeBytes = 1,
        });
        await wal.InitializeAsync();
        var record = await wal.AppendAsync("payload", "Trade");
        await wal.AppendAsync("later", "Trade");
        await wal.CommitAsync(record.Sequence);
        var segmentsBefore = Directory.GetFiles(_walDir, "*.wal");
        segmentsBefore.Length.Should().BeGreaterThan(1);

        var archiveDir = Path.Combine(_walDir, "archive");
        AtomicFileWriter.DirectorySyncFaultInjector.Value = dir => dir == archiveDir ? ("fsync", 5) : null;
        var truncate = () => wal.TruncateAsync(record.Sequence);
        await truncate.Should().ThrowAsync<DirectorySyncException>();

        Directory.GetFiles(_walDir, "*.wal").Should().BeEquivalentTo(segmentsBefore,
            "a segment is never deleted while its archive copy is unconfirmed");
    }
}
