using FluentAssertions;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// A WAL directory has exactly one live owner. A second instance on the same directory used to
/// treat the first one's live segment as closed, archive it away and reuse its sequence numbers,
/// so flushed-but-uncommitted records vanished from recovery without any error.
/// </summary>
public sealed class WriteAheadLogOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-wal-owner-{Guid.NewGuid():N}");

    public WriteAheadLogOwnershipTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task SecondInstance_OnAnOwnedDirectory_IsRefusedAndTheOwnersRecordsSurvive()
    {
        var walDir = Path.Combine(_root, "wal");
        var owner = new WriteAheadLog(walDir);
        await owner.InitializeAsync();
        for (var i = 1; i <= 4; i++)
            await owner.AppendAsync($"payload-{i}", "Trade");
        await owner.FlushAsync();

        await using (var intruder = new WriteAheadLog(walDir))
        {
            var initialize = () => intruder.InitializeAsync();
            (await initialize.Should().ThrowAsync<WalDirectoryInUseException>())
                .Which.WalDirectory.Should().Be(walDir);
        }

        // The owner "crashes" without committing; recovery must still see every flushed record.
        await owner.DisposeAsync();
        await using var recovered = new WriteAheadLog(walDir);
        await recovered.InitializeAsync();
        var uncommitted = new List<string>();
        await foreach (var record in recovered.GetUncommittedRecordsAsync())
            uncommitted.Add(record.DeserializePayload<string>()!);

        uncommitted.Should().Equal("payload-1", "payload-2", "payload-3", "payload-4");
        Directory.GetFiles(walDir, "*.wal").Should().NotBeEmpty();
    }

    [Fact]
    public async Task DisposingTheOwner_ReleasesTheDirectory()
    {
        var walDir = Path.Combine(_root, "wal");
        await using (var first = new WriteAheadLog(walDir))
        {
            await first.InitializeAsync();
        }

        await using var second = new WriteAheadLog(walDir);
        await second.InitializeAsync();
        (await second.AppendAsync("after", "Trade")).Sequence.Should().Be(1);
    }

    [Fact]
    public async Task AnInstanceThatNeverInitializes_DoesNotClaimTheDirectory()
    {
        // Hosts compose WAL singletons they never initialize (for example a UI graph); those
        // must not block the instance that actually runs the pipeline.
        var walDir = Path.Combine(_root, "wal");
        await using var composedOnly = new WriteAheadLog(walDir);
        await using var running = new WriteAheadLog(walDir);

        await running.InitializeAsync();
        (await running.AppendAsync("x", "Trade")).Sequence.Should().Be(1);
    }

    [Fact]
    public async Task RepairAsync_OnAnOwnedDirectory_IsRefusedAndChangesNoFiles()
    {
        var walDir = Path.Combine(_root, "wal");
        await using var owner = new WriteAheadLog(walDir);
        await owner.InitializeAsync();
        await owner.AppendAsync("payload", "Trade");
        await owner.FlushAsync();
        var before = SnapshotSegments(walDir);

        await using var repairer = new WriteAheadLog(walDir);
        var repair = () => repairer.RepairAsync();

        await repair.Should().ThrowAsync<WalDirectoryInUseException>();
        SnapshotSegments(walDir).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task SeparateDirectories_RunSideBySide()
    {
        await using var collector = new WriteAheadLog(Path.Combine(_root, "_wal", "collector"));
        await using var backfill = new WriteAheadLog(Path.Combine(_root, "_wal", "backfill"));

        await collector.InitializeAsync();
        await backfill.InitializeAsync();

        (await collector.AppendAsync("live", "Trade")).Sequence.Should().Be(1);
        (await backfill.AppendAsync("historical", "Bar")).Sequence.Should().Be(1);
    }

    [Fact]
    public async Task LegacySegments_AreAdoptedIntoTheOwnedDirectoryAndRecovered()
    {
        var legacyDir = Path.Combine(_root, "_wal");
        await using (var legacy = new WriteAheadLog(legacyDir))
        {
            await legacy.InitializeAsync();
            await legacy.AppendAsync("left-behind-1", "Trade");
            await legacy.AppendAsync("left-behind-2", "Trade");
            await legacy.FlushAsync();
        }

        var collectorDir = Path.Combine(legacyDir, "collector");
        await using var collector = new WriteAheadLog(collectorDir, new WalOptions { LegacySegmentDirectory = legacyDir });
        await collector.InitializeAsync();

        Directory.GetFiles(legacyDir, "*.wal").Should().BeEmpty("every legacy segment moved into the workload directory");
        var uncommitted = new List<string>();
        await foreach (var record in collector.GetUncommittedRecordsAsync())
            uncommitted.Add(record.DeserializePayload<string>()!);
        uncommitted.Should().Equal("left-behind-1", "left-behind-2");
        (await collector.AppendAsync("next", "Trade")).Sequence.Should().Be(3,
            "adopted records keep their sequences, so new ones continue after them");
    }

    [Fact]
    public async Task LegacyAdoption_WhileTheLegacyDirectoryIsOwned_IsRefusedAndMovesNothing()
    {
        var legacyDir = Path.Combine(_root, "_wal");
        await using var legacyOwner = new WriteAheadLog(legacyDir);
        await legacyOwner.InitializeAsync();
        await legacyOwner.AppendAsync("live", "Trade");
        await legacyOwner.FlushAsync();
        var before = SnapshotSegments(legacyDir);

        await using var collector = new WriteAheadLog(
            Path.Combine(legacyDir, "collector"), new WalOptions { LegacySegmentDirectory = legacyDir });
        var initialize = () => collector.InitializeAsync();

        (await initialize.Should().ThrowAsync<WalDirectoryInUseException>())
            .Which.WalDirectory.Should().Be(legacyDir);
        SnapshotSegments(legacyDir).Should().BeEquivalentTo(before);
    }

    private static Dictionary<string, long> SnapshotSegments(string directory)
        => Directory.GetFiles(directory, "*.wal").ToDictionary(static f => Path.GetFileName(f)!, static f => new FileInfo(f).Length);
}
