using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Projects management fees, performance fees, high-water marks, and investor capital allocations.
/// </summary>
public static class PartnershipInvestorAccountingProjector
{
    private const decimal AllocationTolerance = LedgerToleranceConstants.Allocation;

    public static PartnershipInvestorAllocationProjection Project(PartnershipInvestorAllocationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInvestorAllocations(input.Investors);

        var grossProfitOrLoss = input.EndingNavBeforeFees - input.BeginningNav;
        var managementFee = RoundCurrency(input.BeginningNav * input.ManagementFeeRate);
        var incentiveBase = Math.Max(0m, input.EndingNavBeforeFees - input.HighWaterMark - managementFee);
        var performanceFee = RoundCurrency(incentiveBase * input.PerformanceFeeRate);
        var allocableProfitOrLoss = grossProfitOrLoss - managementFee - performanceFee;
        var endingNavAfterFees = input.EndingNavBeforeFees - managementFee - performanceFee;
        var updatedHighWaterMark = Math.Max(input.HighWaterMark, endingNavAfterFees);
        var description = input.Description ?? $"Partnership allocation for {input.FundId} period {input.PeriodId}";
        var allocations = BuildInvestorAllocations(input.Investors, allocableProfitOrLoss);
        var lines = BuildLines(input, managementFee, performanceFee, allocableProfitOrLoss, allocations);

        return new PartnershipInvestorAllocationProjection(
            input,
            grossProfitOrLoss,
            managementFee,
            performanceFee,
            allocableProfitOrLoss,
            endingNavAfterFees,
            updatedHighWaterMark,
            description,
            allocations,
            lines);
    }

    private static IReadOnlyList<PartnershipInvestorAllocation> BuildInvestorAllocations(
        IReadOnlyList<PartnershipInvestor> investors,
        decimal allocableProfitOrLoss)
    {
        var allocations = new List<PartnershipInvestorAllocation>(investors.Count);
        var runningTotal = 0m;

        for (var index = 0; index < investors.Count; index++)
        {
            var investor = investors[index];
            var allocation = index == investors.Count - 1
                ? allocableProfitOrLoss - runningTotal
                : RoundCurrency(allocableProfitOrLoss * investor.AllocationPercent);

            runningTotal += allocation;
            allocations.Add(new PartnershipInvestorAllocation(
                investor,
                allocation,
                LedgerAccounts.InvestorCapitalFor(investor.InvestorId)));
        }

        return allocations;
    }

    private static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> BuildLines(
        PartnershipInvestorAllocationInput input,
        decimal managementFee,
        decimal performanceFee,
        decimal allocableProfitOrLoss,
        IReadOnlyList<PartnershipInvestorAllocation> allocations)
    {
        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>();

        if (managementFee > 0m)
        {
            lines.Add((LedgerAccounts.ManagementFeeExpenseFor(input.FundId), managementFee, 0m));
            lines.Add((LedgerAccounts.ManagementFeePayableFor(input.FundId), 0m, managementFee));
        }

        if (performanceFee > 0m)
        {
            lines.Add((LedgerAccounts.PerformanceFeeExpenseFor(input.FundId), performanceFee, 0m));
            lines.Add((LedgerAccounts.PerformanceFeePayableFor(input.FundId), 0m, performanceFee));
        }

        if (allocableProfitOrLoss > 0m)
        {
            lines.Add((LedgerAccounts.RetainedEarningsFor(input.FundId), allocableProfitOrLoss, 0m));
            foreach (var allocation in allocations)
                lines.Add((allocation.CapitalAccount, 0m, allocation.AllocatedProfitOrLoss));
        }
        else if (allocableProfitOrLoss < 0m)
        {
            var loss = Math.Abs(allocableProfitOrLoss);
            foreach (var allocation in allocations)
                lines.Add((allocation.CapitalAccount, Math.Abs(allocation.AllocatedProfitOrLoss), 0m));

            lines.Add((LedgerAccounts.RetainedEarningsFor(input.FundId), 0m, loss));
        }

        return lines;
    }

    private static void ValidateInvestorAllocations(IReadOnlyList<PartnershipInvestor> investors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0m;
        foreach (var investor in investors)
        {
            ArgumentNullException.ThrowIfNull(investor);
            if (!seen.Add(investor.InvestorId))
                throw new ArgumentException($"Investor '{investor.InvestorId}' is duplicated.", nameof(investors));
            total += investor.AllocationPercent;
        }

        if (Math.Abs(total - 1m) > AllocationTolerance)
            throw new ArgumentException($"Investor allocation percentages must sum to 1.000000; actual total was {total}.", nameof(investors));
    }
}
