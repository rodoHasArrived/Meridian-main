using Meridian.Contracts.Workstation;

namespace Meridian.Ledger;

/// <summary>
/// Single owner of valuation mark admission. Compatibility settings may tighten these controls;
/// disabled, allow, flag and incomplete-coverage settings cannot authorize unsupported numbers.
/// </summary>
public sealed record ValuationFreshnessPolicy
{
    public ValuationFreshnessPolicy(
        int maximumAgeDays = 3,
        DailyPortfolioPriceConfidence minimumConfidence = DailyPortfolioPriceConfidence.Medium,
        string version = "mark-freshness-v1")
    {
        if (maximumAgeDays < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAgeDays));
        if (!Enum.IsDefined(minimumConfidence))
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        MaximumAgeDays = maximumAgeDays;
        MinimumConfidence = minimumConfidence < DailyPortfolioPriceConfidence.Medium
            ? DailyPortfolioPriceConfidence.Medium : minimumConfidence;
        Version = version;
    }

    public static ValuationFreshnessPolicy Default { get; } = new();
    public int MaximumAgeDays { get; }
    public DailyPortfolioPriceConfidence MinimumConfidence { get; }
    public string Version { get; }
    public bool RequireCompleteCoverage => true;
    public bool RequireObservedDate => true;

    public MarkFreshnessAssessmentDto Assess(
        string symbol,
        Guid? securityId,
        string? financialAccountId,
        DateOnly valuationDate,
        DateOnly? observedOn,
        DailyPortfolioPriceConfidence? confidence,
        decimal? price)
    {
        int? age = observedOn is { } observed ? valuationDate.DayNumber - observed.DayNumber : null;
        var reason = price is null ? "No closing mark was available."
            : price <= 0m ? "Closing mark price must be positive."
            : !observedOn.HasValue ? "Mark observation date is required."
            : age < 0 ? $"Mark observation date {observedOn:yyyy-MM-dd} is after valuation date {valuationDate:yyyy-MM-dd}."
            : age > MaximumAgeDays ? $"Mark is {age} days old; maximum allowed age is {MaximumAgeDays} days."
            : confidence is null || !Enum.IsDefined(confidence.Value) || confidence < MinimumConfidence
                ? $"Mark confidence {confidence?.ToString() ?? "unknown"} is below required {MinimumConfidence}."
            : null;
        return new MarkFreshnessAssessmentDto(symbol, securityId, financialAccountId, valuationDate,
            observedOn, age, Version, reason is null ? "Current" : "ReviewRequired", reason);
    }
}
