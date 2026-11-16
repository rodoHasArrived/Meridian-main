using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// The durable journal is stricter than <see cref="JournalEntry.IsBalanced"/>: legs are stored as
/// <c>numeric(38, 10)</c> and the journal seal requires at least two legs whose stored debits and
/// credits are exactly equal. Checking the same rule before the transaction turns a database
/// constraint failure at commit into a validation error that names the journal and the residual.
/// </summary>
public static class LedgerDurableBalanceRule
{
    /// <summary>Decimal places retained by the journal leg amount columns.</summary>
    public const int StoredScale = 10;

    /// <summary>Returns the reason the store would refuse the entry, or null when it is acceptable.</summary>
    public static string? Violation(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Lines.Count < 2)
            return $"Journal entry '{entry.JournalEntryId}' must contain at least two legs; it has {entry.Lines.Count}.";

        var debits = 0m;
        var credits = 0m;
        foreach (var line in entry.Lines)
        {
            // PostgreSQL rounds numeric input half away from zero when reducing scale.
            debits += Math.Round(line.Debit, StoredScale, MidpointRounding.AwayFromZero);
            credits += Math.Round(line.Credit, StoredScale, MidpointRounding.AwayFromZero);
        }

        return debits == credits
            ? null
            : $"Journal entry '{entry.JournalEntryId}' does not balance exactly at {StoredScale} decimal places: "
              + $"debits {debits} and credits {credits} differ by {debits - credits}. "
              + "The in-memory tolerance does not apply to the durable journal.";
    }

    /// <summary>Throws when the store would refuse the entry.</summary>
    public static void Ensure(JournalEntry entry)
    {
        if (Violation(entry) is { } violation)
            throw new LedgerValidationException(violation);
    }
}
