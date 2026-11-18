using System.Text.Json;
using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Append-only checks shared by the file and PostgreSQL fixing stores.</summary>
public static class IntercompanyRateFixingStoreRules
{
    public static string Tenant(string tenantId) => string.IsNullOrWhiteSpace(tenantId) || tenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
        ? throw new UnauthorizedAccessException("A single tenant scope is required.")
        : tenantId.Trim().ToLowerInvariant();

    /// <summary>
    /// Null when <paramref name="next"/> is an identical retry of the current version, which is returned
    /// unchanged; otherwise <paramref name="next"/> must be exactly one new version that keeps all prior steps.
    /// </summary>
    public static bool IsIdenticalRetry(IntercompanyRateFixingDto? current, IntercompanyRateFixingDto next, long expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (current is not null && current.Version == next.Version && Serialize(current) == Serialize(next))
            return true;
        if ((current?.Version ?? 0) != expectedVersion)
            throw new InvalidOperationException("The fixing changed since it was read. Reload it before acting.");
        if (next.Version != expectedVersion + 1 || next.Events.Count != (current?.Events.Count ?? 0) + 1
            || (current is not null && (current.FixingId != next.FixingId || current.IndexReference != next.IndexReference
                || current.FixingDate != next.FixingDate || !current.Events.SequenceEqual(next.Events.Take(current.Events.Count)))))
            throw new InvalidOperationException("A fixing version must append exactly one step and keep its retained history.");
        return false;
    }

    public static void RequireTenant(string tenant, IntercompanyRateFixingDto fixing)
    {
        if (!string.Equals(Tenant(fixing.TenantId), tenant, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The fixing belongs to a different tenant.");
    }

    public static string Serialize(IntercompanyRateFixingDto fixing)
        => JsonSerializer.Serialize(fixing, IntercompanyRateFixingJsonContext.Default.IntercompanyRateFixingDto);
}
