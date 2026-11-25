using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>One durable change record: a committed economic event of one security.</summary>
public sealed record SecurityMasterChangeRecord(
    Guid ChangeId,
    Guid SecurityId,
    long StreamVersion,
    string EventType,
    string Actor,
    DateTimeOffset RecordedAt,
    string? RestatementDecisionJson,
    DateTimeOffset? ProcessedAt);

/// <summary>
/// Review A4 / P4: the Security Master change outbox (migration 072). Every economic event append
/// writes a change record in the same transaction, so every write origin produces exactly one record
/// per event. Consumers claim with <c>FOR UPDATE SKIP LOCKED</c>, which cannot skip a late-committing
/// event the way a <c>global_sequence &gt; cursor</c> reader can.
/// </summary>
public interface ISecurityMasterChangeOutbox
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> unprocessed records for <paramref name="consumer"/>.
    /// A claim older than <paramref name="claimTimeout"/> is treated as abandoned and reclaimable.
    /// </summary>
    Task<IReadOnlyList<SecurityMasterChangeRecord>> ClaimAsync(string consumer, int batchSize, TimeSpan claimTimeout, CancellationToken ct = default);

    /// <summary>Marks claimed records processed; only rows still claimed by <paramref name="consumer"/> change.</summary>
    Task<int> MarkProcessedAsync(string consumer, IReadOnlyCollection<Guid> changeIds, CancellationToken ct = default);

    /// <summary>Persists the restatement routing a publish computed for the change at <paramref name="streamVersion"/>.</summary>
    Task<bool> RecordRestatementDecisionAsync(Guid securityId, long streamVersion, string decisionJson, CancellationToken ct = default);

    /// <summary>The change records of one security, oldest first.</summary>
    Task<IReadOnlyList<SecurityMasterChangeRecord>> ListAsync(Guid securityId, CancellationToken ct = default);
}

/// <summary>PostgreSQL change outbox.</summary>
public sealed class PostgresSecurityMasterChangeOutbox : ISecurityMasterChangeOutbox
{
    private const string Columns =
        "change_id, security_id, stream_version, event_type, actor, recorded_at, restatement_decision::text, processed_at";

    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterChangeOutbox(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private string Table => $"{_options.Schema}.security_master_change_outbox";

    /// <summary>Inserts the change record for one appended event inside the append's transaction.</summary>
    internal static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schema,
        SecurityMasterEventEnvelope @event,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {schema}.security_master_change_outbox (change_id, security_id, stream_version, event_type, actor)
            values (@change_id, @security_id, @stream_version, @event_type, @actor)
            on conflict (security_id, stream_version) do nothing;
            """;
        command.Parameters.AddWithValue("change_id", Guid.NewGuid());
        command.Parameters.AddWithValue("security_id", @event.SecurityId);
        command.Parameters.AddWithValue("stream_version", @event.StreamVersion);
        command.Parameters.AddWithValue("event_type", @event.EventType);
        command.Parameters.AddWithValue("actor", @event.Actor);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityMasterChangeRecord>> ClaimAsync(string consumer, int batchSize, TimeSpan claimTimeout, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            with candidates as (
                select change_id
                from {Table}
                where processed_at is null
                  and (claimed_at is null or claimed_at < now() - @claim_timeout)
                order by recorded_at, change_id
                limit @batch_size
                for update skip locked
            )
            update {Table} o
            set claimed_by = @consumer, claimed_at = now()
            from candidates c
            where o.change_id = c.change_id
            returning o.change_id, o.security_id, o.stream_version, o.event_type, o.actor, o.recorded_at,
                      o.restatement_decision::text, o.processed_at;
            """;
        command.Parameters.AddWithValue("consumer", consumer);
        command.Parameters.AddWithValue("batch_size", batchSize);
        command.Parameters.Add(new NpgsqlParameter("claim_timeout", NpgsqlDbType.Interval) { Value = claimTimeout });
        var rows = await ReadAsync(command, ct).ConfigureAwait(false);
        return rows.OrderBy(static row => row.RecordedAt).ThenBy(static row => row.SecurityId).ThenBy(static row => row.StreamVersion).ToArray();
    }

    public async Task<int> MarkProcessedAsync(string consumer, IReadOnlyCollection<Guid> changeIds, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        if (changeIds.Count == 0)
        {
            return 0;
        }

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            update {Table}
            set processed_at = now()
            where change_id = any(@ids) and claimed_by = @consumer and processed_at is null;
            """;
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = changeIds.ToArray() });
        command.Parameters.AddWithValue("consumer", consumer);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> RecordRestatementDecisionAsync(Guid securityId, long streamVersion, string decisionJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionJson);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            update {Table}
            set restatement_decision = @decision
            where security_id = @security_id and stream_version = @stream_version;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("stream_version", streamVersion);
        command.Parameters.Add(new NpgsqlParameter("decision", NpgsqlDbType.Jsonb) { Value = decisionJson });
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<SecurityMasterChangeRecord>> ListAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {Columns} from {Table} where security_id = @security_id order by stream_version;";
        command.Parameters.AddWithValue("security_id", securityId);
        return await ReadAsync(command, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SecurityMasterChangeRecord>> ReadAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var rows = new List<SecurityMasterChangeRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SecurityMasterChangeRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero)));
        }

        return rows;
    }

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
