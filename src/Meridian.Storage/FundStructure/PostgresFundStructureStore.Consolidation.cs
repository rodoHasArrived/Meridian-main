using System.Data;
using Meridian.Contracts.FundStructure;
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
            return new ConsolidationAuthorityLease(connection, transaction, _options.Schema);
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

    private sealed class ConsolidationAuthorityLease : ILegalEntityReadAuthorityLease
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private int _disposed;
        public ConsolidationAuthorityLease(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema)
        {
            _connection = connection;
            _transaction = transaction;
            var host = connection.Host ?? throw new InvalidOperationException(
                "A configured PostgreSQL host is required to establish legal-entity endpoint authority.");
            StructureAuthorityProof = new(host, connection.Port, connection.Database, schema.ToLowerInvariant(),
                () => Volatile.Read(ref _disposed) == 0 && _transaction.Connection is not null);
        }
        public LegalEntityReadAuthorityProof StructureAuthorityProof { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            { await _transaction.DisposeAsync().ConfigureAwait(false); }
            finally { await _connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
