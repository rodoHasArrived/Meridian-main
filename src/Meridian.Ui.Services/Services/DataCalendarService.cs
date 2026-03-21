namespace Meridian.Ui.Services;

/// <summary>
/// Service for data completeness calendar visualization.
/// Provides heatmap data for viewing data coverage across time.
/// </summary>
public sealed class DataCalendarService
{
    private readonly DataCompletenessService _completenessService;
    private readonly StorageAnalyticsService _storageService;

    public DataCalendarService()
    {
        var tradingCalendar = new TradingCalendarService();
        var manifestService = ManifestService.Instance;
        _completenessService = new DataCompletenessService(manifestService, tradingCalendar);
        _storageService = StorageAnalyticsService.Instance;
    }

    public DataCalendarService(DataCompletenessService completenessService)
    {
        _completenessService = completenessService ?? throw new ArgumentNullException(nameof(completenessService));
        _storageService = StorageAnalyticsService.Instance;
    }

    /// <summary>
    /// Gets calendar heatmap data for a year.
    /// </summary>
    public async Task<CalendarYearData> GetYearCalendarAsync(
        int year,
        string[]? symbols = null,
        CancellationToken ct = default)
    {
        var calendar = new CalendarYearData { Year = year };

        // Get all months
        for (int month = 1; month <= 12; month++)
        {
            var monthData = await GetMonthCalendarAsync(year, month, symbols, ct);
            calendar.Months.Add(monthData);
        }

        // Calculate year statistics
        calendar.TotalTradingDays = calendar.Months.Sum(m => m.TradingDays);
        calendar.DaysWithData = calendar.Months.Sum(m => m.DaysWithData);
        calendar.TotalGaps = calendar.Months.Sum(m => m.GapCount);
        calendar.OverallCompleteness = calendar.TotalTradingDays > 0
            ? (double)calendar.DaysWithData / calendar.TotalTradingDays * 100
            : 0;

        return calendar;
    }

    /// <summary>
    /// Gets calendar heatmap data for a month.
    /// </summary>
    public async Task<CalendarMonthData> GetMonthCalendarAsync(
        int year,
        int month,
        string[]? symbols = null,
        CancellationToken ct = default)
    {
        var monthData = new CalendarMonthData
        {
            Year = year,
            Month = month,
            MonthName = new DateTime(year, month, 1).ToString("MMMM")
        };

        var daysInMonth = DateTime.DaysInMonth(year, month);

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            var dayData = await GetDayDataAsync(date, symbols, ct);
            monthData.Days.Add(dayData);

            if (dayData.IsTradingDay)
            {
                monthData.TradingDays++;
                if (dayData.HasData)
                    monthData.DaysWithData++;
                if (dayData.HasGaps)
                    monthData.GapCount++;
            }
        }

        monthData.Completeness = monthData.TradingDays > 0
            ? (double)monthData.DaysWithData / monthData.TradingDays * 100
            : 100;

        return monthData;
    }

    /// <summary>
    /// Gets detailed data for a single day.
    /// </summary>
    public async Task<CalendarDayData> GetDayDataAsync(
        DateOnly date,
        string[]? symbols = null,
        CancellationToken ct = default)
    {
        var dayData = new CalendarDayData
        {
            Date = date,
            DayOfWeek = date.DayOfWeek,
            IsWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday,
            IsHoliday = IsMarketHoliday(date),
            IsTradingDay = IsTradingDay(date)
        };

        if (!dayData.IsTradingDay)
        {
            dayData.CompletenessLevel = CompletenessLevel.NonTrading;
            return dayData;
        }

        // Get completeness data for this day
        var report = await _completenessService.GetDayCompletenessAsync(date, symbols, ct);

        dayData.HasData = report.HasData;
        dayData.Completeness = report.Completeness;
        dayData.EventCount = report.EventCount;
        dayData.ExpectedEvents = report.ExpectedEvents;
        dayData.HasGaps = report.GapCount > 0;
        dayData.GapCount = report.GapCount;

        // Determine completeness level for heatmap coloring
        dayData.CompletenessLevel = dayData.Completeness switch
        {
            >= 99 => CompletenessLevel.Complete,
            >= 95 => CompletenessLevel.Good,
            >= 80 => CompletenessLevel.Partial,
            >= 50 => CompletenessLevel.Poor,
            > 0 => CompletenessLevel.Minimal,
            _ => CompletenessLevel.Missing
        };

        // Get per-symbol breakdown
        if (symbols != null)
        {
            foreach (var symbol in symbols)
            {
                var symbolReport = await _completenessService.GetSymbolDayCompletenessAsync(
                    symbol, date, ct);

                dayData.SymbolBreakdown.Add(new SymbolDayData
                {
                    Symbol = symbol,
                    HasData = symbolReport.HasData,
                    Completeness = symbolReport.Completeness,
                    EventCount = symbolReport.EventCount,
                    HasGaps = symbolReport.GapCount > 0
                });
            }
        }

        return dayData;
    }

    /// <summary>
    /// Gets symbol-date matrix for coverage visualization.
    /// </summary>
    public async Task<CoverageMatrixData> GetCoverageMatrixAsync(
        string[] symbols,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default)
    {
        var matrix = new CoverageMatrixData
        {
            FromDate = fromDate,
            ToDate = toDate
        };

        // Generate date range
        var dates = new List<DateOnly>();
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            if (IsTradingDay(date))
                dates.Add(date);
        }

        matrix.Dates = dates;

        // Get coverage for each symbol
        foreach (var symbol in symbols)
        {
            var symbolCoverage = new SymbolCoverageData { Symbol = symbol };

            foreach (var date in dates)
            {
                var dayReport = await _completenessService.GetSymbolDayCompletenessAsync(
                    symbol, date, ct);

                symbolCoverage.DayCoverage.Add(new DayCoverageInfo
                {
                    Date = date,
                    HasData = dayReport.HasData,
                    Completeness = dayReport.Completeness
                });
            }

            symbolCoverage.OverallCompleteness = symbolCoverage.DayCoverage.Count > 0
                ? symbolCoverage.DayCoverage.Average(d => d.Completeness)
                : 0;
            symbolCoverage.DaysWithData = symbolCoverage.DayCoverage.Count(d => d.HasData);
            symbolCoverage.TotalDays = dates.Count;

            matrix.Symbols.Add(symbolCoverage);
        }

        return matrix;
    }

    /// <summary>
    /// Gets gap summary for date range.
    /// </summary>
    public async Task<GapSummaryData> GetGapSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        string[]? symbols = null,
        CancellationToken ct = default)
    {
        var summary = new GapSummaryData
        {
            FromDate = fromDate,
            ToDate = toDate
        };

        var report = await _completenessService.GetCompletenessReportAsync(
            symbols ?? Array.Empty<string>(), fromDate, toDate, ct);

        summary.TotalGaps = report.GapCount;
        summary.TotalTradingDays = report.TotalTradingDays;
        summary.DaysWithGaps = report.DaysWithGaps;
        summary.OverallCompleteness = report.OverallCompleteness;

        // Get individual gaps
        var gaps = await _completenessService.GetGapsAsync(symbols, fromDate, toDate, ct);

        foreach (var gap in gaps)
        {
            summary.Gaps.Add(new GapInfo
            {
                Symbol = gap.Symbol,
                StartDate = gap.StartDate,
                EndDate = gap.EndDate,
                GapType = gap.GapType.ToString(),
                ExpectedEvents = gap.ExpectedEvents,
                ActualEvents = gap.ActualEvents,
                CanRepair = gap.CanRepair
            });
        }

        // Group gaps by type
        summary.GapsByType = summary.Gaps
            .GroupBy(g => g.GapType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Group gaps by symbol
        summary.GapsBySymbol = summary.Gaps
            .GroupBy(g => g.Symbol)
            .ToDictionary(g => g.Key, g => g.Count());

        return summary;
    }

    /// <summary>
    /// Initiates gap repair for specified gaps.
    /// </summary>
    public async Task<GapRepairResult> RepairGapsAsync(
        IEnumerable<GapInfo> gaps,
        IProgress<GapRepairProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new GapRepairResult();
        var gapList = gaps.ToList();
        var processed = 0;

        foreach (var gap in gapList)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new GapRepairProgress
            {
                CurrentSymbol = gap.Symbol,
                CurrentDate = gap.StartDate,
                ProcessedGaps = processed,
                TotalGaps = gapList.Count,
                PercentComplete = (double)processed / gapList.Count * 100
            });

            try
            {
                var repairSuccess = await _completenessService.RepairGapAsync(
                    gap.Symbol, gap.StartDate, gap.EndDate, ct);

                if (repairSuccess)
                {
                    result.RepairedGaps++;
                    result.RepairedSymbols.Add(gap.Symbol);
                }
                else
                {
                    result.FailedGaps++;
                    result.FailedItems.Add($"{gap.Symbol}: {gap.StartDate:yyyy-MM-dd}");
                }
            }
            catch (Exception ex)
            {
                result.FailedGaps++;
                result.FailedItems.Add($"{gap.Symbol}: {ex.Message}");
            }

            processed++;
        }

        result.Success = result.FailedGaps == 0;
        return result;
    }

    /// <summary>
    /// Gets completeness trend data over time.
    /// </summary>
    public async Task<CompletenessTrendData> GetCompletenessTrendAsync(
        DateOnly fromDate,
        DateOnly toDate,
        string[]? symbols = null,
        CancellationToken ct = default)
    {
        var trend = new CompletenessTrendData
        {
            FromDate = fromDate,
            ToDate = toDate
        };

        // Group by week or month depending on range
        var totalDays = toDate.DayNumber - fromDate.DayNumber;
        var groupByWeek = totalDays <= 90;

        var currentDate = fromDate;
        while (currentDate <= toDate)
        {
            var periodEnd = groupByWeek
                ? currentDate.AddDays(6)
                : new DateOnly(currentDate.Year, currentDate.Month, 1).AddMonths(1).AddDays(-1);

            if (periodEnd > toDate)
                periodEnd = toDate;

            var report = await _completenessService.GetCompletenessReportAsync(
                symbols ?? Array.Empty<string>(), currentDate, periodEnd, ct);

            trend.Points.Add(new CompletenessTrendPoint
            {
                PeriodStart = currentDate,
                PeriodEnd = periodEnd,
                Completeness = report.OverallScore,
                EventCount = report.TotalActualEvents,
                GapCount = report.GapCount
            });

            currentDate = periodEnd.AddDays(1);
        }

        return trend;
    }

    private bool IsTradingDay(DateOnly date)
    {
        // Weekend check
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return false;

        // Holiday check
        if (IsMarketHoliday(date))
            return false;

        return true;
    }

    private bool IsMarketHoliday(DateOnly date)
    {
        // US market holidays (simplified - would need full calendar in production)
        var holidays = GetUsMarketHolidays(date.Year);
        return holidays.Contains(date);
    }

    private HashSet<DateOnly> GetUsMarketHolidays(int year)
    {
        var holidays = new HashSet<DateOnly>();

        // New Year's Day
        var newYear = new DateOnly(year, 1, 1);
        if (newYear.DayOfWeek == DayOfWeek.Saturday)
            holidays.Add(new DateOnly(year - 1, 12, 31));
        else if (newYear.DayOfWeek == DayOfWeek.Sunday)
            holidays.Add(new DateOnly(year, 1, 2));
        else
            holidays.Add(newYear);

        // MLK Day (3rd Monday of January)
        holidays.Add(GetNthDayOfMonth(year, 1, DayOfWeek.Monday, 3));

        // Presidents Day (3rd Monday of February)
        holidays.Add(GetNthDayOfMonth(year, 2, DayOfWeek.Monday, 3));

        // Good Friday (varies)
        holidays.Add(GetGoodFriday(year));

        // Memorial Day (last Monday of May)
        holidays.Add(GetLastDayOfMonth(year, 5, DayOfWeek.Monday));

        // Juneteenth (June 19)
        var juneteenth = new DateOnly(year, 6, 19);
        if (juneteenth.DayOfWeek == DayOfWeek.Saturday)
            holidays.Add(juneteenth.AddDays(-1));
        else if (juneteenth.DayOfWeek == DayOfWeek.Sunday)
            holidays.Add(juneteenth.AddDays(1));
        else
            holidays.Add(juneteenth);

        // Independence Day (July 4)
        var july4 = new DateOnly(year, 7, 4);
        if (july4.DayOfWeek == DayOfWeek.Saturday)
            holidays.Add(july4.AddDays(-1));
        else if (july4.DayOfWeek == DayOfWeek.Sunday)
            holidays.Add(july4.AddDays(1));
        else
            holidays.Add(july4);

        // Labor Day (1st Monday of September)
        holidays.Add(GetNthDayOfMonth(year, 9, DayOfWeek.Monday, 1));

        // Thanksgiving (4th Thursday of November)
        holidays.Add(GetNthDayOfMonth(year, 11, DayOfWeek.Thursday, 4));

        // Christmas (December 25)
        var christmas = new DateOnly(year, 12, 25);
        if (christmas.DayOfWeek == DayOfWeek.Saturday)
            holidays.Add(christmas.AddDays(-1));
        else if (christmas.DayOfWeek == DayOfWeek.Sunday)
            holidays.Add(christmas.AddDays(1));
        else
            holidays.Add(christmas);

        return holidays;
    }

    private DateOnly GetNthDayOfMonth(int year, int month, DayOfWeek dayOfWeek, int n)
    {
        var first = new DateOnly(year, month, 1);
        var daysUntil = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(daysUntil + (n - 1) * 7);
    }

    private DateOnly GetLastDayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var daysSince = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return last.AddDays(-daysSince);
    }

    private DateOnly GetGoodFriday(int year)
    {
        // Easter calculation (Anonymous Gregorian algorithm)
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;

        var easter = new DateOnly(year, month, day);
        return easter.AddDays(-2); // Good Friday is 2 days before Easter
    }
}

/// <summary>
/// Calendar data for a year.
/// </summary>
public sealed class CalendarYearData
{
    public int Year { get; set; }
    public List<CalendarMonthData> Months { get; } = new();
    public int TotalTradingDays { get; set; }
    public int DaysWithData { get; set; }
    public int TotalGaps { get; set; }
    public double OverallCompleteness { get; set; }
}

/// <summary>
/// Calendar data for a month.
/// </summary>
public sealed class CalendarMonthData
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public List<CalendarDayData> Days { get; } = new();
    public int TradingDays { get; set; }
    public int DaysWithData { get; set; }
    public int GapCount { get; set; }
    public double Completeness { get; set; }
}

/// <summary>
/// Calendar data for a day.
/// </summary>
public sealed class CalendarDayData
{
    public DateOnly Date { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsWeekend { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsTradingDay { get; set; }
    public bool HasData { get; set; }
    public double Completeness { get; set; }
    public long EventCount { get; set; }
    public long ExpectedEvents { get; set; }
    public bool HasGaps { get; set; }
    public int GapCount { get; set; }
    public CompletenessLevel CompletenessLevel { get; set; }
    public List<SymbolDayData> SymbolBreakdown { get; } = new();
}

/// <summary>
/// Symbol data for a single day.
/// </summary>
public sealed class SymbolDayData
{
    public string Symbol { get; set; } = string.Empty;
    public bool HasData { get; set; }
    public double Completeness { get; set; }
    public long EventCount { get; set; }
    public bool HasGaps { get; set; }
}

/// <summary>
/// Completeness level for heatmap coloring.
/// </summary>
public enum CompletenessLevel : byte
{
    NonTrading,  // Weekend/Holiday
    Missing,     // 0% - no data
    Minimal,     // 1-49% - very incomplete
    Poor,        // 50-79% - significant gaps
    Partial,     // 80-94% - minor gaps
    Good,        // 95-98% - nearly complete
    Complete     // 99-100% - complete
}

/// <summary>
/// Coverage matrix data (symbols x dates).
/// </summary>
public sealed class CoverageMatrixData
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public List<DateOnly> Dates { get; set; } = new();
    public List<SymbolCoverageData> Symbols { get; } = new();
}

/// <summary>
/// Symbol coverage data.
/// </summary>
public sealed class SymbolCoverageData
{
    public string Symbol { get; set; } = string.Empty;
    public List<DayCoverageInfo> DayCoverage { get; } = new();
    public double OverallCompleteness { get; set; }
    public int DaysWithData { get; set; }
    public int TotalDays { get; set; }
}

/// <summary>
/// Day coverage info.
/// </summary>
public sealed class DayCoverageInfo
{
    public DateOnly Date { get; set; }
    public bool HasData { get; set; }
    public double Completeness { get; set; }
}

/// <summary>
/// Gap summary data.
/// </summary>
public sealed class GapSummaryData
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int TotalGaps { get; set; }
    public int TotalTradingDays { get; set; }
    public int DaysWithGaps { get; set; }
    public double OverallCompleteness { get; set; }
    public List<GapInfo> Gaps { get; } = new();
    public Dictionary<string, int> GapsByType { get; set; } = new();
    public Dictionary<string, int> GapsBySymbol { get; set; } = new();
}

/// <summary>
/// Gap information.
/// </summary>
public sealed class GapInfo
{
    public string Symbol { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string GapType { get; set; } = string.Empty;
    public long ExpectedEvents { get; set; }
    public long ActualEvents { get; set; }
    public bool CanRepair { get; set; }
}

/// <summary>
/// Gap repair result.
/// </summary>
public sealed class GapRepairResult
{
    public bool Success { get; set; }
    public int RepairedGaps { get; set; }
    public int FailedGaps { get; set; }
    public HashSet<string> RepairedSymbols { get; } = new();
    public List<string> FailedItems { get; } = new();
}

/// <summary>
/// Gap repair progress.
/// </summary>
public sealed class GapRepairProgress
{
    public string CurrentSymbol { get; set; } = string.Empty;
    public DateOnly CurrentDate { get; set; }
    public int ProcessedGaps { get; set; }
    public int TotalGaps { get; set; }
    public double PercentComplete { get; set; }
}

/// <summary>
/// Completeness trend data.
/// </summary>
public sealed class CompletenessTrendData
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public List<CompletenessTrendPoint> Points { get; } = new();
}

/// <summary>
/// Completeness trend point.
/// </summary>
public sealed class CompletenessTrendPoint
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public double Completeness { get; set; }
    public long EventCount { get; set; }
    public int GapCount { get; set; }
}
