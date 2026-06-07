namespace Meridian.Application.Coordination;

public sealed class ScheduledWorkOwnershipService : IScheduledWorkOwnershipService
{
    private const string DispatcherLeaderResourceId = "leader/backfill-dispatch";
    private readonly ILeaseManager _leaseManager;

    public ScheduledWorkOwnershipService(ILeaseManager leaseManager)
    {
        _leaseManager = leaseManager;
    }

    public Task<LeaseAcquireResult> TryAcquireDispatcherLeadershipAsync(CancellationToken ct = default)
        => _leaseManager.TryAcquireAsync(DispatcherLeaderResourceId, ct);

    public Task<LeaseAcquireResult> TryAcquireScheduleAsync(string scheduleId, CancellationToken ct = default)
        => _leaseManager.TryAcquireAsync($"schedules/{scheduleId}", ct);

    public Task<bool> ReleaseScheduleAsync(string scheduleId, CancellationToken ct = default)
        => _leaseManager.ReleaseAsync($"schedules/{scheduleId}", ct);

    public Task<LeaseAcquireResult> TryAcquireJobAsync(string jobId, CancellationToken ct = default)
        => _leaseManager.TryAcquireAsync($"jobs/{jobId}", ct);

    public Task<bool> ReleaseJobAsync(string jobId, CancellationToken ct = default)
        => _leaseManager.ReleaseAsync($"jobs/{jobId}", ct);
}
