using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Shared PostgreSQL controls for canonical corporate-action identity and lineage. Every writer of
/// <c>corporate_actions</c> must acquire the same security-chain lock before reconciling identity,
/// validating a successor, or inserting a row.
/// </summary>
internal static class PostgresCorporateActionCanonicalStore
{
    internal static async Task AcquireSecurityChainLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_scope, 0));";
        command.Parameters.AddWithValue("lock_scope", $"corporate-action-security-chain:{securityId:D}");
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static async Task<CorporateActionDto?> LoadOrReconcileByEconomicIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string qualifiedCorporateActionsTable,
        Guid securityId,
        string economicFingerprint,
        string? lifecycleState,
        Guid? supersedesCorporateActionId,
        CancellationToken ct)
    {
        var normalizedLifecycleState = NormalizeLifecycleState(lifecycleState);
        var matches = new List<CorporateActionDto>(2);
        await using (var command = CreateIdentityCommand(
            connection,
            transaction,
            qualifiedCorporateActionsTable,
            securityId,
            normalizedLifecycleState,
            supersedesCorporateActionId,
            "economic_fingerprint = @economic_fingerprint"))
        {
            command.Parameters.AddWithValue("economic_fingerprint", economicFingerprint);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                matches.Add(ReadCorporateAction(reader));
            }
        }

        var legacyMatches = new List<CorporateActionDto>(2);
        await using (var command = CreateIdentityCommand(
            connection,
            transaction,
            qualifiedCorporateActionsTable,
            securityId,
            normalizedLifecycleState,
            supersedesCorporateActionId,
            "economic_fingerprint is null"))
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var legacyAction = ReadCorporateAction(reader);
                if (string.Equals(
                        CorporateActionEconomicFingerprint.Compute(legacyAction),
                        economicFingerprint,
                        StringComparison.Ordinal))
                {
                    legacyMatches.Add(legacyAction);
                }
            }
        }

        if (matches.Count + legacyMatches.Count > 1)
        {
            throw new CorporateActionSourceConflictException(
                "Multiple canonical corporate actions have the same economic identity; reconcile the legacy duplicates before appending this canonical event.");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (legacyMatches.Count == 0)
        {
            return null;
        }

        var legacyMatch = legacyMatches[0];
        await using var backfill = connection.CreateCommand();
        backfill.Transaction = transaction;
        backfill.CommandText =
            $"""
            update {qualifiedCorporateActionsTable}
            set economic_fingerprint = @economic_fingerprint
            where corp_act_id = @corp_act_id
              and economic_fingerprint is null;
            """;
        backfill.Parameters.AddWithValue("economic_fingerprint", economicFingerprint);
        backfill.Parameters.AddWithValue("corp_act_id", legacyMatch.CorpActId);
        if (await backfill.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new CorporateActionStateConflictException(
                legacyMatch.CorpActId,
                "The matching legacy corporate action changed during fingerprint reconciliation; reload before retrying.");
        }

        return legacyMatch;
    }

    internal static async Task ValidateSuccessorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string qualifiedCorporateActionsTable,
        CorporateActionDto candidate,
        CancellationToken ct)
    {
        if (candidate.SupersedesCorpActId is not { } parentId)
        {
            return;
        }

        Guid parentSecurityId;
        string parentEventType;
        string? parentLifecycle;
        await using (var parentCommand = connection.CreateCommand())
        {
            parentCommand.Transaction = transaction;
            parentCommand.CommandText =
                $"""
                select security_id, event_type, lifecycle_state
                from {qualifiedCorporateActionsTable}
                where corp_act_id = @parent_id
                for update;
                """;
            parentCommand.Parameters.AddWithValue("parent_id", parentId);
            await using var reader = await parentCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new CorporateActionNotFoundException("Superseded canonical corporate action", parentId);
            }

            parentSecurityId = reader.GetGuid(0);
            parentEventType = reader.GetString(1);
            parentLifecycle = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        if (parentSecurityId != candidate.SecurityId)
        {
            throw new CorporateActionSourceConflictException(
                "A canonical corporate-action successor must reference a predecessor on the same security.");
        }

        if (!string.Equals(
                CorporateActionEventTypes.Normalize(parentEventType),
                CorporateActionEventTypes.Normalize(candidate.EventType),
                StringComparison.Ordinal))
        {
            throw new CorporateActionSourceConflictException(
                "A canonical corporate-action successor must retain its predecessor's event type.");
        }

        if (string.Equals(
                NormalizeLifecycleState(parentLifecycle),
                CorporateActionLifecycleStates.Cancelled,
                StringComparison.Ordinal))
        {
            throw new CorporateActionStateConflictException(
                parentId,
                "A cancelled canonical corporate action is terminal and cannot be superseded.");
        }

        if (CorporateActionLifecycleStates.Rank(candidate.LifecycleState)
            < CorporateActionLifecycleStates.Rank(parentLifecycle))
        {
            throw new CorporateActionStateConflictException(
                parentId,
                "A canonical corporate-action successor cannot move the source lifecycle backwards.");
        }

        await using var successorCommand = connection.CreateCommand();
        successorCommand.Transaction = transaction;
        successorCommand.CommandText =
            $"""
            select corp_act_id
            from {qualifiedCorporateActionsTable}
            where supersedes_corp_act_id = @parent_id
              and corp_act_id <> @candidate_id
            limit 1;
            """;
        successorCommand.Parameters.AddWithValue("parent_id", parentId);
        successorCommand.Parameters.AddWithValue("candidate_id", candidate.CorpActId);
        var existingSuccessor = await successorCommand.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (existingSuccessor is Guid successorId)
        {
            throw new CorporateActionStateConflictException(
                parentId,
                $"Canonical corporate action '{parentId:D}' is already superseded by '{successorId:D}'; supersede the chain tip instead.");
        }
    }

    private static NpgsqlCommand CreateIdentityCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string qualifiedCorporateActionsTable,
        Guid securityId,
        string lifecycleState,
        Guid? supersedesCorporateActionId,
        string fingerprintPredicate)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select corp_act_id, security_id, event_type, ex_date, pay_date, dividend_per_share,
                   currency, split_ratio, new_security_id, distribution_ratio,
                   acquirer_security_id, exchange_ratio, subscription_price_per_share,
                   rights_per_share, record_date, lifecycle_state, supersedes_corp_act_id,
                   redemption_price_percent_of_par, payload, payload_schema_version
            from {qualifiedCorporateActionsTable}
            where security_id = @security_id
              and {fingerprintPredicate}
              and coalesce(nullif(lifecycle_state, ''), @confirmed_state) = @lifecycle_state
              and supersedes_corp_act_id is not distinct from @supersedes_corp_act_id
            order by corp_act_id
            for update;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("confirmed_state", CorporateActionLifecycleStates.Confirmed);
        command.Parameters.AddWithValue("lifecycle_state", lifecycleState);
        command.Parameters.Add(new NpgsqlParameter("supersedes_corp_act_id", NpgsqlDbType.Uuid)
        {
            Value = (object?)supersedesCorporateActionId ?? DBNull.Value,
        });
        return command;
    }

    private static CorporateActionDto ReadCorporateAction(NpgsqlDataReader reader)
    {
        var exDate = DateOnly.FromDateTime(reader.GetDateTime(3));
        DateOnly? payDate = reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4));
        DateOnly? recordDate = reader.IsDBNull(14) ? null : DateOnly.FromDateTime(reader.GetDateTime(14));
        JsonElement? payload = null;
        if (!reader.IsDBNull(18))
        {
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(18));
                payload = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Legacy malformed payloads remain readable but cannot falsely match a new digest.
            }
        }

        return new CorporateActionDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            exDate,
            payDate,
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetDecimal(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.IsDBNull(11) ? null : reader.GetDecimal(11),
            reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            reader.IsDBNull(13) ? null : reader.GetDecimal(13),
            recordDate,
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetGuid(16),
            reader.IsDBNull(17) ? null : reader.GetDecimal(17),
            payload,
            reader.GetInt32(19));
    }

    private static string NormalizeLifecycleState(string? lifecycleState) =>
        string.IsNullOrWhiteSpace(lifecycleState)
            ? CorporateActionLifecycleStates.Confirmed
            : lifecycleState.Trim();
}
