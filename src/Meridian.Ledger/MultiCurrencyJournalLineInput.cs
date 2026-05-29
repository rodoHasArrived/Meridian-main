namespace Meridian.Ledger;

/// <summary>
/// Local-currency journal line before conversion to the reporting/base-currency ledger amount.
/// </summary>
public sealed record MultiCurrencyJournalLineInput
{
    public MultiCurrencyJournalLineInput(
        LedgerAccount account,
        string localCurrency,
        decimal localDebit,
        decimal localCredit,
        decimal fxRateToBase)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(localCurrency))
            throw new ArgumentException("Local currency must not be null or whitespace.", nameof(localCurrency));
        if (localDebit < 0m)
            throw new ArgumentOutOfRangeException(nameof(localDebit), localDebit, "Local debit cannot be negative.");
        if (localCredit < 0m)
            throw new ArgumentOutOfRangeException(nameof(localCredit), localCredit, "Local credit cannot be negative.");
        if (localDebit == 0m && localCredit == 0m)
            throw new ArgumentException("A multi-currency journal line must have a non-zero local amount.");
        if (localDebit != 0m && localCredit != 0m)
            throw new ArgumentException("Exactly one of local debit or local credit must be non-zero.");
        if (fxRateToBase <= 0m)
            throw new ArgumentOutOfRangeException(nameof(fxRateToBase), fxRateToBase, "FX rate to base must be positive.");

        Account = account;
        LocalCurrency = NormalizeCurrency(localCurrency, nameof(localCurrency));
        LocalDebit = localDebit;
        LocalCredit = localCredit;
        FxRateToBase = fxRateToBase;
    }

    public LedgerAccount Account { get; }

    public string LocalCurrency { get; }

    public decimal LocalDebit { get; }

    public decimal LocalCredit { get; }

    public decimal FxRateToBase { get; }

    private static string NormalizeCurrency(string currency, string parameterName)
    {
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(static c => c is >= 'A' and <= 'Z'))
            throw new ArgumentException("Currency codes must be three alphabetic ISO-style characters.", parameterName);

        return normalized;
    }
}
