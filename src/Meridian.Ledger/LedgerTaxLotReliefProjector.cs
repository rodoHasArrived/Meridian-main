namespace Meridian.Ledger;

/// <summary>
/// Applies account-level tax-lot relief policy to produce realized gain/loss journal lines.
/// </summary>
public static class LedgerTaxLotReliefProjector
{
    public static LedgerTaxLotReliefProjection Project(LedgerTaxLotReliefInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var orderedLots = OrderLots(input).ToList();
        var selections = SelectLots(input.QuantitySold, orderedLots);
        var proceeds = RoundCurrency(input.QuantitySold * input.SalePrice);
        var costBasis = selections.Sum(static selection => selection.CostBasis);
        var realizedGainOrLoss = proceeds - costBasis;
        var lines = BuildLines(input, proceeds, costBasis, realizedGainOrLoss);

        return new LedgerTaxLotReliefProjection(input, selections, proceeds, costBasis, realizedGainOrLoss, lines);
    }

    private static IEnumerable<LedgerTaxLot> OrderLots(LedgerTaxLotReliefInput input)
    {
        return input.ReliefMethod switch
        {
            LedgerTaxLotReliefMethod.Fifo => input.OpenLots
                .OrderBy(static lot => lot.AcquiredDate)
                .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase),
            LedgerTaxLotReliefMethod.Lifo => input.OpenLots
                .OrderByDescending(static lot => lot.AcquiredDate)
                .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase),
            LedgerTaxLotReliefMethod.Hifo => input.OpenLots
                .OrderByDescending(static lot => lot.UnitCost)
                .ThenBy(static lot => lot.AcquiredDate)
                .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase),
            LedgerTaxLotReliefMethod.SpecificId => OrderSpecificLots(input),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.ReliefMethod, "Unsupported tax-lot relief method."),
        };
    }

    private static IEnumerable<LedgerTaxLot> OrderSpecificLots(LedgerTaxLotReliefInput input)
    {
        if (input.SpecificLotIds.Count == 0)
            throw new ArgumentException("SpecificId relief requires at least one selected lot identifier.", nameof(input));

        var openLots = input.OpenLots.ToDictionary(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lotId in input.SpecificLotIds)
        {
            if (!seen.Add(lotId))
                throw new ArgumentException($"Specific lot '{lotId}' was selected more than once.", nameof(input));
            if (!openLots.TryGetValue(lotId, out var lot))
                throw new ArgumentException($"Specific lot '{lotId}' is not open for account '{input.Account}'.", nameof(input));

            yield return lot;
        }
    }

    private static IReadOnlyList<LedgerTaxLotReliefSelection> SelectLots(
        decimal quantitySold,
        IReadOnlyList<LedgerTaxLot> orderedLots)
    {
        var remaining = quantitySold;
        var selections = new List<LedgerTaxLotReliefSelection>();

        foreach (var lot in orderedLots)
        {
            if (remaining <= 0m)
                break;

            var relievedQuantity = Math.Min(remaining, lot.Quantity);
            selections.Add(new LedgerTaxLotReliefSelection(
                lot,
                relievedQuantity,
                RoundCurrency(relievedQuantity * lot.UnitCost)));
            remaining -= relievedQuantity;
        }

        if (remaining > 0m)
            throw new InvalidOperationException($"Insufficient open tax-lot quantity to relieve {quantitySold}; remaining shortfall was {remaining}.");

        return selections;
    }

    private static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> BuildLines(
        LedgerTaxLotReliefInput input,
        decimal proceeds,
        decimal costBasis,
        decimal realizedGainOrLoss)
    {
        var financialAccountId = input.FinancialAccountId;
        var cash = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.Cash
            : LedgerAccounts.CashAccount(financialAccountId);
        var gain = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.RealizedGain
            : LedgerAccounts.RealizedGainFor(financialAccountId);
        var loss = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.RealizedLoss
            : LedgerAccounts.RealizedLossFor(financialAccountId);

        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>
        {
            (cash, proceeds, 0m),
        };

        if (realizedGainOrLoss >= 0m)
        {
            lines.Add((input.Account, 0m, costBasis));
            if (realizedGainOrLoss > 0m)
                lines.Add((gain, 0m, realizedGainOrLoss));
        }
        else
        {
            lines.Add((loss, Math.Abs(realizedGainOrLoss), 0m));
            lines.Add((input.Account, 0m, costBasis));
        }

        return lines;
    }

    private static decimal RoundCurrency(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
