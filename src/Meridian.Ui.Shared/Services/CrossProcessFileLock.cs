using Meridian.Core.IO;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Acquires an exclusive lock file shared by every process that composes over the same data root.
/// </summary>
/// <remarks>
/// <para>The lock is the open handle itself — <see cref="FileShare.None"/> refuses every other open,
/// in this process or another — so releasing is disposing the stream, and a crashed holder releases
/// implicitly when the operating system closes its handles. The file is never deleted; an empty
/// lock file left behind carries no state.</para>
/// <para>Acquisition polls rather than blocks because the filesystem offers no wait primitive that
/// works across processes and platforms — but only a refusal that names another live handle is
/// retried (see <see cref="IsContention"/>), with jittered exponential backoff. Any other I/O failure is a storage fault and
/// propagates immediately: polling it for the timeout window would misreport an outage as slow
/// contention and hide the actionable exception. When contention outlasts the timeout, the
/// acquisition throws <see cref="TimeoutException"/> carrying the last refusal as its inner
/// exception, so a wedged holder reads as what it is rather than as cancellation; a caller's own
/// cancellation still surfaces as <see cref="OperationCanceledException"/>.</para>
/// </remarks>
internal static class CrossProcessFileLock
{
    public static Task<FileStream> AcquireAsync(
        string lockPath,
        TimeSpan timeout,
        CancellationToken ct)
        => ExclusiveFileLock.AcquireAsync(
            lockPath,
            timeout,
            ct,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

    /// <summary>
    /// Whether a refused open reports another live handle on the lock file, as opposed to a
    /// storage fault (descriptor exhaustion, a removed data directory, a failing remote mount).
    /// See <see cref="ExclusiveFileLock.IsContention"/> for the platform mapping.
    /// </summary>
    internal static bool IsContention(IOException exception)
        => ExclusiveFileLock.IsContention(exception);
}
