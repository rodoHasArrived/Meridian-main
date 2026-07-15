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
            .OrderBy(static line => line.SecurityId)
            .ThenBy(static line => line.Symbol, StringComparer.Ordinal)
            .ThenBy(static line => line.FinancialAccountId, StringComparer.Ordinal)
            .ThenBy(static line => line.EvidenceReference, StringComparer.Ordinal)
            .ToList();
        var journalLines = BuildJournalLines(input, lines);

        return new DailyPortfolioPricingProjection(input, lines, journalLines);
    }

    private static DailyPortfolioPricingLine BuildLine(DailyPortfolioPricingInput input, DailyPortfolioPriceMark mark)
    {
        var costBasis = RoundCurrency(mark.Quantity * mark.CostPrice);
        var marketValue = RoundCurrency(mark.Quantity * mark.MarkPrice);
        var unrealizedGainOrLoss = marketValue - costBasis;
        var hasPriorCarryingValue = mark.PriorCarryingValue.HasValue;
        var priorCarryingValue = RoundCurrency(mark.PriorCarryingValue ?? costBasis);
        var markAdjustment = marketValue - priorCarryingValue;

        return new DailyPortfolioPricingLine(
            mark.Symbol,
            mark.Quantity,
            mark.CostPrice,
            mark.MarkPrice,
            costBasis,
            marketValue,
            unrealizedGainOrLoss,
            priorCarryingValue,
            hasPriorCarryingValue,
            markAdjustment,
            mark.PriceSource,
            mark.EvidenceReference,
            input.Policy.PolicyId,
            input.Policy.ValuationMethod,
            mark.FinancialAccountId,
            mark.InstrumentType,
            FairValueLevel: mark.FairValueLevel == FairValueLevel.Unclassified
                ? input.Policy.DefaultFairValueLevel
                : mark.FairValueLevel,
            IsStalePriced: mark.IsStalePriced,
            PriceObservedOn: mark.PriceObservedOn,
            Confidence: mark.Confidence,
            SecurityId: mark.SecurityId,
            CarryingValueSource: mark.CarryingValueSource,
            CarryingValueCapturedAtUtc: mark.CarryingValueCapturedAtUtc,
            CarryingValueEvidenceReference: mark.CarryingValueEvidenceReference);
    }

    private static IReadOnlyList<DailyPortfolioPricingJournalLine> BuildJournalLines(
        DailyPortfolioPricingInput input,
        IReadOnlyList<DailyPortfolioPricingLine> lines)
    {
        var journalLines = new List<DailyPortfolioPricingJournalLine>();

        foreach (var line in lines)
        {
            if (line.MarkAdjustment == 0m)
                continue;

            var securitiesAccount = LedgerAccounts.Securities(line.Symbol, line.FinancialAccountId);
            var scope = line.FinancialAccountId ?? line.Symbol;
            var dimensions = new LedgerLineDimensionSet(
                FundId: input.Policy.FundId,
                InstrumentId: line.SecurityId,
                AccountId: line.FinancialAccountId);

            if (line.MarkAdjustment > 0m)
            {
                journalLines.Add(new DailyPortfolioPricingJournalLine(line, securitiesAccount, line.MarkAdjustment, 0m, dimensions));
                journalLines.Add(new DailyPortfolioPricingJournalLine(line, LedgerAccounts.UnrealizedGainFor(scope), 0m, line.MarkAdjustment, dimensions));
            }
            else
            {
                var loss = Math.Abs(line.MarkAdjustment);
                journalLines.Add(new DailyPortfolioPricingJournalLine(line, LedgerAccounts.UnrealizedLossFor(scope), loss, 0m, dimensions));
                journalLines.Add(new DailyPortfolioPricingJournalLine(line, securitiesAccount, 0m, loss, dimensions));
            }
        }

        return journalLines;
    }
}
