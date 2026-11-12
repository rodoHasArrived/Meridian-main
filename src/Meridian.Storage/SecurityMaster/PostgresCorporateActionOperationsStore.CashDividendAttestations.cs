using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresCorporateActionOperationsStore
{
    public async Task<CorporateActionCashDividendAttestationDto?> GetCashDividendAttestationAsync(
        Guid caseId, string tenantId, string companyId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
    {
        RequireCashDividendScope(tenantId, companyId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadCashDividendAttestationAsync(connection, null, caseId, tenantId, companyId, true, ct).ConfigureAwait(false);
    }, "cash-dividend attestation read").ConfigureAwait(false);

    public Task<CorporateActionCashDividendAttestationResultDto?> GetCashDividendAttestationReceiptAsync(
        Guid caseId, string idempotencyKey, string requestFingerprint, CancellationToken ct = default) =>
        ExecutePersistenceReadAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await ReadReceiptAsync<CorporateActionCashDividendAttestationResultDto>(connection, null,
                AttestCashDividendOperation, caseId, idempotencyKey, requestFingerprint, ct).ConfigureAwait(false);
        }, "cash-dividend attestation receipt");

    public Task<CorporateActionCashDividendAttestationResultDto> AttestCashDividendAsync(
        AttestCorporateActionCashDividendRequestDto request,
        CorporateActionCashDividendAttestationDto attestation,
        Guid transitionId,
        string requestFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(attestation);
        RequireCashDividendScope(request.TenantId, request.CompanyId);
        if (request.Authority is not { Origin: OperationsActionOriginDto.HumanOperator })
            throw new CorporateActionPermissionDeniedException("CashDividendAttestation", "HumanOperator");
        return ExecuteCaseMutationWithRetryAsync(request.CaseId, "cash-dividend preparer attestation",
            retryCt => AttestCashDividendOnceAsync(request, attestation, transitionId, requestFingerprint, retryCt), ct);
    }

    private async Task<CorporateActionCashDividendAttestationResultDto> AttestCashDividendOnceAsync(
        AttestCorporateActionCashDividendRequestDto request,
        CorporateActionCashDividendAttestationDto attestation,
        Guid transitionId,
        string fingerprint,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        // Scope serialization owns the declaration and capacity predicates. ReadCommitted gives
        // the next queued command a fresh snapshot after acquiring that exclusive scope lock.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await AcquireCashDividendScopeLockAsync(connection, transaction, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionCashDividendAttestationResultDto>(connection, transaction,
            AttestCashDividendOperation, request.CaseId, request.IdempotencyKey, fingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }
        RequireCashDividendHostEnabled();
        if (request.Authority?.CanAttestAccounting != true)
            throw new CorporateActionPermissionDeniedException("CashDividendAttestation", "CanAttestAccounting");
        await AcquireCashDividendSourceChainAsync(connection, transaction, request.CaseId, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(connection, transaction, AttestCashDividendOperation, request.CaseId, request.IdempotencyKey, ct).ConfigureAwait(false);
        var processingCase = await LoadScopedCaseForMutationAsync(connection, transaction, request.CaseId,
            request.TenantId, request.CompanyId, request.ExpectedVersion, request.ScopeAssertion, ct).ConfigureAwait(false);
        if (processingCase.State is not (CorporateActionCaseStates.ReadyForApproval or CorporateActionCashDividendAttestationPolicy.CaseState))
            throw new CorporateActionStateConflictException(request.CaseId, "A preparer attestation requires a current approval candidate or explicit reaffirmation of its unchanged attested projection.");
        await EnsureCurrentCashDividendSourceAsync(connection, transaction, processingCase, ct).ConfigureAwait(false);
        var projection = await LoadCurrentProjectionAsync(connection, transaction, request.CaseId,
            request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        if (projection is null || projection.ProjectionId != request.ProjectionId || projection.ProjectionId != attestation.ProjectionId)
            throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale, "The cash-dividend projection changed before attestation.");
        var declaration = await RequireCurrentCashDividendDeclarationAsync(connection, transaction,
            request.TenantId, request.CompanyId, projection.LedgerBookId, request.Actor,
            request.ExpectedDeclarationRevision, ct).ConfigureAwait(false);
        ValidateCashDividendAttestationOperands(request, attestation, processingCase, projection, declaration);
        await RetainCashDividendAliasAsync(connection, transaction, request.TenantId, request.CompanyId, attestation, ct).ConfigureAwait(false);
        var reservation = await ReserveCashDividendBudgetAsync(connection, transaction,
            processingCase, projection, attestation, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        await VoidActiveCashDividendAttestationsAsync(connection, transaction, request.CaseId, request.Actor, now, ct).ConfigureAwait(false);
        var updated = await UpdateCaseVersionAsync(connection, transaction, processingCase, request.ExpectedVersion, request.Actor,
            CorporateActionCashDividendAttestationPolicy.CaseState, processingCase.AssignedTo, null, now, ct).ConfigureAwait(false);
        var retained = attestation with
        {
            CaseId = processingCase.CaseId,
            BoundCaseVersion = updated.Version,
            BudgetReservationId = reservation,
            AttestedBy = request.Actor.Trim(),
            AttestedAtUtc = now,
            VoidedAtUtc = null,
            VoidedBy = null
        };
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                insert into {Qualified("cash_dividend_attestations")}
                    (attestation_id,case_id,projection_id,reservation_id,tenant_id,company_id,ledger_book_id,
                     declaration_id,declaration_revision,bound_case_version,attested_by,attested_at,payload,payload_hash,
                     projection_payload,projection_hash)
                values (@id,@case,@projection,@reservation,@tenant,@company,@book,@declaration,@revision,@version,@actor,@recorded,@payload,@hash,
                    @projection_payload,@projection_hash)
                """;
            command.Parameters.AddWithValue("id", retained.AttestationId);
            command.Parameters.AddWithValue("case", processingCase.CaseId);
            command.Parameters.AddWithValue("projection", projection.ProjectionId);
            command.Parameters.AddWithValue("reservation", reservation);
            command.Parameters.AddWithValue("tenant", request.TenantId);
            command.Parameters.AddWithValue("company", request.CompanyId);
            command.Parameters.AddWithValue("book", projection.LedgerBookId);
            command.Parameters.AddWithValue("declaration", declaration.DeclarationId);
            command.Parameters.AddWithValue("revision", declaration.Revision);
            command.Parameters.AddWithValue("version", updated.Version);
            command.Parameters.AddWithValue("actor", retained.AttestedBy);
            command.Parameters.AddWithValue("recorded", now.UtcDateTime);
            AddCashDividendPayload(command, retained);
            var projectionJson = JsonSerializer.Serialize(projection, JsonOptions);
            command.Parameters.Add(new NpgsqlParameter("projection_payload", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = projectionJson });
            command.Parameters.AddWithValue("projection_hash", Sha256Digest.ComputeUtf8(projectionJson));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var transition = new CorporateActionCaseTransitionDto(transitionId, request.CaseId, processingCase.State,
            CorporateActionCashDividendAttestationPolicy.CaseState, request.ExpectedVersion, updated.Version,
            request.Actor, request.Reason, request.IdempotencyKey, now, request.CorrelationId);
        await InsertTransitionAsync(connection, transaction, AttestCashDividendOperation, transition, ct).ConfigureAwait(false);
        var result = new CorporateActionCashDividendAttestationResultDto(updated, retained, transition, false);
        await WriteReceiptAsync(connection, transaction, AttestCashDividendOperation, request.CaseId,
            request.IdempotencyKey, fingerprint, result, now, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    private static void ValidateCashDividendAttestationOperands(AttestCorporateActionCashDividendRequestDto request,
        CorporateActionCashDividendAttestationDto attestation, CorporateActionProcessingCaseDto processingCase,
        CorporateActionCaseAccountingProjectionDto projection, CashDividendAttestationDeclarationDto declaration)
    {
        var action = processingCase.SourceSnapshot?.ProposedAction;
        var eligibility = new CorporateActionCashDividendEligibilityDto(processingCase.SecurityId, attestation.ExDate,
            attestation.RelatedEventKey, attestation.ProviderId, attestation.SourceEventId, attestation.SourceEventVersion,
            attestation.SourceEvidenceHash, action?.DividendPerShare ?? 0m, attestation.GrossImpactUsd,
            attestation.WholeEventGrossUsd, attestation.BudgetMonth, attestation.EntitlementSnapshotHash,
            attestation.EntitlementSnapshotJson, attestation.AccountRoleBinding);
        CorporateActionCashDividendAttestationValidation.EnsureEligibility(processingCase, projection, eligibility);
        CorporateActionCashDividendAttestationValidation.EnsureSameAccountRoleBinding(declaration.AccountRoleBinding, attestation.AccountRoleBinding);
        if (!PreparerAttestationApprovalBoundary.IsReservedReceiptId(attestation.AttestationId)
            || attestation.CaseId != processingCase.CaseId || attestation.BudgetReservationId == Guid.Empty
            || attestation.DeclarationId != declaration.DeclarationId || attestation.DeclarationRevision != declaration.Revision
            || attestation.PolicyVersion != declaration.PolicyVersion || attestation.PolicyHash != declaration.PolicyHash
            || !SameCashDividendActor(attestation.AttestedBy, request.Actor) || !SameCashDividendActor(projection.PreparedBy, request.Actor)
            || request.Confirmation != CorporateActionCashDividendAttestationPolicy.Confirmation
            || attestation.Confirmation != request.Confirmation || attestation.Disclosure != CorporateActionCashDividendAttestationPolicy.Disclosure
            || request.ExpectedEligibilityHash != attestation.EntitlementSnapshotHash
            || request.EvidenceReference != "cash-dividend-eligibility:" + attestation.EntitlementSnapshotHash
            || request.EvidenceHash != attestation.EntitlementSnapshotHash
            || attestation.EvidenceReference != request.EvidenceReference || attestation.EvidenceHash != request.EvidenceHash
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 4000)
            throw new CorporateActionValidationException("The preparer attestation does not bind this exact eligible dividend, declaration, human and reviewed evidence.");
    }

    private async Task<CashDividendAttestationDeclarationDto> RequireCurrentCashDividendDeclarationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string tenantId, string companyId,
        Guid bookId, string actor, long revision, CancellationToken ct)
    {
        var declaration = await LoadCashDividendDeclarationAsync(connection, transaction, tenantId, companyId, true, ct).ConfigureAwait(false);
        if (declaration is not { Enabled: true } || declaration.LedgerBookId != bookId
            || declaration.Revision != revision || !SameCashDividendActor(declaration.PrincipalId, actor))
            throw new CorporateActionOperationException("cash_dividend_declaration_required", "The selected USD book requires a current declaration bound to this authenticated principal and exact revision.");
        return declaration;
    }

    private async Task<Guid> ReserveCashDividendBudgetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase, CorporateActionCaseAccountingProjectionDto projection,
        CorporateActionCashDividendAttestationDto attestation, CancellationToken ct)
    {
        var economicFingerprint = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            processingCase.CaseId,
            projection.AccountingEventId,
            projection.AccountingEventVersion,
            projection.ProjectionInputHash,
            projection.PostingIntentHash,
            projection.PostingIdempotencyKey,
            projection.DraftedCandidateFingerprint,
            projection.LedgerBookId,
            projection.PeriodId,
            projection.Currency,
            projection.EffectiveDate,
            attestation.RelatedEventKey,
            attestation.GrossImpactUsd,
            attestation.WholeEventGrossUsd,
            attestation.BudgetMonth,
            attestation.SourceEvidenceHash,
            attestation.EntitlementSnapshotHash
        }, JsonOptions));
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = $"select reservation_id,economic_fingerprint from {Qualified("cash_dividend_attestation_reservations")} where tenant_id=@tenant and company_id=@company and ledger_book_id=@book and posting_idempotency_key=@key";
            AddCashDividendBudgetScope(existing, processingCase.Scope.TenantId, processingCase.Scope.CompanyId, projection.LedgerBookId);
            existing.Parameters.AddWithValue("key", projection.PostingIdempotencyKey);
            await using var reader = await existing.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!Sha256Digest.FixedEquals(reader.GetString(1), economicFingerprint))
                    throw new CorporateActionIdempotencyConflictException(processingCase.CaseId, projection.PostingIdempotencyKey);
                return reader.GetGuid(0);
            }
        }
        decimal eventGross;
        decimal monthGross;
        await using (var sums = connection.CreateCommand())
        {
            sums.Transaction = transaction;
            sums.CommandText = $"select coalesce(sum(gross_usd) filter (where related_event_key=@event),0),coalesce(sum(gross_usd) filter (where budget_month=@month),0) from {Qualified("cash_dividend_attestation_reservations")} where tenant_id=@tenant and company_id=@company and ledger_book_id=@book";
            AddCashDividendBudgetScope(sums, processingCase.Scope.TenantId, processingCase.Scope.CompanyId, projection.LedgerBookId);
            sums.Parameters.AddWithValue("event", attestation.RelatedEventKey);
            sums.Parameters.AddWithValue("month", attestation.BudgetMonth);
            await using var reader = await sums.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            eventGross = reader.GetDecimal(0);
            monthGross = reader.GetDecimal(1);
        }
        if (eventGross + attestation.GrossImpactUsd > CorporateActionCashDividendAttestationPolicy.RelatedEventGrossLimitUsd
            || eventGross + attestation.GrossImpactUsd > attestation.WholeEventGrossUsd
            || monthGross + attestation.GrossImpactUsd > CorporateActionCashDividendAttestationPolicy.BookCalendarMonthGrossLimitUsd)
            throw new CorporateActionOperationException("cash_dividend_attestation_limit", "The complete related-event or book/calendar-month gross limit would be exceeded. Existing reservations, including uncertain outcomes, remain counted.");
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            insert into {Qualified("cash_dividend_attestation_reservations")}
                (reservation_id,tenant_id,company_id,ledger_book_id,case_id,posting_idempotency_key,
                 related_event_key,budget_month,gross_usd,whole_event_gross_usd,economic_fingerprint,reserved_at)
            values (@id,@tenant,@company,@book,@case,@key,@event,@month,@gross,@whole,@fingerprint,@at)
            """;
        AddCashDividendBudgetScope(insert, processingCase.Scope.TenantId, processingCase.Scope.CompanyId, projection.LedgerBookId);
        insert.Parameters.AddWithValue("id", attestation.BudgetReservationId);
        insert.Parameters.AddWithValue("case", processingCase.CaseId);
        insert.Parameters.AddWithValue("key", projection.PostingIdempotencyKey);
        insert.Parameters.AddWithValue("event", attestation.RelatedEventKey);
        insert.Parameters.AddWithValue("month", attestation.BudgetMonth);
        insert.Parameters.AddWithValue("gross", attestation.GrossImpactUsd);
        insert.Parameters.AddWithValue("whole", attestation.WholeEventGrossUsd);
        insert.Parameters.AddWithValue("fingerprint", economicFingerprint);
        insert.Parameters.AddWithValue("at", DateTime.UtcNow);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return attestation.BudgetReservationId;
    }

    private async Task RetainCashDividendAliasAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string tenantId, string companyId, CorporateActionCashDividendAttestationDto attestation, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("cash_dividend_attestation_event_aliases")}
                (tenant_id,company_id,provider_id,source_event_id,source_event_version,source_evidence_hash,related_event_key,recorded_at)
            values (@tenant,@company,@provider,@source,@source_version,@source_hash,@event,@at) on conflict do nothing;
            select related_event_key=@event and source_event_version=@source_version and source_evidence_hash=@source_hash
            from {Qualified("cash_dividend_attestation_event_aliases")}
            where tenant_id=@tenant and company_id=@company and provider_id=@provider and source_event_id=@source;
            """;
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        command.Parameters.AddWithValue("provider", attestation.ProviderId);
        command.Parameters.AddWithValue("source", attestation.SourceEventId);
        command.Parameters.AddWithValue("source_version", attestation.SourceEventVersion);
        command.Parameters.AddWithValue("source_hash", attestation.SourceEvidenceHash);
        command.Parameters.AddWithValue("event", attestation.RelatedEventKey);
        command.Parameters.AddWithValue("at", DateTime.UtcNow);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            throw new CorporateActionOperationException("cash_dividend_event_alias_conflict", "An observed provider event cannot shift its security, ex-date, source revision or evidence to another attestation budget bucket. Amendments require ordinary review.");
    }

    private async Task<CorporateActionCashDividendAttestationDto?> LoadCashDividendAttestationAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid caseId, string tenantId, string companyId, bool activeOnly, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select payload::text,payload_hash,voided_at,voided_by from {Qualified("cash_dividend_attestations")} where case_id=@case and tenant_id=@tenant and company_id=@company" + (activeOnly ? " and voided_at is null" : "") + " order by attested_at desc limit 1";
        command.Parameters.AddWithValue("case", caseId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var receipt = ReadCashDividendPayload<CorporateActionCashDividendAttestationDto>(reader.GetString(0), reader.GetString(1));
        if (receipt.CaseId != caseId)
            throw CashDividendUnavailable("The economic attestation failed its case binding.");
        return receipt with { VoidedAtUtc = reader.IsDBNull(2) ? null : ReadTimestamp(reader, 2), VoidedBy = reader.IsDBNull(3) ? null : reader.GetString(3) };
    }

    private async Task VoidActiveCashDividendAttestationsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid caseId, string actor, DateTimeOffset at, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"update {Qualified("cash_dividend_attestations")} set voided_at=@at,voided_by=@actor where case_id=@case and voided_at is null";
        command.Parameters.AddWithValue("case", caseId);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("at", at.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool SameCashDividendActor(string? left, string? right) => !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void AddCashDividendBudgetScope(NpgsqlCommand command, string tenantId, string companyId, Guid bookId)
    {
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        command.Parameters.AddWithValue("book", bookId);
    }

    private void RequireCashDividendHostEnabled()
    {
        if (!_options.EnableSinglePrincipalCashDividendAttestations)
            throw new CorporateActionOperationException("cash_dividend_policy_disabled", "Economic preparer attestation is disabled on this host.");
    }
}
