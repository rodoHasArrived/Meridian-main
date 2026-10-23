namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// One unadjusted notional coupon period, <c>[Start, End)</c>, that an Actual/Actual (ICMA) fraction
/// is measured against. A stub period carries the regular period(s) it would otherwise have been.
/// </summary>
/// <param name="Start">The notional period's unadjusted start.</param>
/// <param name="End">The notional period's unadjusted end.</param>
public readonly record struct DayCountReferencePeriod(DateOnly Start, DateOnly End);

/// <summary>
/// The period context some day-count conventions need and the period-less
/// <see cref="DayCountConventions.Fraction(DayCountConvention, DateOnly, DateOnly)"/> dispatch cannot
/// supply: the ICMA reference periods and frequency, the termination date for the 30E/360 (ISDA)
/// February carve-out, and the business-day predicate for Business/252. The context overload of
/// <see cref="DayCountConventions.TryFraction(DayCountConvention, DateOnly, DateOnly, DayCountPeriodContext, out decimal, out string?)"/>
/// refuses when the context a convention needs is missing instead of degrading.
/// </summary>
/// <param name="ReferencePeriods">Contiguous, ascending notional periods covering the accrual window (ICMA).</param>
/// <param name="PeriodsPerYear">Regular coupon periods per year (ICMA); null when the tenor has no whole frequency.</param>
/// <param name="TerminationDate">The instrument's termination date (30E/360 ISDA February carve-out); null when unknown.</param>
/// <param name="IsBusinessDay">The business-day predicate (Business/252); null when no calendar applies.</param>
public sealed record DayCountPeriodContext(
    IReadOnlyList<DayCountReferencePeriod> ReferencePeriods,
    int? PeriodsPerYear,
    DateOnly? TerminationDate = null,
    Func<DateOnly, bool>? IsBusinessDay = null);
