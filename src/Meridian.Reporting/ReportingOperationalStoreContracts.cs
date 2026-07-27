using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

public sealed record ReportingScheduleExecutionLease(
    string LeaseOwner,
    DateTimeOffset LeaseExpiresAtUtc,
    long LeaseVersion);

public sealed class ReportingScheduleExecutionLeaseException(
    string tenantId,
    string companyId,
    string scheduleId,
    string message) : InvalidOperationException(message)
{
    public string TenantId { get; } = tenantId;

    public string CompanyId { get; } = companyId;

    public string ScheduleId { get; } = scheduleId;
}

/// <summary>
/// Durable schedule snapshot boundary shared by reporting orchestration and storage adapters.
/// </summary>
public interface IReportingScheduleStore
{
    IReadOnlyList<ReportingScheduleRecordDto> Load();

    void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules);

    void Upsert(ReportingScheduleRecordDto schedule) =>
        Upsert(schedule, expectedUpdatedAtUtc: null);

    void Upsert(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var existingSchedules = Load();
        var matching = existingSchedules
            .Where(existing => HasSameIdentity(existing, schedule))
            .Take(2)
            .ToArray();
        if (matching.Length > 1)
        {
            throw new InvalidDataException(
                $"Reporting schedule '{schedule.TenantId}/{schedule.CompanyId}/{schedule.ScheduleId}' has duplicate retained rows.");
        }

        var current = matching.SingleOrDefault();
        EnsureExpectedRevision(schedule, current, expectedUpdatedAtUtc);
        if (current is not null
            && EqualityComparer<ReportingScheduleRecordDto>.Default.Equals(current, schedule))
        {
            return;
        }

        var retained = existingSchedules
            .Where(existing => !HasSameIdentity(existing, schedule))
            .Append(schedule)
            .ToArray();
        Save(retained);
    }

    ReportingScheduleExecutionLease? TryClaimExecution(
        ReportingScheduleRecordDto schedule,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "A reporting schedule execution lease must have a positive duration.");
        }

        var current = Load().SingleOrDefault(candidate => HasSameIdentity(candidate, schedule));
        return current is not null && current.UpdatedAtUtc == schedule.UpdatedAtUtc
            ? new ReportingScheduleExecutionLease(
                leaseOwner.Trim(),
                nowUtc.ToUniversalTime().Add(leaseDuration),
                LeaseVersion: 1)
            : null;
    }

    ReportingScheduleExecutionLease? RenewExecutionLease(
        ReportingScheduleRecordDto schedule,
        ReportingScheduleExecutionLease lease,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(lease);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "A reporting schedule execution lease must have a positive duration.");
        }

        return Load().Any(candidate =>
            HasSameIdentity(candidate, schedule)
            && candidate.UpdatedAtUtc == schedule.UpdatedAtUtc)
            ? lease with
            {
                LeaseExpiresAtUtc = nowUtc.ToUniversalTime().Add(leaseDuration)
            }
            : null;
    }

    void ReleaseExecutionLease(
        string tenantId,
        string companyId,
        string scheduleId,
        ReportingScheduleExecutionLease lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        ArgumentNullException.ThrowIfNull(lease);
    }

    void UpsertClaimedExecution(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset expectedUpdatedAtUtc,
        ReportingScheduleExecutionLease lease)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(lease);
        Upsert(schedule, expectedUpdatedAtUtc);
    }

    bool Delete(
        string tenantId,
        string companyId,
        string scheduleId,
        DateTimeOffset expectedUpdatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        var existing = Load();
        var current = existing.SingleOrDefault(schedule => HasSameIdentity(
            schedule,
            tenantId.Trim(),
            companyId.Trim(),
            scheduleId.Trim()));
        if (current is null)
        {
            return false;
        }
        if (current.UpdatedAtUtc != expectedUpdatedAtUtc)
        {
            throw ReportingScheduleConcurrencyException.ForConflict(
                current,
                expectedUpdatedAtUtc);
        }

        var retained = existing
            .Where(schedule => !HasSameIdentity(
                schedule,
                tenantId.Trim(),
                companyId.Trim(),
                scheduleId.Trim()))
            .ToArray();
        Save(retained);
        return true;
    }

    private static void EnsureExpectedRevision(
        ReportingScheduleRecordDto candidate,
        ReportingScheduleRecordDto? current,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (current is null)
        {
            if (expectedUpdatedAtUtc is not null)
            {
                throw ReportingScheduleConcurrencyException.ForMissing(
                    candidate,
                    expectedUpdatedAtUtc.Value);
            }

            return;
        }

        if (expectedUpdatedAtUtc is null)
        {
            if (EqualityComparer<ReportingScheduleRecordDto>.Default.Equals(current, candidate))
            {
                return;
            }

            throw ReportingScheduleConcurrencyException.ForConflict(
                current,
                expectedUpdatedAtUtc: null);
        }

        if (current.UpdatedAtUtc != expectedUpdatedAtUtc.Value)
        {
            throw ReportingScheduleConcurrencyException.ForConflict(
                current,
                expectedUpdatedAtUtc.Value);
        }
        if (!EqualityComparer<ReportingScheduleRecordDto>.Default.Equals(current, candidate)
            && candidate.UpdatedAtUtc <= current.UpdatedAtUtc)
        {
            throw new ArgumentException(
                "A changed reporting schedule must advance UpdatedAtUtc beyond the retained revision.",
                nameof(candidate));
        }
    }

    private static bool HasSameIdentity(
        ReportingScheduleRecordDto left,
        ReportingScheduleRecordDto right) =>
        HasSameIdentity(
            left,
            right.TenantId?.Trim() ?? string.Empty,
            right.CompanyId?.Trim() ?? string.Empty,
            right.ScheduleId.Trim());

    private static bool HasSameIdentity(
        ReportingScheduleRecordDto schedule,
        string tenantId,
        string companyId,
        string scheduleId) =>
        string.Equals(schedule.TenantId?.Trim() ?? string.Empty, tenantId, StringComparison.Ordinal)
        && string.Equals(schedule.CompanyId?.Trim() ?? string.Empty, companyId, StringComparison.Ordinal)
        && string.Equals(schedule.ScheduleId.Trim(), scheduleId, StringComparison.OrdinalIgnoreCase);
}

public sealed class ReportingScheduleConcurrencyException : InvalidOperationException
{
    private ReportingScheduleConcurrencyException(
        string tenantId,
        string companyId,
        string scheduleId,
        DateTimeOffset? expectedUpdatedAtUtc,
        DateTimeOffset? actualUpdatedAtUtc,
        string message)
        : base(message)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        ScheduleId = scheduleId;
        ExpectedUpdatedAtUtc = expectedUpdatedAtUtc;
        ActualUpdatedAtUtc = actualUpdatedAtUtc;
    }

    public string TenantId { get; }

    public string CompanyId { get; }

    public string ScheduleId { get; }

    public DateTimeOffset? ExpectedUpdatedAtUtc { get; }

    public DateTimeOffset? ActualUpdatedAtUtc { get; }

    public static ReportingScheduleConcurrencyException ForConflict(
        ReportingScheduleRecordDto current,
        DateTimeOffset? expectedUpdatedAtUtc) =>
        new(
            current.TenantId?.Trim() ?? string.Empty,
            current.CompanyId?.Trim() ?? string.Empty,
            current.ScheduleId.Trim(),
            expectedUpdatedAtUtc,
            current.UpdatedAtUtc,
            $"Reporting schedule '{current.TenantId}/{current.CompanyId}/{current.ScheduleId}' changed after it was loaded; expected revision '{Format(expectedUpdatedAtUtc)}', retained revision is '{Format(current.UpdatedAtUtc)}'. Reload and retry.");

    public static ReportingScheduleConcurrencyException ForMissing(
        ReportingScheduleRecordDto candidate,
        DateTimeOffset expectedUpdatedAtUtc) =>
        new(
            candidate.TenantId?.Trim() ?? string.Empty,
            candidate.CompanyId?.Trim() ?? string.Empty,
            candidate.ScheduleId.Trim(),
            expectedUpdatedAtUtc,
            actualUpdatedAtUtc: null,
            $"Reporting schedule '{candidate.TenantId}/{candidate.CompanyId}/{candidate.ScheduleId}' no longer exists at expected revision '{Format(expectedUpdatedAtUtc)}'. Reload and retry.");

    private static string Format(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        ?? "<create>";
}
