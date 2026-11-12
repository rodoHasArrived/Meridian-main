using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.PrivateCapital;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.FundAccounts;

/// <summary>Append-only PostgreSQL records with per-partition locking and transactional receipts.</summary>
public sealed class PostgresPrivateCapitalRecordStore : IPrivateCapitalRecordStore
{
    private readonly FundAccountStoreOptions _options;
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private bool _migrated;

    public PostgresPrivateCapitalRecordStore(FundAccountStoreOptions options)
    {
        if (!Regex.IsMatch(options.Schema, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid fund-account schema.", nameof(options));
        _options = options;
    }

    private async Task EnsureMigratedAsync(CancellationToken ct)
    {
        await _migrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_migrated)
                return;
            await new FundAccountMigrationRunner(_options).EnsureMigratedAsync(ct).ConfigureAwait(false);
            _migrated = true;
        }
        finally { _migrationGate.Release(); }
    }

    public async Task<IReadOnlyList<PrivateCapitalStoredRecord>> ReadAsync(PrivateCapitalScope scope, CancellationToken ct = default)
    {
        PrivateCapitalScopeValidation.Require(scope);
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return await LoadAsync(connection, null, scope, ct).ConfigureAwait(false);
    }

    public async Task AppendAsync(PrivateCapitalScope scope,
        Func<IReadOnlyList<PrivateCapitalStoredRecord>, IReadOnlyList<PrivateCapitalStoredRecord>> prepare,
        CancellationToken ct = default)
    {
        PrivateCapitalScopeValidation.Require(scope);
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var scopeLock = new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended(@scope, 0))", connection, transaction))
        {
            scopeLock.Parameters.AddWithValue("scope", JsonSerializer.Serialize(scope));
            await scopeLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var existing = await LoadAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        var added = prepare(existing);
        foreach (var record in added)
        {
            await using var command = new NpgsqlCommand($"insert into \"{_options.Schema}\".private_capital_record (tenant_id,company_id,record_kind,record_id,payload) values (@tenant,@company,@kind,@id,@payload)", connection, transaction);
            command.Parameters.AddWithValue("tenant", scope.TenantId);
            command.Parameters.AddWithValue("company", scope.CompanyId);
            command.Parameters.AddWithValue("kind", record.Kind);
            command.Parameters.AddWithValue("id", record.Id);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, record.Payload);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PrivateCapitalStoredRecord>> LoadAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, PrivateCapitalScope scope, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"select record_kind,record_id,payload::text from \"{_options.Schema}\".private_capital_record where tenant_id=@tenant and company_id=@company", connection, transaction);
        command.Parameters.AddWithValue("tenant", scope.TenantId);
        command.Parameters.AddWithValue("company", scope.CompanyId);
        var records = new List<PrivateCapitalStoredRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            records.Add(new(reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));
        return records;
    }
}
