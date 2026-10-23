using System.Globalization;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>The rule that places each month-based grid date within its month.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScheduleRollKind>))]
public enum ScheduleRollKind
{
    /// <summary>Keep the anchor's day of month, clamped to shorter months (<see cref="DateOnly.AddMonths"/> semantics).</summary>
    AnchorDay,

    /// <summary>A fixed day of month (1 to 31), clamped to shorter months.</summary>
    FixedDay,

    /// <summary>The last day of the month.</summary>
    EndOfMonth,

    /// <summary>The third Wednesday of the month (IMM dates).</summary>
    Imm,
}

/// <summary>
/// The roll convention of a schedule grid: where in its month each month-based grid date falls.
/// <c>default(ScheduleRoll)</c> is <see cref="AnchorDay"/>.
/// </summary>
/// <remarks>
/// Each grid date is rolled independently from the raw anchored date, so a date clamped into a short
/// month never shifts later dates. In anchored generation <see cref="AnchorDay"/> equals
/// <see cref="FixedDay(int)"/> of the anchor's day, because <see cref="DateOnly.AddMonths"/> already
/// clamps.
/// </remarks>
public readonly record struct ScheduleRoll
{
    private ScheduleRoll(ScheduleRollKind kind, int dayOfMonth)
    {
        Kind = kind;
        DayOfMonth = dayOfMonth;
    }

    /// <summary>Keep the raw grid date as it is.</summary>
    public static ScheduleRoll AnchorDay { get; } = default;

    /// <summary>Roll each grid date to the last day of its month.</summary>
    public static ScheduleRoll EndOfMonth { get; } = new(ScheduleRollKind.EndOfMonth, 0);

    /// <summary>Roll each grid date to the third Wednesday of its month.</summary>
    public static ScheduleRoll Imm { get; } = new(ScheduleRollKind.Imm, 0);

    /// <summary>Roll each grid date to <paramref name="dayOfMonth"/>, clamped to the month's length.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dayOfMonth"/> is outside 1 to 31.</exception>
    public static ScheduleRoll FixedDay(int dayOfMonth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dayOfMonth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dayOfMonth, 31);
        return new ScheduleRoll(ScheduleRollKind.FixedDay, dayOfMonth);
    }

    /// <summary>
    /// The roll a schedule anchored on <paramref name="anchor"/> implies: <see cref="EndOfMonth"/> when
    /// <paramref name="endOfMonthRule"/> is set and the anchor is a month end, otherwise
    /// <see cref="FixedDay(int)"/> of the anchor's day.
    /// </summary>
    public static ScheduleRoll ForAnchor(DateOnly anchor, bool endOfMonthRule)
        => endOfMonthRule && anchor.Day == DateTime.DaysInMonth(anchor.Year, anchor.Month)
            ? EndOfMonth
            : FixedDay(anchor.Day);

    /// <summary>The roll rule.</summary>
    public ScheduleRollKind Kind { get; }

    /// <summary>The fixed day of month for <see cref="ScheduleRollKind.FixedDay"/>; 0 otherwise.</summary>
    public int DayOfMonth { get; }

    /// <summary>Places <paramref name="rawGridDate"/> within its month according to the rule.</summary>
    public DateOnly Apply(DateOnly rawGridDate)
    {
        var daysInMonth = DateTime.DaysInMonth(rawGridDate.Year, rawGridDate.Month);
        return Kind switch
        {
            ScheduleRollKind.FixedDay => new DateOnly(rawGridDate.Year, rawGridDate.Month, Math.Min(DayOfMonth, daysInMonth)),
            ScheduleRollKind.EndOfMonth => new DateOnly(rawGridDate.Year, rawGridDate.Month, daysInMonth),
            ScheduleRollKind.Imm => ThirdWednesday(rawGridDate.Year, rawGridDate.Month),
            _ => rawGridDate,
        };
    }

    /// <summary>Whether <paramref name="date"/> already satisfies the rule.</summary>
    public bool Matches(DateOnly date) => Apply(date) == date;

    /// <summary>The rule in shorthand, for example <c>FixedDay(15)</c> or <c>EndOfMonth</c>.</summary>
    public override string ToString()
        => Kind == ScheduleRollKind.FixedDay
            ? string.Create(CultureInfo.InvariantCulture, $"FixedDay({DayOfMonth})")
            : Kind.ToString();

    private static DateOnly ThirdWednesday(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var toWednesday = ((int)DayOfWeek.Wednesday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(toWednesday + 14);
    }
}
