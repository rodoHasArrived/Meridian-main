using System.Diagnostics.CodeAnalysis;
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

    /// <summary>OTC exposure snapshots, collateral balances and margin calls (migrations 054 and 055); events carry no security id and name a netting set (snapshots, calls) or a collateral agreement (balances).</summary>
    public const string OtcCollateral = "OtcCollateral";
}

/// <summary>
/// One recorded governance event. <see cref="Payload"/> is the stored JSON as written;
/// <see cref="ReadPayload{TPayload}"/> reads it through the declared contract for
/// <see cref="Stream"/>/<see cref="EventType"/>, dispatching on <see cref="PayloadVersion"/>.
/// </summary>
public sealed record SecurityGovernanceEvent(
    long GlobalSequence,
    Guid? SecurityId,
    string Stream,
    string EventType,
    string Actor,
    DateTimeOffset OccurredAt,
    JsonElement Payload,
    int PayloadVersion)
{
    /// <summary>
    /// The payload as its declared type. Throws <see cref="SecurityGovernancePayloadContractException"/>
    /// when the event type has no contract on this node, <see cref="SecurityGovernancePayloadVersionException"/>
    /// when <see cref="PayloadVersion"/> has no reader, and <see cref="InvalidOperationException"/> when
    /// <typeparamref name="TPayload"/> is not the declared type.
    /// </summary>
    public TPayload ReadPayload<TPayload>()
        where TPayload : class, ISecurityGovernancePayload
        => SecurityGovernancePayloadContracts.Read<TPayload>(Stream, EventType, PayloadVersion, Payload);

    /// <summary>
    /// Reads the payload when this node declares a contract and a reader for its version and
    /// <typeparamref name="TPayload"/> is the declared type; otherwise returns false and leaves the
    /// raw <see cref="Payload"/> as the only reading. Malformed JSON for a declared version still throws.
    /// </summary>
    public bool TryReadPayload<TPayload>([NotNullWhen(true)] out TPayload? payload)
        where TPayload : class, ISecurityGovernancePayload
    {
        payload = null;
        if (!SecurityGovernancePayloadContracts.TryResolve(Stream, EventType, out var contract)
            || !typeof(TPayload).IsAssignableFrom(contract.PayloadType)
            || !contract.ReadableVersions.Contains(PayloadVersion))
        {
            return false;
        }

        payload = ReadPayload<TPayload>();
        return true;
    }
}

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

    /// <summary>
    /// Appends one governance event inside the caller's transaction. The payload must be the type
    /// declared for <paramref name="stream"/>/<paramref name="eventType"/> in
    /// <see cref="SecurityGovernancePayloadContracts"/>; it is serialized through
    /// <see cref="SecurityGovernanceJsonContext"/>, and its contract's current version is written to
    /// <c>payload_version</c> explicitly rather than left to the column default.
    /// </summary>
    public static async Task AppendAsync<TPayload>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string schema,
        Guid? securityId,
        string stream,
        string eventType,
        string actor,
        DateTimeOffset occurredAt,
        TPayload payload,
        CancellationToken ct)
        where TPayload : class, ISecurityGovernancePayload
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);
        var contract = SecurityGovernancePayloadContracts.ResolveForWrite<TPayload>(stream, eventType);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {schema}.security_governance_events
                (security_id, stream, event_type, actor, occurred_at, payload, payload_version)
            values
                (@security_id, @stream, @event_type, @actor, @occurred_at, @payload::jsonb, @payload_version);
            """;
        command.Parameters.AddWithValue("security_id", (object?)securityId ?? DBNull.Value);
        command.Parameters.AddWithValue("stream", stream);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("actor", string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim());
        command.Parameters.AddWithValue("occurred_at", occurredAt.UtcDateTime);
        command.Parameters.AddWithValue("payload", contract.Serialize(payload));
        command.Parameters.AddWithValue("payload_version", contract.CurrentVersion);
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
