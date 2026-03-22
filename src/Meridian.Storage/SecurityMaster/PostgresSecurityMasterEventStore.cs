using System.Data;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSecurityMasterEventStore : ISecurityMasterEventStore
{
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<PostgresSecurityMasterEventStore> _logger;

    public PostgresSecurityMasterEventStore(
        SecurityMasterOptions options,
        ILogger<PostgresSecurityMasterEventStore>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<PostgresSecurityMasterEventStore>.Instance;
    }

    public async Task AppendAsync(Guid securityId, long expectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
            return;

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        var currentVersion = await LoadCurrentVersionAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
        if (currentVersion != expectedVersion)
        {
            throw new InvalidOperationException(
                $"Security stream version conflict for {securityId}. Expected {expectedVersion}, actual {currentVersion}.");
        }

        foreach (var @event in events)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("security_events")} (
                    security_id,
                    stream_version,
                    event_type,
                    event_timestamp,
                    actor,
                    correlation_id,
                    causation_id,
                    payload,
                    metadata)
                values (
                    @security_id,
                    @stream_version,
                    @event_type,
                    @event_timestamp,
                    @actor,
                    @correlation_id,
                    @causation_id,
                    @payload::jsonb,
                    @metadata::jsonb);
                """;

            command.Parameters.AddWithValue("security_id", @event.SecurityId);
            command.Parameters.AddWithValue("stream_version", @event.StreamVersion);
            command.Parameters.AddWithValue("event_type", @event.EventType);
            command.Parameters.AddWithValue("event_timestamp", @event.EventTimestamp.UtcDateTime);
            command.Parameters.AddWithValue("actor", @event.Actor);
            command.Parameters.AddWithValue("correlation_id", (object?)@event.CorrelationId ?? DBNull.Value);
            command.Parameters.AddWithValue("causation_id", (object?)@event.CausationId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "payload",
                JsonSerializer.Serialize(@event.Payload, SecurityMasterJsonContext.HighPerformanceOptions));
            command.Parameters.AddWithValue(
                "metadata",
                JsonSerializer.Serialize(@event.Metadata, SecurityMasterJsonContext.HighPerformanceOptions));

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Appended {EventCount} security events for {SecurityId}", events.Count, securityId);
    }

    public async Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select global_sequence,
                   security_id,
                   stream_version,
                   event_type,
                   event_timestamp,
                   actor,
                   correlation_id,
                   causation_id,
                   payload::text,
                   metadata::text
            from {Qualified("security_events")}
            where security_id = @security_id
            order by stream_version;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        return await ReadEventsAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadSinceSequenceAsync(long sequenceExclusive, int take, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select global_sequence,
                   security_id,
                   stream_version,
                   event_type,
                   event_timestamp,
                   actor,
                   correlation_id,
                   causation_id,
                   payload::text,
                   metadata::text
            from {Qualified("security_events")}
            where global_sequence > @sequence
            order by global_sequence
            limit @take;
            """;
        command.Parameters.AddWithValue("sequence", sequenceExclusive);
        command.Parameters.AddWithValue("take", take);

        return await ReadEventsAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<long> GetLatestSequenceAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select coalesce(max(global_sequence), 0) from {Qualified("security_events")};";
        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
    }

    private async Task<long> LoadCurrentVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select coalesce((
                select stream_version
                from {Qualified("security_events")}
                where security_id = @security_id
                order by stream_version desc
                limit 1
                for update), 0);
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
    }

    private async Task<IReadOnlyList<SecurityMasterEventEnvelope>> ReadEventsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<SecurityMasterEventEnvelope>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new SecurityMasterEventEnvelope(
                reader.IsDBNull(0) ? null : reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTime>(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                JsonDocument.Parse(reader.GetString(8)).RootElement.Clone(),
                JsonDocument.Parse(reader.GetString(9)).RootElement.Clone()));
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

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
