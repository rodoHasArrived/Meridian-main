using Meridian.Contracts.Tenancy;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    // SEC-005 slice 4c-ii: the caller's tenant for the current ambient scope, or null (fail-open).
    private string? ResolveCallerTenant() => _tenantAccessor?.ResolveCallerTenant();

    // SEC-005 slice 4c-ii, tightened for W9-GOV-008 criterion 2: append the tenant predicate (and bind
    // its parameter) to a read command. Under the deployment-boundary posture a tenantless caller adds
    // nothing, so every row passes — identical behavior under one-company-per-deployment. Under the
    // fail-closed posture that caller is refused instead, and the clause itself no longer serves
    // unattributed rows. The column expression is table-qualified by the caller.
    private void ApplyTenantReadFilter(NpgsqlCommand command, string tenantColumnExpression, string? callerTenantId)
    {
        RejectUnscopedRead(callerTenantId, "ledger records");

        if (!TenantReadPredicate.ShouldFilter(callerTenantId))
        {
            return;
        }

        command.CommandText += TenantReadPredicate.FilterClause(tenantColumnExpression, _tenantScope.Mode);
        command.Parameters.AddWithValue(
            TenantReadPredicate.ParameterName,
            TenantReadPredicate.NormalizeParameter(callerTenantId!));
    }

    // SEC-005 slice 4c-ii: scope a journal-entry read (which has no period join in its main query) by
    // the tenant stamped on the entry's accounting period, via an EXISTS subquery.
    private void ApplyTenantPeriodReadFilter(NpgsqlCommand command, string periodIdColumnExpression, string? callerTenantId)
    {
        RejectUnscopedRead(callerTenantId, "ledger journal entries");

        if (!TenantReadPredicate.ShouldFilter(callerTenantId))
        {
            return;
        }

        command.CommandText += TenantReadPredicate.PeriodExistsClause(
            Qualified("accounting_periods"), periodIdColumnExpression, _tenantScope.Mode);
        command.Parameters.AddWithValue(
            TenantReadPredicate.ParameterName,
            TenantReadPredicate.NormalizeParameter(callerTenantId!));
    }

    // Rejected, not emptied: an empty result set is indistinguishable from a genuinely empty ledger,
    // both to the caller and to whoever reads the support ticket it produces. A background job that
    // holds retained authority declares it through FundScopeTenantAuthority rather than being exempt.
    private void RejectUnscopedRead(string? callerTenantId, string readDescription)
    {
        if (TenantReadPredicate.ShouldRejectRead(callerTenantId, _tenantScope.Mode))
        {
            throw new TenantScopeRejectedException(readDescription);
        }
    }
}
