using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Provides US market trading calendar functionality including:
/// - Market holidays (NYSE/NASDAQ)
/// - Half-day trading sessions
/// - Regular, pre-market, and after-hours session times
/// - Timezone-aware market status queries
/// </summary>
public sealed class TradingCalendar
{
    private readonly ILogger _log = LoggingSetup.ForContext<TradingCalendar>();
    private readonly TimeZoneInfo _easternTimeZone;
    private readonly HashSet<DateOnly> _holidays;
    private readonly HashSet<DateOnly> _halfDays;

    /// <summary>
    /// Pre-market session start time (4:00 AM ET).
    /// </summary>
    public static readonly TimeOnly PreMarketOpen = new(4, 0);

    /// <summary>
    /// Regular session start time (9:30 AM ET).
    /// </summary>
    public static readonly TimeOnly RegularMarketOpen = new(9, 30);

    /// <summary>
    /// Regular session end time (4:00 PM ET).
    /// </summary>
    public static readonly TimeOnly RegularMarketClose = new(16, 0);

    /// <summary>
    /// Half-day session end time (1:00 PM ET).
    /// </summary>
    public static readonly TimeOnly HalfDayClose = new(13, 0);

    /// <summary>
    /// After-hours session end time (8:00 PM ET).
    /// </summary>
    public static readonly TimeOnly AfterHoursClose = new(20, 0);

    /// <summary>
    /// Creates a new trading calendar with US market holidays.
    /// </summary>
    public TradingCalendar()
    {
        _easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById(GetEasternTimeZoneId());
        _holidays = GenerateHolidays(DateTime.UtcNow.Year - 1, DateTime.UtcNow.Year + 2);
        _halfDays = GenerateHalfDays(DateTime.UtcNow.Year - 1, DateTime.UtcNow.Year + 2);

        _log.Information("TradingCalendar initialized with {HolidayCount} holidays and {HalfDayCount} half-days",
            _holidays.Count, _halfDays.Count);
    }

    /// <summary>
    /// Gets the current market status.
    /// </summary>
    public MarketStatus GetCurrentStatus()
    {
        return GetStatusAt(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Gets the market status at a specific time.
    /// </summary>
    public MarketStatus GetStatusAt(DateTimeOffset utcTime)
    {
        var easternTime = TimeZoneInfo.ConvertTime(utcTime, _easternTimeZone);
        var date = DateOnly.FromDateTime(easternTime.DateTime);
        var time = TimeOnly.FromDateTime(easternTime.DateTime);

        // Check if it's a weekend
        if (easternTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return new MarketStatus(
                State: MarketState.Closed,
                Reason: "Weekend",
                CurrentSessionStart: null,
                CurrentSessionEnd: null,
                NextSessionStart: GetNextTradingDayOpen(date),
                IsHalfDay: false,
                EasternTime: easternTime
            );
        }

        // Check if it's a holiday
        if (_holidays.Contains(date))
        {
            return new MarketStatus(
                State: MarketState.Closed,
                Reason: GetHolidayName(date),
                CurrentSessionStart: null,
                CurrentSessionEnd: null,
                NextSessionStart: GetNextTradingDayOpen(date),
                IsHalfDay: false,
                EasternTime: easternTime
            );
        }

        var isHalfDay = _halfDays.Contains(date);
        var closeTime = isHalfDay ? HalfDayClose : RegularMarketClose;

        // Determine session
        if (time < PreMarketOpen)
        {
            return new MarketStatus(
                State: MarketState.Closed,
                Reason: "Before pre-market",
                CurrentSessionStart: null,
                CurrentSessionEnd: null,
                NextSessionStart: CombineDateTime(date, PreMarketOpen, _easternTimeZone),
                IsHalfDay: isHalfDay,
                EasternTime: easternTime
            );
        }

        if (time >= PreMarketOpen && time < RegularMarketOpen)
        {
            return new MarketStatus(
                State: MarketState.PreMarket,
                Reason: "Pre-market session",
                CurrentSessionStart: CombineDateTime(date, PreMarketOpen, _easternTimeZone),
                CurrentSessionEnd: CombineDateTime(date, RegularMarketOpen, _easternTimeZone),
                NextSessionStart: CombineDateTime(date, RegularMarketOpen, _easternTimeZone),
                IsHalfDay: isHalfDay,
                EasternTime: easternTime
            );
        }

        if (time >= RegularMarketOpen && time < closeTime)
        {
            return new MarketStatus(
                State: MarketState.Open,
                Reason: "Regular trading session",
                CurrentSessionStart: CombineDateTime(date, RegularMarketOpen, _easternTimeZone),
                CurrentSessionEnd: CombineDateTime(date, closeTime, _easternTimeZone),
                NextSessionStart: null,
                IsHalfDay: isHalfDay,
                EasternTime: easternTime
            );
        }

        // After regular close
        if (isHalfDay)
        {
            // No after-hours on half days
            return new MarketStatus(
                State: MarketState.Closed,
                Reason: "Half-day close",
                CurrentSessionStart: null,
                CurrentSessionEnd: null,
                NextSessionStart: GetNextTradingDayOpen(date),
                IsHalfDay: true,
                EasternTime: easternTime
            );
        }

        if (time >= closeTime && time < AfterHoursClose)
        {
            return new MarketStatus(
                State: MarketState.AfterHours,
                Reason: "After-hours session",
                CurrentSessionStart: CombineDateTime(date, RegularMarketClose, _easternTimeZone),
                CurrentSessionEnd: CombineDateTime(date, AfterHoursClose, _easternTimeZone),
                NextSessionStart: GetNextTradingDayOpen(date),
                IsHalfDay: false,
                EasternTime: easternTime
            );
        }

        // After 8 PM ET
        return new MarketStatus(
            State: MarketState.Closed,
            Reason: "After market hours",
            CurrentSessionStart: null,
            CurrentSessionEnd: null,
            NextSessionStart: GetNextTradingDayOpen(date),
            IsHalfDay: false,
            EasternTime: easternTime
        );
    }

    /// <summary>
    /// Checks if a specific date is a trading day.
    /// </summary>
    public bool IsTradingDay(DateOnly date)
    {
        var dayOfWeek = date.DayOfWeek;
        if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        return !_holidays.Contains(date);
    }

    /// <summary>
    /// Checks if a specific date is a half trading day.
    /// </summary>
    public bool IsHalfDay(DateOnly date) => _halfDays.Contains(date);

    /// <summary>
    /// Checks if today is a trading day.
    /// </summary>
    public bool IsTodayTradingDay()
    {
        var easternNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _easternTimeZone);
        return IsTradingDay(DateOnly.FromDateTime(easternNow.DateTime));
    }

    /// <summary>
    /// Gets the next trading day from a given date.
    /// </summary>
    public DateOnly GetNextTradingDay(DateOnly fromDate)
    {
        var nextDate = fromDate.AddDays(1);
        while (!IsTradingDay(nextDate))
        {
            nextDate = nextDate.AddDays(1);
        }
        return nextDate;
    }

    /// <summary>
    /// Gets the previous trading day from a given date.
    /// </summary>
    public DateOnly GetPreviousTradingDay(DateOnly fromDate)
    {
        var prevDate = fromDate.AddDays(-1);
        while (!IsTradingDay(prevDate))
        {
            prevDate = prevDate.AddDays(-1);
        }
        return prevDate;
    }

    /// <summary>
    /// Gets a list of trading days within a date range.
    /// </summary>
    public IReadOnlyList<DateOnly> GetTradingDays(DateOnly startDate, DateOnly endDate)
    {
        var tradingDays = new List<DateOnly>();
        var current = startDate;
        while (current <= endDate)
        {
            if (IsTradingDay(current))
                tradingDays.Add(current);
            current = current.AddDays(1);
        }
        return tradingDays;
    }

    /// <summary>
    /// Gets the count of trading days within a date range.
    /// </summary>
    public int GetTradingDayCount(DateOnly startDate, DateOnly endDate)
    {
        return GetTradingDays(startDate, endDate).Count;
    }

    /// <summary>
    /// Gets the time until market open (regular session).
    /// Returns null if market is currently open.
    /// </summary>
    public TimeSpan? GetTimeUntilOpen()
    {
        var status = GetCurrentStatus();
        if (status.State == MarketState.Open)
            return null;

        if (status.NextSessionStart.HasValue)
            return status.NextSessionStart.Value - DateTimeOffset.UtcNow;

        return null;
    }

    /// <summary>
    /// Gets the time until market close.
    /// Returns null if market is currently closed.
    /// </summary>
    public TimeSpan? GetTimeUntilClose()
    {
        var status = GetCurrentStatus();
        if (status.State is MarketState.Open or MarketState.PreMarket or MarketState.AfterHours)
        {
            if (status.CurrentSessionEnd.HasValue)
                return status.CurrentSessionEnd.Value - DateTimeOffset.UtcNow;
        }

        return null;
    }

    /// <summary>
    /// Gets all holidays for a given year.
    /// </summary>
    public IReadOnlyList<MarketHoliday> GetHolidays(int year)
    {
        // Check if we have holidays for this year, if not, generate them
        if (!_holidays.Any(d => d.Year == year))
        {
            var yearHolidays = GenerateHolidays(year, year);
            foreach (var h in yearHolidays)
            {
                _holidays.Add(h);
            }
        }

        return _holidays
            .Where(d => d.Year == year)
            .Select(d => new MarketHoliday(d, GetHolidayName(d)))
            .OrderBy(h => h.Date)
            .ToList();
    }

    /// <summary>
    /// Gets all half-days for a given year.
    /// </summary>
    public IReadOnlyList<DateOnly> GetHalfDays(int year)
    {
        return _halfDays.Where(d => d.Year == year).OrderBy(d => d).ToList();
    }

    private DateTimeOffset GetNextTradingDayOpen(DateOnly currentDate)
    {
        var nextTradingDay = GetNextTradingDay(currentDate);
        return CombineDateTime(nextTradingDay, PreMarketOpen, _easternTimeZone);
    }

    private static DateTimeOffset CombineDateTime(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        var dt = date.ToDateTime(time);
        return new DateTimeOffset(dt, tz.GetUtcOffset(dt));
    }

    private static string GetEasternTimeZoneId()
    {
        // Handle cross-platform timezone ID differences
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York").Id;
        }
        catch
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").Id;
        }
    }

    /// <summary>
    /// Generates US market holidays for a range of years.
    /// NYSE/NASDAQ observe the same holidays.
    /// </summary>
    private static HashSet<DateOnly> GenerateHolidays(int startYear, int endYear)
    {
        var holidays = new HashSet<DateOnly>();

        for (var year = startYear; year <= endYear; year++)
        {
            // New Year's Day (Jan 1, observed)
            holidays.Add(GetObservedHoliday(new DateOnly(year, 1, 1)));

            // Martin Luther King Jr. Day (3rd Monday of January)
            holidays.Add(GetNthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3));

            // Presidents' Day (3rd Monday of February)
            holidays.Add(GetNthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3));

            // Good Friday (Friday before Easter Sunday)
            holidays.Add(GetGoodFriday(year));

            // Memorial Day (Last Monday of May)
            holidays.Add(GetLastWeekdayOfMonth(year, 5, DayOfWeek.Monday));

            // Juneteenth (June 19, observed) - NYSE began observing in 2022
            if (year >= 2022)
            {
                holidays.Add(GetObservedHoliday(new DateOnly(year, 6, 19)));
            }

            // Independence Day (July 4, observed)
            holidays.Add(GetObservedHoliday(new DateOnly(year, 7, 4)));

            // Labor Day (1st Monday of September)
            holidays.Add(GetNthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1));

            // Thanksgiving Day (4th Thursday of November)
            holidays.Add(GetNthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4));

            // Christmas Day (Dec 25, observed)
            holidays.Add(GetObservedHoliday(new DateOnly(year, 12, 25)));
        }

        return holidays;
    }

    /// <summary>
    /// Generates half trading days for a range of years.
    /// Markets close at 1:00 PM ET on these days.
    /// </summary>
    private static HashSet<DateOnly> GenerateHalfDays(int startYear, int endYear)
    {
        var halfDays = new HashSet<DateOnly>();

        for (var year = startYear; year <= endYear; year++)
        {
            // Day after Thanksgiving (Black Friday) - early close at 1 PM
            var thanksgiving = GetNthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4);
            halfDays.Add(thanksgiving.AddDays(1));

            // Christmas Eve (Dec 24) - if it falls on a weekday
            var christmasEve = new DateOnly(year, 12, 24);
            if (christmasEve.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                halfDays.Add(christmasEve);
            }

            // July 3rd - if July 4th falls on a weekday (excluding Monday)
            var july4 = new DateOnly(year, 7, 4);
            if (july4.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday or DayOfWeek.Friday)
            {
                halfDays.Add(new DateOnly(year, 7, 3));
            }
        }

        return halfDays;
    }

    /// <summary>
    /// Gets the observed holiday date (moves weekend holidays to Friday/Monday).
    /// </summary>
    private static DateOnly GetObservedHoliday(DateOnly actualDate)
    {
        return actualDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => actualDate.AddDays(-1), // Friday
            DayOfWeek.Sunday => actualDate.AddDays(1),    // Monday
            _ => actualDate
        };
    }

    /// <summary>
    /// Gets the Nth weekday of a month (e.g., 3rd Monday of January).
    /// </summary>
    private static DateOnly GetNthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int n)
    {
        var firstOfMonth = new DateOnly(year, month, 1);
        var firstDayOfWeek = firstOfMonth;

        while (firstDayOfWeek.DayOfWeek != dayOfWeek)
        {
            firstDayOfWeek = firstDayOfWeek.AddDays(1);
        }

        return firstDayOfWeek.AddDays(7 * (n - 1));
    }

    /// <summary>
    /// Gets the last weekday of a month (e.g., last Monday of May).
    /// </summary>
    private static DateOnly GetLastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var lastOfMonth = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        while (lastOfMonth.DayOfWeek != dayOfWeek)
        {
            lastOfMonth = lastOfMonth.AddDays(-1);
        }

        return lastOfMonth;
    }

    /// <summary>
    /// Calculates Good Friday (Friday before Easter Sunday).
    /// Uses the Anonymous Gregorian algorithm for Easter calculation.
    /// </summary>
    private static DateOnly GetGoodFriday(int year)
    {
        var easter = CalculateEaster(year);
        return easter.AddDays(-2);
    }

    /// <summary>
    /// Calculates Easter Sunday using the Anonymous Gregorian algorithm.
    /// </summary>
    private static DateOnly CalculateEaster(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;

        return new DateOnly(year, month, day);
    }

    private static string GetHolidayName(DateOnly date)
    {
        var month = date.Month;
        var day = date.Day;

        // Check specific dates first
        if (month == 1 && day <= 2)
            return "New Year's Day";
        if (month == 6 && day >= 18 && day <= 20)
            return "Juneteenth";
        if (month == 7 && day >= 3 && day <= 5)
            return "Independence Day";
        if (month == 12 && day >= 24 && day <= 26)
            return "Christmas Day";

        // Check by month and weekday patterns
        if (month == 1 && date.DayOfWeek == DayOfWeek.Monday)
            return "Martin Luther King Jr. Day";
        if (month == 2 && date.DayOfWeek == DayOfWeek.Monday)
            return "Presidents' Day";

        // Good Friday - calculate precisely for the year
        if ((month == 3 || month == 4) && date.DayOfWeek == DayOfWeek.Friday)
        {
            var goodFriday = GetGoodFriday(date.Year);
            if (date == goodFriday)
                return "Good Friday";
        }

        if (month == 5 && date.DayOfWeek == DayOfWeek.Monday)
            return "Memorial Day";
        if (month == 9 && date.DayOfWeek == DayOfWeek.Monday)
            return "Labor Day";
        if (month == 11 && date.DayOfWeek == DayOfWeek.Thursday)
            return "Thanksgiving Day";

        return "Market Holiday";
    }
}

/// <summary>
/// Current market status information.
/// </summary>
public readonly record struct MarketStatus(
    MarketState State,
    string Reason,
    DateTimeOffset? CurrentSessionStart,
    DateTimeOffset? CurrentSessionEnd,
    DateTimeOffset? NextSessionStart,
    bool IsHalfDay,
    DateTimeOffset EasternTime
)
{
    /// <summary>
    /// Returns true if regular trading is currently active.
    /// </summary>
    public bool IsRegularTradingHours => State == MarketState.Open;

    /// <summary>
    /// Returns true if any trading session is active (pre-market, regular, or after-hours).
    /// </summary>
    public bool IsAnySessionActive => State is MarketState.PreMarket or MarketState.Open or MarketState.AfterHours;

    /// <summary>
    /// Returns the time remaining in the current session, or null if no session is active.
    /// </summary>
    public TimeSpan? TimeRemainingInSession =>
        CurrentSessionEnd.HasValue ? CurrentSessionEnd.Value - DateTimeOffset.UtcNow : null;

    public override string ToString()
    {
        var halfDayIndicator = IsHalfDay ? " (half-day)" : "";
        return $"{State}: {Reason}{halfDayIndicator}";
    }
}

/// <summary>
/// Market trading state.
/// </summary>
public enum MarketState : byte
{
    /// <summary>Market is closed.</summary>
    Closed,

    /// <summary>Pre-market session (4:00 AM - 9:30 AM ET).</summary>
    PreMarket,

    /// <summary>Regular trading session (9:30 AM - 4:00 PM ET).</summary>
    Open,

    /// <summary>After-hours session (4:00 PM - 8:00 PM ET).</summary>
    AfterHours
}

/// <summary>
/// Market holiday information.
/// </summary>
public readonly record struct MarketHoliday(DateOnly Date, string Name);
