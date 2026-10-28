using System.Globalization;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>The unit a <see cref="ScheduleTenor"/> counts in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScheduleTenorUnit>))]
public enum ScheduleTenorUnit
{
    /// <summary>Calendar days.</summary>
    Day,

    /// <summary>Weeks of seven calendar days.</summary>
    Week,

    /// <summary>Calendar months, clamped to the target month's length.</summary>
    Month,

    /// <summary>Years, counted as twelve calendar months each.</summary>
    Year,

    /// <summary>A single period from the effective date to the termination date.</summary>
    Term,
}

/// <summary>
/// The length of one regular schedule period, such as <c>3M</c>, <c>1Y</c>, <c>2W</c>, <c>1D</c>, or
/// <c>T</c> for a single term period.
/// </summary>
/// <remarks>
/// A year counts as twelve months, so <c>Years(n)</c> and <c>Months(12n)</c> generate the same grid.
/// <c>default(ScheduleTenor)</c> has a count of zero and is not a valid tenor; the schedule kernel
/// refuses it.
/// </remarks>
public readonly struct ScheduleTenor : IEquatable<ScheduleTenor>
{
    private const int MaxYearCount = int.MaxValue / 12;

    /// <summary>Creates a tenor of <paramref name="count"/> units. A <see cref="ScheduleTenorUnit.Term"/> tenor's count is fixed at 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive (or, for years, exceeds what a month count can hold), or <paramref name="unit"/> is undefined.</exception>
    public ScheduleTenor(int count, ScheduleTenorUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown schedule tenor unit.");
        }

        if (unit == ScheduleTenorUnit.Term)
        {
            count = 1;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (unit == ScheduleTenorUnit.Year)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxYearCount);
        }

        Count = count;
        Unit = unit;
    }

    /// <summary>The number of units per period; 1 for a term tenor and 0 only for <c>default</c>.</summary>
    public int Count { get; }

    /// <summary>The unit the tenor counts in.</summary>
    public ScheduleTenorUnit Unit { get; }

    /// <summary>A tenor of <paramref name="count"/> days.</summary>
    public static ScheduleTenor Days(int count) => new(count, ScheduleTenorUnit.Day);

    /// <summary>A tenor of <paramref name="count"/> weeks.</summary>
    public static ScheduleTenor Weeks(int count) => new(count, ScheduleTenorUnit.Week);

    /// <summary>A tenor of <paramref name="count"/> months.</summary>
    public static ScheduleTenor Months(int count) => new(count, ScheduleTenorUnit.Month);

    /// <summary>A tenor of <paramref name="count"/> years.</summary>
    public static ScheduleTenor Years(int count) => new(count, ScheduleTenorUnit.Year);

    /// <summary>The single-period tenor: one period from effective date to termination.</summary>
    public static ScheduleTenor Term { get; } = new(1, ScheduleTenorUnit.Term);

    /// <summary>Whether the tenor steps in calendar months (a month or year tenor).</summary>
    public bool IsMonthBased => Unit is ScheduleTenorUnit.Month or ScheduleTenorUnit.Year;

    /// <summary>Months per period: <see cref="Count"/> for months, twelve times it for years, otherwise null.</summary>
    public int? MonthsPerPeriod => Unit switch
    {
        ScheduleTenorUnit.Month => Count,
        ScheduleTenorUnit.Year => 12 * Count,
        _ => null,
    };

    /// <summary>
    /// Regular periods per year when the tenor divides a year evenly by market convention: 12/n for an
    /// n-month tenor that divides twelve, 1 for one year, 52 and 26 for one and two weeks, 365 for one
    /// day; otherwise null.
    /// </summary>
    public int? PeriodsPerYear => Unit switch
    {
        ScheduleTenorUnit.Month when Count > 0 && 12 % Count == 0 => 12 / Count,
        ScheduleTenorUnit.Year when Count == 1 => 1,
        ScheduleTenorUnit.Week when Count == 1 => 52,
        ScheduleTenorUnit.Week when Count == 2 => 26,
        ScheduleTenorUnit.Day when Count == 1 => 365,
        _ => null,
    };

    /// <summary>
    /// <paramref name="date"/> moved by <paramref name="periods"/> tenors (negative moves back). Month
    /// steps clamp the day to the target month's length, as <see cref="DateOnly.AddMonths"/> does.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenor is a term tenor, which has no step.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The result falls outside the <see cref="DateOnly"/> range.</exception>
    public DateOnly Advance(DateOnly date, int periods)
    {
        if (Unit == ScheduleTenorUnit.Term)
        {
            throw new InvalidOperationException("A term tenor has no step to advance by.");
        }

        return TryAdvance(date, periods, out var result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(periods), periods, "The advanced date falls outside the supported date range.");
    }

    /// <summary>
    /// Non-throwing <see cref="Advance"/>: false when the result falls outside the
    /// <see cref="DateOnly"/> range, or when the tenor is a term tenor (or <c>default</c>).
    /// </summary>
    public bool TryAdvance(DateOnly date, int periods, out DateOnly result)
    {
        result = date;
        if (Count <= 0)
        {
            return false;
        }

        switch (Unit)
        {
            case ScheduleTenorUnit.Month:
            case ScheduleTenorUnit.Year:
                {
                    var monthIndex = (date.Year * 12L) + date.Month - 1 + ((long)periods * MonthsPerPeriod!.Value);
                    var year = monthIndex >= 0 ? monthIndex / 12 : -1;
                    if (year < DateOnly.MinValue.Year || year > DateOnly.MaxValue.Year)
                    {
                        return false;
                    }

                    var month = (int)(monthIndex % 12) + 1;
                    result = new DateOnly((int)year, month, Math.Min(date.Day, DateTime.DaysInMonth((int)year, month)));
                    return true;
                }

            case ScheduleTenorUnit.Week:
            case ScheduleTenorUnit.Day:
                {
                    var daysPerPeriod = Unit == ScheduleTenorUnit.Week ? 7L * Count : Count;
                    var dayNumber = date.DayNumber + ((long)periods * daysPerPeriod);
                    if (dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber)
                    {
                        return false;
                    }

                    result = DateOnly.FromDayNumber((int)dayNumber);
                    return true;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Maps a Security Master payment frequency to its tenor: Daily 1D, Weekly 1W, BiWeekly 2W,
    /// Monthly 1M, BiMonthly 2M, Quarterly 3M, SemiAnnual 6M, Annual 12M and AtMaturity the term
    /// tenor. Irregular has no tenor and returns false.
    /// </summary>
    public static bool TryFromPaymentFrequency(SecurityPaymentFrequency frequency, out ScheduleTenor tenor)
    {
        ScheduleTenor? mapped = frequency switch
        {
            SecurityPaymentFrequency.Daily => Days(1),
            SecurityPaymentFrequency.Weekly => Weeks(1),
            SecurityPaymentFrequency.BiWeekly => Weeks(2),
            SecurityPaymentFrequency.Monthly => Months(1),
            SecurityPaymentFrequency.BiMonthly => Months(2),
            SecurityPaymentFrequency.Quarterly => Months(3),
            SecurityPaymentFrequency.SemiAnnual => Months(6),
            SecurityPaymentFrequency.Annual => Months(12),
            SecurityPaymentFrequency.AtMaturity => Term,
            _ => null,
        };

        tenor = mapped.GetValueOrDefault();
        return mapped.HasValue;
    }

    /// <inheritdoc />
    public bool Equals(ScheduleTenor other) => Count == other.Count && Unit == other.Unit;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ScheduleTenor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Count, Unit);

    /// <summary>The tenor in market shorthand: <c>3M</c>, <c>1Y</c>, <c>2W</c>, <c>1D</c>, or <c>T</c> for a term tenor.</summary>
    public override string ToString() => Unit switch
    {
        ScheduleTenorUnit.Term => "T",
        ScheduleTenorUnit.Year => string.Create(CultureInfo.InvariantCulture, $"{Count}Y"),
        ScheduleTenorUnit.Month => string.Create(CultureInfo.InvariantCulture, $"{Count}M"),
        ScheduleTenorUnit.Week => string.Create(CultureInfo.InvariantCulture, $"{Count}W"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Count}D"),
    };

    /// <summary>Whether two tenors have the same count and unit.</summary>
    public static bool operator ==(ScheduleTenor left, ScheduleTenor right) => left.Equals(right);

    /// <summary>Whether two tenors differ in count or unit.</summary>
    public static bool operator !=(ScheduleTenor left, ScheduleTenor right) => !left.Equals(right);
}
