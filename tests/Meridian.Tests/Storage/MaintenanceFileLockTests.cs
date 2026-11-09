using FluentAssertions;
using Meridian.Storage.Maintenance;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// The maintenance stores take their cross-process lock from constructors with no cancellation
/// token. Acquisition used to retry every <see cref="IOException"/> forever, so a held or
/// unwritable lock file hung startup with no error.
/// </summary>
public sealed class MaintenanceFileLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-maint-lock-{Guid.NewGuid():N}");

    public MaintenanceFileLockTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AcquireAsync_LockHeldElsewhere_TimesOutWithLockPathInsteadOfHanging()
    {
        var lockPath = Path.Combine(_root, "schedules.json.lock");
        await using var holder = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var act = () => MaintenanceFileLock.AcquireAsync(
            lockPath, "Maintenance schedule", CancellationToken.None, TimeSpan.FromMilliseconds(200));

        var thrown = await act.Should().ThrowAsync<TimeoutException>();
        thrown.Which.Message.Should().Contain(lockPath);
        thrown.Which.InnerException.Should().BeAssignableTo<IOException>();
    }

    [Fact]
    public async Task AcquireAsync_LockFree_ReturnsExclusiveStream()
    {
        var lockPath = Path.Combine(_root, "history.json.lock");

        await using var stream = await MaintenanceFileLock.AcquireAsync(
            lockPath, "Maintenance history", CancellationToken.None, TimeSpan.FromMilliseconds(200));

        stream.CanWrite.Should().BeTrue();
    }
}
