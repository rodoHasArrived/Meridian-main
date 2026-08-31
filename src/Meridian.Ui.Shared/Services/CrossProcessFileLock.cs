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
/// works across processes and platforms. The timeout bounds how long a caller can be starved by a
/// wedged holder; on expiry the linked token cancels and the acquisition throws
/// <see cref="OperationCanceledException"/> rather than proceeding unserialized.</para>
/// </remarks>
internal static class CrossProcessFileLock
{
    public static async Task<FileStream> AcquireAsync(
        string lockPath,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);

        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);
        expiry.CancelAfter(timeout);
        while (true)
        {
            expiry.Token.ThrowIfCancellationRequested();
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
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), expiry.Token).ConfigureAwait(false);
            }
        }
    }
}
