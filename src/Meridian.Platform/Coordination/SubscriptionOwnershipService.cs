namespace Meridian.Application.Coordination;

public sealed class SubscriptionOwnershipService : ISubscriptionOwnershipService
{
    private readonly ILeaseManager _leaseManager;

    public SubscriptionOwnershipService(ILeaseManager leaseManager)
    {
        _leaseManager = leaseManager;
    }

    public Task<LeaseAcquireResult> TryAcquireAsync(
        string providerId,
        string kind,
        string symbol,
        CancellationToken ct = default)
        => _leaseManager.TryAcquireAsync(BuildResourceId(providerId, kind, symbol), ct);

    public Task<bool> ReleaseAsync(
        string providerId,
        string kind,
        string symbol,
        CancellationToken ct = default)
        => _leaseManager.ReleaseAsync(BuildResourceId(providerId, kind, symbol), ct);

    private static string BuildResourceId(string providerId, string kind, string symbol)
        => $"symbols/{providerId.Trim().ToLowerInvariant()}/{kind.Trim().ToLowerInvariant()}/{symbol.Trim().ToUpperInvariant()}";
}
