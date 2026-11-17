namespace Meridian.Contracts.Workstation;

/// <summary>
/// The period-over-period rule shared by every prior-period comparison: change is current minus
/// prior, and percent change divides by the absolute prior so its sign follows the change. A prior of
/// zero has no percentage. The posted-ledger comparison and the NAV, attribution and cash-ladder
/// comparisons all use it, so one figure never has two percentages.
/// </summary>
public static class PeriodChange
{
    public static decimal Change(decimal current, decimal prior) => current - prior;

    public static decimal? Percent(decimal current, decimal prior) =>
        prior == 0m ? null : (current - prior) / Math.Abs(prior) * 100m;
}

/// <summary>Outcome of a prior-period comparison request.</summary>
public static class PeriodComparisonStatus
{
    public const string Ready = "Ready";
    /// <summary>No baseline figures exist for the requested prior period.</summary>
    public const string Unavailable = "Unavailable";
    /// <summary>The request cannot be compared, for example a baseline on or after the current date.</summary>
    public const string Incompatible = "Incompatible";
}

/// <summary>One side of a comparison: the period it covers and whether it is complete.</summary>
/// <param name="Label">Operator wording, e.g. "Nov 2026" or "As of Oct 31, 2026".</param>
/// <param name="Start">First day the figures cover, or null for a point-in-time value.</param>
/// <param name="End">Last day the figures cover (the as-of day for a point-in-time value).</param>
/// <param name="IsPartial">True when the period is not complete, e.g. month to date.</param>
public sealed record PeriodComparisonWindowDto(
    string Label,
    DateOnly? Start,
    DateOnly End,
    bool IsPartial);

/// <summary>One compared figure.</summary>
/// <param name="Key">Stable identifier, e.g. "total" or an asset class or symbol.</param>
/// <param name="Label">Operator label.</param>
/// <param name="PercentChange">Change ÷ |Prior| × 100; null when Prior is zero.</param>
/// <param name="IsTotal">True for the line that totals the others.</param>
public sealed record PeriodComparisonLineDto(
    string Key,
    string Label,
    decimal Current,
    decimal Prior,
    decimal Change,
    decimal? PercentChange,
    bool IsTotal = false)
{
    /// <summary>Builds a line with change and percent from <see cref="PeriodChange"/>.</summary>
    public static PeriodComparisonLineDto Create(string key, string label, decimal current, decimal prior, bool isTotal = false) =>
        new(key, label, current, prior, PeriodChange.Change(current, prior), PeriodChange.Percent(current, prior), isTotal);
}

/// <summary>
/// A prior-period comparison for a workstation surface (NAV, run attribution, cash ladder).
/// <see cref="Notes"/> states what the figures do and do not include; a surface shows them beside
/// the table rather than implying more than the source supports.
/// </summary>
public sealed record PeriodComparisonDto(
    string Status,
    string? Reason,
    string Subject,
    string? Currency,
    PeriodComparisonWindowDto Current,
    PeriodComparisonWindowDto? Baseline,
    IReadOnlyList<PeriodComparisonLineDto> Lines,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
