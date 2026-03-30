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

    public async Task AppendCorporateActionAsync(CorporateActionDto action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("corporate_actions")} (
                corp_act_id,
                security_id,
                event_type,
                ex_date,
                pay_date,
                dividend_per_share,
                currency,
                split_ratio,
                new_security_id,
                distribution_ratio,
                acquirer_security_id,
                exchange_ratio,
                subscription_price_per_share,
                rights_per_share)
            values (
                @corp_act_id,
                @security_id,
                @event_type,
                @ex_date,
                @pay_date,
                @dividend_per_share,
                @currency,
                @split_ratio,
                @new_security_id,
                @distribution_ratio,
                @acquirer_security_id,
                @exchange_ratio,
                @subscription_price_per_share,
                @rights_per_share)
            on conflict (corp_act_id) do nothing;
            """;

        command.Parameters.AddWithValue("corp_act_id", action.CorpActId);
        command.Parameters.AddWithValue("security_id", action.SecurityId);
        command.Parameters.AddWithValue("event_type", action.EventType);
        command.Parameters.AddWithValue("ex_date", action.ExDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("pay_date", (object?)action.PayDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("dividend_per_share", (object?)action.DividendPerShare ?? DBNull.Value);
        command.Parameters.AddWithValue("currency", (object?)action.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue("split_ratio", (object?)action.SplitRatio ?? DBNull.Value);
        command.Parameters.AddWithValue("new_security_id", (object?)action.NewSecurityId ?? DBNull.Value);
        command.Parameters.AddWithValue("distribution_ratio", (object?)action.DistributionRatio ?? DBNull.Value);
        command.Parameters.AddWithValue("acquirer_security_id", (object?)action.AcquirerSecurityId ?? DBNull.Value);
        command.Parameters.AddWithValue("exchange_ratio", (object?)action.ExchangeRatio ?? DBNull.Value);
        command.Parameters.AddWithValue("subscription_price_per_share", (object?)action.SubscriptionPricePerShare ?? DBNull.Value);
        command.Parameters.AddWithValue("rights_per_share", (object?)action.RightsPerShare ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select corp_act_id,
                   security_id,
                   event_type,
                   ex_date,
                   pay_date,
                   dividend_per_share,
                   currency,
                   split_ratio,
                   new_security_id,
                   distribution_ratio,
                   acquirer_security_id,
                   exchange_ratio,
                   subscription_price_per_share,
                   rights_per_share
            from {Qualified("corporate_actions")}
            where security_id = @security_id
            order by ex_date;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<CorporateActionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var exDateRaw = reader.GetDateTime(3);
            var payDateRaw = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
            results.Add(new CorporateActionDto(
                CorpActId: reader.GetGuid(0),
                SecurityId: reader.GetGuid(1),
                EventType: reader.GetString(2),
                ExDate: DateOnly.FromDateTime(exDateRaw),
                PayDate: payDateRaw.HasValue ? DateOnly.FromDateTime(payDateRaw.Value) : null,
                DividendPerShare: reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                Currency: reader.IsDBNull(6) ? null : reader.GetString(6),
                SplitRatio: reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                NewSecurityId: reader.IsDBNull(8) ? null : reader.GetGuid(8),
                DistributionRatio: reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                AcquirerSecurityId: reader.IsDBNull(10) ? null : reader.GetGuid(10),
                ExchangeRatio: reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                SubscriptionPricePerShare: reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                RightsPerShare: reader.IsDBNull(13) ? null : reader.GetDecimal(13)));
        }

        return results;
    }
}
