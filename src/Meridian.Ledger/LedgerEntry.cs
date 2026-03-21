namespace Meridian.Ledger;

/// <summary>
/// One line in a <see cref="JournalEntry"/> representing a debit or credit to a specific account.
/// Exactly one of <see cref="Debit"/> or <see cref="Credit"/> must be non-zero; neither may be negative.
/// </summary>
public sealed record LedgerEntry
{
    /// <summary>Unique identifier for this ledger line.</summary>
    public Guid EntryId { get; private init; }

    /// <summary>The journal entry this line belongs to.</summary>
    public Guid JournalEntryId { get; private init; }

    /// <summary>When the underlying economic event occurred (replay / simulated time).</summary>
    public DateTimeOffset Timestamp { get; private init; }

    /// <summary>Account being debited or credited.</summary>
    public LedgerAccount Account { get; private init; }

    /// <summary>Non-negative debit amount; must be zero when <see cref="Credit"/> is non-zero.</summary>
    public decimal Debit { get; private init; }

    /// <summary>Non-negative credit amount; must be zero when <see cref="Debit"/> is non-zero.</summary>
    public decimal Credit { get; private init; }

    /// <summary>Human-readable description of the economic event.</summary>
    public string Description { get; private init; }

    /// <summary>
    /// Initializes a new <see cref="LedgerEntry"/> with validation.
    /// </summary>
    /// <exception cref="LedgerValidationException">
    /// Thrown when <paramref name="debit"/> or <paramref name="credit"/> is negative,
    /// or when both are zero or both are non-zero.
    /// </exception>
    public LedgerEntry(
        Guid entryId,
        Guid journalEntryId,
        DateTimeOffset timestamp,
        LedgerAccount account,
        decimal debit,
        decimal credit,
        string description)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(description);

        if (debit < 0m)
            throw new LedgerValidationException($"Debit amount cannot be negative (was {debit}).");

        if (credit < 0m)
            throw new LedgerValidationException($"Credit amount cannot be negative (was {credit}).");

        if ((debit == 0m) == (credit == 0m))
            throw new LedgerValidationException(
                "Exactly one of Debit or Credit must be non-zero per ledger entry " +
                $"(debit={debit}, credit={credit}).");

        EntryId = entryId;
        JournalEntryId = journalEntryId;
        Timestamp = timestamp;
        Account = account;
        Debit = debit;
        Credit = credit;
        Description = description;
    }
}

