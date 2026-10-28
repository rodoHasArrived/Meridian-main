namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// A <c>scheduleConventions</c> block as the shared reader
/// (<see cref="SecurityVersionedTerms.ReadScheduleConventions"/>) reads it: how the contractual
/// schedule kernel (ADR-025) builds an instrument's periods. The members follow the F#
/// <c>ScheduleConventions</c> record in order and meaning. Text members are kept verbatim, blank
/// strings included, so a consumer can refuse a blank spelling instead of reading it as absent; a
/// member the block does not carry, or carries with the wrong JSON kind, is <see langword="null"/>.
/// </summary>
/// <param name="BusinessDayConvention">How payment dates (and, with <paramref name="AccrualAdjusted"/>, accrual boundaries) move off non-business days.</param>
/// <param name="AccrualAdjusted">
/// Whether accrual period boundaries are adjusted for business days (payment dates always are, under
/// <paramref name="BusinessDayConvention"/>); <see langword="null"/> when the terms do not say.
/// </param>
/// <param name="PaymentCalendars">The payment calendars by id, joined into one calendar when more than one is named.</param>
/// <param name="PaymentLagBusinessDays">Business days between an accrual end and its payment date.</param>
/// <param name="RollConvention">The day of the month (or rule) unadjusted period dates roll on.</param>
/// <param name="EndOfMonthRule">Whether a month-end anchor rolls to every month end.</param>
/// <param name="FirstRegularDate">The end of an irregular first period.</param>
/// <param name="LastRegularDate">The start of an irregular last period.</param>
/// <param name="Direction">Schedule generation direction: Forward or Backward.</param>
/// <param name="StubPolicy">Whether an irregular period is short or long.</param>
/// <param name="CouponRateUnit">Whether a coupon rate is a percent or a fraction: Percent or Fraction.</param>
/// <param name="CouponCashBasis">How a regular coupon's cash amount is computed: Level (rate over frequency) or DayCount.</param>
public sealed record ScheduleConventionTerms(
    string? BusinessDayConvention,
    bool? AccrualAdjusted,
    IReadOnlyList<string> PaymentCalendars,
    int? PaymentLagBusinessDays,
    string? RollConvention,
    bool? EndOfMonthRule,
    DateOnly? FirstRegularDate,
    DateOnly? LastRegularDate,
    string? Direction,
    string? StubPolicy,
    string? CouponRateUnit,
    string? CouponCashBasis)
{
    /// <summary>No convention declared.</summary>
    public static ScheduleConventionTerms Empty { get; } =
        new(null, null, [], null, null, null, null, null, null, null, null, null);

    /// <summary>Whether nothing is declared: no payment calendar and every other member null.</summary>
    public bool IsEmpty
        => PaymentCalendars.Count == 0
           && BusinessDayConvention is null
           && AccrualAdjusted is null
           && PaymentLagBusinessDays is null
           && RollConvention is null
           && EndOfMonthRule is null
           && FirstRegularDate is null
           && LastRegularDate is null
           && Direction is null
           && StubPolicy is null
           && CouponRateUnit is null
           && CouponCashBasis is null;
}
