using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// PostgreSQL authority for corporate-action observations and scoped processing cases. Proposal
/// acceptance is a single serializable transaction that appends the canonical Security Master
/// action, creates the initial case/transition, records the decision, and writes its replay receipt.
/// </summary>
public sealed partial class PostgresCorporateActionOperationsStore : ICorporateActionOperationsStore
{
    private const string AcceptOperation = "AcceptSourceProposal";
    private const string RejectOperation = "RejectSourceProposal";
    private const int MaximumSerializableAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SecurityMasterOptions _options;

    public PostgresCorporateActionOperationsStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<CorporateActionSourceProposalDto> RecordSourceProposalAsync(
        CorporateActionSourceProposalDto proposal,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RecordSourceProposalOnceAsync(proposal, ct).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // A writer outside this process may not honor our advisory lock. PostgreSQL waits
                // for that writer before reporting 23505, so a fresh read can deterministically
                // distinguish an identical replay from changed content under the same source ID.
                return await ExecutePersistenceReadAsync(
                        () => ReconcileSourceIdentityCollisionAsync(proposal, ct),
                        "source identity collision reconciliation")
                    .ConfigureAwait(false);
            }
            catch (PostgresException exception) when (
                IsRetryableConcurrencyFailure(exception) && attempt < MaximumSerializableAttempts)
            {
                ct.ThrowIfCancellationRequested();
            }
            catch (PostgresException exception) when (IsRetryableConcurrencyFailure(exception))
            {
                throw new CorporateActionPersistenceUnavailableException(
                    $"Corporate-action source proposal persistence remained contended after {MaximumSerializableAttempts} serializable attempts.");
            }
            catch (NpgsqlException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action source proposal persistence is temporarily unavailable; no command was accepted.");
            }
            catch (TimeoutException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action source proposal persistence timed out; the durable result must be reloaded before retrying.");
            }
        }
    }

    private async Task<CorporateActionSourceProposalDto> RecordSourceProposalOnceAsync(
        CorporateActionSourceProposalDto proposal,
        CancellationToken ct)
    {

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireTransactionLockAsync(
            connection,
            transaction,
            SourceEventLockScope(proposal.ProviderIdentity),
            ct).ConfigureAwait(false);

        var existing = await LoadProposalBySourceIdentityAsync(
            connection,
            transaction,
            proposal.ProviderIdentity.ProviderId,
            proposal.ProviderIdentity.SourceEventId,
            proposal.ProviderIdentity.SourceEventVersion,
            forUpdate: true,
            ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var replayCandidate = BindExactSourceReplay(existing, proposal);
            if (!CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, replayCandidate))
            {
                throw new CorporateActionIdempotencyConflictException(
                    existing.ProposalId,
                    $"{proposal.ProviderIdentity.ProviderId}:{proposal.ProviderIdentity.SourceEventId}:{proposal.ProviderIdentity.SourceEventVersion}");
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return existing;
        }

        var currentTip = await LoadCurrentSourceRevisionTipAsync(
                connection,
                transaction,
                proposal.ProviderIdentity.ProviderId,
                proposal.ProviderIdentity.SourceEventId,
                ct)
            .ConfigureAwait(false);
        if (currentTip is null)
        {
            if (proposal.SupersedesProposalId is { } orphanedParentId)
            {
                throw new CorporateActionStateConflictException(
                    orphanedParentId,
                    "The declared source-proposal parent is not the current tip of this provider event amendment chain.");
            }
        }
        else
        {
            if (await HasSourceSuccessorAsync(
                    connection, transaction, currentTip.ProposalId, proposal.ProposalId, ct)
                .ConfigureAwait(false))
            {
                throw new CorporateActionStateConflictException(
                    currentTip.ProposalId,
                    "The source proposal is no longer the amendment-chain tip; reload its current successor.");
            }

            proposal = BindNewSourceRevision(currentTip, proposal);
        }

        await InsertProposalAsync(connection, transaction, proposal, ct).ConfigureAwait(false);

        if (proposal.SupersedesProposalId is { } priorId)
        {
            await using var supersede = connection.CreateCommand();
            supersede.Transaction = transaction;
            supersede.CommandText =
                $"""
                update {Qualified("corporate_action_source_proposals")}
                set state = @state,
                    version = version + 1,
                    updated_at = @updated_at
                where proposal_id = @proposal_id;
                """;
            supersede.Parameters.AddWithValue("state", CorporateActionSourceProposalStates.Superseded);
            supersede.Parameters.AddWithValue("updated_at", proposal.RecordedAtUtc.UtcDateTime);
            supersede.Parameters.AddWithValue("proposal_id", priorId);
            await supersede.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return proposal;
    }

    public async Task<CorporateActionSourceProposalDto?> GetSourceProposalAsync(
        Guid proposalId,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await LoadProposalAsync(connection, transaction: null, proposalId, forUpdate: false, ct)
                .ConfigureAwait(false);
        }, "source proposal read").ConfigureAwait(false);

    public async Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListSourceProposalsAsync(
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
            {ProposalSelect}
            where (@security_id is null or security_id = @security_id)
              and (@state is null or state = @state)
            order by observed_at desc, proposal_id
            limit @take;
            """;
            command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid)
            {
                Value = (object?)securityId ?? DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text)
            {
                Value = string.IsNullOrWhiteSpace(state) ? DBNull.Value : state.Trim(),
            });
            command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

            var proposals = new List<CorporateActionSourceProposalDto>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                proposals.Add(ReadProposal(reader));
            }

            return proposals;
        }, "source proposal list").ConfigureAwait(false);

    public async Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListActionableSourceProposalsAsync(
        Guid? securityId,
        int take,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
            {ProposalSelect}
            where (@security_id is null or security_id = @security_id)
              and state in (@observed_state, @review_required_state)
            order by observed_at desc, proposal_id
            limit @take;
            """;
            command.Parameters.Add(new NpgsqlParameter("security_id", NpgsqlDbType.Uuid)
            {
                Value = (object?)securityId ?? DBNull.Value,
            });
            command.Parameters.AddWithValue("observed_state", CorporateActionSourceProposalStates.Observed);
            command.Parameters.AddWithValue("review_required_state", CorporateActionSourceProposalStates.ReviewRequired);
            command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 500));

            var proposals = new List<CorporateActionSourceProposalDto>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                proposals.Add(ReadProposal(reader));
            }

            return proposals;
        }, "actionable source proposal list").ConfigureAwait(false);

    public async Task<CorporateActionSourceProposalAcceptanceResultDto?> GetAcceptanceReceiptAsync(
        Guid proposalId,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var receipt = await ReadReceiptAsync<CorporateActionSourceProposalAcceptanceResultDto>(
                    connection,
                    transaction: null,
                    AcceptOperation,
                    proposalId,
                    idempotencyKey,
                    requestFingerprint,
                    ct)
                .ConfigureAwait(false);
            return receipt is null ? null : receipt with { Replayed = true };
        }, "acceptance receipt read").ConfigureAwait(false);

    public async Task<CorporateActionSourceProposalAcceptanceResultDto> AcceptSourceProposalAsync(
        AcceptCorporateActionSourceProposalRequestDto request,
        Guid corporateActionId,
        Guid caseId,
        Guid transitionId,
        SecurityMasterCorporateActionRestatementDto? restatement,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptSourceProposalOnceAsync(
                    request,
                    corporateActionId,
                    caseId,
                    transitionId,
                    restatement,
                    requestFingerprint,
                    ct).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (
                (IsRetryableConcurrencyFailure(exception)
                 || exception.SqlState == PostgresErrorCodes.UniqueViolation)
                && attempt < MaximumSerializableAttempts)
            {
                // On retry the advisory lock and receipt read turn an identical concurrent command
                // into a replay, while a competing decision becomes an explicit version/state
                // conflict. Unique receipt races are handled the same way.
                ct.ThrowIfCancellationRequested();
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new CorporateActionStateConflictException(
                    request.ProposalId,
                    "Corporate-action acceptance collided with an existing durable command or canonical successor; reload the proposal before retrying.");
            }
            catch (PostgresException exception) when (IsRetryableConcurrencyFailure(exception))
            {
                throw new CorporateActionPersistenceUnavailableException(
                    $"Corporate-action acceptance remained contended after {MaximumSerializableAttempts} serializable attempts.");
            }
            catch (NpgsqlException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action acceptance persistence is temporarily unavailable; reload the proposal before retrying.");
            }
            catch (TimeoutException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action acceptance timed out; reload its durable receipt before retrying.");
            }
        }
    }

    private async Task<CorporateActionSourceProposalAcceptanceResultDto> AcceptSourceProposalOnceAsync(
        AcceptCorporateActionSourceProposalRequestDto request,
        Guid corporateActionId,
        Guid caseId,
        Guid transitionId,
        SecurityMasterCorporateActionRestatementDto? restatement,
        string requestFingerprint,
        CancellationToken ct)
    {

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireTransactionLockAsync(
            connection,
            transaction,
            $"corporate-action-proposal:{request.ProposalId:D}",
            ct).ConfigureAwait(false);
        await AcquireTransactionLockAsync(
            connection,
            transaction,
            $"corporate-action-command:{AcceptOperation}:{request.ProposalId:D}:{request.IdempotencyKey}",
            ct).ConfigureAwait(false);

        var replay = await ReadReceiptAsync<CorporateActionSourceProposalAcceptanceResultDto>(
            connection, transaction, AcceptOperation, request.ProposalId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var proposal = await LoadProposalAsync(connection, transaction, request.ProposalId, forUpdate: true, ct)
            .ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Corporate-action source proposal", request.ProposalId);

        await PostgresCorporateActionCanonicalStore.AcquireSecurityChainLockAsync(
            connection,
            transaction,
            proposal.SecurityId,
            ct).ConfigureAwait(false);

        EnsureVersion(proposal.ProposalId, request.ExpectedVersion, proposal.Version);
        if (!CorporateActionSourceProposalStates.CanDecide(proposal.State))
        {
            throw new CorporateActionStateConflictException(
                proposal.ProposalId,
                $"Source proposal '{proposal.ProposalId:D}' in state '{proposal.State}' cannot be accepted.");
        }

        var now = DateTimeOffset.UtcNow;
        var canonicalSupersedesId = await ResolveCanonicalSupersedeAsync(
                connection, transaction, proposal, ct)
            .ConfigureAwait(false);
        var candidateCorporateAction = proposal.ProposedAction with
        {
            CorpActId = corporateActionId,
            SupersedesCorpActId = canonicalSupersedesId,
        };
        var candidateEconomicFingerprint = CorporateActionEconomicFingerprint.Compute(candidateCorporateAction);
        if (!string.Equals(
                candidateEconomicFingerprint,
                proposal.EconomicFingerprint,
                StringComparison.Ordinal))
        {
            throw new CorporateActionSourceConflictException(
                "The retained proposal fingerprint does not match its canonical economic terms; retain corrected source evidence before acceptance.");
        }

        var existingCorporateAction = await PostgresCorporateActionCanonicalStore.LoadOrReconcileByEconomicIdentityAsync(
                connection,
                transaction,
                Qualified("corporate_actions"),
                proposal.SecurityId,
                candidateEconomicFingerprint,
                candidateCorporateAction.LifecycleState,
                canonicalSupersedesId,
                ct)
            .ConfigureAwait(false);
        var corporateAction = existingCorporateAction ?? candidateCorporateAction;
        await PostgresCorporateActionCanonicalStore.ValidateSuccessorAsync(
                connection, transaction, Qualified("corporate_actions"), corporateAction, ct)
            .ConfigureAwait(false);
        if (existingCorporateAction is null)
        {
            await InsertCorporateActionAsync(
                    connection, transaction, corporateAction, candidateEconomicFingerprint, ct)
                .ConfigureAwait(false);
        }

        var acceptedCorporateActionId = corporateAction.CorpActId;
        await LinkCanonicalSourceAsync(
            connection, transaction, acceptedCorporateActionId, proposal, now, ct).ConfigureAwait(false);

        var existingCase = await LoadCaseByCanonicalScopeAsync(
                connection, transaction, acceptedCorporateActionId, request.Scope, ct)
            .ConfigureAwait(false);
        CorporateActionProcessingCaseDto processingCase;
        CorporateActionCaseTransitionDto transition;
        SecurityMasterCorporateActionRestatementDto? acceptedRestatement;
        var createdCase = existingCase is null;
        if (existingCase is null)
        {
            processingCase = new CorporateActionProcessingCaseDto(
                caseId,
                proposal.ProposalId,
                acceptedCorporateActionId,
                proposal.SecurityId,
                request.Scope,
                CorporateActionCaseStates.Detected,
                Version: 1,
                request.MethodologyProfileId,
                AssignedTo: null,
                BlockedReason: null,
                CreatedBy: request.Actor,
                CreatedAtUtc: now,
                UpdatedBy: request.Actor,
                UpdatedAtUtc: now,
                SourceSnapshot: new CorporateActionCaseSourceSnapshotDto(
                    proposal.ProposedAction,
                    proposal.ProviderIdentity,
                    proposal.DisplayMetadata));
            await InsertCaseAsync(connection, transaction, processingCase, ct).ConfigureAwait(false);
            if (restatement is not null)
            {
                await InsertRestatementObligationAsync(
                    connection, transaction, processingCase, restatement, now, ct).ConfigureAwait(false);
            }

            acceptedRestatement = restatement;
            transition = new CorporateActionCaseTransitionDto(
                transitionId,
                caseId,
                FromState: null,
                ToState: CorporateActionCaseStates.Detected,
                ExpectedVersion: 0,
                ResultingVersion: 1,
                Actor: request.Actor,
                Reason: string.IsNullOrWhiteSpace(request.Reason)
                    ? "Accepted provider source proposal into governed processing case."
                    : request.Reason.Trim(),
                IdempotencyKey: request.IdempotencyKey,
                OccurredAtUtc: now,
                CorrelationId: request.CorrelationId);
            await InsertTransitionAsync(
                connection, transaction, AcceptOperation, transition, ct).ConfigureAwait(false);
        }
        else
        {
            if (existingCase.CaseId != caseId
                || !string.Equals(
                    existingCase.MethodologyProfileId,
                    request.MethodologyProfileId,
                    StringComparison.Ordinal))
            {
                throw new CorporateActionStateConflictException(
                    existingCase.CaseId,
                    "The canonical action already has a case for this full scope with different server identity or methodology.");
            }

            processingCase = existingCase;
            transition = await LoadInitialCaseTransitionAsync(
                    connection, transaction, existingCase.CaseId, ct)
                .ConfigureAwait(false)
                ?? throw new CorporateActionStateConflictException(
                    existingCase.CaseId,
                    "The existing canonical scoped case has no durable initial transition.");
            acceptedRestatement = await LoadRestatementObligationAsync(
                    connection, transaction, existingCase.CaseId, ct)
                .ConfigureAwait(false);
        }

        var dissent = await AttachProviderDissentConflictAsync(
                connection, transaction, processingCase, proposal, request.Actor, now, createdCase, ct)
            .ConfigureAwait(false);
        processingCase = dissent.Case;

        var decidedProposal = proposal with
        {
            State = CorporateActionSourceProposalStates.Accepted,
            Version = proposal.Version + 1,
            AcceptedCorporateActionId = acceptedCorporateActionId,
            InitialCaseId = processingCase.CaseId,
            UpdatedAtUtc = now,
            DecisionBy = request.Actor,
            DecisionAtUtc = now,
            DecisionReason = request.Reason,
            CorrelationId = request.CorrelationId,
        };

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                $"""
                update {Qualified("corporate_action_source_proposals")}
                set state = @state,
                    version = @resulting_version,
                    accepted_corp_act_id = @corp_act_id,
                    initial_case_id = @case_id,
                    updated_at = @updated_at,
                    decision_by = @decision_by,
                    decision_at = @decision_at,
                    decision_reason = @decision_reason,
                    correlation_id = @correlation_id
                where proposal_id = @proposal_id
                  and version = @expected_version;
                """;
            update.Parameters.AddWithValue("state", decidedProposal.State);
            update.Parameters.AddWithValue("resulting_version", decidedProposal.Version);
            update.Parameters.AddWithValue("corp_act_id", acceptedCorporateActionId);
            update.Parameters.AddWithValue("case_id", processingCase.CaseId);
            update.Parameters.AddWithValue("updated_at", now.UtcDateTime);
            update.Parameters.AddWithValue("decision_by", request.Actor);
            update.Parameters.AddWithValue("decision_at", now.UtcDateTime);
            update.Parameters.AddWithValue("decision_reason", (object?)request.Reason ?? DBNull.Value);
            update.Parameters.AddWithValue("correlation_id", (object?)request.CorrelationId ?? DBNull.Value);
            update.Parameters.AddWithValue("proposal_id", proposal.ProposalId);
            update.Parameters.AddWithValue("expected_version", request.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new CorporateActionVersionConflictException(
                    proposal.ProposalId,
                    request.ExpectedVersion,
                    proposal.Version);
            }
        }

        var audit = new SecurityMasterCorporateActionAuditDto(
            AuditId: $"security-master-corporate-action:{acceptedCorporateActionId:D}:proposal:{proposal.ProposalId:D}",
            SecurityId: proposal.SecurityId,
            CorporateActionId: acceptedCorporateActionId,
            EventType: corporateAction.EventType,
            SourceSystem: proposal.ProviderIdentity.ProviderId,
            Actor: request.Actor,
            RecordedAtUtc: now,
            SourceRecordId: proposal.ProviderIdentity.SourceEventId,
            Reason: request.Reason,
            CorrelationId: request.CorrelationId);

        var result = new CorporateActionSourceProposalAcceptanceResultDto(
            decidedProposal,
            corporateAction,
            processingCase,
            transition,
            audit,
            acceptedRestatement,
            Replayed: false,
            SourceConflict: dissent.Conflict);
        await WriteReceiptAsync(
            connection, transaction, AcceptOperation, proposal.ProposalId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private async Task<(CorporateActionProcessingCaseDto Case, CorporateActionConflictDto? Conflict)> AttachProviderDissentConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase,
        CorporateActionSourceProposalDto proposal,
        string actor,
        DateTimeOffset now,
        bool createdCase,
        CancellationToken ct)
    {
        var dissentingSources = proposal.DisplayMetadata?.DissentingSources
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (dissentingSources.Length == 0)
        {
            return (processingCase, null);
        }

        if (!CorporateActionDissentEvidencePolicy.HasCompleteFieldCandidates(
                proposal.DisplayMetadata,
                proposal.ProviderIdentity.ProviderId))
        {
            throw new CorporateActionSourceConflictException(
                "Provider dissent cannot be accepted until each differing field retains actual per-source values and typed evidence references.");
        }

        var dissentingFields = proposal.DisplayMetadata?.DissentingFields?.Where(static field =>
                !string.IsNullOrWhiteSpace(field.Field)
                && field.Candidates.Count >= 2
                && field.Candidates
                    .Select(static candidate => candidate.Value.GetRawText())
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .Count() == 2)
            .GroupBy(static field => field.Field.Trim(), StringComparer.Ordinal)
            .Select(static group => group.First() with { Field = group.Key })
            .ToArray() ?? [];

        if (!createdCase && processingCase.State is not (
                CorporateActionCaseStates.Detected or CorporateActionCaseStates.NeedsTerms or
                CorporateActionCaseStates.Disputed or CorporateActionCaseStates.Blocked or
                CorporateActionCaseStates.RestatementRequired))
        {
            throw new CorporateActionStateConflictException(
                processingCase.CaseId,
                $"Provider dissent cannot be attached to case state '{processingCase.State}'; use the governed reopen or restatement path first.");
        }

        if (!createdCase)
        {
            processingCase = await UpdateCaseVersionAsync(
                connection, transaction, processingCase, processingCase.Version, actor,
                processingCase.State, processingCase.AssignedTo, processingCase.BlockedReason,
                now, ct).ConfigureAwait(false);
        }

        CorporateActionConflictDto? firstConflict = null;
        foreach (var field in dissentingFields)
        {
            var conflictId = StablePersistenceId(
                $"source-dissent-conflict:{field.Field}", proposal.ProposalId);
            var description =
                $"Provider observations disagree on '{field.Field}'; the field-level conflict must be resolved or waived before terms confirmation.";
            var conflict = new CorporateActionConflictDto(
                conflictId,
                processingCase.CaseId,
                field.Field,
                description,
                field.Candidates,
                CorporateActionConflictStates.Open,
                Resolution: null,
                processingCase.Version,
                actor,
                now);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("corporate_action_case_conflicts")} (
                    conflict_id, case_id, field_name, description, candidates, state, resolution,
                    case_version, recorded_by, recorded_at)
                values (
                    @conflict_id, @case_id, @field_name, @description, @candidates, @state, null,
                    @case_version, @recorded_by, @recorded_at);
                """;
            command.Parameters.AddWithValue("conflict_id", conflict.ConflictId);
            command.Parameters.AddWithValue("case_id", conflict.CaseId);
            command.Parameters.AddWithValue("field_name", conflict.Field);
            command.Parameters.AddWithValue("description", conflict.Description);
            AddJson(command, "candidates", conflict.Candidates);
            command.Parameters.AddWithValue("state", conflict.State);
            command.Parameters.AddWithValue("case_version", conflict.CaseVersion);
            command.Parameters.AddWithValue("recorded_by", conflict.RecordedBy);
            command.Parameters.AddWithValue("recorded_at", conflict.RecordedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            firstConflict ??= conflict;
        }

        return (processingCase, firstConflict);
    }

    public async Task<CorporateActionSourceProposalDecisionResultDto> RejectSourceProposalAsync(
        RejectCorporateActionSourceProposalRequestDto request,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RejectSourceProposalOnceAsync(request, requestFingerprint, ct)
                    .ConfigureAwait(false);
            }
            catch (PostgresException exception) when (
                (IsRetryableConcurrencyFailure(exception)
                 || exception.SqlState == PostgresErrorCodes.UniqueViolation)
                && attempt < MaximumSerializableAttempts)
            {
                // A fresh serializable transaction re-acquires the proposal/command locks and
                // re-reads the durable receipt. Identical concurrent rejection commands therefore
                // replay, while a competing decision observes the committed version/state.
                ct.ThrowIfCancellationRequested();
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new CorporateActionStateConflictException(
                    request.ProposalId,
                    "Corporate-action proposal rejection collided with an existing durable command; reload the proposal before retrying.");
            }
            catch (PostgresException exception) when (IsRetryableConcurrencyFailure(exception))
            {
                throw new CorporateActionPersistenceUnavailableException(
                    $"Corporate-action proposal rejection remained contended after {MaximumSerializableAttempts} serializable attempts.");
            }
            catch (NpgsqlException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action proposal rejection persistence is temporarily unavailable; reload before retrying.");
            }
            catch (TimeoutException)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "Corporate-action proposal rejection timed out; reload its durable receipt before retrying.");
            }
        }
    }

    private async Task<CorporateActionSourceProposalDecisionResultDto> RejectSourceProposalOnceAsync(
        RejectCorporateActionSourceProposalRequestDto request,
        string requestFingerprint,
        CancellationToken ct)
    {

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireTransactionLockAsync(
            connection,
            transaction,
            $"corporate-action-proposal:{request.ProposalId:D}",
            ct).ConfigureAwait(false);
        await AcquireTransactionLockAsync(
            connection,
            transaction,
            $"corporate-action-command:{RejectOperation}:{request.ProposalId:D}:{request.IdempotencyKey}",
            ct).ConfigureAwait(false);

        var replay = await ReadReceiptAsync<CorporateActionSourceProposalDecisionResultDto>(
            connection, transaction, RejectOperation, request.ProposalId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var proposal = await LoadProposalAsync(connection, transaction, request.ProposalId, forUpdate: true, ct)
            .ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Corporate-action source proposal", request.ProposalId);
        EnsureVersion(proposal.ProposalId, request.ExpectedVersion, proposal.Version);
        if (!CorporateActionSourceProposalStates.CanDecide(proposal.State))
        {
            throw new CorporateActionStateConflictException(
                proposal.ProposalId,
                $"Source proposal '{proposal.ProposalId:D}' in state '{proposal.State}' cannot be rejected.");
        }

        var now = DateTimeOffset.UtcNow;
        var rejected = proposal with
        {
            State = CorporateActionSourceProposalStates.Rejected,
            Version = proposal.Version + 1,
            UpdatedAtUtc = now,
            DecisionBy = request.Actor,
            DecisionAtUtc = now,
            DecisionReason = request.Reason,
            CorrelationId = request.CorrelationId,
        };

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                $"""
                update {Qualified("corporate_action_source_proposals")}
                set state = @state,
                    version = @resulting_version,
                    updated_at = @updated_at,
                    decision_by = @decision_by,
                    decision_at = @decision_at,
                    decision_reason = @decision_reason,
                    correlation_id = @correlation_id
                where proposal_id = @proposal_id
                  and version = @expected_version;
                """;
            update.Parameters.AddWithValue("state", rejected.State);
            update.Parameters.AddWithValue("resulting_version", rejected.Version);
            update.Parameters.AddWithValue("updated_at", now.UtcDateTime);
            update.Parameters.AddWithValue("decision_by", request.Actor);
            update.Parameters.AddWithValue("decision_at", now.UtcDateTime);
            update.Parameters.AddWithValue("decision_reason", request.Reason);
            update.Parameters.AddWithValue("correlation_id", (object?)request.CorrelationId ?? DBNull.Value);
            update.Parameters.AddWithValue("proposal_id", proposal.ProposalId);
            update.Parameters.AddWithValue("expected_version", request.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new CorporateActionVersionConflictException(
                    proposal.ProposalId,
                    request.ExpectedVersion,
                    proposal.Version);
            }
        }

        var result = new CorporateActionSourceProposalDecisionResultDto(rejected, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, RejectOperation, proposal.ProposalId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    internal static string SourceEventLockScope(CorporateActionProviderEventIdentityDto identity) =>
        $"corporate-action-source-chain:{identity.ProviderId.Length}:{identity.ProviderId}:" +
        $"{identity.SourceEventId.Length}:{identity.SourceEventId}";

    internal static CorporateActionSourceProposalDto BindExactSourceReplay(
        CorporateActionSourceProposalDto existing,
        CorporateActionSourceProposalDto candidate)
    {
        if (candidate.SupersedesProposalId.HasValue)
        {
            return candidate;
        }

        return candidate with
        {
            SupersedesProposalId = existing.SupersedesProposalId,
            ProposedAction = candidate.ProposedAction.SupersedesCorpActId.HasValue
                ? candidate.ProposedAction
                : candidate.ProposedAction with
                {
                    SupersedesCorpActId = existing.ProposedAction.SupersedesCorpActId,
                },
        };
    }

    internal static CorporateActionSourceProposalDto BindNewSourceRevision(
        CorporateActionSourceProposalDto currentTip,
        CorporateActionSourceProposalDto candidate)
    {
        if (currentTip.SecurityId != candidate.SecurityId
            || !string.Equals(
                currentTip.ProviderIdentity.ProviderId,
                candidate.ProviderIdentity.ProviderId,
                StringComparison.Ordinal)
            || !string.Equals(
                currentTip.ProviderIdentity.SourceEventId,
                candidate.ProviderIdentity.SourceEventId,
                StringComparison.Ordinal))
        {
            throw new CorporateActionSourceConflictException(
                "A source revision must remain in the same provider event and security amendment chain.");
        }

        if (candidate.SupersedesProposalId is { } declaredParentId
            && declaredParentId != currentTip.ProposalId)
        {
            throw new CorporateActionStateConflictException(
                declaredParentId,
                "The declared source-proposal parent is stale; revisions must supersede the current provider-event tip.");
        }

        if (candidate.ProviderIdentity.ObservedAtUtc < currentTip.ProviderIdentity.ObservedAtUtc)
        {
            throw new CorporateActionStateConflictException(
                currentTip.ProposalId,
                "An older provider observation cannot supersede a newer revision of the same event.");
        }

        var canonicalAncestorId = currentTip.AcceptedCorporateActionId
                                  ?? currentTip.ProposedAction.SupersedesCorpActId;
        if (candidate.ProposedAction.SupersedesCorpActId is { } declaredCanonicalId
            && declaredCanonicalId != canonicalAncestorId)
        {
            throw new CorporateActionSourceConflictException(
                "The correction's declared canonical predecessor does not match its nearest accepted source-proposal ancestor.");
        }

        return candidate with
        {
            SupersedesProposalId = currentTip.ProposalId,
            ProposedAction = candidate.ProposedAction with
            {
                SupersedesCorpActId = canonicalAncestorId,
            },
        };
    }

    private async Task InsertProposalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionSourceProposalDto proposal,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_source_proposals")} (
                proposal_id, security_id, provider_id, source_event_id, source_event_version,
                observed_at, evidence_hash, evidence_reference, provider_release_status, payload_schema_version,
                economic_fingerprint, proposed_action, display_ticker, winning_source,
                agreeing_sources, dissenting_sources, dissent_fields, state, version, supersedes_proposal_id,
                accepted_corp_act_id, initial_case_id, recorded_by, recorded_at, updated_at,
                decision_by, decision_at, decision_reason, correlation_id)
            values (
                @proposal_id, @security_id, @provider_id, @source_event_id, @source_event_version,
                @observed_at, @evidence_hash, @evidence_reference, @provider_release_status, @payload_schema_version,
                @economic_fingerprint, @proposed_action, @display_ticker, @winning_source,
                @agreeing_sources, @dissenting_sources, @dissent_fields, @state, @version, @supersedes_proposal_id,
                @accepted_corp_act_id, @initial_case_id, @recorded_by, @recorded_at, @updated_at,
                @decision_by, @decision_at, @decision_reason, @correlation_id);
            """;
        command.Parameters.AddWithValue("proposal_id", proposal.ProposalId);
        command.Parameters.AddWithValue("security_id", proposal.SecurityId);
        command.Parameters.AddWithValue("provider_id", proposal.ProviderIdentity.ProviderId);
        command.Parameters.AddWithValue("source_event_id", proposal.ProviderIdentity.SourceEventId);
        command.Parameters.AddWithValue("source_event_version", proposal.ProviderIdentity.SourceEventVersion);
        command.Parameters.AddWithValue("observed_at", proposal.ProviderIdentity.ObservedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("evidence_hash", (object?)proposal.ProviderIdentity.EvidenceHash ?? DBNull.Value);
        command.Parameters.AddWithValue("evidence_reference", (object?)proposal.ProviderIdentity.EvidenceReference ?? DBNull.Value);
        command.Parameters.AddWithValue("provider_release_status", proposal.ProviderIdentity.ReleaseStatus.ToString());
        command.Parameters.AddWithValue("payload_schema_version", proposal.PayloadSchemaVersion);
        command.Parameters.AddWithValue("economic_fingerprint", proposal.EconomicFingerprint);
        AddJson(command, "proposed_action", proposal.ProposedAction);
        command.Parameters.AddWithValue("display_ticker", (object?)proposal.DisplayMetadata?.Ticker ?? DBNull.Value);
        command.Parameters.AddWithValue("winning_source", (object?)proposal.DisplayMetadata?.WinningSource ?? DBNull.Value);
        AddJson(command, "agreeing_sources", proposal.DisplayMetadata?.AgreeingSources ?? []);
        AddJson(command, "dissenting_sources", proposal.DisplayMetadata?.DissentingSources ?? []);
        AddJson(command, "dissent_fields", proposal.DisplayMetadata?.DissentingFields ?? []);
        command.Parameters.AddWithValue("state", proposal.State);
        command.Parameters.AddWithValue("version", proposal.Version);
        command.Parameters.AddWithValue("supersedes_proposal_id", (object?)proposal.SupersedesProposalId ?? DBNull.Value);
        command.Parameters.AddWithValue("accepted_corp_act_id", (object?)proposal.AcceptedCorporateActionId ?? DBNull.Value);
        command.Parameters.AddWithValue("initial_case_id", (object?)proposal.InitialCaseId ?? DBNull.Value);
        command.Parameters.AddWithValue("recorded_by", proposal.RecordedBy);
        command.Parameters.AddWithValue("recorded_at", proposal.RecordedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", proposal.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("decision_by", (object?)proposal.DecisionBy ?? DBNull.Value);
        command.Parameters.AddWithValue("decision_at", (object?)proposal.DecisionAtUtc?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("decision_reason", (object?)proposal.DecisionReason ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)proposal.CorrelationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveCanonicalSupersedeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionSourceProposalDto proposal,
        CancellationToken ct)
    {
        var declaredCanonicalParent = proposal.ProposedAction.SupersedesCorpActId;
        if (proposal.SupersedesProposalId is not { } sourceParentId)
        {
            return declaredCanonicalParent;
        }

        Guid? acceptedCanonicalParent = null;
        var visited = new HashSet<Guid>();
        var currentId = sourceParentId;
        while (true)
        {
            if (!visited.Add(currentId))
            {
                throw new CorporateActionSourceConflictException(
                    "The source-proposal amendment chain contains a cycle.");
            }

            var sourceAncestor = await LoadProposalAsync(
                    connection, transaction, currentId, forUpdate: true, ct)
                .ConfigureAwait(false)
                ?? throw new CorporateActionNotFoundException(
                    "Superseded corporate-action source proposal", currentId);
            if (sourceAncestor.SecurityId != proposal.SecurityId
                || !string.Equals(
                    sourceAncestor.ProviderIdentity.ProviderId,
                    proposal.ProviderIdentity.ProviderId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    sourceAncestor.ProviderIdentity.SourceEventId,
                    proposal.ProviderIdentity.SourceEventId,
                    StringComparison.Ordinal))
            {
                throw new CorporateActionSourceConflictException(
                    "A source correction must remain in the same provider event and security amendment chain.");
            }

            if (sourceAncestor.AcceptedCorporateActionId is { } acceptedId)
            {
                acceptedCanonicalParent = acceptedId;
                break;
            }

            if (sourceAncestor.SupersedesProposalId is not { } nextId)
            {
                break;
            }

            currentId = nextId;
        }

        if (declaredCanonicalParent is { } declared && declared != acceptedCanonicalParent)
        {
            throw new CorporateActionSourceConflictException(
                "The correction's declared canonical predecessor does not match its nearest accepted source-proposal ancestor.");
        }

        return acceptedCanonicalParent;
    }

    private async Task InsertRestatementObligationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase,
        SecurityMasterCorporateActionRestatementDto restatement,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_restatement_obligations")} (
                obligation_id, case_id, corp_act_id, tenant_id, company_id, scope,
                restatement_required, candidates, status, recorded_at)
            values (
                @obligation_id, @case_id, @corp_act_id, @tenant_id, @company_id, @scope,
                @restatement_required, @candidates, @status, @recorded_at);
            """;
        command.Parameters.AddWithValue(
            "obligation_id",
            StablePersistenceId("restatement-obligation", processingCase.CaseId));
        command.Parameters.AddWithValue("case_id", processingCase.CaseId);
        command.Parameters.AddWithValue("corp_act_id", processingCase.CorporateActionId);
        command.Parameters.AddWithValue("tenant_id", processingCase.Scope.TenantId);
        command.Parameters.AddWithValue("company_id", processingCase.Scope.CompanyId);
        AddJson(command, "scope", processingCase.Scope);
        command.Parameters.AddWithValue("restatement_required", restatement.RestatementRequired);
        AddJson(command, "candidates", restatement.Candidates);
        command.Parameters.AddWithValue("status", restatement.EvaluationStatus);
        command.Parameters.AddWithValue("recorded_at", recordedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static Guid StablePersistenceId(string purpose, Guid aggregateId)
    {
        var digest = Sha256Digest.ComputeUtf8(
            $"corporate-action:{purpose}:v1:{aggregateId:D}");
        return Guid.ParseExact(digest[..32], "N");
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
                corp_act_id, security_id, event_type, ex_date, pay_date, dividend_per_share,
                currency, split_ratio, new_security_id, distribution_ratio, acquirer_security_id,
                exchange_ratio, subscription_price_per_share, rights_per_share, record_date,
                lifecycle_state, supersedes_corp_act_id, redemption_price_percent_of_par, payload,
                payload_schema_version, economic_fingerprint)
            values (
                @corp_act_id, @security_id, @event_type, @ex_date, @pay_date, @dividend_per_share,
                @currency, @split_ratio, @new_security_id, @distribution_ratio, @acquirer_security_id,
                @exchange_ratio, @subscription_price_per_share, @rights_per_share, @record_date,
                @lifecycle_state, @supersedes_corp_act_id, @redemption_price_percent_of_par, @payload,
                @payload_schema_version, @economic_fingerprint);
            """;
        AddCorporateActionParameters(command, action);
        command.Parameters.AddWithValue("economic_fingerprint", economicFingerprint);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertCaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_processing_cases")} (
                case_id, proposal_id, corp_act_id, security_id, tenant_id, company_id,
                structure_node_id, fund_profile_id, financial_account_id, portfolio_id,
                custody_account_id, ledger_book_id, period_id, accounting_basis,
                functional_currency, jurisdiction, state, version, methodology_profile_id,
                assigned_to, blocked_reason, created_by, created_at, updated_by, updated_at)
            values (
                @case_id, @proposal_id, @corp_act_id, @security_id, @tenant_id, @company_id,
                @structure_node_id, @fund_profile_id, @financial_account_id, @portfolio_id,
                @custody_account_id, @ledger_book_id, @period_id, @accounting_basis,
                @functional_currency, @jurisdiction, @state, @version, @methodology_profile_id,
                @assigned_to, @blocked_reason, @created_by, @created_at, @updated_by, @updated_at);
            """;
        AddCaseParameters(command, processingCase);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task LinkCanonicalSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid corporateActionId,
        CorporateActionSourceProposalDto proposal,
        DateTimeOffset linkedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_canonical_sources")} (
                corp_act_id, proposal_id, provider_id, source_event_id, source_event_version, linked_at)
            values (
                @corp_act_id, @proposal_id, @provider_id, @source_event_id, @source_event_version, @linked_at);
            """;
        command.Parameters.AddWithValue("corp_act_id", corporateActionId);
        command.Parameters.AddWithValue("proposal_id", proposal.ProposalId);
        command.Parameters.AddWithValue("provider_id", proposal.ProviderIdentity.ProviderId);
        command.Parameters.AddWithValue("source_event_id", proposal.ProviderIdentity.SourceEventId);
        command.Parameters.AddWithValue("source_event_version", proposal.ProviderIdentity.SourceEventVersion);
        command.Parameters.AddWithValue("linked_at", linkedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<CorporateActionProcessingCaseDto?> LoadCaseByCanonicalScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid corporateActionId,
        CorporateActionCaseScopeDto scope,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            {CaseSelect}
            where pc.corp_act_id = @corp_act_id
              and pc.tenant_id = @tenant_id
              and pc.company_id = @company_id
              and pc.structure_node_id is not distinct from @structure_node_id
              and pc.fund_profile_id is not distinct from @fund_profile_id
              and pc.financial_account_id is not distinct from @financial_account_id
              and pc.portfolio_id is not distinct from @portfolio_id
              and pc.custody_account_id is not distinct from @custody_account_id
              and pc.ledger_book_id is not distinct from @ledger_book_id
              and pc.period_id is not distinct from @period_id
              and pc.accounting_basis is not distinct from @accounting_basis
              and pc.functional_currency is not distinct from @functional_currency
              and pc.jurisdiction is not distinct from @jurisdiction
            limit 1
            for update of pc;
            """;
        command.Parameters.AddWithValue("corp_act_id", corporateActionId);
        command.Parameters.AddWithValue("tenant_id", scope.TenantId);
        command.Parameters.AddWithValue("company_id", scope.CompanyId);
        AddNullableText(command, "structure_node_id", scope.StructureNodeId);
        AddNullableText(command, "fund_profile_id", scope.FundProfileId);
        AddNullableText(command, "financial_account_id", scope.FinancialAccountId);
        AddNullableText(command, "portfolio_id", scope.PortfolioId);
        AddNullableText(command, "custody_account_id", scope.CustodyAccountId);
        AddNullableText(command, "ledger_book_id", scope.LedgerBookId);
        AddNullableText(command, "period_id", scope.PeriodId);
        AddNullableText(command, "accounting_basis", scope.AccountingBasis);
        AddNullableText(command, "functional_currency", scope.FunctionalCurrency);
        AddNullableText(command, "jurisdiction", scope.Jurisdiction);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadCase(reader) : null;
    }

    private async Task<CorporateActionCaseTransitionDto?> LoadInitialCaseTransitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid caseId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select transition_id, case_id, from_state, to_state, expected_version,
                   resulting_version, actor, reason, idempotency_key, occurred_at,
                   correlation_id, policy_override_applied
            from {Qualified("corporate_action_case_transitions")}
            where case_id = @case_id
              and from_state is null
            order by resulting_version, occurred_at
            limit 1;
            """;
        command.Parameters.AddWithValue("case_id", caseId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new CorporateActionCaseTransitionDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            ReadTimestamp(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetBoolean(11));
    }

    private async Task<SecurityMasterCorporateActionRestatementDto?> LoadRestatementObligationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid caseId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select restatement_required, candidates::text, status
            from {Qualified("corporate_action_restatement_obligations")}
            where case_id = @case_id;
            """;
        command.Parameters.AddWithValue("case_id", caseId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var candidates = JsonSerializer.Deserialize<IReadOnlyList<Meridian.Contracts.Workstation.RestatementCandidateDto>>(
                reader.GetString(1), JsonOptions)
            ?? [];
        return new SecurityMasterCorporateActionRestatementDto(
            reader.GetBoolean(0),
            candidates,
            reader.GetString(2));
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });
    }

    private async Task InsertTransitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string operationKind,
        CorporateActionCaseTransitionDto transition,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_case_transitions")} (
                transition_id, case_id, operation_kind, from_state, to_state, expected_version, resulting_version,
                actor, reason, idempotency_key, occurred_at, correlation_id, policy_override_applied)
            values (
                @transition_id, @case_id, @operation_kind, @from_state, @to_state, @expected_version, @resulting_version,
                @actor, @reason, @idempotency_key, @occurred_at, @correlation_id, @policy_override_applied);
            """;
        command.Parameters.AddWithValue("transition_id", transition.TransitionId);
        command.Parameters.AddWithValue("case_id", transition.CaseId);
        command.Parameters.AddWithValue("operation_kind", operationKind);
        command.Parameters.AddWithValue("from_state", (object?)transition.FromState ?? DBNull.Value);
        command.Parameters.AddWithValue("to_state", transition.ToState);
        command.Parameters.AddWithValue("expected_version", transition.ExpectedVersion);
        command.Parameters.AddWithValue("resulting_version", transition.ResultingVersion);
        command.Parameters.AddWithValue("actor", transition.Actor);
        command.Parameters.AddWithValue("reason", transition.Reason);
        command.Parameters.AddWithValue("idempotency_key", transition.IdempotencyKey);
        command.Parameters.AddWithValue("occurred_at", transition.OccurredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("correlation_id", (object?)transition.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("policy_override_applied", transition.PolicyOverrideApplied);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<CorporateActionSourceProposalDto?> LoadProposalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid proposalId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            {ProposalSelect}
            where proposal_id = @proposal_id
            {(forUpdate ? "for update" : string.Empty)};
            """;
        command.Parameters.AddWithValue("proposal_id", proposalId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadProposal(reader) : null;
    }

    private async Task<CorporateActionSourceProposalDto?> LoadProposalBySourceIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string providerId,
        string sourceEventId,
        string sourceEventVersion,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            {ProposalSelect}
            where provider_id = @provider_id
              and source_event_id = @source_event_id
              and source_event_version = @source_event_version
            {(forUpdate ? "for update" : string.Empty)};
            """;
        command.Parameters.AddWithValue("provider_id", providerId);
        command.Parameters.AddWithValue("source_event_id", sourceEventId);
        command.Parameters.AddWithValue("source_event_version", sourceEventVersion);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadProposal(reader) : null;
    }

    private async Task<CorporateActionSourceProposalDto?> LoadCurrentSourceRevisionTipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string providerId,
        string sourceEventId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            {ProposalSelect}
            where provider_id = @provider_id
              and source_event_id = @source_event_id
              and state <> @superseded_state
            order by observed_at desc, recorded_at desc, proposal_id desc
            limit 2
            for update;
            """;
        command.Parameters.AddWithValue("provider_id", providerId);
        command.Parameters.AddWithValue("source_event_id", sourceEventId);
        command.Parameters.AddWithValue("superseded_state", CorporateActionSourceProposalStates.Superseded);

        var tips = new List<CorporateActionSourceProposalDto>(2);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            tips.Add(ReadProposal(reader));
        }

        return tips.Count switch
        {
            0 => null,
            1 => tips[0],
            _ => throw new CorporateActionSourceConflictException(
                "Multiple live tips exist for one provider event amendment chain; persistence is quarantined until repaired."),
        };
    }

    private async Task<bool> HasSourceSuccessorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parentProposalId,
        Guid candidateProposalId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select exists (
                select 1
                from {Qualified("corporate_action_source_proposals")}
                where supersedes_proposal_id = @parent_proposal_id
                  and proposal_id <> @candidate_proposal_id);
            """;
        command.Parameters.AddWithValue("parent_proposal_id", parentProposalId);
        command.Parameters.AddWithValue("candidate_proposal_id", candidateProposalId);
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
    }

    private async Task<CorporateActionSourceProposalDto> ReconcileSourceIdentityCollisionAsync(
        CorporateActionSourceProposalDto proposal,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var existing = await LoadProposalBySourceIdentityAsync(
                connection,
                transaction: null,
                proposal.ProviderIdentity.ProviderId,
                proposal.ProviderIdentity.SourceEventId,
                proposal.ProviderIdentity.SourceEventVersion,
                forUpdate: false,
                ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            throw new CorporateActionStateConflictException(
                proposal.ProposalId,
                "A corporate-action proposal uniqueness collision occurred outside the provider event identity; reload and retry with a new ProposalId.");
        }

        var replayCandidate = BindExactSourceReplay(existing, proposal);
        if (!CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, replayCandidate))
        {
            throw new CorporateActionIdempotencyConflictException(
                existing.ProposalId,
                $"{proposal.ProviderIdentity.ProviderId}:{proposal.ProviderIdentity.SourceEventId}:{proposal.ProviderIdentity.SourceEventVersion}");
        }

        return existing;
    }

    private static CorporateActionSourceProposalDto ReadProposal(NpgsqlDataReader reader)
    {
        var action = JsonSerializer.Deserialize<CorporateActionDto>(reader.GetString(10), JsonOptions)
            ?? throw new InvalidOperationException("Stored corporate-action proposal has no action payload.");
        if (!Enum.TryParse<CorporateActionProviderReleaseStatusDto>(
                reader.GetString(28), ignoreCase: false, out var releaseStatus)
            || !Enum.IsDefined(releaseStatus))
        {
            throw new InvalidOperationException("Stored corporate-action proposal has an unknown provider release status.");
        }

        var identity = new CorporateActionProviderEventIdentityDto(
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ReadTimestamp(reader, 5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            releaseStatus);
        var displayMetadata = reader.IsDBNull(11) && reader.IsDBNull(12) && reader.IsDBNull(27)
            ? null
            : new CorporateActionSourceDisplayMetadataDto(
                reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                reader.IsDBNull(12) ? identity.ProviderId : reader.GetString(12),
                JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(13), JsonOptions) ?? [],
                JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(14), JsonOptions) ?? [],
                reader.IsDBNull(27)
                    ? []
                    : JsonSerializer.Deserialize<IReadOnlyList<CorporateActionDissentFieldDto>>(
                        reader.GetString(27), JsonOptions) ?? []);
        return new CorporateActionSourceProposalDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            identity,
            action,
            reader.GetInt32(8),
            reader.GetString(9),
            reader.GetString(15),
            reader.GetInt64(16),
            reader.IsDBNull(17) ? null : reader.GetGuid(17),
            reader.IsDBNull(18) ? null : reader.GetGuid(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19),
            reader.GetString(20),
            ReadTimestamp(reader, 21),
            ReadTimestamp(reader, 22),
            reader.IsDBNull(23) ? null : reader.GetString(23),
            reader.IsDBNull(24) ? null : ReadTimestamp(reader, 24),
            reader.IsDBNull(25) ? null : reader.GetString(25),
            reader.IsDBNull(26) ? null : reader.GetString(26),
            ActionAvailability: null,
            DisplayMetadata: displayMetadata);
    }

    private async Task<T?> ReadReceiptAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string operation,
        Guid aggregateId,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken ct)
        where T : class
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select request_fingerprint, result_payload::text
            from {Qualified("corporate_action_command_receipts")}
            where operation_kind = @operation_kind
              and aggregate_id = @aggregate_id
              and idempotency_key = @idempotency_key;
            """;
        command.Parameters.AddWithValue("operation_kind", operation);
        command.Parameters.AddWithValue("aggregate_id", aggregateId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        if (!Sha256Digest.FixedEquals(reader.GetString(0), requestFingerprint))
        {
            throw new CorporateActionIdempotencyConflictException(aggregateId, idempotencyKey);
        }

        return JsonSerializer.Deserialize<T>(reader.GetString(1), JsonOptions)
            ?? throw new InvalidOperationException("Stored corporate-action command receipt is malformed.");
    }

    private async Task WriteReceiptAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string operation,
        Guid aggregateId,
        string idempotencyKey,
        string requestFingerprint,
        T result,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_command_receipts")} (
                receipt_id, operation_kind, aggregate_id, idempotency_key, request_fingerprint,
                result_payload, recorded_at)
            values (
                @receipt_id, @operation_kind, @aggregate_id, @idempotency_key, @request_fingerprint,
                @result_payload, @recorded_at);
            """;
        command.Parameters.AddWithValue("receipt_id", Guid.NewGuid());
        command.Parameters.AddWithValue("operation_kind", operation);
        command.Parameters.AddWithValue("aggregate_id", aggregateId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("request_fingerprint", requestFingerprint);
        AddJson(command, "result_payload", result);
        command.Parameters.AddWithValue("recorded_at", recordedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task AcquireTransactionLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string lockScope,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_scope, 0));";
        command.Parameters.AddWithValue("lock_scope", lockScope);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool IsRetryableConcurrencyFailure(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

    private static void EnsureVersion(Guid resourceId, long expectedVersion, long currentVersion)
    {
        if (expectedVersion != currentVersion)
        {
            throw new CorporateActionVersionConflictException(resourceId, expectedVersion, currentVersion);
        }
    }

    private static void AddCorporateActionParameters(NpgsqlCommand command, CorporateActionDto action)
    {
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
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = action.Payload is { ValueKind: not JsonValueKind.Undefined } payload
                ? payload.GetRawText()
                : DBNull.Value,
        });
        command.Parameters.AddWithValue("payload_schema_version", action.PayloadSchemaVersion);
    }

    private static void AddCaseParameters(NpgsqlCommand command, CorporateActionProcessingCaseDto processingCase)
    {
        command.Parameters.AddWithValue("case_id", processingCase.CaseId);
        command.Parameters.AddWithValue("proposal_id", processingCase.ProposalId);
        command.Parameters.AddWithValue("corp_act_id", processingCase.CorporateActionId);
        command.Parameters.AddWithValue("security_id", processingCase.SecurityId);
        command.Parameters.AddWithValue("tenant_id", processingCase.Scope.TenantId);
        command.Parameters.AddWithValue("company_id", processingCase.Scope.CompanyId);
        command.Parameters.AddWithValue("structure_node_id", (object?)processingCase.Scope.StructureNodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("fund_profile_id", (object?)processingCase.Scope.FundProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("financial_account_id", (object?)processingCase.Scope.FinancialAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("portfolio_id", (object?)processingCase.Scope.PortfolioId ?? DBNull.Value);
        command.Parameters.AddWithValue("custody_account_id", (object?)processingCase.Scope.CustodyAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("ledger_book_id", (object?)processingCase.Scope.LedgerBookId ?? DBNull.Value);
        command.Parameters.AddWithValue("period_id", (object?)processingCase.Scope.PeriodId ?? DBNull.Value);
        command.Parameters.AddWithValue("accounting_basis", (object?)processingCase.Scope.AccountingBasis ?? DBNull.Value);
        command.Parameters.AddWithValue("functional_currency", (object?)processingCase.Scope.FunctionalCurrency ?? DBNull.Value);
        command.Parameters.AddWithValue("jurisdiction", (object?)processingCase.Scope.Jurisdiction ?? DBNull.Value);
        command.Parameters.AddWithValue("state", processingCase.State);
        command.Parameters.AddWithValue("version", processingCase.Version);
        command.Parameters.AddWithValue("methodology_profile_id", (object?)processingCase.MethodologyProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("assigned_to", (object?)processingCase.AssignedTo ?? DBNull.Value);
        command.Parameters.AddWithValue("blocked_reason", (object?)processingCase.BlockedReason ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by", processingCase.CreatedBy);
        command.Parameters.AddWithValue("created_at", processingCase.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("updated_by", processingCase.UpdatedBy);
        command.Parameters.AddWithValue("updated_at", processingCase.UpdatedAtUtc.UtcDateTime);
    }

    private static void AddJson<T>(NpgsqlCommand command, string parameterName, T value)
    {
        command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, JsonOptions),
        });
    }

    private static async Task<T> ExecutePersistenceReadAsync<T>(
        Func<Task<T>> read,
        string operation)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            throw new CorporateActionPersistenceUnavailableException(
                $"Corporate-action {operation} is temporarily unavailable.");
        }
        catch (TimeoutException)
        {
            throw new CorporateActionPersistenceUnavailableException(
                $"Corporate-action {operation} timed out before a durable result was available.");
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.PersistenceUnavailable,
                "Security Master corporate-action persistence is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new CorporateActionPersistenceUnavailableException(
                "The corporate-action durable store is unavailable; no mutation was accepted.");
        }
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(reader.GetDateTime(ordinal).ToUniversalTime());

    private string ProposalSelect =>
        $"""
        select proposal_id, security_id, provider_id, source_event_id, source_event_version,
               observed_at, evidence_hash, evidence_reference, payload_schema_version,
               economic_fingerprint, proposed_action::text, display_ticker, winning_source,
               agreeing_sources::text, dissenting_sources::text, state, version, supersedes_proposal_id,
               accepted_corp_act_id, initial_case_id, recorded_by, recorded_at, updated_at,
               decision_by, decision_at, decision_reason, correlation_id, dissent_fields::text,
               provider_release_status
        from {Qualified("corporate_action_source_proposals")}
        """;
}
