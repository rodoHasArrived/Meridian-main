using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;
using Xunit;

namespace Meridian.Tests.Reporting;

/// <summary>
/// The certified partners-capital roll-forward a governed reporting run captures: it must
/// hydrate only the run's book and period from the journal store, and decline to certify
/// anything it cannot scope instead of producing an empty or unscoped statement.
/// </summary>
public sealed class LedgerReportingPartnersCapitalSourceTests
{
    private const string Fund = "fund-a";
    private static readonly Guid Book = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid PeriodId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Opened = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly LedgerAccountingPeriod June = new(
        PeriodId, Book, 2026, 6, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "Open", Opened, null, 1);

    [Theory]
    [InlineData("June 2026")]
    [InlineData("june 2026")]
    [InlineData("2026-06")]
    [InlineData("b0000000-0000-0000-0000-000000000002")]
    public async Task CaptureAsync_RollsCapitalForwardForTheMatchedPeriod(string periodId)
    {
        var source = new LedgerReportingPartnersCapitalSource(Store().Object);

        var projection = await source.CaptureAsync(Parameters(periodId));

        projection.Should().NotBeNull();
        projection!.BeginningCapital.Should().Be(5_000_000m, "only the May contribution precedes the period");
        projection.Contributions.Should().Be(1_000_000m);
        projection.AllocatedResult.Should().Be(50_000m);
        projection.EndingCapital.Should().Be(6_050_000m);
        projection.IsReconciled.Should().BeTrue();
        projection.EndingCapital.Should().Be(projection.BeginningCapital + projection.Contributions
            - projection.Distributions + projection.AllocatedResult + projection.OtherMovements);
    }

    [Fact]
    public async Task CaptureAsync_DeclinesWhatItCannotScope()
    {
        var store = Store();

        (await new LedgerReportingPartnersCapitalSource(null).CaptureAsync(Parameters("2026-06"))).Should().BeNull(
            "no journal store means no certified source");
        (await new LedgerReportingPartnersCapitalSource(store.Object).CaptureAsync(Parameters("2026-07"))).Should().BeNull(
            "an unknown period must not certify a statement for some other period");
        (await new LedgerReportingPartnersCapitalSource(store.Object).CaptureAsync(Parameters("2026-06", withBook: false))).Should().BeNull(
            "a run with no ledger book id cannot be scoped to one book");
        (await new LedgerReportingPartnersCapitalSource(store.Object).CaptureAsync(Parameters("  "))).Should().BeNull();
    }

    private static ReportingRunParametersDto Parameters(string periodId, bool withBook = true) => new(
        new ReportingRunScopeDto(Fund),
        periodId,
        new DateOnly(2026, 6, 30),
        new ReportingLedgerBookSelectionDto(LedgerBookId: withBook ? Book : null),
        ReportingAccountingBasisDto.Gaap,
        "USD",
        ReportingConsolidationLevelDto.Fund,
        ReportingOutputFormatDto.Pdf,
        ReportingFinalityDto.Draft,
        IncludeSupportingSchedules: false,
        IncludeEvidenceAppendix: false);

    private static Mock<ILedgerJournalStore> Store()
    {
        var dimensions = new LedgerLineDimensionSet(FundId: Fund, BookId: Book.ToString("D"));
        var lp1 = LedgerAccounts.InvestorCapitalFor("lp-1");
        var lp2 = LedgerAccounts.InvestorCapitalFor("lp-2");
        var records = new[]
        {
            Journal(new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero), "May contribution", dimensions,
                (LedgerAccounts.Cash, 5_000_000m, 0m), (lp1, 0m, 5_000_000m)),
            Journal(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), "June contribution", dimensions,
                (LedgerAccounts.Cash, 1_000_000m, 0m), (lp2, 0m, 1_000_000m)),
            Journal(new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero), "June interest", dimensions,
                (LedgerAccounts.Cash, 50_000m, 0m), (LedgerAccounts.CashInterestIncome, 0m, 50_000m)),
        };

        var store = new Mock<ILedgerJournalStore>();
        store.Setup(item => item.ListPeriodsAsync(Book, It.IsAny<string?>(), Fund, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([June]);
        store.Setup(item => item.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LedgerJournalEntryQuery query, CancellationToken _) => records
                .Where(record => record.AggregateId == query.LedgerBookId
                    && (query.OccurredTo is not { } to || record.Entry.Timestamp <= to))
                .ToArray());
        return store;
    }

    private static LedgerJournalEntryRecord Journal(
        DateTimeOffset at, string description, LedgerLineDimensionSet dimensions,
        params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var id = Guid.NewGuid();
        var entry = new JournalEntry(id, at, description,
            lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, at, line.Account, line.Debit, line.Credit, description, dimensions,
                new LedgerEntryCurrency("USD", "USD", line.Debit, line.Credit, 1m))).ToArray(),
            new(EffectiveDate: DateOnly.FromDateTime(at.UtcDateTime)));
        return new LedgerJournalEntryRecord(entry, Book, PeriodId, null, null, 1, at);
    }
}
