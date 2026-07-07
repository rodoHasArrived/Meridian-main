using System.Data.Common;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Durable, Postgres-backed golden-record conflict store. Conflict detection reuses the shared
/// <see cref="SecurityMasterConflictDetection"/> logic; detected conflicts and their resolution state
/// are persisted so a resolution and its chosen winner survive process recycles and are visible across
/// every instance — the audit guarantee the in-memory store could not provide.
/// </summary>
public sealed class PostgresSecurityMasterConflictService : ISecurityMasterConflictService
{
    private const string ConflictsTable = "security_master_conflicts";

    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<PostgresSecurityMasterConflictService> _logger;

    public PostgresSecurityMasterConflictService(
        ISecurityMasterStore store,
        SecurityMasterOptions options,
        ILogger<PostgresSecurityMasterConflictService> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
    {
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var detected = SecurityMasterConflictDetection.DetectAll(all, DateTimeOffset.UtcNow);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        // Record newly detected conflicts; ON CONFLICT DO NOTHING preserves any existing resolution
        // state so a re-detected, already-resolved conflict is never re-opened.
        foreach (var conflict in detected)
        {
            await InsertIfAbsentAsync(connection, transaction: null, conflict, ct).ConfigureAwait(false);
        }

        if (detected.Count > 0)
        {
            _logger.LogInformation("Detected {Count} identifier conflicts in Security Master", detected.Count);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {ConflictColumns}
            from {Qualified(ConflictsTable)}
            where status = 'Open'
            order by detected_at;
            """;

        var results = new List<SecurityMasterConflict>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapConflict(reader));
        }

        return results;
    }

    public async Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {ConflictColumns}
            from {Qualified(ConflictsTable)}
            where conflict_id = @conflict_id;
            """;
        command.Parameters.AddWithValue("conflict_id", conflictId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapConflict(reader) : null;
    }

    public async Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
    {
        var newStatus = request.Resolution.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
            ? "Dismissed"
            : "Resolved";

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Atomic compare-and-set: only an Open conflict transitions, and the winner, resolver, and
        // reason are captured in the SAME write as the close. A concurrent resolver that lost the race
        // matches zero rows and observes null instead of overwriting the first operator's decision.
        command.CommandText =
            $"""
            update {Qualified(ConflictsTable)}
            set status = @status,
                resolved_winner_source = @resolved_winner_source,
                resolved_by = @resolved_by,
                resolved_reason = @resolved_reason,
                resolved_at = @resolved_at
            where conflict_id = @conflict_id and status = 'Open'
            returning {ConflictColumns};
            """;
        command.Parameters.AddWithValue("status", newStatus);
        command.Parameters.AddWithValue("resolved_winner_source", (object?)request.ChosenWinnerSource ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_by", request.ResolvedBy);
        command.Parameters.AddWithValue("resolved_reason", (object?)request.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_at", DateTimeOffset.UtcNow.UtcDateTime);
        command.Parameters.AddWithValue("conflict_id", request.ConflictId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var updated = MapConflict(reader);
        _logger.LogInformation(
            "Conflict {ConflictId} for security {SecurityId} {Status} by {ResolvedBy}",
            updated.ConflictId, updated.SecurityId, newStatus, request.ResolvedBy);
        return updated;
    }

    public async Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
    {
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var candidates = SecurityMasterConflictDetection.DetectForProjection(projection, all, DateTimeOffset.UtcNow);
        if (candidates.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        int newConflicts = 0;
        foreach (var conflict in candidates)
        {
            var inserted = await InsertIfAbsentAsync(connection, transaction: null, conflict, ct).ConfigureAwait(false);
            if (inserted)
            {
                newConflicts++;
                _logger.LogWarning(
                    "Ingest-time conflict detected: {FieldPath} already assigned to security {ExistingId} (new: {NewId})",
                    conflict.FieldPath, conflict.ValueB, projection.SecurityId);
            }
        }

        if (newConflicts > 0)
        {
            _logger.LogInformation(
                "Recorded {Count} new identifier conflict(s) for security {SecurityId}",
                newConflicts, projection.SecurityId);
        }
    }

    private async Task<bool> InsertIfAbsentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        SecurityMasterConflict conflict,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(ConflictsTable)} (
                conflict_id, security_id, conflict_kind, field_path,
                provider_a, value_a, provider_b, value_b, detected_at, status,
                resolved_winner_source, resolved_by, resolved_reason, resolved_at)
            values (
                @conflict_id, @security_id, @conflict_kind, @field_path,
                @provider_a, @value_a, @provider_b, @value_b, @detected_at, @status,
                @resolved_winner_source, @resolved_by, @resolved_reason, @resolved_at)
            on conflict (conflict_id) do nothing;
            """;
        command.Parameters.AddWithValue("conflict_id", conflict.ConflictId);
        command.Parameters.AddWithValue("security_id", conflict.SecurityId);
        command.Parameters.AddWithValue("conflict_kind", conflict.ConflictKind);
        command.Parameters.AddWithValue("field_path", conflict.FieldPath);
        command.Parameters.AddWithValue("provider_a", conflict.ProviderA);
        command.Parameters.AddWithValue("value_a", conflict.ValueA);
        command.Parameters.AddWithValue("provider_b", conflict.ProviderB);
        command.Parameters.AddWithValue("value_b", conflict.ValueB);
        command.Parameters.AddWithValue("detected_at", conflict.DetectedAt.UtcDateTime);
        command.Parameters.AddWithValue("status", conflict.Status);
        command.Parameters.AddWithValue("resolved_winner_source", (object?)conflict.ResolvedWinnerSource ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_by", (object?)conflict.ResolvedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_reason", (object?)conflict.ResolvedReason ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_at", (object?)conflict.ResolvedAt?.UtcDateTime ?? DBNull.Value);

        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    private const string ConflictColumns =
        "conflict_id, security_id, conflict_kind, field_path, provider_a, value_a, provider_b, value_b, " +
        "detected_at, status, resolved_winner_source, resolved_by, resolved_reason, resolved_at";

    private static SecurityMasterConflict MapConflict(DbDataReader reader) => new(
        ConflictId: reader.GetGuid(0),
        SecurityId: reader.GetGuid(1),
        ConflictKind: reader.GetString(2),
        FieldPath: reader.GetString(3),
        ProviderA: reader.GetString(4),
        ValueA: reader.GetString(5),
        ProviderB: reader.GetString(6),
        ValueB: reader.GetString(7),
        DetectedAt: new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
        Status: reader.GetString(9))
    {
        ResolvedWinnerSource = reader.IsDBNull(10) ? null : reader.GetString(10),
        ResolvedBy = reader.IsDBNull(11) ? null : reader.GetString(11),
        ResolvedReason = reader.IsDBNull(12) ? null : reader.GetString(12),
        ResolvedAt = reader.IsDBNull(13) ? null : new DateTimeOffset(reader.GetDateTime(13), TimeSpan.Zero),
    };

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
