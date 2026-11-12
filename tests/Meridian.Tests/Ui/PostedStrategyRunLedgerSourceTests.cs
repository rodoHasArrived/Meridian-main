using FluentAssertions;
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
    public async Task MissingAccountingScope_NeverSelectsJournalsByStrategyAlone()
    {
        var (store, registry) = Stores([Record("run-a")]);
        var result = await new PostedStrategyRunLedgerSource(store, registry).ReadAsync(Run with { FundAccountId = null }, Scope);
        result.Should().BeNull();
        await store.DidNotReceive().ListLedgerBooksAsync(Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ledgerBookId", "malformed")]
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
        store.ListLedgerBooksAsync(null, AccountId, null, Arg.Any<CancellationToken>()).Returns(new[] { book });
        store.QueryAsync(Arg.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == BookId),
            Arg.Any<CancellationToken>()).Returns(records);
        store.GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(
            PeriodId, BookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", At, null, 1));
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync("fund-a", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("fund-a", "tenant-a", "company-a"));
        return (store, registry);
    }

    private static LedgerJournalEntryRecord Record(string runId, string? accountId = null)
    {
        accountId ??= AccountId.ToString("D");
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(At, "Fill", [(LedgerAccounts.Securities("SPY", accountId), 10m, 0m),
            (LedgerAccounts.CashFor(accountId), 0m, 10m)], new JournalEntryMetadata(
                StrategyId: "strategy-a", FinancialAccountId: accountId,
                Tags: new Dictionary<string, string> { ["strategy.runId"] = runId }));
        return new(ledger.Journal.Single(), Guid.NewGuid(), PeriodId, null, null, 1, At);
    }
}
