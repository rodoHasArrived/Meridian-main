using Meridian.Contracts.DirectLending;
using Npgsql;

namespace Meridian.Storage.DirectLending;

public sealed partial class PostgresDirectLendingStateStore
{
    private static async Task LockRunIdentityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string kind, Guid runId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@identity, 0));";
        command.Parameters.AddWithValue("identity", $"direct-lending/{kind}/{runId:N}");
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<ProjectionRunDto?> ReadProjectionIdentityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid runId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select projection_run_id, loan_id, loan_terms_version, servicing_revision,
                   projection_as_of, market_data_as_of, trigger_event_id, trigger_type,
                   terms_hash, engine_version, status, supersedes_projection_run_id, generated_at
            from {Qualified("projection_run")} where projection_run_id = @run_id;
            """;
        command.Parameters.AddWithValue("run_id", runId);
        return (await ReadProjectionRunsAsync(command, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    private async Task<ReconciliationRunDto?> ReadReconciliationIdentityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid runId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select reconciliation_run_id, loan_id, projection_run_id, requested_at, completed_at, status
            from {Qualified("reconciliation_run")} where reconciliation_run_id = @run_id;
            """;
        command.Parameters.AddWithValue("run_id", runId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new ReconciliationRunDto(reader.GetGuid(0), reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                reader.GetString(5))
            : null;
    }
}
