using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Workstation;

/// <summary>One serialized metadata row per authorized work-item identity, with atomic audit/replay receipts.</summary>
public sealed class PostgresOperatorTriageStore : IOperatorTriageStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OperatorTriageStoreOptions _options;
    private readonly PostgresMigrationRunner _migrations;
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private bool _migrated;

    public PostgresOperatorTriageStore(OperatorTriageStoreOptions options)
    {
        _options = options;
        _migrations = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString, Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("Workstation", "Migrations"), DisplayName = "Operator triage",
            LockScopeName = "operator_triage", ConnectionStringSettingName = "MERIDIAN_OPERATOR_TRIAGE_CONNECTION_STRING",
            LedgerTableName = "operator_triage_schema_migrations"
        });
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        await _migrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_migrated)
            {
                await _migrations.EnsureMigratedAsync(ct).ConfigureAwait(false);
                _migrated = true;
            }
        }
        finally { _migrationGate.Release(); }
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    public async Task<OperatorTriageState> GetAsync(OperatorTriageScope scope, string workItemId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await LoadAsync(connection, null, scope, workItemId, ct).ConfigureAwait(false);
    }

    public async Task<OperatorTriageState> UpdateAsync(OperatorTriageScope scope, string workItemId,
        Func<OperatorTriageState, CancellationToken, Task<OperatorTriageState>> update, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var gate = connection.CreateCommand())
        {
            gate.Transaction = transaction;
            gate.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@identity, 0))";
            gate.Parameters.AddWithValue("identity", JsonSerializer.Serialize(new[] { scope.TenantId, scope.CompanyId, workItemId }));
            await gate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var current = await LoadAsync(connection, transaction, scope, workItemId, ct).ConfigureAwait(false);
        var next = await update(current, ct).ConfigureAwait(false);
        if (next.Version != current.Version)
        {
            await using var save = connection.CreateCommand();
            save.Transaction = transaction;
            save.CommandText = $"""
                INSERT INTO {_options.Schema}.item_metadata (tenant_id, company_id, work_item_id, state)
                VALUES (@tenant, @company, @item, @state)
                ON CONFLICT (tenant_id, company_id, work_item_id) DO UPDATE SET state = EXCLUDED.state
                """;
            BindScope(save, scope, workItemId);
            save.Parameters.AddWithValue("state", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(next, Json));
            await save.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return next;
    }

    private async Task<OperatorTriageState> LoadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        OperatorTriageScope scope, string workItemId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT state::text FROM {_options.Schema}.item_metadata WHERE tenant_id=@tenant AND company_id=@company AND work_item_id=@item";
        BindScope(command, scope, workItemId);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? OperatorTriageState.Empty : JsonSerializer.Deserialize<OperatorTriageState>(json, Json)
            ?? throw new InvalidDataException("Retained triage metadata could not be read.");
    }

    private static void BindScope(NpgsqlCommand command, OperatorTriageScope scope, string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        command.Parameters.AddWithValue("tenant", scope.TenantId);
        command.Parameters.AddWithValue("company", scope.CompanyId);
        command.Parameters.AddWithValue("item", workItemId);
    }
}
