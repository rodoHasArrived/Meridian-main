namespace Meridian.Contracts.Calendars;

/// <summary>
/// A business-day convention paired with the calendar it is applied against, as FpML's
/// <c>BusinessDayAdjustments</c> pairs a convention with its business centres.
/// </summary>
/// <remarks>
/// Record equality compares <see cref="Calendar"/> by reference, so two adjustments are equal only
/// when they share the same calendar instance.
/// </remarks>
/// <param name="Convention">How a non-business day is moved.</param>
/// <param name="Calendar">The calendar that decides business days; required unless <paramref name="Convention"/> is <see cref="BusinessDayConvention.Unadjusted"/>.</param>
public sealed record BusinessDayAdjustment(BusinessDayConvention Convention, IBusinessDayCalendar? Calendar)
{
    /// <summary>The adjustment that keeps every date as it is.</summary>
    public static BusinessDayAdjustment Unadjusted { get; } = new(BusinessDayConvention.Unadjusted, null);

    /// <summary>Creates an adjustment with a required calendar.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="calendar"/> is null.</exception>
    public static BusinessDayAdjustment Of(BusinessDayConvention convention, IBusinessDayCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return new BusinessDayAdjustment(convention, calendar);
    }

    /// <summary>Whether this adjustment leaves every date unchanged.</summary>
    public bool IsUnadjusted => Convention == BusinessDayConvention.Unadjusted;

    /// <summary>Applies the convention to <paramref name="date"/>.</summary>
    /// <exception cref="InvalidOperationException">The convention is not <see cref="BusinessDayConvention.Unadjusted"/> and no calendar is set, or the calendar is degenerate.</exception>
    public DateOnly Apply(DateOnly date)
    {
        if (IsUnadjusted)
        {
            return date;
        }

        if (Calendar is null)
        {
            throw new InvalidOperationException($"The {Convention} business-day convention requires a calendar.");
        }

        return Calendar.Adjust(date, Convention);
    }
}
