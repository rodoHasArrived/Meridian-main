namespace Meridian.Ledger;

/// <summary>
/// Explicit multi-currency detail for one <see cref="LedgerEntry"/> leg. The leg's
/// <see cref="LedgerEntry.Debit"/> / <see cref="LedgerEntry.Credit"/> remain the functional
/// (base) amounts the ledger balances and reports on; this record preserves the original
/// transaction-currency amounts and the FX rate applied, so currency no longer has to be
/// inferred from an account symbol.
/// </summary>
public sealed record LedgerEntryCurrency
{
    public LedgerEntryCurrency(
        string transactionCurrency,
        string functionalCurrency,
        decimal transactionDebit,
        decimal transactionCredit,
        decimal fxRateToFunctional)
    {
        TransactionCurrency = NormalizeCurrency(transactionCurrency, nameof(transactionCurrency));
        FunctionalCurrency = NormalizeCurrency(functionalCurrency, nameof(functionalCurrency));

        if (transactionDebit < 0m)
            throw new LedgerValidationException($"Transaction debit cannot be negative (was {transactionDebit}).");
        if (transactionCredit < 0m)
            throw new LedgerValidationException($"Transaction credit cannot be negative (was {transactionCredit}).");
        if (transactionDebit == 0m && transactionCredit == 0m)
            throw new LedgerValidationException("A currency-aware ledger leg must have a non-zero transaction amount.");
        if (transactionDebit != 0m && transactionCredit != 0m)
        {
            throw new LedgerValidationException(
                "Exactly one of transaction debit or transaction credit must be non-zero " +
                $"(debit={transactionDebit}, credit={transactionCredit}).");
        }

        if (fxRateToFunctional <= 0m)
            throw new LedgerValidationException($"FX rate to functional currency must be positive (was {fxRateToFunctional}).");

        TransactionDebit = transactionDebit;
        TransactionCredit = transactionCredit;
        FxRateToFunctional = fxRateToFunctional;
    }

    /// <summary>ISO-style code of the currency the transaction amounts are denominated in.</summary>
    public string TransactionCurrency { get; }

    /// <summary>ISO-style code of the functional/base currency the leg's debit/credit are in.</summary>
    public string FunctionalCurrency { get; }

    /// <summary>Original debit amount in <see cref="TransactionCurrency"/>.</summary>
    public decimal TransactionDebit { get; }

    /// <summary>Original credit amount in <see cref="TransactionCurrency"/>.</summary>
    public decimal TransactionCredit { get; }

    /// <summary>Multiplier converting a transaction-currency amount to the functional currency.</summary>
    public decimal FxRateToFunctional { get; }

    /// <summary>True when the transaction and functional currencies are the same.</summary>
    public bool IsFunctionalCurrency =>
        string.Equals(TransactionCurrency, FunctionalCurrency, StringComparison.Ordinal);

    /// <summary>Which side of the leg carries the amount, mirroring the functional debit/credit.</summary>
    public bool IsDebit => TransactionDebit > 0m;

    internal static string NormalizeCurrency(string currency, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new LedgerValidationException($"{parameterName} must not be null or whitespace.");

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(static c => c is >= 'A' and <= 'Z'))
            throw new LedgerValidationException($"{parameterName} must be a three-letter ISO-style currency code.");

        return normalized;
    }
}
