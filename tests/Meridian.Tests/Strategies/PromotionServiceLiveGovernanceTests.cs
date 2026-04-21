using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class PromotionServiceLiveGovernanceTests
{
    [Fact]
    public async Task EvaluateAsync_WhenPaperRunTargetsLive_RequiresHumanApprovalControls()
    {
        var service = BuildService(
            out var store,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "paper",
                LiveExecutionEnabled = false
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.TargetMode.Should().Be(RunType.Live);
        result.RequiresHumanApproval.Should().BeTrue();
        result.RequiresManualOverride.Should().BeTrue();
        result.RequiredManualOverrideKind.Should().Be(ExecutionManualOverrideKinds.AllowLivePromotion);
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason => reason.Contains("does not enable live execution", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_WhenPaperRunTargetsLiveWithPaperGateway_RequiresHumanApprovalControls()
    {
        var service = BuildService(
            out var store,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "paper",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.TargetMode.Should().Be(RunType.Live);
        result.RequiresHumanApproval.Should().BeTrue();
        result.RequiresManualOverride.Should().BeTrue();
        result.RequiredManualOverrideKind.Should().Be(ExecutionManualOverrideKinds.AllowLivePromotion);
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason => reason.Contains("paper trading", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApproveAsync_WhenPaperRunHasApprovalAndOverride_CreatesLiveRun()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        var brokerageConfiguration = new BrokerageConfiguration
        {
            Gateway = "alpaca",
            LiveExecutionEnabled = true
        };

        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration);

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ManualOverrideId: manualOverride.OverrideId));

        result.Success.Should().BeTrue();
        result.NewRunId.Should().NotBeNullOrWhiteSpace();
        result.AuditReference.Should().NotBeNullOrWhiteSpace();
        result.ApprovedBy.Should().Be("ops");

        var history = service.GetPromotionHistory();
        history.Should().ContainSingle();
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ManualOverrideId.Should().Be(manualOverride.OverrideId);
        history[0].AuditReference.Should().NotBeNullOrWhiteSpace();

        var recordedRuns = new List<StrategyRunEntry>();
        await foreach (var entry in store.GetAllRunsAsync())
        {
            recordedRuns.Add(entry);
        }

        recordedRuns.Should().Contain(entry =>
            entry.RunId == result.NewRunId &&
            entry.RunType == RunType.Live &&
            entry.ParentRunId == run.RunId);
    }

    private static PromotionService BuildService(
        out StrategyRunStore store,
        ExecutionOperatorControlService? controls = null,
        ExecutionAuditTrailService? auditTrail = null,
        BrokerageConfiguration? brokerageConfiguration = null)
    {
        store = new StrategyRunStore();
        var promoter = new BacktestToLivePromoter();
        return new PromotionService(
            store,
            promoter,
            NullLogger<PromotionService>.Instance,
            controls,
            auditTrail,
            brokerageConfiguration);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BacktestResult BuildPassingResult()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2026, 1, 1),
            To: new DateOnly(2026, 3, 1),
            Symbols: ["SPY"],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var snapshot = new PortfolioSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Date: new DateOnly(2026, 3, 1),
            Cash: 110_000m,
            MarginBalance: 0m,
            LongMarketValue: 0m,
            ShortMarketValue: 0m,
            TotalEquity: 110_000m,
            DailyReturn: 0m,
            Positions: new Dictionary<string, Position>(),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 110_000m,
            GrossPnl: 10_000m,
            NetPnl: 9_500m,
            TotalReturn: 0.10m,
            AnnualizedReturn: 0.25m,
            SharpeRatio: 1.5d,
            SortinoRatio: 2.0d,
            CalmarRatio: 3.0d,
            MaxDrawdown: 2_000m,
            MaxDrawdownPercent: 0.02m,
            MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 2.0d,
            WinRate: 0.65d,
            TotalTrades: 20,
            WinningTrades: 13,
            LosingTrades: 7,
            TotalCommissions: 500m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.22d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["SPY"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 500);
    }
}
