using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed partial class PostgresAssetOperationsProjectionStore
{
    private async Task AcquirePositionScopeLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BookPositionDto position,
        CancellationToken ct)
    {
        if (!InstrumentPositionProjectionRules.ParticipatesInActiveOverlap(position.Status))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(@lock_key);";
        command.Parameters.AddWithValue("lock_key", ComputeScopeLockKey(position));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task AcquireProjectionRunLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectionRunId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(@lock_key);";
        command.Parameters.AddWithValue("lock_key", ComputeProjectionRunLockKey(projectionRunId));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ValidateProjectionLineageAppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProjectionLineageDto lineage,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select (payload -> 'projectionLineage')::text
            from {Qualified("book_position_projections")}
            where projection_run_id = @projection_run_id
            union all
            select (payload -> 'projectionLineage')::text
            from {Qualified("position_economic_state_projections")}
            where projection_run_id = @projection_run_id;
            """;
        command.Parameters.AddWithValue("projection_run_id", lineage.ProjectionRunId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var persisted = JsonSerializer.Deserialize<ProjectionLineageDto>(reader.GetString(0), JsonOptions);
            if (persisted is not null && !PayloadEquals(persisted, lineage))
            {
                throw new InvalidOperationException(
                    $"Projection run '{lineage.ProjectionRunId:D}' cannot be reused with conflicting lineage.");
            }
        }
    }

    private static long ComputeScopeLockKey(BookPositionDto position)
    {
        var scope = string.Join(
            '|',
            position.SecurityId.ToString("D"),
            position.RoleId.ToString("D"),
            position.BookContext.LedgerBookId.ToString("D"),
            position.BookContext.FundProfileId.Trim().ToUpperInvariant(),
            position.BookContext.FundStructureNodeKind,
            position.PositionSide.Trim().ToUpperInvariant());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"position-scope|{scope}"));
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    private static long ComputeProjectionRunLockKey(Guid projectionRunId)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"projection-run|{projectionRunId:D}"));
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }
}
