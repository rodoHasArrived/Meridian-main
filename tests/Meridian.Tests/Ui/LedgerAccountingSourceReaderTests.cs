using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerAccountingSourceReaderTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid PeriodId = Guid.NewGuid();
    private static readonly Guid AggregateId = Guid.NewGuid();
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly LedgerAccountingPeriod Period = new(PeriodId, BookId, 2026, 11, "November",
        new(2026, 11, 1), new(2026, 11, 30), "Open", Timestamp, null, 1);

    [Fact]
    public async Task PaperReader_FullRetainedFillPopulationIncludesUnappliedClaimsAndCommission()
    {
        var store = Substitute.For<IPaperSessionStore>();
        var context = new TradeFillLedgerPostingContext("test", AggregateId, PeriodId, BookId);
        store.LoadAllSessionsAsync(Arg.Any<CancellationToken>()).Returns(new[] {
            new PersistedSessionRecord("session-a", "strategy-a", null, 100m, Timestamp, null, true, [],
                AccountingContext: context, FinancialAccountId: "default") });
        var fill = new ExecutionReport
        {
            OrderId = "order-a",
            Symbol = "ABC",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            FilledQuantity = 1m,
            FillPrice = 10m,
            Commission = 1m,
            Timestamp = Timestamp
        };
        var retained = PaperSessionFillRecord.CreateCanonical(fill, isApplied: false);
        store.LoadFillRecordsAsync("session-a", Arg.Any<CancellationToken>()).Returns(new[] { retained });
        var reader = new PaperSessionAccountingSourceReader(store, new("session-a", AggregateId, BookId, PeriodId, "default"));

        var result = await reader.ReadAsync(new(BookId, PeriodId, AggregateId, "default", [LedgerAccountingSourceKind.Fill]), Period);

        result.IsComplete.Should().BeTrue();
        result.Expectations.Should().ContainSingle().Which.SourceId.Should().Be(retained.FillId.ToString("D"));
        result.Expectations[0].RequiredPostingKeys.Should().BeEquivalentTo(new[] { "trade-fill", "commission" });
    }

    [Fact]
    public async Task PaperReader_AnotherBookBindingCannotClaimCoverage()
    {
        var store = Substitute.For<IPaperSessionStore>();
        var reader = new PaperSessionAccountingSourceReader(store, new("session-a", AggregateId, BookId, PeriodId, "default"));
        var result = await reader.ReadAsync(new(Guid.NewGuid(), PeriodId, AggregateId, "default", [LedgerAccountingSourceKind.Fill]), Period);
        result.IsComplete.Should().BeFalse();
        await store.DidNotReceive().LoadFillRecordsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoanReader_DoesNotReadUnscopedLoanHistoryWithoutAnAuthoritativeBookBinding()
    {
        var loans = Substitute.For<IDirectLendingStateStore>();
        var journals = Substitute.For<ILedgerJournalStore>();
        journals.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerJournalEntryRecord>());

        var result = await new LoanAccountingSourceReader(loans, journals).ReadAsync(
            new(BookId, PeriodId, AggregateId, AggregateId.ToString("D"), [LedgerAccountingSourceKind.LoanEvent]), Period);

        result.IsComplete.Should().BeFalse();
        result.Limitation.Should().Contain("loan-to-book binding");
        await loans.DidNotReceive().LoadContractProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await loans.DidNotReceive().GetHistoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoanReader_UsesIndependentlyRetainedHistoryIncludingReversalAndZeroImpactEvents()
    {
        var loans = Substitute.For<IDirectLendingStateStore>();
        var journals = Substitute.For<ILedgerJournalStore>();
        var journalId = Guid.NewGuid();
        var entry = new JournalEntry(journalId, Timestamp, "Loan funding", [
            new(Guid.NewGuid(), journalId, Timestamp, LedgerAccounts.Cash, 10m, 0m, "Loan funding"),
            new(Guid.NewGuid(), journalId, Timestamp, LedgerAccounts.CapitalAccount, 0m, 10m, "Loan funding")],
            new(Institution: "DirectLending", FinancialAccountId: AggregateId.ToString("D")));
        journals.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new LedgerJournalEntryRecord(entry, AggregateId, PeriodId, null, null, 1, Timestamp) });
        loans.LoadContractProjectionAsync(AggregateId, Arg.Any<CancellationToken>())
            .Returns(new LoanContractDetailDto(AggregateId, "Loan", new(Guid.NewGuid(), "Borrower", null),
                LoanStatus.Active, new(2026, 11, 1), null, null, 1, null!, []));
        LoanEventLineageDto Event(string kind, string payload) => new(Guid.NewGuid(), 1, kind, 1,
            new(2026, 11, 1), Timestamp, payload, null, null, null, "test", false);
        loans.GetHistoryAsync(AggregateId, Arg.Any<CancellationToken>()).Returns(new[] {
            Event("loan.drawdown-booked", """{"amount":10}"""),
            Event("loan.daily-accrual-reversed", """{"interestAmount":1}"""),
            Event("loan.drawdown-booked", """{"amount":0}""") });

        var result = await new LoanAccountingSourceReader(loans, journals).ReadAsync(
            new(BookId, PeriodId, AggregateId, AggregateId.ToString("D"), [LedgerAccountingSourceKind.LoanEvent]), Period);

        result.IsComplete.Should().BeTrue();
        result.Expectations.Should().HaveCount(3);
        result.Expectations.SelectMany(source => source.RequiredPostingKeys).Should()
            .BeEquivalentTo(new[] { "loan.drawdown-booked", "loan.daily-accrual-reversed" });
    }

    [Fact]
    public async Task AssetReader_IndependentUnpostedSourceRequiresOriginatingJournal()
    {
        var source = AssetSource("account-a");
        var store = Substitute.For<IAssetAccountingEventProjectionStore>();
        store.ListByBookPeriodAsync(BookId, PeriodId, Arg.Any<CancellationToken>()).Returns(new[] { new AssetAccountingEventProjectionRecord(source, "retained") });
        var result = await new AssetAccountingSourceReader(store).ReadAsync(
            new(BookId, PeriodId, BookId, "account-a", [LedgerAccountingSourceKind.AssetEvent]), Period);
        result.IsComplete.Should().BeTrue();
        result.Expectations.Should().ContainSingle().Which.RequiredPostingKeys.Should().ContainSingle()
            .Which.Should().Be("AssetAccounting.Valuation");
        var report = LedgerAccountingCompleteness.Reconcile(result.Expectations, [], result.IsComplete, true);
        report.IsComplete.Should().BeFalse();
        report.Issues.Should().ContainSingle().Which.Kind.Should().Be(LedgerAccountingCompletenessIssueKind.MissingJournal);
    }

    [Fact]
    public async Task AssetReader_MissingAccountLineageCannotBecomeZeroSourcePopulation()
    {
        var store = Substitute.For<IAssetAccountingEventProjectionStore>();
        store.ListByBookPeriodAsync(BookId, PeriodId, Arg.Any<CancellationToken>()).Returns(new[] { new AssetAccountingEventProjectionRecord(AssetSource(null), "retained") });
        var result = await new AssetAccountingSourceReader(store).ReadAsync(
            new(BookId, PeriodId, BookId, "account-a", [LedgerAccountingSourceKind.AssetEvent]), Period);
        result.IsComplete.Should().BeFalse();
        result.Limitation.Should().Contain("account lineage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task AssetReader_UnprojectedSourceCannotAttestZeroAccountingImpact(int eventAmount)
    {
        var source = AssetSource("account-a") with { EventAmount = eventAmount, ProjectedEffect = null };
        var store = Substitute.For<IAssetAccountingEventProjectionStore>();
        store.ListByBookPeriodAsync(BookId, PeriodId, Arg.Any<CancellationToken>())
            .Returns(new[] { new AssetAccountingEventProjectionRecord(source, "retained") });

        var result = await new AssetAccountingSourceReader(store).ReadAsync(
            new(BookId, PeriodId, BookId, "account-a", [LedgerAccountingSourceKind.AssetEvent]), Period);

        result.IsComplete.Should().BeFalse();
        result.Limitation.Should().Contain("projected accounting effect");
        LedgerAccountingCompleteness.Reconcile(result.Expectations, [], result.IsComplete, true)
            .IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task AssetReader_RefusesUnbalancedRetainedProjectedEffect()
    {
        var source = AssetSource("account-a");
        source = source with { ProjectedEffect = source.ProjectedEffect! with { TotalCredits = 9m } };
        var store = Substitute.For<IAssetAccountingEventProjectionStore>();
        store.ListByBookPeriodAsync(BookId, PeriodId, Arg.Any<CancellationToken>())
            .Returns(new[] { new AssetAccountingEventProjectionRecord(source, "retained") });

        var read = () => new AssetAccountingSourceReader(store).ReadAsync(
            new(BookId, PeriodId, BookId, "account-a", [LedgerAccountingSourceKind.AssetEvent]), Period);

        await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*unbalanced or inconsistent*");
    }

    [Fact]
    public async Task AssetReader_SourceInAnotherPeriodRefusesScopeLeak()
    {
        var source = AssetSource("account-a");
        source = source with { Scope = source.Scope with { PeriodId = Guid.NewGuid() } };
        var store = Substitute.For<IAssetAccountingEventProjectionStore>();
        store.ListByBookPeriodAsync(BookId, PeriodId, Arg.Any<CancellationToken>()).Returns(new[] { new AssetAccountingEventProjectionRecord(source, "retained") });
        var act = () => new AssetAccountingSourceReader(store).ReadAsync(
            new(BookId, PeriodId, BookId, "account-a", [LedgerAccountingSourceKind.AssetEvent]), Period);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private static AssetAccountingEventSpineDto AssetSource(string? accountId)
    {
        var eventId = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 1);
        var economicEvent = new EconomicEventReferenceDto(eventId, "AssetAccounting.Valuation", 1, date, Timestamp, "AssetOperations");
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "valuation", "v1", "v1", "base", date, Timestamp, "AssetOperations", null, economicEvent);
        var scope = new AssetAccountingEventScopeDto(Guid.NewGuid(), 1, Guid.NewGuid(), 1, BookId, PeriodId,
            AccountingBasisKindDto.Primary, "fund-a", Dimensions: new(AccountId: accountId));
        return new(eventId, AssetAccountingEventKindDto.Valuation, 1, 1, date, 10m, "USD", scope, economicEvent, lineage,
            ProjectedEffect: new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey,
                lineage.ModelVersion, date, 10m, 10m, "USD", [
                    new("Securities", 10m, 0m, "USD"), new("Unrealized Gain", 0m, 10m, "USD")]));
    }
}
