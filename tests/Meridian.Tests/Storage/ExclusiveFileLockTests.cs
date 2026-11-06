using FluentAssertions;
using Meridian.Core.IO;

namespace Meridian.Tests.Storage;

/// <summary>
/// Pins the shared cross-process lock-file contract used by ConfigStore, the login session store,
/// the Alpaca trade-update cursor store, and the workstation's CrossProcessFileLock: only genuine
/// contention is retried (with jittered backoff), a wedged holder surfaces as a timeout carrying
/// the refusal, and the async form honours caller cancellation.
/// </summary>
public sealed class ExclusiveFileLockTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-exclusive-lock-").FullName;

    private string LockPath => Path.Combine(_root, "nested", "store.lock");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }

    [Fact]
    public async Task AcquireAsync_CreatesDirectoryAndReleasesOnDispose()
    {
        await using (await ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromSeconds(5)))
        {
            File.Exists(LockPath).Should().BeTrue();
        }

        await using var again = await ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromSeconds(1));
        File.Exists(LockPath).Should().BeTrue("the lock file is retained, never deleted");
    }

    [Fact]
    public async Task AcquireAsync_WaitsForHolderToRelease()
    {
        var holder = await ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromSeconds(5));
        var waiter = ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromSeconds(10));

        await Task.Delay(50);
        waiter.IsCompleted.Should().BeFalse("the holder still owns the lock");
        await holder.DisposeAsync();

        await using var acquired = await waiter;
        acquired.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_ContentionOutlastingTimeout_ThrowsTimeoutWithRefusal()
    {
        await using var holder = await ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromSeconds(5));

        var acquire = () => ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromMilliseconds(100));

        (await acquire.Should().ThrowAsync<TimeoutException>())
            .Which.InnerException.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task AcquireAsync_CallerCancellation_SurfacesAsCancellation()
    {
        await using var holder = await ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var acquire = () => ExclusiveFileLock.AcquireAsync(LockPath, TimeSpan.FromSeconds(30), cts.Token);

        await acquire.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Acquire_ContentionOutlastingTimeout_ThrowsTimeoutWithRefusal()
    {
        using var holder = ExclusiveFileLock.Acquire(LockPath, TimeSpan.FromSeconds(5));

        var acquire = () => ExclusiveFileLock.Acquire(LockPath, TimeSpan.FromMilliseconds(100));

        acquire.Should().Throw<TimeoutException>()
            .Which.InnerException.Should().BeOfType<IOException>();
    }

    [Fact]
    public void Acquire_StorageFault_FailsFastWithoutRetrying()
    {
        // The lock path is an existing directory: opening it is a storage fault, not contention,
        // so it must surface immediately rather than poll for the whole timeout window.
        Directory.CreateDirectory(LockPath);
        var started = DateTime.UtcNow;

        var acquire = () => ExclusiveFileLock.Acquire(LockPath, TimeSpan.FromSeconds(30));

        acquire.Should().Throw<Exception>().Which.Should().NotBeOfType<TimeoutException>();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void IsContention_DefaultIOException_IsStorageFault()
        => ExclusiveFileLock.IsContention(new IOException("disk gone")).Should().BeFalse();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void RetryBackoff_StaysWithinJitteredExponentialBounds(int attempt)
    {
        var nominalMs = Math.Min(
            RetryBackoff.MaxDelay.TotalMilliseconds,
            RetryBackoff.InitialDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 30)));

        for (var sample = 0; sample < 100; sample++)
        {
            var delay = RetryBackoff.Delay(attempt).TotalMilliseconds;
            delay.Should().BeGreaterThanOrEqualTo(Math.Max(1, nominalMs * 0.5) - 0.001);
            delay.Should().BeLessThanOrEqualTo(nominalMs + 0.001);
        }
    }

    [Fact]
    public void RetryBackoff_RejectsNonPositiveAttempt()
        => ((Action)(() => RetryBackoff.Delay(0))).Should().Throw<ArgumentOutOfRangeException>();
}
