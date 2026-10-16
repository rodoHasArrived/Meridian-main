using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Storage.Operations;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StoppedRunPromotionTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "Meridian.Tests", "stopped-run-promotion", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifiedOperatorStop_WithQualifyingRetainedMetrics_CanBePromotedToLive()
    {
        const string runId = "stopped-paper";
        const string strategyId = "operator-stop";
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(_dataRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance);
        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            ExecutionManualOverrideKinds.AllowLivePromotion, "Reviewed stopped paper session", "ops",
            StrategyId: strategyId, RunId: runId));
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Live);
        var evidence = checklist.Select(item => item switch
        {
            PromotionApprovalChecklist.LiveOverrideReviewed => $"{item}:manual-override/{manualOverride.OverrideId}",
            PromotionApprovalChecklist.PaperExecutionModelReviewed => $"{item}:paper-match/1+paper-cost/1",
            _ => $"{item}:retained/operator-stop/{item}"
        }).ToArray();
        var writer = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
        var run = StrategyRunEntry.Start(strategyId, "Operator Stop", RunType.Paper, runId)
            .Complete(BuildPassingResult()) with
        {
            TerminalStatus = StrategyRunStatus.Stopped,
            WalkForwardEvidence = new StrategyRunWalkForwardEvidence(
                1.1d, 0.08m, 0.85d, 6, DateTimeOffset.UtcNow)
        };
        await writer.RecordRunAsync(run);
        var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
        var retained = await restarted.GetRunByIdAsync(runId);
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        retained.TerminalStatus.Should().Be(StrategyRunStatus.Stopped);
        retained.Metrics.Should().NotBeNull();
        var service = new PromotionService(
            restarted,
            new BacktestToLivePromoter(),
            new JsonlPromotionRecordStore(
                Path.Combine(_dataRoot, "promotions"), NullLogger<JsonlPromotionRecordStore>.Instance),
            NullLogger<PromotionService>.Instance,
            controls,
            brokerageConfiguration: new BrokerageConfiguration { Gateway = "alpaca", LiveExecutionEnabled = true },
            trustEvidenceProvider: new PromotionTrustEvidenceStub());

        var evaluation = await service.EvaluateAsync(runId);
        var approval = await service.ApproveAsync(new PromotionApprovalRequest(
            runId,
            ApprovedBy: "ops",
            ApprovalReason: "Completed cleanup and retained paper metrics reviewed.",
            ApprovalChecklist: checklist,
            EvidenceReferences: evidence,
            ManualOverrideId: manualOverride.OverrideId));

        evaluation.Ready.Should().BeTrue();
        evaluation.IsEligible.Should().BeTrue(evaluation.Reason);
        approval.Success.Should().BeTrue(approval.Reason);
        var target = await restarted.GetRunByIdAsync(approval.NewRunId!);
        target!.RunType.Should().Be(RunType.Live);
        target.ParentRunId.Should().Be(runId);
        (await service.GetPromotionHistoryAsync()).Should().ContainSingle()
            .Which.SourceRunId.Should().Be(runId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static BacktestResult BuildPassingResult()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 3, 1), Symbols: ["SPY"],
            InitialCash: 100_000m, DataRoot: "./data");
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
            request, new HashSet<string>(["SPY"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [], CashFlows: [], Fills: [], Metrics: metrics,
            Ledger: new global::Meridian.Ledger.Ledger(), ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 500);
    }
}
