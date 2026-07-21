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

    /// <summary>Optional dimensional accounting scope for this specific ledger line.</summary>
    public LedgerLineDimensionSet? Dimensions { get; private init; }

    /// <summary>
    /// Optional transaction-currency detail. When present, <see cref="Debit"/> / <see cref="Credit"/>
    /// remain the functional (base) amounts and this carries the original currency, transaction
    /// amounts, and FX rate. When null the leg is currency-blind (legacy behavior).
    /// </summary>
    public LedgerEntryCurrency? Currency { get; private init; }

    /// <summary>
    /// Initializes a new <see cref="LedgerEntry"/> with validation.
    /// </summary>
    /// <exception cref="LedgerValidationException">
    /// Thrown when <paramref name="debit"/> or <paramref name="credit"/> is negative,
    /// when both are zero or both are non-zero, or when <paramref name="currency"/> disagrees
    /// with the debit/credit side of the leg.
    /// </exception>
    public LedgerEntry(
        Guid entryId,
        Guid journalEntryId,
        DateTimeOffset timestamp,
        LedgerAccount account,
        decimal debit,
        decimal credit,
        string description,
        LedgerLineDimensionSet? dimensions = null,
        LedgerEntryCurrency? currency = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(description);

        if (debit < 0m)
            throw new LedgerValidationException($"Debit amount cannot be negative (was {debit}).");

        if (credit < 0m)
            throw new LedgerValidationException($"Credit amount cannot be negative (was {credit}).");

        if (debit == 0m && credit == 0m)
            throw new LedgerValidationException(
                "A ledger entry must have a non-zero amount; both Debit and Credit are zero.");

        if (debit != 0m && credit != 0m)
            throw new LedgerValidationException(
                "Exactly one of Debit or Credit must be non-zero per ledger entry " +
                $"(debit={debit}, credit={credit}).");

        if (currency is not null && currency.IsDebit != debit > 0m)
        {
            throw new LedgerValidationException(
                "Transaction currency detail must be on the same side as the functional amount " +
                $"(functional debit={debit}, credit={credit}; transaction debit={currency.TransactionDebit}, credit={currency.TransactionCredit}).");
        }

        EntryId = entryId;
        JournalEntryId = journalEntryId;
        Timestamp = timestamp;
        Account = account;
        Debit = debit;
        Credit = credit;
        Description = description;
        Dimensions = dimensions;
        Currency = currency;
    }
}

