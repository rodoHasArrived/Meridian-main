using System.Data;
using Meridian.Ledger;
using Npgsql;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Tax-lot persistence for the Postgres ledger store: the lot of record's non-atomic reads and
/// writes, its typed validation, and the single ordinal-positional reader every tax_lots column
/// list in this store must agree with.
/// </summary>
public sealed partial class PostgresLedgerJournalStore
{
    public async Task<LedgerTaxLotRecord> SaveTaxLotAsync(
        LedgerTaxLotRecord lot,
        CancellationToken ct = default)
    {
        ValidateTaxLot(lot);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("tax_lots")} as retained (
                tax_lot_record_id,
                ledger_book_id,
                account_name,
                account_type,
                symbol,
                financial_account_id,
                lot_id,
                acquired_date,
                original_quantity,
                open_quantity,
                unit_cost,
                currency,
                source_journal_entry_id,
                evidence_ref,
                version,
                originating_mutation_batch_id,
                last_mutation_batch_id,
                created_at,
                updated_at,
                security_id,
                book_position_id,
                original_face,
                booked_factor,
                par_basis)
            values (
                @tax_lot_record_id,
                @ledger_book_id,
                @account_name,
                @account_type,
                @symbol,
                @financial_account_id,
                @lot_id,
                @acquired_date,
                @original_quantity,
                @open_quantity,
                @unit_cost,
                @currency,
                @source_journal_entry_id,
                @evidence_ref,
                @version,
                null,
                null,
                @created_at,
                @updated_at,
                @security_id,
                @book_position_id,
                @original_face,
                @booked_factor,
                @par_basis)
            on conflict (tax_lot_record_id) do update
            set ledger_book_id = excluded.ledger_book_id,
                account_name = excluded.account_name,
                account_type = excluded.account_type,
                symbol = excluded.symbol,
                financial_account_id = excluded.financial_account_id,
                lot_id = excluded.lot_id,
                acquired_date = excluded.acquired_date,
                original_quantity = excluded.original_quantity,
                open_quantity = excluded.open_quantity,
                unit_cost = excluded.unit_cost,
                currency = excluded.currency,
                source_journal_entry_id = excluded.source_journal_entry_id,
                evidence_ref = excluded.evidence_ref,
                security_id = excluded.security_id,
                book_position_id = excluded.book_position_id,
                original_face = excluded.original_face,
                booked_factor = excluded.booked_factor,
                par_basis = excluded.par_basis,
                version = retained.version + 1,
                updated_at = excluded.updated_at
            where retained.originating_mutation_batch_id is null
              and @expected_version > 0
              and retained.version = @expected_version
            returning tax_lot_record_id,
                      ledger_book_id,
                      account_name,
                      account_type,
                      symbol,
                      financial_account_id,
                      lot_id,
                      acquired_date,
                      original_quantity,
                      open_quantity,
                      unit_cost,
                      currency,
                      source_journal_entry_id,
                      evidence_ref,
                      version,
                      originating_mutation_batch_id,
                      last_mutation_batch_id,
                      created_at,
                      updated_at,
                      security_id,
                      book_position_id,
                      original_face,
                      booked_factor,
                      par_basis;
            """;
        command.Parameters.AddWithValue("tax_lot_record_id", lot.TaxLotRecordId);
        command.Parameters.AddWithValue("ledger_book_id", lot.LedgerBookId);
        AddAccountParameters(command, lot.Account);
        command.Parameters.AddWithValue("lot_id", RequireLineageText(lot.LotId, nameof(lot.LotId)));
        command.Parameters.AddWithValue("acquired_date", lot.AcquiredDate);
        command.Parameters.AddWithValue("original_quantity", lot.OriginalQuantity);
        command.Parameters.AddWithValue("open_quantity", lot.OpenQuantity);
        command.Parameters.AddWithValue("unit_cost", lot.UnitCost);
        command.Parameters.AddWithValue("currency", RequireLineageText(lot.Currency, nameof(lot.Currency)).ToUpperInvariant());
        command.Parameters.AddWithValue("source_journal_entry_id", (object?)lot.SourceJournalEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("evidence_ref", (object?)NormalizeOptional(lot.EvidenceRef) ?? DBNull.Value);
        command.Parameters.AddWithValue("version", lot.Version <= 0 ? 1 : lot.Version);
        command.Parameters.AddWithValue("expected_version", Math.Max(0, lot.Version));
        command.Parameters.AddWithValue("created_at", lot.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", lot.UpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue(
            "security_id",
            lot.SecurityId == Guid.Empty ? DBNull.Value : lot.SecurityId);
        command.Parameters.AddWithValue(
            "book_position_id",
            lot.BookPositionId == Guid.Empty ? DBNull.Value : lot.BookPositionId);
        command.Parameters.AddWithValue("original_face", (object?)lot.OriginalFace ?? DBNull.Value);
        command.Parameters.AddWithValue("booked_factor", (object?)lot.BookedFactor ?? DBNull.Value);
        command.Parameters.AddWithValue("par_basis", (object?)lot.ParBasis ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Ledger tax lot '{lot.TaxLotRecordId}' was not saved because its version was stale or it is managed by an atomic posting batch.");
        }

        return ReadTaxLot(reader);
    }

    public async Task<IReadOnlyList<LedgerTaxLotRecord>> ListOpenTaxLotsAsync(
        Guid ledgerBookId,
        LedgerAccount account,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        ArgumentNullException.ThrowIfNull(account);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id,
                   original_face,
                   booked_factor,
                   par_basis
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and account_name = @account_name
              and account_type = @account_type
              and symbol is not distinct from @symbol
              and financial_account_id is not distinct from @financial_account_id
              and open_quantity > 0
            order by acquired_date, lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        AddAccountParameters(command, account);

        var lots = new List<LedgerTaxLotRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            lots.Add(ReadTaxLot(reader));
        }

        return lots;
    }

    public async Task<IReadOnlyList<LedgerTaxLotRecord>> GetTaxLotsByIdsAsync(
        Guid ledgerBookId,
        IReadOnlyList<Guid> taxLotRecordIds,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        ArgumentNullException.ThrowIfNull(taxLotRecordIds);
        var ids = taxLotRecordIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Any(static id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "At least one distinct non-empty tax-lot record id is required.",
                nameof(taxLotRecordIds));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id,
                   original_face,
                   booked_factor,
                   par_basis
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and tax_lot_record_id = any(@tax_lot_record_ids)
            order by tax_lot_record_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("tax_lot_record_ids", ids);

        var lots = new List<LedgerTaxLotRecord>(ids.Length);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            lots.Add(ReadTaxLot(reader));
        }

        return lots;
    }

    private static void ValidateTaxLot(LedgerTaxLotRecord lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(lot.Account);

        if (lot.TaxLotRecordId == Guid.Empty)
        {
            throw new ArgumentException("Tax-lot record id is required.", nameof(lot));
        }

        if (lot.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(lot));
        }

        _ = RequireLineageText(lot.LotId, nameof(lot.LotId));
        _ = RequireLineageText(lot.Currency, nameof(lot.Currency));

        if (lot.OriginalQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.OriginalQuantity, "Original tax-lot quantity must be positive.");
        }

        if (lot.OpenQuantity < 0m || lot.OpenQuantity > lot.OriginalQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.OpenQuantity, "Open tax-lot quantity must be between zero and original quantity.");
        }

        if (lot.UnitCost < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.UnitCost, "Tax-lot unit cost cannot be negative.");
        }

        if (lot.Version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.Version, "Tax-lot version cannot be negative.");
        }

        if ((lot.SecurityId == Guid.Empty) != (lot.BookPositionId == Guid.Empty))
        {
            throw new LedgerValidationException(
                "Tax-lot Security Master and book-position identities must either both be supplied or both be absent.");
        }

        ValidateFaceValueTerms(lot);

        if (lot.OriginatingMutationBatchId.HasValue || lot.LastMutationBatchId.HasValue)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot mutation lineage can only be written through IAtomicTaxLotJournalStore.");
        }
    }

    private static LedgerTaxLotRecord ReadTaxLot(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ReadLedgerAccount(reader, 2),
            reader.GetString(6),
            DateOnly.FromDateTime(reader.GetDateTime(7)),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetString(11),
            ReadUtcDateTimeOffset(reader, 17),
            ReadUtcDateTimeOffset(reader, 18),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetInt64(14),
            reader.IsDBNull(15) ? null : reader.GetGuid(15),
            reader.IsDBNull(16) ? null : reader.GetGuid(16),
            reader.IsDBNull(19) ? Guid.Empty : reader.GetGuid(19),
            reader.IsDBNull(20) ? Guid.Empty : reader.GetGuid(20),
            reader.IsDBNull(21) ? null : reader.GetDecimal(21),
            reader.IsDBNull(22) ? null : reader.GetDecimal(22),
            reader.IsDBNull(23) ? null : reader.GetDecimal(23));

    /// <summary>
    /// Enforces the acquisition-time par conventions the lot of record now carries, mirroring the
    /// <c>FaceValueLot</c> constructor invariants and the <c>ck_tax_lots_face_terms</c> /
    /// <c>ck_tax_lots_face_terms_complete</c> database constraints. Keeping the guard here means a
    /// malformed lot fails as a typed ledger validation error rather than as a Postgres constraint
    /// violation raised from inside a serializable posting transaction.
    /// </summary>
    private static void ValidateFaceValueTerms(LedgerTaxLotRecord lot)
    {
        var supplied = (lot.OriginalFace.HasValue ? 1 : 0) +
                       (lot.BookedFactor.HasValue ? 1 : 0) +
                       (lot.ParBasis.HasValue ? 1 : 0);
        if (supplied == 0)
        {
            return;
        }

        if (supplied != 3)
        {
            throw new LedgerValidationException(
                "Tax-lot face amount, booked factor, and par basis must be supplied together; a face " +
                "without the basis it was priced against, or without the factor it was booked at, is " +
                "the half-stated convention the lot of record exists to remove.");
        }

        if (lot.OriginalFace!.Value <= 0m)
        {
            throw new LedgerValidationException("Tax-lot original face must be positive.");
        }

        if (lot.BookedFactor!.Value is <= 0m or > 1m)
        {
            throw new LedgerValidationException("Tax-lot booked factor must be in (0, 1].");
        }

        if (lot.ParBasis!.Value <= 0m)
        {
            throw new LedgerValidationException("Tax-lot par basis must be positive.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerTaxLotRecord>> ListOpenTaxLotsByAssetScopeAsync(
        Guid ledgerBookId,
        Guid securityId,
        Guid bookPositionId,
        DateOnly effectiveDate,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        if (securityId == Guid.Empty)
        {
            throw new ArgumentException("Security Master identity is required.", nameof(securityId));
        }

        if (bookPositionId == Guid.Empty)
        {
            throw new ArgumentException("Book-position identity is required.", nameof(bookPositionId));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id,
                   original_face,
                   booked_factor,
                   par_basis
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and security_id = @security_id
              and book_position_id = @book_position_id
              and acquired_date <= @effective_date
              and open_quantity > 0
            order by acquired_date, lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("book_position_id", bookPositionId);
        command.Parameters.AddWithValue("effective_date", effectiveDate);

        var lots = new List<LedgerTaxLotRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            lots.Add(ReadTaxLot(reader));
        }

        return lots;
    }

}
