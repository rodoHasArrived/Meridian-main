using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Raised when a fund-structure read or write cannot be served under the active tenant posture.
/// </summary>
/// <remarks>
/// Distinct from "returned nothing" on purpose. The exit criterion requires a request without
/// resolvable tenant scope to be <i>rejected rather than defaulted</i>, and an empty graph is a
/// default: the caller cannot tell it apart from a genuinely empty structure, and neither can an
/// operator reading a support ticket.
/// </remarks>
public sealed class FundStructureTenantScopeException : Exception
{
    public FundStructureTenantScopeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Whether a fund-structure node is visible to a caller. The read-side counterpart of
/// <see cref="TenantReadPredicate"/> for the store that has no SQL predicate to carry it.
/// </summary>
/// <remarks>
/// <para>Kept as a pure decision for the same reason <see cref="TenantReadPredicate"/> is: the
/// Postgres fund-structure suite is skipped in CI for want of a database, so an over- or
/// under-filtering regression here would otherwise ship uncaught — and the two failure modes are
/// asymmetric but both severe. Over-filtering makes a legitimate operator's structure vanish;
/// under-filtering is the cross-tenant read the criterion exists to close.</para>
/// </remarks>
public static class FundStructureTenantScope
{
    /// <summary>
    /// Whether <paramref name="nodeId"/> may be served to a caller in
    /// <paramref name="callerTenantId"/>.
    /// </summary>
    public static bool IsVisible(
        FundStructureTenantMap tenants,
        string? callerTenantId,
        Guid nodeId,
        TenantScopeEnforcementMode mode)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        // A store that does not model ownership has no ownership to enforce. This is the in-memory
        // and JSON-backed posture, which serves one undivided graph and is barred from production
        // compositions by ADR-019 rather than by this check.
        if (!tenants.IsPartitioned)
        {
            return true;
        }

        var hasCallerTenant = !string.IsNullOrWhiteSpace(callerTenantId);
        var isAttributed = tenants.NodeTenants.TryGetValue(nodeId, out var owner)
            && !string.IsNullOrWhiteSpace(owner);

        if (!hasCallerTenant)
        {
            // Under fail-closed a tenantless caller is refused outright before reaching here; the
            // service raises FundStructureTenantScopeException rather than filtering to nothing.
            return mode != TenantScopeEnforcementMode.FailClosed;
        }

        if (isAttributed)
        {
            return IsHeldBy(owner!, callerTenantId!);
        }

        // The unattributed row is where the two postures actually differ, and where the ordering
        // constraint bites: fail-closed hides rows the attribution has not reached, so the backfill
        // has to land first or a scoped reader loses data that was never anyone else's.
        return mode != TenantScopeEnforcementMode.FailClosed;
    }

    /// <summary>
    /// Whether a caller may read the fund structure at all, or must be refused for want of a
    /// resolvable scope.
    /// </summary>
    public static bool IsCallerAdmissible(
        FundStructureTenantMap tenants,
        string? callerTenantId,
        TenantScopeEnforcementMode mode)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        return !tenants.IsPartitioned
            || mode != TenantScopeEnforcementMode.FailClosed
            || !string.IsNullOrWhiteSpace(callerTenantId);
    }

    /// <summary>
    /// Trimmed, case-insensitive tenant identity, matching the write-side stamp,
    /// <see cref="FundProfileOwnership.IsHeldBy"/>, and <see cref="TenantReadPredicate"/>'s
    /// <c>lower(trim(...))</c> comparison. A single comparison rule across all three is what stops a
    /// node verifying as owned in one path and foreign in another.
    /// </summary>
    private static bool IsHeldBy(string ownerTenantId, string callerTenantId)
        => string.Equals(ownerTenantId.Trim(), callerTenantId.Trim(), StringComparison.OrdinalIgnoreCase);
}
