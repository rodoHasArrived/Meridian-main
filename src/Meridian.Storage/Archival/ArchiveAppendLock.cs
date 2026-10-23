namespace Meridian.Storage.Archival;

/// <summary>
/// An exclusive lock file for copy-on-write appends to one published archive, shared by every
/// process that writes the same data root. <see cref="JsonlWriteFence"/> serializes appenders in
/// one process; two processes would each copy the same archive and the last replacement would
/// silently drop the other's committed member, so the whole read-copy-replace also holds this.
/// </summary>
/// <remarks>
/// <para>The lock is the open handle itself: <see cref="FileShare.None"/> refuses every other open,
/// in this process or another, so releasing is disposing the stream and a crashed holder releases
/// when the operating system closes its handles. The file is never deleted; an empty lock file
/// left beside the archive carries no state and is not a data file to any reader.</para>
/// <para>Acquisition polls, because the filesystem offers no cross-process wait primitive that
/// works on every platform, and retries only a refusal that names another live handle. Any other
/// I/O failure is a storage fault and propagates at once. Contention that outlasts the timeout
/// throws <see cref="TimeoutException"/> with the last refusal as its inner exception; the caller's
/// own cancellation surfaces as <see cref="OperationCanceledException"/>.</para>
/// </remarks>
internal static class ArchiveAppendLock
{
    public static string PathFor(string archivePath) => archivePath + ".append-lock";

    public static Task<FileStream> AcquireAsync(string archivePath, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return AcquireLockFileAsync(PathFor(archivePath), FileShare.None, timeout, ct);
    }

    // Opens a lock file with the given share mode, polling while another live handle refuses it.
    // FileShare.None is exclusive; FileShare.ReadWrite admits other shared holders and refuses an
    // exclusive one, in this process or another (Windows share modes; Unix flock LOCK_SH/LOCK_EX).
    internal static async Task<FileStream> AcquireLockFileAsync(string lockPath, FileShare share, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);
        expiry.CancelAfter(timeout);
        while (true)
        {
            IOException contention;
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, share,
                    bufferSize: 1, options: FileOptions.Asynchronous | FileOptions.WriteThrough);
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
                throw new TimeoutException($"Timed out after {timeout} waiting for lock '{lockPath}'.", contention);
            }
        }
    }

    // Windows refuses a locked file with ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33)
    // in the low word of HResult; Unix advisory locking refuses with EAGAIN/EWOULDBLOCK, surfaced
    // with the raw errno as HResult (11 on Linux, 35 on macOS).
    internal static bool IsContention(IOException exception)
        => OperatingSystem.IsWindows()
            ? (exception.HResult & 0xFFFF) is 32 or 33
            : exception.HResult is 11 or 35;
}
