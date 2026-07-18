using System.Data;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed partial class PostgresAssetOperationsProjectionStore
{
    public async Task<InstrumentPositionProjectionSnapshot> GetSecurityAsync(
        Guid securityId,
        CancellationToken ct = default)
    {
        if (securityId == Guid.Empty)
        {
            throw new ArgumentException("Security identity cannot be empty.", nameof(securityId));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);
        var snapshot = await ReadInstrumentSnapshotAsync(connection, transaction, securityId, null, null, ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<InstrumentPositionProjectionSnapshot> GetAsOfAsync(
        Guid securityId,
        Guid ledgerBookId,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        if (securityId == Guid.Empty)
        {
            throw new ArgumentException("Security identity cannot be empty.", nameof(securityId));
        }

        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger-book identity cannot be empty.", nameof(ledgerBookId));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);
        var snapshot = await ReadInstrumentSnapshotAsync(
                connection,
                transaction,
                securityId,
                ledgerBookId,
                asOfDate,
                ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<BookPositionDto?> GetBookPositionAsync(
        Guid positionId,
        CancellationToken ct = default)
    {
        if (positionId == Guid.Empty)
        {
            throw new ArgumentException("Book-position identity cannot be empty.", nameof(positionId));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);
        var position = await ReadBookPositionAsync(connection, transaction, positionId, false, ct)
            .ConfigureAwait(false);
        if (position is null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }

        var states = await ReadPositionStatesAsync(connection, transaction, positionId, null, ct)
            .ConfigureAwait(false);
        var hydrated = HydratePosition(position, states);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return hydrated;
    }

    public async Task<BookPositionDto> UpsertAsync(
        InstrumentRoleDto role,
        BookPositionDto position,
        PositionEconomicStateDto? economicState,
        long expectedVersion,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        var normalized = InstrumentPositionProjectionRules.NormalizeWrite(position, economicState);
        var normalizedPosition = normalized.Position;
        var state = normalized.EconomicState;
        if (state is not null && state.Version != normalizedPosition.Version)
        {
            throw new InvalidOperationException(
                "A dedicated economic-state write must use the same version as its book position.");
        }
        InstrumentPositionProjectionRules.ValidateWrite(
            role,
            normalizedPosition,
            state,
            expectedVersion,
            approval);
        InstrumentPositionProjectionRules.ValidateDedicatedProvenance(role, normalizedPosition, state);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        await AcquirePositionScopeLockAsync(connection, transaction, normalizedPosition, ct).ConfigureAwait(false);
        var lineage = state?.ProjectionLineage ?? normalizedPosition.ProjectionLineage;
        if (lineage is not null)
        {
            await AcquireProjectionRunLockAsync(connection, transaction, lineage.ProjectionRunId, ct)
                .ConfigureAwait(false);
            await ValidateProjectionLineageAppendAsync(connection, transaction, lineage, ct)
                .ConfigureAwait(false);
        }

        var persistedRole = await ReadInstrumentRoleAsync(connection, transaction, role.RoleId, true, ct)
            .ConfigureAwait(false);
        var persistedPosition = await ReadBookPositionAsync(
                connection,
                transaction,
                normalizedPosition.PositionId,
                true,
                ct)
            .ConfigureAwait(false);
        var persistedState = state is null
            ? null
            : await ReadEconomicStateAsync(connection, transaction, state.EconomicStateId, true, ct)
                .ConfigureAwait(false);

        var roleReplay = persistedRole is not null && PayloadEquals(persistedRole, role);
        var positionReplay = persistedPosition is not null && PayloadEquals(persistedPosition, normalizedPosition);
        var stateReplay = state is null || persistedState is not null && PayloadEquals(persistedState, state);
        if (expectedVersion == normalizedPosition.Version - 1 && roleReplay && positionReplay && stateReplay)
        {
            var replayStates = await ReadPositionStatesAsync(
                    connection,
                    transaction,
                    normalizedPosition.PositionId,
                    null,
                    ct)
                .ConfigureAwait(false);
            var replay = HydratePosition(persistedPosition!, replayStates);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay;
        }

        ValidateRoleTransition(persistedRole, role);
        ValidatePositionTransition(persistedPosition, normalizedPosition, expectedVersion);
        ValidatePositionIdentity(persistedPosition, normalizedPosition);
        await EnsureRoleCoversPositionsAsync(
            connection,
            transaction,
            role,
            normalizedPosition,
            ct).ConfigureAwait(false);
        await EnsureStatesRemainWithinPositionAsync(
            connection,
            transaction,
            normalizedPosition,
            ct).ConfigureAwait(false);
        await EnsureNoOverlapAsync(connection, transaction, normalizedPosition, ct).ConfigureAwait(false);
        await ValidateEconomicStateAppendAsync(
            connection,
            transaction,
            normalizedPosition,
            state,
            persistedState,
            ct).ConfigureAwait(false);

        await UpsertInstrumentRoleAsync(connection, transaction, normalizedPosition.SecurityId, role, approval, ct)
            .ConfigureAwait(false);
        await UpsertBookPositionAsync(
                connection,
                transaction,
                normalizedPosition.SecurityId,
                normalizedPosition,
                approval,
                ct)
            .ConfigureAwait(false);
        if (state is not null && persistedState is null)
        {
            await AppendEconomicStateAsync(
                    connection,
                    transaction,
                    normalizedPosition.SecurityId,
                    normalizedPosition,
                    state,
                    approval,
                    ct)
                .ConfigureAwait(false);
        }

        var retainedStates = await ReadPositionStatesAsync(
                connection,
                transaction,
                normalizedPosition.PositionId,
                null,
                ct)
            .ConfigureAwait(false);
        var result = HydratePosition(normalizedPosition, retainedStates);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private async Task<InstrumentPositionProjectionSnapshot> ReadInstrumentSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid securityId,
        Guid? ledgerBookId,
        DateOnly? asOfDate,
        CancellationToken ct)
    {
        var roles = await ReadTypedListAsync<InstrumentRoleDto>(
            connection,
            transaction,
            "instrument_role_projections",
            "effective_from, version, role_id",
            securityId,
            ct).ConfigureAwait(false);
        var candidatePositions = await ReadBookPositionsAsync(
                connection,
                transaction,
                securityId,
                ledgerBookId,
                asOfDate,
                ct)
            .ConfigureAwait(false);

        var effectiveRoles = asOfDate is null
            ? roles
            : roles.Where(role =>
                    candidatePositions.Any(position => position.RoleId == role.RoleId) &&
                    InstrumentPositionProjectionRules.IsActive(role.EffectiveFrom, role.EffectiveTo, asOfDate.Value))
                .ToArray();
        var effectiveRoleIds = effectiveRoles.Select(static role => role.RoleId).ToHashSet();
        var positions = asOfDate is null
            ? candidatePositions
            : candidatePositions.Where(position => effectiveRoleIds.Contains(position.RoleId)).ToArray();
        var positionIds = positions.Select(static position => position.PositionId).ToArray();
        var states = await ReadSnapshotStatesAsync(
                connection,
                transaction,
                securityId,
                ledgerBookId,
                positionIds,
                asOfDate,
                ct)
            .ConfigureAwait(false);
        var hydratedPositions = positions
            .Select(position => HydratePosition(
                position,
                states.Where(state => state.PositionId == position.PositionId),
                asOfDate))
            .ToArray();
        var lineages = BuildLineageHistory(hydratedPositions, states);

        return new InstrumentPositionProjectionSnapshot(
            securityId,
            effectiveRoles,
            hydratedPositions,
            states,
            lineages)
        {
            LedgerBookId = ledgerBookId,
            AsOfDate = asOfDate
        };
    }

    private async Task<IReadOnlyList<BookPositionDto>> ReadBookPositionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid securityId,
        Guid? ledgerBookId,
        DateOnly? asOfDate,
        CancellationToken ct)
    {
        var where = new StringBuilder("security_id = @security_id");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("security_id", securityId);
        if (ledgerBookId is Guid bookId)
        {
            where.Append(" and ledger_book_id = @ledger_book_id");
            command.Parameters.AddWithValue("ledger_book_id", bookId);
        }

        if (asOfDate is DateOnly asOf)
        {
            where.Append(" and effective_from <= @as_of and (effective_to is null or effective_to >= @as_of)");
            command.Parameters.AddWithValue("as_of", asOf);
        }

        command.CommandText =
            $"select payload::text from {Qualified("book_position_projections")} where {where} " +
            "order by effective_from, version, position_id;";
        return await ReadPayloadsAsync<BookPositionDto>(command, ct).ConfigureAwait(false);
    }

    private async Task<InstrumentRoleDto?> ReadInstrumentRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid roleId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select payload::text from {Qualified("instrument_role_projections")} where role_id = @id" +
            (forUpdate ? " for update;" : ";");
        command.Parameters.AddWithValue("id", roleId);
        return await ReadPayloadAsync<InstrumentRoleDto>(command, ct).ConfigureAwait(false);
    }

    private async Task<BookPositionDto?> ReadBookPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid positionId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select payload::text from {Qualified("book_position_projections")} where position_id = @id" +
            (forUpdate ? " for update;" : ";");
        command.Parameters.AddWithValue("id", positionId);
        return await ReadPayloadAsync<BookPositionDto>(command, ct).ConfigureAwait(false);
    }

    private async Task<PositionEconomicStateDto?> ReadEconomicStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid economicStateId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select payload::text from {Qualified("position_economic_state_projections")} where economic_state_id = @id" +
            (forUpdate ? " for update;" : ";");
        command.Parameters.AddWithValue("id", economicStateId);
        return await ReadPayloadAsync<PositionEconomicStateDto>(command, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PositionEconomicStateDto>> ReadPositionStatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid positionId,
        DateOnly? asOfDate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select payload::text from {Qualified("position_economic_state_projections")} " +
            "where position_id = @position_id" +
            (asOfDate is null ? string.Empty : " and as_of_date <= @as_of") +
            " order by as_of_date, version, economic_state_id;";
        command.Parameters.AddWithValue("position_id", positionId);
        if (asOfDate is DateOnly asOf)
        {
            command.Parameters.AddWithValue("as_of", asOf);
        }

        return await ReadPayloadsAsync<PositionEconomicStateDto>(command, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PositionEconomicStateDto>> ReadSnapshotStatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid securityId,
        Guid? ledgerBookId,
        IReadOnlyList<Guid> positionIds,
        DateOnly? asOfDate,
        CancellationToken ct)
    {
        if (positionIds.Count == 0)
        {
            return [];
        }

        var where = new StringBuilder("security_id = @security_id and position_id = any(@position_ids)");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("position_ids", positionIds.ToArray());
        if (ledgerBookId is Guid bookId)
        {
            where.Append(" and ledger_book_id = @ledger_book_id");
            command.Parameters.AddWithValue("ledger_book_id", bookId);
        }

        if (asOfDate is DateOnly asOf)
        {
            where.Append(" and as_of_date <= @as_of");
            command.Parameters.AddWithValue("as_of", asOf);
        }

        command.CommandText =
            $"select payload::text from {Qualified("position_economic_state_projections")} " +
            $"where {where} order by as_of_date, version, economic_state_id;";
        return await ReadPayloadsAsync<PositionEconomicStateDto>(command, ct).ConfigureAwait(false);
    }

    private async Task ValidateEconomicStateAppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BookPositionDto position,
        PositionEconomicStateDto? state,
        PositionEconomicStateDto? persistedState,
        CancellationToken ct)
    {
        if (state is null)
        {
            return;
        }

        if (persistedState is not null && !PayloadEquals(persistedState, state))
        {
            throw new InvalidOperationException(
                $"Economic state '{state.EconomicStateId:D}' is append-only and cannot be replaced.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select economic_state_id from {Qualified("position_economic_state_projections")} " +
            "where position_id = @position_id and version = @version for update;";
        command.Parameters.AddWithValue("position_id", position.PositionId);
        command.Parameters.AddWithValue("version", state.Version);
        var existingId = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (existingId is Guid existingStateId && existingStateId != state.EconomicStateId)
        {
            throw new InvalidOperationException(
                $"Book position '{position.PositionId:D}' already has economic state version {state.Version}.");
        }
    }

    private async Task EnsureRoleCoversPositionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InstrumentRoleDto role,
        BookPositionDto? replacement,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select payload::text from {Qualified("book_position_projections")} " +
            "where role_id = @role_id for update;";
        command.Parameters.AddWithValue("role_id", role.RoleId);
        var persisted = await ReadPayloadsAsync<BookPositionDto>(command, ct).ConfigureAwait(false);
        var positions = persisted
            .Where(position => replacement is null || position.PositionId != replacement.PositionId)
            .Concat(replacement is null ? [] : [replacement]);
        foreach (var position in positions)
        {
            InstrumentPositionProjectionRules.ValidateRoleWindow(role, position);
        }
    }

    private async Task EnsureStatesRemainWithinPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BookPositionDto position,
        CancellationToken ct)
    {
        var states = await ReadPositionStatesAsync(
                connection,
                transaction,
                position.PositionId,
                null,
                ct)
            .ConfigureAwait(false);
        foreach (var state in states)
        {
            InstrumentPositionProjectionRules.ValidateEconomicState(position, state);
        }
    }

    private async Task EnsureNoOverlapAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BookPositionDto position,
        CancellationToken ct)
    {
        if (!InstrumentPositionProjectionRules.ParticipatesInActiveOverlap(position.Status))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select position_id
            from {Qualified("book_position_projections")}
            where position_id <> @position_id
              and security_id = @security_id
              and role_id = @role_id
              and ledger_book_id = @ledger_book_id
              and lower(owner_scope_id) = lower(@owner_scope_id)
              and lower(owner_scope_kind) = lower(@owner_scope_kind)
              and lower(position_side) = lower(@position_side)
              and lower(position_status) not in ('closed', 'inactive', 'terminated', 'matured')
              and effective_from <= @effective_to
              and coalesce(effective_to, 'infinity'::date) >= @effective_from
            limit 1
            for update;
            """;
        command.Parameters.AddWithValue("position_id", position.PositionId);
        command.Parameters.AddWithValue("security_id", position.SecurityId);
        command.Parameters.AddWithValue("role_id", position.RoleId);
        command.Parameters.AddWithValue("ledger_book_id", position.BookContext.LedgerBookId);
        command.Parameters.AddWithValue("owner_scope_id", position.BookContext.FundProfileId);
        command.Parameters.AddWithValue("owner_scope_kind", position.BookContext.FundStructureNodeKind.ToString());
        command.Parameters.AddWithValue("position_side", position.PositionSide);
        command.Parameters.AddWithValue("effective_from", position.EffectiveFrom);
        command.Parameters.AddWithValue("effective_to", position.EffectiveTo ?? DateOnly.MaxValue);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is Guid overlapId)
        {
            throw new InvalidOperationException(
                $"Book position '{position.PositionId:D}' overlaps active position '{overlapId:D}' in the same security/book/owner/role scope.");
        }
    }

    private static void ValidateRoleTransition(InstrumentRoleDto? persisted, InstrumentRoleDto incoming)
    {
        if (persisted is null)
        {
            return;
        }

        if (persisted.SecurityId != incoming.SecurityId ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.OwnerScopeId, incoming.OwnerScopeId) ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.OwnerScopeKind, incoming.OwnerScopeKind) ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.RoleKind, incoming.RoleKind))
        {
            throw new InvalidOperationException("Instrument role identity and owner scope are immutable.");
        }

        if (!PayloadEquals(persisted, incoming) && incoming.Version != persisted.Version + 1)
        {
            throw new InvalidOperationException(
                $"Instrument role '{incoming.RoleId:D}' expected next version {persisted.Version + 1}.");
        }
    }

    private static void ValidateCompatibilityRoleTransition(
        InstrumentRoleDto? persisted,
        InstrumentRoleDto incoming)
    {
        if (persisted is null)
        {
            return;
        }

        if (persisted.SecurityId != incoming.SecurityId ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.OwnerScopeId, incoming.OwnerScopeId) ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.OwnerScopeKind, incoming.OwnerScopeKind) ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.RoleKind, incoming.RoleKind))
        {
            throw new InvalidOperationException("Instrument role identity and owner scope are immutable.");
        }

        if (!PayloadEquals(persisted, incoming) && incoming.Version <= persisted.Version)
        {
            throw new InvalidOperationException(
                $"Instrument role '{incoming.RoleId:D}' requires a strictly newer compatibility version.");
        }
    }

    private static void ValidatePositionTransition(
        BookPositionDto? persisted,
        BookPositionDto incoming,
        long expectedVersion)
    {
        if (persisted is null)
        {
            if (expectedVersion != 0 || incoming.Version != 1)
            {
                throw new InvalidOperationException("New book positions require ExpectedVersion 0 and Version 1.");
            }

            return;
        }

        if (persisted.Version != expectedVersion || incoming.Version != expectedVersion + 1)
        {
            throw new InvalidOperationException(
                $"Book position '{incoming.PositionId:D}' is stale. Persisted version is {persisted.Version}; expected {expectedVersion}.");
        }
    }

    private static void ValidateCompatibilityPositionTransition(
        BookPositionDto? persisted,
        BookPositionDto incoming)
    {
        if (persisted is null || PayloadEquals(persisted, incoming))
        {
            return;
        }

        if (incoming.Version <= persisted.Version)
        {
            throw new InvalidOperationException(
                $"Book position '{incoming.PositionId:D}' is stale; expected next version {persisted.Version + 1}.");
        }
    }

    private static void ValidateCompatibilityStateVersion(
        BookPositionDto position,
        PositionEconomicStateDto state)
    {
        if (state.Version != position.Version && state.Version != checked(position.Version + 1))
        {
            throw new InvalidOperationException(
                "Compatibility economic-state writes must match the position version or its immediate successor.");
        }
    }

    private static void ValidatePositionIdentity(BookPositionDto? persisted, BookPositionDto incoming)
    {
        if (persisted is null)
        {
            return;
        }

        if (persisted.SecurityId != incoming.SecurityId ||
            persisted.RoleId != incoming.RoleId ||
            persisted.BookContext.LedgerBookId != incoming.BookContext.LedgerBookId ||
            persisted.BookContext.FundStructureNodeId != incoming.BookContext.FundStructureNodeId ||
            !InstrumentPositionProjectionRules.ExactTextEquals(
                persisted.BookContext.FundProfileId,
                incoming.BookContext.FundProfileId) ||
            persisted.BookContext.FundStructureNodeKind != incoming.BookContext.FundStructureNodeKind ||
            !InstrumentPositionProjectionRules.ExactTextEquals(persisted.PositionSide, incoming.PositionSide))
        {
            throw new InvalidOperationException("Book-position security, role, book, owner scope, and side are immutable.");
        }
    }

    private static BookPositionDto HydratePosition(
        BookPositionDto position,
        IEnumerable<PositionEconomicStateDto> states,
        DateOnly? asOfDate = null)
    {
        var current = states
            .OrderByDescending(static state => state.AsOfDate)
            .ThenByDescending(static state => state.Version)
            .ThenByDescending(static state => state.EconomicStateId)
            .FirstOrDefault();
        var positionLineage = position.ProjectionLineage;
        if (asOfDate is DateOnly cutoff && positionLineage?.ProjectionAsOfDate > cutoff)
        {
            positionLineage = null;
        }

        return position with
        {
            CurrentEconomicState = current,
            ProjectionLineage = current?.ProjectionLineage ?? positionLineage
        };
    }

    private static IReadOnlyList<ProjectionLineageDto> BuildLineageHistory(
        IReadOnlyList<BookPositionDto> positions,
        IReadOnlyList<PositionEconomicStateDto> states)
        => positions
            .Select(static position => position.ProjectionLineage)
            .Concat(states.Select(static state => state.ProjectionLineage))
            .Where(static lineage => lineage is not null)
            .Cast<ProjectionLineageDto>()
            .DistinctBy(static lineage => lineage.ProjectionRunId)
            .OrderBy(static lineage => lineage.ProjectionAsOfDate)
            .ThenBy(static lineage => lineage.GeneratedAtUtc)
            .ThenBy(static lineage => lineage.ProjectionRunId)
            .ToArray();

    private static bool PayloadEquals<T>(T left, T right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, JsonOptions),
            JsonSerializer.SerializeToElement(right, JsonOptions));

    private static async Task<T?> ReadPayloadAsync<T>(NpgsqlCommand command, CancellationToken ct)
    {
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return payload is null ? default : JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static async Task<IReadOnlyList<T>> ReadPayloadsAsync<T>(
        NpgsqlCommand command,
        CancellationToken ct)
    {
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions);
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static void AddSourceEventParameters(NpgsqlCommand command, EconomicEventReferenceDto? source)
    {
        command.Parameters.AddWithValue("source_domain", (object?)source?.SourceDomain ?? DBNull.Value);
        command.Parameters.AddWithValue("source_entity_id", (object?)source?.SourceEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("source_content_hash", (object?)source?.SourceContentHash ?? DBNull.Value);
    }

    private static void AddProjectionParameters(NpgsqlCommand command, ProjectionLineageDto? lineage)
    {
        command.Parameters.AddWithValue("projection_run_id", (object?)lineage?.ProjectionRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("projection_event_id", (object?)lineage?.ProjectionEventId ?? DBNull.Value);
    }
}
