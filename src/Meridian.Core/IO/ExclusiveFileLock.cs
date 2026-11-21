namespace Meridian.Core.IO;

/// <summary>
/// Acquires an exclusive cross-process lock file: the open <see cref="FileShare.None"/> handle is the
/// lock, so disposing the returned stream releases it and a crashed holder releases implicitly.
/// </summary>
/// <remarks>
/// <para>Acquisition polls, because the filesystem offers no portable cross-process wait primitive.
/// Only a refusal that names another live handle (<see cref="IsContention"/>) is retried; any other
/// I/O failure is a storage fault and propagates immediately rather than being reported as slow
/// contention after the timeout. Retries back off exponentially with jitter
/// (<see cref="RetryBackoff"/>) so contending processes do not poll in lockstep.</para>
/// <para>Prefer <see cref="AcquireAsync"/> on async paths: it waits with <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// and observes cancellation. <see cref="Acquire"/> exists for synchronous APIs that must take the
/// lease while holding an in-process monitor, where awaiting is not possible.</para>
/// <para>The lock file is never deleted: deleting it could let two holders lock different inodes.</para>
/// </remarks>
public static class ExclusiveFileLock
{
    /// <summary>Opens and exclusively locks <paramref name="lockPath"/>, waiting asynchronously while another handle holds it.</summary>
    /// <exception cref="TimeoutException">Contention outlasted <paramref name="timeout"/>; the inner exception is the last refusal.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static async Task<FileStream> AcquireAsync(
        string lockPath,
        TimeSpan timeout,
        CancellationToken ct = default,
        FileOptions options = FileOptions.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        ct.ThrowIfCancellationRequested();
        EnsureDirectory(lockPath);

        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);
        expiry.CancelAfter(timeout);
        for (var attempt = 1; ; attempt++)
        {
            IOException contention;
            try
            {
                return Open(lockPath, options);
            }
            catch (IOException exception) when (IsContention(exception))
            {
                contention = exception;
            }

            try
            {
                await Task.Delay(RetryBackoff.Delay(attempt), expiry.Token).ConfigureAwait(false);
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
    /// Synchronous form of <see cref="AcquireAsync"/> for callers that must hold an in-process monitor
    /// while taking the lease. Blocks the calling thread between attempts; do not call on async paths.
    /// </summary>
    /// <exception cref="TimeoutException">Contention outlasted <paramref name="timeout"/>; the inner exception is the last refusal.</exception>
    public static FileStream Acquire(string lockPath, TimeSpan timeout, FileOptions options = FileOptions.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        EnsureDirectory(lockPath);

        var deadline = DateTime.UtcNow + timeout;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Open(lockPath, options);
            }
            catch (IOException exception) when (IsContention(exception))
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        $"Timed out after {timeout} waiting for lock file '{lockPath}'.",
                        exception);
                }

                var delay = RetryBackoff.Delay(attempt);
                Thread.Sleep(delay < remaining ? delay : remaining);
            }
        }
    }

    /// <summary>
    /// Whether a refused open reports another live handle on the file, as opposed to a storage fault.
    /// </summary>
    /// <remarks>
    /// Windows refuses a locked file with ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33) in
    /// the low word of <see cref="Exception.HResult"/>. Unix advisory locking refuses with
    /// EAGAIN/EWOULDBLOCK, surfaced with the raw errno as the HResult: 11 on Linux, 35 on macOS.
    /// </remarks>
    public static bool IsContention(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return OperatingSystem.IsWindows()
            ? (exception.HResult & 0xFFFF) is 32 or 33
            : exception.HResult is 11 or 35;
    }

    private static FileStream Open(string lockPath, FileOptions options)
        => new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, options);

    private static void EnsureDirectory(string lockPath)
    {
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}

/// <summary>Bounded exponential backoff with jitter for short local retry loops (lock files, file replacement).</summary>
public static class RetryBackoff
{
    /// <summary>Delay before the first retry.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>Upper bound on any single delay, before jitter.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Returns the delay before retry <paramref name="attempt"/> (1-based): <c>initial * 2^(attempt-1)</c>
    /// capped at <paramref name="max"/>, then scaled by a random factor in [0.5, 1.0] so concurrent
    /// waiters spread out.
    /// </summary>
    public static TimeSpan Delay(int attempt, TimeSpan? initial = null, TimeSpan? max = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        var initialMs = (initial ?? InitialDelay).TotalMilliseconds;
        var maxMs = (max ?? MaxDelay).TotalMilliseconds;
        var exponent = Math.Min(attempt - 1, 30);
        var cappedMs = Math.Min(maxMs, initialMs * Math.Pow(2, exponent));
        var jittered = cappedMs * (0.5 + (Random.Shared.NextDouble() * 0.5));
        return TimeSpan.FromMilliseconds(Math.Max(1, jittered));
    }
}
