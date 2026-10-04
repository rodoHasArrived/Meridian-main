using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Applies account-level tax-lot relief policy to produce realized gain/loss journal lines.
/// </summary>
public static class LedgerTaxLotReliefProjector
{
    /// <summary>Retained convention for the current sign-preserving proceeds allocator.</summary>
    public const int CurrentProceedsAllocationVersion = 1;

    public static LedgerTaxLotReliefProjection Project(LedgerTaxLotReliefInput input)
        => Project(input, retainedLegacyProceeds: false);

    /// <summary>
    /// Replays the convention retained with a disposal. Unversioned history keeps the original
    /// final-residual allocator even when today's sign-preserving allocator would also succeed.
    /// This compatibility entry point is only for history; new projections use <see cref="Project(LedgerTaxLotReliefInput)"/>.
    /// </summary>
    internal static LedgerTaxLotReliefProjection ReconstructRetainedDisposal(
        LedgerTaxLotReliefInput input, int? proceedsAllocationVersion)
        => proceedsAllocationVersion switch
        {
            null => Project(input, retainedLegacyProceeds: true),
            CurrentProceedsAllocationVersion => Project(input, retainedLegacyProceeds: false),
            _ => throw new ArgumentOutOfRangeException(nameof(proceedsAllocationVersion),
                proceedsAllocationVersion, "Unknown retained tax-lot proceeds allocation version."),
        };

    private static LedgerTaxLotReliefProjection Project(LedgerTaxLotReliefInput input, bool retainedLegacyProceeds)
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
        var selections = BuildSelections(parcels, input.SalePrice, proceeds, input.SaleDate,
            pooled: input.ReliefMethod == LedgerTaxLotReliefMethod.AverageCost, retainedLegacyProceeds);
        var costBasis = selections.Sum(static selection => selection.CostBasis);
        var realizedGainOrLoss = proceeds - costBasis;
        var washSale = ComputeWashSale(input, selections);
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
                costBasis = Math.Min(totalCostBasis - allocated, RoundCurrency(slice.Quantity * pooledUnitCost));
                allocated += costBasis;
            }

            var reportedUnitCost = slice.Quantity != 0m ? costBasis / slice.Quantity : pooledUnitCost;
            parcels.Add(new ReliefParcel(slice.Lot, slice.Quantity, costBasis, reportedUnitCost));
        }

        return parcels;
    }

    /// <summary>
    /// Attributes proceeds and holding-period character to each priced parcel. Proceeds are
    /// allocated with sign-preserving rounding residuals so the per-parcel amounts sum
    /// exactly to <paramref name="totalProceeds"/>; a realized-gain export can then report row
    /// amounts that tie to the journal instead of re-deriving them and drifting a cent.
    /// </summary>
    private static IReadOnlyList<LedgerTaxLotReliefSelection> BuildSelections(
        IReadOnlyList<ReliefParcel> parcels,
        decimal salePrice,
        decimal totalProceeds,
        DateOnly saleDate,
        bool pooled,
        bool retainedLegacyProceeds)
    {
        var selections = new List<LedgerTaxLotReliefSelection>(parcels.Count);
        var allocatedProceeds = pooled
            ? AllocatePooledProceeds(parcels, totalProceeds)
            : retainedLegacyProceeds
                ? AllocateLegacyDiscreteProceeds(parcels, salePrice, totalProceeds)
                : AllocateDiscreteProceeds(parcels, salePrice, totalProceeds);

        for (var index = 0; index < parcels.Count; index++)
        {
            var parcel = parcels[index];
            var parcelProceeds = allocatedProceeds[index];

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

    private static decimal[] AllocateLegacyDiscreteProceeds(
        IReadOnlyList<ReliefParcel> parcels, decimal salePrice, decimal totalProceeds)
    {
        // Frozen unversioned history convention: independently round each parcel, cap it at
        // remaining proceeds, and put the final residual on the last parcel. Do not apply the
        // new sign bounds retroactively or silently move results between historical tax lots.
        var proceeds = new decimal[parcels.Count];
        var remaining = totalProceeds;
        for (var index = 0; index < parcels.Count; index++)
        {
            proceeds[index] = index == parcels.Count - 1
                ? remaining
                : Math.Min(remaining, RoundCurrency(parcels[index].Quantity * salePrice));
            remaining -= proceeds[index];
        }
        return proceeds;
    }

    private static decimal[] AllocateDiscreteProceeds(
        IReadOnlyList<ReliefParcel> parcels, decimal salePrice, decimal totalProceeds)
    {
        // Reserve each future gain/break-even parcel's rounded basis before consuming proceeds.
        // Loss parcels may receive at most their basis; rounding may erase a sub-cent result,
        // but it must never reverse the sign of the parcel's unrounded economic result.
        var minimums = parcels.Select(parcel => salePrice >= parcel.UnitCost ? parcel.CostBasis : 0m).ToArray();
        var maximums = parcels.Select(parcel => salePrice <= parcel.UnitCost ? parcel.CostBasis : totalProceeds).ToArray();
        var remainingMinimum = minimums.Sum();
        var remainingMaximum = maximums.Sum();
        if (totalProceeds < remainingMinimum || totalProceeds > remainingMaximum)
            throw new InvalidOperationException("Rounded proceeds and lot bases cannot preserve every parcel's economic result sign.");

        var proceeds = new decimal[parcels.Count];
        var remaining = totalProceeds;
        for (var index = 0; index < parcels.Count; index++)
        {
            remainingMinimum -= minimums[index];
            remainingMaximum -= maximums[index];
            var lower = Math.Max(minimums[index], remaining - remainingMaximum);
            var upper = Math.Min(maximums[index], remaining - remainingMinimum);
            proceeds[index] = Math.Clamp(RoundCurrency(parcels[index].Quantity * salePrice), lower, upper);
            remaining -= proceeds[index];
        }
        return proceeds;
    }

    private static decimal[] AllocatePooledProceeds(IReadOnlyList<ReliefParcel> parcels, decimal totalProceeds)
    {
        // Independent basis/proceeds residuals can invent loss parcels inside a gain-producing pool.
        // Preserve the rounded basis and allocate the pool's result with the same sign to each parcel.
        var remainingBasis = parcels.Sum(static parcel => parcel.CostBasis);
        var result = totalProceeds - remainingBasis;
        var magnitude = Math.Abs(result);
        var remaining = magnitude;
        var quantity = parcels.Sum(static parcel => parcel.Quantity);
        var proceeds = new decimal[parcels.Count];
        for (var index = 0; index < parcels.Count; index++)
        {
            var parcel = parcels[index];
            remainingBasis -= parcel.CostBasis;
            var amount = index == parcels.Count - 1
                ? remaining
                : Math.Min(remaining, RoundCurrency(magnitude * (parcel.Quantity / quantity)));
            if (result < 0m)
            {
                // A parcel cannot lose more than its basis. The floor leaves enough future basis
                // to absorb the residual, including a zero-proceeds sale of every pooled parcel.
                amount = Math.Clamp(amount, Math.Max(0m, remaining - remainingBasis), Math.Min(remaining, parcel.CostBasis));
            }
            remaining -= amount;
            proceeds[index] = parcel.CostBasis + (result < 0m ? -amount : amount);
        }
        return proceeds;
    }

    /// <summary>
    /// Matches each loss parcel in relief order against a shared pool of replacement shares.
    /// Gain parcels do not consume replacements or offset the losses eligible for deferral.
    /// </summary>
    private static WashSaleOutcome? ComputeWashSale(
        LedgerTaxLotReliefInput input,
        IReadOnlyList<LedgerTaxLotReliefSelection> selections)
    {
        if (!input.WashSalePolicy.AppliesOn(input.SaleDate) || input.ReplacementAcquisitions.Count == 0)
            return null;

        var lossSelections = selections.Where(static selection => selection.RealizedGainOrLoss < 0m).ToList();
        if (lossSelections.Count == 0)
            return null;

        var scopedSecurityId = ResolveSoldSecurityId(selections);
        var replacements = SelectReplacements(input, selections, scopedSecurityId);
        var available = replacements.Select(static replacement => replacement.Quantity).ToArray();
        var sources = replacements.Select(static _ => new List<WashSaleSourceAllocation>()).ToArray();
        var disallowedTotal = 0m;
        var matchedTotal = 0m;

        foreach (var selection in lossSelections)
        {
            var remainingQuantity = selection.QuantityRelieved;
            var matches = new List<(int Index, decimal Quantity)>();
            for (var index = 0; index < replacements.Count && remainingQuantity > 0m; index++)
            {
                if (available[index] <= 0m || !SecurityMatches(selection.Lot.SecurityId ?? scopedSecurityId, replacements[index].SecurityId))
                    continue;

                var quantity = Math.Min(remainingQuantity, available[index]);
                available[index] -= quantity;
                remainingQuantity -= quantity;
                matches.Add((index, quantity));
            }

            var matchedQuantity = selection.QuantityRelieved - remainingQuantity;
            if (matchedQuantity == 0m)
                continue;

            var loss = -selection.RealizedGainOrLoss;
            var disallowed = Math.Min(loss, RoundCurrency(loss * (matchedQuantity / selection.QuantityRelieved)));
            var allocated = 0m;
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                // Round once per source loss and put its residual on the last matched replacement.
                // Keep zero-amount matches as quantity evidence: those shares are still consumed.
                var amount = index == matches.Count - 1
                    ? disallowed - allocated
                    : Math.Min(disallowed - allocated, RoundCurrency(disallowed * (match.Quantity / matchedQuantity)));
                allocated += amount;
                sources[match.Index].Add(new WashSaleSourceAllocation(selection, match.Quantity, amount));
            }

            disallowedTotal += disallowed;
            matchedTotal += matchedQuantity;
        }

        if (matchedTotal == 0m)
            return null;

        var increases = new List<WashSaleBasisIncrease>();
        for (var index = 0; index < replacements.Count; index++)
        {
            if (sources[index].Count == 0)
                continue;

            var replacement = replacements[index];
            increases.Add(new WashSaleBasisIncrease(
                replacement.LotId,
                sources[index].Sum(static source => source.Amount),
                DateOnly.FromDayNumber(sources[index].Min(static source => source.Source.Lot.HoldingPeriodStart.DayNumber)),
                replacement.Account)
            {
                AppliedPolicy = input.WashSalePolicy,
                SourceAllocations = sources[index].ToArray(),
            });
        }

        return new WashSaleOutcome(
            disallowedTotal,
            lossSelections.Sum(static selection => -selection.RealizedGainOrLoss) - disallowedTotal,
            matchedTotal,
            increases);
    }

    private static IReadOnlyList<WashSaleReplacementAcquisition> SelectReplacements(
        LedgerTaxLotReliefInput input,
        IReadOnlyList<LedgerTaxLotReliefSelection> selections,
        Guid? scopedSecurityId)
    {
        var lower = input.SaleDate.AddDays(-input.WashSalePolicy.WindowDays);
        var upper = input.SaleDate.AddDays(input.WashSalePolicy.WindowDays);
        var relievedIds = selections.Select(static selection => selection.Lot.LotId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacements = new List<WashSaleReplacementAcquisition>();
        var identities = new Dictionary<LedgerAccount, Dictionary<string, WashSaleReplacementAcquisition>>();
        foreach (var replacement in input.ReplacementAcquisitions
            .Where(replacement => replacement.Quantity > 0m
                && replacement.AcquiredDate >= lower && replacement.AcquiredDate <= upper
                && selections.Any(selection => selection.RealizedGainOrLoss < 0m
                    && SecurityMatches(selection.Lot.SecurityId ?? scopedSecurityId, replacement.SecurityId))
                && !((replacement.Account ?? input.Account) == input.Account
                    && relievedIds.Contains(replacement.LotId))
                && (input.WashSalePolicy.Scope != WashSaleReplacementScope.DisposingAccount
                    || replacement.Account is null || replacement.Account == input.Account))
            .OrderBy(static replacement => replacement.AcquiredDate)
            .ThenBy(static replacement => replacement.LotId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static replacement => replacement.LotId, StringComparer.Ordinal)
            .ThenBy(static replacement => replacement.Account?.Name, StringComparer.Ordinal)
            .ThenBy(static replacement => replacement.Account?.AccountType)
            .ThenBy(static replacement => replacement.Account?.Symbol, StringComparer.Ordinal)
            .ThenBy(static replacement => replacement.Account?.FinancialAccountId, StringComparer.Ordinal))
        {
            // A repeated candidate is the same capacity, not another acquisition. Reject conflicting
            // facts rather than let input order choose the quantity used for a single lot identity.
            var account = replacement.Account ?? input.Account;
            if (!identities.TryGetValue(account, out var lots))
                identities.Add(account, lots = new(StringComparer.OrdinalIgnoreCase));
            if (lots.TryGetValue(replacement.LotId, out var existing))
            {
                if (existing.AcquiredDate != replacement.AcquiredDate || existing.Quantity != replacement.Quantity
                    || existing.SecurityId != replacement.SecurityId)
                    throw new ArgumentException($"Conflicting replacement facts for lot '{replacement.LotId}'.", nameof(input));
                continue;
            }

            lots.Add(replacement.LotId, replacement);
            replacements.Add(replacement);
        }

        return replacements;
    }

    private static Guid? ResolveSoldSecurityId(IReadOnlyList<LedgerTaxLotReliefSelection> selections)
    {
        // Legacy lots may lack an identity even when the disposal has one unambiguous known
        // security. Keep that scope for unidentified lots without overriding explicit identities.
        var securityIds = selections.Select(static selection => selection.Lot.SecurityId)
            .Where(static securityId => securityId is not null)
            .Distinct()
            .Take(2)
            .ToArray();
        return securityIds.Length == 1 ? securityIds[0] : null;
    }

    private static bool SecurityMatches(Guid? soldSecurityId, Guid? replacementSecurityId)
    {
        if (soldSecurityId is null || replacementSecurityId is null)
            return true; // caller-scoped: accept the replacement in the security's own relief context.
        return soldSecurityId.Value == replacementSecurityId.Value;
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

        var recognizedGainOrLoss = realizedGainOrLoss + disallowedLoss;
        if (recognizedGainOrLoss >= 0m)
        {
            lines.Add((input.Account, 0m, costBasis - disallowedLoss));
            if (recognizedGainOrLoss > 0m)
                lines.Add((gain, 0m, recognizedGainOrLoss));
        }
        else
        {
            // Only the allowed portion of the loss is recognized; the disallowed (wash-sale)
            // portion is capitalized back into the replacement lot's basis, which nets against the
            // position credit so the entry still balances and no premature loss is booked.
            var allowedLoss = -recognizedGainOrLoss;
            if (allowedLoss > 0m)
                lines.Add((loss, allowedLoss, 0m));
            lines.Add((input.Account, 0m, costBasis - disallowedLoss));
        }

        // Drop any degenerate zero/zero lines (e.g. a fully-deferred wash sale at a zero sale
        // price) so the projection is always materializable into valid ledger entries.
        return lines.Where(static line => line.debit != 0m || line.credit != 0m).ToList();
    }
}
