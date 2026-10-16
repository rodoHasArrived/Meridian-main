using Meridian.Core.IO;

namespace Meridian.Storage.Archival;

/// <summary>Exposes the storage-owned durable writer through the shared file I/O contract.</summary>
public sealed class AtomicFileWriterAdapter : IAtomicFileWriter
{
    public void Write(string destinationPath, string content, CancellationToken ct = default) =>
        AtomicFileWriter.Write(destinationPath, content, ct);

    public Task WriteAsync(string destinationPath, string content, CancellationToken ct = default) =>
        AtomicFileWriter.WriteAsync(destinationPath, content, ct);

    public Task WriteAsync(string destinationPath, byte[] content, CancellationToken ct = default) =>
        AtomicFileWriter.WriteAsync(destinationPath, content, ct);

    public Task AppendLinesAsync(string destinationPath, IEnumerable<string> lines, CancellationToken ct = default) =>
        AtomicFileWriter.AppendLinesAsync(destinationPath, lines, ct);

    public Task SyncDirectoryAsync(string directory, CancellationToken ct = default) =>
        AtomicFileWriter.SyncDirectoryAsync(directory, ct);
}
