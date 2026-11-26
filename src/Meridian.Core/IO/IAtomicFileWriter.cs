namespace Meridian.Core.IO;

/// <summary>
/// Durable file replacement and copy-on-write append operations supplied by the host.
/// Implementations commit complete content atomically and preserve existing cancellation
/// and directory durability guarantees once the replacement is committed.
/// </summary>
public interface IAtomicFileWriter
{
    void Write(string destinationPath, string content, CancellationToken ct = default);

    Task WriteAsync(string destinationPath, string content, CancellationToken ct = default);

    Task WriteAsync(string destinationPath, byte[] content, CancellationToken ct = default);

    Task AppendLinesAsync(string destinationPath, IEnumerable<string> lines, CancellationToken ct = default);

    /// <summary>
    /// Best-effort directory fsync for an operation that has already committed: a failure is
    /// recorded but not thrown, so a committed write is never reported as failed.
    /// </summary>
    Task SyncDirectoryAsync(string directory, CancellationToken ct = default);

    /// <summary>
    /// Directory fsync that throws when durability cannot be confirmed. Use before an
    /// acknowledgement or before removing the only other copy, where a retry is idempotent.
    /// </summary>
    Task SyncDirectoryOrThrowAsync(string directory, CancellationToken ct = default);
}
