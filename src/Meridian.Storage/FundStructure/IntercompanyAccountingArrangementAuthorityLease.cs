namespace Meridian.Storage.FundStructure;

internal sealed class IntercompanyAccountingArrangementAuthorityLease(Func<ValueTask> release) : IAsyncDisposable
{
    private int _disposed;
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0 ? release() : ValueTask.CompletedTask;
}
