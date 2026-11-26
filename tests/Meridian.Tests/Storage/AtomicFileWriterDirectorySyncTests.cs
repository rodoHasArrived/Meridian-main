using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// <see cref="AtomicFileWriter.SyncDirectoryStrictAsync"/> is a durability barrier and fails
/// closed; <see cref="AtomicFileWriter.SyncDirectoryAsync"/> stays best effort. Failures are
/// injected through the async-local test seam so the behaviour is the same on every platform.
/// </summary>
public sealed class AtomicFileWriterDirectorySyncTests : TempDirectoryTestBase
{
    private const int Eio = 5;
    private const int Einval = 22;

    [Fact]
    public async Task SyncDirectoryStrictAsync_FsyncFails_ThrowsWithoutThePath()
    {
        AtomicFileWriter.DirectorySyncFault.Value = _ => Eio;

        var act = () => AtomicFileWriter.SyncDirectoryStrictAsync(TestDataRoot);

        var exception = (await act.Should().ThrowAsync<IOException>()).Which;
        exception.Message.Should().Contain("errno 5").And.NotContain(TestDataRoot);
    }

    [Fact]
    public async Task SyncDirectoryAsync_FsyncFails_DoesNotThrow()
    {
        AtomicFileWriter.DirectorySyncFault.Value = _ => Eio;

        await AtomicFileWriter.SyncDirectoryAsync(TestDataRoot);
    }

    [Fact]
    public async Task SyncDirectoryStrictAsync_FilesystemCannotFsyncDirectories_DoesNotThrow()
    {
        AtomicFileWriter.DirectorySyncFault.Value = _ => Einval;

        await AtomicFileWriter.SyncDirectoryStrictAsync(TestDataRoot);
    }

    [Fact]
    public async Task SyncDirectoryStrictAsync_HealthyDirectory_Succeeds()
    {
        await AtomicFileWriter.SyncDirectoryStrictAsync(TestDataRoot);
    }

    [Fact]
    public async Task DirectoryFsyncFailure_IsCounted()
    {
        var before = await ReadFailureCounterAsync();
        AtomicFileWriter.DirectorySyncFault.Value = _ => Eio;

        await AtomicFileWriter.SyncDirectoryAsync(TestDataRoot);

        // Other tests may fail fsyncs concurrently, so only a lower bound is exact.
        (await ReadFailureCounterAsync()).Should().BeGreaterThanOrEqualTo(before + 1);
    }

    [Fact]
    public async Task WriteAsync_PostRenameDirectoryFsyncFails_StillReportsTheCommittedWrite()
    {
        var path = Path.Combine(TestDataRoot, "state.json");
        AtomicFileWriter.DirectorySyncFault.Value = _ => Eio;

        await AtomicFileWriter.WriteAsync(path, "{}", CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("{}");
    }

    private static async Task<double> ReadFailureCounterAsync()
    {
        using var buffer = new MemoryStream();
        await Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(buffer);
        var text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        var line = text.Split('\n').FirstOrDefault(l =>
            l.StartsWith("mdc_storage_dir_fsync_failures_total ", StringComparison.Ordinal));
        return line is null
            ? 0
            : double.Parse(line.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }
}
