using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Projects daily position marks into audited valuation rows and balanced fair-value adjustments.
/// </summary>
public static class DailyPortfolioPricingProjector
{
    public static DailyPortfolioPricingProjection Project(DailyPortfolioPricingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = input.Marks
            .Select(mark => BuildLine(input, mark))
            .ToList();
        var journalLines = BuildJournalLines(lines);

        return new DailyPortfolioPricingProjection(input, lines, journalLines);
    }

    private static DailyPortfolioPricingLine BuildLine(DailyPortfolioPricingInput input, DailyPortfolioPriceMark mark)
    {
        var costBasis = RoundCurrency(mark.Quantity * mark.CostPrice);
        var marketValue = RoundCurrency(mark.Quantity * mark.MarkPrice);
        var unrealizedGainOrLoss = marketValue - costBasis;

        return new DailyPortfolioPricingLine(
            mark.Symbol,
            mark.Quantity,
            mark.CostPrice,
            mark.MarkPrice,
            costBasis,
            marketValue,
            unrealizedGainOrLoss,
            mark.PriceSource,
            mark.EvidenceReference,
            input.Policy.PolicyId,
            input.Policy.ValuationMethod,
            mark.FinancialAccountId,
            mark.InstrumentType,
            mark.PriceObservedOn,
            mark.Confidence);
    }

    private static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> BuildJournalLines(
        IReadOnlyList<DailyPortfolioPricingLine> lines)
    {
        var journalLines = new List<(LedgerAccount account, decimal debit, decimal credit)>();

        foreach (var line in lines)
        {
            if (line.UnrealizedGainOrLoss == 0m)
                continue;

            var securitiesAccount = LedgerAccounts.Securities(line.Symbol, line.FinancialAccountId);
            var scope = line.FinancialAccountId ?? line.Symbol;

            if (line.UnrealizedGainOrLoss > 0m)
            {
                journalLines.Add((securitiesAccount, line.UnrealizedGainOrLoss, 0m));
                journalLines.Add((LedgerAccounts.UnrealizedGainFor(scope), 0m, line.UnrealizedGainOrLoss));
            }
            else
            {
                var loss = Math.Abs(line.UnrealizedGainOrLoss);
                journalLines.Add((LedgerAccounts.UnrealizedLossFor(scope), loss, 0m));
                journalLines.Add((securitiesAccount, 0m, loss));
            }
        }

        return journalLines;
    }
}
