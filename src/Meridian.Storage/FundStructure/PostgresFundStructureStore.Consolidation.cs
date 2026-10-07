using System.Data;
using Npgsql;

namespace Meridian.Storage.FundStructure;

public sealed partial class PostgresFundStructureStore
{
    /// <summary>
    /// Holds authoritative perimeter tables stable, including newly inserted competing claims.
    /// Uses this store's actual connection and schema, which may differ from the ledger database.
    /// The caller must retain the lease until its consolidation journal commit has completed.
    /// </summary>
    public async Task<IAsyncDisposable> AcquireConsolidationAuthorityLeaseAsync(CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                lock table {Q("organization")}, {Q("business")}, {Q("fund")},
                    {Q("legal_entity")}, {Q("ownership_link")}, {Q("client")},
                    {Q("sleeve")}, {Q("vehicle")}, {Q("investment_portfolio")},
                    {Q("fund_structure_assignment")}, {Q("fund_structure_linked_account")} in share mode;
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new ConsolidationAuthorityLease(connection, transaction);
        }
        catch
        {
            try
            {
                if (transaction is not null)
                    await transaction.DisposeAsync().ConfigureAwait(false);
            }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private sealed class ConsolidationAuthorityLease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
