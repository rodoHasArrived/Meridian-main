using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class LedgerReportingSchedulePeriodResolverTests
{
    private static readonly Guid BookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resolve_AprilStartFiscalYear_ReturnsTheDurablePeriodIdNotACalendarSelector()
    {
        // Fiscal 2027 starts 1 April 2026: May 2026 is period 02, not "2026-05".
        var april = Period(BookId, 2027, 1, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var may = Period(BookId, 2027, 2, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var resolver = Resolver([Book(BookId, "fund-a", "Primary")], [april, may]);

        var selector = await resolver.ResolvePeriodAsync(
            Parameters("fund-a", new ReportingLedgerBookSelectionDto(BookId)),
            new DateOnly(2026, 5, 31));

        selector.Should().Be(new ReportingSchedulePeriodMatch(may.PeriodId.ToString("D"), may.StartDate, may.EndDate));
    }

    [Fact]
    public async Task Resolve_BookSelectedByCode_MatchesExactlyOneBookOfTheFund()
    {
        var may = Period(BookId, 2026, 5, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var resolver = Resolver(
            [Book(BookId, "fund-a", "STAT-GL"), Book(OtherBookId, "fund-a", "Tax")],
            [may, Period(OtherBookId, 2026, 5, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31))]);

        var selector = await resolver.ResolvePeriodAsync(
            Parameters("fund-a", new ReportingLedgerBookSelectionDto(LedgerBookCode: "stat-gl")),
            new DateOnly(2026, 5, 15));

        selector!.Selector.Should().Be(may.PeriodId.ToString("D"));
    }

    [Fact]
    public async Task Resolve_ReturnsNullRatherThanGuessing()
    {
        var may = Period(BookId, 2026, 5, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var overlapping = Period(BookId, 2026, 13, new DateOnly(2026, 5, 20), new DateOnly(2026, 6, 10));
        var resolver = Resolver(
            [Book(BookId, "fund-a", "Primary"), Book(OtherBookId, "fund-a", "Primary")],
            [may, overlapping]);
        var byId = Parameters("fund-a", new ReportingLedgerBookSelectionDto(BookId));

        // No period opened yet for the date.
        (await resolver.ResolvePeriodAsync(byId, new DateOnly(2026, 7, 31))).Should().BeNull();
        // Two periods contain the date.
        (await resolver.ResolvePeriodAsync(byId, new DateOnly(2026, 5, 25))).Should().BeNull();
        // The book belongs to another fund.
        (await resolver.ResolvePeriodAsync(
            Parameters("fund-b", new ReportingLedgerBookSelectionDto(BookId)),
            new DateOnly(2026, 5, 10))).Should().BeNull();
        // The code matches two books.
        (await resolver.ResolvePeriodAsync(
            Parameters("fund-a", new ReportingLedgerBookSelectionDto(LedgerBookCode: "Primary")),
            new DateOnly(2026, 5, 10))).Should().BeNull();
        // Unknown book id.
        (await resolver.ResolvePeriodAsync(
            Parameters("fund-a", new ReportingLedgerBookSelectionDto(Guid.NewGuid())),
            new DateOnly(2026, 5, 10))).Should().BeNull();
    }

    // Fiscal 2027 on a 4-4-5 calendar starting Sunday 1 February 2026: period lengths in weeks.
    private static IReadOnlyList<LedgerAccountingPeriod> FourFourFiveYear(Guid bookId, int periodCount = 12)
    {
        int[] weeks = [4, 4, 5, 4, 4, 5, 4, 4, 5, 4, 4, 5, 4];
        var periods = new List<LedgerAccountingPeriod>();
        var start = new DateOnly(2026, 2, 1);
        for (var periodNo = 1; periodNo <= periodCount; periodNo++)
        {
            var end = start.AddDays((weeks[periodNo - 1] * 7) - 1);
            periods.Add(Period(bookId, 2027, periodNo, start, end));
            start = end.AddDays(1);
        }

        return periods;
    }

    [Fact]
    public async Task ResolveNextClose_FourFourFiveCalendar_UsesFiscalPeriodNumbersNotCalendarQuarters()
    {
        var year = FourFourFiveYear(BookId);
        var resolver = Resolver([Book(BookId, "fund-a", "Primary")], year);
        var parameters = Parameters("fund-a", new ReportingLedgerBookSelectionDto(BookId));
        // Quarter 1 (periods 1-3) ends Saturday 2 May 2026; quarter 2 (periods 4-6) ends 1 August.
        year[2].EndDate.Should().Be(new DateOnly(2026, 5, 2));
        year[5].EndDate.Should().Be(new DateOnly(2026, 8, 1));

        var monthly = await resolver.ResolveNextClosePeriodAsync(
            parameters, new DateOnly(2026, 5, 2), ReportingSchedulePeriodFrequencyDto.Monthly);
        var quarterly = await resolver.ResolveNextClosePeriodAsync(
            parameters, new DateOnly(2026, 5, 2), ReportingSchedulePeriodFrequencyDto.Quarterly);
        var annually = await resolver.ResolveNextClosePeriodAsync(
            parameters, new DateOnly(2026, 5, 2), ReportingSchedulePeriodFrequencyDto.Annually);

        monthly!.Selector.Should().Be(year[3].PeriodId.ToString("D"), "the period starting the next day");
        quarterly!.Selector.Should().Be(year[5].PeriodId.ToString("D"), "period 6 ends the fiscal quarter");
        quarterly.EndDate.Should().Be(new DateOnly(2026, 8, 1));
        annually!.Selector.Should().Be(year[11].PeriodId.ToString("D"), "period 12 ends the fiscal year");
    }

    [Fact]
    public async Task ResolveNextClose_MidQuarterSchedule_ClosesTheQuarterItIsIn()
    {
        var year = FourFourFiveYear(BookId);
        var resolver = Resolver([Book(BookId, "fund-a", "Primary")], year);

        // The current as-of is the end of period 4, part-way through quarter 2.
        var quarterly = await resolver.ResolveNextClosePeriodAsync(
            Parameters("fund-a", new ReportingLedgerBookSelectionDto(BookId)),
            year[3].EndDate,
            ReportingSchedulePeriodFrequencyDto.Quarterly);

        quarterly!.Selector.Should().Be(year[5].PeriodId.ToString("D"));
    }

    [Fact]
    public async Task ResolveNextClose_ReturnsNullForFiscalYearsItCannotDivideIntoQuarters()
    {
        var parameters = Parameters("fund-a", new ReportingLedgerBookSelectionDto(BookId));
        var thirteen = Resolver([Book(BookId, "fund-a", "Primary")], FourFourFiveYear(BookId, periodCount: 13));
        var partlyOpened = Resolver([Book(BookId, "fund-a", "Primary")], FourFourFiveYear(BookId, periodCount: 5));
        var afterFirstQuarter = new DateOnly(2026, 5, 2);

        (await thirteen.ResolveNextClosePeriodAsync(
            parameters, afterFirstQuarter, ReportingSchedulePeriodFrequencyDto.Quarterly)).Should().BeNull();
        (await partlyOpened.ResolveNextClosePeriodAsync(
            parameters, afterFirstQuarter, ReportingSchedulePeriodFrequencyDto.Annually)).Should().BeNull();
        // Monthly needs only the following period, which both ledgers have.
        (await thirteen.ResolveNextClosePeriodAsync(
            parameters, afterFirstQuarter, ReportingSchedulePeriodFrequencyDto.Monthly)).Should().NotBeNull();
        // No period has been opened for the day after the last one.
        (await partlyOpened.ResolveNextClosePeriodAsync(
            parameters, new DateOnly(2026, 6, 27), ReportingSchedulePeriodFrequencyDto.Monthly)).Should().BeNull();
    }

    private static LedgerReportingSchedulePeriodResolver Resolver(
        IReadOnlyList<LedgerBookRecord> books,
        IReadOnlyList<LedgerAccountingPeriod> periods) =>
        new(
            (bookId, _) => Task.FromResult(books.FirstOrDefault(book => book.LedgerBookId == bookId)),
            (fundId, _) => Task.FromResult<IReadOnlyList<LedgerBookRecord>>(
                books.Where(book => book.FundProfileId == fundId).ToArray()),
            (bookId, _) => Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(
                periods.Where(period => period.LedgerBookId == bookId).ToArray()));

    private static LedgerBookRecord Book(Guid bookId, string fundId, string displayName) =>
        new(bookId, fundId, Guid.NewGuid(), default, displayName, "USD", Now, Now);

    private static LedgerAccountingPeriod Period(Guid bookId, int fiscalYear, int periodNo, DateOnly start, DateOnly end) =>
        new(Guid.NewGuid(), bookId, fiscalYear, periodNo, $"FY{fiscalYear} P{periodNo:00}", start, end, "Open", Now, null, 1);

    private static ReportingRunParametersDto Parameters(string fundId, ReportingLedgerBookSelectionDto book) =>
        new(
            new ReportingRunScopeDto(fundId),
            "retained-selector",
            new DateOnly(2026, 4, 30),
            book,
            default,
            "USD",
            default,
            default,
            default,
            IncludeSupportingSchedules: false,
            IncludeEvidenceAppendix: false);
}
