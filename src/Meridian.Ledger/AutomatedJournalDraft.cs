namespace Meridian.Ledger;

/// <summary>
/// Balanced journal draft produced from a normalized automated journal event.
/// </summary>
public sealed record AutomatedJournalDraft(
    AutomatedJournalEvent Event,
    string Description,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)> Lines,
    JournalEntryMetadata Metadata)
{
    /// <summary>Total debits in the projected journal draft.</summary>
    public decimal TotalDebits => Lines.Sum(static line => line.debit);

    /// <summary>Total credits in the projected journal draft.</summary>
    public decimal TotalCredits => Lines.Sum(static line => line.credit);

    /// <summary>True when total debits equal total credits.</summary>
    public bool IsBalanced => TotalDebits == TotalCredits;
}

