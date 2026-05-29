namespace Meridian.Ledger;

/// <summary>
/// Converts local-currency journal lines into balanced base-currency ledger posting lines.
/// </summary>
public static class MultiCurrencyJournalProjector
{
    public static MultiCurrencyJournalProjection Project(MultiCurrencyJournalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = input.Lines
            .Select(line => new MultiCurrencyJournalLineProjection(
                line.Account,
                line.LocalCurrency,
                input.BaseCurrency,
                line.LocalDebit,
                line.LocalCredit,
                line.FxRateToBase,
                RoundCurrency(line.LocalDebit * line.FxRateToBase),
                RoundCurrency(line.LocalCredit * line.FxRateToBase)))
            .ToList();
        var projection = new MultiCurrencyJournalProjection(input, lines);

        if (!projection.IsBaseBalanced)
        {
            throw new LedgerValidationException(
                $"Multi-currency journal entry is not balanced in base currency {input.BaseCurrency}: " +
                $"debits={projection.TotalBaseDebits}, credits={projection.TotalBaseCredits}.");
        }

        return projection;
    }

    private static decimal RoundCurrency(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
