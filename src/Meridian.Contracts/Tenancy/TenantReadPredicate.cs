namespace Meridian.Contracts.Tenancy;

/// <summary>
/// SEC-005 slice 4c-ii read-predicate decision, extended for W9-GOV-008 criterion 2. Centralizes
/// whether a fund-scoped read filters by the stamped <c>tenant_id</c> column and the SQL fragment
/// that does so, so the decision is unit-testable without a database and is shared across every
/// fund-scoped store (ledger, fund accounts, operations-continuity) rather than re-derived per store.
/// </summary>
/// <remarks>
/// <para>The Postgres stores' integration suites are skipped in CI (no database), so a filtering
/// regression — a predicate wrongly applied to a tenantless caller, a normalization mismatch, or a
/// fail-open clause left behind after the tightening — would otherwise ship uncaught. Keeping the
/// decision here lets every branch be proven in CI.</para>
///
/// <para><b>The two postures.</b> Under
/// <see cref="TenantScopeEnforcementMode.DeploymentBoundary"/> a row is visible when its
/// <c>tenant_id</c> IS NULL (unbound or legacy) OR equals the caller's tenant, and a tenantless
/// caller gets no predicate at all, so behaviour is identical under one-company-per-deployment.
/// Under <see cref="TenantScopeEnforcementMode.FailClosed"/> the <c>IS NULL</c> disjunct is dropped —
/// an unattributed row is no longer served to a scoped caller — and a caller whose tenant cannot be
/// resolved is refused outright rather than reading unfiltered.</para>
///
/// <para><b>Every clause takes the mode, and there is no mode-less overload.</b> A call site that
/// forgot to pass one would silently inherit the looser posture, and that is the exact failure this
/// type exists to make impossible to introduce quietly. <see cref="ShouldFilter"/> is the one
/// question the mode does not change — whether there is a tenant to scope by at all — so it does not
/// take one; <see cref="ShouldRejectRead"/> is where a tenantless caller is decided. The comparison
/// uses <c>lower(trim(...))</c> on both sides to match the write-side stamp and
/// <see cref="FundProfileOwnership.IsHeldBy"/>.</para>
/// </remarks>
public static class TenantReadPredicate
{
    /// <summary>The conventional bind-parameter name for the caller's tenant.</summary>
    public const string ParameterName = "caller_tenant";

    /// <summary>
    /// True when the caller has a resolved tenant and the read must be scoped to it.
    /// </summary>
    /// <remarks>
    /// A tenantless caller yields false under both postures, but for opposite reasons: under
    /// <see cref="TenantScopeEnforcementMode.DeploymentBoundary"/> there is nothing to scope by and
    /// every row passes, while under <see cref="TenantScopeEnforcementMode.FailClosed"/> the read
    /// should never have been issued — see <see cref="ShouldRejectRead"/>, which callers must consult
    /// first.
    /// </remarks>
    public static bool ShouldFilter(string? callerTenantId)
        => !string.IsNullOrWhiteSpace(callerTenantId);

    /// <summary>
    /// True when the read must be refused outright rather than served.
    /// </summary>
    /// <remarks>
    /// The categorical half of the criterion: a request without resolvable tenant scope is rejected
    /// rather than defaulted. A predicate cannot express refusal — omitting a clause returns
    /// everything — so this is a separate question the caller has to ask before building the command.
    /// Out-of-request readers that legitimately hold retained authority establish it through
    /// <see cref="FundScopeTenantAuthority"/> instead of being exempted here.
    /// </remarks>
    public static bool ShouldRejectRead(string? callerTenantId, TenantScopeEnforcementMode mode)
        => mode == TenantScopeEnforcementMode.FailClosed && string.IsNullOrWhiteSpace(callerTenantId);

    /// <summary>
    /// The normalized tenant value to bind as the predicate parameter, trimmed to match the
    /// write-side stamp. Only meaningful when <see cref="ShouldFilter"/> is true.
    /// </summary>
    public static string NormalizeParameter(string callerTenantId)
        => callerTenantId.Trim();

    /// <summary>
    /// The tenant predicate for a row whose tenant column is given by
    /// <paramref name="tenantColumnExpression"/> (e.g. <c>tenant_id</c> or <c>p.tenant_id</c>).
    /// Prefixed with <c> and </c> for direct concatenation onto a <c>where 1 = 1</c> clause.
    /// </summary>
    public static string FilterClause(string tenantColumnExpression, TenantScopeEnforcementMode mode)
    {
        var ownedByCaller =
            $"lower(trim({tenantColumnExpression})) = lower(trim(@{ParameterName}))";

        return mode == TenantScopeEnforcementMode.FailClosed
            ? $" and ({ownedByCaller})"
            : $" and ({tenantColumnExpression} is null or {ownedByCaller})";
    }

    /// <summary>
    /// The tenant predicate for a journal-entry read with no period join in its main query: scopes
    /// the entry by the tenant stamped on its accounting period via an EXISTS subquery.
    /// </summary>
    public static string PeriodExistsClause(
        string periodsTable,
        string periodIdColumnExpression,
        TenantScopeEnforcementMode mode)
    {
        var ownedByCaller =
            $"lower(trim(tenant_period.tenant_id)) = lower(trim(@{ParameterName}))";
        var tenantCondition = mode == TenantScopeEnforcementMode.FailClosed
            ? ownedByCaller
            : $"tenant_period.tenant_id is null or {ownedByCaller}";

        return $" and exists (select 1 from {periodsTable} tenant_period"
            + $" where tenant_period.period_id = {periodIdColumnExpression}"
            + $" and ({tenantCondition}))";
    }
}
