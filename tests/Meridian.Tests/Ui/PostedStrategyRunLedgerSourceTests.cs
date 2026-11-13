using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class PostedStrategyRunLedgerSourceTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid PeriodId = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly StrategyRunReadScope Scope = new("tenant-a", "company-a");
    private static StrategyRunEntry Run => new("run-a", "strategy-a", "Strategy", RunType.Paper,
        At, null, null, FundProfileId: "fund-a", FundAccountId: AccountId,
        ParameterSet: new Dictionary<string, string>
        {
            ["workstationTenantId"] = "tenant-a",
            ["workstationCompanyId"] = "company-a"
        });

    [Fact]
    public async Task PostedRunRead_SelectsExactRun_WithoutDependingOnTerminalMetrics()
    {
        var (store, registry) = Stores([Record("run-a"), Record("run-b")]);
        var source = new PostedStrategyRunLedgerSource(store, registry);
        var result = await source.ReadAsync(Run, Scope);
        result.Should().NotBeNull();
        result!.Journal.Should().ContainSingle().Which.Metadata.Tags!["strategy.runId"].Should().Be("run-a");

        var repository = new StrategyRunStore();
        await repository.RecordRunAsync(Run);
        var reads = new StrategyRunReadService(repository, new PortfolioReadService(), new LedgerReadService(),
            postedLedgerSource: source);
        var summary = await reads.GetLedgerSummaryAsync(Run.RunId, Scope);
        summary.Should().NotBeNull();
        summary!.JournalEntryCount.Should().Be(1);
        (await reads.GetLedgerSnapshotAsync(Run.RunId, Scope, At.AddTicks(-1)))!.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingAttribution_PreservesRetainedLedgerInsteadOfPresentingAnEmptyPostedBook()
    {
        var retained = new Meridian.Ledger.Ledger();
        retained.Post(Record(null).Entry);
        var run = Run with { Metrics = Result(retained), LedgerReference = "retained-run-ledger" };
        var (store, registry) = Stores([Record(null), Record("run-b")]);
        var source = new PostedStrategyRunLedgerSource(store, registry);
        (await source.ReadAsync(run, Scope)).Should().BeNull("legacy journals do not attest run attribution");
        var (reads, continuity) = await ReadersAsync(run, source);

        var snapshot = await reads.GetLedgerSnapshotAsync(run.RunId, Scope);
        var summary = await reads.GetLedgerSummaryAsync(run.RunId, Scope);
        var detail = await continuity.GetRunContinuityAsync(run.RunId, Scope);

        snapshot!.Journal.Should().ContainSingle().Which.JournalEntryId.Should().Be(retained.Journal.Single().JournalEntryId);
        summary!.JournalEntryCount.Should().Be(1);
        detail!.ContinuityStatus.HasLedger.Should().BeTrue();
        detail.ContinuityStatus.Warnings.Should().NotContain(warning => warning.Code == "missing-ledger");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsOfSummary_CutsTheAuthorizedPostedPopulationOrRetainedFallback(bool hasPostedAttribution)
    {
        var accountId = AccountId.ToString("D");
        var retained = new Meridian.Ledger.Ledger();
        var posted = new Meridian.Ledger.Ledger();
        void Add(Meridian.Ledger.Ledger ledger, DateTimeOffset timestamp, decimal amount, bool attributed)
            => ledger.PostLines(timestamp, "Scoped historical posting",
                [(LedgerAccounts.Securities("SPY", accountId), amount, 0m),
                 (LedgerAccounts.CashFor(accountId), 0m, amount)],
                new JournalEntryMetadata(StrategyId: "strategy-a", FinancialAccountId: accountId,
                    Tags: attributed ? new Dictionary<string, string> { ["strategy.runId"] = Run.RunId } : null));
        Add(retained, At, 37m, false);
        Add(retained, At.AddHours(1), 71m, false);
        Add(posted, At, 10m, hasPostedAttribution);
        Add(posted, At.AddHours(1), 20m, hasPostedAttribution);
        var records = posted.Journal.Select(entry => new LedgerJournalEntryRecord(entry, Guid.NewGuid(),
            PeriodId, null, null, 1, entry.Timestamp)).ToArray();
        var (store, registry) = Stores(records);
        var run = Run with { Metrics = Result(retained), LedgerReference = "retained-run-ledger" };
        var (reads, _) = await ReadersAsync(run, new PostedStrategyRunLedgerSource(store, registry));

        var summary = (await reads.GetLedgerSummaryAsync(run.RunId, At, Scope))!;
        var snapshot = (await reads.GetLedgerSnapshotAsync(run.RunId, Scope, At))!;

        summary.AsOf.Should().Be(At);
        summary.JournalEntryCount.Should().Be(1);
        summary.LedgerEntryCount.Should().Be(2);
        summary.TrialBalance.Single(line => line.AccountName == "Securities").Balance
            .Should().Be(hasPostedAttribution ? 10m : 37m);
        snapshot.GetBalance(LedgerAccounts.Securities("SPY", accountId))
            .Should().Be(hasPostedAttribution ? 10m : 37m);
        summary.Journal.Should().ContainSingle().Which.Timestamp.Should().Be(At);
        (await reads.GetLedgerSummaryAsync(run.RunId, Scope))!.JournalEntryCount.Should().Be(2);
        retained.Journal.Should().HaveCount(2);
        posted.Journal.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("accountScopeId", "acct-paper")]
    [InlineData("accountId", "historical-broker-account")]
    public async Task LegacyAccountLabel_PreservesAuthorizedFullRunDetailAndRetainedLedger(string key, string label)
    {
        var retained = new Meridian.Ledger.Ledger();
        retained.Post(Record(null).Entry);
        var parameters = new Dictionary<string, string>(Run.ParameterSet!) { [key] = label };
        var run = Run with
        {
            FundAccountId = null,
            ParameterSet = parameters,
            Metrics = Result(retained),
            LedgerReference = "retained-legacy-run-ledger"
        };
        var (store, registry) = Stores([Record("run-a")]);
        var source = new PostedStrategyRunLedgerSource(store, registry);
        var (reads, continuity) = await ReadersAsync(run, source);

        var detail = await reads.GetRunDetailAsync(run.RunId, Scope);
        detail.Should().NotBeNull();
        detail!.Ledger!.JournalEntryCount.Should().Be(1);
        (await reads.GetLedgerSnapshotAsync(run.RunId, Scope))!.Journal.Should().ContainSingle()
            .Which.JournalEntryId.Should().Be(retained.Journal.Single().JournalEntryId);
        (await continuity.GetRunContinuityAsync(run.RunId, Scope))!.ContinuityStatus.Warnings
            .Should().NotContain(warning => warning.Code == "missing-ledger");
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ListLedgerBooksAsync(Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>());

        var foreignScope = Scope with { TenantId = "other-tenant", CompanyId = "other-company" };
        (await reads.GetRunDetailAsync(run.RunId, foreignScope)).Should().BeNull("legacy labels do not weaken retained run authorization");
    }

    [Theory]
    [InlineData("ledgerBookId")]
    [InlineData("ledgerBookScopeId")]
    [InlineData("bookId")]
    [InlineData("bookScopeId")]
    public async Task OpaqueBookAliasWithTypedFundAccountAndLegacyAccountLabel_PreservesRetainedDetail(string bookKey)
    {
        var retained = new Meridian.Ledger.Ledger();
        retained.Post(Record(null).Entry);
        var run = Run with
        {
            Metrics = Result(retained),
            LedgerReference = "retained-legacy-book",
            ParameterSet = new Dictionary<string, string>(Run.ParameterSet!)
            {
                ["accountScopeId"] = "acct-paper",
                [bookKey] = "book-paper"
            }
        };
        var (store, registry) = Stores([]);
        var (reads, _) = await ReadersAsync(run, new PostedStrategyRunLedgerSource(store, registry));
        var detail = await reads.GetRunDetailAsync(run.RunId, Scope);
        detail!.Ledger!.JournalEntryCount.Should().Be(1);
        (await reads.GetLedgerSnapshotAsync(run.RunId, Scope))!.Journal.Should().ContainSingle()
            .Which.JournalEntryId.Should().Be(retained.Journal.Single().JournalEntryId);
        (await reads.GetRunDetailAsync(run.RunId, Scope with { CompanyId = "other-company" })).Should().BeNull();
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().GetLedgerBookAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ListLedgerBooksAsync(Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UuidAccountAliasWithoutTypedFundAccount_StillRequiresAuthorizedBookAndProvenance()
    {
        var run = Run with
        {
            FundAccountId = null,
            ParameterSet = new Dictionary<string, string>(Run.ParameterSet!) { ["accountScopeId"] = AccountId.ToString("D") }
        };
        var (store, registry) = Stores([Record("run-a")]);
        var result = await new PostedStrategyRunLedgerSource(store, registry).ReadAsync(run, Scope);
        result!.Journal.Should().ContainSingle();
        registry.ResolveAsync("fund-a", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("fund-a", "other-tenant", "other-company"));
        (await new PostedStrategyRunLedgerSource(store, registry).ReadAsync(run, Scope)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyAccountLabelAlongsideConsistentUuidBinding_PreservesRetainedDetailWithoutPostedGuess(bool typedBinding)
    {
        var retained = new Meridian.Ledger.Ledger();
        retained.Post(Record(null).Entry);
        var parameters = new Dictionary<string, string>(Run.ParameterSet!) { ["accountScopeId"] = "acct-paper" };
        if (!typedBinding)
            parameters["accountId"] = AccountId.ToString("D");
        var run = Run with
        {
            FundAccountId = typedBinding ? AccountId : null,
            ParameterSet = parameters,
            Metrics = Result(retained),
            LedgerReference = "retained-account-label"
        };
        var (store, registry) = Stores([]);
        var (reads, _) = await ReadersAsync(run, new PostedStrategyRunLedgerSource(store, registry));
        (await reads.GetRunDetailAsync(run.RunId, Scope))!.Ledger!.JournalEntryCount.Should().Be(1);
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ListLedgerBooksAsync(Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ledgerBookId")]
    [InlineData("ledgerBookScopeId")]
    [InlineData("bookId")]
    [InlineData("bookScopeId")]
    public async Task UuidBookAlias_StillResolvesOnlyTheBoundAuthorizedBook(string bookKey)
    {
        var run = Run with
        {
            ParameterSet = new Dictionary<string, string>(Run.ParameterSet!) { [bookKey] = BookId.ToString("D") }
        };
        var (store, registry) = Stores([Record("run-a")]);
        var source = new PostedStrategyRunLedgerSource(store, registry);
        (await source.ReadAsync(run, Scope))!.Journal.Should().ContainSingle();
        await store.Received(1).GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>());
        registry.ResolveAsync("fund-a", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("fund-a", "other-tenant", "company-a"));
        (await source.ReadAsync(run, Scope)).Should().BeNull();
        await store.Received(1).QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("book")]
    [InlineData("account-aliases")]
    [InlineData("typed-account")]
    [InlineData("empty-book")]
    public async Task OpaqueLabelsCannotMaskConflictingOrEmptyActualUuidBindings(string conflict)
    {
        var parameters = new Dictionary<string, string>(Run.ParameterSet!)
        {
            ["accountScopeId"] = "acct-paper",
            ["bookScopeId"] = "book-paper"
        };
        Guid? typedAccount = AccountId;
        if (conflict == "book")
        {
            parameters["ledgerBookId"] = BookId.ToString("D");
            parameters["bookId"] = Guid.NewGuid().ToString("D");
        }
        else if (conflict == "empty-book")
            parameters["ledgerBookId"] = Guid.Empty.ToString("D");
        else if (conflict == "account-aliases")
        {
            typedAccount = null;
            parameters["accountScopeId"] = AccountId.ToString("D");
            parameters["accountId"] = Guid.NewGuid().ToString("D");
        }
        else
            parameters["accountId"] = Guid.NewGuid().ToString("D");
        var run = Run with { FundAccountId = typedAccount, ParameterSet = parameters, Metrics = Result(new Meridian.Ledger.Ledger()) };
        var (store, registry) = Stores([]);
        var (reads, _) = await ReadersAsync(run, new PostedStrategyRunLedgerSource(store, registry));
        await reads.Invoking(service => service.GetRunDetailAsync(run.RunId, Scope)).Should().ThrowAsync<InvalidDataException>();
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().GetLedgerBookAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoAttributedOrRetainedPopulation_PreservesMissingLedgerWarning(bool hasLegacyPosting)
    {
        var (store, registry) = Stores(hasLegacyPosting ? [Record(null)] : []);
        var source = new PostedStrategyRunLedgerSource(store, registry);
        var (reads, continuity) = await ReadersAsync(Run, source);

        (await source.ReadAsync(Run, Scope)).Should().BeNull();
        (await reads.GetLedgerSnapshotAsync(Run.RunId, Scope)).Should().BeNull();
        (await reads.GetLedgerSummaryAsync(Run.RunId, Scope)).Should().BeNull();
        var detail = await continuity.GetRunContinuityAsync(Run.RunId, Scope);
        detail!.ContinuityStatus.HasLedger.Should().BeFalse();
        detail.ContinuityStatus.Warnings.Should().ContainSingle(warning => warning.Code == "missing-ledger");
    }

    [Fact]
    public async Task ExplicitlyRetainedEmptyLedger_RemainsAvailableForANewRun()
    {
        var run = Run with { Metrics = Result(new Meridian.Ledger.Ledger()), LedgerReference = "retained-empty-run-ledger" };
        var (store, registry) = Stores([]);
        var source = new PostedStrategyRunLedgerSource(store, registry);
        var (reads, continuity) = await ReadersAsync(run, source);

        (await source.ReadAsync(run, Scope)).Should().BeNull("an empty authorized book cannot attest posted run coverage");
        (await reads.GetLedgerSnapshotAsync(run.RunId, Scope))!.Journal.Should().BeEmpty();
        (await reads.GetLedgerSummaryAsync(run.RunId, Scope))!.JournalEntryCount.Should().Be(0);
        var detail = await continuity.GetRunContinuityAsync(run.RunId, Scope);
        detail!.ContinuityStatus.HasLedger.Should().BeTrue("the retained run explicitly supplies an empty ledger");
        detail.ContinuityStatus.Warnings.Should().NotContain(warning => warning.Code == "missing-ledger");
    }

    [Fact]
    public async Task ValidAttributedPopulation_StillPrecedesRetainedRunMetrics()
    {
        var retained = new Meridian.Ledger.Ledger();
        retained.Post(Record("run-b").Entry);
        var run = Run with { Metrics = Result(retained) };
        var posted = Record("run-a");
        var (store, registry) = Stores([posted]);
        var (reads, _) = await ReadersAsync(run, new PostedStrategyRunLedgerSource(store, registry));

        (await reads.GetLedgerSnapshotAsync(run.RunId, Scope))!.Journal.Should().ContainSingle()
            .Which.JournalEntryId.Should().Be(posted.Entry.JournalEntryId);
    }

    [Fact]
    public async Task ForeignBookOwnership_DoesNotQueryOrPresentAnEmptyAuthorizedBook()
    {
        var (store, registry) = Stores([Record("run-a")]);
        registry.ResolveAsync("fund-a", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-a", "tenant-b", "company-b"));
        var result = await new PostedStrategyRunLedgerSource(store, registry).ReadAsync(Run, Scope);
        result.Should().BeNull();
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchingRunTag_WithContradictoryAccountProvenance_FailsClosed()
    {
        var (store, registry) = Stores([Record("run-a", Guid.NewGuid().ToString("D"))]);
        var action = () => new PostedStrategyRunLedgerSource(store, registry).ReadAsync(Run, Scope);
        await action.Should().ThrowAsync<InvalidDataException>().WithMessage("*provenance conflicts*");
    }

    [Fact]
    public async Task ManyJournalsAcrossPeriods_ReadEachDistinctPeriodOncePerInvocation()
    {
        var secondPeriod = Guid.NewGuid();
        var records = Enumerable.Range(0, 120).Select(index => Record("run-a") with
        {
            PeriodId = index % 2 == 0 ? PeriodId : secondPeriod
        }).ToArray();
        var (store, registry) = Stores(records);
        store.GetPeriodAsync(secondPeriod, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(
            secondPeriod, BookId, 2026, 12, "Second retained period", new(2026, 11, 1), new(2026, 11, 30), "Open", At, null, 1));
        var source = new PostedStrategyRunLedgerSource(store, registry);

        (await source.ReadAsync(Run, Scope))!.Journal.Should().HaveCount(120);
        await store.Received(1).GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>());
        await store.Received(1).GetPeriodAsync(secondPeriod, Arg.Any<CancellationToken>());

        (await source.ReadAsync(Run, Scope))!.Journal.Should().HaveCount(120);
        await store.Received(2).GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>());
        await store.Received(2).GetPeriodAsync(secondPeriod, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PeriodAuthorityIsRecheckedOnNextRead_AndForeignBookCannotReusePriorCache()
    {
        var secondPeriod = Guid.NewGuid();
        var records = Enumerable.Range(0, 40).Select(index => Record("run-a") with
        {
            PeriodId = index % 2 == 0 ? PeriodId : secondPeriod
        }).ToArray();
        var (store, registry) = Stores(records);
        var authorized = new LedgerAccountingPeriod(secondPeriod, BookId, 2026, 12, "Second retained period",
            new(2026, 11, 1), new(2026, 11, 30), "Open", At, null, 1);
        store.GetPeriodAsync(secondPeriod, Arg.Any<CancellationToken>()).Returns(authorized);
        var source = new PostedStrategyRunLedgerSource(store, registry);
        (await source.ReadAsync(Run, Scope))!.Journal.Should().HaveCount(40);

        store.GetPeriodAsync(secondPeriod, Arg.Any<CancellationToken>()).Returns(authorized with { LedgerBookId = Guid.NewGuid() });
        await source.Invoking(reader => reader.ReadAsync(Run, Scope)).Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*different accounting book*");
        await store.Received(2).GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>());
        await store.Received(2).GetPeriodAsync(secondPeriod, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAuthoritativeAccountIdentity_IsNotTreatedAsAnAbsentLegacyBinding(bool typedIdentity)
    {
        var run = Run with
        {
            FundAccountId = typedIdentity ? Guid.Empty : null,
            ParameterSet = new Dictionary<string, string>(Run.ParameterSet!)
            {
                ["accountScopeId"] = typedIdentity ? "acct-paper" : Guid.Empty.ToString("D")
            }
        };
        var (store, registry) = Stores([]);
        await new PostedStrategyRunLedgerSource(store, registry).Invoking(source => source.ReadAsync(run, Scope))
            .Should().ThrowAsync<InvalidDataException>();
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingAccountingScope_NeverSelectsJournalsByStrategyAlone()
    {
        var (store, registry) = Stores([Record("run-a")]);
        var result = await new PostedStrategyRunLedgerSource(store, registry).ReadAsync(Run with { FundAccountId = null }, Scope);
        result.Should().BeNull();
        await store.DidNotReceive().ListLedgerBooksAsync(Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ledgerBookId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("accountId", "11111111-1111-1111-1111-111111111111")]
    public async Task InvalidExplicitScope_NeverFallsBackToAnotherBook(string key, string value)
    {
        var (store, registry) = Stores([Record("run-a")]);
        var run = Run with { ParameterSet = new Dictionary<string, string> { [key] = value } };
        var action = () => new PostedStrategyRunLedgerSource(store, registry).ReadAsync(run, Scope);
        await action.Should().ThrowAsync<InvalidDataException>();
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ListLedgerBooksAsync(Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>());
    }

    private static (ILedgerJournalStore, IFundProfileTenancyRegistry) Stores(IReadOnlyList<LedgerJournalEntryRecord> records)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var book = new LedgerBookRecord(BookId, "fund-a", AccountId, FundStructureNodeKindDto.Account,
            "Account book", "USD", At, At);
        store.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>()).Returns(book);
        store.ListLedgerBooksAsync(null, AccountId, null, Arg.Any<CancellationToken>()).Returns(new[] { book });
        store.QueryAsync(Arg.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == BookId),
            Arg.Any<CancellationToken>()).Returns(records);
        store.GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(
            PeriodId, BookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", At, null, 1));
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync("fund-a", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("fund-a", "tenant-a", "company-a"));
        return (store, registry);
    }

    private static async Task<(StrategyRunReadService Reads, StrategyRunContinuityService Continuity)> ReadersAsync(
        StrategyRunEntry run, PostedStrategyRunLedgerSource source)
    {
        var repository = new StrategyRunStore();
        await repository.RecordRunAsync(run);
        var reads = new StrategyRunReadService(repository, new PortfolioReadService(), new LedgerReadService(),
            postedLedgerSource: source);
        var reconciliation = new ReconciliationRunService(reads, new ReconciliationProjectionService(),
            new InMemoryReconciliationRunRepository());
        return (reads, new StrategyRunContinuityService(reads, new CashFlowProjectionService(repository), reconciliation));
    }

    private static BacktestResult Result(IReadOnlyLedger ledger) => new(
        new BacktestRequest(From: new(2026, 11, 1), To: new(2026, 11, 1), DataRoot: "./data"),
        new HashSet<string> { "SPY" }, [], [], [],
        new BacktestMetrics(100m, 100m, 0m, 0m, 0m, 0m, 0d, 0d, 0d, 0m, 0m, 0, 0d, 0d, 0, 0, 0,
            0m, 0m, 0m, 0d, new Dictionary<string, SymbolAttribution>()),
        ledger, TimeSpan.Zero, 0);

    private static LedgerJournalEntryRecord Record(string? runId, string? accountId = null)
    {
        accountId ??= AccountId.ToString("D");
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(At, "Fill", [(LedgerAccounts.Securities("SPY", accountId), 10m, 0m),
            (LedgerAccounts.CashFor(accountId), 0m, 10m)], new JournalEntryMetadata(
                StrategyId: "strategy-a", FinancialAccountId: accountId,
                Tags: runId is null ? null : new Dictionary<string, string> { ["strategy.runId"] = runId }));
        return new(ledger.Journal.Single(), Guid.NewGuid(), PeriodId, null, null, 1, At);
    }
}
