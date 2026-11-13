using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Bounded separation of an already earned purchased coupon from nonaccreting par principal.</summary>
public static class BondIncomePurchasedCouponTail
{
    public const string ModelVersion = "purchased-coupon-separate-par-tail-v1";

    public static void Require(BondIncomeInstructionDto income, IReadOnlyList<BondWorkflowCashFlow> flows,
        DateOnly from, decimal clean, decimal purchasedAccrued)
    {
        var proof = income.PurchasedCouponTail ?? throw new ArgumentException("Purchased coupon tail proof is absent.");
        var coupons = flows.Where(f => f.Interest != 0m && f.PaymentDate > from).ToArray();
        var principal = flows.Where(f => f.Principal > 0m && f.PaymentDate > from).ToArray();
        var currentCoupons = income.CurrentProjection.CashFlows.Where(f => f.Interest != 0m && f.PaymentDate > from).ToArray();
        if (proof.ModelVersion != ModelVersion || proof.AcquisitionMutationBatchId == Guid.Empty
            || proof.AcquisitionJournalEntryId == Guid.Empty || from != income.AcquisitionDate || clean <= 0m
            || coupons.Length != 1 || principal.Length == 0 || flows.Any(f => f.Principal < 0m)
            || coupons[0].IsForecast || coupons[0].AccrualEnd > from || coupons[0].AccrualStart >= coupons[0].AccrualEnd
            || proof.PaymentDate != coupons[0].PaymentDate || proof.AccrualStart != coupons[0].AccrualStart
            || proof.AccrualEnd != coupons[0].AccrualEnd || proof.PurchasedCouponCash == 0m
            || proof.PurchasedCouponCash != purchasedAccrued || principal.Sum(f => f.Principal) != clean
            || income.Resets.Count != 0 || income.Calls.Count != 0 || income.CurrentIndexRatio != income.AcquisitionIndexRatio
            || currentCoupons.Length != 1 || currentCoupons[0].IsForecast
            || currentCoupons[0].PaymentDate != proof.PaymentDate || currentCoupons[0].AccrualStart != proof.AccrualStart
            || currentCoupons[0].AccrualEnd != proof.AccrualEnd || currentCoupons[0].Interest != coupons[0].Interest)
            throw new ArgumentException("Purchased coupon separation requires exactly one fully purchased observed coupon, actual birth identities and nonaccreting par principal; nonpar, mixed or unmatched tails are unavailable.");
        var allocation = income.CouponCashAllocations?.SingleOrDefault(a => a.PaymentDate == proof.PaymentDate
            && a.AccrualStart == proof.AccrualStart && a.AccrualEnd == proof.AccrualEnd);
        if (allocation is null || allocation.Lots.Count == 0)
            throw new ArgumentException("Purchased coupon separation requires retained native holder entitlement allocation.");
    }
}
