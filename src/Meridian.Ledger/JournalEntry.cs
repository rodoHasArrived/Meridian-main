namespace Meridian.Ledger;

/// <summary>
/// A balanced group of <see cref="LedgerEntry"/> lines representing a single economic event.
/// Per double-entry accounting rules the sum of debits must equal the sum of credits
/// (<see cref="IsBalanced"/>).
/// </summary>
public sealed record JournalEntry
{
    /// <summary>Unique identifier for this journal entry.</summary>
    public Guid JournalEntryId { get; private init; }

    /// <summary>When the underlying economic event occurred (replay / simulated time).</summary>
    public DateTimeOffset Timestamp { get; private init; }

    /// <summary>Human-readable description of the economic event.</summary>
    public string Description { get; private init; }

    /// <summary>The individual debit/credit lines that make up this entry.</summary>
    public IReadOnlyList<LedgerEntry> Lines { get; private init; }

    /// <summary>
    /// Optional audit metadata that links the journal entry back to trading-domain events.
    /// </summary>
    public JournalEntryMetadata Metadata { get; private init; }

    /// <summary>
    /// Initializes a new <see cref="JournalEntry"/> with validation.
    /// </summary>
    /// <exception cref="LedgerValidationException">
    /// Thrown when <paramref name="description"/> is null or whitespace,
    /// when <paramref name="lines"/> is null or empty,
    /// or when any line metadata is inconsistent with the journal entry.
    /// </exception>
    public JournalEntry(
        Guid journalEntryId,
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<LedgerEntry> lines,
        JournalEntryMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new LedgerValidationException("Journal entry description must not be null or whitespace.");

        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
            throw new LedgerValidationException("A journal entry must have at least one line.");

        var seenEntryIds = new HashSet<Guid>();
        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);

            if (line.JournalEntryId != journalEntryId)
            {
                throw new LedgerValidationException(
                    $"Ledger entry '{line.EntryId}' references journal entry '{line.JournalEntryId}' but expected '{journalEntryId}'.");
            }

            if (line.Timestamp != timestamp)
            {
                throw new LedgerValidationException(
                    $"Ledger entry '{line.EntryId}' timestamp '{line.Timestamp:O}' does not match journal timestamp '{timestamp:O}'.");
            }

            if (!string.Equals(line.Description, description, StringComparison.Ordinal))
            {
                throw new LedgerValidationException(
                    $"Ledger entry '{line.EntryId}' description must match the journal description.");
            }

            if (!seenEntryIds.Add(line.EntryId))
                throw new LedgerValidationException($"Ledger entry '{line.EntryId}' is duplicated within the journal entry.");
        }

        JournalEntryId = journalEntryId;
        Timestamp = timestamp;
        Description = description;
        Lines = ReadOnlyCollectionHelpers.FreezeList(lines);
        Metadata = metadata?.Normalize() ?? new JournalEntryMetadata();
    }

    /// <summary>
    /// Tolerance used when comparing total debits to total credits.
    /// Prevents false negatives caused by separate rounding paths.
    /// </summary>
    private const decimal BalanceTolerance = 0.000001m;

    /// <summary>
    /// Returns <c>true</c> when the total debits approximately equal the total credits
    /// (within <see cref="BalanceTolerance"/>).
    /// </summary>
    public bool IsBalanced
    {
        get
        {
            var totalDebit = 0m;
            var totalCredit = 0m;
            foreach (var line in Lines)
            {
                totalDebit += line.Debit;
                totalCredit += line.Credit;
            }

            return Math.Abs(totalDebit - totalCredit) <= BalanceTolerance;
        }
    }
}
