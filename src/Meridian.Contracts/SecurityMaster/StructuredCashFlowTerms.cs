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
    IReadOnlyList<StructuredFactorScheduleEntry> FactorSchedule)
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
}
