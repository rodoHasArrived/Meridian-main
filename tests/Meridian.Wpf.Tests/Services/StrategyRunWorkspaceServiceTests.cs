using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class StrategyRunWorkspaceServiceTests
{
    [Fact]
    public async Task RecordBacktestRunAsync_ShouldExposeRecordedRunAcrossBrowserAndDrillIns()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService());
        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 1),
            To: new DateOnly(2026, 3, 20),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");
        var result = BuildResult();

        var runId = await service.RecordBacktestRunAsync(request, "Buy & Hold (equal-weight)", result);

        runId.Should().NotBeNullOrEmpty();

        var detail = await service.GetRunDetailAsync(runId);
        detail.Should().NotBeNull();
        detail!.Summary.Mode.Should().Be(StrategyRunMode.Backtest);
        detail.Summary.StrategyId.Should().Be("buy-hold-equal-weight");
        detail.Portfolio.Should().NotBeNull();
        detail.Ledger.Should().NotBeNull();
        detail.Parameters.Should().ContainKey("symbols");

        var portfolio = await service.GetPortfolioAsync(runId);
        portfolio.Should().NotBeNull();
        portfolio!.Positions.Should().ContainSingle(position => position.Symbol == "AAPL");

        var ledger = await service.GetLedgerAsync(runId);
        ledger.Should().NotBeNull();
        ledger!.TrialBalance.Should().NotBeEmpty();

        var latest = await service.GetLatestRunAsync();
        latest.Should().NotBeNull();
        latest!.RunId.Should().Be(runId);

        var activeContext = await service.GetActiveRunContextAsync();
        activeContext.Should().NotBeNull();
        activeContext!.PromotionStatus.Label.Should().Be("Candidate for paper");
        activeContext.AuditStatus.Label.Should().Be("Audit trail ready");
        activeContext.ValidationStatus.Label.Should().Be("Validation ready");

        var tradingSummary = await service.GetTradingSummaryAsync();
        tradingSummary.ActiveRunContext.Should().NotBeNull();
        tradingSummary.PromotionStatus.Label.Should().Be("Candidate for paper");
        tradingSummary.AuditStatus.Label.Should().Be("Audit trail ready");
        tradingSummary.ValidationStatus.Label.Should().Be("Validation ready");
    }

    [Fact]
    public async Task RecordBacktestRunAsync_WithInjectedSecurityLookup_ShouldExposeSecurityCoverage()
    {
        var store = new StrategyRunStore();
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL"));

        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(lookup),
            new Meridian.Strategies.Services.LedgerReadService(lookup));
        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 1),
            To: new DateOnly(2026, 3, 20),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");

        var runId = await service.RecordBacktestRunAsync(request, "Buy & Hold (equal-weight)", BuildResult());
        var detail = await service.GetRunDetailAsync(runId);

        detail.Should().NotBeNull();
        detail!.Portfolio.Should().NotBeNull();
        detail.Portfolio!.SecurityResolvedCount.Should().Be(1);
        detail.Portfolio.SecurityMissingCount.Should().Be(0);
        detail.Portfolio.Positions[0].Security.Should().NotBeNull();
        detail.Portfolio.Positions[0].Security!.DisplayName.Should().Be("Apple Inc.");

        detail.Ledger.Should().NotBeNull();
        detail.Ledger!.SecurityResolvedCount.Should().Be(1);
        detail.Ledger.SecurityMissingCount.Should().Be(0);
        detail.Ledger.TrialBalance.Should().Contain(line =>
            line.Symbol == "AAPL" &&
            line.Security != null &&
            line.Security.DisplayName == "Apple Inc.");
    }

    [Fact]
    public async Task GetTradingSummaryAsync_WithoutActiveRun_ShouldExposeAggregatePromotionAuditAndValidationStatus()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService());
        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 1),
            To: new DateOnly(2026, 3, 20),
            Symbols: ["AAPL", "MSFT"],
            InitialCash: 100_000m,
            DataRoot: "./data/test");

        await service.RecordBacktestRunAsync(request, "Buy & Hold (equal-weight)", BuildResult());
        await service.SetActiveRunContextAsync(null);

        var summary = await service.GetTradingSummaryAsync();

        summary.ActiveRunContext.Should().BeNull();
        summary.PromotionStatus.Label.Should().Be("Awaiting review");
        summary.AuditStatus.Label.Should().Be("Audit trail ready");
        summary.ValidationStatus.Label.Should().Be("Validation ready");
    }

    [Fact]
    public async Task GetTradingSummaryAsync_WithIncompletePaperRun_ShouldExposeAuditAndValidationAttention()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService());
        var run = StrategyRunEntry.Start(
            "alpha-mean-reversion",
            "Alpha Mean Reversion",
            RunType.Paper,
            "paper-run-001");

        await store.RecordRunAsync(run);
        await service.SetActiveRunContextAsync(run.RunId);

        var activeContext = await service.GetActiveRunContextAsync();

        activeContext.Should().NotBeNull();
        activeContext!.PromotionStatus.Label.Should().Be("Requires completion");
        activeContext.PromotionStatus.Tone.Should().Be(TradingWorkspaceStatusTone.Warning);
        activeContext.AuditStatus.Label.Should().Be("Audit trail pending");
        activeContext.AuditStatus.Tone.Should().Be(TradingWorkspaceStatusTone.Warning);
        activeContext.ValidationStatus.Label.Should().Be("Validation attention");
        activeContext.ValidationStatus.Tone.Should().Be(TradingWorkspaceStatusTone.Warning);
        activeContext.ValidationStatus.Detail.Should().Contain("parameters");

        await service.SetActiveRunContextAsync(null);
        var summary = await service.GetTradingSummaryAsync();

        summary.PromotionStatus.Label.Should().Be("Completion required");
        summary.PromotionStatus.Tone.Should().Be(TradingWorkspaceStatusTone.Warning);
        summary.AuditStatus.Label.Should().Be("Audit pending");
        summary.AuditStatus.Tone.Should().Be(TradingWorkspaceStatusTone.Warning);
        summary.ValidationStatus.Label.Should().Be("Validation attention");
        summary.ValidationStatus.Tone.Should().Be(TradingWorkspaceStatusTone.Warning);
    }

    [Fact]
    public async Task GetActiveRunContextAsync_WithPaperRunReadyForLive_ShouldSurfaceBrokerValidationGap()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(),
            new Meridian.Strategies.Services.LedgerReadService(),
            new BrokerageConfiguration
            {
                Gateway = "paper",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("paper-live-review", "Paper Live Review", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildResult(),
            PortfolioId = "paper-live-review-portfolio",
            LedgerReference = "paper-live-review-ledger",
            AuditReference = "audit-paper-live-review"
        };
        await store.RecordRunAsync(run);
        await service.SetActiveRunContextAsync(run.RunId);

        var activeContext = await service.GetActiveRunContextAsync();
        var tradingSummary = await service.GetTradingSummaryAsync();

        activeContext.Should().NotBeNull();
        activeContext!.ValidationStatus.Label.Should().Be("Broker validation gap");
        activeContext.ValidationStatus.Detail.Should().Contain("paper trading");
        tradingSummary.ValidationStatus.Label.Should().Be("Broker validation gap");
        tradingSummary.ValidationStatus.Detail.Should().Contain("paper trading");
    }

    private static BacktestResult BuildResult()
    {
        var startedAt = new DateTimeOffset(2026, 3, 20, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(15);

        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 100, 185m, 1_500m, 3_000m)
        };

        var account = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02);
        var accountSnapshots = new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [account.AccountId] = new FinancialAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                Kind: account.Kind,
                Institution: account.Institution,
                Cash: 82_000m,
                MarginBalance: 0m,
                LongMarketValue: 21_000m,
                ShortMarketValue: 0m,
                Equity: 103_000m,
                Positions: positions,
                Rules: account.Rules!)
        };

        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 82_000m,
            MarginBalance: 0m,
            LongMarketValue: 21_000m,
            ShortMarketValue: 0m,
            TotalEquity: 103_000m,
            DailyReturn: 0.03m,
            Positions: positions,
            Accounts: accountSnapshots,
            DayCashFlows: Array.Empty<CashFlowEntry>());

        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var equity = new LedgerAccount("Owner Equity", LedgerAccountType.Equity);
        var gains = new LedgerAccount("Trading Gains", LedgerAccountType.Revenue, Symbol: "AAPL");

        ledger.PostLines(startedAt, "initial-capital", new[]
        {
            (cash, 100_000m, 0m),
            (equity, 0m, 100_000m)
        });

        ledger.PostLines(completedAt, "close-run", new[]
        {
            (cash, 3_000m, 0m),
            (gains, 0m, 3_000m)
        });

        return new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 3, 1),
                To: new DateOnly(2026, 3, 20),
                Symbols: ["AAPL", "MSFT"],
                InitialCash: 100_000m,
                DataRoot: "./data/test"),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT" },
            Snapshots: [snapshot],
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills:
            [
                new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "AAPL", 100, 185m, 4m, startedAt.AddMinutes(1), account.AccountId)
            ],
            Metrics: new BacktestMetrics(
                InitialCapital: 100_000m,
                FinalEquity: 103_000m,
                GrossPnl: 3_004m,
                NetPnl: 3_000m,
                TotalReturn: 0.03m,
                AnnualizedReturn: 0.03m,
                SharpeRatio: 1.1,
                SortinoRatio: 1.1,
                CalmarRatio: 0.8,
                MaxDrawdown: 800m,
                MaxDrawdownPercent: 0.008m,
                MaxDrawdownRecoveryDays: 2,
                ProfitFactor: 1.6,
                WinRate: 1.0,
                TotalTrades: 1,
                WinningTrades: 1,
                LosingTrades: 0,
                TotalCommissions: 4m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0.12,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>
                {
                    ["AAPL"] = new("AAPL", 3_000m, 1_500m, 1, 4m, 0m)
                }),
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(15),
            TotalEventsProcessed: 1_250);
    }

    private sealed class StubSecurityReferenceLookup : Meridian.Strategies.Services.ISecurityReferenceLookup
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
