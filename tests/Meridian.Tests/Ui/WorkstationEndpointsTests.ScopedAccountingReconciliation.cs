using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScopedPaperReconciliation_UsesAuthorizedPostedNativeFactsAtTheRetainedSnapshotCutoff(bool bookOwnedByCaller)
    {
        var accountId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var run = ScopedAccountingRun("paper-native", accountId, securityId, at.AddMinutes(1));
        var journals = ScopedAccountingJournals(run, bookId, periodId, securityId, at);
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListLedgerBooksAsync(null, accountId, null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new LedgerBookRecord(bookId, "fund-native", accountId, FundStructureNodeKindDto.Account, "Synthetic account", "USD", at, at)
        });
        store.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(
            periodId, bookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", at, null, 1));
        store.QueryAsync(Arg.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == bookId), Arg.Any<CancellationToken>()).Returns(journals);
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync("fund-native", Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("fund-native",
            bookOwnedByCaller ? "tenant-native" : "other-tenant", "company-native"));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IStrategyRunLedgerSource>(new PostedStrategyRunLedgerSource(store, registry));
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserTenantId: "tenant-native", currentUserCompanyId: "company-native");
        var runs = app.Services.GetRequiredService<IStrategyRepository>();
        await runs.RecordRunAsync(run);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest(run.RunId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions))!;
        if (bookOwnedByCaller)
        {
            result.Breaks.Should().BeEmpty();
            result.Summary.LedgerAsOf.Should().Be(at.AddMinutes(1));
            result.Matches.SelectMany(static item => item.Measures).Should().ContainSingle(item =>
                item.Kind == ReconciliationBreakMeasureKindDto.Quantity && item.Expected == 10m && item.Actual == 10m);
            result.Matches.SelectMany(static item => item.Measures).Should().ContainSingle(item =>
                item.Kind == ReconciliationBreakMeasureKindDto.CostBasis && item.Expected == 1000m && item.Actual == 1000m && item.Unit == "USD");
        }
        else
        {
            result.Summary.LedgerAsOf.Should().BeNull();
            result.Breaks.Should().Contain(item => item.CheckId == "ledger-summary-missing");
            await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
        }

        var foreign = run with
        {
            RunId = "foreign-native",
            ParameterSet = new Dictionary<string, string> { ["workstationTenantId"] = "tenant-foreign", ["workstationCompanyId"] = "company-foreign" }
        };
        await runs.RecordRunAsync(foreign);
        var foreignResult = result with { Summary = result.Summary with { ReconciliationRunId = "foreign-result", RunId = foreign.RunId } };
        await app.Services.GetRequiredService<IReconciliationRunRepository>().SaveAsync(foreignResult);
        store.ClearReceivedCalls();

        (await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest(foreign.RunId)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(UiApiRoutes.ReconciliationRunById.Replace("{reconciliationRunId}", "foreign-result")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(UiApiRoutes.RunsReconciliation.Replace("{runId}", foreign.RunId)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(UiApiRoutes.RunsReconciliationHistory.Replace("{runId}", foreign.RunId)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        await store.DidNotReceive().QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScopedPaperReconciliation_EmptyPrivateContextLedgerRemainsMissingAccountingEvidence()
    {
        var at = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var run = ScopedAccountingRun("paper-without-posted-source", Guid.NewGuid(), Guid.NewGuid(), at);
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserTenantId: "tenant-native", currentUserCompanyId: "company-native");
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(run);

        var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest(run.RunId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions))!;
        result.Summary.LedgerAsOf.Should().BeNull();
        result.Breaks.Should().Contain(item => item.CheckId == "ledger-summary-missing");
        result.Matches.Should().NotContain(item => item.Measures.Any(measure =>
            measure.Kind is ReconciliationBreakMeasureKindDto.Quantity or ReconciliationBreakMeasureKindDto.CostBasis));
    }

    private static StrategyRunEntry ScopedAccountingRun(string runId, Guid accountId, Guid securityId, DateTimeOffset cut)
    {
        var account = accountId.ToString("D");
        var positions = new Dictionary<string, Position> { ["SPY"] = new("SPY", 10, 100m, 0m, 0m) };
        var snapshot = new PortfolioSnapshot(cut, DateOnly.FromDateTime(cut.UtcDateTime), 9000m, 0m, 1000m, 0m, 10000m, 0m,
            positions, new Dictionary<string, FinancialAccountSnapshot>
            {
                [account] = new(account, "Synthetic account", FinancialAccountKind.Brokerage, "Synthetic", 9000m, 0m,
                    1000m, 0m, 10000m, positions, new())
            }, [])
        {
            AccountingPositions = [new("SPY", account, securityId, false, 10m, 1000m, "Shares", 1m, "USD")]
        };
        var template = BuildReconciliationReadyRun(runId);
        return template with
        {
            RunType = RunType.Paper,
            StrategyId = "native-scope-strategy",
            FundProfileId = "fund-native",
            FundAccountId = accountId,
            ParameterSet = new Dictionary<string, string> { ["workstationTenantId"] = "tenant-native", ["workstationCompanyId"] = "company-native" },
            Metrics = template.Metrics! with { Snapshots = [snapshot], Ledger = new Meridian.Ledger.Ledger(), CashFlows = [], Universe = new HashSet<string> { "SPY" } }
        };
    }

    private static IReadOnlyList<LedgerJournalEntryRecord> ScopedAccountingJournals(StrategyRunEntry run, Guid bookId, Guid periodId,
        Guid securityId, DateTimeOffset at)
    {
        var account = run.FundAccountId!.Value.ToString("D");
        const string carryingName = "Approved equity carrying";
        var carrying = new LedgerAccount(carryingName, LedgerAccountType.Asset, "SPY", account);
        var ledger = new Meridian.Ledger.Ledger();
        void Add(DateTimeOffset time, string sourceRun, bool capital, decimal amount)
        {
            var id = Guid.NewGuid();
            var metadata = new JournalEntryMetadata(ActivityType: capital ? "opening-capital" : "execution.trade-fill",
                Symbol: capital ? null : "SPY", SecurityId: capital ? null : securityId, StrategyId: run.StrategyId, FinancialAccountId: account,
                Tags: new Dictionary<string, string> { ["strategy.runId"] = sourceRun, ["execution.securities-account"] = carryingName });
            if (!capital)
                metadata = SecurityTradePositionEvidence.WithPostings(metadata,
                    [new(SecurityTradePostingKind.OpenLong, amount / 100m, amount)], SecurityLotUnitKind.Shares, 1m);
            var debit = capital ? LedgerAccounts.CashFor(account) : carrying;
            var credit = capital ? LedgerAccounts.CapitalAccountFor(account) : LedgerAccounts.CashFor(account);
            ledger.Post(new(id, time, "Synthetic native facts",
            [
                new LedgerEntry(Guid.NewGuid(), id, time, debit, amount, 0m, "Synthetic native facts", currency: new("USD", "USD", amount, 0m, 1m))
                    { ChartAccount = debit == carrying ? new("synthetic-carrying-node", "synthetic-chart-v1", "1100") : null },
                new LedgerEntry(Guid.NewGuid(), id, time, credit, 0m, amount, "Synthetic native facts", currency: new("USD", "USD", 0m, amount, 1m))
            ], metadata));
        }
        Add(at.AddMinutes(-1), run.RunId, true, 10000m);
        Add(at, run.RunId, false, 1000m);
        Add(at.AddMinutes(2), run.RunId, false, 2000m);
        Add(at, "another-run", true, 99999m);
        return ledger.Journal.Select(entry => new LedgerJournalEntryRecord(entry, Guid.NewGuid(), periodId, null, null, 1, at)).ToArray();
    }
}
