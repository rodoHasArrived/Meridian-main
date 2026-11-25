using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : IImpactDependencyGuard
{
    public async Task<IAsyncDisposable> AcquireImpactDependencyLeaseAsync(string tenantId, string fundProfileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        if (!string.Equals(ResolveCallerTenant()?.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Impact finalization requires an authenticated tenant-bound ledger scope.");
        var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Stable broad locks intentionally fence phantoms as well as existing versions. The lease
            // precedes the Security Master generation lock and is held only for finalization.
            command.CommandText = "set local lock_timeout = '5s'; lock table " + string.Join(", ",
                new[] { "accounting_configuration_journal_templates", "accounting_periods", "fund_profile_tenancy", "journal_entries", "journal_legs", "ledger_books", "tax_lot_policies", "tax_lots" }
                    .Select(Qualified)) + " in share mode;";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await using var scope = connection.CreateCommand();
            scope.Transaction = transaction;
            scope.CommandText = $"select count(*) from {Qualified("fund_profile_tenancy")} where lower(trim(fund_profile_id)) = lower(trim(@fund)) and lower(trim(tenant_id)) = lower(trim(@tenant))";
            scope.Parameters.AddWithValue("fund", fundProfileId);
            scope.Parameters.AddWithValue("tenant", tenantId);
            if (Convert.ToInt64(await scope.ExecuteScalarAsync(ct).ConfigureAwait(false)) != 1)
                throw new InvalidOperationException("Impact finalization requires authoritative fund ownership.");
            return new ImpactDependencyLease(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ImpactDependencyLease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            { await transaction.RollbackAsync().ConfigureAwait(false); }
            finally { await transaction.DisposeAsync().ConfigureAwait(false); await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
