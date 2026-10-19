using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Derivatives;

public sealed record SwapReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string? SwapType,
    DateOnly EffectiveDate,
    DateOnly MaturityDate,
    string LifecycleStat,
    IReadOnlyList<SwapLegDto> Legs,
    string PrimaryIdentifier,
    long Version);

public sealed record SwapLegDto(
    string LegType,
    string Currency,
    string? Index,
    decimal? FixedRate,
    /// <summary>The leg's payment-frequency text as stored (<c>paymentFrequency</c> or <c>frequency</c>).</summary>
    string? PaymentFrequency = null,
    /// <summary>The leg's day-count text as stored (<c>dayCount</c>, <c>dayCountConvention</c> or <c>dayCountBasis</c>).</summary>
    string? DayCount = null)
{
    /// <summary><see cref="PaymentFrequency"/> as a typed cadence, or null when absent or unrecognized.</summary>
    public SecurityPaymentFrequency? PaymentCadence
        => SecurityTermConventions.TryParsePaymentFrequency(PaymentFrequency, out var frequency) ? frequency : null;

    /// <summary><see cref="DayCount"/> as a typed basis, or null when absent or unrecognized.</summary>
    public Meridian.Contracts.SecurityMaster.DayCountConvention? DayCountConvention
        => DayCountConventions.TryParse(DayCount, out var convention) ? convention : null;
}
