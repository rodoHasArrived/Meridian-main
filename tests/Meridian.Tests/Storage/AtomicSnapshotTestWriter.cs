using Meridian.Storage.Archival;

namespace Meridian.Tests.Storage;

/// <summary>
/// Deterministic write probe for storage snapshot fault, blocking, and cancellation scenarios.
/// </summary>
internal sealed class AtomicSnapshotTestWriter : IDisposable
{
    private SnapshotWriteBlock? _nextBlock;
    private int _failNextWrite;

    public void FailNextWrite() => Interlocked.Exchange(ref _failNextWrite, 1);

    public SnapshotWriteBlock BlockNextWrite()
    {
        var block = new SnapshotWriteBlock();
        if (Interlocked.CompareExchange(ref _nextBlock, block, null) != null)
        {
            throw new InvalidOperationException("A snapshot write is already configured to block.");
        }

        return block;
    }

    public void Write(string path, string content)
    {
        var block = Interlocked.Exchange(ref _nextBlock, null);
        if (block != null)
        {
            block.SignalEntered();
            block.WaitForRelease();
        }

        ThrowIfFaulted();
        AtomicFileWriter.Write(path, content);
    }

    public async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        var block = Interlocked.Exchange(ref _nextBlock, null);
        if (block != null)
        {
            block.SignalEntered();
            await block.WaitForReleaseAsync(ct).ConfigureAwait(false);
        }

        ThrowIfFaulted();
        await AtomicFileWriter.WriteAsync(path, content, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _nextBlock, null)?.Release();
    }

    private void ThrowIfFaulted()
    {
        if (Interlocked.Exchange(ref _failNextWrite, 0) == 1)
        {
            throw new IOException("Injected snapshot persistence failure.");
        }
    }
}

internal sealed class SnapshotWriteBlock
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilEnteredAsync(CancellationToken ct) => _entered.Task.WaitAsync(ct);

    public void Release() => _release.TrySetResult();

    internal void SignalEntered() => _entered.TrySetResult();

    internal void WaitForRelease() => _release.Task.GetAwaiter().GetResult();

    internal Task WaitForReleaseAsync(CancellationToken ct) => _release.Task.WaitAsync(ct);
}
