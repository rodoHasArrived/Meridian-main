namespace Meridian.Storage.Archival;

/// <summary>
/// The cross-process single-writer lock for one WAL directory. A <see cref="WriteAheadLog"/>
/// holds it from <see cref="WriteAheadLog.InitializeAsync"/> until disposal, and a repair holds it
/// while it rewrites segments, so no second writer or repair can commit, truncate or rewrite the
/// segments a live writer owns.
/// </summary>
/// <remarks>
/// The lock is the open handle on <see cref="PathFor"/>: <see cref="FileShare.None"/> refuses every
/// other open, in this process or another, and a crashed holder releases when the operating system
/// closes its handles. The file is never deleted and carries no state; its name does not match the
/// <c>*.wal</c> segment listings. Readers that only enumerate records never take it.
/// </remarks>
internal static class WalDirectoryLock
{
    public const string FileName = "writer.lock";

    public static string PathFor(string walDirectory) => Path.Combine(walDirectory, FileName);

    /// <summary>
    /// Takes the directory for writing, waiting up to <paramref name="timeout"/> for a previous
    /// holder that is still shutting down.
    /// </summary>
    /// <exception cref="WalDirectoryInUseException">Another writer or repair still holds it.</exception>
    public static async Task<FileStream> AcquireAsync(string walDirectory, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walDirectory);
        try
        {
            return await ArchiveAppendLock
                .AcquireLockFileAsync(PathFor(walDirectory), FileShare.None, timeout, ct)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new WalDirectoryInUseException(walDirectory, exception);
        }
    }

    /// <summary>Takes the directory without waiting.</summary>
    /// <exception cref="WalDirectoryInUseException">Another writer or repair holds it.</exception>
    public static FileStream Acquire(string walDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walDirectory);
        try
        {
            return new FileStream(PathFor(walDirectory), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 1, options: FileOptions.None);
        }
        catch (IOException exception) when (ArchiveAppendLock.IsContention(exception))
        {
            throw new WalDirectoryInUseException(walDirectory, exception);
        }
    }
}

/// <summary>
/// Thrown when a WAL directory is already held by another writer or by a running repair.
/// </summary>
public sealed class WalDirectoryInUseException : IOException
{
    public WalDirectoryInUseException(string walDirectory, Exception? innerException = null)
        : base(
            $"WAL directory '{walDirectory}' is in use by another writer or repair. " +
            "Only one process may write, truncate or repair a WAL directory at a time.",
            innerException)
    {
        WalDirectory = walDirectory;
    }

    public string WalDirectory { get; }
}
