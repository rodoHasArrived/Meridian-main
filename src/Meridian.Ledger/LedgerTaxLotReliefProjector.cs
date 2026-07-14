using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Applies account-level tax-lot relief policy to produce realized gain/loss journal lines.
/// </summary>
public static class LedgerTaxLotReliefProjector
{
    public static LedgerTaxLotReliefProjection Project(LedgerTaxLotReliefInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Feed reference-data (day-count amortization, factor, corporate-action) basis
        // adjustments into the relief engine before ordering, so cost-basis relief reflects the
        // effective lots rather than their raw recorded quantity and unit cost.
        var effectiveLots = input.BasisAdjustments.Count == 0
            ? input.OpenLots
            : LedgerTaxLotBasisAdjuster.Apply(input.OpenLots, input.BasisAdjustments);

        var orderedLots = OrderLots(input, effectiveLots).ToList();
        var averageUnitCost = ResolveAverageUnitCost(input.ReliefMethod, effectiveLots);
        var selections = SelectLots(input.QuantitySold, orderedLots, averageUnitCost);
        var proceeds = RoundCurrency(input.QuantitySold * input.SalePrice);
        var costBasis = selections.Sum(static selection => selection.CostBasis);
        var realizedGainOrLoss = proceeds - costBasis;
        var washSale = ComputeWashSale(input, selections, realizedGainOrLoss);
        var disallowedLoss = washSale?.DisallowedLoss ?? 0m;
        var lines = BuildLines(input, proceeds, costBasis, realizedGainOrLoss, disallowedLoss);

        return new LedgerTaxLotReliefProjection(input, selections, proceeds, costBasis, realizedGainOrLoss, lines)
        {
            AppliedAdjustments = input.BasisAdjustments,
            EffectiveLots = effectiveLots,
            WashSale = washSale,
        };
    }

    /// <summary>
    /// Computes the pooled average unit cost for <see cref="LedgerTaxLotReliefMethod.AverageCost"/>,
    /// or <c>null</c> for lot-discrete methods (which value each slice at its own lot's unit cost).
    /// </summary>
    private static decimal? ResolveAverageUnitCost(
        LedgerTaxLotReliefMethod method,
        IReadOnlyList<LedgerTaxLot> effectiveLots)
    {
        if (method != LedgerTaxLotReliefMethod.AverageCost)
            return null;

        var totalQuantity = effectiveLots.Sum(static lot => lot.Quantity);
        if (totalQuantity <= 0m)
            return null; // no relievable quantity; SelectLots surfaces the shortfall consistently.

        var totalCost = effectiveLots.Sum(static lot => lot.Quantity * lot.UnitCost);
        return totalCost / totalQuantity;
    }

    private static IEnumerable<LedgerTaxLot> OrderLots(LedgerTaxLotReliefInput input, IReadOnlyList<LedgerTaxLot> effectiveLots)
    {
        return input.ReliefMethod switch
        {
            LedgerTaxLotReliefMethod.Fifo => effectiveLots
                .OrderBy(static lot => lot.AcquiredDate)
                .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase),
            LedgerTaxLotReliefMethod.Lifo => effectiveLots
                .OrderByDescending(static lot => lot.AcquiredDate)
                .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase),
            LedgerTaxLotReliefMethod.Hifo => effectiveLots
                .OrderByDescending(static lot => lot.UnitCost)
                .ThenBy(static lot => lot.AcquiredDate)
                .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase),
            LedgerTaxLotReliefMethod.SpecificId => OrderSpecificLots(input, effectiveLots),
            // Average cost pools every lot into a single average unit cost (see ResolveAverageUnitCost),
            // but lots are still depleted oldest-first so lot-closing and holding periods stay deterministic.
            LedgerTaxLotReliefMethod.AverageCost => effectiveLots
                .OrderBy(static lot => lot.AcquiredDate)
                .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.ReliefMethod, "Unsupported tax-lot relief method."),
        };
    }

    private static IEnumerable<LedgerTaxLot> OrderSpecificLots(LedgerTaxLotReliefInput input, IReadOnlyList<LedgerTaxLot> effectiveLots)
    {
        if (input.SpecificLotIds.Count == 0)
            throw new ArgumentException("SpecificId relief requires at least one selected lot identifier.", nameof(input));

        var openLots = effectiveLots.ToDictionary(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase);
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
        IReadOnlyList<LedgerTaxLot> orderedLots,
        decimal? averageUnitCost)
    {
        var consumption = LotConsumption.Consume(orderedLots, quantitySold, static lot => lot.Quantity);

        if (!consumption.FullyConsumed)
            throw new InvalidOperationException($"Insufficient open tax-lot quantity to relieve {quantitySold}; remaining shortfall was {consumption.Shortfall}.");

        return consumption.Slices
            .Select(slice =>
            {
                // Average cost relieves every share at the pooled unit cost; lot-discrete methods
                // relieve at the lot's own cost.
                var relievedUnitCost = averageUnitCost ?? slice.Lot.UnitCost;
                var costBasis = RoundCurrency(slice.Quantity * relievedUnitCost);
                // For average cost, report the unit cost implied by the rounded pooled basis so the
                // realized-gain export ties (QuantityRelieved * UnitCost == CostBasis); discrete
                // methods report the lot's own recorded unit cost.
                var reportedUnitCost = averageUnitCost is not null && slice.Quantity != 0m
                    ? costBasis / slice.Quantity
                    : relievedUnitCost;
                return new LedgerTaxLotReliefSelection(slice.Lot, slice.Quantity, costBasis, reportedUnitCost);
            })
            .ToList();
    }

    /// <summary>
    /// Applies the account's <see cref="WashSalePolicy"/> to a realized loss: when a
    /// substantially-identical security is acquired within the policy window, the proportional
    /// share of the loss is disallowed and carried into the replacement lots' basis. Returns
    /// <c>null</c> when no wash sale applies (a gain, disabled policy, or no matching replacement).
    /// </summary>
    private static WashSaleOutcome? ComputeWashSale(
        LedgerTaxLotReliefInput input,
        IReadOnlyList<LedgerTaxLotReliefSelection> selections,
        decimal realizedGainOrLoss)
    {
        if (!input.WashSalePolicy.Enabled || realizedGainOrLoss >= 0m || input.ReplacementAcquisitions.Count == 0)
            return null;

        var soldSecurityId = ResolveSoldSecurityId(selections);
        var window = input.WashSalePolicy.WindowDays;
        var lower = input.SaleDate.AddDays(-window);
        var upper = input.SaleDate.AddDays(window);

        var matching = input.ReplacementAcquisitions
            .Where(replacement => replacement.Quantity > 0m
                && replacement.AcquiredDate >= lower
                && replacement.AcquiredDate <= upper
                && SecurityMatches(soldSecurityId, replacement.SecurityId))
            .ToList();
        if (matching.Count == 0)
            return null;

        var matchedQuantity = Math.Min(input.QuantitySold, matching.Sum(static replacement => replacement.Quantity));
        if (matchedQuantity <= 0m)
            return null;

        var totalLoss = Math.Abs(realizedGainOrLoss);
        var disallowedLoss = RoundCurrency(totalLoss * (matchedQuantity / input.QuantitySold));
        if (disallowedLoss <= 0m)
            return null;

        // Never disallow more than the recognized loss after rounding.
        disallowedLoss = Math.Min(disallowedLoss, totalLoss);
        var allowedLoss = totalLoss - disallowedLoss;
        // Earliest relieved acquisition date carries the sold shares' holding period onto the
        // replacement lot. Min over DayNumber keeps the result an unambiguous DateOnly.
        var holdingPeriodCarryDate = DateOnly.FromDayNumber(
            selections.Min(static selection => selection.Lot.AcquiredDate.DayNumber));
        var basisIncreases = DistributeBasisIncreases(matching, matchedQuantity, disallowedLoss, holdingPeriodCarryDate);

        return new WashSaleOutcome(disallowedLoss, allowedLoss, matchedQuantity, basisIncreases);
    }

    private static Guid? ResolveSoldSecurityId(IReadOnlyList<LedgerTaxLotReliefSelection> selections)
    {
        var securityIds = selections
            .Select(static selection => selection.Lot.SecurityId)
            .Where(static securityId => securityId is not null)
            .Distinct()
            .ToList();

        // A single, unambiguous security identity is used for matching; mixed or absent identities
        // fall back to trusting the caller-supplied replacement scope.
        return securityIds.Count == 1 ? securityIds[0] : null;
    }

    private static bool SecurityMatches(Guid? soldSecurityId, Guid? replacementSecurityId)
    {
        if (soldSecurityId is null || replacementSecurityId is null)
            return true; // caller-scoped: accept the replacement in the security's own relief context.
        return soldSecurityId.Value == replacementSecurityId.Value;
    }

    /// <summary>
    /// Distributes the disallowed loss across the replacement shares that actually absorb it. Only
    /// <paramref name="matchedQuantity"/> replacement shares carry the deferred loss (a wash sale
    /// matches share-for-share), so replacement lots are consumed in acquisition order until that
    /// quantity is filled — any lots beyond it are untouched. Within the matched shares the loss is
    /// weighted by each lot's consumed quantity, with the rounding residual on the final matched lot
    /// and every increase capped at the remaining balance so none can go negative.
    /// </summary>
    private static IReadOnlyList<WashSaleBasisIncrease> DistributeBasisIncreases(
        IReadOnlyList<WashSaleReplacementAcquisition> matching,
        decimal matchedQuantity,
        decimal disallowedLoss,
        DateOnly holdingPeriodCarryDate)
    {
        // Consume replacement shares oldest-first up to the matched quantity, aggregating per lot.
        var consumedByLot = new List<(string LotId, decimal Quantity)>();
        var remainingToMatch = matchedQuantity;
        foreach (var replacement in matching
            .OrderBy(static replacement => replacement.AcquiredDate)
            .ThenBy(static replacement => replacement.LotId, StringComparer.OrdinalIgnoreCase))
        {
            if (remainingToMatch <= 0m)
                break;

            var consumed = Math.Min(remainingToMatch, replacement.Quantity);
            remainingToMatch -= consumed;

            var existing = consumedByLot.FindIndex(lot => string.Equals(lot.LotId, replacement.LotId, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                consumedByLot[existing] = (replacement.LotId, consumedByLot[existing].Quantity + consumed);
            else
                consumedByLot.Add((replacement.LotId, consumed));
        }

        var totalConsumed = consumedByLot.Sum(static lot => lot.Quantity);
        var increases = new List<WashSaleBasisIncrease>(consumedByLot.Count);
        var allocated = 0m;

        for (var index = 0; index < consumedByLot.Count; index++)
        {
            // Cap each lot at the remaining unallocated loss so accumulated rounding on earlier
            // lots can never push a later lot's basis increase negative; the final lot absorbs
            // whatever residual is left. Basis increases are non-negative and sum exactly.
            var remaining = disallowedLoss - allocated;
            var amount = index == consumedByLot.Count - 1
                ? remaining
                : Math.Min(remaining, RoundCurrency(disallowedLoss * (consumedByLot[index].Quantity / totalConsumed)));
            allocated += amount;
            if (amount != 0m)
                increases.Add(new WashSaleBasisIncrease(consumedByLot[index].LotId, amount, holdingPeriodCarryDate));
        }

        return increases;
    }

    private static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> BuildLines(
        LedgerTaxLotReliefInput input,
        decimal proceeds,
        decimal costBasis,
        decimal realizedGainOrLoss,
        decimal disallowedLoss)
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
            // Only the allowed portion of the loss is recognized; the disallowed (wash-sale)
            // portion is capitalized back into the replacement lot's basis, which nets against the
            // position credit so the entry still balances and no premature loss is booked.
            var allowedLoss = Math.Abs(realizedGainOrLoss) - disallowedLoss;
            if (allowedLoss > 0m)
                lines.Add((loss, allowedLoss, 0m));
            lines.Add((input.Account, 0m, costBasis - disallowedLoss));
        }

        // Drop any degenerate zero/zero lines (e.g. a fully-deferred wash sale at a zero sale
        // price) so the projection is always materializable into valid ledger entries.
        return lines.Where(static line => line.debit != 0m || line.credit != 0m).ToList();
    }
}
