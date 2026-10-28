namespace Meridian.Contracts.Coordination;

/// <summary>
/// Coordinates dispatcher leadership and exclusive ownership of schedules and jobs.
/// </summary>
public interface IScheduledWorkOwnershipService
{
    Task<LeaseAcquireResult> TryAcquireDispatcherLeadershipAsync(CancellationToken ct = default);

    Task<LeaseAcquireResult> TryAcquireScheduleAsync(string scheduleId, CancellationToken ct = default);

    Task<bool> ReleaseScheduleAsync(string scheduleId, CancellationToken ct = default);

    Task<LeaseAcquireResult> TryAcquireJobAsync(string jobId, CancellationToken ct = default);

    Task<bool> ReleaseJobAsync(string jobId, CancellationToken ct = default);
}
