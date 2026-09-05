using System.Data;
using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private const string BackfillLotColumns = """
        tax_lot_record_id, ledger_book_id, account_name, account_type, symbol, financial_account_id,
        lot_id, acquired_date, original_quantity, open_quantity, unit_cost, currency, source_journal_entry_id,
        evidence_ref, version, originating_mutation_batch_id, last_mutation_batch_id, created_at, updated_at,
        security_id, book_position_id, original_face, booked_factor, par_basis, acquisition_terms
        """;

    public async Task<IReadOnlyList<OpenLotBackfillExceptionDto>> SurveyAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await RequireBackfillBookAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        var lots = new List<LedgerTaxLotRecord>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = $"select {BackfillLotColumns} from {Qualified("tax_lots")} where ledger_book_id = @book and open_quantity > 0 order by tax_lot_record_id for update";
            query.Parameters.AddWithValue("book", ledgerBookId);
            await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) lots.Add(ReadTaxLot(reader));
        }
        foreach (var lot in lots)
        {
            var issues = OpenLotBackfillRules.Issues(lot);
            if (issues.Count == 0) continue;
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                insert into {Qualified("open_lot_backfill_exceptions")} as retained
                    (tax_lot_record_id, ledger_book_id, lot_id, lot_version, issues, first_observed_at, last_observed_at)
                values (@lot, @book, @lot_id, @lot_version, @issues, @now, @now)
                on conflict (tax_lot_record_id) do update
                set lot_version = excluded.lot_version, issues = excluded.issues,
                    last_observed_at = excluded.last_observed_at, version = retained.version + 1
                where retained.resolution_receipt_id is null
                  and (retained.lot_version <> excluded.lot_version or retained.issues <> excluded.issues)
                """;
            insert.Parameters.AddWithValue("lot", lot.TaxLotRecordId);
            insert.Parameters.AddWithValue("book", ledgerBookId);
            insert.Parameters.AddWithValue("lot_id", lot.LotId);
            insert.Parameters.AddWithValue("lot_version", lot.Version);
            insert.Parameters.AddWithValue("issues", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(issues));
            insert.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await ListExceptionsAsync(ledgerBookId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpenLotBackfillExceptionDto>> ListExceptionsAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await RequireBackfillBookAsync(connection, null, ledgerBookId, ct).ConfigureAwait(false);
        return await ReadBackfillExceptionsAsync(connection, null, ledgerBookId, ct).ConfigureAwait(false);
    }

    public async Task<OpenLotBackfillEvidenceDto> RetainEvidenceAsync(RetainOpenLotBackfillEvidenceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = BackfillText(request.Actor, "Retaining actor");
        var source = BackfillText(request.SourceSystem, "Source system");
        var reference = BackfillText(request.SourceReference, "Source reference");
        if (request.EvidenceRecordId == Guid.Empty || !Uri.TryCreate(request.SourceUri, UriKind.Absolute, out _))
            throw new LedgerValidationException("Evidence record identity and an absolute source URI are required.");
        var content = request.Content?.ToArray() ?? [];
        var facts = OpenLotBackfillRules.ReadFacts(content, request.ContentHashSha256);
        if (facts.LedgerBookId != request.LedgerBookId || facts.TaxLotRecordId != request.TaxLotRecordId)
            throw new LedgerValidationException("Retained source content must bind the requested exact ledger book and lot.");
        var fingerprint = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            request.EvidenceRecordId, request.LedgerBookId, request.TaxLotRecordId,
            SourceSystem = source, SourceReference = reference, request.SourceUri, request.ContentHashSha256, Actor = actor
        }));
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await RequireBackfillBookAsync(connection, transaction, request.LedgerBookId, ct).ConfigureAwait(false);
        _ = await LoadBackfillLotAsync(connection, transaction, request.LedgerBookId, request.TaxLotRecordId, ct).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                insert into {Qualified("open_lot_backfill_evidence")}
                    (evidence_record_id, ledger_book_id, tax_lot_record_id, source_system, source_reference,
                     source_uri, content, content_hash_sha256, retention_fingerprint, retained_by, retained_at)
                values (@id, @book, @lot, @source, @reference, @uri, @content, @hash, @fingerprint, @actor, @now)
                on conflict (evidence_record_id) do nothing
                """;
            insert.Parameters.AddWithValue("id", request.EvidenceRecordId);
            insert.Parameters.AddWithValue("book", request.LedgerBookId);
            insert.Parameters.AddWithValue("lot", request.TaxLotRecordId);
            insert.Parameters.AddWithValue("source", source);
            insert.Parameters.AddWithValue("reference", reference);
            insert.Parameters.AddWithValue("uri", request.SourceUri);
            insert.Parameters.AddWithValue("content", content);
            insert.Parameters.AddWithValue("hash", request.ContentHashSha256);
            insert.Parameters.AddWithValue("fingerprint", fingerprint);
            insert.Parameters.AddWithValue("actor", actor);
            insert.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var verify = connection.CreateCommand())
        {
            verify.Transaction = transaction;
            verify.CommandText = $"select retention_fingerprint from {Qualified("open_lot_backfill_evidence")} where evidence_record_id = @id and ledger_book_id = @book";
            verify.Parameters.AddWithValue("id", request.EvidenceRecordId);
            verify.Parameters.AddWithValue("book", request.LedgerBookId);
            if (!string.Equals(await verify.ExecuteScalarAsync(ct).ConfigureAwait(false) as string, fingerprint, StringComparison.Ordinal))
                throw new LedgerValidationException("Evidence record identity was already retained for different source bytes or scope.");
        }
        var result = await ReadBackfillEvidenceAsync(connection, transaction, request.LedgerBookId, request.EvidenceRecordId, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result!;
    }

    public async Task<OpenLotBackfillEvidenceDto?> GetEvidenceAsync(Guid ledgerBookId, Guid evidenceRecordId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await RequireBackfillBookAsync(connection, null, ledgerBookId, ct).ConfigureAwait(false);
        return await ReadBackfillEvidenceAsync(connection, null, ledgerBookId, evidenceRecordId, ct).ConfigureAwait(false);
    }

    public async Task<OpenLotBackfillEvidenceDto> ReviewEvidenceAsync(ReviewOpenLotBackfillEvidenceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationsOriginGuard.RequireHumanOperator(request.ActionOrigin, "review legacy lot acquisition evidence");
        var actor = BackfillText(request.Actor, "Reviewing actor");
        var rationale = BackfillText(request.Rationale, "Review rationale");
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var currency = await RequireBackfillBookAsync(connection, transaction, request.LedgerBookId, ct).ConfigureAwait(false);
        var evidence = await ReadBackfillEvidenceAsync(connection, transaction, request.LedgerBookId, request.EvidenceRecordId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Retained acquisition evidence was not found in this ledger book.");
        if (request.ExpectedVersion != 1 || evidence.Version != request.ExpectedVersion)
            throw new LedgerValidationException("Acquisition evidence review version is stale; retain a new source packet for a new decision.");
        if (string.Equals(actor, evidence.RetainedBy, StringComparison.OrdinalIgnoreCase))
            throw new LedgerValidationException("Acquisition evidence requires a reviewer independent from the retaining actor.");
        var now = DateTimeOffset.UtcNow;
        var reviewed = evidence with { ReviewStatus = request.Accepted ? "Accepted" : "Rejected", Version = 2,
            ReviewedBy = actor, ReviewedAtUtc = now, ReviewRationale = rationale };
        if (request.Accepted)
        {
            await ValidateBackfillAuthorityAsync(reviewed.Facts, currency, ct).ConfigureAwait(false);
            var lot = await LoadBackfillLotAsync(connection, transaction, request.LedgerBookId, evidence.TaxLotRecordId, ct).ConfigureAwait(false);
            _ = OpenLotBackfillRules.Enrich(lot, reviewed);
        }
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            insert into {Qualified("open_lot_backfill_reviews")}
                (evidence_record_id, accepted, reviewed_by, reviewed_at, rationale)
            values (@id, @accepted, @actor, @now, @rationale)
            """;
        insert.Parameters.AddWithValue("id", request.EvidenceRecordId);
        insert.Parameters.AddWithValue("accepted", request.Accepted);
        insert.Parameters.AddWithValue("actor", actor);
        insert.Parameters.AddWithValue("now", now);
        insert.Parameters.AddWithValue("rationale", rationale);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return reviewed;
    }

    public async Task<OpenLotBackfillReceiptDto> ApplyAsync(ApplyOpenLotBackfillRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationsOriginGuard.RequireHumanOperator(request.ActionOrigin, "apply legacy lot acquisition backfill");
        BackfillText(request.Actor, "Applying actor");
        BackfillText(request.IdempotencyKey, "Idempotency key");
        var fingerprint = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request));
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var currency = await RequireBackfillBookAsync(connection, transaction, request.LedgerBookId, ct).ConfigureAwait(false);
        await using (var replay = connection.CreateCommand())
        {
            replay.Transaction = transaction;
            replay.CommandText = $"select request_fingerprint, receipt from {Qualified("open_lot_backfill_receipts")} where ledger_book_id = @book and idempotency_key = @key";
            replay.Parameters.AddWithValue("book", request.LedgerBookId);
            replay.Parameters.AddWithValue("key", request.IdempotencyKey);
            await using var reader = await replay.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (reader.GetString(0) != fingerprint) throw new LedgerValidationException("Backfill idempotency key collides with a different command.");
                return JsonSerializer.Deserialize<OpenLotBackfillReceiptDto>(reader.GetString(1))!;
            }
        }
        var evidence = await ReadBackfillEvidenceAsync(connection, transaction, request.LedgerBookId, request.EvidenceRecordId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Retained acquisition evidence was not found in this ledger book.");
        if (evidence.Version != request.ExpectedEvidenceVersion || request.ExpectedEvidenceVersion != 2)
            throw new LedgerValidationException("Reviewed acquisition evidence version is stale or has not been reviewed.");
        await ValidateBackfillAuthorityAsync(evidence.Facts, currency, ct).ConfigureAwait(false);
        var lot = await LoadBackfillLotAsync(connection, transaction, request.LedgerBookId, request.TaxLotRecordId, ct).ConfigureAwait(false);
        if (request.ExpectedLotVersion <= 0 || lot.Version != request.ExpectedLotVersion)
            throw new LedgerValidationException("Legacy lot version is stale; refresh the exception and review the changed lot.");
        var exceptions = await ReadBackfillExceptionsAsync(connection, transaction, request.LedgerBookId, ct).ConfigureAwait(false);
        var exception = exceptions.SingleOrDefault(e => e.TaxLotRecordId == request.TaxLotRecordId);
        if (exception is null || exception.Version != request.ExpectedExceptionVersion || exception.LotVersion != lot.Version)
            throw new LedgerValidationException("The unresolved backfill exception is missing or stale; survey and review the current lot.");
        var now = DateTimeOffset.UtcNow;
        var enriched = OpenLotBackfillRules.Enrich(lot, evidence) with { Version = lot.Version + 1, UpdatedAt = now };
        var receipt = new OpenLotBackfillReceiptDto(Guid.NewGuid(), request.LedgerBookId, request.TaxLotRecordId,
            request.EvidenceRecordId, evidence.ContentHashSha256, request.IdempotencyKey, request.Actor, now,
            lot.Version, enriched.Version, enriched.ToOpenLot());
        await AppendBackfillReceiptAndUpdateAsync(connection, transaction, request, fingerprint, enriched, receipt, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return receipt;
    }

    private async Task AppendBackfillReceiptAndUpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ApplyOpenLotBackfillRequest request, string fingerprint, LedgerTaxLotRecord enriched,
        OpenLotBackfillReceiptDto receipt, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("open_lot_backfill_receipts")}
                (receipt_id, ledger_book_id, tax_lot_record_id, evidence_record_id, idempotency_key,
                 request_fingerprint, expected_lot_version, resulting_lot_version, snapshot_before, snapshot_after, receipt)
            select @receipt_id, @book, @lot, @evidence_id, @key, @fingerprint, @expected_version, @version,
                to_jsonb(retained), to_jsonb(retained) || jsonb_build_object(
                    'security_id', @security_id::uuid, 'book_position_id', @position_id::uuid,
                    'original_face', @original_face::numeric, 'booked_factor', @booked_factor::numeric,
                    'par_basis', @par_basis::numeric, 'acquisition_terms', @acquisition::jsonb,
                    'version', @version::bigint, 'updated_at', @now::timestamptz), @receipt
            from {Qualified("tax_lots")} retained
            where retained.tax_lot_record_id = @lot and retained.ledger_book_id = @book
              and retained.version = @expected_version and retained.acquisition_terms is null;

            update {Qualified("tax_lots")}
            set security_id = @security_id, book_position_id = @position_id,
                original_face = @original_face, booked_factor = @booked_factor, par_basis = @par_basis,
                acquisition_terms = @acquisition, version = @version, updated_at = @now
            where tax_lot_record_id = @lot and ledger_book_id = @book
              and version = @expected_version and acquisition_terms is null;

            update {Qualified("open_lot_backfill_exceptions")}
            set resolution_receipt_id = @receipt_id, version = version + 1, last_observed_at = @now
            where tax_lot_record_id = @lot and ledger_book_id = @book and version = @exception_version
              and resolution_receipt_id is null;
            """;
        command.Parameters.AddWithValue("receipt_id", receipt.ReceiptId);
        command.Parameters.AddWithValue("book", request.LedgerBookId);
        command.Parameters.AddWithValue("lot", request.TaxLotRecordId);
        command.Parameters.AddWithValue("evidence_id", request.EvidenceRecordId);
        command.Parameters.AddWithValue("key", request.IdempotencyKey);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("expected_version", request.ExpectedLotVersion);
        command.Parameters.AddWithValue("version", enriched.Version);
        command.Parameters.AddWithValue("security_id", enriched.SecurityId);
        command.Parameters.AddWithValue("position_id", enriched.BookPositionId);
        command.Parameters.AddWithValue("original_face", NpgsqlDbType.Numeric, (object?)enriched.OriginalFace ?? DBNull.Value);
        command.Parameters.AddWithValue("booked_factor", NpgsqlDbType.Numeric, (object?)enriched.BookedFactor ?? DBNull.Value);
        command.Parameters.AddWithValue("par_basis", NpgsqlDbType.Numeric, (object?)enriched.ParBasis ?? DBNull.Value);
        command.Parameters.AddWithValue("acquisition", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(enriched.Acquisition));
        command.Parameters.AddWithValue("receipt", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(receipt));
        command.Parameters.AddWithValue("now", enriched.UpdatedAt);
        command.Parameters.AddWithValue("exception_version", request.ExpectedExceptionVersion);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 3)
            throw new LedgerValidationException("Backfill must atomically append its receipt, enrich the exact lot and resolve its exception.");
    }

    private async Task<string> RequireBackfillBookAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid ledgerBookId, CancellationToken ct)
    {
        if (ledgerBookId == Guid.Empty) throw new LedgerValidationException("An exact ledger book is required for backfill.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select base_currency from {Qualified("ledger_books")} where ledger_book_id = @book";
        command.Parameters.AddWithValue("book", ledgerBookId);
        ApplyTenantReadFilter(command, "tenant_id", ResolveCallerTenant());
        if (transaction is not null) command.CommandText += " for update";
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new LedgerValidationException("The ledger book is unavailable in the current tenant scope.");
    }

    private async Task<LedgerTaxLotRecord> LoadBackfillLotAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid ledgerBookId, Guid lotId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select {BackfillLotColumns} from {Qualified("tax_lots")} where ledger_book_id = @book and tax_lot_record_id = @lot for update";
        command.Parameters.AddWithValue("book", ledgerBookId);
        command.Parameters.AddWithValue("lot", lotId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadTaxLot(reader)
            : throw new LedgerValidationException("The durable lot was not found in this ledger book.");
    }

    private async Task<IReadOnlyList<OpenLotBackfillExceptionDto>> ReadBackfillExceptionsAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid ledgerBookId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select ledger_book_id, tax_lot_record_id, lot_id, lot_version, issues, version,
                   first_observed_at, last_observed_at, resolution_receipt_id
            from {Qualified("open_lot_backfill_exceptions")}
            where ledger_book_id = @book and resolution_receipt_id is null
            order by first_observed_at, tax_lot_record_id
            """;
        command.Parameters.AddWithValue("book", ledgerBookId);
        var rows = new List<OpenLotBackfillExceptionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt64(3),
                JsonSerializer.Deserialize<string[]>(reader.GetString(4))!, reader.GetInt64(5),
                ReadUtcDateTimeOffset(reader, 6), ReadUtcDateTimeOffset(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8)));
        return rows;
    }

    private async Task<OpenLotBackfillEvidenceDto?> ReadBackfillEvidenceAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid ledgerBookId, Guid evidenceRecordId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select e.evidence_record_id, e.ledger_book_id, e.tax_lot_record_id, e.content, e.content_hash_sha256,
                   e.source_system, e.source_reference, e.source_uri, e.retained_by, e.retained_at,
                   r.accepted, r.reviewed_by, r.reviewed_at, r.rationale
            from {Qualified("open_lot_backfill_evidence")} e
            left join {Qualified("open_lot_backfill_reviews")} r on r.evidence_record_id = e.evidence_record_id
            where e.ledger_book_id = @book and e.evidence_record_id = @id
            """;
        command.Parameters.AddWithValue("book", ledgerBookId);
        command.Parameters.AddWithValue("id", evidenceRecordId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        var hash = reader.GetString(4);
        var facts = OpenLotBackfillRules.ReadFacts(reader.GetFieldValue<byte[]>(3), hash);
        if (facts.LedgerBookId != reader.GetGuid(1) || facts.TaxLotRecordId != reader.GetGuid(2))
            throw new LedgerValidationException("Retained acquisition source content has inconsistent durable scope.");
        var reviewed = !reader.IsDBNull(10);
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), facts,
            reader.GetString(5), reader.GetString(6), reader.GetString(7), hash, reader.GetString(8),
            ReadUtcDateTimeOffset(reader, 9), reviewed ? 2 : 1,
            reviewed ? reader.GetBoolean(10) ? "Accepted" : "Rejected" : "Pending",
            reviewed ? reader.GetString(11) : null, reviewed ? ReadUtcDateTimeOffset(reader, 12) : null,
            reviewed ? reader.GetString(13) : null);
    }

    private async Task ValidateBackfillAuthorityAsync(OpenLotBackfillFactsDto facts, string functionalCurrency, CancellationToken ct)
    {
        var securityMaster = _backfillSecurityMaster?.Invoke();
        var positions = _backfillPositions?.Invoke();
        if (securityMaster is null || positions is null)
            throw new LedgerValidationException("Authoritative Security Master and book-position stores are required to review or apply legacy backfill.");
        var security = await securityMaster.GetProjectionAsync(facts.SecurityId, ct).ConfigureAwait(false);
        var position = await positions.GetBookPositionAsync(facts.BookPositionId, ct).ConfigureAwait(false);
        if (security is null || security.SecurityId != facts.SecurityId || security.Version != facts.SecurityMasterVersion)
            throw new LedgerValidationException("Retained backfill Security Master identity or version is missing or stale.");
        if (position is null || position.SecurityId != facts.SecurityId || position.PositionId != facts.BookPositionId
            || position.BookContext.LedgerBookId != facts.LedgerBookId || position.Version != facts.BookPositionVersion
            || position.EffectiveFrom > facts.AcquiredDate || (position.EffectiveTo is { } end && end < facts.AcquiredDate))
            throw new LedgerValidationException("Retained backfill book-position mapping, version or acquisition scope is missing or stale.");
        if (functionalCurrency != facts.FunctionalCurrency)
            throw new LedgerValidationException("Retained functional acquisition currency must match the authoritative ledger book.");
    }

    private static string BackfillText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000)
            throw new LedgerValidationException(name + " is required and must not exceed 2000 characters.");
        return value.Trim();
    }
}
