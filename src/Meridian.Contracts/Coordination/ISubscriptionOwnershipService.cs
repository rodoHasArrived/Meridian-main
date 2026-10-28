namespace Meridian.Contracts.Coordination;

/// <summary>
/// Coordinates ownership of provider subscriptions by data kind and symbol.
/// </summary>
public interface ISubscriptionOwnershipService
{
    Task<LeaseAcquireResult> TryAcquireAsync(
        string providerId,
        string kind,
        string symbol,
        CancellationToken ct = default);

    Task<bool> ReleaseAsync(
        string providerId,
        string kind,
        string symbol,
        CancellationToken ct = default);
}
