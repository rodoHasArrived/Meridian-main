using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Non-bypassable shape and source-economics checks for explicit agreement provenance.</summary>
public static class IntercompanyAgreementPostingGuard
{
    public static bool HasReferences(JournalEntry entry)
        => entry.Lines.Any(line => line.Dimensions is { } dimensions &&
            (dimensions.IntercompanyAgreementId.HasValue || dimensions.IntercompanyAgreementVersion.HasValue));

    public static void ValidateShape(JournalEntry entry)
    {
        foreach (var line in entry.Lines)
        {
            var dimensions = line.Dimensions;
            if (dimensions is null ||
                (!dimensions.IntercompanyAgreementId.HasValue && !dimensions.IntercompanyAgreementVersion.HasValue))
                continue;
            if (dimensions.IntercompanyAgreementId is not { } id || id == Guid.Empty ||
                dimensions.IntercompanyAgreementVersion is not > 0)
                throw new LedgerValidationException("Intercompany agreement ID and positive approved version must be supplied together.");
            if (!Guid.TryParse(dimensions.EntityId, out var entity) || entity == Guid.Empty ||
                !Guid.TryParse(dimensions.CounterpartyId, out var counterparty) || counterparty == Guid.Empty ||
                entity == counterparty)
                throw new LedgerValidationException("Intercompany agreement references require distinct retained entity and counterparty identities.");
        }
    }

    internal static IntercompanyAgreementPostingRequest BuildRequest(LedgerJournalEntryWrite write,
        LedgerBookRecord book, string tenantId, bool verifiedReversal)
    {
        ValidateShape(write.Entry);
        ValidateAccountingDate(write);
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Equals("all", StringComparison.OrdinalIgnoreCase))
            throw new LedgerValidationException("Agreement posting requires authoritative tenant-owned book scope.");
        if (book.FundStructureNodeKind != FundStructureNodeKindDto.Entity)
            throw new LedgerValidationException("Agreement posting requires a retained legal-entity ledger book.");
        var lines = write.Entry.Lines.Where(line => line.Dimensions?.IntercompanyAgreementId.HasValue == true)
            .Select(line =>
            {
                var dimensions = line.Dimensions!;
                var entity = Guid.Parse(dimensions.EntityId!);
                if (entity != book.FundStructureNodeId)
                    throw new LedgerValidationException("The agreement posting entity must match the authoritative ledger-book entity.");
                if (line.Currency is { } currency &&
                    !string.Equals(currency.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                    throw new LedgerValidationException("Agreement posting functional currency must match the authoritative ledger book.");
                return new IntercompanyAgreementPostingLine(dimensions.IntercompanyAgreementId!.Value,
                    dimensions.IntercompanyAgreementVersion!.Value, entity, Guid.Parse(dimensions.CounterpartyId!),
                    line.Currency?.TransactionCurrency ?? book.BaseCurrency);
            }).ToArray();
        return new IntercompanyAgreementPostingRequest(tenantId.Trim(), book.FundStructureNodeId,
            write.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(write.Entry.Timestamp.UtcDateTime),
            book.BaseCurrency, lines, verifiedReversal);
    }

    internal static void ValidateAccountingDate(LedgerJournalEntryWrite write)
    {
        if (!HasReferences(write.Entry) || write.PostingCommand is not { } command)
            return;
        if ((write.Entry.Metadata.EffectiveDate is { } date && date != command.EffectiveDate) ||
            (command.TreasuryContext?.EffectiveDate is { } treasuryDate && treasuryDate != command.EffectiveDate))
            throw new LedgerValidationException("Agreement posting accounting dates must agree across journal, command and treasury context.");
    }

    /// <summary>
    /// Historical authority is permitted only for a complete one-to-one inverse. A source ID,
    /// reversal label or caller-provided flag alone grants no exemption from current terms.
    /// </summary>
    internal static bool IsExactSourceReversal(LedgerJournalEntryWrite write, LedgerJournalEntryRecord source)
    {
        if (write.PostingCommand?.Intent != AccountingPostingIntentDto.Reversal ||
            write.SourceJournalEntryId != source.Entry.JournalEntryId ||
            write.AccountingBasis != source.AccountingBasis ||
            write.Entry.JournalEntryId == source.Entry.JournalEntryId ||
            write.Entry.Lines.Count != source.Entry.Lines.Count)
            return false;
        var remaining = source.Entry.Lines.ToList();
        foreach (var line in write.Entry.Lines)
        {
            var index = remaining.FindIndex(original =>
                line.EntryId != original.EntryId && line.Account == original.Account &&
                LedgerRetainedValueComparison.AmountsMatch(line.Debit, original.Credit) &&
                LedgerRetainedValueComparison.AmountsMatch(line.Credit, original.Debit) &&
                ExactDimensions(original.Dimensions, line.Dimensions) &&
                InverseCurrency(original.Currency, line.Currency));
            if (index < 0)
                return false;
            remaining.RemoveAt(index);
        }
        return remaining.Count == 0;
    }

    private static bool ExactDimensions(LedgerLineDimensionSet? source, LedgerLineDimensionSet? reversal)
    {
        // Replay's historical text comparison is intentionally case-insensitive. A reversal must
        // also clear the same dimensional ledger bucket, whose canonical scope keys preserve case.
        // Canonicalizing sorts/normalizes dictionary fields without changing their actual values.
        var original = PostgresLedgerJournalStore.CanonicalizeLineDimensions(source);
        var inverse = PostgresLedgerJournalStore.CanonicalizeLineDimensions(reversal);
        return string.Equals(System.Text.Json.JsonSerializer.Serialize(original),
            System.Text.Json.JsonSerializer.Serialize(inverse), StringComparison.Ordinal);
    }

    private static bool InverseCurrency(LedgerEntryCurrency? source, LedgerEntryCurrency? reversal)
    {
        if (source is null || reversal is null)
            return source is null && reversal is null;
        return string.Equals(source.TransactionCurrency, reversal.TransactionCurrency, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(source.FunctionalCurrency, reversal.FunctionalCurrency, StringComparison.OrdinalIgnoreCase) &&
            LedgerRetainedValueComparison.AmountsMatch(source.FxRateToFunctional, reversal.FxRateToFunctional) &&
            LedgerRetainedValueComparison.AmountsMatch(source.TransactionDebit, reversal.TransactionCredit) &&
            LedgerRetainedValueComparison.AmountsMatch(source.TransactionCredit, reversal.TransactionDebit);
    }
}
