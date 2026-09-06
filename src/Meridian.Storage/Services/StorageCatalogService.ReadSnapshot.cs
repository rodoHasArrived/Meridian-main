namespace Meridian.Storage.Services;

public sealed partial class StorageCatalogService
{
    private static FileStream OpenCatalogRead(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void EnsureReadSnapshotUnchanged(string path, long length, DateTime modified)
    {
        var current = new FileInfo(path);
        if (!current.Exists || current.Length != length || current.LastWriteTimeUtc != modified)
            throw new IOException($"Catalog input changed while scanning '{path}'; the previous catalog was retained.");
    }

    /// <summary>Bounds a scan to its captured byte count even when an append handle stays open.</summary>
    private sealed class CatalogReadWindow(Stream inner, long length) : Stream
    {
        private long _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => length - _remaining; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining == 0 || count == 0)
                return 0;
            var read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            Consume(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_remaining == 0 || buffer.Length == 0)
                return 0;
            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], ct).ConfigureAwait(false);
            Consume(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        private void Consume(int read)
        {
            if (read == 0)
                throw new IOException("Catalog input was truncated while reading its captured bytes.");
            _remaining -= read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
