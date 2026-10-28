using System.Diagnostics.CodeAnalysis;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>
/// Day-count fractions for schedule periods: builds the <see cref="DayCountPeriodContext"/> a
/// generated period implies and dispatches to the canonical
/// <see cref="DayCountConventions.TryFraction(DayCountConvention, DateOnly, DateOnly, DayCountPeriodContext, out decimal, out string?)"/>.
/// </summary>
/// <remarks>
/// The context carries the period's unadjusted reference periods, the tenor's periods per year, the
/// schedule's adjusted termination date, and the accrual calendar (falling back to the payment
/// calendar) as the business-day predicate. Actual/Actual (ICMA) is refused on a schedule whose
/// accrual dates are business-day adjusted, because ICMA is defined on unadjusted coupon periods.
/// </remarks>
public static class ScheduleDayCount
{
    /// <summary>The day-count context of <paramref name="period"/> within <paramref name="schedule"/>.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="period"/> is not a period of <paramref name="schedule"/>.</exception>
    public static DayCountPeriodContext ContextFor(SchedulePeriod period, ScheduleGenerationResult schedule)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(schedule);
        if (!BelongsTo(period, schedule))
        {
            throw new ArgumentException("The period is not part of the schedule.", nameof(period));
        }

        var calendar = schedule.Spec.AccrualAdjustment?.Calendar ?? schedule.Spec.PaymentAdjustment?.Calendar;
        return new DayCountPeriodContext(
            period.ReferencePeriods,
            schedule.Spec.Tenor.PeriodsPerYear,
            schedule.Periods[^1].AccrualEnd,
            calendar is null ? null : calendar.IsBusinessDay);
    }

    /// <summary>
    /// The fraction of [<paramref name="start"/>, <paramref name="end"/>) inside
    /// <paramref name="period"/>, or a refusal when the window leaves the period's accrual dates, the
    /// convention lacks context, or ICMA is asked of an accrual-adjusted schedule.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="period"/> or <paramref name="schedule"/> is null.</exception>
    public static bool TryFraction(
        DayCountConvention convention,
        SchedulePeriod period,
        ScheduleGenerationResult schedule,
        DateOnly start,
        DateOnly end,
        out decimal fraction,
        [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(schedule);
        fraction = 0m;
        if (!BelongsTo(period, schedule))
        {
            refusal = "The period is not part of the schedule.";
            return false;
        }

        if (start < period.AccrualStart || end > period.AccrualEnd || end < start)
        {
            refusal = "The accrual window must lie within the period's accrual dates.";
            return false;
        }

        if (convention == DayCountConvention.ActualActualIcma && AccrualIsAdjusted(schedule.Spec))
        {
            refusal = "Actual/Actual (ICMA) is defined on unadjusted coupon periods; this schedule adjusts its accrual dates.";
            return false;
        }

        return DayCountConventions.TryFraction(convention, start, end, ContextFor(period, schedule), out fraction, out refusal);
    }

    /// <summary>The fraction of the whole accrual period of <paramref name="period"/>; see <see cref="TryFraction"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="period"/> or <paramref name="schedule"/> is null.</exception>
    public static bool TryPeriodFraction(
        DayCountConvention convention,
        SchedulePeriod period,
        ScheduleGenerationResult schedule,
        out decimal fraction,
        [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(period);
        return TryFraction(convention, period, schedule, period.AccrualStart, period.AccrualEnd, out fraction, out refusal);
    }

    private static bool BelongsTo(SchedulePeriod period, ScheduleGenerationResult schedule)
        => period.Sequence >= 1
            && period.Sequence <= schedule.Periods.Count
            && schedule.Periods[period.Sequence - 1] == period;

    private static bool AccrualIsAdjusted(ScheduleSpec spec)
        => spec.AccrualAdjustment is { IsUnadjusted: false }
            || spec.EffectiveDateAdjustment is { IsUnadjusted: false }
            || spec.TerminationDateAdjustment is { IsUnadjusted: false };
}
