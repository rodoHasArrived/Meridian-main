using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>The governance streams recorded in <c>security_governance_events</c>.</summary>
public static class SecurityGovernanceStreams
{
    public const string OperatorOverrides = "OperatorOverrides";
    public const string Aliases = "Aliases";
    public const string UnderlyingLinks = "UnderlyingLinks";
    public const string ProjectionRebuild = "ProjectionRebuild";
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
            select global_sequence, security_id, stream, event_type, actor, occurred_at, payload::text, payload_version
            from {_options.Schema}.security_governance_events
            where security_id = @security_id
            order by global_sequence;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

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
