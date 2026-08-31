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
        var parcels = SelectLots(input.QuantitySold, orderedLots, averageUnitCost);
        var proceeds = RoundCurrency(input.QuantitySold * input.SalePrice);
        var selections = BuildSelections(parcels, input.SalePrice, proceeds, input.SaleDate);
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

    /// <summary>
    /// One lot's share of the sale, priced but not yet attributed proceeds or holding-period
    /// character. Splitting parcel pricing from parcel attribution keeps the average-cost residual
    /// logic (which works on cost) separate from the proceeds residual logic (which works on price).
    /// </summary>
    private readonly record struct ReliefParcel(
        LedgerTaxLot Lot,
        decimal Quantity,
        decimal CostBasis,
        decimal UnitCost);

    private static IReadOnlyList<ReliefParcel> SelectLots(
        decimal quantitySold,
        IReadOnlyList<LedgerTaxLot> orderedLots,
        decimal? averageUnitCost)
    {
        var consumption = LotConsumption.Consume(orderedLots, quantitySold, static lot => lot.Quantity);

        if (!consumption.FullyConsumed)
            throw new InvalidOperationException($"Insufficient open tax-lot quantity to relieve {quantitySold}; remaining shortfall was {consumption.Shortfall}.");

        var slices = consumption.Slices;

        // Lot-discrete methods (FIFO/LIFO/HIFO/SpecificId) relieve each lot at its own recorded
        // unit cost; each slice's basis rounds independently because lots are not pooled.
        if (averageUnitCost is not { } pooledUnitCost)
        {
            return slices
                .Select(static slice => new ReliefParcel(
                    slice.Lot,
                    slice.Quantity,
                    RoundCurrency(slice.Quantity * slice.Lot.UnitCost),
                    slice.Lot.UnitCost))
                .ToList();
        }

        // Average cost pools every share at one unit cost. Round the total basis for the whole sold
        // quantity once and carry the rounding residual onto the final slice, so the per-slice bases
        // sum exactly to the rounded pooled basis (no cent drift across lots). Each slice reports the
        // unit cost implied by its rounded basis so the realized-gain export ties per row.
        var totalCostBasis = RoundCurrency(slices.Sum(static slice => slice.Quantity) * pooledUnitCost);
        var parcels = new List<ReliefParcel>(slices.Count);
        var allocated = 0m;

        for (var index = 0; index < slices.Count; index++)
        {
            var slice = slices[index];
            decimal costBasis;
            if (index == slices.Count - 1)
            {
                costBasis = totalCostBasis - allocated;
            }
            else
            {
                costBasis = RoundCurrency(slice.Quantity * pooledUnitCost);
                allocated += costBasis;
            }

            var reportedUnitCost = slice.Quantity != 0m ? costBasis / slice.Quantity : pooledUnitCost;
            parcels.Add(new ReliefParcel(slice.Lot, slice.Quantity, costBasis, reportedUnitCost));
        }

        return parcels;
    }

    /// <summary>
    /// Attributes proceeds and holding-period character to each priced parcel. Proceeds are
    /// allocated with the rounding residual on the final parcel so the per-parcel amounts sum
    /// exactly to <paramref name="totalProceeds"/>; a realized-gain export can then report row
    /// amounts that tie to the journal instead of re-deriving them and drifting a cent.
    /// </summary>
    private static IReadOnlyList<LedgerTaxLotReliefSelection> BuildSelections(
        IReadOnlyList<ReliefParcel> parcels,
        decimal salePrice,
        decimal totalProceeds,
        DateOnly saleDate)
    {
        var selections = new List<LedgerTaxLotReliefSelection>(parcels.Count);
        var allocatedProceeds = 0m;

        for (var index = 0; index < parcels.Count; index++)
        {
            var parcel = parcels[index];
            decimal parcelProceeds;
            if (index == parcels.Count - 1)
            {
                parcelProceeds = totalProceeds - allocatedProceeds;
            }
            else
            {
                parcelProceeds = RoundCurrency(parcel.Quantity * salePrice);
                allocatedProceeds += parcelProceeds;
            }

            // The holding period runs from the lot's effective start, which an earlier wash sale may
            // have moved before the lot was actually acquired (IRC §1223(3)).
            var holdingPeriodStart = parcel.Lot.HoldingPeriodStart;
            selections.Add(new LedgerTaxLotReliefSelection(
                parcel.Lot,
                parcel.Quantity,
                parcel.CostBasis,
                parcel.UnitCost,
                parcelProceeds,
                parcelProceeds - parcel.CostBasis,
                TaxCharacterRule.Classify(holdingPeriodStart, saleDate),
                TaxCharacterRule.HoldingPeriodDays(holdingPeriodStart, saleDate),
                parcel.Lot.HoldingPeriodStartDate is not null));
        }

        return selections;
    }

    /// <summary>
    /// Applies the account's <see cref="WashSalePolicy"/> to a realized loss: when a
    /// substantially-identical security is acquired within the policy window, the proportional
    /// share of the loss is disallowed and carried into the replacement lots' basis. Returns
    /// <c>null</c> when no wash sale applies (a gain, disabled policy, or no matching replacement).
    /// <para>
    /// Scope: deferral is computed from the sale's aggregate net realized loss. A single relief that
    /// spans both gain and loss lots (a net gain that nonetheless contains loss shares) is not yet
    /// decomposed to defer only the loss shares; per-loss-lot matching within a mixed gain/loss sale
    /// is a documented follow-up.
    /// </para>
    /// </summary>
    private static WashSaleOutcome? ComputeWashSale(
        LedgerTaxLotReliefInput input,
        IReadOnlyList<LedgerTaxLotReliefSelection> selections,
        decimal realizedGainOrLoss)
    {
        // AppliesOn (not Enabled) gates the policy so a dated activation leaves sales before its
        // effective date reporting exactly the numbers they were originally closed with.
        if (!input.WashSalePolicy.AppliesOn(input.SaleDate) || realizedGainOrLoss >= 0m || input.ReplacementAcquisitions.Count == 0)
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
        // The earliest relieved holding-period start carries the sold shares' holding period onto
        // the replacement lot (IRC §1223(3)). Using the effective start rather than the raw
        // acquisition date is what lets deferrals chain: a replacement lot that already absorbed an
        // earlier wash sale passes that older start along instead of resetting it to its own
        // purchase date. Min over DayNumber keeps the result an unambiguous DateOnly.
        var holdingPeriodCarryDate = DateOnly.FromDayNumber(
            selections.Min(static selection => selection.Lot.HoldingPeriodStart.DayNumber));
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
        // The replacement's account travels with it so a deferral can be traced to the exact account
        // that absorbed it — which is the whole point of a book-wide replacement scope, where the
        // absorbing account is frequently not the one that sold.
        var consumedByLot = new List<(string LotId, decimal Quantity, LedgerAccount? Account)>();
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
            {
                consumedByLot[existing] = (
                    replacement.LotId,
                    consumedByLot[existing].Quantity + consumed,
                    consumedByLot[existing].Account ?? replacement.Account);
            }
            else
            {
                consumedByLot.Add((replacement.LotId, consumed, replacement.Account));
            }
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
            {
                increases.Add(new WashSaleBasisIncrease(
                    consumedByLot[index].LotId,
                    amount,
                    holdingPeriodCarryDate,
                    consumedByLot[index].Account));
            }
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
