using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : ILedgerSecurityEntitlementInventory
{
    public async Task<LedgerSecurityEntitlementInventorySnapshot> ReadSecurityEntitlementInventoryAsync(
        string tenantId, string companyId, Guid ledgerBookId, Guid securityId, DateOnly asOfDate,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        if (ledgerBookId == Guid.Empty || securityId == Guid.Empty || asOfDate == default)
            throw new ArgumentException("Exact book, security and entitlement date are required.");
        var caller = ResolveCallerTenant();
        if (!TenantReadPredicate.IsResolvedTenant(caller)
            || !string.Equals(caller!.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Entitlement inventory requires the authenticated tenant, including in compatibility deployments.");

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        // This is a read-only repeatable-read snapshot with explicit tenant/company ownership
        // below. Taking writer locks here would deadlock a recheck invoked while the producer
        // lease holds the same book exclusively on its own posting connection.
        LedgerBookRecord book;
        await using (var owner = connection.CreateCommand())
        {
            owner.Transaction = transaction;
            owner.CommandText = $"""
                select b.ledger_book_id, b.fund_profile_id, b.fund_structure_node_id,
                       b.fund_structure_node_kind, b.display_name, b.base_currency,
                       b.accounting_basis, b.accounting_policy_id, b.accounting_policy_version,
                       b.description, b.created_at, b.updated_at
                from {Qualified("ledger_books")} b
                join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
                where b.ledger_book_id = @book_id
                  and lower(trim(b.tenant_id)) = lower(trim(@tenant))
                  and lower(trim(t.tenant_id)) = lower(trim(@tenant))
                  and lower(trim(t.company_id)) = lower(trim(@company));
                """;
            owner.Parameters.AddWithValue("book_id", ledgerBookId);
            owner.Parameters.AddWithValue("tenant", tenantId);
            owner.Parameters.AddWithValue("company", companyId);
            await using var reader = await owner.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The complete entitlement book has no matching retained tenant/company ownership.");
            book = ReadLedgerBook(reader);
        }

        var allBookLots = new List<LedgerTaxLotRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            // No account, position, current-open, or page filter is permitted. Unknown legacy
            // identities are inspected too: silently filtering them cannot establish completeness.
            command.CommandText = $"""
                select tax_lot_record_id, ledger_book_id, account_name, account_type, symbol,
                       financial_account_id, lot_id, acquired_date, original_quantity,
                       open_quantity, unit_cost, currency, source_journal_entry_id, evidence_ref,
                       version, originating_mutation_batch_id, last_mutation_batch_id,
                       created_at, updated_at, security_id, book_position_id, original_face,
                       booked_factor, par_basis, acquisition_terms, basis_adjustment, current_factor
                from {Qualified("tax_lots")}
                where ledger_book_id = @book_id and acquired_date <= @as_of
                order by security_id, book_position_id, tax_lot_record_id;
                """;
            command.Parameters.AddWithValue("book_id", ledgerBookId);
            command.Parameters.AddWithValue("as_of", asOfDate);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                allBookLots.Add(ReadTaxLot(reader));
        }

        if (allBookLots.Any(lot => lot.SecurityId == Guid.Empty || lot.BookPositionId == Guid.Empty))
            throw new LedgerValidationException("Complete entitlement inventory is unavailable: unidentified legacy lots require reviewed identity backfill.");
        var sourceLots = allBookLots.Where(lot => lot.SecurityId == securityId).ToArray();
        if (sourceLots.Any(lot => lot.Acquisition is null || lot.SourceJournalEntryId is null
            || lot.SourceJournalEntryId == Guid.Empty || lot.Version <= 0))
            throw new LedgerValidationException("Complete entitlement inventory requires retained acquisition facts and source journal identities for every related lot.");
        var acquisitionJournalIds = sourceLots.Select(lot => lot.SourceJournalEntryId!.Value).Distinct().ToArray();
        if (acquisitionJournalIds.Length > 0)
        {
            await using var acquisitionSources = connection.CreateCommand();
            acquisitionSources.Transaction = transaction;
            acquisitionSources.CommandText = $"""
                select count(distinct j.journal_entry_id)
                from {Qualified("journal_entries")} j
                join {Qualified("accounting_periods")} p on p.period_id = j.period_id
                where j.journal_entry_id = any(@source_ids) and p.ledger_book_id = @book_id;
                """;
            acquisitionSources.Parameters.AddWithValue("source_ids", acquisitionJournalIds);
            acquisitionSources.Parameters.AddWithValue("book_id", ledgerBookId);
            var verifiedCount = Convert.ToInt64(await acquisitionSources.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (verifiedCount != acquisitionJournalIds.Length)
                throw new LedgerValidationException("A retained acquisition journal is missing or belongs to another book; complete entitlement cannot be established.");
        }
        var history = await ReadDatedLotQuantitiesAsync(connection, transaction, ledgerBookId,
            sourceLots.Select(lot => lot.TaxLotRecordId).ToArray(), ct).ConfigureAwait(false);
        var byLot = history.ToLookup(mutation => mutation.TaxLotRecordId);
        var held = sourceLots.Select(lot => HistoricalTaxLotQuantity.Project(lot,
                byLot[lot.TaxLotRecordId].ToArray(), asOfDate))
            .Where(lot => lot.OpenQuantity > 0m)
            .OrderBy(lot => lot.BookPositionId).ThenBy(lot => lot.TaxLotRecordId).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Method = "complete-book-journal-effective-lot-inventory-v1",
            Isolation = "RepeatableRead",
            TenantId = tenantId.Trim(),
            CompanyId = companyId.Trim(),
            Book = book,
            SecurityId = securityId,
            AsOfDate = asOfDate,
            InspectedBookLotCount = allBookLots.Count,
            SourceBookLots = allBookLots,
            RelatedLotMutationHistory = history,
            HeldLots = held,
            Complete = true
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new(tenantId.Trim(), companyId.Trim(), book, securityId, asOfDate,
            allBookLots.Count, held, json, Sha256Digest.ComputeUtf8(json));
    }
}
