using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl;
using Meridian.Storage.Etl;

namespace Meridian.Tests.Infrastructure.Etl;

/// <summary>
/// Guards ETL source retention. An archive or error location is one directory shared by every run
/// of a source, so a scheduled drop under a fixed name resolves to the same destination forever.
/// Retention that overwrites is retention that silently loses what it was asked to keep.
/// </summary>
public sealed class LocalFileSourceReaderPostProcessingTests : IDisposable
{
    private const string MondayContent = "symbol,qty\nAAPL,100\n";
    private const string TuesdayContent = "symbol,qty\nMSFT,250\n";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-etl-postprocess-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PostProcessFileAsync_WhenArchiveNameHoldsDifferentContent_RetainsBothSources()
    {
        var (sourceDir, archiveDir) = CreateDirectories();
        var archived = Path.Combine(archiveDir, "positions.csv");
        await File.WriteAllTextAsync(archived, MondayContent);
        var incoming = await DropAsync(sourceDir, "positions.csv", TuesdayContent);

        await ArchiveAsync(sourceDir, archiveDir, incoming);

        // Monday's archived source is untouched, and Tuesday's is retained alongside it.
        (await File.ReadAllTextAsync(archived)).Should().Be(MondayContent);
        var retained = Directory.GetFiles(archiveDir);
        retained.Should().HaveCount(2);
        var sibling = retained.Single(path => path != archived);
        (await File.ReadAllTextAsync(sibling)).Should().Be(TuesdayContent);
        Path.GetFileName(sibling).Should().StartWith("positions.sha256-").And.EndWith(".csv");
        File.Exists(incoming).Should().BeFalse("the source is consumed once it is retained");
    }

    [Fact]
    public async Task PostProcessFileAsync_WhenArchiveNameIsFree_KeepsTheOriginalName()
    {
        var (sourceDir, archiveDir) = CreateDirectories();
        var incoming = await DropAsync(sourceDir, "positions.csv", TuesdayContent);

        await ArchiveAsync(sourceDir, archiveDir, incoming);

        Directory.GetFiles(archiveDir).Should().ContainSingle()
            .Which.Should().Be(Path.Combine(archiveDir, "positions.csv"));
        File.Exists(incoming).Should().BeFalse();
    }

    [Fact]
    public async Task PostProcessFileAsync_WhenArchiveNameHoldsIdenticalContent_IsAnIdempotentReplay()
    {
        var (sourceDir, archiveDir) = CreateDirectories();
        var archived = Path.Combine(archiveDir, "positions.csv");
        await File.WriteAllTextAsync(archived, TuesdayContent);
        var incoming = await DropAsync(sourceDir, "positions.csv", TuesdayContent);

        await ArchiveAsync(sourceDir, archiveDir, incoming);

        Directory.GetFiles(archiveDir).Should().ContainSingle();
        (await File.ReadAllTextAsync(archived)).Should().Be(TuesdayContent);
        File.Exists(incoming).Should().BeFalse();
    }

    [Fact]
    public async Task PostProcessFileAsync_RepeatedForTheSameContent_DoesNotAccumulateCopies()
    {
        var (sourceDir, archiveDir) = CreateDirectories();
        await File.WriteAllTextAsync(Path.Combine(archiveDir, "positions.csv"), MondayContent);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var incoming = await DropAsync(sourceDir, "positions.csv", TuesdayContent);
            await ArchiveAsync(sourceDir, archiveDir, incoming);
        }

        // Deterministic naming means the retry resolves onto the same sibling every time.
        Directory.GetFiles(archiveDir).Should().HaveCount(2);
    }

    private (string SourceDir, string ArchiveDir) CreateDirectories()
    {
        var sourceDir = Path.Combine(_root, "drop");
        var archiveDir = Path.Combine(_root, "archive");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(archiveDir);
        return (sourceDir, archiveDir);
    }

    private static async Task<string> DropAsync(string sourceDir, string name, string content)
    {
        var path = Path.Combine(sourceDir, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private Task ArchiveAsync(string sourceDir, string archiveDir, string incoming)
    {
        var reader = new LocalFileSourceReader(new EtlStagingStore(_root));
        var source = new EtlSourceDefinition
        {
            Kind = EtlSourceKind.Local,
            Location = sourceDir,
            PostProcessingAction = EtlSourcePostProcessingAction.MoveToArchive,
            ArchiveLocation = archiveDir
        };

        return reader.PostProcessFileAsync(
            source,
            new EtlRemoteFile { Path = incoming, Name = Path.GetFileName(incoming), SizeBytes = 0 },
            succeeded: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
