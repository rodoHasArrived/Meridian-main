using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// PostgreSQL store for stored market curves (<c>security_curves</c>, <c>security_curve_versions</c>,
/// migration 052). A curve's identity is write-once; its versions are append-only and immutable,
/// and every definition and publication appends to <c>security_governance_events</c> on the
/// <see cref="SecurityGovernanceStreams.Curves"/> stream in the same transaction as the write.
/// </summary>
public sealed class PostgresSecurityCurveStore : ISecurityCurveStore
{
    /// <summary>Governance event types recorded on the <see cref="SecurityGovernanceStreams.Curves"/> stream.</summary>
    public const string DefinedEvent = "CurveDefined";
    public const string VersionPublishedEvent = "CurveVersionPublished";

    private const string DefinitionColumns =
        "curve_id, curve_key, kind, currency, index_name, tenor, day_count, interpolation, source, description, created_by, created_at";

    private const string VersionColumns =
        "v.curve_version_id, v.curve_id, c.curve_key, v.version, v.as_of, v.recorded_at, v.sourced_at, v.source, v.published_by, v.points::text, v.content_sha256";

    private readonly SecurityMasterOptions _options;

    public PostgresSecurityCurveStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<SecurityCurveDefinitionDto> DefineAsync(DefineSecurityCurveRequest request, CancellationToken ct = default)
    {
        var normalized = SecurityCurveRules.Normalize(request);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Definitions of one key are serialized so the first writer wins and the second sees it.
        await LockKeyAsync(connection, transaction, normalized.CurveKey, ct).ConfigureAwait(false);
        var existing = await ReadDefinitionAsync(connection, transaction, "upper(curve_key) = upper(@key)", ("key", normalized.CurveKey), ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!HasSameFacts(existing, normalized))
            {
                throw new InvalidOperationException(
                    $"Curve key '{existing.CurveKey}' is already defined as {existing.Kind} {existing.Currency}" +
                    (existing.IndexName is null ? string.Empty : $" {existing.IndexName}") +
                    $" ({existing.DayCount}, {existing.Interpolation}, source '{existing.Source}'); a curve key is write-once, define a new key.");
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return existing;
        }

        var curveId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {_options.Schema}.security_curves
                    ({DefinitionColumns})
                values
                    (@curve_id, @curve_key, @kind, @currency, @index_name, @tenor, @day_count, @interpolation, @source, @description, @created_by, @created_at);
                """;
            insert.Parameters.AddWithValue("curve_id", curveId);
            insert.Parameters.AddWithValue("curve_key", normalized.CurveKey);
            insert.Parameters.AddWithValue("kind", normalized.Kind.ToString());
            insert.Parameters.AddWithValue("currency", normalized.Currency);
            insert.Parameters.AddWithValue("index_name", (object?)normalized.IndexName ?? DBNull.Value);
            insert.Parameters.AddWithValue("tenor", (object?)normalized.Tenor ?? DBNull.Value);
            insert.Parameters.AddWithValue("day_count", normalized.DayCount);
            insert.Parameters.AddWithValue("interpolation", normalized.Interpolation.ToString());
            insert.Parameters.AddWithValue("source", normalized.Source);
            insert.Parameters.AddWithValue("description", (object?)normalized.Description ?? DBNull.Value);
            insert.Parameters.AddWithValue("created_by", normalized.CreatedBy!);
            insert.Parameters.AddWithValue("created_at", createdAt.UtcDateTime);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // The event and the caller see the row as stored (timestamps at the table's precision).
        var stored = await ReadDefinitionAsync(connection, transaction, "curve_id = @id", ("id", curveId), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Curve '{normalized.CurveKey}' was not stored.");
        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            securityId: null,
            SecurityGovernanceStreams.Curves,
            DefinedEvent,
            stored.CreatedBy,
            stored.CreatedAt,
            new
            {
                curveId = stored.CurveId,
                curveKey = stored.CurveKey,
                kind = stored.Kind.ToString(),
                currency = stored.Currency,
                indexName = stored.IndexName,
                tenor = stored.Tenor,
                dayCount = stored.DayCount,
                interpolation = stored.Interpolation.ToString(),
                source = stored.Source,
                description = stored.Description
            },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return stored;
    }

    public async Task<SecurityCurveDefinitionDto?> GetDefinitionAsync(Guid curveId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadDefinitionAsync(connection, null, "curve_id = @id", ("id", curveId), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityCurveDefinitionDto>> ListDefinitionsAsync(
        SecurityCurveKind? kind = null,
        string? currency = null,
        string? indexName = null,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var predicates = new List<string>();
        if (kind is { } k)
        {
            predicates.Add("kind = @kind");
            command.Parameters.AddWithValue("kind", k.ToString());
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            predicates.Add("currency = upper(@currency)");
            command.Parameters.AddWithValue("currency", currency.Trim());
        }

        if (!string.IsNullOrWhiteSpace(indexName))
        {
            predicates.Add("upper(index_name) = upper(@index_name)");
            command.Parameters.AddWithValue("index_name", indexName.Trim());
        }

        var where = predicates.Count == 0 ? string.Empty : "where " + string.Join(" and ", predicates);
        command.CommandText =
            $"""
            select {DefinitionColumns}
            from {_options.Schema}.security_curves
            {where}
            order by upper(curve_key);
            """;

        var definitions = new List<SecurityCurveDefinitionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            definitions.Add(ReadDefinition(reader));
        }

        return definitions;
    }

    public async Task<SecurityCurveVersionDto> PublishVersionAsync(PublishSecurityCurveVersionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // The row lock serializes publishers of one curve, so the next version number is exact.
        var definition = await ReadDefinitionAsync(connection, transaction, "curve_id = @id", ("id", request.CurveId), ct, forUpdate: true).ConfigureAwait(false)
            ?? throw new ArgumentException($"Curve {request.CurveId:D} is not defined.", nameof(request));
        var normalized = SecurityCurveRules.Normalize(request, definition);
        var contentSha256 = SecurityCurveRules.ContentSha256(normalized.AsOf, normalized.Points);

        // Identical content for the same as-of is the retained version: nothing is written or recorded.
        var retained = await ReadVersionAsync(
            connection, transaction,
            "v.curve_id = @id and v.as_of = @as_of and v.content_sha256 = @hash",
            [("id", definition.CurveId), ("as_of", normalized.AsOf), ("hash", contentSha256)],
            ct).ConfigureAwait(false);
        if (retained is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return retained;
        }

        long version;
        await using (var next = connection.CreateCommand())
        {
            next.Transaction = transaction;
            next.CommandText = $"select coalesce(max(version), 0) + 1 from {_options.Schema}.security_curve_versions where curve_id = @id;";
            next.Parameters.AddWithValue("id", definition.CurveId);
            version = (long)(await next.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 1L);
        }

        var curveVersionId = Guid.NewGuid();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {_options.Schema}.security_curve_versions
                    (curve_version_id, curve_id, version, as_of, sourced_at, published_by, source, points, point_count, content_sha256)
                values
                    (@curve_version_id, @curve_id, @version, @as_of, @sourced_at, @published_by, @source, @points::jsonb, @point_count, @content_sha256);
                """;
            insert.Parameters.AddWithValue("curve_version_id", curveVersionId);
            insert.Parameters.AddWithValue("curve_id", definition.CurveId);
            insert.Parameters.AddWithValue("version", version);
            insert.Parameters.AddWithValue("as_of", normalized.AsOf);
            insert.Parameters.AddWithValue("sourced_at", (object?)normalized.SourcedAt?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("published_by", normalized.PublishedBy!);
            insert.Parameters.AddWithValue("source", normalized.Source);
            insert.Parameters.AddWithValue("points", JsonSerializer.Serialize(normalized.Points.ToArray(), SecurityCurvesJsonContext.Default.SecurityCurvePointDtoArray));
            insert.Parameters.AddWithValue("point_count", normalized.Points.Count);
            insert.Parameters.AddWithValue("content_sha256", contentSha256);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Re-read so the caller and the event carry recorded_at as the database stamped it.
        var stored = await ReadVersionAsync(connection, transaction, "v.curve_version_id = @id", [("id", curveVersionId)], ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Curve version {curveVersionId:D} was not stored.");
        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            securityId: null,
            SecurityGovernanceStreams.Curves,
            VersionPublishedEvent,
            stored.PublishedBy,
            stored.RecordedAt,
            new
            {
                curveId = stored.CurveId,
                curveVersionId = stored.CurveVersionId,
                curveKey = stored.CurveKey,
                kind = definition.Kind.ToString(),
                currency = definition.Currency,
                indexName = definition.IndexName,
                version = stored.Version,
                asOf = stored.AsOf,
                pointCount = stored.Points.Count,
                contentSha256 = stored.ContentSha256,
                source = stored.Source,
                sourcedAt = stored.SourcedAt
            },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return stored;
    }

    public async Task<SecurityCurveVersionDto?> GetVersionAsync(Guid curveId, long version, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadVersionAsync(connection, null, "v.curve_id = @id and v.version = @version", [("id", curveId), ("version", version)], ct).ConfigureAwait(false);
    }

    public async Task<SecurityCurveVersionDto?> GetLatestVersionAsOfAsync(Guid curveId, DateOnly asOf, DateTimeOffset? knownAt = null, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        // The bitemporal read of the pricing lane: economic as-of on or before the date, knowledge
        // time on or before the cutoff (now when none is given); served by ix_security_curve_versions_as_of.
        return await ReadVersionAsync(
            connection, null,
            "v.curve_id = @id and v.as_of <= @as_of and v.recorded_at <= @known_at",
            [("id", curveId), ("as_of", asOf), ("known_at", (knownAt ?? DateTimeOffset.UtcNow).UtcDateTime)],
            ct,
            "order by v.as_of desc, v.version desc limit 1").ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityCurveVersionDto>> ListVersionsAsync(Guid curveId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {VersionColumns}
            from {_options.Schema}.security_curve_versions v
            join {_options.Schema}.security_curves c on c.curve_id = v.curve_id
            where v.curve_id = @id
            order by v.version;
            """;
        command.Parameters.AddWithValue("id", curveId);

        var versions = new List<SecurityCurveVersionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            versions.Add(ReadVersion(reader));
        }

        return versions;
    }

    /// <summary>Whether a re-definition of the key states the same identity; the description is descriptive, not a fact.</summary>
    private static bool HasSameFacts(SecurityCurveDefinitionDto stored, DefineSecurityCurveRequest declared)
        => stored.Kind == declared.Kind
           && string.Equals(stored.Currency, declared.Currency, StringComparison.Ordinal)
           && string.Equals(stored.IndexName, declared.IndexName, StringComparison.OrdinalIgnoreCase)
           && string.Equals(stored.Tenor, declared.Tenor, StringComparison.OrdinalIgnoreCase)
           && string.Equals(stored.DayCount, declared.DayCount, StringComparison.Ordinal)
           && stored.Interpolation == declared.Interpolation
           && string.Equals(stored.Source, declared.Source, StringComparison.Ordinal);

    private static async Task LockKeyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string curveKey, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_scope, 0));";
        command.Parameters.AddWithValue("lock_scope", "security-curve-key:" + curveKey.ToUpperInvariant());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<SecurityCurveDefinitionDto?> ReadDefinitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string predicate,
        (string Name, object Value) parameter,
        CancellationToken ct,
        bool forUpdate = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select {DefinitionColumns} from {_options.Schema}.security_curves where {predicate}{(forUpdate ? " for update" : string.Empty)};";
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadDefinition(reader) : null;
    }

    private async Task<SecurityCurveVersionDto?> ReadVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string predicate,
        IReadOnlyList<(string Name, object Value)> parameters,
        CancellationToken ct,
        string ordering = "")
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {VersionColumns}
            from {_options.Schema}.security_curve_versions v
            join {_options.Schema}.security_curves c on c.curve_id = v.curve_id
            where {predicate}
            {ordering};
            """;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadVersion(reader) : null;
    }

    private static SecurityCurveDefinitionDto ReadDefinition(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            Enum.Parse<SecurityCurveKind>(reader.GetString(2)),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            Enum.Parse<SecurityCurveInterpolation>(reader.GetString(7)),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11));

    private static SecurityCurveVersionDto ReadVersion(NpgsqlDataReader reader)
    {
        var points = JsonSerializer.Deserialize(reader.GetString(9), SecurityCurvesJsonContext.Default.SecurityCurvePointDtoArray) ?? [];
        return new SecurityCurveVersionDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetFieldValue<DateOnly>(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetString(7),
            reader.GetString(8),
            points,
            reader.GetString(10).Trim());
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
