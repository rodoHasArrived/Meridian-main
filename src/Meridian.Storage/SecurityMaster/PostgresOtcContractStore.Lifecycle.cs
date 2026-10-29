using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresOtcContractStore
{
    /// <inheritdoc />
    public async Task<OtcContractLifecycleEventDto> AppendLifecycleEventAsync(AppendOtcContractLifecycleRequest request, CancellationToken ct = default)
    {
        var normalized = OtcContractLifecycleRules.Normalize(request);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await OtcAdvisoryLocks.LockAsync(connection, transaction, "otc-lifecycle-event:" + normalized.EventId.ToString("D"), ct).ConfigureAwait(false);
        var retry = (await ReadLifecycleEventsAsync(connection, transaction, null, ct, normalized.EventId).ConfigureAwait(false)).SingleOrDefault();
        if (retry is not null)
        {
            if (!OtcContractLifecycleRules.IsRetry(retry, normalized))
                throw new InvalidOperationException("The lifecycle event id is already retained with different content.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return retry;
        }

        var probe = (await ReadContractsAsync(connection, transaction, "where contract_id = @id", normalized.ContractId, ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new ArgumentException("The lifecycle contract does not exist.");
        OtcContractDto? successorProbe = null;
        if (normalized.Data is OtcNovationData novation)
            successorProbe = (await ReadContractsAsync(connection, transaction, "where contract_id = @id", novation.SuccessorContractId, ct).ConfigureAwait(false)).SingleOrDefault()
                ?? throw new ArgumentException("The novation successor contract does not exist.");
        await OtcAdvisoryLocks.LockNettingSetContractsAsync(connection, transaction,
            successorProbe is null ? [probe.NettingSetId] : [probe.NettingSetId, successorProbe.NettingSetId], ct).ConfigureAwait(false);
        // Read and pin instrument terms before contract row locks, so a concurrent instrument
        // amendment/deletion cannot invalidate the target or reverse the FK row-lock order.
        var instrument = await ReadLifecycleInstrumentAsync(connection, transaction, probe.SecurityId, ct).ConfigureAwait(false);
        // Match the lock order of snapshot writers and exposure/collateral issuance before any row lock.
        var locked = new Dictionary<Guid, OtcContractDto>();
        foreach (var id in (successorProbe is null ? new[] { probe.ContractId } : new[] { probe.ContractId, successorProbe.ContractId }).Order())
            locked[id] = (await ReadContractsAsync(connection, transaction, "where contract_id = @id", id, ct, forUpdate: true).ConfigureAwait(false)).Single();
        var current = locked[probe.ContractId];
        var successor = successorProbe is null ? null : locked[successorProbe.ContractId];
        if (current.NettingSetId != probe.NettingSetId || successor is not null && successor.NettingSetId != successorProbe!.NettingSetId)
            throw new InvalidOperationException("A lifecycle contract moved netting set under the write; reload and retry.");
        var history = await ReadLifecycleEventsAsync(connection, transaction, current.ContractId, ct).ConfigureAwait(false);
        if (history.Count > 0 && OtcContractLifecycleRules.Replay(history[0].Before, history) != current)
            throw new InvalidOperationException("The contract no longer matches its retained lifecycle history.");
        var assetClass = instrument.AssetClass;
        OtcContractRules.RequireBilateralAssetClass(assetClass);
        if (normalized.Data is OtcCreditEventData or OtcAuctionSettlementData
            && !SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsCreditLifecycle)
            throw new ArgumentException("Credit determinations and auction settlement require a CreditDefaultSwap contract.");
        if (successor is not null)
        {
            await using var existingNovation = connection.CreateCommand();
            existingNovation.Transaction = transaction;
            existingNovation.CommandText = $"select 1 from {_options.Schema}.otc_contract_lifecycle_events where event_type = 'Novation' and payload #>> '{{data,successorContractId}}' = @successor;";
            existingNovation.Parameters.AddWithValue("successor", successor.ContractId.ToString("D"));
            if (await existingNovation.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
                throw new ArgumentException("That successor already completes another retained novation.");
        }
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10)); // PostgreSQL timestamps retain microsecond precision.
        var item = OtcContractLifecycleRules.Create(current, normalized, now, history, successor,
            OtcContractLifecycleRules.TargetFor(current, normalized.Data, instrument));
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"""
                update {_options.Schema}.otc_contracts
                set notional = @notional, status = @status, termination_date = @termination,
                    version = @version, updated_by = @actor, updated_at = @recorded
                where contract_id = @contract and version = @expected;
                """;
            update.Parameters.AddWithValue("notional", item.After.Notional);
            update.Parameters.AddWithValue("status", item.After.Status.ToString());
            update.Parameters.AddWithValue("termination", NpgsqlDbType.Date, (object?)item.After.TerminationDate ?? DBNull.Value);
            update.Parameters.AddWithValue("version", item.After.Version);
            update.Parameters.AddWithValue("actor", item.Actor);
            update.Parameters.AddWithValue("recorded", item.RecordedAt.UtcDateTime);
            update.Parameters.AddWithValue("contract", current.ContractId);
            update.Parameters.AddWithValue("expected", normalized.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The lifecycle contract version changed.");
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                insert into {_options.Schema}.otc_contract_lifecycle_events
                    (event_id, contract_id, contract_version, event_type, effective_date, actor, recorded_at, payload_version, payload)
                values (@event, @contract, @version, @kind, @effective, @actor, @recorded, @payload_version, @payload);
                """;
            insert.Parameters.AddWithValue("event", item.EventId);
            insert.Parameters.AddWithValue("contract", current.ContractId);
            insert.Parameters.AddWithValue("version", item.After.Version);
            insert.Parameters.AddWithValue("kind", item.Kind.ToString());
            insert.Parameters.AddWithValue("effective", item.EffectiveDate);
            insert.Parameters.AddWithValue("actor", item.Actor);
            insert.Parameters.AddWithValue("recorded", item.RecordedAt.UtcDateTime);
            insert.Parameters.AddWithValue("payload_version", item.PayloadVersion);
            insert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, OtcContractLifecycleRules.Serialize(item));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return item;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OtcContractLifecycleEventDto>> GetLifecycleEventsAsync(Guid contractId, CancellationToken ct = default)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("A lifecycle history requires its contract id.");
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var rows = await ReadLifecycleEventsAsync(connection, null, contractId, ct).ConfigureAwait(false);
        if (rows.Count > 0)
            _ = OtcContractLifecycleRules.Replay(rows[0].Before, rows);
        return rows;
    }

    private async Task<SecurityDetailDto> ReadLifecycleInstrumentAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select asset_class, common_terms::text, asset_specific_terms::text, version, effective_from
            from {_options.Schema}.securities where security_id = @id for share;
            """;
        command.Parameters.AddWithValue("id", securityId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("The lifecycle instrument is missing.");
        var common = JsonSerializer.Deserialize<JsonElement>(reader.GetString(1));
        return new(securityId, reader.GetString(0), SecurityStatusDto.Active,
            SecurityTermReader.ReadString(common, "displayName") ?? "Lifecycle instrument",
            SecurityTermReader.ReadString(common, "currency") ?? "", common,
            JsonSerializer.Deserialize<JsonElement>(reader.GetString(2)), [], [], reader.GetInt64(3),
            Utc(reader.GetDateTime(4)), null);
    }

    private async Task<IReadOnlyList<OtcContractLifecycleEventDto>> ReadLifecycleEventsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid? contractId, CancellationToken ct, Guid? eventId = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select event_id, contract_id, contract_version, event_type, effective_date, actor, recorded_at, payload_version, payload::text
            from {_options.Schema}.otc_contract_lifecycle_events
            where {(eventId is not null ? "event_id" : "contract_id")} = @id order by contract_version;
            """;
        command.Parameters.AddWithValue("id", eventId ?? contractId!.Value);
        var rows = new List<OtcContractLifecycleEventDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var item = OtcContractLifecycleRules.Deserialize(reader.GetString(8));
            if (item.EventId != reader.GetGuid(0) || item.Before.ContractId != reader.GetGuid(1)
                || item.After.Version != reader.GetInt64(2) || item.Kind.ToString() != reader.GetString(3)
                || item.EffectiveDate != reader.GetFieldValue<DateOnly>(4) || item.Actor != reader.GetString(5)
                || item.RecordedAt != Utc(reader.GetDateTime(6)) || item.PayloadVersion != reader.GetInt32(7))
                throw new InvalidOperationException("Retained lifecycle metadata contradicts its versioned payload.");
            rows.Add(item);
        }
        return rows;
    }
}
