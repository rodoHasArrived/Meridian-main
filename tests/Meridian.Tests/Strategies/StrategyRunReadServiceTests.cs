using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyRunReadServiceTests
{
    [Fact]
    public async Task GetRunDetailAsync_BuildsSharedPortfolioAndLedgerModels()
    {
        var store = new StrategyRunStore();
        var run = BuildCompletedRun(
            runId: "run-a",
            strategyId: "momentum-1",
            strategyName: "Momentum",
            finalEquity: 125_000m,
            netPnl: 25_000m,
            totalReturn: 0.25m,
            realizedPnl: 9_000m,
            unrealizedPnl: 16_000m,
            fillCount: 2,
            sharpeRatio: 1.42,
            maxDrawdown: 5_500m);

        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var detail = await service.GetRunDetailAsync("run-a");

        detail.Should().NotBeNull();
        detail!.Summary.Mode.Should().Be(StrategyRunMode.Backtest);
        detail.Summary.Engine.Should().Be(StrategyRunEngine.MeridianNative);
        detail.Summary.Status.Should().Be(StrategyRunStatus.Completed);
        detail.Summary.NetPnl.Should().Be(25_000m);
        detail.Summary.FinalEquity.Should().Be(125_000m);
        detail.Summary.AuditReference.Should().Be("run-a-audit");
        detail.Summary.Execution.Should().NotBeNull();
        detail.Summary.Execution!.TotalTrades.Should().Be(2);
        detail.Summary.Execution.TotalCommissions.Should().Be(125m);
        detail.Summary.Promotion.Should().NotBeNull();
        detail.Summary.Promotion!.State.Should().Be(StrategyRunPromotionState.CandidateForPaper);
        detail.Summary.Promotion.SuggestedNextMode.Should().Be(StrategyRunMode.Paper);
        detail.Summary.Governance.Should().NotBeNull();
        detail.Summary.Governance!.HasAuditTrail.Should().BeTrue();
        detail.Execution.Should().NotBeNull();
        detail.Execution!.FillCount.Should().Be(2);
        detail.Promotion.Should().NotBeNull();
        detail.Promotion!.State.Should().Be(StrategyRunPromotionState.CandidateForPaper);
        detail.Governance.Should().NotBeNull();
        detail.Governance!.DatasetReference.Should().Be("dataset/us-equities/2026-q1");
        detail.Portfolio.Should().NotBeNull();
        detail.Portfolio!.Cash.Should().Be(40_000m);
        detail.Portfolio.GrossExposure.Should().Be(85_000m);
        detail.Portfolio.NetExposure.Should().Be(75_000m);
        detail.Portfolio.RealizedPnl.Should().Be(9_000m);
        detail.Portfolio.UnrealizedPnl.Should().Be(16_000m);
        detail.Portfolio.Commissions.Should().Be(125m);
        detail.Portfolio.Financing.Should().Be(35m);
        detail.Portfolio.Positions.Should().ContainSingle(position => position.Symbol == "AAPL" && position.Quantity == 100);
        detail.Ledger.Should().NotBeNull();
        detail.Ledger!.JournalEntryCount.Should().Be(2);
        detail.Ledger.LedgerEntryCount.Should().Be(6);
        detail.Ledger.AssetBalance.Should().Be(124_875m);
        detail.Ledger.RevenueBalance.Should().Be(25_000m);
        detail.Ledger.ExpenseBalance.Should().Be(125m);
        detail.Ledger.TrialBalance.Should().Contain(line => line.AccountName == "Cash");
        detail.Ledger.Journal.Should().HaveCount(2);
    }

    [Fact]
    public async Task CompareRunsAsync_ProducesComparisonFriendlyRows()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildCompletedRun(
            runId: "run-a",
            strategyId: "momentum-1",
            strategyName: "Momentum",
            finalEquity: 125_000m,
            netPnl: 25_000m,
            totalReturn: 0.25m,
            realizedPnl: 9_000m,
            unrealizedPnl: 16_000m,
            fillCount: 2,
            sharpeRatio: 1.42,
            maxDrawdown: 5_500m));
        await store.RecordRunAsync(BuildCompletedRun(
            runId: "run-b",
            strategyId: "momentum-1",
            strategyName: "Momentum",
            finalEquity: 111_000m,
            netPnl: 11_000m,
            totalReturn: 0.11m,
            realizedPnl: 4_000m,
            unrealizedPnl: 7_000m,
            fillCount: 1,
            sharpeRatio: 0.91,
            maxDrawdown: 7_000m));

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var comparison = await service.CompareRunsAsync(["run-a", "run-b"]);

        comparison.Should().HaveCount(2);
        comparison[0].RunId.Should().Be("run-a");
        comparison[0].FinalEquity.Should().Be(125_000m);
        comparison[0].SharpeRatio.Should().Be(1.42);
        comparison[0].PromotionState.Should().Be(StrategyRunPromotionState.CandidateForPaper);
        comparison[0].HasLedger.Should().BeTrue();
        comparison[0].HasAuditTrail.Should().BeTrue();
        comparison[1].RunId.Should().Be("run-b");
    }

    [Fact]
    public async Task GetRunsAsync_WithoutStrategyFilter_ReturnsAllRecordedRuns()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildCompletedRun(
            runId: "run-a",
            strategyId: "momentum-1",
            strategyName: "Momentum",
            finalEquity: 125_000m,
            netPnl: 25_000m,
            totalReturn: 0.25m,
            realizedPnl: 9_000m,
            unrealizedPnl: 16_000m,
            fillCount: 2,
            sharpeRatio: 1.42,
            maxDrawdown: 5_500m));
        await store.RecordRunAsync(BuildCompletedRun(
            runId: "run-b",
            strategyId: "meanrev-1",
            strategyName: "Mean Reversion",
            finalEquity: 103_000m,
            netPnl: 3_000m,
            totalReturn: 0.03m,
            realizedPnl: 1_500m,
            unrealizedPnl: 1_500m,
            fillCount: 1,
            sharpeRatio: 0.55,
            maxDrawdown: 2_000m));

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var runs = await service.GetRunsAsync();

        runs.Should().HaveCount(2);
        runs.Should().Contain(run => run.RunId == "run-a" && run.PortfolioId == "momentum-1-backtest-portfolio");
        runs.Should().Contain(run => run.RunId == "run-b" && run.StrategyId == "meanrev-1");
        runs.All(run => run.Execution is not null && run.Governance is not null && run.Promotion is not null)
            .Should().BeTrue();
    }

    [Fact]
    public async Task GetRunsAsync_InProgressPaperRun_ReportsPromotionAsRequiresCompletion()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(new StrategyRunEntry(
            RunId: "paper-a",
            StrategyId: "carry-1",
            StrategyName: "Carry",
            RunType: RunType.Paper,
            StartedAt: new DateTimeOffset(2026, 3, 21, 15, 0, 0, TimeSpan.Zero),
            EndedAt: null,
            Metrics: null,
            DatasetReference: "dataset/fx/spot",
            FeedReference: "synthetic:fx",
            PortfolioId: "carry-1-paper-portfolio",
            LedgerReference: "carry-1-paper-ledger",
            AuditReference: "paper-a-audit",
            Engine: "BrokerPaper",
            ParameterSet: new Dictionary<string, string>
            {
                ["rebalance"] = "daily"
            }));

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var runs = await service.GetRunsAsync("carry-1");

        runs.Should().ContainSingle();
        var run = runs.Single();
        run.Mode.Should().Be(StrategyRunMode.Paper);
        run.Status.Should().Be(StrategyRunStatus.Running);
        run.Promotion.Should().NotBeNull();
        run.Promotion!.State.Should().Be(StrategyRunPromotionState.RequiresCompletion);
        run.Promotion.SuggestedNextMode.Should().BeNull();
        run.Execution.Should().NotBeNull();
        run.Execution!.HasAuditTrail.Should().BeTrue();
        run.Governance.Should().NotBeNull();
        run.Governance!.HasParameters.Should().BeTrue();
    }

    [Fact]
    public async Task GetRunDetailAsync_WithSecurityLookup_EnrichesPortfolioAndLedgerSymbols()
    {
        var store = new StrategyRunStore();
        var run = BuildCompletedRun(
            runId: "run-security-a",
            strategyId: "momentum-1",
            strategyName: "Momentum",
            finalEquity: 125_000m,
            netPnl: 25_000m,
            totalReturn: 0.25m,
            realizedPnl: 9_000m,
            unrealizedPnl: 16_000m,
            fillCount: 2,
            sharpeRatio: 1.42,
            maxDrawdown: 5_500m);
        await store.RecordRunAsync(run);

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL"));

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(lookup),
            new LedgerReadService(lookup));

        var detail = await service.GetRunDetailAsync("run-security-a");

        detail.Should().NotBeNull();
        detail!.Portfolio.Should().NotBeNull();
        detail.Portfolio!.SecurityResolvedCount.Should().Be(1);
        detail.Portfolio.SecurityMissingCount.Should().Be(0);
        detail.Portfolio.Positions.Should().ContainSingle();
        detail.Portfolio.Positions[0].Security.Should().NotBeNull();
        detail.Portfolio.Positions[0].Security!.DisplayName.Should().Be("Apple Inc.");

        detail.Ledger.Should().NotBeNull();
        detail.Ledger!.SecurityResolvedCount.Should().Be(1);
        detail.Ledger.SecurityMissingCount.Should().Be(0);
        detail.Ledger.TrialBalance
            .Where(line => string.Equals(line.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase))
            .Should()
            .OnlyContain(line => line.Security != null && line.Security.DisplayName == "Apple Inc.");
    }

    [Fact]
    public async Task GetRunDetailAsync_WhenSecurityLookupMissesSymbol_TracksMissingCoverage()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildCompletedRun(
            runId: "run-security-missing",
            strategyId: "momentum-1",
            strategyName: "Momentum",
            finalEquity: 125_000m,
            netPnl: 25_000m,
            totalReturn: 0.25m,
            realizedPnl: 9_000m,
            unrealizedPnl: 16_000m,
            fillCount: 2,
            sharpeRatio: 1.42,
            maxDrawdown: 5_500m));

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(new StubSecurityReferenceLookup()),
            new LedgerReadService(new StubSecurityReferenceLookup()));

        var detail = await service.GetRunDetailAsync("run-security-missing");

        detail.Should().NotBeNull();
        detail!.Portfolio.Should().NotBeNull();
        detail.Portfolio!.SecurityResolvedCount.Should().Be(0);
        detail.Portfolio.SecurityMissingCount.Should().Be(1);
        detail.Portfolio.Positions[0].Security.Should().BeNull();

        detail.Ledger.Should().NotBeNull();
        detail.Ledger!.SecurityResolvedCount.Should().Be(0);
        detail.Ledger.SecurityMissingCount.Should().Be(1);
        detail.Ledger.TrialBalance
            .Where(line => string.Equals(line.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase))
            .Should()
            .OnlyContain(line => line.Security == null);
    }

    [Fact]
    public async Task GetLedgerSummaryAsync_ReturnsLedgerSummaryForKnownRun()
    {
        var store = new StrategyRunStore();
        var run = BuildCompletedRun(
            runId: "run-ledger-direct",
            strategyId: "momentum-1",
            strategyName: "Momentum",
            finalEquity: 115_000m,
            netPnl: 15_000m,
            totalReturn: 0.15m,
            realizedPnl: 10_000m,
            unrealizedPnl: 5_000m,
            fillCount: 1,
            sharpeRatio: 1.1,
            maxDrawdown: 2_000m);

        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var summary = await service.GetLedgerSummaryAsync("run-ledger-direct");

        summary.Should().NotBeNull();
        summary!.RunId.Should().Be("run-ledger-direct");
        summary.JournalEntryCount.Should().Be(2);
        summary.TrialBalance.Should().NotBeEmpty();
        summary.Journal.Should().HaveCount(2);
        summary.Journal.Should().BeInDescendingOrder(j => j.Timestamp);
        summary.AssetBalance.Should().BePositive();
    }

    [Fact]
    public async Task GetLedgerSummaryAsync_ReturnsNullForUnknownRun()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var summary = await service.GetLedgerSummaryAsync("no-such-run");

        summary.Should().BeNull();
    }

    [Fact]
    public async Task GetLedgerSummaryAsync_ReturnsNullWhenRunHasNoLedger()
    {
        var store = new StrategyRunStore();
        var runWithoutLedger = new StrategyRunEntry(
            RunId: "run-no-ledger",
            StrategyId: "strat-1",
            StrategyName: "TestStrategy",
            RunType: RunType.Backtest,
            StartedAt: new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero),
            EndedAt: new DateTimeOffset(2026, 3, 21, 11, 0, 0, TimeSpan.Zero),
            Metrics: null);

        await store.RecordRunAsync(runWithoutLedger);

        var service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        var summary = await service.GetLedgerSummaryAsync("run-no-ledger");

        summary.Should().BeNull();
    }

    private static StrategyRunEntry BuildCompletedRun(
        string runId,
        string strategyId,
        string strategyName,
        decimal finalEquity,
        decimal netPnl,
        decimal totalReturn,
        decimal realizedPnl,
        decimal unrealizedPnl,
        int fillCount,
        double sharpeRatio,
        decimal maxDrawdown)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 14, 30, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddHours(2);

        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 100, 450m, unrealizedPnl, realizedPnl)
        };

        var account = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02);
        var accountSnapshots = new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [account.AccountId] = new FinancialAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                Kind: account.Kind,
                Institution: account.Institution,
                Cash: 40_000m,
                MarginBalance: -5_000m,
                LongMarketValue: 80_000m,
                ShortMarketValue: -5_000m,
                Equity: finalEquity,
                Positions: positions,
                Rules: account.Rules!)
        };

        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 40_000m,
            MarginBalance: -5_000m,
            LongMarketValue: 80_000m,
            ShortMarketValue: -5_000m,
            TotalEquity: finalEquity,
            DailyReturn: totalReturn,
            Positions: positions,
            Accounts: accountSnapshots,
            DayCashFlows: Array.Empty<CashFlowEntry>());

        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var equity = new LedgerAccount("Owner's Equity", LedgerAccountType.Equity);
        var tradingGains = new LedgerAccount("Trading Gains", LedgerAccountType.Revenue, Symbol: "AAPL");
        var commissions = new LedgerAccount("Commissions", LedgerAccountType.Expense, Symbol: "AAPL");

        ledger.PostLines(startedAt, "initial-capital", new[]
        {
            (cash, 100_000m, 0m),
            (equity, 0m, 100_000m),
        });

        ledger.PostLines(completedAt, "close-run", new[]
        {
            (cash, 25_000m, 0m),
            (tradingGains, 0m, 25_000m),
            (commissions, 125m, 0m),
            (cash, 0m, 125m),
        });

        var fills = Enumerable.Range(0, fillCount)
            .Select(index => new FillEvent(
                FillId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                Symbol: "AAPL",
                FilledQuantity: 50,
                FillPrice: 455m + index,
                Commission: 62.5m,
                FilledAt: startedAt.AddMinutes(index + 1),
                AccountId: account.AccountId))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Symbols: ["AAPL"],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: finalEquity,
            GrossPnl: netPnl + 125m,
            NetPnl: netPnl,
            TotalReturn: totalReturn,
            AnnualizedReturn: totalReturn,
            SharpeRatio: sharpeRatio,
            SortinoRatio: sharpeRatio,
            CalmarRatio: sharpeRatio / 2,
            MaxDrawdown: maxDrawdown,
            MaxDrawdownPercent: maxDrawdown / 100_000m,
            MaxDrawdownRecoveryDays: 3,
            ProfitFactor: 1.8,
            WinRate: 0.65,
            TotalTrades: fillCount,
            WinningTrades: fillCount,
            LosingTrades: 0,
            TotalCommissions: 125m,
            TotalMarginInterest: 50m,
            TotalShortRebates: 15m,
            Xirr: 0.19,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                ["AAPL"] = new("AAPL", realizedPnl, unrealizedPnl, fillCount, 125m, 35m)
            });

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [snapshot],
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills: fills,
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(12),
            TotalEventsProcessed: 2_500);

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: completedAt,
            Metrics: result,
            DatasetReference: "dataset/us-equities/2026-q1",
            FeedReference: "polygon:stocks",
            PortfolioId: $"{strategyId}-backtest-portfolio",
            LedgerReference: $"{strategyId}-backtest-ledger",
            AuditReference: $"{runId}-audit",
            Engine: "MeridianNative",
            ParameterSet: new Dictionary<string, string>
            {
                ["lookback"] = "20",
                ["threshold"] = "1.5"
            });
    }

    private sealed class StubSecurityReferenceLookup : ISecurityReferenceLookup
    {
        private readonly Dictionary<string, WorkstationSecurityReference> _references = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string symbol, WorkstationSecurityReference reference)
        {
            _references[symbol] = reference;
        }

        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            _references.TryGetValue(symbol, out var reference);
            return Task.FromResult<WorkstationSecurityReference?>(reference);
        }
    }
}
