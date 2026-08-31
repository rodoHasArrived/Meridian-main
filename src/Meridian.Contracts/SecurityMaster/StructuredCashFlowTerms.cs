namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Strongly typed economic terms resolved once from a security's raw term JSON, replacing the
/// scattered fuzzy key-probing that previously lived inline in the projection math. All alias
/// resolution and JSON traversal is centralized in <see cref="StructuredCashFlowTermsResolver"/>;
/// downstream projection and ledger code consumes this typed shape only.
/// </summary>
public sealed record StructuredCashFlowTerms(
    DateOnly? MaturityDate,
    DateOnly? IssueDate,
    decimal? PrincipalFace,
    decimal? CurrentFactor,
    decimal? CouponRate,
    string? PaymentFrequency,
    string? DayCountConvention,
    IReadOnlyList<StructuredFactorScheduleEntry> FactorSchedule,
    IReadOnlyList<StructuredCashFlowLeg>? Legs = null,
    IReadOnlyList<StructuredPrincipalScheduleEntry>? PrincipalSchedule = null,
    IReadOnlyList<StructuredStepCouponEntry>? StepCouponSchedule = null,
    string? InflationIndex = null,
    decimal? InflationBaseIndexValue = null,
    decimal? InflationIndexRatio = null)
{
    /// <summary>An empty term set used when a security carries no readable structured terms.</summary>
    public static StructuredCashFlowTerms Empty { get; } = new(
        MaturityDate: null,
        IssueDate: null,
        PrincipalFace: null,
        CurrentFactor: null,
        CouponRate: null,
        PaymentFrequency: null,
        DayCountConvention: null,
        FactorSchedule: Array.Empty<StructuredFactorScheduleEntry>());

    /// <summary>True when a typed, multi-point factor schedule was resolved from the terms.</summary>
    public bool HasFactorSchedule => FactorSchedule.Count > 0;

    /// <summary>True when typed cash-flow legs were resolved (multi-leg or floating-rate structure).</summary>
    public bool HasLegs => Legs is { Count: > 0 };

    /// <summary>True when contractual principal payment dates and amounts were resolved.</summary>
    public bool HasPrincipalSchedule => PrincipalSchedule is { Count: > 0 };

    /// <summary>
    /// Returns the pool factor in effect on <paramref name="asOf"/> — the latest scheduled factor
    /// dated on or before that day — falling back to the scalar <see cref="CurrentFactor"/> when no
    /// schedule point applies. This is how the typed factor schedule seeds amortization instead of a
    /// single free-text scalar.
    /// </summary>
    public decimal? FactorAsOf(DateOnly asOf)
    {
        for (var i = FactorSchedule.Count - 1; i >= 0; i--)
        {
            var entry = FactorSchedule[i];
            if (entry.AsOfDate <= asOf)
            {
                return entry.Factor;
            }
        }

        return CurrentFactor;
    }

    /// <summary>True when a dated step-coupon schedule was resolved from the terms.</summary>
    public bool HasStepCouponSchedule => StepCouponSchedule is { Count: > 0 };

    /// <summary>
    /// Returns the coupon rate payable on <paramref name="asOf"/> — for step coupons the latest
    /// scheduled step effective on or before that day, otherwise the scalar
    /// <see cref="CouponRate"/>. This is what makes a step-rate bond computable instead of a
    /// classified-but-inert label: accrual and projection math can ask for the rate per period.
    /// </summary>
    public decimal? CouponRateAsOf(DateOnly asOf)
    {
        if (StepCouponSchedule is { Count: > 0 } schedule)
        {
            decimal? applicable = null;
            DateOnly? applicableDate = null;
            foreach (var entry in schedule)
            {
                if (entry.EffectiveDate <= asOf
                    && (applicableDate is null || entry.EffectiveDate >= applicableDate.Value))
                {
                    applicable = entry.Rate;
                    applicableDate = entry.EffectiveDate;
                }
            }

            return applicable ?? CouponRate;
        }

        return CouponRate;
    }
}

/// <summary>
/// One dated coupon-rate step resolved from a bond's <c>stepSchedule</c>: the annual rate payable
/// from <see cref="EffectiveDate"/> until the next entry's effective date (or maturity).
/// </summary>
public sealed record StructuredStepCouponEntry(DateOnly EffectiveDate, decimal Rate);
