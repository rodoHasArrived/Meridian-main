using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Unit tests for <see cref="DataGapAnalyzer"/> gap detection correctness.
///
/// <para>
/// The analyzer counts expected trading days by excluding weekends (Saturday and Sunday).
/// Tests use concrete date ranges to assert exact gap counts rather than relying on
/// environment-dependent assumptions.
/// </para>
/// </summary>
public sealed class DataGapAnalyzerTests : IDisposable
{
    private readonly string _dataRoot;

    public DataGapAnalyzerTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "DataGapAnalyzerTests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dataRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    // ── empty directory ────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_EmptyDirectory_AllTradingDaysAreGaps()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // 2024-01-02 (Tue) to 2024-01-05 (Fri) = 4 trading days
        var result = await analyzer.AnalyzeAsync(
            ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        result.TotalSymbols.Should().Be(1);
        result.SymbolsWithGaps.Should().Be(1);
        result.SymbolsComplete.Should().Be(0);

        var aaplGap = result.SymbolGaps["AAPL"];
        aaplGap.HasGaps.Should().BeTrue();
        aaplGap.GapDates.Should().HaveCount(4);
        aaplGap.ExistingDates.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeSymbolGapsAsync_EmptyDirectory_AllExpectedDaysAreGaps()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        var gapInfo = await analyzer.AnalyzeSymbolGapsAsync(
            "MSFT",
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        gapInfo.ExpectedDays.Should().Be(4);
        gapInfo.CoveredDays.Should().Be(0);
        gapInfo.GapDates.Should().HaveCount(4);
        gapInfo.HasGaps.Should().BeTrue();
        gapInfo.CoveragePercent.Should().Be(0);
    }

    // ── weekend exclusion ─────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_RangeSpansWeekend_ExcludesSaturdayAndSunday()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // 2024-01-01 (Mon) to 2024-01-07 (Sun): Mon-Fri = 5 trading days, Sat+Sun excluded
        var result = await analyzer.AnalyzeAsync(
            ["SPY"],
            from: new DateOnly(2024, 1, 1),
            to: new DateOnly(2024, 1, 7));

        var spyGap = result.SymbolGaps["SPY"];
        spyGap.ExpectedDays.Should().Be(5, "only Mon-Fri should be expected trading days");
        spyGap.GapDates.Should().NotContain(new DateOnly(2024, 1, 6), "Saturday must be excluded");
        spyGap.GapDates.Should().NotContain(new DateOnly(2024, 1, 7), "Sunday must be excluded");
    }

    [Fact]
    public async Task AnalyzeAsync_RangeIsWeekendOnly_ReturnsZeroExpectedDays()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // 2024-01-06 (Sat) to 2024-01-07 (Sun) = 0 trading days
        var result = await analyzer.AnalyzeAsync(
            ["AAPL"],
            from: new DateOnly(2024, 1, 6),
            to: new DateOnly(2024, 1, 7));

        var aaplGap = result.SymbolGaps["AAPL"];
        aaplGap.ExpectedDays.Should().Be(0);
        aaplGap.HasGaps.Should().BeFalse();
        aaplGap.GapDates.Should().BeEmpty();
    }

    // ── multi-week range ──────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MultiWeekRange_CorrectlyCountsTradingDays()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // 2024-01-01 (Mon) to 2024-01-14 (Sun) = 2 calendar weeks = 10 trading days
        var result = await analyzer.AnalyzeAsync(
            ["AAPL"],
            from: new DateOnly(2024, 1, 1),
            to: new DateOnly(2024, 1, 14));

        var aaplGap = result.SymbolGaps["AAPL"];
        aaplGap.ExpectedDays.Should().Be(10, "2 full weeks Mon-Fri = 10 trading days");
        aaplGap.GapDates.Should().HaveCount(10);
    }

    [Fact]
    public async Task AnalyzeAsync_ThreeWeekRange_CorrectlyExcludesAllWeekends()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // 2024-01-02 (Tue) to 2024-01-19 (Fri) = 14 calendar days
        // Weekends: 6-7 Jan, 13-14 Jan = 4 weekend days
        // Trading days: 14 - 4 - 0 (Jan 2 is Tue, not cutting a day off) = actually need to compute
        // Mon 1 Jan, Tue 2 Jan, Wed 3, Thu 4, Fri 5 = week 1: 5 days (1-5)
        // Mon 8, Tue 9, Wed 10, Thu 11, Fri 12 = week 2: 5 days
        // Mon 15, Tue 16, Wed 17, Thu 18, Fri 19 = week 3: 5 days
        // Range 2 Jan - 19 Jan: exclude Mon Jan 1 = 5+5+5 - 1 = 14 trading days? Let me re-count
        // 2 Jan (Tue), 3 (Wed), 4 (Thu), 5 (Fri), 8 (Mon), 9 (Tue), 10 (Wed), 11 (Thu), 12 (Fri),
        // 15 (Mon), 16 (Tue), 17 (Wed), 18 (Thu), 19 (Fri) = 14 trading days
        var result = await analyzer.AnalyzeAsync(
            ["MSFT"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 19));

        var msftGap = result.SymbolGaps["MSFT"];
        msftGap.ExpectedDays.Should().Be(14, "Jan 2-19 excluding 4 weekend days = 14 trading days");
        msftGap.GapDates.Should().NotContain(d =>
            d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday,
            "gap dates must never include weekends");
    }

    // ── multi-month range ─────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MultiMonthRange_CorrectlyCountsTradingDays()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // Jan 2024: weekdays only. Jan has 31 days; weekends are 6-7, 13-14, 20-21, 27-28 = 8 weekend days
        // 31 - 8 = 23 trading days (Jan 1 is Mon, so all weekdays are included)
        // Feb 2024: 29 days (2024 is leap year); weekends are 3-4, 10-11, 17-18, 24-25 = 8 weekend days
        // 29 - 8 = 21 trading days
        // Mar 2024: 31 days; weekends are 2-3, 9-10, 16-17, 23-24, 30-31 = 10 weekend days
        // 31 - 10 = 21 trading days
        // Total: 23 + 21 + 21 = 65 trading days
        var result = await analyzer.AnalyzeAsync(
            ["GOOG"],
            from: new DateOnly(2024, 1, 1),
            to: new DateOnly(2024, 3, 31));

        var googGap = result.SymbolGaps["GOOG"];
        googGap.ExpectedDays.Should().Be(65, "Q1 2024 (Jan-Mar) should have 65 trading days");
        googGap.GapDates.Should().HaveCount(65);
        googGap.GapDates.Should().NotContain(d =>
            d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday,
            "gap dates must never include weekends");
    }

    [Fact]
    public async Task AnalyzeAsync_SixMonthRange_NoGapsExceedExpectedCount()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // 6 months from 2024-01-01 to 2024-06-30 — just verify the gap count is
        // bounded to the trading-day count and has no weekend dates
        var result = await analyzer.AnalyzeAsync(
            ["SPY"],
            from: new DateOnly(2024, 1, 1),
            to: new DateOnly(2024, 6, 30));

        var spyGap = result.SymbolGaps["SPY"];
        spyGap.GapDates.Should().NotContain(d =>
            d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday,
            "no weekend date should ever appear as a gap");

        // 6 months ≈ ~130 trading days; generous upper bound
        spyGap.ExpectedDays.Should().BeLessThanOrEqualTo(135);
        spyGap.ExpectedDays.Should().BeGreaterThan(120);
    }

    // ── existing data detected ────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_WithExistingDailyBarFiles_ReportsOnlyMissingDates()
    {
        // Seed two JSONL files for AAPL covering 2024-01-02 and 2024-01-03
        CreateBarFile("AAPL", new DateOnly(2024, 1, 2));
        CreateBarFile("AAPL", new DateOnly(2024, 1, 3));

        var analyzer = new DataGapAnalyzer(_dataRoot);

        // Range: 2024-01-02 to 2024-01-05 (Tue-Fri = 4 trading days)
        var result = await analyzer.AnalyzeAsync(
            ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        var aaplGap = result.SymbolGaps["AAPL"];
        aaplGap.ExistingDates.Should().Contain(new DateOnly(2024, 1, 2));
        aaplGap.ExistingDates.Should().Contain(new DateOnly(2024, 1, 3));
        aaplGap.GapDates.Should().Contain(new DateOnly(2024, 1, 4));
        aaplGap.GapDates.Should().Contain(new DateOnly(2024, 1, 5));
        aaplGap.GapDates.Should().HaveCount(2, "only the two missing trading days should be gaps");
        aaplGap.CoveredDays.Should().Be(2);
    }

    [Fact]
    public async Task AnalyzeAsync_AllDatesAlreadyPresent_ReportsNoGaps()
    {
        // Seed all 4 trading days in the range
        CreateBarFile("MSFT", new DateOnly(2024, 1, 2));
        CreateBarFile("MSFT", new DateOnly(2024, 1, 3));
        CreateBarFile("MSFT", new DateOnly(2024, 1, 4));
        CreateBarFile("MSFT", new DateOnly(2024, 1, 5));

        var analyzer = new DataGapAnalyzer(_dataRoot);

        var result = await analyzer.AnalyzeAsync(
            ["MSFT"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        result.SymbolsWithGaps.Should().Be(0);
        result.SymbolsComplete.Should().Be(1);

        var msftGap = result.SymbolGaps["MSFT"];
        msftGap.HasGaps.Should().BeFalse();
        msftGap.GapDates.Should().BeEmpty();
        msftGap.CoveredDays.Should().Be(4);
        msftGap.CoveragePercent.Should().Be(100);
    }

    // ── multiple symbols ──────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MultipleSymbols_IndependentGapTracking()
    {
        // Seed 3 of 4 trading days for AAPL, none for MSFT
        CreateBarFile("AAPL", new DateOnly(2024, 1, 2));
        CreateBarFile("AAPL", new DateOnly(2024, 1, 3));
        CreateBarFile("AAPL", new DateOnly(2024, 1, 4));

        var analyzer = new DataGapAnalyzer(_dataRoot);

        var result = await analyzer.AnalyzeAsync(
            ["AAPL", "MSFT"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        result.TotalSymbols.Should().Be(2);
        result.SymbolsWithGaps.Should().Be(2);

        result.SymbolGaps["AAPL"].GapDates.Should().HaveCount(1);
        result.SymbolGaps["AAPL"].GapDates.Should().Contain(new DateOnly(2024, 1, 5));

        result.SymbolGaps["MSFT"].GapDates.Should().HaveCount(4);
    }

    // ── GetGapRanges consolidation ────────────────────────────────────────

    [Fact]
    public async Task GetGapRanges_ConsecutiveTradingDays_ProducesSingleRange()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        var gapInfo = await analyzer.AnalyzeSymbolGapsAsync(
            "AAPL",
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 12)); // 2 weeks of trading days

        // Without existing data, all trading days are gaps; they should be one consolidated range
        var ranges = gapInfo.GetGapRanges();
        ranges.Should().HaveCount(1, "consecutive trading days should consolidate into a single range");
        ranges[0].From.Should().Be(new DateOnly(2024, 1, 2));
        ranges[0].To.Should().Be(new DateOnly(2024, 1, 12));
    }

    [Fact]
    public async Task GetGapRanges_MultiMonthRange_ConsolidatesIntoSingleRange()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // Gaps spanning 3 months should consolidate into a single range when BatchSizeDays=365
        var gapInfo = await analyzer.AnalyzeSymbolGapsAsync(
            "SPY",
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 3, 29));

        var ranges = gapInfo.GetGapRanges(maxDaysPerRange: 365);
        ranges.Should().HaveCount(1,
            "a 3-month gap with batchSize=365 should produce a single API call range");
    }

    [Fact]
    public async Task GetGapRanges_WithSmallBatchSize_SplitsIntoMultipleRanges()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // 3-month date range should split when BatchSizeDays=30
        var gapInfo = await analyzer.AnalyzeSymbolGapsAsync(
            "SPY",
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 3, 29));

        var ranges = gapInfo.GetGapRanges(maxDaysPerRange: 30);
        ranges.Should().HaveCountGreaterThan(1,
            "a 3-month gap with batchSize=30 should be split into multiple ranges");
    }

    // ── cache invalidation ────────────────────────────────────────────────

    [Fact]
    public async Task ClearCache_AfterAddingNewFiles_AllowsDetectionOfNewDates()
    {
        var analyzer = new DataGapAnalyzer(_dataRoot);

        // First analysis — no files
        var before = await analyzer.AnalyzeSymbolGapsAsync(
            "AAPL",
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));
        before.GapDates.Should().HaveCount(4);

        // Add a file and clear the cache
        CreateBarFile("AAPL", new DateOnly(2024, 1, 2));
        analyzer.ClearCache();

        // Second analysis — should detect the new file
        var after = await analyzer.AnalyzeSymbolGapsAsync(
            "AAPL",
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));
        after.GapDates.Should().HaveCount(3, "cache cleared; newly created file should be seen");
        after.ExistingDates.Should().Contain(new DateOnly(2024, 1, 2));
    }

    // ── helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal daily-bar JSONL file so the analyzer picks up the date.
    /// Naming pattern: &lt;SYMBOL&gt;_Bar_&lt;date&gt;.jsonl — matches the filename-date
    /// extraction pattern used by <see cref="DataGapAnalyzer"/>.
    /// </summary>
    private void CreateBarFile(string symbol, DateOnly date)
    {
        var dir = _dataRoot;
        var path = Path.Combine(dir, $"{symbol}_Bar_{date:yyyy-MM-dd}.jsonl");
        // Write an empty file — the analyzer uses the filename date, not file content, when possible.
        File.WriteAllText(path, string.Empty);
    }
}
