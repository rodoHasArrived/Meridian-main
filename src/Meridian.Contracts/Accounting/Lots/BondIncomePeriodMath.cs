using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Accounting period EIR allocation; purchased accrued interest is a separate asset.</summary>
internal static class BondIncomePeriodMath
{
    public static decimal Solve(IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly from, decimal clean,
        decimal purchasedAccrued, BondWorkflowResult opening, BondIncomeInstructionDto policy)
    {
        if (policy.ZeroCoupon)
            return BondIncomeZeroCouponMath.Solve(flows, from, clean, purchasedAccrued);
        if (from == policy.AcquisitionDate && policy.PurchasedCouponTail is null
            && flows.Any(f => f.Interest != 0m && f.PaymentDate > from && f.AccrualEnd <= from))
            throw new ArgumentException("Already earned coupon cash remaining at acquisition requires explicit purchased-coupon separation; nonpar, mixed or unmatched tails are unavailable.");
        if (policy.PurchasedCouponTail is not null)
        {
            BondIncomePurchasedCouponTail.Require(policy, flows, from, clean, purchasedAccrued);
            // Positive principal, actual future dates, and total principal equal to clean cost
            // certify the unique annual principal-only root zero. The coupon remains a separate
            // purchased receivable/payable and retains its actual cash date.
            return 0m;
        }
        if (TryAffine(flows, from, clean, purchasedAccrued, opening, policy, out var affine))
            return affine;
        var coefficients = flows.Where(f => f.PaymentDate > from).GroupBy(f => f.PaymentDate)
            .OrderBy(g => g.Key).Select(g => g.Sum(f => f.Total)).Prepend(-clean - purchasedAccrued)
            .Where(c => c != 0m).Select(Math.Sign).ToArray();
        if (coefficients.Zip(coefficients.Skip(1), (a, b) => a != b ? 1 : 0).Sum() != 1)
            throw new ArgumentException("Accounting effective yield is unavailable: YieldUniquenessNotEstablished.");
        var periods = flows.Where(f => f.AccrualStart < f.AccrualEnd).ToArray();
        if (periods.Length == 0 || periods.Any(f => f.PeriodUnits is not > 0m || f.AccrualFraction is not > 0m))
            throw new ArgumentException("Accounting yield requires retained contractual period and day-count units.");
        var through = flows.Max(f => f.PaymentDate);
        var low = -0.999999999m / periods.Max(f => f.PeriodUnits!.Value);
        var high = 1m;
        decimal Evaluate(decimal rate) => Roll(flows, from, through, clean, rate, purchasedAccrued, opening, null, policy);
        var lowValue = Evaluate(low);
        var highValue = Evaluate(high);
        for (var i = 0; Math.Sign(lowValue) == Math.Sign(highValue) && i < 20; i++)
        {
            high *= 2m;
            highValue = Evaluate(high);
        }
        if (Math.Sign(lowValue) == Math.Sign(highValue))
            throw new ArgumentException("Accounting effective yield is unavailable: YieldNotBracketed.");
        for (var i = 0; i < 160; i++)
        {
            var middle = (low + high) / 2m;
            var value = Evaluate(middle);
            if (Math.Abs(value) < 0.00000000000001m || high - low < 0.0000000000000000000000001m)
                return middle;
            if (Math.Sign(value) == Math.Sign(lowValue))
            { low = middle; lowValue = value; }
            else
                high = middle;
        }
        throw new ArgumentException("Accounting effective yield did not meet its numerical residual bound.");
    }

    public static string Certificate(IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly from, decimal clean,
        decimal purchasedAccrued, BondWorkflowResult opening, BondIncomeInstructionDto policy)
        => policy.ZeroCoupon ? "positive-principal-annual-act365-v1"
            : policy.PurchasedCouponTail is not null ? BondIncomePurchasedCouponTail.ModelVersion
            : TryAffine(flows, from, clean, purchasedAccrued, opening, policy, out _) ? "single-period-affine-accounting-v1"
            : "dated-sign-change-bracket-v1";

    private static bool TryAffine(IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly from, decimal clean,
        decimal purchasedAccrued, BondWorkflowResult opening, BondIncomeInstructionDto policy, out decimal rate)
    {
        rate = 0m;
        var periods = flows.Where(f => f.AccrualStart < f.AccrualEnd && f.AccrualEnd > from).ToArray();
        var principal = flows.Where(f => f.Principal > 0m && f.PaymentDate > from).ToArray();
        if (clean <= 0m || periods.Length != 1 || principal.Length == 0 || flows.Any(f => f.Principal < 0m))
            return false;
        var period = periods[0];
        var dates = principal.Select(f => f.PaymentDate).Distinct().ToArray();
        if (period.PeriodUnits is not > 0m || period.AccrualFraction is not > 0m || period.PaymentDate < period.AccrualEnd
            || dates.Length != 1 || dates[0] <= from || dates[0] > period.AccrualEnd
            || dates[0] != period.AccrualEnd && principal.Sum(f => f.Principal) != clean)
            return false;
        var start = from > period.AccrualStart ? from : period.AccrualStart;
        var held = period.PeriodUnits.Value * (Weight(period, dates[0], null, policy) - Weight(period, start, opening, policy));
        var slope = clean * held;
        if (slope <= 0m)
            return false;
        // This is an exact certificate of the adopted accounting recurrence, not a cash-IRR
        // certificate. For end-period principal R(q)=C+AI-I-P+C*heldUnits*q. For par
        // midperiod payoff the post-payoff principal tail cancels, giving the same affine form.
        var constant = clean + purchasedAccrued - period.Interest - principal.Sum(f => f.Principal);
        rate = -constant / slope;
        if (1m + rate * period.PeriodUnits.Value <= 0m)
            throw new ArgumentException("Accounting period yield is outside its positive accumulation domain.");
        var residual = Roll(flows, from, flows.Max(f => f.PaymentDate), clean, rate, purchasedAccrued, opening, null, policy);
        if (Math.Abs(residual) > 0.00000000000001m)
            throw new ArgumentException("Accounting effective yield did not meet its numerical residual bound.");
        if (dates[0] < period.AccrualEnd && policy.ThroughDate == dates[0]
            && Math.Abs(Roll(flows, from, dates[0], clean, rate, purchasedAccrued, opening, policy.AmortizationProjection, policy))
                > 0.000000000001m)
            throw new ArgumentException("A par midperiod payoff requires exact retained fully earned contractual coupon accrual and zero terminal clean basis.");
        return true;
    }

    public static decimal Roll(IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly from, DateOnly through,
        decimal clean, decimal rate, decimal openingAccrued, BondWorkflowResult opening,
        BondWorkflowResult? ending, BondIncomeInstructionDto policy)
    {
        if (policy.ZeroCoupon)
            return BondIncomeZeroCouponMath.Roll(flows, from, through, clean, rate, openingAccrued);
        if (policy.PurchasedCouponTail is not null)
        {
            BondIncomePurchasedCouponTail.Require(policy, flows, from, clean, openingAccrued);
            if (rate != 0m)
                throw new ArgumentException("Separated nonaccreting par principal requires its certified zero yield.");
            return clean - flows.Where(f => f.Principal > 0m && f.PaymentDate > from && f.PaymentDate <= through).Sum(f => f.Principal);
        }
        var periods = flows.Where(f => f.AccrualStart < f.AccrualEnd && f.AccrualEnd > from)
            .OrderBy(f => f.AccrualEnd).ToArray();
        var principal = flows.Where(f => f.Principal > 0m && f.PaymentDate > from && f.PaymentDate <= through)
            .GroupBy(f => f.PaymentDate).ToDictionary(g => g.Key, g => g.Sum(f => f.Principal));
        var consumed = new HashSet<DateOnly>();
        var first = true;
        foreach (var period in periods)
        {
            var start = from > period.AccrualStart ? from : period.AccrualStart;
            if (through <= start)
                break;
            var end = through < period.AccrualEnd ? through : period.AccrualEnd;
            var startWeight = start == period.AccrualStart ? 0m : Weight(period, start, opening, policy);
            var endWeight = end == period.AccrualEnd ? 1m : Weight(period, end, ending, policy);
            var units = period.PeriodUnits ?? throw new ArgumentException("Accounting accrual requires retained period units.");
            var heldUnits = units * (endWeight - startWeight);
            if (heldUnits < 0m || 1m + rate * units <= 0m)
                throw new ArgumentException("Accounting period yield is outside its positive accumulation domain.");
            var effectiveIncome = clean * rate * heldUnits;
            var paid = 0m;
            foreach (var item in principal.Where(p => p.Key >= start && p.Key <= end).OrderBy(p => p.Key))
            {
                if (!consumed.Add(item.Key))
                    continue;
                var at = Weight(period, item.Key, null, policy);
                effectiveIncome -= item.Value * rate * units * Math.Max(0m, endWeight - at);
                paid += item.Value;
            }
            decimal coupon;
            if (end == period.AccrualEnd)
                coupon = period.Interest;
            else if (ending is not null)
                coupon = ending.ContractualAccruedInterest ?? ending.AccruedInterest
                ?? throw new ArgumentException("Accounting accrual requires retained accrued-interest inputs.");
            else
                throw new ArgumentException("Interim accounting accrual requires a dated retained projection.");
            if (first)
                coupon -= openingAccrued;
            clean += effectiveIncome - coupon - paid;
            first = false;
            if (end == through)
                break;
        }
        return clean - principal.Where(p => !consumed.Contains(p.Key)).Sum(p => p.Value);
    }

    private static decimal Weight(BondWorkflowCashFlow period, DateOnly date, BondWorkflowResult? snapshot,
        BondIncomeInstructionDto policy)
    {
        if (date <= period.AccrualStart)
            return 0m;
        if (date >= period.AccrualEnd)
            return 1m;
        if (snapshot?.Context.SettlementDate == date)
        {
            var elapsed = policy.LevelCoupon ? snapshot.SettlementAccrualPeriodUnits : snapshot.SettlementAccrualFraction;
            var total = policy.LevelCoupon ? snapshot.SettlementPeriodUnits : snapshot.SettlementPeriodAccrualFraction;
            if (elapsed is { } part && total is > 0m)
                return part / total.Value;
        }
        if (policy.DayCount is DayCountConvention.Unknown or DayCountConvention.Business252)
            throw new ArgumentException("Accounting accrual lacks contextual day-count evidence.");
        // ICMA's denominator cancels inside a regular reference period; irregular periods need
        // the shared contextual snapshot instead of a raw-date approximation.
        if (policy.DayCount == DayCountConvention.ActualActualIcma)
        {
            if (period.PeriodUnits != 1m)
                throw new ArgumentException("Irregular ICMA principal changes need contextual segment weights.");
            return (decimal)(date.DayNumber - period.AccrualStart.DayNumber) / (period.AccrualEnd.DayNumber - period.AccrualStart.DayNumber);
        }
        var full = DayCountConventions.Fraction(policy.DayCount, period.AccrualStart, period.AccrualEnd);
        if (full <= 0m)
            throw new ArgumentException("Accounting contractual accrual period is empty.");
        return DayCountConventions.Fraction(policy.DayCount, period.AccrualStart, date) / full;
    }
}
