using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed class PostgresAssetOperationsProjectionStore : IAssetOperationsProjectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AssetOperationsOptions _options;

    public PostgresAssetOperationsProjectionStore(AssetOperationsOptions options)
    {
        _options = options;
        ValidateIdentifier(_options.Schema, nameof(options.Schema));
    }

    public async Task<AssetOperationsDetailDto?> GetAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        var subject = await ReadSingleAsync<AssetOperationSubjectDto>(
            connection,
            "asset_operation_subjects",
            securityId,
            ct).ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        var instrumentRoles = await ReadTypedListAsync<InstrumentRoleDto>(
            connection,
            "instrument_role_projections",
            "effective_from, version, role_id",
            securityId,
            ct).ConfigureAwait(false);
        var bookPositions = await ReadTypedListAsync<BookPositionDto>(
            connection,
            "book_position_projections",
            "effective_from, version, position_id",
            securityId,
            ct).ConfigureAwait(false);
        var economicStates = await ReadTypedListAsync<PositionEconomicStateDto>(
            connection,
            "position_economic_state_projections",
            "as_of_date, version, economic_state_id",
            securityId,
            ct).ConfigureAwait(false);
        var projectionLineages = bookPositions
            .Select(static position => position.ProjectionLineage)
            .Concat(economicStates.Select(static state => state.ProjectionLineage))
            .Where(static lineage => lineage is not null)
            .Cast<ProjectionLineageDto>()
            .DistinctBy(static lineage => lineage.ProjectionRunId)
            .ToArray();

        return new AssetOperationsDetailDto(
            subject,
            await ReadListAsync<AssetTermsVersionDto>(connection, "asset_terms_versions", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetLifecycleEventDto>(connection, "asset_lifecycle_events", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetCashFlowProjectionRunDto>(connection, "asset_cash_flow_projection_runs", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetProjectedCashFlowDto>(connection, "asset_projected_cash_flows", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetActualActivityDto>(connection, "asset_actual_activity", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetReconciliationRunDto>(connection, "asset_reconciliation_runs", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetReconciliationResultDto>(connection, "asset_reconciliation_results", securityId, ct).ConfigureAwait(false),
            await ReadListAsync<AssetLedgerProjectionDto>(connection, "asset_ledger_projections", securityId, ct).ConfigureAwait(false),
            await ReadSingleAsync<AssetOperationsReadinessDto>(connection, "asset_operations_readiness", securityId, ct).ConfigureAwait(false)
                ?? BuildFallbackReadiness(subject),
            await ReadListAsync<AssetLifecycleEventDto>(connection, "asset_workflow_audit", securityId, ct).ConfigureAwait(false))
        {
            InstrumentRoles = instrumentRoles,
            BookPositions = bookPositions,
            PositionEconomicStates = economicStates,
            ProjectionLineages = projectionLineages
        };
    }

    public async Task UpsertAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(approval);

        if (projection.Subject.SecurityId == Guid.Empty)
        {
            throw new ArgumentException("Asset Operations projections require a non-empty security_id.", nameof(projection));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await DeleteExistingAsync(connection, transaction, projection.Subject.SecurityId, ct).ConfigureAwait(false);
        await InsertAsync(connection, transaction, "asset_operation_subjects", projection.Subject.SecurityId, "SecurityMaster", projection.Subject.SecurityId.ToString("D"), projection.Subject, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_terms_versions", projection.Subject.SecurityId, projection.TermsHistory, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_lifecycle_events", projection.Subject.SecurityId, projection.LifecycleEvents, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_cash_flow_projection_runs", projection.Subject.SecurityId, projection.CashFlowProjectionRuns, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_projected_cash_flows", projection.Subject.SecurityId, projection.ProjectedCashFlows, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_actual_activity", projection.Subject.SecurityId, projection.ActualActivity, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_reconciliation_runs", projection.Subject.SecurityId, projection.ReconciliationRuns, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_reconciliation_results", projection.Subject.SecurityId, projection.ReconciliationResults, approval, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_ledger_projections", projection.Subject.SecurityId, projection.LedgerProjections, approval, ct).ConfigureAwait(false);
        await InsertAsync(connection, transaction, "asset_operations_readiness", projection.Subject.SecurityId, projection.Readiness.SourceDomain, projection.Readiness.SourceEntityId, projection.Readiness, ct).ConfigureAwait(false);
        await InsertManyAsync(connection, transaction, "asset_workflow_audit", projection.Subject.SecurityId, projection.WorkflowAudit, approval, ct).ConfigureAwait(false);

        var normalizedPositions = NormalizeTypedProjections(projection);
        var normalizedStates = NormalizeEconomicStates(projection, normalizedPositions);
        foreach (var role in projection.InstrumentRoles)
        {
            await UpsertInstrumentRoleAsync(connection, transaction, projection.Subject.SecurityId, role, approval, ct).ConfigureAwait(false);
        }

        foreach (var position in normalizedPositions)
        {
            await UpsertBookPositionAsync(connection, transaction, projection.Subject.SecurityId, position, approval, ct).ConfigureAwait(false);
        }

        foreach (var state in normalizedStates)
        {
            await AppendEconomicStateAsync(connection, transaction, projection.Subject.SecurityId, state, approval, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AssetOperationsOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private async Task DeleteExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        foreach (var table in Tables)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"delete from {Qualified(table)} where security_id = @security_id;";
            command.Parameters.AddWithValue("security_id", securityId);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task InsertManyAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        Guid securityId,
        IReadOnlyList<T> rows,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct)
    {
        foreach (var row in rows)
        {
            var source = SourceLineage(row);
            await InsertAsync(connection, transaction, table, securityId, source.SourceDomain, source.SourceEntityId, row, ct).ConfigureAwait(false);
        }
    }

    private async Task InsertAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        Guid securityId,
        string? sourceDomain,
        string? sourceEntityId,
        T payload,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(table)} (id, security_id, source_domain, source_entity_id, payload)
            values (@id, @security_id, @source_domain, @source_entity_id, @payload::jsonb);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("source_domain", (object?)sourceDomain ?? DBNull.Value);
        command.Parameters.AddWithValue("source_entity_id", (object?)sourceEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<T?> ReadSingleAsync<T>(
        NpgsqlConnection connection,
        string table,
        Guid securityId,
        CancellationToken ct)
    {
        var rows = await ReadListAsync<T>(connection, table, securityId, ct).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        NpgsqlConnection connection,
        string table,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select payload::text
            from {Qualified(table)}
            where security_id = @security_id
            order by created_at, id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions);
            if (row is not null)
            {
                results.Add(row);
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<T>> ReadTypedListAsync<T>(
        NpgsqlConnection connection,
        string table,
        string orderBy,
        Guid securityId,
        CancellationToken ct)
    {
        ValidateIdentifier(table, nameof(table));
        if (!TypedOrderings.Contains(orderBy, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unsupported typed projection ordering.", nameof(orderBy));
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select payload::text from {Qualified(table)} where security_id = @security_id order by {orderBy};";
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions);
            if (row is not null)
            {
                results.Add(row);
            }
        }

        return results;
    }

    private IReadOnlyList<BookPositionDto> NormalizeTypedProjections(AssetOperationsProjectionDto projection)
    {
        foreach (var role in projection.InstrumentRoles)
        {
            if (role.RoleId == Guid.Empty || role.SecurityId != projection.Subject.SecurityId || role.Version <= 0)
            {
                throw new InvalidOperationException("Instrument role projections require matching Security Master identity, a non-empty role ID, and a positive version.");
            }

            if (role.EffectiveTo < role.EffectiveFrom)
            {
                throw new InvalidOperationException("Instrument role projection end dates cannot precede start dates.");
            }
        }

        var roleIds = projection.InstrumentRoles.Select(static role => role.RoleId).ToHashSet();
        var positions = new List<BookPositionDto>(projection.BookPositions.Count);
        foreach (var position in projection.BookPositions)
        {
            if (position.PositionId == Guid.Empty ||
                position.SecurityId != projection.Subject.SecurityId ||
                position.RoleId == Guid.Empty ||
                position.BookContext.LedgerBookId == Guid.Empty ||
                position.Version <= 0)
            {
                throw new InvalidOperationException("Book position projections require matching Security Master identity, non-empty position/role/book IDs, and a positive version.");
            }

            if (position.EffectiveTo < position.EffectiveFrom)
            {
                throw new InvalidOperationException("Book position projection end dates cannot precede start dates.");
            }

            if (projection.InstrumentRoles.Count > 0 && !roleIds.Contains(position.RoleId))
            {
                throw new InvalidOperationException($"Book position '{position.PositionId:D}' references a role that is not included in the projection write.");
            }

            var state = position.CurrentEconomicState ?? projection.PositionEconomicStates
                .Where(candidate => candidate.PositionId == position.PositionId)
                .OrderByDescending(static candidate => candidate.AsOfDate)
                .ThenByDescending(static candidate => candidate.Version)
                .FirstOrDefault();
            var lineage = position.ProjectionLineage ?? projection.ProjectionLineages
                .FirstOrDefault(candidate => candidate.BookPositionId == position.PositionId ||
                    candidate.TriggerEvent.BookPositionId == position.PositionId);
            positions.Add(position with
            {
                CurrentEconomicState = state,
                ProjectionLineage = lineage
            });
        }

        foreach (var state in projection.PositionEconomicStates)
        {
            if (state.EconomicStateId == Guid.Empty || state.PositionId == Guid.Empty || state.Version <= 0)
            {
                throw new InvalidOperationException("Position economic-state projections require non-empty IDs and a positive version.");
            }

            if (projection.BookPositions.Count > 0 && projection.BookPositions.All(position => position.PositionId != state.PositionId))
            {
                throw new InvalidOperationException($"Economic state '{state.EconomicStateId:D}' references a position that is not included in the projection write.");
            }
        }

        if (projection.ProjectionLineages.Any(lineage =>
                lineage.BookPositionId is not Guid positionId ||
                projection.BookPositions.All(position => position.PositionId != positionId)))
        {
            throw new InvalidOperationException("Projection lineage must reference a book position included in the projection write.");
        }

        return positions;
    }

    private static IReadOnlyList<PositionEconomicStateDto> NormalizeEconomicStates(
        AssetOperationsProjectionDto projection,
        IReadOnlyList<BookPositionDto> positions)
    {
        var lineages = projection.ProjectionLineages
            .Concat(positions
                .Select(static position => position.ProjectionLineage)
                .Where(static lineage => lineage is not null)!)
            .Cast<ProjectionLineageDto>()
            .ToArray();

        return projection.PositionEconomicStates
            .Select(state => state.ProjectionLineage is not null
                ? state
                : state with
                {
                    ProjectionLineage = lineages.FirstOrDefault(lineage =>
                        lineage.BookPositionId == state.PositionId &&
                        lineage.TriggerEvent.EventId == state.SourceEvent?.EventId)
                })
            .ToArray();
    }

    private async Task UpsertInstrumentRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        InstrumentRoleDto role,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("instrument_role_projections")} as persisted_role (
                role_id, security_id, owner_scope_id, owner_scope_kind, role_kind,
                effective_from, effective_to, version, source_event_id,
                approval_actor, approval_reference, approved_at, evidence_links, payload)
            values (
                @id, @security_id, @owner_scope_id, @owner_scope_kind, @role_kind,
                @effective_from, @effective_to, @version, @source_event_id,
                @approval_actor, @approval_reference, @approved_at, @evidence::jsonb, @payload::jsonb)
            on conflict (role_id) do update set
                effective_from = excluded.effective_from,
                effective_to = excluded.effective_to,
                version = excluded.version,
                source_event_id = excluded.source_event_id,
                evidence_links = excluded.evidence_links,
                payload = excluded.payload,
                updated_at = case
                    when excluded.version > persisted_role.version then now()
                    else persisted_role.updated_at
                end
            where persisted_role.security_id = excluded.security_id
              and persisted_role.owner_scope_id = excluded.owner_scope_id
              and persisted_role.owner_scope_kind = excluded.owner_scope_kind
              and persisted_role.role_kind = excluded.role_kind
              and (excluded.version > persisted_role.version
                   or (excluded.version = persisted_role.version
                       and excluded.payload = persisted_role.payload));
            """;
        AddTypedProjectionParameters(command, securityId, role.RoleId, role.Version, role.OriginEvent?.EventId, role.EvidenceLinks, role, approval);
        command.Parameters.AddWithValue("owner_scope_id", role.OwnerScopeId);
        command.Parameters.AddWithValue("owner_scope_kind", role.OwnerScopeKind);
        command.Parameters.AddWithValue("role_kind", role.RoleKind);
        command.Parameters.AddWithValue("effective_from", role.EffectiveFrom);
        command.Parameters.AddWithValue("effective_to", (object?)role.EffectiveTo ?? DBNull.Value);
        await EnsureTypedWriteAsync(command, "instrument role", role.RoleId, ct).ConfigureAwait(false);
    }

    private async Task UpsertBookPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        BookPositionDto position,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("book_position_projections")} as persisted_position (
                position_id, security_id, role_id, ledger_book_id, owner_scope_id, owner_scope_kind,
                effective_from, effective_to, version, source_event_id,
                approval_actor, approval_reference, approved_at, evidence_links, payload)
            values (
                @id, @security_id, @role_id, @ledger_book_id, @owner_scope_id, @owner_scope_kind,
                @effective_from, @effective_to, @version, @source_event_id,
                @approval_actor, @approval_reference, @approved_at, @evidence::jsonb, @payload::jsonb)
            on conflict (position_id) do update set
                effective_from = excluded.effective_from,
                effective_to = excluded.effective_to,
                version = excluded.version,
                source_event_id = excluded.source_event_id,
                evidence_links = excluded.evidence_links,
                payload = excluded.payload,
                updated_at = case
                    when excluded.version > persisted_position.version then now()
                    else persisted_position.updated_at
                end
            where persisted_position.security_id = excluded.security_id
              and persisted_position.role_id = excluded.role_id
              and persisted_position.ledger_book_id = excluded.ledger_book_id
              and persisted_position.owner_scope_id = excluded.owner_scope_id
              and persisted_position.owner_scope_kind = excluded.owner_scope_kind
              and (excluded.version > persisted_position.version
                   or (excluded.version = persisted_position.version
                       and excluded.payload = persisted_position.payload));
            """;
        var sourceEventId = position.OriginEvent?.EventId ?? position.ProjectionLineage?.TriggerEvent.EventId;
        AddTypedProjectionParameters(command, securityId, position.PositionId, position.Version, sourceEventId, position.EvidenceLinks, position, approval);
        command.Parameters.AddWithValue("role_id", position.RoleId);
        command.Parameters.AddWithValue("ledger_book_id", position.BookContext.LedgerBookId);
        command.Parameters.AddWithValue("owner_scope_id", position.BookContext.FundProfileId);
        command.Parameters.AddWithValue("owner_scope_kind", position.BookContext.FundStructureNodeKind.ToString());
        command.Parameters.AddWithValue("effective_from", position.EffectiveFrom);
        command.Parameters.AddWithValue("effective_to", (object?)position.EffectiveTo ?? DBNull.Value);
        await EnsureTypedWriteAsync(command, "book position", position.PositionId, ct).ConfigureAwait(false);
    }

    private async Task AppendEconomicStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        PositionEconomicStateDto state,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("position_economic_state_projections")} as persisted_state (
                economic_state_id, position_id, security_id, as_of_date, version, source_event_id,
                approval_actor, approval_reference, approved_at, evidence_links, payload)
            values (
                @id, @position_id, @security_id, @as_of_date, @version, @source_event_id,
                @approval_actor, @approval_reference, @approved_at, @evidence::jsonb, @payload::jsonb)
            on conflict (economic_state_id) do update set
                economic_state_id = persisted_state.economic_state_id
            where excluded.payload = persisted_state.payload;
            """;
        AddTypedProjectionParameters(command, securityId, state.EconomicStateId, state.Version, state.SourceEvent?.EventId, state.EvidenceLinks, state, approval);
        command.Parameters.AddWithValue("position_id", state.PositionId);
        command.Parameters.AddWithValue("as_of_date", state.AsOfDate);
        await EnsureTypedWriteAsync(command, "economic state", state.EconomicStateId, ct).ConfigureAwait(false);
    }

    private static void AddTypedProjectionParameters<T>(
        NpgsqlCommand command,
        Guid securityId,
        Guid id,
        long version,
        Guid? sourceEventId,
        IReadOnlyList<string> evidenceLinks,
        T payload,
        AssetOperationsWriteApprovalDto approval)
    {
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("source_event_id", (object?)sourceEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("approval_actor", approval.Actor);
        command.Parameters.AddWithValue("approval_reference", approval.ApprovalReference);
        command.Parameters.AddWithValue("approved_at", approval.ApprovedAt);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(evidenceLinks, JsonOptions));
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static async Task EnsureTypedWriteAsync(
        NpgsqlCommand command,
        string projectionKind,
        Guid id,
        CancellationToken ct)
    {
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException(
                $"The {projectionKind} projection '{id:D}' is stale or conflicts with the persisted version.");
        }
    }

    private static AssetOperationsReadinessDto BuildFallbackReadiness(AssetOperationSubjectDto subject)
        => new(
            subject.SecurityId,
            "ReviewRequired",
            subject.OperationalProfile,
            [],
            subject.OperationalProfile,
            ["No Asset Operations readiness projection has been published."],
            DateTimeOffset.UtcNow,
            "AssetOperations",
            subject.SecurityId.ToString("D"));

    private static (string? SourceDomain, string? SourceEntityId) SourceLineage<T>(T row)
        => row switch
        {
            AssetTermsVersionDto value => (value.SourceDomain, value.SourceEntityId),
            AssetLifecycleEventDto value => (value.SourceDomain, value.SourceEntityId),
            AssetCashFlowProjectionRunDto value => (value.SourceDomain, value.SourceEntityId),
            AssetProjectedCashFlowDto value => (value.SourceDomain, value.SourceEntityId),
            AssetActualActivityDto value => (value.SourceDomain, value.SourceEntityId),
            AssetReconciliationRunDto value => (value.SourceDomain, value.SourceEntityId),
            AssetReconciliationResultDto value => (value.SourceDomain, value.SourceEntityId),
            AssetLedgerProjectionDto value => (value.SourceDomain, value.SourceEntityId),
            AssetOperationsReadinessDto value => (value.SourceDomain, value.SourceEntityId),
            _ => ("AssetOperations", null)
        };

    private string Qualified(string table)
    {
        ValidateIdentifier(table, nameof(table));
        return $"{QuoteIdentifier(_options.Schema)}.{QuoteIdentifier(table)}";
    }

    private static string QuoteIdentifier(string value) => $"\"{value}\"";

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("PostgreSQL identifiers cannot be empty.", parameterName);
        }

        if (!IsValidIdentifier(value))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and underscores, and start with a letter or underscore.",
                parameterName);
        }
    }

    private static bool IsValidIdentifier(string value)
    {
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(static character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static readonly string[] Tables =
    [
        "asset_operation_subjects",
        "asset_terms_versions",
        "asset_lifecycle_events",
        "asset_cash_flow_projection_runs",
        "asset_projected_cash_flows",
        "asset_actual_activity",
        "asset_reconciliation_runs",
        "asset_reconciliation_results",
        "asset_ledger_projections",
        "asset_operations_readiness",
        "asset_workflow_audit"
    ];

    private static readonly string[] TypedOrderings =
    [
        "effective_from, version, role_id",
        "effective_from, version, position_id",
        "as_of_date, version, economic_state_id"
    ];
}
