using System.Collections.Concurrent;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FileReportingScheduleStore
{
    private static readonly ConcurrentDictionary<string, ReportingScheduleExecutionLease> ExecutionLeases =
        new(StringComparer.Ordinal);

    private readonly string _storeKey;

    public ReportingScheduleExecutionLease? TryClaimExecution(
        ReportingScheduleRecordDto schedule,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        lock (_gate)
        {
            var identity = ReportingScheduleIdentity.From(schedule);
            var current = Load().SingleOrDefault(candidate =>
                ReportingScheduleIdentityComparer.Instance.Equals(
                    ReportingScheduleIdentity.From(candidate),
                    identity));
            if (current is null || current.UpdatedAtUtc != schedule.UpdatedAtUtc)
            {
                return null;
            }

            var evaluatedAtUtc = DateTimeOffset.UtcNow;
            var normalizedOwner = leaseOwner.Trim();
            var claimKey = BuildExecutionLeaseKey(identity);
            if (ExecutionLeases.TryGetValue(claimKey, out var retained)
                && retained.LeaseExpiresAtUtc > evaluatedAtUtc
                && !string.Equals(
                    retained.LeaseOwner,
                    normalizedOwner,
                    StringComparison.Ordinal))
            {
                return null;
            }

            var acquired = new ReportingScheduleExecutionLease(
                normalizedOwner,
                evaluatedAtUtc.Add(leaseDuration),
                retained is null ? 1 : checked(retained.LeaseVersion + 1));
            ExecutionLeases[claimKey] = acquired;
            return acquired;
        }
    }

    public ReportingScheduleExecutionLease? RenewExecutionLease(
        ReportingScheduleRecordDto schedule,
        ReportingScheduleExecutionLease lease,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(lease);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        lock (_gate)
        {
            var identity = ReportingScheduleIdentity.From(schedule);
            var claimKey = BuildExecutionLeaseKey(identity);
            var evaluatedAtUtc = nowUtc.ToUniversalTime();
            if (!ExecutionLeases.TryGetValue(claimKey, out var retained)
                || retained.LeaseExpiresAtUtc <= evaluatedAtUtc
                || !string.Equals(
                    retained.LeaseOwner,
                    lease.LeaseOwner,
                    StringComparison.Ordinal)
                || retained.LeaseVersion != lease.LeaseVersion)
            {
                return null;
            }

            var renewed = retained with
            {
                LeaseExpiresAtUtc = evaluatedAtUtc.Add(leaseDuration)
            };
            ExecutionLeases[claimKey] = renewed;
            return renewed;
        }
    }

    public void ReleaseExecutionLease(
        string tenantId,
        string companyId,
        string scheduleId,
        ReportingScheduleExecutionLease lease)
    {
        var identity = ReportingScheduleIdentity.Create(
            tenantId,
            companyId,
            scheduleId);
        ArgumentNullException.ThrowIfNull(lease);
        lock (_gate)
        {
            var claimKey = BuildExecutionLeaseKey(identity);
            if (ExecutionLeases.TryGetValue(claimKey, out var retained)
                && string.Equals(
                    retained.LeaseOwner,
                    lease.LeaseOwner,
                    StringComparison.Ordinal)
                && retained.LeaseVersion == lease.LeaseVersion)
            {
                ExecutionLeases.TryRemove(claimKey, out _);
            }
        }
    }

    public void UpsertClaimedExecution(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset expectedUpdatedAtUtc,
        ReportingScheduleExecutionLease lease)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(lease);
        lock (_gate)
        {
            var identity = ReportingScheduleIdentity.From(schedule);
            var claimKey = BuildExecutionLeaseKey(identity);
            if (!ExecutionLeases.TryGetValue(claimKey, out var retained)
                || retained.LeaseExpiresAtUtc <= DateTimeOffset.UtcNow
                || !string.Equals(
                    retained.LeaseOwner,
                    lease.LeaseOwner,
                    StringComparison.Ordinal)
                || retained.LeaseVersion != lease.LeaseVersion)
            {
                throw new ReportingScheduleExecutionLeaseException(
                    identity.TenantId,
                    identity.CompanyId,
                    identity.ScheduleId,
                    "The reporting schedule execution lease is missing, expired, or was superseded by another owner.");
            }

            Upsert(schedule, expectedUpdatedAtUtc);
        }
    }

    private string BuildExecutionLeaseKey(ReportingScheduleIdentity identity) =>
        $"{_storeKey.Length}:{_storeKey}:{identity.TenantId.Length}:{identity.TenantId}:{identity.CompanyId.Length}:{identity.CompanyId}:{identity.ScheduleId.ToLowerInvariant()}";
}
