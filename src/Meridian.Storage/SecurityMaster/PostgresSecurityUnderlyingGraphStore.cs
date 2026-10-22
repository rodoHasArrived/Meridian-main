using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// PostgreSQL store for the underlying graph (<c>security_underlying_links</c>). Declared links are
/// written here, each change appending to <c>security_governance_events</c> in the same
/// transaction; derived links are written by <see cref="PostgresSecurityMasterStore"/> inside the
/// projection transaction (see <see cref="SecurityUnderlyingLinkWriter"/>), so both read from one
/// table.
/// </summary>
public sealed class PostgresSecurityUnderlyingGraphStore : ISecurityUnderlyingGraphStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityUnderlyingGraphStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<SecurityUnderlyingLinkDto>> GetUnderlyingsAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await SecurityUnderlyingLinkWriter.ReadAsync(
                connection,
                null,
                _options.Schema,
                "where security_id = @id",
                securityId,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityUnderlyingLinkDto>> GetDependentsAsync(Guid underlyingSecurityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await SecurityUnderlyingLinkWriter.ReadAsync(
                connection,
                null,
                _options.Schema,
                "where underlying_security_id = @id",
                underlyingSecurityId,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<SecurityUnderlyingLinkDto> DeclareAsync(DeclareSecurityUnderlyingLinkRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SecurityId == Guid.Empty)
            throw new ArgumentException("A declared link must name the security it belongs to.", nameof(request));
        if (request.UnderlyingSecurityId is null && string.IsNullOrWhiteSpace(request.Reference))
            throw new ArgumentException("A declared link must name an underlying security or a reference.", nameof(request));
        if (request.UnderlyingSecurityId == request.SecurityId)
            throw new ArgumentException("A security cannot be its own underlying.", nameof(request));
        if (request.ValidTo is { } validTo && validTo <= request.ValidFrom)
            throw new ArgumentException("A declared link's validity window must end after it starts.", nameof(request));
        if (request.Weight is < 0m)
            throw new ArgumentException("A basket weight cannot be negative.", nameof(request));

        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        var link = new SecurityUnderlyingLinkDto(
            SecurityUnderlyingLinkDeriver.LinkId(request.SecurityId, request.Role, request.UnderlyingSecurityId, reference, SecurityUnderlyingLinkSource.Declared),
            request.SecurityId,
            request.Role,
            request.UnderlyingSecurityId,
            reference,
            request.Weight,
            request.ValidFrom,
            request.ValidTo,
            SecurityUnderlyingLinkSource.Declared,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            string.IsNullOrWhiteSpace(request.CreatedBy) ? "unknown" : request.CreatedBy.Trim(),
            DateTimeOffset.UtcNow);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Declarations of one edge are serialized so the event records the state it replaced.
        await SecurityUnderlyingLinkWriter.LockLinkAsync(connection, transaction, link.LinkId, ct).ConfigureAwait(false);
        var previous = await SecurityUnderlyingLinkWriter.ReadByIdAsync(connection, transaction, _options.Schema, link.LinkId, ct).ConfigureAwait(false);
        var changes = previous is null ? [] : SecurityUnderlyingLinkWriter.ChangedFields(previous, link);
        if (previous is not null && changes.Count == 0)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return previous;
        }

        await SecurityUnderlyingLinkWriter.UpsertAsync(connection, transaction, _options.Schema, link, ct).ConfigureAwait(false);

        // The event and the caller see the row as stored: the table keeps the first spelling of a
        // reference and rounds weights and timestamps to its own precision.
        var stored = await SecurityUnderlyingLinkWriter.ReadByIdAsync(connection, transaction, _options.Schema, link.LinkId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Declared underlying link '{link.LinkId}' was not stored.");
        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            stored.SecurityId,
            SecurityGovernanceStreams.UnderlyingGraph,
            previous is null ? SecurityUnderlyingLinkWriter.DeclaredEvent : SecurityUnderlyingLinkWriter.AmendedEvent,
            stored.CreatedBy,
            stored.CreatedAt,
            new
            {
                link = SecurityUnderlyingLinkWriter.Describe(stored),
                previous = previous is null ? null : SecurityUnderlyingLinkWriter.Describe(previous),
                changes
            },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return stored;
    }

    public async Task<bool> RemoveDeclaredAsync(Guid securityId, Guid linkId, string removedBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(removedBy);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SecurityUnderlyingLinkWriter.LockLinkAsync(connection, transaction, linkId, ct).ConfigureAwait(false);

        SecurityUnderlyingLinkDto? removed;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                delete from {_options.Schema}.security_underlying_links
                where link_id = @link_id and security_id = @security_id and source = 'Declared'
                returning {SecurityUnderlyingLinkWriter.Columns};
                """;
            command.Parameters.AddWithValue("link_id", linkId);
            command.Parameters.AddWithValue("security_id", securityId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            removed = await reader.ReadAsync(ct).ConfigureAwait(false) ? SecurityUnderlyingLinkWriter.ReadLink(reader) : null;
        }

        if (removed is null)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        // The row is gone; the governance log keeps what was removed and who removed it.
        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            securityId,
            SecurityGovernanceStreams.UnderlyingGraph,
            SecurityUnderlyingLinkWriter.RemovedEvent,
            removedBy,
            DateTimeOffset.UtcNow,
            new { link = SecurityUnderlyingLinkWriter.Describe(removed) },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
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

/// <summary>
/// The SQL the graph table is written and read with, shared by the graph store (declared links)
/// and the projection store (derived links) so the two never drift on column shape.
/// </summary>
internal static class SecurityUnderlyingLinkWriter
{
    /// <summary>Governance event types recorded on the <see cref="SecurityGovernanceStreams.UnderlyingGraph"/> stream.</summary>
    public const string DeclaredEvent = "UnderlyingLinkDeclared";
    public const string AmendedEvent = "UnderlyingLinkAmended";
    public const string RemovedEvent = "UnderlyingLinkRemoved";

    /// <summary>The graph table's columns, in the order <see cref="ReadLink"/> reads them.</summary>
    public const string Columns =
        "link_id, security_id, role, underlying_security_id, reference_text, weight, " +
        "valid_from, valid_to, source, notes, created_by, created_at";

    /// <summary>Serializes writers of one link for the rest of the transaction.</summary>
    public static async Task LockLinkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid linkId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_scope, 0));";
        command.Parameters.AddWithValue("lock_scope", "security-underlying-link:" + linkId.ToString("D"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static async Task<SecurityUnderlyingLinkDto?> ReadByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schema,
        Guid linkId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select {Columns} from {schema}.security_underlying_links where link_id = @link_id;";
        command.Parameters.AddWithValue("link_id", linkId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadLink(reader) : null;
    }

    /// <summary>
    /// The declared facts a re-declaration of the same edge changes, compared at the precision the
    /// table stores (microsecond timestamps, ten weight decimals) so a value that round-trips
    /// unchanged is never reported as an amendment. The edge's identity (security, role, target,
    /// reference) is fixed by the link id.
    /// </summary>
    public static IReadOnlyList<string> ChangedFields(SecurityUnderlyingLinkDto stored, SecurityUnderlyingLinkDto declared)
    {
        var changes = new List<string>();
        if (RoundWeight(stored.Weight) != RoundWeight(declared.Weight))
            changes.Add("weight");
        if (ToMicroseconds(stored.ValidFrom) != ToMicroseconds(declared.ValidFrom))
            changes.Add("validFrom");
        if (ToMicroseconds(stored.ValidTo) != ToMicroseconds(declared.ValidTo))
            changes.Add("validTo");
        if (!string.Equals(stored.Notes, declared.Notes, StringComparison.Ordinal))
            changes.Add("notes");
        return changes;
    }

    /// <summary>The link as a governance payload records it.</summary>
    public static object Describe(SecurityUnderlyingLinkDto link) => new
    {
        linkId = link.LinkId,
        securityId = link.SecurityId,
        role = link.Role.ToString(),
        underlyingSecurityId = link.UnderlyingSecurityId,
        reference = link.Reference,
        weight = link.Weight,
        validFrom = link.ValidFrom,
        validTo = link.ValidTo,
        notes = link.Notes,
        declaredBy = link.CreatedBy,
        declaredAt = link.CreatedAt
    };

    private static decimal? RoundWeight(decimal? weight)
        => weight is { } value ? decimal.Round(value, 10, MidpointRounding.AwayFromZero) : null;

    // Npgsql writes timestamptz as whole microseconds from the PostgreSQL epoch, truncating toward
    // it, so the comparison counts from the same epoch (an instant before 2000 truncates upward).
    private static readonly long PostgresEpochTicks = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static long? ToMicroseconds(DateTimeOffset? value)
        => value is { } instant ? (instant.UtcTicks - PostgresEpochTicks) / TimeSpan.TicksPerMicrosecond : null;

    /// <summary>
    /// Replaces the derived links of <paramref name="record"/> with those its terms state now.
    /// Declared links are untouched. Runs inside the projection transaction so the graph and the
    /// projection commit together.
    /// </summary>
    public static async Task ReplaceDerivedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schema,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {schema}.security_underlying_links where security_id = @security_id and source = 'Derived';";
            delete.Parameters.AddWithValue("security_id", record.SecurityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var link in SecurityUnderlyingLinkDeriver.Derive(record))
        {
            await UpsertAsync(connection, transaction, schema, link, ct).ConfigureAwait(false);
        }
    }

    public static async Task UpsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string schema,
        SecurityUnderlyingLinkDto link,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {schema}.security_underlying_links (
                link_id, security_id, role, underlying_security_id, reference_text, weight,
                valid_from, valid_to, source, notes, created_by, created_at)
            values (
                @link_id, @security_id, @role, @underlying_security_id, @reference_text, @weight,
                @valid_from, @valid_to, @source, @notes, @created_by, @created_at)
            on conflict (link_id) do update set
                weight = excluded.weight,
                valid_from = excluded.valid_from,
                valid_to = excluded.valid_to,
                notes = excluded.notes,
                created_by = excluded.created_by,
                created_at = excluded.created_at;
            """;
        command.Parameters.AddWithValue("link_id", link.LinkId);
        command.Parameters.AddWithValue("security_id", link.SecurityId);
        command.Parameters.AddWithValue("role", link.Role.ToString());
        command.Parameters.AddWithValue("underlying_security_id", (object?)link.UnderlyingSecurityId ?? DBNull.Value);
        command.Parameters.AddWithValue("reference_text", (object?)link.Reference ?? DBNull.Value);
        command.Parameters.AddWithValue("weight", (object?)link.Weight ?? DBNull.Value);
        command.Parameters.AddWithValue("valid_from", link.ValidFrom.UtcDateTime);
        command.Parameters.AddWithValue("valid_to", (object?)link.ValidTo?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("source", link.Source.ToString());
        command.Parameters.AddWithValue("notes", (object?)link.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by", link.CreatedBy);
        command.Parameters.AddWithValue("created_at", link.CreatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<SecurityUnderlyingLinkDto>> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string schema,
        string whereClause,
        Guid id,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {Columns}
            from {schema}.security_underlying_links
            {whereClause}
            order by source, role, valid_from, link_id;
            """;
        command.Parameters.AddWithValue("id", id);

        var links = new List<SecurityUnderlyingLinkDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            links.Add(ReadLink(reader));
        }

        return links;
    }

    /// <summary>Reads one link from a row selected or returned with <see cref="Columns"/>.</summary>
    public static SecurityUnderlyingLinkDto ReadLink(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            Enum.Parse<SecurityUnderlyingRole>(reader.GetString(2)),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)),
            reader.IsDBNull(7) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)),
            Enum.Parse<SecurityUnderlyingLinkSource>(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc)));
}
