using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Codex review finding on PR #2884: the lock acquisition retried every <see cref="IOException"/>
/// as contention, so a genuine storage fault polled for the whole timeout window and then
/// surfaced as <see cref="OperationCanceledException"/> — an outage misreported as cancellation,
/// with the actionable error hidden. These tests pin the corrected contract: only a refusal that
/// names another live handle is retried, a timed-out wait names itself and carries the refusal,
/// and the caller's own cancellation stays cancellation.
/// </summary>
public sealed class CrossProcessFileLockTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-lock-").FullName;

    private string LockPath => Path.Combine(_root, "store.lock");

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
    public async Task TheRealContentionRefusal_IsClassifiedAsContention()
    {
        // Pins the platform mapping against the genuine exception rather than a synthesized one:
        // on Unix the runtime reports the advisory-lock refusal with the raw errno as HResult,
        // and if a runtime change moved it, retry-on-contention would silently become
        // fail-on-contention — the two-instance serialization tests would catch that loudly, and
        // this test names why.
        await using var holder = await CrossProcessFileLock.AcquireAsync(
            LockPath, TimeSpan.FromSeconds(5), CancellationToken.None);

        IOException? refusal = null;
        try
        {
            new FileStream(
                LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose();
        }
        catch (IOException exception)
        {
            refusal = exception;
        }

        refusal.Should().NotBeNull("a second open of a FileShare.None handle must be refused");
        CrossProcessFileLock.IsContention(refusal!).Should().BeTrue(
            "the platform's real contention refusal (HResult 0x{0:X8}) must be retried",
            refusal!.HResult);
    }

    [Fact]
    public void AStorageFault_IsNotClassifiedAsContention()
    {
        // A default IOException carries COR_E_IO, the shape of descriptor exhaustion, a removed
        // data directory, or a failing remote mount. Retrying it for the timeout window would
        // misreport the outage as slow contention.
        CrossProcessFileLock.IsContention(new IOException("disk gone")).Should().BeFalse();
    }

    [Fact]
    public async Task AWaitThatOutlastsTheTimeout_NamesItselfAndCarriesTheRefusal()
    {
        await using var holder = await CrossProcessFileLock.AcquireAsync(
            LockPath, TimeSpan.FromSeconds(5), CancellationToken.None);

        var acquire = async () => await CrossProcessFileLock.AcquireAsync(
            LockPath, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        (await acquire.Should().ThrowAsync<TimeoutException>(
                "a wedged holder is a timeout, not a cancellation"))
            .Which.InnerException.Should().BeOfType<IOException>(
                "the last refusal is the diagnostic the operator needs");
    }

    [Fact]
    public async Task TheCallersOwnCancellation_StaysCancellation()
    {
        await using var holder = await CrossProcessFileLock.AcquireAsync(
            LockPath, TimeSpan.FromSeconds(5), CancellationToken.None);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var acquire = async () => await CrossProcessFileLock.AcquireAsync(
            LockPath, TimeSpan.FromSeconds(30), cancellation.Token);

        await acquire.Should().ThrowAsync<OperationCanceledException>();
    }
}
