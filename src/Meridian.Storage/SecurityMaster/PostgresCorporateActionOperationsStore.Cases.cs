using System.Data;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresCorporateActionOperationsStore
{
    private const string AddEvidenceOperation = "AddCaseEvidence";
    private const string RecordConflictOperation = "RecordCaseConflict";
    private const string ResolveConflictOperation = "ResolveCaseConflict";
    private const string UpsertOptionOperation = "UpsertProcessingOption";
    private const string TransitionCaseOperation = "TransitionCase";

    public async Task<CorporateActionProcessingCaseDto?> GetCaseAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await LoadCaseAsync(
                connection, transaction: null, caseId, tenantId, companyId, forUpdate: false, ct)
                .ConfigureAwait(false);
        }, "processing case read").ConfigureAwait(false);

    public async Task<IReadOnlyList<CorporateActionProcessingCaseDto>> ListCasesAsync(
        string tenantId,
        string companyId,
        Guid? securityId,
        string? state,
        int take,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
            {CaseSelect}
            where pc.tenant_id = @tenant_id
              and pc.company_id = @company_id
              and (@security_id is null or pc.security_id = @security_id)
              and (@state is null or pc.state = @state)
            order by pc.updated_at desc, pc.case_id
            limit @take;
            """;
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid)
            {
                Value = (object?)securityId ?? DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text)
            {
                Value = string.IsNullOrWhiteSpace(state) ? DBNull.Value : state.Trim(),
            });
            command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

            var cases = new List<CorporateActionProcessingCaseDto>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                cases.Add(ReadCase(reader));
            }

            return cases;
        }, "processing case list").ConfigureAwait(false);

    public async Task<CorporateActionConflictDto?> GetConflictAsync(
        Guid caseId,
        Guid conflictId,
        string tenantId,
        string companyId,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                {ConflictSelect}
                where c.case_id = @case_id
                  and c.conflict_id = @conflict_id
                  and pc.tenant_id = @tenant_id
                  and pc.company_id = @company_id;
                """;
            command.Parameters.AddWithValue("case_id", caseId);
            command.Parameters.AddWithValue("conflict_id", conflictId);
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("company_id", companyId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadConflict(reader) : null;
        }, "case conflict read").ConfigureAwait(false);

    public async Task<IReadOnlyList<CorporateActionConflictDto>> ListConflictsAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        string? state,
        int take,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                {ConflictSelect}
                where c.case_id = @case_id
                  and pc.tenant_id = @tenant_id
                  and pc.company_id = @company_id
                  and (@state is null or c.state = @state)
                order by c.recorded_at desc, c.conflict_id
                limit @take;
                """;
            command.Parameters.AddWithValue("case_id", caseId);
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text)
            {
                Value = (object?)state ?? DBNull.Value,
            });
            command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));
            var conflicts = new List<CorporateActionConflictDto>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                conflicts.Add(ReadConflict(reader));
            }

            return conflicts;
        }, "case conflict list").ConfigureAwait(false);

    public async Task<CorporateActionEvidenceMutationResultDto> AddEvidenceAsync(
        AddCorporateActionEvidenceRequestDto request,
        Guid evidenceId,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "evidence mutation",
                retryCt => AddEvidenceOnceAsync(request, evidenceId, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionEvidenceMutationResultDto> AddEvidenceOnceAsync(
        AddCorporateActionEvidenceRequestDto request,
        Guid evidenceId,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, AddEvidenceOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionEvidenceMutationResultDto>(
            connection, transaction, AddEvidenceOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(connection, transaction, request, ct)
            .ConfigureAwait(false);
        EnsureCaseContentMutable(processingCase, "add evidence");
        var now = DateTimeOffset.UtcNow;
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            state: processingCase.State, assignedTo: processingCase.AssignedTo,
            blockedReason: processingCase.BlockedReason, now, ct).ConfigureAwait(false);
        var evidence = new CorporateActionEvidenceDto(
            evidenceId,
            request.CaseId,
            request.EvidenceKind,
            request.EvidenceReference,
            request.EvidenceHash,
            request.Description,
            request.Metadata,
            updatedCase.Version,
            request.Actor,
            now);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("corporate_action_case_evidence")} (
                    evidence_id, case_id, evidence_kind, evidence_reference, evidence_hash,
                    description, metadata, case_version, recorded_by, recorded_at)
                values (
                    @evidence_id, @case_id, @evidence_kind, @evidence_reference, @evidence_hash,
                    @description, @metadata, @case_version, @recorded_by, @recorded_at);
                """;
            command.Parameters.AddWithValue("evidence_id", evidence.EvidenceId);
            command.Parameters.AddWithValue("case_id", evidence.CaseId);
            command.Parameters.AddWithValue("evidence_kind", evidence.EvidenceKind);
            command.Parameters.AddWithValue("evidence_reference", evidence.EvidenceReference);
            command.Parameters.AddWithValue("evidence_hash", (object?)evidence.EvidenceHash ?? DBNull.Value);
            command.Parameters.AddWithValue("description", (object?)evidence.Description ?? DBNull.Value);
            command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb)
            {
                Value = evidence.Metadata is { ValueKind: not JsonValueKind.Undefined } metadata
                    ? metadata.GetRawText()
                    : DBNull.Value,
            });
            command.Parameters.AddWithValue("case_version", evidence.CaseVersion);
            command.Parameters.AddWithValue("recorded_by", evidence.RecordedBy);
            command.Parameters.AddWithValue("recorded_at", evidence.RecordedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var result = new CorporateActionEvidenceMutationResultDto(updatedCase, evidence, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, AddEvidenceOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<CorporateActionConflictMutationResultDto> RecordConflictAsync(
        RecordCorporateActionConflictRequestDto request,
        Guid conflictId,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "conflict mutation",
                retryCt => RecordConflictOnceAsync(request, conflictId, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionConflictMutationResultDto> RecordConflictOnceAsync(
        RecordCorporateActionConflictRequestDto request,
        Guid conflictId,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, RecordConflictOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionConflictMutationResultDto>(
            connection, transaction, RecordConflictOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(connection, transaction, request, ct)
            .ConfigureAwait(false);
        EnsureCaseContentMutable(processingCase, "record a conflict");
        EnsureConflictCanBeRecorded(processingCase);
        var now = DateTimeOffset.UtcNow;
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            state: processingCase.State, assignedTo: processingCase.AssignedTo,
            blockedReason: processingCase.BlockedReason, now, ct).ConfigureAwait(false);
        var conflict = new CorporateActionConflictDto(
            conflictId,
            request.CaseId,
            request.Field,
            request.Description,
            request.Candidates,
            CorporateActionConflictStates.Open,
            Resolution: null,
            updatedCase.Version,
            request.Actor,
            now);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("corporate_action_case_conflicts")} (
                    conflict_id, case_id, field_name, description, candidates, state, resolution,
                    case_version, recorded_by, recorded_at)
                values (
                    @conflict_id, @case_id, @field_name, @description, @candidates, @state, @resolution,
                    @case_version, @recorded_by, @recorded_at);
                """;
            command.Parameters.AddWithValue("conflict_id", conflict.ConflictId);
            command.Parameters.AddWithValue("case_id", conflict.CaseId);
            command.Parameters.AddWithValue("field_name", conflict.Field);
            command.Parameters.AddWithValue("description", conflict.Description);
            AddJson(command, "candidates", conflict.Candidates);
            command.Parameters.AddWithValue("state", conflict.State);
            command.Parameters.Add(new NpgsqlParameter("resolution", NpgsqlDbType.Text)
            {
                Value = DBNull.Value,
            });
            command.Parameters.AddWithValue("case_version", conflict.CaseVersion);
            command.Parameters.AddWithValue("recorded_by", conflict.RecordedBy);
            command.Parameters.AddWithValue("recorded_at", conflict.RecordedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var result = new CorporateActionConflictMutationResultDto(updatedCase, conflict, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, RecordConflictOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<CorporateActionConflictResolutionResultDto> ResolveConflictAsync(
        ResolveCorporateActionConflictRequestDto request,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "conflict resolution",
                retryCt => ResolveConflictOnceAsync(request, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionConflictResolutionResultDto> ResolveConflictOnceAsync(
        ResolveCorporateActionConflictRequestDto request,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, ResolveConflictOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionConflictResolutionResultDto>(
            connection, transaction, ResolveConflictOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(connection, transaction, request, ct)
            .ConfigureAwait(false);
        EnsureCaseContentMutable(processingCase, "resolve or waive a conflict");

        CorporateActionConflictDto conflict;
        await using (var load = connection.CreateCommand())
        {
            load.Transaction = transaction;
            load.CommandText =
                $"""
                select field_name, description, candidates, state, resolution, case_version,
                       recorded_by, recorded_at, resolved_by, resolved_at,
                       resolution_evidence_reference, resolution_evidence_hash
                from {Qualified("corporate_action_case_conflicts")}
                where conflict_id = @conflict_id and case_id = @case_id
                for update;
                """;
            load.Parameters.AddWithValue("conflict_id", request.ConflictId);
            load.Parameters.AddWithValue("case_id", request.CaseId);
            await using var reader = await load.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new CorporateActionNotFoundException("Corporate-action case conflict", request.ConflictId);
            }

            conflict = new CorporateActionConflictDto(
                request.ConflictId,
                request.CaseId,
                reader.GetString(0),
                reader.GetString(1),
                JsonSerializer.Deserialize<IReadOnlyList<CorporateActionConflictCandidateDto>>(
                    reader.GetString(2), JsonOptions) ?? [],
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                ReadTimestamp(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : ReadTimestamp(reader, 9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11));
        }

        if (!string.Equals(conflict.State, CorporateActionConflictStates.Open, StringComparison.Ordinal))
        {
            throw new CorporateActionStateConflictException(
                request.ConflictId,
                $"Corporate-action conflict is already '{conflict.State}' and cannot be disposed again.");
        }

        var now = DateTimeOffset.UtcNow;
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            state: processingCase.State, assignedTo: processingCase.AssignedTo,
            blockedReason: processingCase.BlockedReason, now, ct).ConfigureAwait(false);
        var resolved = conflict with
        {
            State = request.Disposition,
            Resolution = request.Resolution,
            CaseVersion = updatedCase.Version,
            ResolvedBy = request.Actor,
            ResolvedAtUtc = now,
            ResolutionEvidenceReference = request.EvidenceReference,
            ResolutionEvidenceHash = request.EvidenceHash,
        };
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                $"""
                update {Qualified("corporate_action_case_conflicts")}
                set state = @state,
                    resolution = @resolution,
                    case_version = @case_version,
                    resolved_by = @resolved_by,
                    resolved_at = @resolved_at,
                    resolution_evidence_reference = @evidence_reference,
                    resolution_evidence_hash = @evidence_hash
                where conflict_id = @conflict_id and case_id = @case_id and state = @open_state;
                """;
            update.Parameters.AddWithValue("state", resolved.State);
            update.Parameters.AddWithValue("resolution", resolved.Resolution!);
            update.Parameters.AddWithValue("case_version", resolved.CaseVersion);
            update.Parameters.AddWithValue("resolved_by", resolved.ResolvedBy!);
            update.Parameters.AddWithValue("resolved_at", now.UtcDateTime);
            update.Parameters.AddWithValue("evidence_reference", resolved.ResolutionEvidenceReference!);
            update.Parameters.AddWithValue("evidence_hash", resolved.ResolutionEvidenceHash!);
            update.Parameters.AddWithValue("conflict_id", resolved.ConflictId);
            update.Parameters.AddWithValue("case_id", resolved.CaseId);
            update.Parameters.AddWithValue("open_state", CorporateActionConflictStates.Open);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new CorporateActionStateConflictException(
                    request.ConflictId,
                    "Corporate-action conflict changed before its disposition could be recorded.");
            }
        }

        var result = new CorporateActionConflictResolutionResultDto(updatedCase, resolved, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, ResolveConflictOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<CorporateActionProcessingOptionMutationResultDto> UpsertOptionAsync(
        UpsertCorporateActionProcessingOptionRequestDto request,
        Guid optionId,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "processing-option mutation",
                retryCt => UpsertOptionOnceAsync(request, optionId, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionProcessingOptionMutationResultDto> UpsertOptionOnceAsync(
        UpsertCorporateActionProcessingOptionRequestDto request,
        Guid optionId,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, UpsertOptionOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionProcessingOptionMutationResultDto>(
            connection, transaction, UpsertOptionOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(connection, transaction, request, ct)
            .ConfigureAwait(false);
        EnsureCaseContentMutable(processingCase, "change a processing option");
        var now = DateTimeOffset.UtcNow;
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            state: processingCase.State, assignedTo: processingCase.AssignedTo,
            blockedReason: processingCase.BlockedReason, now, ct).ConfigureAwait(false);
        var option = new CorporateActionProcessingOptionDto(
            optionId,
            request.CaseId,
            request.OptionCode,
            request.Label,
            request.Description,
            request.State,
            request.SourceMethodology,
            request.Blockers ?? [],
            request.Parameters,
            updatedCase.Version,
            request.Actor,
            now);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("corporate_action_processing_options")} (
                    option_id, case_id, option_code, label, description, state, source_methodology,
                    blockers, parameters, case_version, recorded_by, recorded_at)
                values (
                    @option_id, @case_id, @option_code, @label, @description, @state, @source_methodology,
                    @blockers, @parameters, @case_version, @recorded_by, @recorded_at);
                """;
            command.Parameters.AddWithValue("option_id", option.OptionId);
            command.Parameters.AddWithValue("case_id", option.CaseId);
            command.Parameters.AddWithValue("option_code", option.OptionCode);
            command.Parameters.AddWithValue("label", option.Label);
            command.Parameters.AddWithValue("description", option.Description);
            command.Parameters.AddWithValue("state", option.State);
            command.Parameters.AddWithValue("source_methodology", (object?)option.SourceMethodology ?? DBNull.Value);
            AddJson(command, "blockers", option.Blockers);
            command.Parameters.Add(new NpgsqlParameter("parameters", NpgsqlDbType.Jsonb)
            {
                Value = option.Parameters is { ValueKind: not JsonValueKind.Undefined } parameters
                    ? parameters.GetRawText()
                    : DBNull.Value,
            });
            command.Parameters.AddWithValue("case_version", option.CaseVersion);
            command.Parameters.AddWithValue("recorded_by", option.RecordedBy);
            command.Parameters.AddWithValue("recorded_at", option.RecordedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var result = new CorporateActionProcessingOptionMutationResultDto(updatedCase, option, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, UpsertOptionOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<CorporateActionCaseTransitionResultDto> TransitionCaseAsync(
        TransitionCorporateActionCaseRequestDto request,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "state transition",
                retryCt => TransitionCaseOnceAsync(request, transitionId, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionCaseTransitionResultDto> TransitionCaseOnceAsync(
        TransitionCorporateActionCaseRequestDto request,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, TransitionCaseOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionCaseTransitionResultDto>(
            connection, transaction, TransitionCaseOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(connection, transaction, request, ct)
            .ConfigureAwait(false);
        if (CorporateActionCaseStates.RequiresDownstreamAuthority(request.ToState))
        {
            throw new CorporateActionDownstreamAuthorityRequiredException(request.ToState);
        }

        if (!CorporateActionCaseTransitionAuthorization.IsAuthorized(
                request.ToState,
                request.Authority,
                request.PolicyOverride,
                out var requiredAuthority))
        {
            throw new CorporateActionPermissionDeniedException(request.ToState, requiredAuthority);
        }

        if (!CorporateActionCaseTransitionPolicy.CanTransition(processingCase.State, request.ToState))
        {
            throw new CorporateActionStateConflictException(
                processingCase.CaseId,
                $"Corporate-action case cannot transition from '{processingCase.State}' to '{request.ToState}'.");
        }

        await ValidateTransitionPreconditionsAsync(
                connection, transaction, processingCase, request.ToState, ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var blockedReason = string.Equals(request.ToState, CorporateActionCaseStates.Blocked, StringComparison.Ordinal)
            ? request.BlockedReason
            : null;
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            request.ToState, request.AssignedTo ?? processingCase.AssignedTo, blockedReason, now, ct)
            .ConfigureAwait(false);
        var transition = new CorporateActionCaseTransitionDto(
            transitionId,
            processingCase.CaseId,
            processingCase.State,
            request.ToState,
            request.ExpectedVersion,
            updatedCase.Version,
            request.Actor,
            request.Reason,
            request.IdempotencyKey,
            now,
            request.CorrelationId,
            request.PolicyOverride);
        await InsertTransitionAsync(
            connection, transaction, TransitionCaseOperation, transition, ct).ConfigureAwait(false);

        var result = new CorporateActionCaseTransitionResultDto(updatedCase, transition, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, TransitionCaseOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private async Task ValidateTransitionPreconditionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase,
        string targetState,
        CancellationToken ct)
    {
        if (!CorporateActionCaseTransitionPolicy.RequiresConflictFreeTerms(
                processingCase.State,
                targetState))
        {
            return;
        }

        await using (var evidenceCommand = connection.CreateCommand())
        {
            evidenceCommand.Transaction = transaction;
            evidenceCommand.CommandText =
                $$"""
                select exists (
                    select 1
                    from {{Qualified("corporate_action_case_evidence")}}
                    where case_id = @case_id
                      and evidence_hash ~ '^[0-9a-f]{64}$'
                      and (
                          evidence_reference ~* '^(https|s3|gs|azure|document|vault|alpaca|provider|provider-event)://'
                          or evidence_reference ~* '^urn:'));
                """;
            evidenceCommand.Parameters.AddWithValue("case_id", processingCase.CaseId);
            var hasRetainedEvidence = (bool)(await evidenceCommand.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
            if (!hasRetainedEvidence)
            {
                throw new CorporateActionTermsIncompleteException(
                    $"Transition to '{targetState}' requires at least one retained evidence reference on the processing case.");
            }
        }

        await using var conflictCommand = connection.CreateCommand();
        conflictCommand.Transaction = transaction;
        conflictCommand.CommandText =
            $"""
            select exists (
                select 1
                from {Qualified("corporate_action_case_conflicts")}
                where case_id = @case_id
                  and state = @open_state);
            """;
        conflictCommand.Parameters.AddWithValue("case_id", processingCase.CaseId);
        conflictCommand.Parameters.AddWithValue("open_state", CorporateActionConflictStates.Open);
        var hasOpenConflict = (bool)(await conflictCommand.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
        if (hasOpenConflict)
        {
            throw new CorporateActionSourceConflictException(
                $"Transition to '{targetState}' is blocked while the processing case contains an open source conflict.");
        }

        if (string.Equals(targetState, CorporateActionCaseStates.ReadyForApproval, StringComparison.Ordinal))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "ReadyForApproval requires a durable accounting projection and policy decision bound to the exact case, evidence, scope, and period versions; that authority is not yet persisted.");
        }
    }

    private async Task<TResult> ExecuteCaseMutationWithRetryAsync<TResult>(
        Guid caseId,
        string operationDescription,
        Func<CancellationToken, Task<TResult>> execute,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await execute(ct).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (
                (IsRetryableConcurrencyFailure(exception)
                 || exception.SqlState == PostgresErrorCodes.UniqueViolation)
                && attempt < MaximumSerializableAttempts)
            {
                // The next transaction re-acquires the aggregate/command advisory locks and reads
                // the durable receipt before touching the case. Same-command races replay; a
                // competing command sees the committed case version and returns a domain 409.
                ct.ThrowIfCancellationRequested();
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new CorporateActionStateConflictException(
                    caseId,
                    $"Corporate-action case {operationDescription} collided with an existing durable command; reload the case before retrying.");
            }
            catch (PostgresException exception) when (IsRetryableConcurrencyFailure(exception))
            {
                throw new CorporateActionPersistenceUnavailableException(
                    $"Corporate-action case {operationDescription} remained contended after {MaximumSerializableAttempts} serializable attempts.");
            }
            catch (NpgsqlException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    $"Corporate-action case {operationDescription} persistence is temporarily unavailable; reload before retrying.");
            }
            catch (TimeoutException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    $"Corporate-action case {operationDescription} timed out; reload its durable receipt before retrying.");
            }
        }
    }

    private static async Task AcquireCaseCommandLocksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string operation,
        Guid caseId,
        string idempotencyKey,
        CancellationToken ct)
    {
        // Lock ordering is deliberately aggregate first, command second for every case mutation.
        await AcquireTransactionLockAsync(
            connection,
            transaction,
            $"corporate-action-case:{caseId:D}",
            ct).ConfigureAwait(false);
        await AcquireTransactionLockAsync(
            connection,
            transaction,
            $"corporate-action-command:{operation}:{caseId:D}:{idempotencyKey}",
            ct).ConfigureAwait(false);
    }

    private async Task<CorporateActionProcessingCaseDto> LoadScopedCaseForMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AddCorporateActionEvidenceRequestDto request,
        CancellationToken ct) =>
        await LoadScopedCaseForMutationAsync(
            connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
            request.ExpectedVersion, ct).ConfigureAwait(false);

    private async Task<CorporateActionProcessingCaseDto> LoadScopedCaseForMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResolveCorporateActionConflictRequestDto request,
        CancellationToken ct) =>
        await LoadScopedCaseForMutationAsync(
            connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
            request.ExpectedVersion, ct).ConfigureAwait(false);

    private async Task<CorporateActionProcessingCaseDto> LoadScopedCaseForMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RecordCorporateActionConflictRequestDto request,
        CancellationToken ct) =>
        await LoadScopedCaseForMutationAsync(
            connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
            request.ExpectedVersion, ct).ConfigureAwait(false);

    private async Task<CorporateActionProcessingCaseDto> LoadScopedCaseForMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        UpsertCorporateActionProcessingOptionRequestDto request,
        CancellationToken ct) =>
        await LoadScopedCaseForMutationAsync(
            connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
            request.ExpectedVersion, ct).ConfigureAwait(false);

    private async Task<CorporateActionProcessingCaseDto> LoadScopedCaseForMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TransitionCorporateActionCaseRequestDto request,
        CancellationToken ct) =>
        await LoadScopedCaseForMutationAsync(
            connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
            request.ExpectedVersion, ct).ConfigureAwait(false);

    private async Task<CorporateActionProcessingCaseDto> LoadScopedCaseForMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid caseId,
        string tenantId,
        string companyId,
        long expectedVersion,
        CancellationToken ct)
    {
        var processingCase = await LoadCaseAsync(
            connection, transaction, caseId, tenantId, companyId, forUpdate: true, ct)
            .ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Corporate-action processing case", caseId);
        if (HasNarrowScope(processingCase.Scope))
        {
            throw new CorporateActionScopeMismatchException(
                "Narrowly scoped corporate-action cases cannot be mutated through a tenant/company-only command. Supply an authoritative full-scope command path.");
        }

        EnsureVersion(caseId, expectedVersion, processingCase.Version);
        return processingCase;
    }

    private static bool HasNarrowScope(CorporateActionCaseScopeDto scope) =>
        !string.IsNullOrWhiteSpace(scope.StructureNodeId)
        || !string.IsNullOrWhiteSpace(scope.FundProfileId)
        || !string.IsNullOrWhiteSpace(scope.FinancialAccountId)
        || !string.IsNullOrWhiteSpace(scope.PortfolioId)
        || !string.IsNullOrWhiteSpace(scope.CustodyAccountId)
        || !string.IsNullOrWhiteSpace(scope.LedgerBookId)
        || !string.IsNullOrWhiteSpace(scope.PeriodId)
        || !string.IsNullOrWhiteSpace(scope.AccountingBasis)
        || !string.IsNullOrWhiteSpace(scope.FunctionalCurrency)
        || !string.IsNullOrWhiteSpace(scope.Jurisdiction);

    private static void EnsureCaseContentMutable(
        CorporateActionProcessingCaseDto processingCase,
        string operation)
    {
        if (!CorporateActionCaseStates.IsContentFrozen(processingCase.State))
        {
            return;
        }

        throw new CorporateActionStateConflictException(
            processingCase.CaseId,
            $"Corporate-action case state '{processingCase.State}' is content-frozen and cannot {operation}; use the governed reopen or restatement transition first.");
    }

    internal static void EnsureConflictCanBeRecorded(CorporateActionProcessingCaseDto processingCase)
    {
        if (!CorporateActionCaseStates.PresupposesConfirmedTerms(processingCase.State))
        {
            return;
        }

        throw new CorporateActionSourceConflictException(
            $"A new source conflict cannot be recorded while case '{processingCase.CaseId:D}' is in '{processingCase.State}', because that state presupposes confirmed terms; move the case through Blocked into Disputed/NeedsTerms or use the governed restatement path first.");
    }

    private async Task<CorporateActionProcessingCaseDto> UpdateCaseVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase,
        long expectedVersion,
        string actor,
        string state,
        string? assignedTo,
        string? blockedReason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var updated = processingCase with
        {
            State = state,
            Version = processingCase.Version + 1,
            AssignedTo = assignedTo,
            BlockedReason = blockedReason,
            UpdatedBy = actor,
            UpdatedAtUtc = now,
            ActionAvailability = null,
        };

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {Qualified("corporate_action_processing_cases")}
            set state = @state,
                version = @resulting_version,
                assigned_to = @assigned_to,
                blocked_reason = @blocked_reason,
                updated_by = @updated_by,
                updated_at = @updated_at
            where case_id = @case_id
              and version = @expected_version;
            """;
        command.Parameters.AddWithValue("state", updated.State);
        command.Parameters.AddWithValue("resulting_version", updated.Version);
        command.Parameters.AddWithValue("assigned_to", (object?)updated.AssignedTo ?? DBNull.Value);
        command.Parameters.AddWithValue("blocked_reason", (object?)updated.BlockedReason ?? DBNull.Value);
        command.Parameters.AddWithValue("updated_by", updated.UpdatedBy);
        command.Parameters.AddWithValue("updated_at", updated.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("case_id", updated.CaseId);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new CorporateActionVersionConflictException(
                processingCase.CaseId,
                expectedVersion,
                processingCase.Version);
        }

        return updated;
    }

    private async Task<CorporateActionProcessingCaseDto?> LoadCaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid caseId,
        string? tenantId,
        string? companyId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            {CaseSelect}
            where pc.case_id = @case_id
              and (@tenant_id is null or pc.tenant_id = @tenant_id)
              and (@company_id is null or pc.company_id = @company_id)
            {(forUpdate ? "for update of pc" : string.Empty)};
            """;
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Text)
        {
            Value = (object?)tenantId ?? DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("company_id", NpgsqlDbType.Text)
        {
            Value = (object?)companyId ?? DBNull.Value,
        });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadCase(reader) : null;
    }

    private static CorporateActionProcessingCaseDto ReadCase(NpgsqlDataReader reader)
    {
        var scope = new CorporateActionCaseScopeDto(
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15));
        var proposedAction = JsonSerializer.Deserialize<CorporateActionDto>(reader.GetString(25), JsonOptions)
            ?? throw new InvalidOperationException("Stored corporate-action case has no source action payload.");
        if (!Enum.TryParse<CorporateActionProviderReleaseStatusDto>(
                reader.GetString(32), ignoreCase: false, out var releaseStatus)
            || !Enum.IsDefined(releaseStatus))
        {
            throw new InvalidOperationException("Stored corporate-action case has an unknown provider release status.");
        }

        var providerIdentity = new CorporateActionProviderEventIdentityDto(
            reader.GetString(26),
            reader.GetString(27),
            reader.GetString(28),
            ReadTimestamp(reader, 29),
            reader.IsDBNull(30) ? null : reader.GetString(30),
            reader.IsDBNull(31) ? null : reader.GetString(31),
            releaseStatus);
        var displayMetadata = reader.IsDBNull(33) && reader.IsDBNull(34) && reader.IsDBNull(37)
            ? null
            : new CorporateActionSourceDisplayMetadataDto(
                reader.IsDBNull(33) ? string.Empty : reader.GetString(33),
                reader.IsDBNull(34) ? providerIdentity.ProviderId : reader.GetString(34),
                JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(35), JsonOptions) ?? [],
                JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(36), JsonOptions) ?? [],
                reader.IsDBNull(37)
                    ? []
                    : JsonSerializer.Deserialize<IReadOnlyList<CorporateActionDissentFieldDto>>(
                        reader.GetString(37), JsonOptions) ?? []);
        var sourceSnapshot = new CorporateActionCaseSourceSnapshotDto(
            proposedAction,
            providerIdentity,
            displayMetadata);

        return new CorporateActionProcessingCaseDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            scope,
            reader.GetString(16),
            reader.GetInt64(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.GetString(21),
            ReadTimestamp(reader, 22),
            reader.GetString(23),
            ReadTimestamp(reader, 24),
            SourceSnapshot: sourceSnapshot);
    }

    private static CorporateActionConflictDto ReadConflict(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            JsonSerializer.Deserialize<IReadOnlyList<CorporateActionConflictCandidateDto>>(
                reader.GetString(4), JsonOptions) ?? [],
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt64(7),
            reader.GetString(8),
            ReadTimestamp(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : ReadTimestamp(reader, 11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));

    private string CaseSelect =>
        $"""
        select pc.case_id, pc.proposal_id, pc.corp_act_id, pc.security_id, pc.tenant_id, pc.company_id,
               pc.structure_node_id, pc.fund_profile_id, pc.financial_account_id, pc.portfolio_id,
               pc.custody_account_id, pc.ledger_book_id, pc.period_id, pc.accounting_basis,
               pc.functional_currency, pc.jurisdiction, pc.state, pc.version, pc.methodology_profile_id,
               pc.assigned_to, pc.blocked_reason, pc.created_by, pc.created_at, pc.updated_by, pc.updated_at,
               p.proposed_action::text, p.provider_id, p.source_event_id, p.source_event_version,
               p.observed_at, p.evidence_hash, p.evidence_reference, p.provider_release_status,
               p.display_ticker, p.winning_source, p.agreeing_sources::text,
               p.dissenting_sources::text, p.dissent_fields::text
        from {Qualified("corporate_action_processing_cases")} pc
        join {Qualified("corporate_action_source_proposals")} p on p.proposal_id = pc.proposal_id
        """;

    private string ConflictSelect =>
        $"""
        select c.conflict_id, c.case_id, c.field_name, c.description, c.candidates::text,
               c.state, c.resolution, c.case_version, c.recorded_by, c.recorded_at,
               c.resolved_by, c.resolved_at, c.resolution_evidence_reference,
               c.resolution_evidence_hash
        from {Qualified("corporate_action_case_conflicts")} c
        join {Qualified("corporate_action_processing_cases")} pc on pc.case_id = c.case_id
        """;
}
