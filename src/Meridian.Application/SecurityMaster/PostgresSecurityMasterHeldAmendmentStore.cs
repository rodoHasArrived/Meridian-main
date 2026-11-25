using System.Data.Common;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Application.SecurityMaster;

/// <summary>PostgreSQL store for held ingest amendments (migration 070).</summary>
public sealed class PostgresSecurityMasterHeldAmendmentStore : ISecurityMasterHeldAmendmentStore
{
    private const string Columns =
        "held_amendment_id, security_id, source_system, request_json::text, dominated_field_paths, conflict_ids, "
        + "held_at_version, held_at, status, closed_by, closed_at";

    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterHeldAmendmentStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task AddAsync(SecurityMasterHeldAmendment amendment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(amendment);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {Table} (held_amendment_id, security_id, source_system, request_json, dominated_field_paths,
                                 conflict_ids, held_at_version, held_at, status)
            values (@id, @security_id, @source_system, @request_json, @paths, @conflict_ids, @version, @held_at, @status);
            """;
        command.Parameters.AddWithValue("id", amendment.HeldAmendmentId);
        command.Parameters.AddWithValue("security_id", amendment.SecurityId);
        command.Parameters.AddWithValue("source_system", amendment.SourceSystem);
        command.Parameters.Add(new NpgsqlParameter("request_json", NpgsqlDbType.Jsonb) { Value = amendment.RequestJson });
        command.Parameters.Add(new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = amendment.DominatedFieldPaths.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("conflict_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = amendment.ConflictIds.ToArray() });
        command.Parameters.AddWithValue("version", amendment.HeldAtVersion);
        command.Parameters.AddWithValue("held_at", amendment.HeldAt.UtcDateTime);
        command.Parameters.AddWithValue("status", amendment.Status);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityMasterHeldAmendment>> ListAsync(Guid? securityId, string? status, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select {Columns}
            from {Table}
            where (@security_id is null or security_id = @security_id)
              and (@status is null or status = @status)
            order by held_at, held_amendment_id;
            """;
        command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid) { Value = (object?)securityId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text) { Value = (object?)status ?? DBNull.Value });
        var rows = new List<SecurityMasterHeldAmendment>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    public async Task<SecurityMasterHeldAmendment?> GetAsync(Guid heldAmendmentId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {Columns} from {Table} where held_amendment_id = @id;";
        command.Parameters.AddWithValue("id", heldAmendmentId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<bool> CloseAsync(Guid heldAmendmentId, string status, string closedBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(closedBy);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            update {Table}
            set status = @status, closed_by = @closed_by, closed_at = now()
            where held_amendment_id = @id and status = 'Held';
            """;
        command.Parameters.AddWithValue("id", heldAmendmentId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("closed_by", closedBy);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private static SecurityMasterHeldAmendment Map(DbDataReader reader) => new(
        HeldAmendmentId: reader.GetGuid(0),
        SecurityId: reader.GetGuid(1),
        SourceSystem: reader.GetString(2),
        RequestJson: reader.GetString(3),
        DominatedFieldPaths: reader.GetFieldValue<string[]>(4),
        ConflictIds: reader.GetFieldValue<Guid[]>(5),
        HeldAtVersion: reader.GetInt64(6),
        HeldAt: new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
        Status: reader.GetString(8),
        ClosedBy: reader.IsDBNull(9) ? null : reader.GetString(9),
        ClosedAt: reader.IsDBNull(10) ? null : new DateTimeOffset(reader.GetDateTime(10), TimeSpan.Zero));

    private string Table => $"{_options.Schema}.security_master_held_amendments";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
