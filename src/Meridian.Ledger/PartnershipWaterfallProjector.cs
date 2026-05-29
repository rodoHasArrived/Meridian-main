namespace Meridian.Ledger;

/// <summary>
/// Projects tiered partnership waterfalls into investor capital allocations.
/// </summary>
public static class PartnershipWaterfallProjector
{
    private const decimal AllocationTolerance = 0.000001m;

    public static PartnershipWaterfallAllocationProjection Project(PartnershipWaterfallAllocationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var investors = ValidateAndMapInvestors(input.Investors);
        ValidateTiers(input.Tiers, investors);

        var tierAllocations = BuildTierAllocations(input, investors);
        var investorAllocations = BuildInvestorAllocations(tierAllocations, input.Investors);
        var allocatedProfit = tierAllocations.Sum(static allocation => allocation.AllocatedAmount);
        var unallocatedProfit = input.DistributableProfit - allocatedProfit;
        var lines = BuildLines(input.FundId, allocatedProfit, investorAllocations);
        var description = input.Description ?? $"Partnership waterfall allocation for {input.FundId} period {input.PeriodId}";

        return new PartnershipWaterfallAllocationProjection(
            input,
            description,
            tierAllocations,
            investorAllocations,
            unallocatedProfit,
            lines);
    }

    private static IReadOnlyList<PartnershipWaterfallTierAllocation> BuildTierAllocations(
        PartnershipWaterfallAllocationInput input,
        IReadOnlyDictionary<string, PartnershipInvestor> investors)
    {
        var remainingProfit = input.DistributableProfit;
        var allocations = new List<PartnershipWaterfallTierAllocation>();

        foreach (var tier in input.Tiers)
        {
            if (remainingProfit <= 0m)
                break;

            var tierAmount = tier.CapAmount.HasValue
                ? Math.Min(remainingProfit, tier.CapAmount.Value)
                : remainingProfit;
            var runningTierTotal = 0m;

            for (var index = 0; index < tier.AllocationRules.Count; index++)
            {
                var rule = tier.AllocationRules[index];
                var investor = investors[rule.InvestorId];
                var amount = index == tier.AllocationRules.Count - 1
                    ? tierAmount - runningTierTotal
                    : RoundCurrency(tierAmount * rule.AllocationPercent);

                runningTierTotal += amount;
                if (amount == 0m)
                    continue;

                allocations.Add(new PartnershipWaterfallTierAllocation(
                    tier.TierId,
                    tier.Description,
                    investor,
                    amount,
                    LedgerAccounts.InvestorCapitalFor(investor.InvestorId)));
            }

            remainingProfit -= tierAmount;
        }

        return allocations;
    }

    private static IReadOnlyList<PartnershipInvestorAllocation> BuildInvestorAllocations(
        IReadOnlyList<PartnershipWaterfallTierAllocation> tierAllocations,
        IReadOnlyList<PartnershipInvestor> investors)
    {
        var totals = tierAllocations
            .GroupBy(static allocation => allocation.Investor.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Sum(allocation => allocation.AllocatedAmount),
                StringComparer.OrdinalIgnoreCase);
        var result = new List<PartnershipInvestorAllocation>();

        foreach (var investor in investors)
        {
            if (!totals.TryGetValue(investor.InvestorId, out var total) || total == 0m)
                continue;

            result.Add(new PartnershipInvestorAllocation(
                investor,
                total,
                LedgerAccounts.InvestorCapitalFor(investor.InvestorId)));
        }

        return result;
    }

    private static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> BuildLines(
        string fundId,
        decimal allocatedProfit,
        IReadOnlyList<PartnershipInvestorAllocation> investorAllocations)
    {
        if (allocatedProfit == 0m)
            return [];

        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>
        {
            (LedgerAccounts.RetainedEarningsFor(fundId), allocatedProfit, 0m),
        };

        foreach (var allocation in investorAllocations)
            lines.Add((allocation.CapitalAccount, 0m, allocation.AllocatedProfitOrLoss));

        return lines;
    }

    private static IReadOnlyDictionary<string, PartnershipInvestor> ValidateAndMapInvestors(
        IReadOnlyList<PartnershipInvestor> investors)
    {
        var result = new Dictionary<string, PartnershipInvestor>(StringComparer.OrdinalIgnoreCase);
        foreach (var investor in investors)
        {
            ArgumentNullException.ThrowIfNull(investor);
            if (!result.TryAdd(investor.InvestorId, investor))
                throw new ArgumentException($"Investor '{investor.InvestorId}' is duplicated.", nameof(investors));
        }

        return result;
    }

    private static void ValidateTiers(
        IReadOnlyList<PartnershipWaterfallTier> tiers,
        IReadOnlyDictionary<string, PartnershipInvestor> investors)
    {
        var seenTiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in tiers)
        {
            ArgumentNullException.ThrowIfNull(tier);
            if (!seenTiers.Add(tier.TierId))
                throw new ArgumentException($"Waterfall tier '{tier.TierId}' is duplicated.", nameof(tiers));

            var seenInvestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var total = 0m;
            foreach (var rule in tier.AllocationRules)
            {
                ArgumentNullException.ThrowIfNull(rule);
                if (!investors.ContainsKey(rule.InvestorId))
                    throw new ArgumentException($"Waterfall tier '{tier.TierId}' references unknown investor '{rule.InvestorId}'.", nameof(tiers));
                if (!seenInvestors.Add(rule.InvestorId))
                    throw new ArgumentException($"Waterfall tier '{tier.TierId}' contains duplicate investor '{rule.InvestorId}'.", nameof(tiers));

                total += rule.AllocationPercent;
            }

            if (Math.Abs(total - 1m) > AllocationTolerance)
                throw new ArgumentException($"Waterfall tier '{tier.TierId}' allocation percentages must sum to 1.000000; actual total was {total}.", nameof(tiers));
        }
    }

    private static decimal RoundCurrency(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
