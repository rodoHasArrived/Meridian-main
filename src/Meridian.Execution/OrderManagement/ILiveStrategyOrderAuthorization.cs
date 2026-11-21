namespace Meridian.Execution.OrderManagement;

/// <summary>Leases live authority until the gateway call starts. The lease never spans broker acknowledgement.</summary>
public interface ILiveStrategyOrderAuthorization
{
    Task<LiveStrategyOrderAuthorizationLease> AcquireAsync(
        string runId, string? strategyArtifactId, Guid? fundAccountId,
        string symbol, Func<decimal?> measureUsdNotional, CancellationToken ct);
}

public sealed class LiveStrategyOrderAuthorizationLease(
    string? refusal,
    IAsyncDisposable? authorityLock = null) : IAsyncDisposable
{
    public string? Refusal { get; } = refusal;

    public ValueTask DisposeAsync() => authorityLock?.DisposeAsync() ?? ValueTask.CompletedTask;
}
