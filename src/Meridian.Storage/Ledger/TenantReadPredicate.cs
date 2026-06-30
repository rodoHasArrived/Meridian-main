namespace Meridian.Storage.Ledger;

/// <summary>
/// SEC-005 slice 4c-ii read-predicate decision. Centralizes the rule for whether a fund-scoped read
/// should filter by the stamped <c>tenant_id</c> column, and the SQL fragment that does so, so the
/// fail-open branch is unit-testable without a database.
/// </summary>
/// <remarks>
/// The Postgres ledger suite is skipped in CI (no database), so an over-filtering regression — a
/// predicate wrongly applied to a tenantless caller, or a normalization mismatch — would otherwise
/// ship uncaught and make legitimate single-company data vanish. Keeping the decision here lets the
/// highest-risk branch (tenantless caller ⇒ no filter) be proven in CI.
///
/// Semantics: a row is visible when its <c>tenant_id</c> IS NULL (unbound/legacy — fail-open) OR
/// equals the caller's tenant. The predicate is emitted ONLY when the caller has a resolved tenant;
/// a tenantless caller (single-company deployment, or the legacy tenantless admin profile) gets no
/// predicate at all, so every row passes and behavior is identical under one-company-per-deployment.
/// The comparison uses <c>lower(trim(...))</c> on both sides to match the write-side stamp and
/// <c>FundProfileOwnership.IsHeldBy</c> (trimmed, case-insensitive).
/// </remarks>
internal static class TenantReadPredicate
{
    /// <summary>The conventional bind-parameter name for the caller's tenant.</summary>
    public const string ParameterName = "caller_tenant";

    /// <summary>
    /// True when the caller has a resolved tenant and the read must be scoped to it. A tenantless
    /// caller yields false: the predicate is omitted and every row passes (deployment-boundary
    /// fail-open posture).
    /// </summary>
    public static bool ShouldFilter(string? callerTenantId)
        => !string.IsNullOrWhiteSpace(callerTenantId);

    /// <summary>
    /// The normalized tenant value to bind as the predicate parameter, trimmed to match the write-side
    /// stamp. Only meaningful when <see cref="ShouldFilter"/> is true.
    /// </summary>
    public static string NormalizeParameter(string callerTenantId)
        => callerTenantId.Trim();

    /// <summary>
    /// The fail-open tenant predicate for a row whose tenant column is given by
    /// <paramref name="tenantColumnExpression"/> (e.g. <c>tenant_id</c> or <c>p.tenant_id</c>).
    /// Prefixed with <c> and </c> for direct concatenation onto a <c>where 1 = 1</c> clause.
    /// </summary>
    public static string FilterClause(string tenantColumnExpression)
        => $" and ({tenantColumnExpression} is null"
            + $" or lower(trim({tenantColumnExpression})) = lower(trim(@{ParameterName})))";

    /// <summary>
    /// The fail-open tenant predicate for a journal-entry read that has no period join in its main
    /// query: scopes the entry by the tenant stamped on its accounting period via an EXISTS subquery.
    /// </summary>
    public static string PeriodExistsClause(string periodsTable, string periodIdColumnExpression)
        => $" and exists (select 1 from {periodsTable} tenant_period"
            + $" where tenant_period.period_id = {periodIdColumnExpression}"
            + $" and (tenant_period.tenant_id is null"
            + $" or lower(trim(tenant_period.tenant_id)) = lower(trim(@{ParameterName}))))";
}
