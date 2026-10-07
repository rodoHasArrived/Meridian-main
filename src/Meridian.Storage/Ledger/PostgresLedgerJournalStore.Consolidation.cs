using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task ValidateCurrentConsolidationSourcesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerJournalEntryWrite entry,
        CancellationToken ct)
    {
        var evidence = ConsolidationPostingEvidenceGuard.Validate(entry);
        if (evidence is null)
            return;

        // The ledger audit head is already locked. All supported journal append paths take that
        // lock, so a source/correction cannot commit between this check and the elimination.
        // READ COMMITTED sees the preceding writer; SERIALIZABLE retries the whole transaction
        // if its snapshot predates a concurrent audit-head change.
        foreach (var version in evidence.BookVersions.OrderBy(item => item.LedgerBookId))
        {
            await EnsureTenantRowAsync(connection, transaction, "ledger_books", "ledger_book_id",
                version.LedgerBookId, false, ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                select count(je.journal_entry_id)::integer, coalesce(max(je.global_sequence), 0),
                       exists(select 1 from {Qualified("ledger_books")} b where b.ledger_book_id = @book)
                from {Qualified("journal_entries")} je
                join {Qualified("accounting_periods")} p on p.period_id = je.period_id
                where p.ledger_book_id = @book
                  and coalesce(nullif(btrim(je.metadata ->> 'effectiveDate'), ''),
                      to_char(je.occurred_at at time zone 'UTC', 'YYYY-MM-DD')) <= @as_of;
                """;
            command.Parameters.AddWithValue("book", version.LedgerBookId);
            command.Parameters.AddWithValue("as_of", evidence.Request.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) || !reader.GetBoolean(2)
                || reader.GetInt32(0) != version.JournalCount || reader.GetInt64(1) != version.MaxSequence)
                throw new LedgerValidationException(
                    "Consolidation source balances or posted eliminations changed after review; rerun consolidation and obtain renewed review.");
        }
    }
}

/// <summary>Reserved evidence is checked even when a caller bypasses the workbench.</summary>
internal static class ConsolidationPostingEvidenceGuard
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web);

    internal static ConsolidationEvidenceDto? Validate(LedgerJournalEntryWrite write)
    {
        var tags = write.Entry.Metadata.Tags;
        var hasEvidence = tags?.TryGetValue("consolidation.evidence", out _) == true;
        var hasDigest = tags?.TryGetValue("consolidation.digest", out _) == true;
        var isReserved = write.PostingCommand?.IdempotencyKey?.Trim()
            .StartsWith("consolidation:", StringComparison.OrdinalIgnoreCase) == true
            || write.Entry.Metadata.IdempotencyKey?.Trim()
                .StartsWith("consolidation:", StringComparison.OrdinalIgnoreCase) == true;
        if (!hasEvidence && !hasDigest && !isReserved)
            return null;

        if (write.PostingCommand is not { ApprovalState: AccountingPostingApprovalStateDto.Approved } command
            || string.IsNullOrWhiteSpace(command.ApprovalId))
            throw new LedgerValidationException("Consolidation eliminations require explicit accounting approval before append.");

        if (!hasEvidence || !hasDigest
            || string.IsNullOrWhiteSpace(tags!["consolidation.evidence"])
            || string.IsNullOrWhiteSpace(tags["consolidation.digest"]))
            throw new LedgerValidationException("Consolidation eliminations require retained reviewed source evidence and its digest.");

        var json = tags["consolidation.evidence"];
        var digest = Sha256Digest.ComputeUtf8(json);
        if (!Sha256Digest.FixedEquals(digest, tags["consolidation.digest"]))
            throw new LedgerValidationException("Consolidation source evidence digest does not match the reviewed payload.");

        ConsolidationEvidenceDto evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<ConsolidationEvidenceDto>(json, EvidenceJsonOptions)
                ?? throw new JsonException("Consolidation evidence is null.");
        }
        catch (JsonException)
        {
            throw new LedgerValidationException("Consolidation reviewed source evidence is invalid.");
        }

        if (evidence.Request is null || evidence.Request.OrganizationId == Guid.Empty
            || evidence.Request.OwnershipRootId == Guid.Empty
            || evidence.Request.EliminationBookId != (write.LedgerBookId ?? command.LedgerBookId)
            || evidence.Request.PeriodId != write.PeriodId || evidence.Request.AsOf != command.EffectiveDate
            || (write.Entry.Metadata.EffectiveDate.HasValue && write.Entry.Metadata.EffectiveDate != evidence.Request.AsOf)
            || (command.TreasuryContext?.EffectiveDate is { } treasuryDate && treasuryDate != evidence.Request.AsOf)
            || string.IsNullOrWhiteSpace(evidence.ScopeKey) || string.IsNullOrWhiteSpace(evidence.SourceFingerprint)
            || string.IsNullOrWhiteSpace(evidence.PerimeterFingerprint) || string.IsNullOrWhiteSpace(evidence.RuleVersion))
            throw new LedgerValidationException("Consolidation reviewed evidence must match the posting book, period and effective date.");

        if (evidence.BookVersions is not { Count: 3 }
            || evidence.BookVersions.Any(item => item is null || item.LedgerBookId == Guid.Empty
                || item.JournalCount < 0 || item.MaxSequence < 0 || (item.JournalCount == 0) != (item.MaxSequence == 0))
            || evidence.BookVersions.Select(item => item.LedgerBookId).Distinct().Count() != 3
            || !evidence.BookVersions.Any(item => item.LedgerBookId == evidence.Request.EliminationBookId))
            throw new LedgerValidationException("Consolidation requires reviewed versions for both source books and the elimination book.");

        if (evidence.PriorPostedJournalIds is null
            || evidence.PriorPostedJournalIds.Any(id => id == Guid.Empty)
            || evidence.PriorPostedJournalIds.Distinct().Count() != evidence.PriorPostedJournalIds.Count
            || (evidence.PriorPostedJournalIds.Count > 0
                && (!command.SourceJournalEntryId.HasValue
                    || !evidence.PriorPostedJournalIds.Contains(command.SourceJournalEntryId.Value))))
            throw new LedgerValidationException("Consolidation corrections require linked prior posted elimination evidence.");

        ValidateLines(write, evidence);
        return evidence;
    }

    private static void ValidateLines(LedgerJournalEntryWrite write, ConsolidationEvidenceDto evidence)
    {
        if (evidence.ExpectedLines is null || evidence.ExpectedLines.Count != write.Entry.Lines.Count)
            throw new LedgerValidationException("Consolidation journal lines differ from the reviewed elimination.");
        for (var index = 0; index < evidence.ExpectedLines.Count; index++)
        {
            var expected = evidence.ExpectedLines[index];
            var actual = write.Entry.Lines[index];
            if (expected is null || expected.Amount <= 0m || !Enum.IsDefined(expected.Side)
                || actual.Account.Name != expected.AccountPath
                || !IsSupportedAccount(actual.Account)
                || actual.Debit != (expected.Side == AccountingTemplateLineSideDto.Debit ? expected.Amount : 0m)
                || actual.Credit != (expected.Side == AccountingTemplateLineSideDto.Credit ? expected.Amount : 0m)
                || actual.Account.Symbol != expected.LedgerAccountSymbol
                || actual.Account.FinancialAccountId != expected.LedgerAccountFinancialAccountId
                || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(actual.Dimensions, EvidenceJsonOptions),
                    JsonSerializer.SerializeToElement(expected.Dimensions, EvidenceJsonOptions))
                || !string.Equals(expected.Currency, write.PostingCommand?.BookContext?.BaseCurrency, StringComparison.OrdinalIgnoreCase)
                || (actual.Currency is { } currency && (currency.FxRateToFunctional != 1m
                    || currency.TransactionDebit != actual.Debit || currency.TransactionCredit != actual.Credit
                    || !string.Equals(currency.TransactionCurrency, expected.Currency, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(currency.FunctionalCurrency, expected.Currency, StringComparison.OrdinalIgnoreCase))))
                throw new LedgerValidationException("Consolidation journal lines differ from the reviewed elimination.");
        }
    }

    private static bool IsSupportedAccount(LedgerAccount account) => (account.Name, account.AccountType) is
        ("Assets:Intercompany Receivable", LedgerAccountType.Asset) or
        ("Liabilities:Intercompany Payable", LedgerAccountType.Liability);
}
