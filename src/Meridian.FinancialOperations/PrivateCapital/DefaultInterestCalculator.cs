using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.FinancialOperations.PrivateCapital;

/// <summary>
/// Detects unfunded capital-call installments past their grace period and computes simple
/// default interest on the unfunded principal. Pure and deterministic.
/// </summary>
public static class DefaultInterestCalculator
{
    /// <summary>
    /// Computes simple interest on a defaulted principal from <paramref name="from"/> (exclusive of
    /// the grace period, handled by the caller) to <paramref name="to"/> under the given day-count
    /// convention, rounded to 2dp.
    /// </summary>
    public static decimal ComputeSimpleInterest(
        decimal principal,
        decimal annualRate,
        DateOnly from,
        DateOnly to,
        DefaultInterestConvention convention)
    {
        if (to <= from || principal <= 0m || annualRate <= 0m)
            return 0m;

        var yearBasis = convention switch
        {
            DefaultInterestConvention.Actual360 => 360m,
            DefaultInterestConvention.Thirty360 => 360m,
            _ => 365m,
        };
        var dayCount = convention == DefaultInterestConvention.Thirty360
            ? Thirty360Days(from, to)
            : to.DayNumber - from.DayNumber;

        return RoundCurrency(principal * annualRate * dayCount / yearBasis);
    }

    /// <summary>
    /// Evaluates each installment against its funding evidence and, where the call is unfunded past
    /// its grace period, produces a <see cref="CapitalCallDefault"/> with a single accrual span from
    /// the grace end to the cure date (if fully funded later) or to <paramref name="asOf"/>.
    /// </summary>
    public static IReadOnlyList<CapitalCallDefault> Evaluate(
        InvestorCommitment commitment,
        IReadOnlyList<DrawdownInstallment> installments,
        IReadOnlyList<CapitalCallFundingReceipt> fundingReceipts,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(installments);
        ArgumentNullException.ThrowIfNull(fundingReceipts);

        var fundingByInstallment = fundingReceipts
            .GroupBy(static receipt => receipt.InstallmentId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static receipt => receipt.ReceivedDate).ToArray(),
                StringComparer.Ordinal);

        var defaults = new List<CapitalCallDefault>();
        foreach (var installment in installments
                     .Where(item => item.Status is not (DrawdownInstallmentStatus.Waived or DrawdownInstallmentStatus.Cancelled))
                     .OrderBy(static item => item.Sequence))
        {
            var called = installment.ResolveCallAmount(commitment.TotalCommitment);
            fundingByInstallment.TryGetValue(installment.InstallmentId, out var receipts);
            var accrualStart = installment.DueDate.AddDays(commitment.DefaultGraceDays);
            if (asOf <= accrualStart)
                continue;

            // The defaulted principal is what remained unfunded at the end of the grace period;
            // funding that arrived within grace cures the shortfall without a default.
            var fundedByGraceEnd = receipts
                ?.Where(receipt => receipt.ReceivedDate <= accrualStart)
                .Sum(static receipt => receipt.Amount) ?? 0m;
            var defaultedPrincipal = RoundCurrency(called - fundedByGraceEnd);
            if (defaultedPrincipal <= 0m)
                continue;

            // A cure requires cumulative funding to reach the full called amount; the cure date is
            // the receipt that closes the gap. Interest accrues until cure (if any) or the as-of date.
            var cureDate = ResolveCureDate(called, receipts);
            var accrualEnd = cureDate ?? asOf;
            var interest = ComputeSimpleInterest(
                defaultedPrincipal,
                commitment.DefaultInterestRateAnnual,
                accrualStart,
                accrualEnd,
                commitment.DefaultInterestConvention);

            var defaultId = $"default:{commitment.CommitmentId}:{installment.InstallmentId}";
            var accruals = interest > 0m
                ? new List<DefaultInterestAccrual>
                {
                    new(
                        $"{defaultId}:{accrualEnd:yyyyMMdd}",
                        defaultId,
                        accrualStart,
                        accrualEnd,
                        defaultedPrincipal,
                        commitment.DefaultInterestRateAnnual,
                        commitment.DefaultInterestConvention,
                        interest),
                }
                : new List<DefaultInterestAccrual>();

            defaults.Add(new CapitalCallDefault(
                defaultId,
                commitment,
                installment,
                defaultedPrincipal,
                installment.DueDate,
                cureDate,
                cureDate is null ? DrawdownInstallmentStatus.Defaulted : DrawdownInstallmentStatus.Cured,
                accruals));
        }

        return defaults;
    }

    private static DateOnly? ResolveCureDate(decimal called, IReadOnlyList<CapitalCallFundingReceipt>? receipts)
    {
        if (receipts is null)
            return null;

        var running = 0m;
        foreach (var receipt in receipts)
        {
            running += receipt.Amount;
            if (running + LedgerToleranceConstants.Balance >= called)
                return receipt.ReceivedDate;
        }

        return null;
    }

    /// <summary>US 30/360 day count between two dates (bond-basis).</summary>
    internal static int Thirty360Days(DateOnly from, DateOnly to)
    {
        var d1 = from.Day;
        var d2 = to.Day;
        if (d1 == 31)
            d1 = 30;
        if (d2 == 31 && d1 == 30)
            d2 = 30;

        return ((to.Year - from.Year) * 360)
               + ((to.Month - from.Month) * 30)
               + (d2 - d1);
    }
}
