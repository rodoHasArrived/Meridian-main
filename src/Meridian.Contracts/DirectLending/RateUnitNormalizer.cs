using System.Text.Json.Serialization;

namespace Meridian.Contracts.DirectLending;

/// <summary>
/// The unit a direct-lending rate value is declared in. There is deliberately no "unspecified"
/// member: a declared rate always says what its number means, so a sub-one value never has to
/// be read by magnitude (the D10 defect in <c>docs/architecture/security-master-extensibility-review.md</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DirectLendingRateUnit>))]
public enum DirectLendingRateUnit : byte
{
    /// <summary>The value is already a fraction: 0.05 means five percent.</summary>
    Fraction = 0,

    /// <summary>The value is in percentage points: 5 means five percent.</summary>
    Percent = 1,

    /// <summary>The value is in basis points: 500 means five percent.</summary>
    BasisPoints = 2
}

/// <summary>The rate field a declared value belongs to; each field has its own valid range.</summary>
public enum DirectLendingRateField : byte
{
    /// <summary>An observed benchmark fixing. May be negative.</summary>
    IndexFixing = 0,

    /// <summary>The margin over the index. May be negative (a discount margin).</summary>
    Spread = 1,

    /// <summary>A rate floor. May be negative (for example a negative-rate index floor).</summary>
    Floor = 2,

    /// <summary>A rate cap.</summary>
    Cap = 3,

    /// <summary>The share of interest paid in kind; must lie in [0, 1] once normalized.</summary>
    PikShare = 4
}

/// <summary>A rate value together with the unit it was declared in.</summary>
public sealed record DeclaredRate(decimal Value, DirectLendingRateUnit Unit)
{
    public static DeclaredRate Fraction(decimal value) => new(value, DirectLendingRateUnit.Fraction);

    public static DeclaredRate Percent(decimal value) => new(value, DirectLendingRateUnit.Percent);

    public static DeclaredRate BasisPoints(decimal value) => new(value, DirectLendingRateUnit.BasisPoints);
}

/// <summary>Outcome of normalizing one declared rate.</summary>
public readonly record struct RateNormalizationResult(decimal Fraction, string? Error)
{
    public bool IsSuccess => Error is null;
}

/// <summary>
/// Converts declared direct-lending rates to fractions using the declared unit only.
/// </summary>
/// <remarks>
/// Scoped to direct-lending reset and capitalization terms. It is the explicit-unit half of
/// remediation slice 4 (<c>docs/engineering/plans/security-master-review-remediation-plan.md</c>):
/// no magnitude inference, no blanket [0, 1] rule, and field-specific ranges so negative index
/// fixings, floors and discount margins stay representable. The Security Master cash-flow and
/// accounting readers are unchanged and can adopt this type when slice 4 is approved.
/// </remarks>
public static class RateUnitNormalizer
{
    /// <summary>
    /// Absolute bound on any normalized rate. 10 (1,000%) is far outside any loan rate and catches
    /// a value declared in the wrong unit (for example 500 declared as Percent instead of bps).
    /// </summary>
    public const decimal MaxAbsoluteFraction = 10m;

    public static RateNormalizationResult Normalize(DeclaredRate? rate, DirectLendingRateField field)
    {
        if (rate is null)
        {
            return new(0m, $"{field} rate is required and must declare its unit.");
        }

        if (!Enum.IsDefined(rate.Unit))
        {
            return new(0m, $"{field} rate declares an unknown unit '{(int)rate.Unit}'.");
        }

        var fraction = rate.Unit switch
        {
            DirectLendingRateUnit.Fraction => rate.Value,
            DirectLendingRateUnit.Percent => rate.Value / 100m,
            DirectLendingRateUnit.BasisPoints => rate.Value / 10_000m,
            _ => 0m
        };

        if (Math.Abs(fraction) > MaxAbsoluteFraction)
        {
            return new(0m, $"{field} rate {rate.Value} {rate.Unit} normalizes to {fraction}, outside the supported range of ±{MaxAbsoluteFraction}.");
        }

        switch (field)
        {
            case DirectLendingRateField.Cap when fraction < 0m:
                return new(0m, $"Cap rate {rate.Value} {rate.Unit} must not be negative.");
            case DirectLendingRateField.PikShare when fraction < 0m || fraction > 1m:
                return new(0m, $"PIK share {rate.Value} {rate.Unit} must normalize to a fraction between 0 and 1.");
        }

        return new(fraction, null);
    }

    /// <summary>Normalizes a value that must be valid; throws when it is not.</summary>
    public static decimal ToFraction(DeclaredRate rate, DirectLendingRateField field)
    {
        var result = Normalize(rate, field);
        return result.IsSuccess ? result.Fraction : throw new ArgumentException(result.Error, nameof(rate));
    }

    /// <summary>Expresses a fraction in basis points (the unit <see cref="RateResetDto.SpreadBps"/> records).</summary>
    public static decimal ToBasisPoints(decimal fraction) => fraction * 10_000m;
}
