using FluentAssertions;
using Meridian.Documents;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Tests.Ui.Evidence;

public sealed class FileEvidenceArtifactStoreBufferedQuotaTests : IDisposable
{
    private const int KiB = 1024;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-buffered-quota-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ShortBufferedWrite_AccountsForPhysicalBytesBeforeAnotherRequestCanReserveHeadroom()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var disk = new MemoryStream();
        await using var destination = new BufferedStream(disk, 64 * KiB);
        var coordinator = new EvidenceStorageQuotaCoordinator(_root, new EvidenceStorageQuotaOptions
        {
            MaxPackageBytes = 100 * KiB,
            DefaultTenantBudgetBytes = 100 * KiB,
            MinimumDiskHeadroomBytes = 4 * KiB
        }, _ => 0, _ => 16 * KiB - disk.Length);
        await using var reservation = await coordinator.ReserveAsync("first-tenant", 8 * KiB, 1, timeout.Token);
        var copiedChunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new ShortReadThenWaitStream(copiedChunk, finishSource);
        var copy = FileEvidenceArtifactStore.CopyExportArtifactAsync(source, destination, "short-read", timeout.Token, reservation);

        try
        {
            // The second source read starts only after the first chunk's reservation has been
            // reconciled. A completed BufferedStream.WriteAsync alone leaves these bytes in RAM.
            await copiedChunk.Task.WaitAsync(timeout.Token);
            var competing = async () =>
            {
                await using var other = await coordinator.ReserveAsync("second-tenant", 5 * KiB, 1, timeout.Token);
            };
            (await competing.Should().ThrowAsync<EvidenceStorageQuotaExceededException>())
                .Which.Reason.Should().Be("disk-headroom");
            disk.Length.Should().Be(8 * KiB);
        }
        finally
        {
            finishSource.TrySetResult();
            await copy.WaitAsync(timeout.Token);
        }

        (await copy).SizeBytes.Should().Be(8 * KiB);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ShortReadThenWaitStream(TaskCompletionSource copiedChunk, TaskCompletionSource finishSource) : Stream
    {
        private int _bytesRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_bytesRead < 8 * KiB)
            {
                var count = Math.Min(buffer.Length, 8 * KiB - _bytesRead);
                buffer.Span[..count].Clear();
                _bytesRead += count;
                return count;
            }

            copiedChunk.TrySetResult();
            await finishSource.Task.WaitAsync(ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
