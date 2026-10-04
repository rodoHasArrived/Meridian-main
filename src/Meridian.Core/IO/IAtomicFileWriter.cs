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

    Task SyncDirectoryAsync(string directory, CancellationToken ct = default);
}
