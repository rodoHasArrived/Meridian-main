using System.Data;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresCorporateActionOperationsStore
{
    public Task<CorporateActionCashDividendPostingResultDto> ExecuteAttestedCashDividendPostingAsync(
        PostAttestedCorporateActionCashDividendRequestDto request,
        Func<CorporateActionCashDividendPostingLease, CancellationToken, Task<CorporateActionCashDividendPostingDto>> post,
        string requestFingerprint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(post);
        RequireCashDividendScope(request.TenantId, request.CompanyId);
        if (request.Authority is not { Origin: OperationsActionOriginDto.HumanOperator })
            throw new CorporateActionPermissionDeniedException("PostAttestedCashDividend", "HumanOperator");
        return ExecuteCaseMutationWithRetryAsync(request.CaseId, "attested cash-dividend posting",
            retryCt => ExecuteAttestedCashDividendPostingOnceAsync(request, post, requestFingerprint, retryCt), ct);
    }

    private async Task<CorporateActionCashDividendPostingResultDto> ExecuteAttestedCashDividendPostingOnceAsync(
        PostAttestedCorporateActionCashDividendRequestDto request,
        Func<CorporateActionCashDividendPostingLease, CancellationToken, Task<CorporateActionCashDividendPostingDto>> post,
        string fingerprint, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        // Every declaration mutation takes this same scope lock. It lasts through journal, spine
        // and case receipt commit, so withdrawal and economic posting have one durable order.
        await AcquireCashDividendScopeLockAsync(connection, transaction, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        var replay = await ReadReceiptAsync<CorporateActionCashDividendPostingResultDto>(connection, transaction,
            PostAttestedCashDividendOperation, request.CaseId, request.IdempotencyKey, fingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return replay with { Replayed = true };
        }
        RequireCashDividendHostEnabled();
        if (request.Authority?.CanPostAccounting != true)
            throw new CorporateActionPermissionDeniedException("PostAttestedCashDividend", "CanPostAccounting");
        if (_cashDividendLedgerStore is not Meridian.Storage.Ledger.PostgresLedgerJournalStore nativeLedger)
            throw CashDividendUnavailable("Native cash-dividend posting requires the configured native ledger transaction authority.");
        var selectedBookId = await ReadCashDividendSelectedBookAsync(connection, transaction, request.CaseId, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        // Ordinary lot writers take shared entitlement/book authority before security rows.
        // Freeze the book first, then security, canonical chain and case, in that same order.
        await nativeLedger.LockCashDividendBookAsync(connection, transaction, selectedBookId, ct).ConfigureAwait(false);
        await AcquireCashDividendSourceChainAsync(connection, transaction, request.CaseId, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        await AcquireCaseCommandLocksAsync(connection, transaction, PostAttestedCashDividendOperation,
            request.CaseId, request.IdempotencyKey, ct).ConfigureAwait(false);
        var processingCase = await LoadScopedCaseForMutationAsync(connection, transaction, request.CaseId,
            request.TenantId, request.CompanyId, request.ExpectedVersion, request.ScopeAssertion, ct).ConfigureAwait(false);
        if (processingCase.State != CorporateActionCashDividendAttestationPolicy.CaseState)
            throw new CorporateActionStateConflictException(request.CaseId, "The case is not in its distinct preparer-attested state.");
        await EnsureCurrentCashDividendSourceAsync(connection, transaction, processingCase, ct).ConfigureAwait(false);
        var projection = await LoadCurrentProjectionAsync(connection, transaction, request.CaseId,
            request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        var attestation = await LoadCashDividendAttestationAsync(connection, transaction, request.CaseId,
            request.TenantId, request.CompanyId, true, ct).ConfigureAwait(false);
        if (projection is null || attestation is null || !attestation.IsActive
            || projection.LedgerBookId != selectedBookId
            || projection.ProjectionId != request.ProjectionId || attestation.ProjectionId != request.ProjectionId
            || attestation.AttestationId != request.AttestationId || !SameCashDividendActor(attestation.AttestedBy, request.Actor)
            || !SameCashDividendActor(projection.PreparedBy, request.Actor))
            throw new CorporateActionValidationException("Posting requires the exact active preparer attestation and unchanged projection.");
        var declaration = await RequireCurrentCashDividendDeclarationAsync(connection, transaction, request.TenantId,
            request.CompanyId, projection.LedgerBookId, request.Actor, attestation.DeclarationRevision, ct).ConfigureAwait(false);
        if (attestation.DeclarationId != declaration.DeclarationId || attestation.PolicyHash != declaration.PolicyHash)
            throw new CorporateActionValidationException("The preparer attestation targets a different economic declaration or policy.");
        CorporateActionCashDividendAttestationValidation.EnsureSameAccountRoleBinding(declaration.AccountRoleBinding, attestation.AccountRoleBinding);
        await EnsureCashDividendReservationAsync(connection, transaction, processingCase, projection, attestation, ct).ConfigureAwait(false);
        var lease = new CorporateActionCashDividendPostingLease(attestation, projection, declaration, connection, transaction,
            async verifyCt =>
            {
                RequireCashDividendHostEnabled();
                var current = await RequireCurrentCashDividendDeclarationAsync(connection, transaction, request.TenantId,
                    request.CompanyId, projection.LedgerBookId, request.Actor, attestation.DeclarationRevision, verifyCt).ConfigureAwait(false);
                var retained = await LoadCashDividendAttestationAsync(connection, transaction, request.CaseId,
                    request.TenantId, request.CompanyId, true, verifyCt).ConfigureAwait(false);
                if (current.DeclarationId != declaration.DeclarationId || retained?.AttestationId != attestation.AttestationId
                    || retained.ProjectionId != projection.ProjectionId)
                    throw new CorporateActionValidationException("Native preparer authority changed during the posting invocation.");
            }, (symbol, securityId, version, asOf, verifyCt) => EnsureCashDividendCanonicalInstrumentAsync(connection,
                transaction, processingCase.SecurityId, symbol, securityId, version, asOf, verifyCt));
        try
        {
            var posted = await post(lease, ct).ConfigureAwait(false);
            ValidateCashDividendPostingResult(posted, request, projection, attestation);
            lease.EnsureCompleted(posted.JournalEntryId);
            var now = DateTimeOffset.UtcNow;
            var updated = await UpdateCaseVersionAsync(connection, transaction, processingCase, request.ExpectedVersion,
                request.Actor, CorporateActionCaseStates.Posted, processingCase.AssignedTo, null, now, ct).ConfigureAwait(false);
            // PostedAt is the native journal's timestamp, not an inferred acknowledgment time.
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
                    insert into {Qualified("cash_dividend_attestation_postings")}
                        (posting_id,attestation_id,case_id,projection_id,tenant_id,company_id,ledger_book_id,
                         period_id,journal_entry_id,posted_at,payload,payload_hash)
                    values (@id,@attestation,@case,@projection,@tenant,@company,@book,@period,@journal,@at,@payload,@hash)
                    """;
                command.Parameters.AddWithValue("id", posted.PostingId);
                command.Parameters.AddWithValue("attestation", attestation.AttestationId);
                command.Parameters.AddWithValue("case", request.CaseId);
                command.Parameters.AddWithValue("projection", projection.ProjectionId);
                AddCashDividendBudgetScope(command, request.TenantId, request.CompanyId, projection.LedgerBookId);
                command.Parameters.AddWithValue("period", projection.PeriodId);
                command.Parameters.AddWithValue("journal", posted.JournalEntryId);
                command.Parameters.AddWithValue("at", posted.PostedAtUtc.UtcDateTime);
                AddCashDividendPayload(command, posted);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            var transition = new CorporateActionCaseTransitionDto(Guid.NewGuid(), request.CaseId, processingCase.State,
                CorporateActionCaseStates.Posted, request.ExpectedVersion, updated.Version, request.Actor, request.Reason,
                request.IdempotencyKey, now, request.CorrelationId);
            await InsertTransitionAsync(connection, transaction, PostAttestedCashDividendOperation, transition, ct).ConfigureAwait(false);
            var result = new CorporateActionCashDividendPostingResultDto(updated, posted, transition, false);
            await WriteReceiptAsync(connection, transaction, PostAttestedCashDividendOperation, request.CaseId,
                request.IdempotencyKey, fingerprint, result, now, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            lease.Deactivate();
        }
    }

    public async Task<CorporateActionCashDividendPostingDto?> GetCashDividendPostingAsync(
        Guid caseId, string tenantId, string companyId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
    {
        RequireCashDividendScope(tenantId, companyId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select payload::text,payload_hash from {Qualified("cash_dividend_attestation_postings")} where case_id=@case and tenant_id=@tenant and company_id=@company order by posted_at desc limit 1";
        command.Parameters.AddWithValue("case", caseId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadCashDividendPayload<CorporateActionCashDividendPostingDto>(reader.GetString(0), reader.GetString(1)) : null;
    }, "cash-dividend posting read").ConfigureAwait(false);

    public async Task<CorporateActionCashDividendPostedProofDto?> GetCashDividendPostedProofAsync(
        Guid journalEntryId, string tenantId, string companyId, CancellationToken ct = default)
        => await ExecutePersistenceReadAsync(async () =>
    {
        RequireCashDividendScope(tenantId, companyId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select p.payload::text,p.payload_hash,a.payload::text,a.payload_hash,
                   d.payload::text,d.payload_hash,a.projection_payload::text,a.projection_hash
            from {Qualified("cash_dividend_attestation_postings")} p
            join {Qualified("cash_dividend_attestations")} a on a.attestation_id=p.attestation_id
                and a.tenant_id=p.tenant_id and a.company_id=p.company_id and a.projection_id=p.projection_id
            join {Qualified("cash_dividend_attestation_declaration_history")} d
                on d.tenant_id=a.tenant_id and d.company_id=a.company_id
                and d.declaration_id=a.declaration_id and d.revision=a.declaration_revision
            where p.journal_entry_id=@journal and p.tenant_id=@tenant and p.company_id=@company
            """;
        command.Parameters.AddWithValue("journal", journalEntryId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        var posting = ReadCashDividendPayload<CorporateActionCashDividendPostingDto>(reader.GetString(0), reader.GetString(1));
        var attestation = ReadCashDividendPayload<CorporateActionCashDividendAttestationDto>(reader.GetString(2), reader.GetString(3));
        var declaration = ReadCashDividendPayload<CashDividendAttestationDeclarationDto>(reader.GetString(4), reader.GetString(5));
        var projection = ReadCashDividendPayload<CorporateActionCaseAccountingProjectionDto>(reader.GetString(6), reader.GetString(7));
        if (posting.JournalEntryId != journalEntryId || posting.AttestationId != attestation.AttestationId
            || posting.ProjectionId != projection.ProjectionId || declaration.DeclarationId != attestation.DeclarationId
            || declaration.Revision != attestation.DeclarationRevision || declaration.TenantId != tenantId
            || declaration.CompanyId != companyId || posting.LedgerBookId != declaration.LedgerBookId
            || posting.LedgerBookId != projection.LedgerBookId || posting.PeriodId != projection.PeriodId)
            throw CashDividendUnavailable("The original posted preparer proof failed its retained scope binding.");
        return new CorporateActionCashDividendPostedProofDto(posting, attestation, declaration, projection);
    }, "posted cash-dividend proof read").ConfigureAwait(false);

    private async Task EnsureCashDividendReservationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CorporateActionProcessingCaseDto processingCase, CorporateActionCaseAccountingProjectionDto projection,
        CorporateActionCashDividendAttestationDto attestation, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select count(*) from {Qualified("cash_dividend_attestation_reservations")} where reservation_id=@id and tenant_id=@tenant and company_id=@company and ledger_book_id=@book and case_id=@case and posting_idempotency_key=@key and related_event_key=@event and budget_month=@month and gross_usd=@gross and whole_event_gross_usd=@whole";
        AddCashDividendBudgetScope(command, processingCase.Scope.TenantId, processingCase.Scope.CompanyId, projection.LedgerBookId);
        command.Parameters.AddWithValue("id", attestation.BudgetReservationId);
        command.Parameters.AddWithValue("case", processingCase.CaseId);
        command.Parameters.AddWithValue("key", projection.PostingIdempotencyKey);
        command.Parameters.AddWithValue("event", attestation.RelatedEventKey);
        command.Parameters.AddWithValue("month", attestation.BudgetMonth);
        command.Parameters.AddWithValue("gross", attestation.GrossImpactUsd);
        command.Parameters.AddWithValue("whole", attestation.WholeEventGrossUsd);
        if ((long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))! != 1)
            throw CashDividendUnavailable("The exact economic posting has no intact durable cap reservation.");
    }

    private static void ValidateCashDividendPostingResult(CorporateActionCashDividendPostingDto posting,
        PostAttestedCorporateActionCashDividendRequestDto request, CorporateActionCaseAccountingProjectionDto projection,
        CorporateActionCashDividendAttestationDto attestation)
    {
        if (posting.PostingId == Guid.Empty || posting.JournalEntryId == Guid.Empty
            || posting.CaseId != request.CaseId || posting.AttestationId != attestation.AttestationId
            || posting.ProjectionId != projection.ProjectionId || posting.LedgerBookId != projection.LedgerBookId
            || posting.PeriodId != projection.PeriodId || posting.AccountingBasis != projection.AccountingBasis
            || posting.Currency != "USD" || posting.TotalDebits != attestation.GrossImpactUsd
            || posting.TotalCredits != posting.TotalDebits || posting.PostingStatus != "Posted"
            || posting.TaxLotMutationBatchId is not null || !SameCashDividendActor(posting.PostedBy, request.Actor)
            || posting.PostedAtUtc < attestation.AttestedAtUtc)
            throw new CorporateActionValidationException("The native journal result does not match the exact attested USD dividend.");
    }
}
