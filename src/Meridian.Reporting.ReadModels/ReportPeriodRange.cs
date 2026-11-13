namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Confidently resolves a report pack's free-form <c>Period</c> label into the calendar date range it
/// covers — but <b>only</b> for unambiguous ISO-style labels: a bare year (<c>2026</c>), a year-month
/// (<c>2026-05</c>), or a full date (<c>2026-05-31</c>). Fiscal period/quarter labels (<c>2026-P03</c>,
/// <c>2026-Q1</c>) and any other free text return <c>null</c>, because mapping them to calendar dates
/// requires a fund-specific fiscal calendar this layer does not have.
///
/// <para>Callers MUST treat <c>null</c> as "unknown range" and <b>fail open</b> (keep the candidate):
/// the restatement narrowing may only <i>exclude</i> a pack it is certain falls entirely before an
/// edit's effective date, never one whose period it cannot parse — otherwise it would risk a silent
/// restatement miss.</para>
/// </summary>
public static class ReportPeriodRange
{
    /// <summary>
    /// The inclusive <c>[Start, End]</c> calendar range a period label covers, or <c>null</c> when the
    /// label is not an unambiguous ISO year / year-month / year-month-day.
    /// </summary>
    public static (DateOnly Start, DateOnly End)? TryParse(string? period)
    {
        if (string.IsNullOrWhiteSpace(period))
        {
            return null;
        }

        var parts = period.Trim().Split('-');
        if (parts.Length is < 1 or > 3)
        {
            return null;
        }

        // The year must be a 4-digit calendar year; this is what excludes "P03"/"Q1"-style first tokens.
        if (parts[0].Length != 4 || !int.TryParse(parts[0], out var year) || year is < 1900 or > 9999)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        }

        // A non-numeric or out-of-range month (e.g. "P03", "Q1", "13") fails open to null.
        if (!int.TryParse(parts[1], out var month) || month is < 1 or > 12)
        {
            return null;
        }

        if (parts.Length == 2)
        {
            return (new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        }

        if (!int.TryParse(parts[2], out var day) || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        var date = new DateOnly(year, month, day);
        return (date, date);
    }

    /// <summary>
    /// True only when <paramref name="period"/> parses to a confident range that ends strictly before
    /// <paramref name="effectiveDate"/> — i.e. the period is entirely earlier than an edit effective on
    /// that date and so cannot be affected by it. Unparseable labels return <c>false</c> (fail open).
    /// </summary>
    public static bool IsEntirelyBefore(string? period, DateOnly effectiveDate)
        => TryParse(period) is { End: var end } && end < effectiveDate;
}
