using FluentAssertions;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// A failed directory fsync used to log a Warning and return as if it succeeded. The best-effort
/// path now counts and logs it at Error; the strict path throws so callers can fail closed.
/// </summary>
public sealed class AtomicFileWriterDirectorySyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-dirsync-{Guid.NewGuid():N}");

    public AtomicFileWriterDirectorySyncTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        AtomicFileWriter.DirectorySyncFaultInjector.Value = null;
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
    public async Task SyncDirectoryOrThrowAsync_FsyncFailure_ThrowsWithTheFailedOperation()
    {
        AtomicFileWriter.DirectorySyncFaultInjector.Value = dir => dir == _root ? ("fsync", 5) : null;
        var before = AtomicFileWriter.DirectorySyncFailureCount;

        var sync = () => AtomicFileWriter.SyncDirectoryOrThrowAsync(_root);

        var failure = (await sync.Should().ThrowAsync<DirectorySyncException>()).Which;
        failure.Operation.Should().Be("fsync");
        failure.Errno.Should().Be(5);
        failure.Directory.Should().Be(_root);
        AtomicFileWriter.DirectorySyncFailureCount.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task SyncDirectoryAsync_Failure_IsCountedButNotThrown()
    {
        AtomicFileWriter.DirectorySyncFaultInjector.Value = dir => dir == _root ? ("open", 24) : null;
        var before = AtomicFileWriter.DirectorySyncFailureCount;

        await AtomicFileWriter.SyncDirectoryAsync(_root);

        AtomicFileWriter.DirectorySyncFailureCount.Should().BeGreaterThan(before,
            "a best-effort sync that fails must still be visible to monitoring");
    }

    [Fact]
    public async Task WriteAsync_DirectorySyncFailsAfterTheRename_KeepsTheCommittedWrite()
    {
        // The rename has already published the file, so the write must not be reported failed
        // (a retry of an append would duplicate it); the failure is counted instead.
        var path = Path.Combine(_root, "state.json");
        AtomicFileWriter.DirectorySyncFaultInjector.Value = dir => dir == _root ? ("fsync", 5) : null;
        var before = AtomicFileWriter.DirectorySyncFailureCount;

        await AtomicFileWriter.WriteAsync(path, "{\"v\":1}");

        (await File.ReadAllTextAsync(path)).Should().Be("{\"v\":1}");
        AtomicFileWriter.DirectorySyncFailureCount.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task SyncDirectoryOrThrowAsync_FilesystemWithoutDirectoryFsync_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // procfs refuses directory fsync with EINVAL, like several FUSE/9p mounts: the store must
        // stay usable there (PostgreSQL makes the same call); the refusal is logged and counted.
        var link = Path.Combine(_root, "proc-link");
        Directory.CreateSymbolicLink(link, "/proc/self");
        try
        {
            var sync = () => AtomicFileWriter.SyncDirectoryOrThrowAsync(link);
            await sync.Should().NotThrowAsync("an unsupported directory fsync is not a durability failure of this store");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task SyncDirectoryOrThrowAsync_Healthy_Succeeds()
    {
        await AtomicFileWriter.SyncDirectoryOrThrowAsync(_root);
    }
}
