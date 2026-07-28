namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// The production <see cref="IAccountingCalendar"/>: a configurable weekend mask plus an
/// operator-supplied holiday set. Period resolution follows the bank posting convention — a posting
/// stamped on a weekend or holiday is recognized in the next business day's accounting period.
/// Dates are interpreted against the UTC timeline of the posting timestamp; deployments whose books
/// close in a local market timezone pre-normalize timestamps before ingestion.
/// </summary>
public sealed class BusinessDayAccountingCalendar : IAccountingCalendar
{
    // A full leap decade: any sane holiday set has a business day well inside this window, so
    // exceeding it means the calendar is degenerate (e.g. every day marked as holiday).
    private const int MaxRollDays = 3660;

    private readonly HashSet<DateOnly> _holidays;
    private readonly bool[] _weekend = new bool[7];

    /// <summary>Weekends-only calendar (Saturday/Sunday, no holidays) — the safe default.</summary>
    public static BusinessDayAccountingCalendar Default { get; } = new();

    public BusinessDayAccountingCalendar(
        IEnumerable<DateOnly>? holidays = null,
        IEnumerable<DayOfWeek>? weekendDays = null)
    {
        _holidays = holidays is null ? [] : [.. holidays];
        var weekend = weekendDays?.Distinct().ToArray() ?? [DayOfWeek.Saturday, DayOfWeek.Sunday];
        if (weekend.Length >= 7)
        {
            throw new ArgumentException(
                "A business calendar requires at least one working day of the week.",
                nameof(weekendDays));
        }

        foreach (var day in weekend)
        {
            _weekend[(int)day] = true;
        }
    }

    public bool IsBusinessDay(DateOnly date) => !_weekend[(int)date.DayOfWeek] && !_holidays.Contains(date);

    public DateOnly ResolvePeriod(DateTimeOffset postedAtUtc) =>
        RollForwardToBusinessDay(DateOnly.FromDateTime(postedAtUtc.UtcDateTime));

    public DateOnly RollForwardToBusinessDay(DateOnly date) => Roll(date, step: 1);

    public DateOnly RollBackToBusinessDay(DateOnly date) => Roll(date, step: -1);

    public int CountBusinessDaysBetween(DateOnly from, DateOnly to)
    {
        if (from == to)
        {
            return 0;
        }

        var (start, end, sign) = from < to ? (from, to, 1) : (to, from, -1);
        var count = 0;
        for (var day = start.AddDays(1); day <= end; day = day.AddDays(1))
        {
            if (IsBusinessDay(day))
            {
                count++;
            }
        }

        return count * sign;
    }

    public DateOnly AddBusinessDays(DateOnly date, int businessDays)
    {
        if (businessDays == 0)
        {
            return date;
        }

        var step = businessDays > 0 ? 1 : -1;
        var remaining = Math.Abs(businessDays);
        var current = date;
        var walked = 0;
        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsBusinessDay(current))
            {
                remaining--;
            }

            if (++walked > MaxRollDays + (7L * Math.Abs(businessDays)))
            {
                throw new InvalidOperationException(
                    $"Could not advance {businessDays} business days from {date:yyyy-MM-dd}; the calendar's holiday set is degenerate.");
            }
        }

        return current;
    }

    private DateOnly Roll(DateOnly date, int step)
    {
        var current = date;
        for (var i = 0; i < MaxRollDays; i++)
        {
            if (IsBusinessDay(current))
            {
                return current;
            }

            current = current.AddDays(step);
        }

        throw new InvalidOperationException(
            $"No business day found within {MaxRollDays} days of {date:yyyy-MM-dd}; the calendar's holiday set is degenerate.");
    }
}
