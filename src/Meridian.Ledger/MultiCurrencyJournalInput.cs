namespace Meridian.Ledger;

/// <summary>
/// Journal-entry input that records local-currency amounts and converts them to a base-currency posting.
/// </summary>
public sealed record MultiCurrencyJournalInput
{
    public MultiCurrencyJournalInput(
        DateTimeOffset timestamp,
        string description,
        string baseCurrency,
        IReadOnlyList<MultiCurrencyJournalLineInput> lines,
        JournalEntryMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Journal entry description must not be null or whitespace.", nameof(description));
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw new ArgumentException("Base currency must not be null or whitespace.", nameof(baseCurrency));
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            throw new ArgumentException("A multi-currency journal entry must have at least one line.", nameof(lines));

        Timestamp = timestamp;
        Description = description.Trim();
        BaseCurrency = NormalizeCurrency(baseCurrency, nameof(baseCurrency));
        Lines = lines;
        Metadata = metadata;
    }

    public DateTimeOffset Timestamp { get; }

    public string Description { get; }

    public string BaseCurrency { get; }

    public IReadOnlyList<MultiCurrencyJournalLineInput> Lines { get; }

    public JournalEntryMetadata? Metadata { get; }

    private static string NormalizeCurrency(string currency, string parameterName)
    {
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(static c => c is >= 'A' and <= 'Z'))
            throw new ArgumentException("Currency codes must be three alphabetic ISO-style characters.", parameterName);

        return normalized;
    }
}
