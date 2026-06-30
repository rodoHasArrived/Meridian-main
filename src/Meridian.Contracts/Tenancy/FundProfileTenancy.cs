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
