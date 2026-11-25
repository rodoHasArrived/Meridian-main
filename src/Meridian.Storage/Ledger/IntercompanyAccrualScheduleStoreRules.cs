using System.Text.Json;
using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Append-only checks shared by the file and PostgreSQL accrual-schedule stores.</summary>
public static class IntercompanyAccrualScheduleStoreRules
{
    public static string Tenant(string tenantId) => string.IsNullOrWhiteSpace(tenantId) || tenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
        ? throw new UnauthorizedAccessException("A single tenant scope is required.")
        : tenantId.Trim().ToLowerInvariant();

    /// <summary>True for an identical retry of the current version; otherwise <paramref name="next"/> must append exactly one step.</summary>
    public static bool IsIdenticalRetry(IntercompanyAccrualScheduleDto? current, IntercompanyAccrualScheduleDto next, long expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (current is not null && current.Version == next.Version && Serialize(current) == Serialize(next))
            return true;
        if ((current?.Version ?? 0) != expectedVersion)
            throw new InvalidOperationException("The schedule changed since it was read. Reload it before acting.");
        if (next.Version != expectedVersion + 1 || next.Events.Count != (current?.Events.Count ?? 0) + 1
            || (current is not null && (current.ScheduleId != next.ScheduleId || current.LedgerBookId != next.LedgerBookId
                || current.Kind != next.Kind || current.ProfileArrangementId != next.ProfileArrangementId
                || current.PrincipalArrangementId != next.PrincipalArrangementId || current.CompanyId != next.CompanyId
                || !current.Events.SequenceEqual(next.Events.Take(current.Events.Count)))))
            throw new InvalidOperationException("A schedule version must append exactly one step and keep its retained identity and history.");
        return false;
    }

    public static void RequireTenant(string tenant, IntercompanyAccrualScheduleDto schedule)
    {
        if (!string.Equals(Tenant(schedule.TenantId), tenant, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The schedule belongs to a different tenant.");
    }

    public static string Serialize(IntercompanyAccrualScheduleDto schedule)
        => JsonSerializer.Serialize(schedule, IntercompanyAccrualScheduleJsonContext.Default.IntercompanyAccrualScheduleDto);

    public static IntercompanyAccrualScheduleDto Deserialize(string json)
        => JsonSerializer.Deserialize(json, IntercompanyAccrualScheduleJsonContext.Default.IntercompanyAccrualScheduleDto)
            ?? throw new InvalidDataException("A retained schedule payload is empty.");
}
