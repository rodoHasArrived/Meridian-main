using System.Data;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Durable corporate-action accounting lane: exact-version projection bindings, maker-checker
/// approvals, and immutable posting records. Every mutation shares the case command mechanics —
/// serializable transaction, advisory locks, receipt-first replay, optimistic version bump, and an
/// actor-attributed audit transition — so a failed attempt always leaves the case recoverable in
/// its prior state.
/// </summary>
public sealed partial class PostgresCorporateActionOperationsStore
{
    private const string AttachAccountingProjectionOperation = "AttachAccountingProjection";
    private const string ApproveAccountingOperation = "ApproveCaseAccounting";
    private const string PostAccountingOperation = "PostCaseAccounting";

    public async Task<CorporateActionCaseAccountingProjectionDto?> GetAccountingProjectionAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await LoadCurrentProjectionAsync(
                connection, transaction: null, caseId, tenantId, companyId, ct).ConfigureAwait(false);
        }, "accounting projection read").ConfigureAwait(false);

    public async Task<CorporateActionCaseAccountingApprovalDto?> GetAccountingApprovalAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await LoadActiveApprovalAsync(
                connection, transaction: null, caseId, tenantId, companyId, ct).ConfigureAwait(false);
        }, "accounting approval read").ConfigureAwait(false);

    public async Task<CorporateActionAccountingPostingResultDto?> GetAccountingPostingReceiptAsync(
        Guid caseId,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var receipt = await ReadReceiptAsync<CorporateActionAccountingPostingResultDto>(
                    connection,
                    transaction: null,
                    PostAccountingOperation,
                    caseId,
                    idempotencyKey,
                    requestFingerprint,
                    ct)
                .ConfigureAwait(false);
            return receipt is null ? null : receipt with { Replayed = true };
        }, "accounting posting receipt read").ConfigureAwait(false);

    public async Task<CorporateActionAccountingProjectionMutationResultDto> AttachAccountingProjectionAsync(
        AttachCorporateActionAccountingProjectionRequestDto request,
        CorporateActionCaseAccountingProjectionDto projection,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projection);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "accounting projection attachment",
                retryCt => AttachAccountingProjectionOnceAsync(request, projection, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionAccountingProjectionMutationResultDto> AttachAccountingProjectionOnceAsync(
        AttachCorporateActionAccountingProjectionRequestDto request,
        CorporateActionCaseAccountingProjectionDto projection,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, AttachAccountingProjectionOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionAccountingProjectionMutationResultDto>(
            connection, transaction, AttachAccountingProjectionOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(
                connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
                request.ExpectedVersion, request.ScopeAssertion, ct)
            .ConfigureAwait(false);
        CorporateActionCaseAccountingPolicy.EnsureProjectionAttachable(processingCase);
        CorporateActionCaseAccountingPolicy.EnsureExactAccountingScope(processingCase.Scope);
        CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);

        var now = DateTimeOffset.UtcNow;
        await SupersedeCurrentProjectionAsync(connection, transaction, request.CaseId, now, ct)
            .ConfigureAwait(false);
        await VoidActiveApprovalsAsync(connection, transaction, request.CaseId, request.Actor, now, ct)
            .ConfigureAwait(false);
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            state: processingCase.State, assignedTo: processingCase.AssignedTo,
            blockedReason: processingCase.BlockedReason, now, ct).ConfigureAwait(false);
        var boundProjection = projection with
        {
            CaseId = request.CaseId,
            BoundCaseVersion = updatedCase.Version,
            PreparedAtUtc = now,
            IsCurrent = true,
            SupersededAtUtc = null,
        };
        await InsertProjectionAsync(connection, transaction, boundProjection, ct).ConfigureAwait(false);

        var result = new CorporateActionAccountingProjectionMutationResultDto(
            updatedCase, boundProjection, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, AttachAccountingProjectionOperation, request.CaseId,
            request.IdempotencyKey, requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<CorporateActionAccountingApprovalResultDto> ApproveAccountingAsync(
        ApproveCorporateActionCaseAccountingRequestDto request,
        CorporateActionCaseAccountingApprovalDto approval,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(approval);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "accounting approval",
                retryCt => ApproveAccountingOnceAsync(request, approval, transitionId, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionAccountingApprovalResultDto> ApproveAccountingOnceAsync(
        ApproveCorporateActionCaseAccountingRequestDto request,
        CorporateActionCaseAccountingApprovalDto approval,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, ApproveAccountingOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionAccountingApprovalResultDto>(
            connection, transaction, ApproveAccountingOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(
                connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
                request.ExpectedVersion, request.ScopeAssertion, ct)
            .ConfigureAwait(false);
        CorporateActionCaseAccountingPolicy.EnsureApprovable(processingCase);
        var projection = await LoadCurrentProjectionAsync(
                connection, transaction, request.CaseId, request.TenantId, request.CompanyId, ct)
            .ConfigureAwait(false);
        if (projection is null || projection.ProjectionId != request.ProjectionId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "Accounting approval targets a projection binding that is no longer the case's current exact-version projection.");
        }

        CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
        CorporateActionCaseAccountingPolicy.EnsureBalanced(projection);
        CorporateActionCaseAccountingPolicy.EnsurePolicyCoverage(projection);
        CorporateActionCaseAccountingPolicy.EnsureLotResolution(projection);
        CorporateActionCaseAccountingPolicy.EnsureIndependentOfPreparer(projection, request.Actor);

        var now = DateTimeOffset.UtcNow;
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            state: CorporateActionCaseStates.Approved, assignedTo: processingCase.AssignedTo,
            blockedReason: null, now, ct).ConfigureAwait(false);
        var boundApproval = approval with
        {
            CaseId = request.CaseId,
            ProjectionId = projection.ProjectionId,
            BoundCaseVersion = updatedCase.Version,
            ApprovedAtUtc = now,
            VoidedAtUtc = null,
            VoidedBy = null,
        };
        await InsertApprovalAsync(connection, transaction, boundApproval, ct).ConfigureAwait(false);
        var transition = new CorporateActionCaseTransitionDto(
            transitionId,
            processingCase.CaseId,
            processingCase.State,
            CorporateActionCaseStates.Approved,
            request.ExpectedVersion,
            updatedCase.Version,
            request.Actor,
            request.Reason,
            request.IdempotencyKey,
            now,
            request.CorrelationId);
        await InsertTransitionAsync(
            connection, transaction, ApproveAccountingOperation, transition, ct).ConfigureAwait(false);

        var result = new CorporateActionAccountingApprovalResultDto(
            updatedCase, boundApproval, transition, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, ApproveAccountingOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<CorporateActionAccountingPostingResultDto> RecordAccountingPostingAsync(
        PostCorporateActionCaseAccountingRequestDto request,
        CorporateActionCaseAccountingPostingDto posting,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(posting);
        return await ExecuteCaseMutationWithRetryAsync(
                request.CaseId,
                "accounting posting record",
                retryCt => RecordAccountingPostingOnceAsync(request, posting, transitionId, requestFingerprint, retryCt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionAccountingPostingResultDto> RecordAccountingPostingOnceAsync(
        PostCorporateActionCaseAccountingRequestDto request,
        CorporateActionCaseAccountingPostingDto posting,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(
            connection, transaction, PostAccountingOperation, request.CaseId, request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionAccountingPostingResultDto>(
            connection, transaction, PostAccountingOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }

        var processingCase = await LoadScopedCaseForMutationAsync(
                connection, transaction, request.CaseId, request.TenantId, request.CompanyId,
                request.ExpectedVersion, request.ScopeAssertion, ct)
            .ConfigureAwait(false);
        CorporateActionCaseAccountingPolicy.EnsurePostable(processingCase);
        CorporateActionCaseAccountingPolicy.EnsureExactAccountingScope(processingCase.Scope);
        var projection = await LoadCurrentProjectionAsync(
                connection, transaction, request.CaseId, request.TenantId, request.CompanyId, ct)
            .ConfigureAwait(false);
        if (projection is null
            || projection.ProjectionId != request.ProjectionId
            || projection.ProjectionId != posting.ProjectionId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "The durable posting record targets a projection binding that is no longer the case's current exact-version projection.");
        }

        var approval = await LoadActiveApprovalAsync(
                connection, transaction, request.CaseId, request.TenantId, request.CompanyId, ct)
            .ConfigureAwait(false);
        CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(approval, projection, request.Actor);
        if (approval!.ApprovalId != request.ApprovalId || approval.ApprovalId != posting.ApprovalId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.MakerCheckerRequired,
                "The durable posting record does not reference the case's active maker-checker approval.");
        }

        if (posting.JournalEntryId == Guid.Empty
            || posting.TotalDebits != posting.TotalCredits
            || posting.TotalDebits <= 0m
            || !string.Equals(posting.PostingStatus, "Posted", StringComparison.Ordinal)
            || posting.LedgerBookId != projection.LedgerBookId
            || posting.PeriodId != projection.PeriodId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.JournalUnbalanced,
                "A durable posting record requires an immutable journal id with balanced amounts, Posted status, and the projection's exact ledger book and period.");
        }

        var now = DateTimeOffset.UtcNow;
        var updatedCase = await UpdateCaseVersionAsync(
            connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            state: CorporateActionCaseStates.Posted, assignedTo: processingCase.AssignedTo,
            blockedReason: null, now, ct).ConfigureAwait(false);
        var boundPosting = posting with
        {
            CaseId = request.CaseId,
            PostedBy = request.Actor,
            PostedAtUtc = now,
        };
        await InsertPostingAsync(connection, transaction, boundPosting, ct).ConfigureAwait(false);
        var transition = new CorporateActionCaseTransitionDto(
            transitionId,
            processingCase.CaseId,
            processingCase.State,
            CorporateActionCaseStates.Posted,
            request.ExpectedVersion,
            updatedCase.Version,
            request.Actor,
            request.Reason,
            request.IdempotencyKey,
            now,
            request.CorrelationId);
        await InsertTransitionAsync(
            connection, transaction, PostAccountingOperation, transition, ct).ConfigureAwait(false);

        var result = new CorporateActionAccountingPostingResultDto(
            updatedCase, boundPosting, transition, Replayed: false);
        await WriteReceiptAsync(
            connection, transaction, PostAccountingOperation, request.CaseId, request.IdempotencyKey,
            requestFingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private async Task SupersedeCurrentProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid caseId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {Qualified("corporate_action_case_accounting_projections")}
            set is_current = false,
                superseded_at = @superseded_at
            where case_id = @case_id
              and is_current;
            """;
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("superseded_at", now.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task VoidActiveApprovalsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid caseId,
        string actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {Qualified("corporate_action_case_accounting_approvals")}
            set voided_at = @voided_at,
                voided_by = @voided_by
            where case_id = @case_id
              and voided_at is null;
            """;
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("voided_at", now.UtcDateTime);
        command.Parameters.AddWithValue("voided_by", actor);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionCaseAccountingProjectionDto projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_case_accounting_projections")} (
                projection_id, case_id, bound_case_version, accounting_event_id, accounting_event_version,
                spine_version, projection_input_hash, posting_intent_hash, posting_idempotency_key,
                drafted_candidate_fingerprint, policy_decision_id, policy_decision_version,
                rule_pack_id, rule_pack_version, selected_rule_id, selected_rule_version,
                ledger_book_id, period_id, expected_period_version, accounting_basis, fund_profile_id,
                currency, effective_date, total_debits, total_credits, lot_snapshot_id,
                lot_snapshot_version, has_authoritative_lot_resolution, prepared_by, prepared_at,
                is_current, superseded_at)
            values (
                @projection_id, @case_id, @bound_case_version, @accounting_event_id, @accounting_event_version,
                @spine_version, @projection_input_hash, @posting_intent_hash, @posting_idempotency_key,
                @drafted_candidate_fingerprint, @policy_decision_id, @policy_decision_version,
                @rule_pack_id, @rule_pack_version, @selected_rule_id, @selected_rule_version,
                @ledger_book_id, @period_id, @expected_period_version, @accounting_basis, @fund_profile_id,
                @currency, @effective_date, @total_debits, @total_credits, @lot_snapshot_id,
                @lot_snapshot_version, @has_authoritative_lot_resolution, @prepared_by, @prepared_at,
                @is_current, @superseded_at);
            """;
        command.Parameters.AddWithValue("projection_id", projection.ProjectionId);
        command.Parameters.AddWithValue("case_id", projection.CaseId);
        command.Parameters.AddWithValue("bound_case_version", projection.BoundCaseVersion);
        command.Parameters.AddWithValue("accounting_event_id", projection.AccountingEventId);
        command.Parameters.AddWithValue("accounting_event_version", projection.AccountingEventVersion);
        command.Parameters.AddWithValue("spine_version", projection.SpineVersion);
        command.Parameters.AddWithValue("projection_input_hash", projection.ProjectionInputHash);
        command.Parameters.AddWithValue("posting_intent_hash", projection.PostingIntentHash);
        command.Parameters.AddWithValue("posting_idempotency_key", projection.PostingIdempotencyKey);
        command.Parameters.AddWithValue("drafted_candidate_fingerprint", projection.DraftedCandidateFingerprint);
        command.Parameters.AddWithValue("policy_decision_id", projection.PolicyDecisionId);
        command.Parameters.AddWithValue("policy_decision_version", projection.PolicyDecisionVersion);
        command.Parameters.AddWithValue("rule_pack_id", projection.RulePackId);
        command.Parameters.AddWithValue("rule_pack_version", projection.RulePackVersion);
        command.Parameters.AddWithValue("selected_rule_id", projection.SelectedRuleId);
        command.Parameters.AddWithValue("selected_rule_version", projection.SelectedRuleVersion);
        command.Parameters.AddWithValue("ledger_book_id", projection.LedgerBookId);
        command.Parameters.AddWithValue("period_id", projection.PeriodId);
        command.Parameters.AddWithValue("expected_period_version", projection.ExpectedPeriodVersion);
        command.Parameters.AddWithValue("accounting_basis", projection.AccountingBasis);
        command.Parameters.AddWithValue("fund_profile_id", projection.FundProfileId);
        command.Parameters.AddWithValue("currency", projection.Currency);
        command.Parameters.Add(new NpgsqlParameter("effective_date", NpgsqlDbType.Date)
        {
            Value = projection.EffectiveDate,
        });
        command.Parameters.AddWithValue("total_debits", projection.TotalDebits);
        command.Parameters.AddWithValue("total_credits", projection.TotalCredits);
        command.Parameters.AddWithValue("lot_snapshot_id", projection.LotSnapshotId);
        command.Parameters.AddWithValue("lot_snapshot_version", projection.LotSnapshotVersion);
        command.Parameters.AddWithValue("has_authoritative_lot_resolution", projection.HasAuthoritativeLotResolution);
        command.Parameters.AddWithValue("prepared_by", projection.PreparedBy);
        command.Parameters.AddWithValue("prepared_at", projection.PreparedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("is_current", projection.IsCurrent);
        command.Parameters.AddWithValue("superseded_at", (object?)projection.SupersededAtUtc?.UtcDateTime ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertApprovalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionCaseAccountingApprovalDto approval,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_case_accounting_approvals")} (
                approval_id, case_id, projection_id, bound_case_version, approved_by, approved_at,
                reason, evidence_reference, evidence_hash, voided_at, voided_by)
            values (
                @approval_id, @case_id, @projection_id, @bound_case_version, @approved_by, @approved_at,
                @reason, @evidence_reference, @evidence_hash, null, null);
            """;
        command.Parameters.AddWithValue("approval_id", approval.ApprovalId);
        command.Parameters.AddWithValue("case_id", approval.CaseId);
        command.Parameters.AddWithValue("projection_id", approval.ProjectionId);
        command.Parameters.AddWithValue("bound_case_version", approval.BoundCaseVersion);
        command.Parameters.AddWithValue("approved_by", approval.ApprovedBy);
        command.Parameters.AddWithValue("approved_at", approval.ApprovedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("reason", approval.Reason);
        command.Parameters.AddWithValue("evidence_reference", approval.EvidenceReference);
        command.Parameters.AddWithValue("evidence_hash", approval.EvidenceHash);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertPostingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorporateActionCaseAccountingPostingDto posting,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("corporate_action_case_accounting_postings")} (
                posting_id, case_id, projection_id, approval_id, journal_entry_id, ledger_book_id,
                period_id, accounting_basis, currency, total_debits, total_credits, posting_status,
                tax_lot_mutation_batch_id, posted_by, posted_at)
            values (
                @posting_id, @case_id, @projection_id, @approval_id, @journal_entry_id, @ledger_book_id,
                @period_id, @accounting_basis, @currency, @total_debits, @total_credits, @posting_status,
                @tax_lot_mutation_batch_id, @posted_by, @posted_at);
            """;
        command.Parameters.AddWithValue("posting_id", posting.PostingId);
        command.Parameters.AddWithValue("case_id", posting.CaseId);
        command.Parameters.AddWithValue("projection_id", posting.ProjectionId);
        command.Parameters.AddWithValue("approval_id", posting.ApprovalId);
        command.Parameters.AddWithValue("journal_entry_id", posting.JournalEntryId);
        command.Parameters.AddWithValue("ledger_book_id", posting.LedgerBookId);
        command.Parameters.AddWithValue("period_id", posting.PeriodId);
        command.Parameters.AddWithValue("accounting_basis", posting.AccountingBasis);
        command.Parameters.AddWithValue("currency", posting.Currency);
        command.Parameters.AddWithValue("total_debits", posting.TotalDebits);
        command.Parameters.AddWithValue("total_credits", posting.TotalCredits);
        command.Parameters.AddWithValue("posting_status", posting.PostingStatus);
        command.Parameters.AddWithValue("tax_lot_mutation_batch_id", (object?)posting.TaxLotMutationBatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("posted_by", posting.PostedBy);
        command.Parameters.AddWithValue("posted_at", posting.PostedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<CorporateActionCaseAccountingProjectionDto?> LoadCurrentProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select ap.projection_id, ap.case_id, ap.bound_case_version, ap.accounting_event_id,
                   ap.accounting_event_version, ap.spine_version, ap.projection_input_hash,
                   ap.posting_intent_hash, ap.posting_idempotency_key, ap.drafted_candidate_fingerprint,
                   ap.policy_decision_id, ap.policy_decision_version, ap.rule_pack_id, ap.rule_pack_version,
                   ap.selected_rule_id, ap.selected_rule_version, ap.ledger_book_id, ap.period_id,
                   ap.expected_period_version, ap.accounting_basis, ap.fund_profile_id, ap.currency,
                   ap.effective_date, ap.total_debits, ap.total_credits, ap.lot_snapshot_id,
                   ap.lot_snapshot_version, ap.has_authoritative_lot_resolution, ap.prepared_by,
                   ap.prepared_at, ap.is_current, ap.superseded_at
            from {Qualified("corporate_action_case_accounting_projections")} ap
            join {Qualified("corporate_action_processing_cases")} pc on pc.case_id = ap.case_id
            where ap.case_id = @case_id
              and ap.is_current
              and pc.tenant_id = @tenant_id
              and pc.company_id = @company_id;
            """;
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("company_id", companyId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadProjection(reader) : null;
    }

    private async Task<CorporateActionCaseAccountingApprovalDto?> LoadActiveApprovalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select aa.approval_id, aa.case_id, aa.projection_id, aa.bound_case_version, aa.approved_by,
                   aa.approved_at, aa.reason, aa.evidence_reference, aa.evidence_hash, aa.voided_at,
                   aa.voided_by
            from {Qualified("corporate_action_case_accounting_approvals")} aa
            join {Qualified("corporate_action_processing_cases")} pc on pc.case_id = aa.case_id
            where aa.case_id = @case_id
              and aa.voided_at is null
              and pc.tenant_id = @tenant_id
              and pc.company_id = @company_id;
            """;
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("company_id", companyId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadApproval(reader) : null;
    }

    private static CorporateActionCaseAccountingProjectionDto ReadProjection(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt64(2),
            reader.GetGuid(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetGuid(10),
            reader.GetInt64(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetGuid(16),
            reader.GetGuid(17),
            reader.GetInt64(18),
            reader.GetString(19),
            reader.GetString(20),
            reader.GetString(21),
            reader.GetFieldValue<DateOnly>(22),
            reader.GetDecimal(23),
            reader.GetDecimal(24),
            reader.GetGuid(25),
            reader.GetInt64(26),
            reader.GetBoolean(27),
            reader.GetString(28),
            ReadTimestamp(reader, 29),
            reader.GetBoolean(30),
            reader.IsDBNull(31) ? null : ReadTimestamp(reader, 31));

    private static CorporateActionCaseAccountingApprovalDto ReadApproval(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetInt64(3),
            reader.GetString(4),
            ReadTimestamp(reader, 5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : ReadTimestamp(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
}
