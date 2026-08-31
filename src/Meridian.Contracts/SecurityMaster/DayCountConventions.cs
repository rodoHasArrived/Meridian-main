namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical day-count basis for fixed-income accrual, coupon, amortization, and cost-basis math.
/// <see cref="Unknown"/> is a real, non-throwing member: unrecognized raw conventions parse to it and
/// evaluate through a documented Actual/365 fallback instead of failing a posting.
/// </summary>
public enum DayCountConvention
{
    /// <summary>Actual calendar days over a 360-day year (money-market: CP, CD, repo, T-bills).</summary>
    Actual360,

    /// <summary>Actual calendar days over a 365-day year, ignoring leap days in the denominator (UK gilts, CAD/AUD money markets).</summary>
    Actual365,

    /// <summary>30/360 US "Bond Basis" per ISDA 2006 §4.16(f) (US corporates, munis, agencies).</summary>
    Thirty360,

    /// <summary>Business/252 — Brazilian convention; business days over 252. Needs a business-day calendar; without one it degrades to Actual/365.</summary>
    Business252,

    /// <summary>Actual/Actual (ISDA) per ISDA 2006 §4.16(b) — actual days per calendar year weighted by that year's length (US Treasuries, many sovereigns).</summary>
    ActualActualIsda,

    /// <summary>30E/360 "Eurobond Basis" per ISDA 2006 §4.16(g) — both D1 and D2 of 31 become 30 unconditionally.</summary>
    ThirtyE360,

    /// <summary>30E/360 (ISDA) per ISDA 2006 §4.16(h) — D1/D2 snap to 30 when they are the last day of their month, including February.</summary>
    ThirtyE360Isda,

    /// <summary>Actual/Actual (ICMA) per ISDA 2006 §4.16(c) — needs the coupon period; without period context it degrades to Actual/Actual (ISDA).</summary>
    ActualActualIcma,

    /// <summary>NL/365 (Actual/365 No-Leap) — actual days excluding any 29&#160;February, over 365.</summary>
    Nl365,

    /// <summary>Actual/365.25 — actual days over the mean Julian year; used by some analytics and insurance conventions.</summary>
    Actual36525,

    /// <summary>Unrecognized or absent convention; evaluated through the Actual/365 safe fallback.</summary>
    Unknown,
}

/// <summary>
/// The single canonical day-count engine for Meridian's fixed-income math. Every accrual, coupon,
/// premium/discount amortization, cost-basis relief, and read-model preview path MUST route through
/// this type so the same bond produces the same day-count fraction everywhere — otherwise GL postings
/// and cost-basis relief silently fail to tie. The F# <c>DayCount</c> module delegates here as well,
/// so there is exactly one 30/360 (and Actual/Actual) implementation across the C# and F# lanes.
/// </summary>
/// <remarks>
/// <para>Convention semantics follow the ISDA 2006 definitions, §4.16:</para>
/// <list type="bullet">
/// <item><description><b>30/360 US (Bond Basis, §4.16(f)):</b> D1 of 31 becomes 30; D2 of 31 becomes 30
/// only when D1 is (already) 30 or 31. No February end-of-month rule — that variant (30/360 US NASD/EOM)
/// is intentionally not applied, matching every existing Meridian call site.</description></item>
/// <item><description><b>30E/360 (Eurobond, §4.16(g)):</b> D1 and D2 of 31 both become 30, unconditionally.</description></item>
/// <item><description><b>30E/360 ISDA (§4.16(h)):</b> D1/D2 become 30 when they are the last day of their
/// month (February included). The §4.16(h) carve-out — D2 stays at February's true end when the end date is
/// the instrument's maturity — needs maturity context, so the dedicated
/// <see cref="ThirtyE360IsdaDays(DateOnly, DateOnly, bool)"/> overload exposes it.</description></item>
/// <item><description><b>Act/Act ICMA (§4.16(c)):</b> needs the enclosing coupon period and frequency;
/// use <see cref="ActualActualIcmaFraction"/>. The period-less <see cref="Fraction(DayCountConvention, DateOnly, DateOnly)"/>
/// dispatch degrades it to Act/Act ISDA rather than guessing a period.</description></item>
/// <item><description><b>Business/252:</b> needs a business-day calendar; use the
/// <see cref="Fraction(DayCountConvention, DateOnly, DateOnly, Func{DateOnly, bool})"/> overload with a
/// calendar predicate. Without one it degrades to Actual/365.</description></item>
/// </list>
/// <para>All arithmetic is <see cref="decimal"/> end-to-end; no <see cref="double"/> intermediates, so
/// fractions are exact and reproducible across runtimes.</para>
/// </remarks>
public static class DayCountConventions
{
    private const decimal DaysPerYear365 = 365m;
    private const decimal DaysPerYear360 = 360m;
    private const decimal DaysPerYear36525 = 365.25m;
    private const decimal BusinessDaysPerYear = 252m;

    // ── Parsing ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a raw convention string (e.g. "30/360", "30E/360", "ACT/365", "Act/Act ICMA", "NL/365",
    /// "BUS/252", "Thirty360", "ActualActualISDA") into a <see cref="DayCountConvention"/>. Never
    /// throws: blank or unrecognized input returns <see cref="DayCountConvention.Unknown"/>.
    /// </summary>
    public static DayCountConvention Parse(string? raw)
        => TryParse(raw, out var convention) ? convention : DayCountConvention.Unknown;

    /// <summary>
    /// Attempts to parse a raw convention string. Returns <see langword="true"/> when a specific
    /// convention was recognized; returns <see langword="false"/> and yields
    /// <see cref="DayCountConvention.Unknown"/> for blank or unrecognized input.
    /// </summary>
    public static bool TryParse(string? raw, out DayCountConvention convention)
    {
        var normalized = NormalizeToken(raw);
        if (normalized.Length == 0)
        {
            convention = DayCountConvention.Unknown;
            return false;
        }

        // Order matters throughout: Actual/Actual before the plain "360"/"365" checks so "ACTACT"
        // variants are not misread as an Actual/360 or Actual/365 basis; "365.25" and "NL/365"
        // before the generic 365; 30E before the generic 360.
        if (normalized.Contains("ACTACT", StringComparison.Ordinal) ||
            normalized.Contains("ACTUALACTUAL", StringComparison.Ordinal))
        {
            convention = normalized.Contains("ICMA", StringComparison.Ordinal) ||
                         normalized.Contains("ISMA", StringComparison.Ordinal)
                ? DayCountConvention.ActualActualIcma
                : DayCountConvention.ActualActualIsda;
            return true;
        }

        if (normalized.Contains("36525", StringComparison.Ordinal))
        {
            convention = DayCountConvention.Actual36525;
            return true;
        }

        if (normalized.Contains("252", StringComparison.Ordinal))
        {
            convention = DayCountConvention.Business252;
            return true;
        }

        if (normalized.Contains("NL365", StringComparison.Ordinal) ||
            normalized.Contains("365NL", StringComparison.Ordinal) ||
            normalized.Contains("NOLEAP", StringComparison.Ordinal))
        {
            convention = DayCountConvention.Nl365;
            return true;
        }

        if (normalized.Contains("30E360", StringComparison.Ordinal) ||
            normalized.Contains("EUROBOND", StringComparison.Ordinal) ||
            normalized.Contains("GERMAN", StringComparison.Ordinal))
        {
            convention = normalized.Contains("ISDA", StringComparison.Ordinal) ||
                         normalized.Contains("GERMAN", StringComparison.Ordinal)
                ? DayCountConvention.ThirtyE360Isda
                : DayCountConvention.ThirtyE360;
            return true;
        }

        if (normalized.Contains("360", StringComparison.Ordinal))
        {
            // "ACT360"/"ACTUAL360" => Actual/360; everything else with a 360 (e.g. "30360",
            // "THIRTY360", "BONDBASIS360") => 30/360 US.
            convention = normalized.Contains("ACT", StringComparison.Ordinal)
                ? DayCountConvention.Actual360
                : DayCountConvention.Thirty360;
            return true;
        }

        if (normalized.Contains("365", StringComparison.Ordinal))
        {
            convention = DayCountConvention.Actual365;
            return true;
        }

        if (normalized.Contains("THIRTY", StringComparison.Ordinal) ||
            normalized.Contains("BONDBASIS", StringComparison.Ordinal))
        {
            convention = DayCountConvention.Thirty360;
            return true;
        }

        convention = DayCountConvention.Unknown;
        return false;
    }

    // ── Fractions ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The day-count fraction of a year between <paramref name="start"/> (inclusive) and
    /// <paramref name="end"/> (exclusive) under the given convention. Returns 0 when
    /// <paramref name="end"/> is not after <paramref name="start"/>. Conventions that need extra
    /// context degrade explicitly: <see cref="DayCountConvention.ActualActualIcma"/> evaluates as
    /// Act/Act ISDA (use <see cref="ActualActualIcmaFraction"/> for exact ICMA) and
    /// <see cref="DayCountConvention.Business252"/> evaluates as Actual/365 (use the calendar overload).
    /// </summary>
    public static decimal Fraction(DayCountConvention convention, DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            return 0m;
        }

        return convention switch
        {
            DayCountConvention.Thirty360 => Thirty360Days(start, end) / DaysPerYear360,
            DayCountConvention.ThirtyE360 => ThirtyE360Days(start, end) / DaysPerYear360,
            DayCountConvention.ThirtyE360Isda => ThirtyE360IsdaDays(start, end) / DaysPerYear360,
            DayCountConvention.Actual360 => ActualDays(start, end) / DaysPerYear360,
            DayCountConvention.Actual365 => ActualDays(start, end) / DaysPerYear365,
            DayCountConvention.Nl365 => Nl365Days(start, end) / DaysPerYear365,
            DayCountConvention.Actual36525 => ActualDays(start, end) / DaysPerYear36525,
            DayCountConvention.ActualActualIsda => ActualActualIsda(start, end),
            DayCountConvention.ActualActualIcma => ActualActualIsda(start, end),
            // Business/252 without a calendar, and Unknown, both fall back to Actual/365.
            _ => ActualDays(start, end) / DaysPerYear365,
        };
    }

    /// <summary>Parses <paramref name="raw"/> and returns the day-count fraction for it.</summary>
    public static decimal Fraction(string? raw, DateOnly start, DateOnly end)
        => Fraction(Parse(raw), start, end);

    /// <summary>
    /// Calendar-aware fraction: identical to <see cref="Fraction(DayCountConvention, DateOnly, DateOnly)"/>
    /// except that <see cref="DayCountConvention.Business252"/> counts days in [start, end) satisfying
    /// <paramref name="isBusinessDay"/> over 252 instead of degrading to Actual/365.
    /// </summary>
    public static decimal Fraction(DayCountConvention convention, DateOnly start, DateOnly end, Func<DateOnly, bool> isBusinessDay)
    {
        ArgumentNullException.ThrowIfNull(isBusinessDay);
        if (end <= start)
        {
            return 0m;
        }

        return convention == DayCountConvention.Business252
            ? Business252Days(start, end, isBusinessDay) / BusinessDaysPerYear
            : Fraction(convention, start, end);
    }

    /// <summary>
    /// Actual/Actual (ICMA) per ISDA 2006 §4.16(c) for an accrual window inside a single coupon
    /// period: actual days accrued over (<paramref name="couponsPerYear"/> × actual days in the
    /// period [<paramref name="periodStart"/>, <paramref name="periodEnd"/>)). This is the exact US
    /// Treasury / ICMA bond accrued-interest convention; the period-less dispatch cannot express it.
    /// </summary>
    public static decimal ActualActualIcmaFraction(
        DateOnly start,
        DateOnly end,
        DateOnly periodStart,
        DateOnly periodEnd,
        int couponsPerYear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(couponsPerYear, 0);
        if (periodEnd <= periodStart)
        {
            throw new ArgumentException("Coupon period end must follow coupon period start.", nameof(periodEnd));
        }

        if (end <= start)
        {
            return 0m;
        }

        return (decimal)ActualDays(start, end) / (couponsPerYear * (decimal)ActualDays(periodStart, periodEnd));
    }

    // ── Day counts ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The numerator day count consistent with <see cref="Fraction(DayCountConvention, DateOnly, DateOnly)"/>:
    /// the convention's 30/360 count for the 30/360 family, leap-day-excluded actual days for
    /// <see cref="DayCountConvention.Nl365"/>, otherwise actual calendar days.
    /// </summary>
    public static int Days(DayCountConvention convention, DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            return 0;
        }

        return convention switch
        {
            DayCountConvention.Thirty360 => Thirty360Days(start, end),
            DayCountConvention.ThirtyE360 => ThirtyE360Days(start, end),
            DayCountConvention.ThirtyE360Isda => ThirtyE360IsdaDays(start, end),
            DayCountConvention.Nl365 => Nl365Days(start, end),
            _ => ActualDays(start, end),
        };
    }

    /// <summary>Parses <paramref name="raw"/> and returns the numerator day count for it.</summary>
    public static int Days(string? raw, DateOnly start, DateOnly end)
        => Days(Parse(raw), start, end);

    /// <summary>
    /// 30/360 US "Bond Basis" day count (ISDA 2006 §4.16(f)): D1 of 31 becomes 30; D2 of 31 becomes 30
    /// only when D1 is (already) 30 or 31. Returns 0 when <paramref name="end"/> is not after
    /// <paramref name="start"/>.
    /// </summary>
    public static int Thirty360Days(DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            return 0;
        }

        var d1 = Math.Min(start.Day, 30);
        var d2 = start.Day >= 30 ? Math.Min(end.Day, 30) : end.Day;
        return ThirtyDayMonths(start, end, d1, d2);
    }

    /// <summary>
    /// 30E/360 "Eurobond Basis" day count (ISDA 2006 §4.16(g)): D1 and D2 of 31 both become 30,
    /// unconditionally. Returns 0 when <paramref name="end"/> is not after <paramref name="start"/>.
    /// </summary>
    public static int ThirtyE360Days(DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            return 0;
        }

        return ThirtyDayMonths(start, end, Math.Min(start.Day, 30), Math.Min(end.Day, 30));
    }

    /// <summary>
    /// 30E/360 (ISDA) day count (ISDA 2006 §4.16(h)): D1 and D2 become 30 when they are the last day
    /// of their month, February included — except that when <paramref name="endIsMaturityDate"/> is
    /// <see langword="true"/> and the end date is the last day of February, D2 keeps February's true
    /// day per the §4.16(h) termination-date carve-out. Returns 0 when <paramref name="end"/> is not
    /// after <paramref name="start"/>.
    /// </summary>
    public static int ThirtyE360IsdaDays(DateOnly start, DateOnly end, bool endIsMaturityDate = false)
    {
        if (end <= start)
        {
            return 0;
        }

        var d1 = IsLastDayOfMonth(start) ? 30 : start.Day;
        var d2 = IsLastDayOfMonth(end) && !(endIsMaturityDate && end.Month == 2) ? 30 : end.Day;
        return ThirtyDayMonths(start, end, d1, d2);
    }

    /// <summary>
    /// NL/365 day count: actual calendar days between <paramref name="start"/> and
    /// <paramref name="end"/> minus any 29&#160;February falling in (<paramref name="start"/>,
    /// <paramref name="end"/>]. Returns 0 when <paramref name="end"/> is not after <paramref name="start"/>.
    /// </summary>
    public static int Nl365Days(DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            return 0;
        }

        return ActualDays(start, end) - CountLeapDays(start, end);
    }

    /// <summary>
    /// Business/252 day count: the number of days in [<paramref name="start"/>, <paramref name="end"/>)
    /// satisfying <paramref name="isBusinessDay"/>. Returns 0 when <paramref name="end"/> is not after
    /// <paramref name="start"/>.
    /// </summary>
    public static int Business252Days(DateOnly start, DateOnly end, Func<DateOnly, bool> isBusinessDay)
    {
        ArgumentNullException.ThrowIfNull(isBusinessDay);
        if (end <= start)
        {
            return 0;
        }

        var count = 0;
        for (var day = start; day < end; day = day.AddDays(1))
        {
            if (isBusinessDay(day))
            {
                count++;
            }
        }

        return count;
    }

    // ── Internals ───────────────────────────────────────────────────────────────────────────────

    private static int ThirtyDayMonths(DateOnly start, DateOnly end, int d1, int d2)
        => ((end.Year - start.Year) * 360) + ((end.Month - start.Month) * 30) + (d2 - d1);

    private static int ActualDays(DateOnly start, DateOnly end)
        => end.DayNumber - start.DayNumber;

    private static bool IsLastDayOfMonth(DateOnly date)
        => date.Day == DateTime.DaysInMonth(date.Year, date.Month);

    private static int CountLeapDays(DateOnly start, DateOnly end)
    {
        // Count 29 Februarys in (start, end] — the OpenGamma/market NL/365 window.
        var count = 0;
        for (var year = start.Year; year <= end.Year; year++)
        {
            if (!DateTime.IsLeapYear(year))
            {
                continue;
            }

            var leapDay = new DateOnly(year, 2, 29);
            if (leapDay > start && leapDay <= end)
            {
                count++;
            }
        }

        return count;
    }

    private static decimal ActualActualIsda(DateOnly start, DateOnly end)
    {
        // Pure decimal arithmetic — no double intermediates — so results are exact and identical to
        // the original F# DayCount implementation this engine canonicalized.
        if (start.Year == end.Year)
        {
            return ActualDays(start, end) / DaysInYear(start.Year);
        }

        var firstYearEnd = new DateOnly(start.Year + 1, 1, 1);
        var lastYearStart = new DateOnly(end.Year, 1, 1);
        var firstFraction = ActualDays(start, firstYearEnd) / DaysInYear(start.Year);
        var lastFraction = ActualDays(lastYearStart, end) / DaysInYear(end.Year);
        var fullYears = end.Year - start.Year - 1;
        return firstFraction + fullYears + lastFraction;
    }

    private static decimal DaysInYear(int year)
        => DateTime.IsLeapYear(year) ? 366m : 365m;

    private static string NormalizeToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        // Keep only alphanumerics, uppercased, so "30/360", "ACT / 360", "act-360", "30E/360.ISDA",
        // and "Thirty_360" all normalize to a slash/space/dash/dot-free comparable token.
        var buffer = new char[raw.Length];
        var length = 0;
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length++] = char.ToUpperInvariant(ch);
            }
        }

        return new string(buffer, 0, length);
    }
}
