using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed class PostgresAssetOperationsProjectionStore : IAssetOperationsProjectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AssetOperationsOptions _options;

    public PostgresAssetOperationsProjectionStore(AssetOperationsOptions options)
    {
        _options = options;
        ValidateIdentifier(_options.Schema, nameof(options.Schema));
    }

    public async Task<AssetOperationsDetailDto?> GetAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        var subject = await ReadSingleAsync<AssetOperationSubjectDto>(
            connection,
            "asset_operation_subjects",
            securityId,
            ct).ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        return new AssetOperationsDetailDto(
            subject,
            await ReadListAsync<AssetTermsVersionDto>(connection, "asset_terms_versions", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetLifecycleEventDto>(connection, "asset_lifecycle_events", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetCashFlowProjectionRunDto>(connection, "asset_cash_flow_projection_runs", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetProjectedCashFlowDto>(connection, "asset_projected_cash_flows", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetActualActivityDto>(connection, "asset_actual_activity", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetReconciliationRunDto>(connection, "asset_reconciliation_runs", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetReconciliationResultDto>(connection, "asset_reconciliation_results", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetLedgerProjectionDto>(connection, "asset_ledger_projections", securityId, ct).ConfigureAwait(false),
            await ReadSingleAsync<AssetOperationsReadinessDto>(connection, "asset_operations_readiness", securityId, ct).ConfigureAwait(false)
                ?? BuildFallbackReadiness(subject),
            await ReadListAsync<AssetLifecycleEventDto>(connection, "asset_workflow_audit", securityId, ct).ConfigureAwait(false));
    }

    public async Task UpsertAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(approval);

        if (projection.Subject.SecurityId == Guid.Empty)
        {
            throw new ArgumentException("Asset Operations projections require a non-empty security_id.", nameof(projection));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await DeleteExistingAsync(connection, transaction, projection.Subject.SecurityId, ct).ConfigureAwait(false);
        await InsertAsync(connection, transaction, "asset_operation_subjects", projection.Subject.SecurityId, "SecurityMaster", projection.Subject.SecurityId.ToString("D"), projection.Subject, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_terms_versions", projection.Subject.SecurityId, projection.TermsHistory, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_lifecycle_events", projection.Subject.SecurityId, projection.LifecycleEvents, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_cash_flow_projection_runs", projection.Subject.SecurityId, projection.CashFlowProjectionRuns, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_projected_cash_flows", projection.Subject.SecurityId, projection.ProjectedCashFlows, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_actual_activity", projection.Subject.SecurityId, projection.ActualActivity, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_reconciliation_runs", projection.Subject.SecurityId, projection.ReconciliationRuns, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_reconciliation_results", projection.Subject.SecurityId, projection.ReconciliationResults, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_ledger_projections", projection.Subject.SecurityId, projection.LedgerProjections, approval, ct).ConfigureAwait(false);
        await InsertAsync(connection, transaction, "asset_operations_readiness", projection.Subject.SecurityId, projection.Readiness.SourceDomain, projection.Readiness.SourceEntityId, projection.Readiness, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_workflow_audit", projection.Subject.SecurityId, projection.WorkflowAudit, approval, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AssetOperationsOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private async Task DeleteExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        foreach (var table in Tables)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"delete from {Qualified(table)} where security_id = @security_id;";
            command.Parameters.AddWithValue("security_id", securityId);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task InsertManyAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        Guid securityId,
        IReadOnlyList<T> rows,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct)
    {
        foreach (var row in rows)
        {
            var source = SourceLineage(row);
            await InsertAsync(connection, transaction, table, securityId, source.SourceDomain, source.SourceEntityId, row, ct).ConfigureAwait(false);
        }
    }

    private async Task InsertAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        Guid securityId,
        string? sourceDomain,
        string? sourceEntityId,
        T payload,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(table)} (id, security_id, source_domain, source_entity_id, payload)
            values (@id, @security_id, @source_domain, @source_entity_id, @payload::jsonb);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("source_domain", (object?)sourceDomain ?? DBNull.Value);
        command.Parameters.AddWithValue("source_entity_id", (object?)sourceEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<T?> ReadSingleAsync<T>(
        NpgsqlConnection connection,
        string table,
        Guid securityId,
        CancellationToken ct)
    {
        var rows = await ReadListAsync<T>(connection, table, securityId, ct).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        NpgsqlConnection connection,
        string table,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select payload::text
            from {Qualified(table)}
            where security_id = @security_id
            order by created_at, id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions);
            if (row is not null)
            {
                results.Add(row);
            }
        }

        return results;
    }

    private static AssetOperationsReadinessDto BuildFallbackReadiness(AssetOperationSubjectDto subject)
        => new(
            subject.SecurityId,
            "ReviewRequired",
            subject.OperationalProfile,
            [],
            subject.OperationalProfile,
            ["No Asset Operations readiness projection has been published."],
            DateTimeOffset.UtcNow,
            "AssetOperations",
            subject.SecurityId.ToString("D"));

    private static (string? SourceDomain, string? SourceEntityId) SourceLineage<T>(T row)
        => row switch
        {
            AssetTermsVersionDto value => (value.SourceDomain, value.SourceEntityId),
            AssetLifecycleEventDto value => (value.SourceDomain, value.SourceEntityId),
            AssetCashFlowProjectionRunDto value => (value.SourceDomain, value.SourceEntityId),
            AssetProjectedCashFlowDto value => (value.SourceDomain, value.SourceEntityId),
            AssetActualActivityDto value => (value.SourceDomain, value.SourceEntityId),
            AssetReconciliationRunDto value => (value.SourceDomain, value.SourceEntityId),
            AssetReconciliationResultDto value => (value.SourceDomain, value.SourceEntityId),
            AssetLedgerProjectionDto value => (value.SourceDomain, value.SourceEntityId),
            AssetOperationsReadinessDto value => (value.SourceDomain, value.SourceEntityId),
            _ => ("AssetOperations", null)
        };

    private string Qualified(string table)
    {
        ValidateIdentifier(table, nameof(table));
        return $"{QuoteIdentifier(_options.Schema)}.{QuoteIdentifier(table)}";
    }

    private static string QuoteIdentifier(string value) => $"\"{value}\"";

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("PostgreSQL identifiers cannot be empty.", parameterName);
        }

        if (!IsValidIdentifier(value))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and underscores, and start with a letter or underscore.",
                parameterName);
        }
    }

    private static bool IsValidIdentifier(string value)
    {
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(static character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static readonly string[] Tables =
    [
        "asset_operation_subjects",
        "asset_terms_versions",
        "asset_lifecycle_events",
        "asset_cash_flow_projection_runs",
        "asset_projected_cash_flows",
        "asset_actual_activity",
        "asset_reconciliation_runs",
        "asset_reconciliation_results",
        "asset_ledger_projections",
        "asset_operations_readiness",
        "asset_workflow_audit"
    ];
}
