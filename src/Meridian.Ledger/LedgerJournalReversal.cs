namespace Meridian.Ledger;

/// <summary>
/// Produces a balanced reversing journal entry from a posted entry by swapping every line's debit
/// and credit. Corrections are booked as new immutable entries rather than mutating the original,
/// preserving double-entry immutability. Because swapping debit and credit preserves the balance
/// identity, reversing a balanced entry always yields a balanced entry. The reversal links back to
/// its original through the description and a <c>reversal.of</c> tag, so audit can reconstruct the
/// original → reversal → rebook chain.
/// </summary>
public static class LedgerJournalReversal
{
    /// <summary>Metadata tag carrying the reversed journal entry's identifier.</summary>
    public const string ReversalOfTag = "reversal.of";

    /// <summary>Metadata tag carrying the correction reason, when supplied.</summary>
    public const string ReversalReasonTag = "reversal.reason";

    /// <summary>
    /// Builds the reversing entry for <paramref name="original"/> under a new
    /// <paramref name="reversalJournalId"/>. Each line's debit and credit are swapped; dimensions
    /// and account are preserved. The reversal is dated at <paramref name="reversedAt"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the reversal id equals the original id.</exception>
    public static JournalEntry Reverse(
        JournalEntry original,
        Guid reversalJournalId,
        DateTimeOffset reversedAt,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (reversalJournalId == original.JournalEntryId)
            throw new ArgumentException("Reversal journal id must differ from the original journal id.", nameof(reversalJournalId));

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var description = normalizedReason is null
            ? $"Reversal of {original.Description}"
            : $"Reversal of {original.Description} — {normalizedReason}";

        var lines = original.Lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                reversalJournalId,
                reversedAt,
                line.Account,
                debit: line.Credit, // swap: a debit becomes a credit and vice versa
                credit: line.Debit,
                description,
                line.Dimensions))
            .ToList();

        return new JournalEntry(reversalJournalId, reversedAt, description, lines, BuildReversalMetadata(original, normalizedReason));
    }

    private static JournalEntryMetadata BuildReversalMetadata(JournalEntry original, string? reason)
    {
        var source = original.Metadata;
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source.Tags is not null)
        {
            foreach (var tag in source.Tags)
                tags[tag.Key] = tag.Value;
        }

        tags[ReversalOfTag] = original.JournalEntryId.ToString("D");
        if (reason is not null)
            tags[ReversalReasonTag] = reason;

        // Identifying provenance is carried forward, but the original idempotency key is
        // intentionally dropped so the reversal is a distinct, independently-guarded posting.
        return new JournalEntryMetadata(
            ActivityType: source.ActivityType,
            Symbol: source.Symbol,
            SecurityId: source.SecurityId,
            OrderId: source.OrderId,
            FillId: source.FillId,
            LedgerBook: source.LedgerBook,
            LedgerView: source.LedgerView,
            FinancialAccountId: source.FinancialAccountId,
            EffectiveDate: source.EffectiveDate,
            FundEventId: source.FundEventId,
            FundEventType: source.FundEventType,
            CapitalAccountId: source.CapitalAccountId,
            InvestorId: source.InvestorId,
            Tags: tags);
    }
}
