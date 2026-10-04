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
/// retried (see <see cref="IsContention"/>). Any other I/O failure is a storage fault and
/// propagates immediately: polling it for the timeout window would misreport an outage as slow
/// contention and hide the actionable exception. When contention outlasts the timeout, the
/// acquisition throws <see cref="TimeoutException"/> carrying the last refusal as its inner
/// exception, so a wedged holder reads as what it is rather than as cancellation; a caller's own
/// cancellation still surfaces as <see cref="OperationCanceledException"/>.</para>
/// </remarks>
internal static class CrossProcessFileLock
{
    public static async Task<FileStream> AcquireAsync(
        string lockPath,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        ct.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);
        expiry.CancelAfter(timeout);
        while (true)
        {
            IOException contention;
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException exception) when (IsContention(exception))
            {
                contention = exception;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), expiry.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    $"Timed out after {timeout} waiting for lock file '{lockPath}'.",
                    contention);
            }
        }
    }

    /// <summary>
    /// Whether a refused open reports another live handle on the lock file, as opposed to a
    /// storage fault (descriptor exhaustion, a removed data directory, a failing remote mount).
    /// </summary>
    /// <remarks>
    /// Windows refuses a locked file with ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION
    /// (33) in the low word of <see cref="Exception.HResult"/>. Unix advisory locking refuses
    /// with EAGAIN/EWOULDBLOCK, and the runtime surfaces that as its sharing-violation
    /// <see cref="IOException"/> carrying the raw errno as the HResult — 11 on Linux (verified
    /// against the real refusal on this repo's CI platform), 35 on macOS. Branching on the
    /// platform keeps the small integers from colliding across error spaces.
    /// </remarks>
    internal static bool IsContention(IOException exception)
        => OperatingSystem.IsWindows()
            ? (exception.HResult & 0xFFFF) is 32 or 33
            : exception.HResult is 11 or 35;
}
