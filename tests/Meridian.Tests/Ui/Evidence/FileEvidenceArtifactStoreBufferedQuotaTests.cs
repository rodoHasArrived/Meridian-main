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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DestinationFailure_RetainsPreparedChargeUntilCleanupCompletes(bool failFlush, bool cancel)
    {
        var cleaned = false;
        var coordinator = new EvidenceStorageQuotaCoordinator(_root, new EvidenceStorageQuotaOptions
        {
            MaxPackageBytes = 100,
            DefaultTenantBudgetBytes = 100,
            MinimumDiskHeadroomBytes = 0
        }, _ => 0, _ => long.MaxValue, (_, _) => Task.FromResult(cleaned));
        await using var reservation = await coordinator.ReserveAsync("tenant", 80, 1);
        using var source = new MemoryStream(new byte[80]);
        using var destination = new FailingDestination(failFlush, cancel);
        var copy = () => FileEvidenceArtifactStore.CopyExportArtifactAsync(source, destination, "partial", reservation: reservation);
        if (cancel)
        {
            await copy.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await copy.Should().ThrowAsync<IOException>();
        }
        destination.Length.Should().Be(failFlush ? 80 : 3);
        var path = Path.Combine(_root, "workstation", "evidence-quota", reservation.Id + ".json");
        using (var record = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            record.RootElement.GetProperty("reservedBytes").GetInt64().Should().Be(80);
            record.RootElement.GetProperty("preparedBytes").GetInt64().Should().Be(80);
            record.RootElement.GetProperty("writtenBytes").GetInt64().Should().Be(0);
        }
        await reservation.DisposeAsync();
        var competing = async () =>
        {
            await using var other = await coordinator.ReserveAsync("tenant", 21, 1);
        };
        (await competing.Should().ThrowAsync<EvidenceStorageQuotaExceededException>()).Which.Reason.Should().Be("tenant-bytes");
        File.Exists(path).Should().BeTrue();
        cleaned = true;
        (await coordinator.RecoverAbandonedAsync()).Should().Be(1);
        await using var retry = await coordinator.ReserveAsync("tenant", 100, 1);
    }

    private sealed class FailingDestination(bool failFlush, bool cancel) : MemoryStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await base.WriteAsync(failFlush ? buffer : buffer[..3], ct);
            if (!failFlush)
            {
                ThrowFailure();
            }
        }

        public override Task FlushAsync(CancellationToken ct)
        {
            ThrowFailure();
            return Task.CompletedTask;
        }

        private void ThrowFailure()
        {
            if (cancel)
            {
                throw new OperationCanceledException("Injected destination cancellation.");
            }
            throw new IOException("Injected destination failure.");
        }
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
