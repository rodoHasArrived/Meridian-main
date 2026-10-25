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

    /// <summary>A short provenance label for projections: index, as-of date and source.</summary>
    public string Describe()
        => $"forwardCurve:{IndexName}@{AsOfDate:yyyy-MM-dd}" + (string.IsNullOrWhiteSpace(Source) ? string.Empty : $" ({Source})");
}

/// <summary>
/// Supplies forward curves for floating-rate cash-flow projection. Hosts without one keep the
/// flat-forward projection (last fixing plus spread). The stored implementation is
/// <c>StoredSecurityForwardCurveProvider</c> in Application, reading <see cref="ISecurityCurveStore"/>.
/// </summary>
public interface ISecurityForwardCurveProvider
{
    Task<SecurityForwardCurve?> GetForwardCurveAsync(string indexName, DateOnly asOf, CancellationToken ct = default);
}
