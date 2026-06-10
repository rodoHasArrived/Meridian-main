using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class FinancialRecordExplorerReadServiceTests
{
    [Fact]
    public async Task GetExplorerAsync_WithLedgerSource_ReturnsProofBackedRows()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildCompletedRun());
        var service = CreateService(new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService()));

        var explorer = await service.GetExplorerAsync(FinancialRecordExplorerReadService.LedgerExplorerId);

        explorer.Should().NotBeNull();
        explorer!.IsBlocked.Should().BeFalse();
        explorer.Title.Should().Be("Ledger Explorer");
        explorer.SavedViews.Should().Contain(view => view.IsSystem && view.IsActive);
        explorer.Rows.Should().Contain(row => row.RecordType == "Ledger");

        var cashRow = explorer.Rows.Should().Contain(row => row.Label == "Cash").Which;
        cashRow.Detail.ProofActions.Should().Contain(action => action.ActionId == "evidence-packet");
        cashRow.Detail.UsedIn.Should().Contain(relationship => relationship.TargetType == "Run");
        cashRow.Detail.Impacts.Should().Contain(relationship => relationship.TargetType == "Report");
        cashRow.Detail.RecordGraph.Nodes.Should().Contain(node => node.NodeType == "Run");
    }

    [Fact]
    public async Task GetExplorerAsync_WithoutStrategyRunReadService_FailsClosedAsBlocked()
    {
        var service = CreateService();

        var explorer = await service.GetExplorerAsync(FinancialRecordExplorerReadService.PortfolioExplorerId);

        explorer.Should().NotBeNull();
        explorer!.IsBlocked.Should().BeTrue();
        explorer.Rows.Should().BeEmpty();
        explorer.BlockedReason.Should().Contain("Strategy run read service is not registered");
        explorer.SummaryItems.Should().Contain(item => item.Id == "state" && item.Tone == "danger");
    }

    [Fact]
    public async Task SaveViewAsync_NormalizesAndReturnsOperatorSavedView()
    {
        var savedViewStore = new InMemoryFinancialRecordExplorerSavedViewStore();
        var service = CreateService(savedViewStore: savedViewStore);

        var saved = await service.SaveViewAsync(
            "LEDGER",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                SavedViewId: null,
                Name: "  Controller Review  ",
                Description: "  Source-backed exception view  ",
                Filters:
                [
                    new("status", "Status", "  Unresolved  ", " contains "),
                    new("", "Skipped", "value")
                ],
                ColumnIds: [" account ", "balance", "account", ""]));

        saved.Should().NotBeNull();
        saved!.SavedViewId.Should().Be("controller-review");
        saved.Name.Should().Be("Controller Review");
        saved.Description.Should().Be("Source-backed exception view");
        saved.IsSystem.Should().BeFalse();
        saved.IsActive.Should().BeFalse();
        saved.Filters.Should().ContainSingle();
        saved.Filters[0].Value.Should().Be("Unresolved");
        saved.Filters[0].Operator.Should().Be("contains");
        saved.ColumnIds.Should().Equal("account", "balance");

        var explorer = await service.GetExplorerAsync(FinancialRecordExplorerReadService.LedgerExplorerId);
        explorer!.SavedViews.Should().Contain(view => view.SavedViewId == "controller-review" && !view.IsSystem);
    }

    [Fact]
    public async Task GetExplorerAsync_WithCanceledToken_ThrowsOperationCanceledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetExplorerAsync(FinancialRecordExplorerReadService.LedgerExplorerId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static FinancialRecordExplorerReadService CreateService(
        StrategyRunReadService? runReadService = null,
        IFinancialRecordExplorerSavedViewStore? savedViewStore = null)
    {
        var services = new ServiceCollection();
        if (runReadService is not null)
        {
            services.AddSingleton(runReadService);
        }

        return new FinancialRecordExplorerReadService(
            services.BuildServiceProvider(),
            savedViewStore ?? new InMemoryFinancialRecordExplorerSavedViewStore());
    }

    private static StrategyRunEntry BuildCompletedRun()
    {
        var startedAt = new DateTimeOffset(2026, 4, 15, 14, 30, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddHours(1);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 100, 450m, 5_000m, 1_000m)
        };
        var account = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02);
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 40_000m,
            MarginBalance: 0m,
            LongMarketValue: 85_000m,
            ShortMarketValue: 0m,
            TotalEquity: 125_000m,
            DailyReturn: 0.25m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [account.AccountId] = new(
                    AccountId: account.AccountId,
                    DisplayName: account.DisplayName,
                    Kind: account.Kind,
                    Institution: account.Institution,
                    Cash: 40_000m,
                    MarginBalance: 0m,
                    LongMarketValue: 85_000m,
                    ShortMarketValue: 0m,
                    Equity: 125_000m,
                    Positions: positions,
                    Rules: account.Rules!)
            },
            DayCashFlows: []);

        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var equity = new LedgerAccount("Owner's Equity", LedgerAccountType.Equity);
        var tradingGains = new LedgerAccount("Trading Gains", LedgerAccountType.Revenue, Symbol: "AAPL");
        ledger.PostLines(startedAt, "initial-capital", [(cash, 100_000m, 0m), (equity, 0m, 100_000m)]);
        ledger.PostLines(completedAt, "close-run", [(cash, 25_000m, 0m), (tradingGains, 0m, 25_000m)]);

        var result = new BacktestResult(
            Request: new BacktestRequest(
                From: DateOnly.FromDateTime(startedAt.UtcDateTime),
                To: DateOnly.FromDateTime(completedAt.UtcDateTime),
                Symbols: ["AAPL"],
                InitialCash: 100_000m),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: new BacktestMetrics(
                InitialCapital: 100_000m,
                FinalEquity: 125_000m,
                GrossPnl: 25_000m,
                NetPnl: 25_000m,
                TotalReturn: 0.25m,
                AnnualizedReturn: 0.25m,
                SharpeRatio: 1.5,
                SortinoRatio: 1.5,
                CalmarRatio: 0.75,
                MaxDrawdown: 1_500m,
                MaxDrawdownPercent: 0.015m,
                MaxDrawdownRecoveryDays: 1,
                ProfitFactor: 2.1,
                WinRate: 1,
                TotalTrades: 1,
                WinningTrades: 1,
                LosingTrades: 0,
                TotalCommissions: 0m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0.18,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AAPL"] = new("AAPL", 1_000m, 5_000m, 1, 0m, 0m)
                }),
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 100);

        return new StrategyRunEntry(
            RunId: "run-proof-1",
            StrategyId: "strategy-proof",
            StrategyName: "Proof Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: completedAt,
            Metrics: result,
            DatasetReference: "dataset/proof",
            FeedReference: "fixture",
            PortfolioId: "portfolio-proof",
            LedgerReference: "ledger-proof",
            AuditReference: "audit-proof-1",
            Engine: "MeridianNative");
    }

    private sealed class InMemoryFinancialRecordExplorerSavedViewStore : IFinancialRecordExplorerSavedViewStore
    {
        private readonly Dictionary<string, List<FinancialRecordExplorerSavedViewDto>> _views = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> LoadAsync(
            string explorerId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<FinancialRecordExplorerSavedViewDto>>(
                _views.TryGetValue(explorerId, out var views) ? views.ToArray() : []);
        }

        public Task<FinancialRecordExplorerSavedViewDto> UpsertAsync(
            string explorerId,
            FinancialRecordExplorerSavedViewDto view,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_views.TryGetValue(explorerId, out var views))
            {
                views = [];
                _views[explorerId] = views;
            }

            views.RemoveAll(existing => string.Equals(existing.SavedViewId, view.SavedViewId, StringComparison.OrdinalIgnoreCase));
            views.Add(view);
            return Task.FromResult(view);
        }
    }
}
