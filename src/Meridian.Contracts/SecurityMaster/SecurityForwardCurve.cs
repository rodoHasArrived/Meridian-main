using System.Globalization;
using System.Text.Json.Serialization;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>A projected index fixing for a reset date. Rates above 1 are read as percentages.</summary>
public sealed record SecurityForwardCurvePoint(DateOnly Date, decimal Rate);

/// <summary>
/// Projected fixings for one floating-rate index, as observed on <see cref="AsOfDate"/>. A floating
/// leg resets each period at the curve's rate for the period's accrual start date, linearly
/// interpolated between points and held flat beyond the first and last point.
/// </summary>
public sealed record SecurityForwardCurve(
    string IndexName,
    DateOnly AsOfDate,
    IReadOnlyList<SecurityForwardCurvePoint> Points,
    string? Source = null)
{
    /// <summary>The immutable stored curve version behind these projected fixings, when retained.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondCalculationInput? RetainedInput { get; init; }
    /// <summary>The projected fixing for a reset on <paramref name="resetDate"/>; null when the curve has no points.</summary>
    public decimal? RateAt(DateOnly resetDate)
    {
        if (Points is not { Count: > 0 })
        {
            return null;
        }

        var ordered = Points.OrderBy(static point => point.Date).ToArray();
        if (resetDate <= ordered[0].Date)
        {
            return ordered[0].Rate;
        }

        for (var i = 1; i < ordered.Length; i++)
        {
            var right = ordered[i];
            if (resetDate > right.Date)
            {
                continue;
            }

            var left = ordered[i - 1];
            var span = right.Date.DayNumber - left.Date.DayNumber;
            if (span == 0)
            {
                return right.Rate;
            }

            var weight = (decimal)(resetDate.DayNumber - left.Date.DayNumber) / span;
            return left.Rate + ((right.Rate - left.Rate) * weight);
        }

        return ordered[^1].Rate;
    }

    /// <summary>The prefix of every <see cref="Describe"/> label, and so of a curve-projected leg's <c>RateSource</c>.</summary>
    public const string RateSourcePrefix = "forwardCurve:";

    private const string AsOfFormat = "yyyy-MM-dd";

    /// <summary>
    /// A short provenance label for projections: <c>forwardCurve:{index}@{yyyy-MM-dd}</c>, then
    /// <c> ({source})</c> when a source is known. The date is written in the invariant culture, so
    /// <see cref="TryReadAsOf"/> reads it back on any host.
    /// </summary>
    public string Describe()
        => RateSourcePrefix + IndexName + "@" + AsOfDate.ToString(AsOfFormat, CultureInfo.InvariantCulture)
           + (string.IsNullOrWhiteSpace(Source) ? string.Empty : " (" + Source + ")");

    /// <summary>
    /// Reads the curve's as-of date back from a <see cref="Describe"/> label, such as a leg's
    /// <c>RateSource</c>. The date is the first <c>@yyyy-MM-dd</c> that ends the label or is followed
    /// by the <c> (source)</c> suffix, so an index name or source that itself contains <c>@</c> still
    /// reads. False for any other label.
    /// </summary>
    public static bool TryReadAsOf(string? rateSource, out DateOnly asOf)
    {
        asOf = default;
        if (rateSource is null || !rateSource.StartsWith(RateSourcePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var at = rateSource.IndexOf('@', RateSourcePrefix.Length); at >= 0; at = rateSource.IndexOf('@', at + 1))
        {
            var dateStart = at + 1;
            if (dateStart + AsOfFormat.Length > rateSource.Length)
            {
                break;
            }

            var tail = rateSource.AsSpan(dateStart + AsOfFormat.Length);
            var endsTheLabel = tail.IsEmpty || (tail.StartsWith(" (", StringComparison.Ordinal) && tail.EndsWith(")", StringComparison.Ordinal));
            if (endsTheLabel && DateOnly.TryParseExact(
                    rateSource.AsSpan(dateStart, AsOfFormat.Length), AsOfFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
            {
                return true;
            }
        }

        asOf = default;
        return false;
    }
}

/// <summary>
/// Supplies forward curves for floating-rate cash-flow projection. Hosts without one keep the
/// flat-forward projection (last fixing plus spread). The stored implementation is
/// <c>StoredSecurityForwardCurveProvider</c> in Application, reading <see cref="ISecurityCurveStore"/>.
/// </summary>
public interface ISecurityForwardCurveProvider
{
    Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, DateOnly asOf, CancellationToken ct = default);

    /// <summary>Reads a curve eligible at both cutoffs; providers without recorded-time support refuse strict requests.</summary>
    Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, BondCalculationContext context, CancellationToken ct = default)
        => context.KnownAt is null && context.Curves is not { Count: > 0 }
            ? GetForwardCurveAsync(indexName, DateOnly.FromDateTime(context.AsOf.UtcDateTime), ct)
            : Task.FromResult<SecurityForwardCurve?>(null);
}
