using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class LedgerAccountingSharedPaperScopeTests
{
    [Theory]
    [InlineData("session-a")]
    [InlineData("session-b")]
    [InlineData(null)]
    public async Task SharedScope_ReadsAllIndependentSessionsIncludingClosedFillsAndFinancing(string? anchor)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.ReadAsync(anchor);

        result.Report.IsComplete.Should().BeTrue();
        result.Report.SourceEventCount.Should().Be(4);
        result.Report.ExpectedJournalCount.Should().Be(6, "both sessions require trade, commission and financing journals");
        result.Report.MatchedJournalCount.Should().Be(6);
        result.Coverage.Should().OnlyContain(coverage => coverage.IsComplete);
        foreach (var coverage in result.Coverage)
            coverage.CoveredSessionIds.Should().Equal("session-a", "session-b");
        await fixture.Sessions.DidNotReceive().LoadFillRecordsAsync("foreign-session", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnotherSessionsMissingSourceOrJournalRemainsABreak(bool removeJournal)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (removeJournal)
            fixture.Records.RemoveAll(record => record.Entry.Metadata.FillId == fixture.SecondFill.FillId
                && record.Entry.Metadata.ActivityType == "trade-fill");
        else
            fixture.Sessions.LoadFillRecordsAsync("session-b", Arg.Any<CancellationToken>())
                .Returns(Array.Empty<PaperSessionFillRecord>());

        var result = await fixture.ReadAsync("session-a");

        result.Report.IsComplete.Should().BeFalse();
        result.Report.Issues.Should().Contain(issue => issue.SourceId == fixture.SecondFill.FillId.ToString("D")
            && issue.Kind == (removeJournal ? LedgerAccountingCompletenessIssueKind.MissingJournal
                : LedgerAccountingCompletenessIssueKind.UnexpectedJournal));
    }

    [Fact]
    public async Task AnotherSessionsMissingFinancingJournalRemainsABreak()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Records.RemoveAll(record => record.SourceEventId == fixture.SecondCharge.SourceEventId);

        var result = await fixture.ReadAsync("session-a");

        result.Report.IsComplete.Should().BeFalse();
        result.Report.Issues.Should().ContainSingle().Which.Kind.Should().Be(LedgerAccountingCompletenessIssueKind.MissingJournal);
        result.Report.Issues[0].SourceId.Should().Be(fixture.SecondCharge.SourceEventId.ToString("D"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrphanReceiptIsNotFilteredAwayUsingKnownSourceMembership(bool financing)
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = Guid.NewGuid();
        fixture.Records.Add(fixture.Journal(source, financing ? "margin-interest" : "trade-fill", "unbound-session"));

        var result = await fixture.ReadAsync("session-a");

        result.Report.IsComplete.Should().BeFalse();
        result.Report.Issues.Should().ContainSingle().Which.Kind.Should().Be(LedgerAccountingCompletenessIssueKind.UnexpectedJournal);
        result.Report.Issues[0].SourceId.Should().Be(source.ToString("D"));
    }

    [Theory]
    [InlineData("missing-session")]
    [InlineData("foreign-session")]
    public async Task MissingOrWrongScopeAnchorCannotSilentlyWidenToOtherSessions(string anchor)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.ReadAsync(anchor);

        result.Report.IsComplete.Should().BeFalse();
        result.Coverage.Should().OnlyContain(coverage => !coverage.IsComplete && coverage.CoveredSessionIds.Count == 0);
        await fixture.Sessions.DidNotReceive().LoadFillRecordsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingAccountLineageInSharedLedgerScopeCannotBecomeAnEmptySuccessfulPopulation()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Catalog.Add(fixture.Session("unknown-account", financialAccountId: null));

        var result = await fixture.ReadAsync("session-a");

        result.Report.IsComplete.Should().BeFalse();
        result.Coverage.Should().OnlyContain(coverage => !coverage.IsComplete && coverage.Limitation!.Contains("financial-account lineage"));
        await fixture.Sessions.DidNotReceive().LoadFillRecordsAsync("unknown-account", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CoveredSessionIdentityDuplicatedInAnotherBookIsRejectedBeforeSourceReads()
    {
        await using var fixture = await Fixture.CreateAsync();
        var second = fixture.Catalog.Single(session => session.SessionId == "session-b");
        fixture.Catalog.Add(second with { AccountingContext = second.AccountingContext! with { LedgerBookId = Guid.NewGuid() } });

        var read = () => fixture.ReadAsync("session-a");

        await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*ambiguous session identity*");
        await fixture.Sessions.DidNotReceive().LoadFillRecordsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherRetainedFinancingScopeRequiresItsOwnSourceOwnerInsteadOfClaimingCompleteCoverage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var index = fixture.Catalog.FindIndex(session => session.SessionId == "session-b");
        fixture.Catalog[index] = fixture.Catalog[index] with
        { AccountingContext = fixture.Catalog[index].AccountingContext! with { PostingScope = "another-retained-owner" } };

        var result = await fixture.ReadAsync("session-a");

        result.Report.IsComplete.Should().BeFalse();
        var financing = result.Coverage.Single(coverage => coverage.SourceKind == LedgerAccountingSourceKind.FinancingCharge);
        financing.IsComplete.Should().BeFalse();
        financing.CoveredSessionIds.Should().Equal("session-a");
        financing.Limitation.Should().Contain("financing source owner");
        result.Coverage.Single(coverage => coverage.SourceKind == LedgerAccountingSourceKind.Fill)
            .CoveredSessionIds.Should().Equal("session-a", "session-b");
    }

    [Fact]
    public async Task OutOfPeriodSourceInAnotherBoundSessionCannotBeSilentlyExcludedFromCompleteCoverage()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Records.RemoveAll(record => record.Entry.Metadata.FillId == fixture.SecondFill.FillId);
        var source = PaperSessionFillRecord.CreateCanonical(new ExecutionReport
        {
            OrderId = "next-period-source",
            Symbol = "ABC",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            FilledQuantity = 1m,
            FillPrice = 10m,
            Timestamp = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)
        }, isApplied: false);
        fixture.Sessions.LoadFillRecordsAsync("session-b", Arg.Any<CancellationToken>()).Returns(new[] { source });

        var result = await fixture.ReadAsync("session-a");

        result.Report.IsComplete.Should().BeFalse();
        result.Coverage.Single(coverage => coverage.SourceKind == LedgerAccountingSourceKind.Fill)
            .Limitation.Should().Contain("bound accounting period");
    }

    private sealed class Fixture : IAsyncDisposable, ITradeFillLedgerPostingTarget
    {
        private static readonly DateTimeOffset At = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-completeness-" + Guid.NewGuid().ToString("N"));
        private readonly Guid _bookId = Guid.NewGuid();
        private readonly Guid _periodId = Guid.NewGuid();
        private readonly Guid _aggregateId = Guid.NewGuid();
        private readonly ILedgerJournalStore _journals = Substitute.For<ILedgerJournalStore>();
        private readonly TradeFillLedgerPostingContext _context;
        private readonly LedgerAccountingPeriod _period;
        private readonly PaperFinancingPostingService _financing;
        public IPaperSessionStore Sessions { get; } = Substitute.For<IPaperSessionStore>();
        public List<PersistedSessionRecord> Catalog { get; } = [];
        public List<LedgerJournalEntryRecord> Records { get; } = [];
        public PaperSessionFillRecord SecondFill { get; private set; } = null!;
        public PaperFinancingCharge SecondCharge { get; private set; } = null!;

        private Fixture()
        {
            _context = new("shared-context", _aggregateId, _periodId, _bookId, ExpectedPeriodVersion: 1);
            _period = new(_periodId, _bookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", At, null, 1);
            var book = new LedgerBookRecord(_bookId, "synthetic-shared-paper", Guid.NewGuid(),
                Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Fund, "Synthetic shared paper", "USD", At, At,
                AccountingPolicyId: _context.AccountingPolicyId, AccountingPolicyVersion: _context.AccountingPolicyVersion);
            _financing = new(_root, _context, this,
                (_, _) => Task.FromResult<LedgerAccountingPeriod?>(_period),
                (_, _) => Task.FromResult<LedgerBookRecord?>(book));
            _journals.GetPeriodAsync(_periodId, Arg.Any<CancellationToken>()).Returns(_period);
            _journals.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(Records.ToArray()));
            Sessions.LoadAllSessionsAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<PersistedSessionRecord>>(Catalog.ToArray()));
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.Catalog.Add(fixture.Session("session-a"));
            fixture.Catalog.Add(fixture.Session("session-b") with { IsActive = false, ClosedAt = At.AddDays(1) });
            fixture.Catalog.Add(fixture.Session("foreign-session") with
            { AccountingContext = fixture._context with { LedgerBookId = Guid.NewGuid() } });
            foreach (var session in new[] { "session-a", "session-b" })
            {
                var fill = PaperSessionFillRecord.CreateCanonical(new ExecutionReport
                {
                    OrderId = "order-" + session,
                    Symbol = "ABC",
                    Side = OrderSide.Buy,
                    ReportType = ExecutionReportType.Fill,
                    OrderStatus = OrderStatus.Filled,
                    FilledQuantity = 1m,
                    FillPrice = 10m,
                    Commission = 1m,
                    Timestamp = At
                }, isApplied: true);
                fixture.Sessions.LoadFillRecordsAsync(session, Arg.Any<CancellationToken>()).Returns(new[] { fill });
                fixture.Records.Add(fixture.Journal(fill.FillId, "trade-fill", session));
                fixture.Records.Add(fixture.Journal(fill.FillId, "commission", session));
                var charge = new PaperFinancingCharge(session, "default", new(2026, 11, 11), 1m,
                    "broker-charge", "Assessed financing", "operator");
                await fixture._financing.PostAsync(charge);
                if (session == "session-b")
                { fixture.SecondFill = fill; fixture.SecondCharge = charge; }
            }
            return fixture;
        }

        public PersistedSessionRecord Session(string id, string? financialAccountId = "default")
            => new(id, "strategy-" + id, null, 100m, At, null, true, [],
                AccountingContext: _context, FinancialAccountId: financialAccountId);

        public Task<LedgerAccountingCompletenessReadResult> ReadAsync(string? anchor)
            => new LedgerAccountingCompletenessReadService(_journals, sessions: Sessions, financing: _financing)
                .ReadAsync(new(_bookId, _periodId, _aggregateId, "default",
                    [LedgerAccountingSourceKind.Fill, LedgerAccountingSourceKind.FinancingCharge], anchor));

        public LedgerJournalEntryRecord Journal(Guid source, string activity, string session)
        {
            var id = Guid.NewGuid();
            var journal = new JournalEntry(id, At, activity, [
                new(Guid.NewGuid(), id, At, LedgerAccounts.Cash, 1m, 0m, activity),
                new(Guid.NewGuid(), id, At, LedgerAccounts.CapitalAccount, 0m, 1m, activity)],
                new(ActivityType: activity, FillId: activity == "margin-interest" ? null : source,
                    Tags: new Dictionary<string, string> { ["source.sessionId"] = session }));
            return new(journal, _aggregateId, _periodId, null, null, Records.Count + 1, At,
                RuleId: activity == "margin-interest" ? "execution.margin-interest" : activity, SourceEventId: source);
        }

        public Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(LedgerJournalEntryWrite write, CancellationToken ct = default)
        {
            var record = new LedgerJournalEntryRecord(write.Entry, write.AggregateId, write.PeriodId, write.CommandId,
                write.CorrelationId, Records.Count + 1, At, write.AccountingBasis, write.AccountingPolicyId,
                write.AccountingPolicyVersion, write.RuleId, write.RuleVersion, write.SourceEventId,
                write.SourceJournalEntryId, write.PostingKind, write.AdjustmentApproval);
            Records.Add(record);
            return Task.FromResult(new TradeFillLedgerPostingConfirmation(record, true));
        }

        public async ValueTask DisposeAsync()
        {
            await _financing.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }
}
