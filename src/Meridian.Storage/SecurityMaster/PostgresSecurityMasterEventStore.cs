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

        // Overlay edits acquire the same transaction-scoped lock before checking the stream head.
        // This makes their expected-version guard atomic with canonical appends without placing
        // partial operator annotations in the replayed economic event stream.
        await LockStreamAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
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

    private static async Task LockStreamAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtext(@security_id::text));";
        command.Parameters.AddWithValue("security_id", securityId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
        var economicFingerprint = CorporateActionEconomicFingerprint.Compute(action);

        const int maximumAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await AppendCorporateActionOnceAsync(action, economicFingerprint, ct).ConfigureAwait(false);
                return;
            }
            catch (PostgresException exception) when (
                (exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
                 || exception.SqlState == PostgresErrorCodes.UniqueViolation)
                && attempt < maximumAttempts)
            {
                ct.ThrowIfCancellationRequested();
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new CorporateActionSourceConflictException(
                    "The canonical corporate action collided with an existing identity or successor; reload the security's canonical chain before retrying.");
            }
            catch (PostgresException exception) when (
                exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    $"Corporate-action append remained contended after {maximumAttempts} serializable attempts.");
            }
            catch (NpgsqlException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action persistence is temporarily unavailable; no canonical event was appended.");
            }
            catch (TimeoutException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action persistence timed out; reload the canonical chain before retrying.");
            }
        }
    }

    private async Task AppendCorporateActionOnceAsync(
        CorporateActionDto action,
        string economicFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await PostgresCorporateActionCanonicalStore.AcquireSecurityChainLockAsync(
            connection, transaction, action.SecurityId, ct).ConfigureAwait(false);

        var existing = await PostgresCorporateActionCanonicalStore.LoadOrReconcileByEconomicIdentityAsync(
                connection,
                transaction,
                Qualified("corporate_actions"),
                action.SecurityId,
                economicFingerprint,
                action.LifecycleState,
                action.SupersedesCorpActId,
                ct)
            .ConfigureAwait(false);
        var canonicalAction = existing ?? action;
        await PostgresCorporateActionCanonicalStore.ValidateSuccessorAsync(
                connection, transaction, Qualified("corporate_actions"), canonicalAction, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await InsertCorporateActionAsync(
                    connection, transaction, action, economicFingerprint, ct)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertCorporateActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionDto action,
        string economicFingerprint,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                rights_per_share,
                record_date,
                lifecycle_state,
                supersedes_corp_act_id,
                redemption_price_percent_of_par,
                payload,
                payload_schema_version,
                economic_fingerprint)
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
                @rights_per_share,
                @record_date,
                @lifecycle_state,
                @supersedes_corp_act_id,
                @redemption_price_percent_of_par,
                @payload,
                @payload_schema_version,
                @economic_fingerprint);
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
        command.Parameters.AddWithValue("record_date", (object?)action.RecordDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("lifecycle_state", (object?)action.LifecycleState ?? DBNull.Value);
        command.Parameters.AddWithValue("supersedes_corp_act_id", (object?)action.SupersedesCorpActId ?? DBNull.Value);
        command.Parameters.AddWithValue("redemption_price_percent_of_par", (object?)action.RedemptionPricePercentOfPar ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlTypes.NpgsqlDbType.Jsonb)
        {
            Value = action.Payload is { ValueKind: not JsonValueKind.Undefined } payload
                ? payload.GetRawText()
                : DBNull.Value,
        });
        command.Parameters.AddWithValue("payload_schema_version", action.PayloadSchemaVersion);
        command.Parameters.AddWithValue("economic_fingerprint", economicFingerprint);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses a stored payload envelope. Read tolerance: a malformed stored document reads as no
    /// payload rather than failing the whole corporate-action history row.
    /// </summary>
    private static JsonElement? ParsePayload(string rawPayload)
    {
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CorporateActionDto ReadCorporateAction(NpgsqlDataReader reader)
    {
        var payDate = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
        var recordDate = reader.IsDBNull(14) ? (DateTime?)null : reader.GetDateTime(14);
        return new CorporateActionDto(
            CorpActId: reader.GetGuid(0),
            SecurityId: reader.GetGuid(1),
            EventType: reader.GetString(2),
            ExDate: DateOnly.FromDateTime(reader.GetDateTime(3)),
            PayDate: payDate.HasValue ? DateOnly.FromDateTime(payDate.Value) : null,
            DividendPerShare: reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            Currency: reader.IsDBNull(6) ? null : reader.GetString(6),
            SplitRatio: reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            NewSecurityId: reader.IsDBNull(8) ? null : reader.GetGuid(8),
            DistributionRatio: reader.IsDBNull(9) ? null : reader.GetDecimal(9),
            AcquirerSecurityId: reader.IsDBNull(10) ? null : reader.GetGuid(10),
            ExchangeRatio: reader.IsDBNull(11) ? null : reader.GetDecimal(11),
            SubscriptionPricePerShare: reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            RightsPerShare: reader.IsDBNull(13) ? null : reader.GetDecimal(13),
            RecordDate: recordDate.HasValue ? DateOnly.FromDateTime(recordDate.Value) : null,
            LifecycleState: reader.IsDBNull(15) ? null : reader.GetString(15),
            SupersedesCorpActId: reader.IsDBNull(16) ? null : reader.GetGuid(16),
            RedemptionPricePercentOfPar: reader.IsDBNull(17) ? null : reader.GetDecimal(17),
            Payload: reader.IsDBNull(18) ? null : ParsePayload(reader.GetString(18)),
            PayloadSchemaVersion: reader.GetInt32(19));
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
                   rights_per_share,
                   record_date,
                   lifecycle_state,
                   supersedes_corp_act_id,
                   redemption_price_percent_of_par,
                   payload,
                   payload_schema_version
            from {Qualified("corporate_actions")}
            where security_id = @security_id
            order by ex_date;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<CorporateActionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadCorporateAction(reader));
        }

        return results;
    }

    public async Task<CorporateActionEventTypeNormalizationResult> NormalizeCorporateActionEventTypesAsync(
        bool apply, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        var counts = new List<(string StoredValue, int RowCount)>();
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText =
                $"""
                select event_type, count(*)
                from {Qualified("corporate_actions")}
                group by event_type
                order by event_type;
                """;
            await using var reader = await countCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                counts.Add((reader.GetString(0), (int)reader.GetInt64(1)));
            }
        }

        var renames = new List<CorporateActionEventTypeRename>();
        var unmapped = new List<CorporateActionEventTypeCount>();
        foreach (var (storedValue, rowCount) in counts)
        {
            var canonical = CorporateActionEventTypes.Normalize(storedValue);
            if (!CorporateActionEventTypes.IsKnown(storedValue))
            {
                unmapped.Add(new CorporateActionEventTypeCount(storedValue, rowCount));
            }
            else if (!string.Equals(canonical, storedValue, StringComparison.Ordinal))
            {
                renames.Add(new CorporateActionEventTypeRename(storedValue, canonical, rowCount));
            }
        }

        if (!apply || renames.Count == 0)
        {
            return new CorporateActionEventTypeNormalizationResult(Applied: false, renames, unmapped);
        }

        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var rename in renames)
        {
            var actions = await LoadCorporateActionsForNormalizationAsync(
                    connection, transaction, rename.StoredValue, ct)
                .ConfigureAwait(false);
            foreach (var action in actions)
            {
                var normalized = action with { EventType = rename.CanonicalName };
                await using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText =
                    $"""
                    update {Qualified("corporate_actions")}
                    set event_type = @canonical,
                        economic_fingerprint = @economic_fingerprint
                    where corp_act_id = @corp_act_id;
                    """;
                updateCommand.Parameters.AddWithValue("canonical", rename.CanonicalName);
                updateCommand.Parameters.AddWithValue(
                    "economic_fingerprint",
                    CorporateActionEconomicFingerprint.Compute(normalized));
                updateCommand.Parameters.AddWithValue("corp_act_id", action.CorpActId);
                await updateCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new CorporateActionEventTypeNormalizationResult(Applied: true, renames, unmapped);
    }

    private async Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsForNormalizationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventType,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select corp_act_id, security_id, event_type, ex_date, pay_date, dividend_per_share,
                   currency, split_ratio, new_security_id, distribution_ratio,
                   acquirer_security_id, exchange_ratio, subscription_price_per_share,
                   rights_per_share, record_date, lifecycle_state, supersedes_corp_act_id,
                   redemption_price_percent_of_par, payload, payload_schema_version
            from {Qualified("corporate_actions")}
            where event_type = @event_type
            order by corp_act_id
            for update;
            """;
        command.Parameters.AddWithValue("event_type", eventType);

        var actions = new List<CorporateActionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            actions.Add(ReadCorporateAction(reader));
        }

        return actions;
    }
}
