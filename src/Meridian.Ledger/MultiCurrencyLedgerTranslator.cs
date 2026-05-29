namespace Meridian.Ledger;

/// <summary>
/// Translates local-currency ledger balances into a base currency and prepares FX revaluation lines.
/// </summary>
public static class MultiCurrencyLedgerTranslator
{
    /// <summary>
    /// Translates the current ledger trial balance to <paramref name="baseCurrency"/>.
    /// Account currency is supplied by <paramref name="accountCurrencies"/> or inferred from ISO-like account symbols.
    /// </summary>
    public static LedgerCurrencyTranslation Translate(
        IReadOnlyLedger ledger,
        string baseCurrency,
        IReadOnlyDictionary<string, decimal> fxRatesToBase,
        IReadOnlyDictionary<LedgerAccount, string>? accountCurrencies = null,
        IReadOnlyDictionary<LedgerAccount, decimal>? carryingBaseBalances = null,
        string? financialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return Translate(
            ledger.TrialBalance(financialAccountId),
            baseCurrency,
            fxRatesToBase,
            accountCurrencies,
            carryingBaseBalances);
    }

    /// <summary>
    /// Translates a flat trial balance to <paramref name="baseCurrency"/>.
    /// FX rates are direct multipliers from local currency to base currency.
    /// </summary>
    public static LedgerCurrencyTranslation Translate(
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        string baseCurrency,
        IReadOnlyDictionary<string, decimal> fxRatesToBase,
        IReadOnlyDictionary<LedgerAccount, string>? accountCurrencies = null,
        IReadOnlyDictionary<LedgerAccount, decimal>? carryingBaseBalances = null)
    {
        ArgumentNullException.ThrowIfNull(trialBalance);
        ArgumentNullException.ThrowIfNull(fxRatesToBase);

        var normalizedBaseCurrency = NormalizeCurrency(baseCurrency, nameof(baseCurrency));
        var normalizedRates = NormalizeRates(fxRatesToBase, normalizedBaseCurrency);
        var exposures = new List<LedgerCurrencyExposure>(trialBalance.Count);

        foreach (var (account, localBalance) in trialBalance)
        {
            var localCurrency = ResolveCurrency(account, accountCurrencies, normalizedBaseCurrency);
            var rate = ResolveRate(localCurrency, normalizedBaseCurrency, normalizedRates);
            decimal? carryingBaseBalance = null;
            if (carryingBaseBalances is not null && carryingBaseBalances.TryGetValue(account, out var configuredCarryingBaseBalance))
                carryingBaseBalance = configuredCarryingBaseBalance;

            exposures.Add(new LedgerCurrencyExposure(
                account,
                localCurrency,
                normalizedBaseCurrency,
                localBalance,
                rate,
                localBalance * rate,
                carryingBaseBalance));
        }

        return new LedgerCurrencyTranslation(normalizedBaseCurrency, exposures);
    }

    /// <summary>
    /// Builds balanced unrealized FX revaluation journal lines for translated monetary asset and liability accounts.
    /// </summary>
    public static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> BuildUnrealizedFxRevaluationLines(
        LedgerCurrencyTranslation translation,
        string? financialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(translation);

        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>();
        var gainAccount = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.UnrealizedFxGain
            : LedgerAccounts.UnrealizedFxGainFor(financialAccountId);
        var lossAccount = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.UnrealizedFxLoss
            : LedgerAccounts.UnrealizedFxLossFor(financialAccountId);

        foreach (var exposure in translation.Exposures)
        {
            if (exposure.BaseCurrencyVariance is not { } variance || variance == 0m)
                continue;

            if (string.Equals(exposure.LocalCurrency, translation.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                continue;

            if (exposure.Account.AccountType is not (LedgerAccountType.Asset or LedgerAccountType.Liability))
            {
                throw new InvalidOperationException(
                    $"FX revaluation journal lines are supported only for asset and liability accounts; '{exposure.Account.Name}' is {exposure.Account.AccountType}.");
            }

            var amount = Math.Abs(variance);
            if (exposure.Account.AccountType == LedgerAccountType.Asset)
            {
                if (variance > 0m)
                {
                    lines.Add((exposure.Account, amount, 0m));
                    lines.Add((gainAccount, 0m, amount));
                }
                else
                {
                    lines.Add((lossAccount, amount, 0m));
                    lines.Add((exposure.Account, 0m, amount));
                }
            }
            else if (variance > 0m)
            {
                lines.Add((lossAccount, amount, 0m));
                lines.Add((exposure.Account, 0m, amount));
            }
            else
            {
                lines.Add((exposure.Account, amount, 0m));
                lines.Add((gainAccount, 0m, amount));
            }
        }

        return lines;
    }

    private static string ResolveCurrency(
        LedgerAccount account,
        IReadOnlyDictionary<LedgerAccount, string>? accountCurrencies,
        string baseCurrency)
    {
        if (accountCurrencies is not null && accountCurrencies.TryGetValue(account, out var configuredCurrency))
            return NormalizeCurrency(configuredCurrency, nameof(accountCurrencies));

        return IsIsoCurrencyCode(account.Symbol) ? account.Symbol!.ToUpperInvariant() : baseCurrency;
    }

    private static decimal ResolveRate(
        string localCurrency,
        string baseCurrency,
        IReadOnlyDictionary<string, decimal> fxRatesToBase)
    {
        if (string.Equals(localCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (!fxRatesToBase.TryGetValue(localCurrency, out var rate))
            throw new ArgumentException($"Missing FX rate from {localCurrency} to {baseCurrency}.", nameof(fxRatesToBase));

        return rate;
    }

    private static IReadOnlyDictionary<string, decimal> NormalizeRates(
        IReadOnlyDictionary<string, decimal> rates,
        string baseCurrency)
    {
        var normalized = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [baseCurrency] = 1m,
        };

        foreach (var (currency, rate) in rates)
        {
            var normalizedCurrency = NormalizeCurrency(currency, nameof(rates));
            if (rate <= 0m)
                throw new ArgumentOutOfRangeException(nameof(rates), "FX rates must be positive.");

            normalized[normalizedCurrency] = rate;
        }

        return normalized;
    }

    private static string NormalizeCurrency(string currency, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency, parameterName);
        var normalized = currency.Trim().ToUpperInvariant();
        if (!IsIsoCurrencyCode(normalized))
            throw new ArgumentException("Currency codes must be three alphabetic ISO-style characters.", parameterName);

        return normalized;
    }

    private static bool IsIsoCurrencyCode(string? currency)
        => currency is { Length: 3 } && currency.All(static c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
}
