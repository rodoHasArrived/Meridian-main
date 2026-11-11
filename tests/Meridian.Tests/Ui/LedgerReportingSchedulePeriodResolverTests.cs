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
