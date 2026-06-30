namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Defense-in-depth guard for body-supplied fund-profile scopes on governed Security Master writes.
///
/// <para>The platform's tenant isolation is enforced by the <b>single-company-per-deployment boundary</b>
/// (today <c>TenantId == CompanyId</c>; the fund-scoped stores — strategy runs, ledger books, report
/// packs — partition by <c>FundProfileId</c> alone, not by tenant). That boundary is the control; see
/// <c>docs/security/security-remediation-backlog.md</c> (SEC-005) for the tracked storage-enforced
/// isolation follow-up.</para>
///
/// <para>This guard is <b>not</b> that boundary. It is a tripwire that refuses a fund-profile id which
/// <i>demonstrably</i> belongs to a different company. It deliberately allows a fund the caller already
/// uses, a fund unknown to every company, and the case where the backing authority is unavailable, so a
/// legitimate edit is never blocked (no false-deny). Under the current single-company runtime it never
/// fires; it only bites if a future multi-tenant, shared-datastore deployment co-locates several
/// companies' funds.</para>
/// </summary>
public interface IFundProfileTenantGuard
{
    /// <summary>
    /// Evaluates whether <paramref name="fundProfileId"/> may be used under the caller's tenant/company
    /// context. Returns allowed for a blank scope, a fund the caller has touched, a fund unknown to every
    /// company, or when the backing authority is unavailable (the deployment boundary remains the
    /// control). Returns denied only when the fund has tenant-attributed history exclusively under other
    /// companies.
    /// </summary>
    Task<FundProfileTenantDecision> EvaluateAsync(
        WorkstationTenantContext tenant,
        string? fundProfileId,
        CancellationToken ct = default);
}

/// <summary>Outcome of an <see cref="IFundProfileTenantGuard"/> evaluation.</summary>
public sealed record FundProfileTenantDecision(bool IsAllowed, string Reason)
{
    public static FundProfileTenantDecision Allow(string reason) => new(true, reason);

    public static FundProfileTenantDecision Deny(string reason) => new(false, reason);
}
