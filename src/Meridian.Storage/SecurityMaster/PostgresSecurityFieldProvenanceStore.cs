using System.Data.Common;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// SQL for the <c>security_field_provenance</c> table, shared between the standalone store and the
/// transactional conflict-resolution write-back so both write the same shape. Public because the
/// conflict service (Meridian.Application) composes the upsert into the SAME transaction that
/// closes a conflict — the invariant migration 027 exists to guarantee.
/// </summary>
public static class PostgresSecurityFieldProvenanceSql
{
    public const string Table = "security_field_provenance";

    public const string SelectColumns =
        "security_id, field_path, origin, source_system, as_of, updated_by, confidence, origin_reference, recorded_at, source_version";

    public static async Task UpsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string schema,
        SecurityFieldProvenanceRecord record,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(record);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Same-key upserts are ordered by the attributed write's COMMIT ORDER when both rows carry
        // one (source_version — the projection version for CanonicalWrite rows), falling back to
        // recorded_at when either side predates the column or the origin has no commit sequence.
        // Wall-clock alone cannot order canonical attribution: amendment v2's delayed provenance
        // write can arrive after v3's with a later recorded_at, and would overwrite v3's row with
        // the stale incumbent — misattributing the field in subsequent conflict detection.
        command.CommandText =
            $"""
            insert into {schema}.{Table} as current_provenance (
                security_id, field_path, origin, source_system, as_of, updated_by, confidence,
                origin_reference, recorded_at, source_version)
            values (
                @security_id, @field_path, @origin, @source_system, @as_of, @updated_by, @confidence,
                @origin_reference, @recorded_at, @source_version)
            on conflict (security_id, field_path, origin) do update set
                source_system = excluded.source_system,
                as_of = excluded.as_of,
                updated_by = excluded.updated_by,
                confidence = excluded.confidence,
                origin_reference = excluded.origin_reference,
                recorded_at = excluded.recorded_at,
                source_version = excluded.source_version
            where case
                when excluded.source_version is not null and current_provenance.source_version is not null
                    then excluded.source_version > current_provenance.source_version
                         or (excluded.source_version = current_provenance.source_version
                             and excluded.recorded_at > current_provenance.recorded_at)
                else excluded.recorded_at > current_provenance.recorded_at
            end;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("field_path", record.FieldPath);
        command.Parameters.AddWithValue("origin", record.Origin);
        command.Parameters.AddWithValue("source_system", record.SourceSystem);
        command.Parameters.AddWithValue("as_of", (object?)record.AsOf?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("updated_by", (object?)record.UpdatedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("confidence", (object?)record.Confidence ?? DBNull.Value);
        command.Parameters.AddWithValue("origin_reference", (object?)record.OriginReference ?? DBNull.Value);
        command.Parameters.AddWithValue("recorded_at", record.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("source_version", (object?)record.SourceVersion ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static async Task RemoveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string schema,
        Guid securityId,
        string fieldPath,
        string origin,
        DateTimeOffset clearedAt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The recorded_at guard mirrors the upsert's newest-write-wins clause: a delayed clear may
        // not erase an attribution recorded after it.
        command.CommandText =
            $"""
            delete from {schema}.{Table}
            where security_id = @security_id
              and field_path = @field_path
              and origin = @origin
              and recorded_at <= @cleared_at;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("field_path", fieldPath);
        command.Parameters.AddWithValue("origin", origin);
        command.Parameters.AddWithValue("cleared_at", clearedAt.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static SecurityFieldProvenanceRecord Map(DbDataReader reader) => new(
        SecurityId: reader.GetGuid(0),
        FieldPath: reader.GetString(1),
        Origin: reader.GetString(2),
        SourceSystem: reader.GetString(3),
        AsOf: reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
        UpdatedBy: reader.IsDBNull(5) ? null : reader.GetString(5),
        Confidence: reader.IsDBNull(6) ? null : reader.GetDecimal(6),
        OriginReference: reader.IsDBNull(7) ? null : reader.GetString(7),
        RecordedAt: reader.IsDBNull(8) ? DateTimeOffset.MinValue : new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
        SourceVersion: reader.IsDBNull(9) ? null : reader.GetInt64(9));
}

/// <summary>
/// Postgres-backed <see cref="ISecurityFieldProvenanceStore"/>. Standalone reads/writes only —
/// conflict resolution does not go through this store, it composes
/// <see cref="PostgresSecurityFieldProvenanceSql.UpsertAsync"/> into its own close transaction.
/// </summary>
public sealed class PostgresSecurityFieldProvenanceStore : ISecurityFieldProvenanceStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityFieldProvenanceStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task UpsertAsync(SecurityFieldProvenanceRecord record, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await PostgresSecurityFieldProvenanceSql
            .UpsertAsync(connection, transaction: null, _options.Schema, record, ct)
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        Guid securityId, string fieldPath, string origin, DateTimeOffset clearedAt, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await PostgresSecurityFieldProvenanceSql
            .RemoveAsync(connection, transaction: null, _options.Schema, securityId, fieldPath, origin, clearedAt, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityFieldProvenanceRecord>> GetAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {PostgresSecurityFieldProvenanceSql.SelectColumns}
            from {_options.Schema}.{PostgresSecurityFieldProvenanceSql.Table}
            where security_id = @security_id
            order by field_path, origin;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<SecurityFieldProvenanceRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(PostgresSecurityFieldProvenanceSql.Map(reader));
        }

        return results;
    }

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
}
