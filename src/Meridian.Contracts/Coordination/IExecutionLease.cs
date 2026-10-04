namespace Meridian.Contracts.Coordination;

/// <summary>A lease owned by one execution, with atomic ownership checks around side effects.</summary>
public interface IExecutionLease : IAsyncDisposable
{
    string ResourceId { get; }

    /// <summary>
    /// Runs an action only while this execution owns the resource. Acquisition and transfer
    /// remain excluded until the action returns. Actions must not reenter the same lease.
    /// </summary>
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);

    async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        T result = default!;
        await ExecuteAsync(async token => { result = await action(token).ConfigureAwait(false); }, ct).ConfigureAwait(false);
        return result;
    }
}

public sealed record ExecutionLeaseAcquireResult(IExecutionLease? Lease, string? CurrentOwner)
{
    public bool Acquired => Lease is not null;
}

public sealed class ExecutionLeaseLostException(string resourceId)
    : InvalidOperationException($"Execution ownership of '{resourceId}' was lost; no further side effects are permitted.");
