using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="DataCompletenessService"/> and <see cref="TradingCalendarService"/> —
/// trading calendar logic, completeness scoring, gap identification, and model validation.
/// </summary>
public sealed class DataCompletenessServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TradingCalendarService _calendar;

    public DataCompletenessServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mdc-completeness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _calendar = new TradingCalendarService();
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TradingCalendarService
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TradingCalendar_IsHoliday_NewYearsDay_ShouldBeTrue()
    {
        _calendar.IsHoliday(new DateOnly(2026, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void TradingCalendar_IsHoliday_Thanksgiving_ShouldBeTrue()
    {
        _calendar.IsHoliday(new DateOnly(2026, 11, 26)).Should().BeTrue();
    }

    [Fact]
    public void TradingCalendar_IsHoliday_RegularDay_ShouldBeFalse()
    {
        _calendar.IsHoliday(new DateOnly(2026, 3, 10)).Should().BeFalse();
    }

    [Fact]
    public void TradingCalendar_IsTradingDay_Weekday_ShouldBeTrue()
    {
        // 2026-03-10 is a Tuesday
        _calendar.IsTradingDay(new DateOnly(2026, 3, 10)).Should().BeTrue();
    }

    [Fact]
    public void TradingCalendar_IsTradingDay_Saturday_ShouldBeFalse()
    {
        // 2026-03-14 is a Saturday
        _calendar.IsTradingDay(new DateOnly(2026, 3, 14)).Should().BeFalse();
    }

    [Fact]
    public void TradingCalendar_IsTradingDay_Sunday_ShouldBeFalse()
    {
        // 2026-03-15 is a Sunday
        _calendar.IsTradingDay(new DateOnly(2026, 3, 15)).Should().BeFalse();
    }

    [Fact]
    public void TradingCalendar_IsTradingDay_Holiday_ShouldBeFalse()
    {
        _calendar.IsTradingDay(new DateOnly(2026, 12, 25)).Should().BeFalse();
    }

    [Fact]
    public void TradingCalendar_GetTradingDays_ShouldExcludeWeekends()
    {
        // Mon Mar 9 to Fri Mar 13 = 5 weekdays
        var days = _calendar.GetTradingDays(new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 13));
        days.Should().HaveCount(5);
        days.Should().OnlyContain(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday);
    }

    [Fact]
    public void TradingCalendar_GetTradingDays_ShouldExcludeHolidays()
    {
        // Dec 22-31: Dec 25 is Christmas, Dec 26-27 weekend
        var days = _calendar.GetTradingDays(new DateOnly(2026, 12, 22), new DateOnly(2026, 12, 31));
        days.Should().NotContain(new DateOnly(2026, 12, 25));
    }

    [Fact]
    public void TradingCalendar_GetTradingDays_EmptyRange_ShouldReturnEmpty()
    {
        var days = _calendar.GetTradingDays(new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 14));
        days.Should().BeEmpty();
    }

    [Fact]
    public void TradingCalendar_GetTradingDays_SingleWeekend_ShouldReturnEmpty()
    {
        var days = _calendar.GetTradingDays(new DateOnly(2026, 3, 14), new DateOnly(2026, 3, 15));
        days.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // DataCompletenessService — basic operations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCompletenessReportAsync_EmptyDirectory_ShouldReturnReportWithNoSymbols()
    {
        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var report = await svc.GetCompletenessReportAsync(
            _tempDir,
            new DateOnly(2026, 3, 9),
            new DateOnly(2026, 3, 13));

        report.Should().NotBeNull();
        report.Symbols.Should().BeEmpty();
        report.ExpectedTradingDays.Should().Be(5);
        report.OverallScore.Should().Be(100); // No symbols = 100% by convention
    }

    [Fact]
    public async Task GetCompletenessReportAsync_WithSymbolDirectory_NoData_ShouldReportZeroScore()
    {
        var symbolDir = Path.Combine(_tempDir, "SPY");
        Directory.CreateDirectory(symbolDir);

        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var report = await svc.GetCompletenessReportAsync(
            _tempDir,
            new DateOnly(2026, 3, 9),
            new DateOnly(2026, 3, 13),
            new[] { "SPY" });

        report.Symbols.Should().HaveCount(1);
        report.Symbols[0].Symbol.Should().Be("SPY");
        report.Symbols[0].Score.Should().Be(0);
        report.Symbols[0].DaysWithData.Should().Be(0);
        report.Symbols[0].MissingDays.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetCompletenessReportAsync_WithDataFile_ShouldCountDay()
    {
        var symbolDir = Path.Combine(_tempDir, "SPY");
        Directory.CreateDirectory(symbolDir);
        // Create a file matching a trading day
        var tradingDay = new DateOnly(2026, 3, 9); // Monday
        var filePath = Path.Combine(symbolDir, $"{tradingDay:yyyy-MM-dd}.jsonl");
        await File.WriteAllTextAsync(filePath, "{\"Timestamp\":\"2026-03-09T10:00:00Z\"}\n");

        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var report = await svc.GetCompletenessReportAsync(
            _tempDir,
            new DateOnly(2026, 3, 9),
            new DateOnly(2026, 3, 13),
            new[] { "SPY" });

        report.Symbols[0].DaysWithData.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetCompletenessReportAsync_SupportsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var act = async () => await svc.GetCompletenessReportAsync(
            _tempDir, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 13), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── GetDailyCompletenessAsync ─────────────────────────────────────

    [Fact]
    public async Task GetDailyCompletenessAsync_Weekend_ShouldReturnNonTradingDay()
    {
        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var result = await svc.GetDailyCompletenessAsync(_tempDir, new DateOnly(2026, 3, 14));

        result.Should().NotBeNull();
        result.IsWeekend.Should().BeTrue();
        result.Status.Should().Be(CompletenessStatus.NonTradingDay);
    }

    [Fact]
    public async Task GetDailyCompletenessAsync_Holiday_ShouldReturnNonTradingDay()
    {
        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var result = await svc.GetDailyCompletenessAsync(_tempDir, new DateOnly(2026, 12, 25));

        result.Should().NotBeNull();
        result.IsHoliday.Should().BeTrue();
        result.Status.Should().Be(CompletenessStatus.NonTradingDay);
    }

    [Fact]
    public async Task GetDailyCompletenessAsync_TradingDay_NoData_ShouldShowMissing()
    {
        var symbolDir = Path.Combine(_tempDir, "AAPL");
        Directory.CreateDirectory(symbolDir);

        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var result = await svc.GetDailyCompletenessAsync(_tempDir, new DateOnly(2026, 3, 10));

        result.Should().NotBeNull();
        result.SymbolsMissingData.Should().BeGreaterThanOrEqualTo(1);
    }

    // ── GetBackfillableGapsAsync ──────────────────────────────────────

    [Fact]
    public async Task GetBackfillableGapsAsync_EmptyDirectory_ShouldReturnEmpty()
    {
        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var gaps = await svc.GetBackfillableGapsAsync(
            _tempDir, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 13));

        gaps.Should().NotBeNull();
        gaps.Should().BeEmpty(); // No symbols = no gaps
    }

    [Fact]
    public async Task GetBackfillableGapsAsync_MissingData_ShouldReturnGaps()
    {
        var symbolDir = Path.Combine(_tempDir, "TSLA");
        Directory.CreateDirectory(symbolDir);

        var manifest = ManifestService.Instance;
        var svc = new DataCompletenessService(manifest, _calendar);

        var gaps = await svc.GetBackfillableGapsAsync(
            _tempDir, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 13), new[] { "TSLA" });

        gaps.Should().NotBeEmpty();
        gaps.Should().OnlyContain(g => g.CanBackfill);
        gaps.Should().OnlyContain(g => g.Symbol == "TSLA");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Model tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CompletenessReport_ShouldHaveDefaultValues()
    {
        var report = new CompletenessReport();
        report.Symbols.Should().NotBeNull().And.BeEmpty();
        report.CalendarData.Should().NotBeNull().And.BeEmpty();
        report.Gaps.Should().NotBeNull().And.BeEmpty();
        report.OverallScore.Should().Be(0);
        report.ExpectedTradingDays.Should().Be(0);
    }

    [Fact]
    public void CompletenessReport_ComputedProperties_ShouldWork()
    {
        var report = new CompletenessReport
        {
            OverallScore = 95.5,
            ExpectedTradingDays = 20
        };

        report.OverallCompleteness.Should().Be(95.5);
        report.TotalTradingDays.Should().Be(20);
        report.GapCount.Should().Be(0);
        report.TotalActualEvents.Should().Be(0);
    }

    [Fact]
    public void DailyCompleteness_ShouldHaveDefaultValues()
    {
        var daily = new DailyCompleteness();
        daily.Symbols.Should().NotBeNull().And.BeEmpty();
        daily.SymbolsWithData.Should().Be(0);
        daily.SymbolsMissingData.Should().Be(0);
        daily.TotalEvents.Should().Be(0);
        daily.HasData.Should().BeFalse();
        daily.Completeness.Should().Be(0);
    }

    [Fact]
    public void DailyCompleteness_ComputedProperties_WithData()
    {
        var daily = new DailyCompleteness
        {
            SymbolsWithData = 8,
            SymbolsMissingData = 2,
            TotalEvents = 50000
        };

        daily.HasData.Should().BeTrue();
        daily.Completeness.Should().Be(80.0);
        daily.EventCount.Should().Be(50000);
        daily.GapCount.Should().Be(2);
    }

    [Fact]
    public void DailySymbolDetail_ShouldHaveDefaultValues()
    {
        var detail = new DailySymbolDetail();
        detail.Symbol.Should().BeEmpty();
        detail.HasData.Should().BeFalse();
        detail.EventCount.Should().Be(0);
        detail.FileSize.Should().Be(0);
        detail.EventTypes.Should().NotBeNull().And.BeEmpty();
        detail.Completeness.Should().Be(0);
        detail.GapCount.Should().Be(1);
    }

    [Fact]
    public void DailySymbolDetail_WithData_ShouldReportComplete()
    {
        var detail = new DailySymbolDetail { HasData = true, EventCount = 5000 };
        detail.Completeness.Should().Be(100.0);
        detail.GapCount.Should().Be(0);
    }

    [Fact]
    public void BackfillableGap_ShouldHaveDefaultValues()
    {
        var gap = new BackfillableGap();
        gap.Symbol.Should().BeEmpty();
        gap.CanBackfill.Should().BeFalse();
        gap.EstimatedEvents.Should().Be(0);
        gap.ActualEvents.Should().Be(0);
    }

    [Fact]
    public void BackfillableGap_ComputedProperties_ShouldWork()
    {
        var date = new DateOnly(2026, 3, 10);
        var gap = new BackfillableGap
        {
            Symbol = "SPY",
            Date = date,
            CanBackfill = true,
            EstimatedEvents = 15000,
            GapType = GapType.Single
        };

        gap.StartDate.Should().Be(date);
        gap.EndDate.Should().Be(date);
        gap.ExpectedEvents.Should().Be(15000);
        gap.CanRepair.Should().BeTrue();
    }

    [Fact]
    public void CalendarDay_ShouldStoreValues()
    {
        var day = new CalendarDay
        {
            Date = new DateOnly(2026, 3, 10),
            IsTradingDay = true,
            SymbolsWithData = 5,
            TotalSymbols = 10,
            CompletenessPercent = 50.0,
            Status = CompletenessStatus.MajorIssues
        };

        day.Date.Should().Be(new DateOnly(2026, 3, 10));
        day.IsTradingDay.Should().BeTrue();
        day.CompletenessPercent.Should().Be(50.0);
        day.Status.Should().Be(CompletenessStatus.MajorIssues);
    }

    [Fact]
    public void DayEventCount_AddEvents_ShouldAccumulate()
    {
        var counts = new DayEventCount();
        counts.AddEvents("Trade", 100);
        counts.AddEvents("Quote", 200);
        counts.AddEvents("Depth", 50);
        counts.AddEvents("Bar", 10);
        counts.AddEvents("Other", 5);

        counts.TradeEvents.Should().Be(100);
        counts.QuoteEvents.Should().Be(200);
        counts.DepthEvents.Should().Be(50);
        counts.BarEvents.Should().Be(10);
        counts.OtherEvents.Should().Be(5);
        counts.TotalEvents.Should().Be(365);
    }

    [Fact]
    public void DayEventCount_AddEvents_UnknownType_ShouldGoToOther()
    {
        var counts = new DayEventCount();
        counts.AddEvents("UnknownType", 42);
        counts.OtherEvents.Should().Be(42);
    }

    // ── Enum tests ───────────────────────────────────────────────────

    [Theory]
    [InlineData(CompletenessStatus.Complete)]
    [InlineData(CompletenessStatus.MinorGaps)]
    [InlineData(CompletenessStatus.SignificantGaps)]
    [InlineData(CompletenessStatus.MajorIssues)]
    [InlineData(CompletenessStatus.NonTradingDay)]
    public void CompletenessStatus_AllValues_ShouldBeDefined(CompletenessStatus status)
    {
        Enum.IsDefined(typeof(CompletenessStatus), status).Should().BeTrue();
    }

    [Theory]
    [InlineData(GapType.Single)]
    [InlineData(GapType.Consecutive)]
    [InlineData(GapType.StartOfRange)]
    [InlineData(GapType.EndOfRange)]
    public void GapType_AllValues_ShouldBeDefined(GapType gapType)
    {
        Enum.IsDefined(typeof(GapType), gapType).Should().BeTrue();
    }
}
