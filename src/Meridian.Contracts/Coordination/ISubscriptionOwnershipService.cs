namespace Meridian.Application.Coordination;

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
