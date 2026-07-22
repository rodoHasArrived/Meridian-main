namespace Meridian.Contracts.Tenancy;

/// <summary>
/// Authoritative registry mapping a fund profile to its owning tenant/company — the storage-enforced
/// source of truth for fund-scoped tenant isolation (security backlog SEC-005).
///
/// <para>Once a fund profile is bound to a tenant, data keyed by that fund (strategy runs, ledger books,
/// report packs) must only be served to callers in the owning tenant. Ownership is established on first
/// authoritative use under a tenant scope (trust-on-first-use); the first owner wins and is never
/// silently reassigned. Under a single-company-per-deployment runtime every fund binds to the one tenant,
/// so nothing is ever foreign and behavior is unchanged.</para>
/// </summary>
public interface IFundProfileTenancyRegistry
{
    /// <summary>
    /// Claims <paramref name="fundProfileId"/> for the given tenant/company if it is currently unbound,
    /// then returns the effective owner. Idempotent and first-owner-wins: a fund already bound to a
    /// different tenant is left unchanged and that existing owner is returned, so the caller can compare
    /// it to detect a foreign fund.
    /// </summary>
    Task<FundProfileOwnership> BindAsync(
        string fundProfileId,
        string tenantId,
        string? companyId = null,
        CancellationToken ct = default);

    /// <summary>Resolves the owning tenant/company for a fund profile, or null when it is unbound.</summary>
    Task<FundProfileOwnership?> ResolveAsync(string fundProfileId, CancellationToken ct = default);

    /// <summary>
    /// True when the caller's tenant may use the fund: it is unbound (unknown) or bound to the caller.
    /// False only when the fund is bound to a different tenant. A read-only check — it never binds.
    /// </summary>
    Task<bool> IsAccessibleAsync(
        string fundProfileId,
        string tenantId,
        string? companyId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves the caller's server-side tenant for the current ambient scope (e.g. the active HTTP
/// request), so storage can apply SEC-005 slice 4c-ii tenant read predicates without threading a
/// caller-tenant argument through every read method and intermediate service.
///
/// <para>Returns null when there is no tenant in scope — a non-request/background caller, or the
/// single-company-per-deployment runtime where the legacy tenantless admin profile has no company.
/// A null result is the fail-open signal: reads are not tenant-scoped and every row passes, matching
/// the deployment-boundary posture. The web layer supplies an <see cref="IHttpContextAccessor"/>-backed
/// implementation; the storage layer depends only on this abstraction (no ASP.NET Core coupling).</para>
/// </summary>
public interface IFundScopeTenantAccessor
{
    /// <summary>
    /// The caller's resolved tenant id for the current scope, or null when none is in scope (fail-open).
    /// </summary>
    string? ResolveCallerTenant();
}

/// <summary>Recorded ownership of a fund profile by a tenant (and, for audit, its company).</summary>
public sealed record FundProfileOwnership(string FundProfileId, string TenantId, string? CompanyId)
{
    /// <summary>
    /// Whether this ownership is held by the supplied tenant. Tenant identity is the authoritative scope
    /// key (it falls back to company id when a distinct tenant id is not yet issued), matched
    /// case-insensitively after trimming.
    /// </summary>
    public bool IsHeldBy(string? tenantId)
        => !string.IsNullOrWhiteSpace(tenantId)
            && string.Equals(TenantId.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase);
}
