using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>The governance streams recorded in <c>security_governance_events</c>.</summary>
public static class SecurityGovernanceStreams
{
    public const string OperatorOverrides = "OperatorOverrides";
    public const string Aliases = "Aliases";
    public const string ProjectionRebuild = "ProjectionRebuild";

    /// <summary>Declared underlying-graph links: declaration, amendment and removal (migration 051).</summary>
    public const string UnderlyingGraph = "UnderlyingGraph";

    /// <summary>Stored market curves: definitions and published versions (migration 053). Events carry no security id; the curve id is in the payload.</summary>
    public const string Curves = "Curves";

    /// <summary>OTC exposure snapshots, collateral balances and margin calls (migrations 054 and 055); events name a netting set, not a security.</summary>
    public const string OtcCollateral = "OtcCollateral";
}

/// <summary>One recorded governance event.</summary>
public sealed record SecurityGovernanceEvent(
    long GlobalSequence,
    Guid? SecurityId,
    string Stream,
    string EventType,
    string Actor,
    DateTimeOffset OccurredAt,
    JsonElement Payload,
    int PayloadVersion);

/// <summary>Reads the append-only Security Master governance event log.</summary>
public interface ISecurityGovernanceEventReader
{
    /// <summary>Every governance event recorded for <paramref name="securityId"/>, oldest first.</summary>
    Task<IReadOnlyList<SecurityGovernanceEvent>> GetForSecurityAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Events on one <paramref name="stream"/> with a global sequence after <paramref name="afterSequence"/>,
    /// oldest first, at most <paramref name="limit"/>. This is how streams whose subject is not a
    /// security, such as <see cref="SecurityGovernanceStreams.Curves"/> and
    /// <see cref="SecurityGovernanceStreams.OtcCollateral"/>, are read.
    /// </summary>
    Task<IReadOnlyList<SecurityGovernanceEvent>> GetForStreamAsync(string stream, long afterSequence = 0, int limit = 500, CancellationToken ct = default);
}

/// <summary>
/// Appends to and reads <c>security_governance_events</c>. Writers call
/// <see cref="AppendAsync"/> with the connection and transaction of the state change they record,
/// so the event and the change commit or roll back together; the table rejects updates, deletes
/// and truncation.
/// </summary>
public sealed class PostgresSecurityGovernanceEventLog : ISecurityGovernanceEventReader
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityGovernanceEventLog(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Appends one governance event inside the caller's transaction.</summary>
    public static async Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string schema,
        Guid? securityId,
        string stream,
        string eventType,
        string actor,
        DateTimeOffset occurredAt,
        object payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {schema}.security_governance_events
                (security_id, stream, event_type, actor, occurred_at, payload)
            values
                (@security_id, @stream, @event_type, @actor, @occurred_at, @payload::jsonb);
            """;
        command.Parameters.AddWithValue("security_id", (object?)securityId ?? DBNull.Value);
        command.Parameters.AddWithValue("stream", stream);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("actor", string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim());
        command.Parameters.AddWithValue("occurred_at", occurredAt.UtcDateTime);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityGovernanceEvent>> GetForSecurityAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(_options, ct).ConfigureAwait(false);
        await using var command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText =
            $"""
            select {Columns}
            from {_options.Schema}.security_governance_events
            where security_id = @security_id
            order by global_sequence;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadEventsAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityGovernanceEvent>> GetForStreamAsync(string stream, long afterSequence = 0, int limit = 500, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        if (limit is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "A stream page holds between 1 and 5000 events.");

        await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(_options, ct).ConfigureAwait(false);
        await using var command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        // Served by ix_security_governance_events_stream_sequence (migration 040).
        command.CommandText =
            $"""
            select {Columns}
            from {_options.Schema}.security_governance_events
            where stream = @stream and global_sequence > @after
            order by global_sequence
            limit @limit;
            """;
        command.Parameters.AddWithValue("stream", stream.Trim());
        command.Parameters.AddWithValue("after", afterSequence);
        command.Parameters.AddWithValue("limit", limit);
        return await ReadEventsAsync(command, ct).ConfigureAwait(false);
    }

    private const string Columns = "global_sequence, security_id, stream, event_type, actor, occurred_at, payload::text, payload_version";

    private static async Task<IReadOnlyList<SecurityGovernanceEvent>> ReadEventsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var events = new List<SecurityGovernanceEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            using var payload = JsonDocument.Parse(reader.GetString(6));
            events.Add(new SecurityGovernanceEvent(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
                payload.RootElement.Clone(),
                reader.GetInt32(7)));
        }

        return events;
    }
}
