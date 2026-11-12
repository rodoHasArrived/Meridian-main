using FluentAssertions;
using Meridian.Ledger;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerAccountingCompletenessReadServiceTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid PeriodId = Guid.NewGuid();
    private static readonly Guid AggregateId = Guid.NewGuid();
    private static readonly Guid FillId = Guid.NewGuid();
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly LedgerAccountingCompletenessQuery Query = new(BookId, PeriodId, AggregateId,
        "account-a", [LedgerAccountingSourceKind.Fill]);

    [Fact]
    public async Task ReadAsync_IndependentlyReadsSourcesAndExactJournalScope()
    {
        var store = Store([Record()]);
        var reader = Substitute.For<ILedgerAccountingSourceReader>();
        reader.SourceKind.Returns(LedgerAccountingSourceKind.Fill);
        reader.ReadAsync(Query, Arg.Any<LedgerAccountingPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new LedgerAccountingSourcePopulation([
                new(new(BookId.ToString("D"), "account-a"), LedgerAccountingSourceKind.Fill,
                    FillId.ToString("D"), ["trade-fill"])], true));
        var service = new LedgerAccountingCompletenessReadService(store, readers: [reader]);

        var result = await service.ReadAsync(Query);

        result.Report.IsComplete.Should().BeTrue();
        result.Report.MatchedJournalCount.Should().Be(1);
        await store.Received(1).QueryAsync(Arg.Is<LedgerJournalEntryQuery>(query =>
            query.LedgerBookId == BookId && query.PeriodId == PeriodId && query.AggregateId == AggregateId),
            Arg.Any<CancellationToken>());
        await reader.Received(1).ReadAsync(Query, Arg.Any<LedgerAccountingPeriod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_NoSourceReader_CannotReportEmptyPopulationComplete()
    {
        var service = new LedgerAccountingCompletenessReadService(Store([]));
        var result = await service.ReadAsync(Query);
        result.Report.IsComplete.Should().BeFalse();
        result.Coverage.Should().ContainSingle().Which.IsComplete.Should().BeFalse();
        result.Coverage[0].Limitation.Should().Contain("No complete retained source reader");
    }

    [Fact]
    public async Task ReadAsync_OtherAccountPostingDoesNotSatisfyExpectedReceipt()
    {
        var reader = Substitute.For<ILedgerAccountingSourceReader>();
        reader.SourceKind.Returns(LedgerAccountingSourceKind.Fill);
        reader.ReadAsync(Query, Arg.Any<LedgerAccountingPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new LedgerAccountingSourcePopulation([
                new(new(BookId.ToString("D"), "account-a"), LedgerAccountingSourceKind.Fill,
                    FillId.ToString("D"), ["trade-fill"])], true));
        var result = await new LedgerAccountingCompletenessReadService(Store([Record("account-b")]), readers: [reader]).ReadAsync(Query);
        result.Report.IsComplete.Should().BeFalse();
        result.Report.Issues.Select(issue => issue.Kind).Should().BeEquivalentTo(
            new[] { LedgerAccountingCompletenessIssueKind.MissingJournal, LedgerAccountingCompletenessIssueKind.ScopeMismatch });
    }

    [Fact]
    public async Task ReadAsync_IndependentlySourcedLoanReversalCountsItsOwnAdjustmentJournal()
    {
        var source = Guid.NewGuid();
        var query = Query with { SourceKinds = [LedgerAccountingSourceKind.LoanEvent] };
        var original = Record();
        var entry = new JournalEntry(original.Entry.JournalEntryId, Timestamp, original.Entry.Description,
            original.Entry.Lines, new(ActivityType: "loan.daily-accrual-reversed", Institution: "DirectLending",
                FinancialAccountId: "account-a", Tags: new Dictionary<string, string>
                {
                    [LedgerJournalReversal.ReversalOfTag] = Guid.NewGuid().ToString("D")
                }));
        var record = original with
        {
            Entry = entry,
            SourceEventId = source,
            RuleId = "loan.daily-accrual-reversed",
            PostingKind = LedgerPostingKindDto.Adjustment
        };
        var reader = Substitute.For<ILedgerAccountingSourceReader>();
        reader.SourceKind.Returns(LedgerAccountingSourceKind.LoanEvent);
        reader.ReadAsync(query, Arg.Any<LedgerAccountingPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new LedgerAccountingSourcePopulation([
                new(new(BookId.ToString("D"), "account-a"), LedgerAccountingSourceKind.LoanEvent,
                    source.ToString("D"), ["loan.daily-accrual-reversed"])], true));

        var result = await new LedgerAccountingCompletenessReadService(Store([record]), readers: [reader]).ReadAsync(query);

        result.Report.IsComplete.Should().BeTrue();
        result.Report.MatchedJournalCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_PeriodInAnotherBook_RefusesBeforeJournalRead()
    {
        var store = Store([]);
        store.GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>()).Returns(Period() with { LedgerBookId = Guid.NewGuid() });
        var act = () => new LedgerAccountingCompletenessReadService(store).ReadAsync(Query);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different ledger book*");
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_StoreScopeLeak_RefusesInsteadOfHidingRecord()
    {
        var store = Store([Record() with { AggregateId = Guid.NewGuid() }]);
        var act = () => new LedgerAccountingCompletenessReadService(store).ReadAsync(Query);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*outside the requested accounting scope*");
    }

    [Fact]
    public async Task ReadAsync_UnknownSourceFamily_CannotClaimCompleteJournalPopulation()
    {
        var entry = Record().Entry;
        var record = new LedgerJournalEntryRecord(new(entry.JournalEntryId, entry.Timestamp, entry.Description,
            entry.Lines, new(FinancialAccountId: "account-a")), AggregateId, PeriodId, null, null, 1, Timestamp,
            SourceEventId: Guid.NewGuid());
        var result = await new LedgerAccountingCompletenessReadService(Store([record])).ReadAsync(Query);
        result.Report.JournalPopulationComplete.Should().BeFalse();
        result.Report.IsComplete.Should().BeFalse();
    }

    private static ILedgerJournalStore Store(IReadOnlyList<LedgerJournalEntryRecord> records)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>()).Returns(Period());
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns(records);
        return store;
    }

    private static LedgerAccountingPeriod Period() => new(PeriodId, BookId, 2026, 11, "November",
        new(2026, 11, 1), new(2026, 11, 30), "Open", Timestamp, null, 1);

    private static LedgerJournalEntryRecord Record(string accountId = "account-a")
    {
        var id = Guid.NewGuid();
        var entry = new JournalEntry(id, Timestamp, "trade", [
            new(Guid.NewGuid(), id, Timestamp, LedgerAccounts.CashAccount(accountId), 10m, 0m, "trade"),
            new(Guid.NewGuid(), id, Timestamp, LedgerAccounts.CapitalAccountFor(accountId), 0m, 10m, "trade")],
            new(ActivityType: "trade-fill", FillId: FillId, FinancialAccountId: accountId));
        return new(entry, AggregateId, PeriodId, null, null, 1, Timestamp);
    }
}
