using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Testing;

namespace Meridian.Tests.Ui;

public sealed class TradingOperatorReadinessServiceTests
{
    [Fact]
    public async Task GetAsync_WithoutRegisteredDependencies_ShouldReturnStableOperatorWorkItemIds()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var first = await service.GetAsync();
        var second = await service.GetAsync();

        var firstIds = first.WorkItems.Select(static item => item.WorkItemId).ToArray();
        var secondIds = second.WorkItems.Select(static item => item.WorkItemId).ToArray();

        firstIds.Should().Equal(
            "paper-session-missing",
            "execution-audit-empty",
            "promotion-decision-missing",
            "dk1-trust-packet-unavailable",
            "report-pack-lineage",
            "reconciliation-policy");
        secondIds.Should().Equal(firstIds);
        firstIds.Should().NotContain(static id => id.StartsWith("operator-", StringComparison.OrdinalIgnoreCase));

        first.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Critical);
        first.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        first.ReadyForPaperOperation.Should().BeFalse();
        first.ReportPack.Should().NotBeNull();
        first.ReportPack!.Status.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        first.EvidenceCompleteness.Should().NotBeNull();
        first.EvidenceCompleteness!.BlockingGateIds.Should().Contain(["session", "replay"]);
        first.EvidenceCompleteness.ReviewGateIds.Should().Contain("report-pack");
    }

    [Fact]
    public async Task GetAsync_WithCanceledToken_ShouldPreserveCancellationFlow()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetAsync_WithOrderAuditMissingRationale_ShouldKeepAuditControlGateInReview()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithOrderAuditMissingRationale_ShouldKeepAuditControlGateInReview));
        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var audit = await auditTrail.RecordAsync(
            category: "Order",
            action: "OrderSubmitted",
            outcome: "Accepted",
            actor: "ops",
            orderId: "paper-order-1",
            correlationId: "corr-paper-order-1");

        var readiness = await service.GetAsync();

        var evidence = readiness.Controls.RecentEvidence.Should().ContainSingle().Subject;
        evidence.AuditId.Should().Be(audit.AuditId);
        evidence.IsExplained.Should().BeFalse();
        evidence.MissingFields.Should().Equal("reason");
        evidence.Scope.Should().Be("order:paper-order-1");
        readiness.Controls.UnexplainedEvidenceCount.Should().Be(1);
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "execution-evidence-incomplete" &&
            item.AuditReference == audit.AuditId);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.AuditReference == audit.AuditId);
    }

    [Fact]
    public async Task GetAsync_WithOrderAuditRationale_ShouldCountControlEvidenceAsExplained()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithOrderAuditRationale_ShouldCountControlEvidenceAsExplained));
        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var audit = await auditTrail.RecordAsync(
            category: "Order",
            action: "OrderSubmitted",
            outcome: "Accepted",
            actor: "ops",
            orderId: "paper-order-2",
            correlationId: "corr-paper-order-2",
            metadata: new Dictionary<string, string>
            {
                ["rationale"] = "Operator submitted the paper order after reviewing risk posture."
            });

        var readiness = await service.GetAsync();

        var evidence = readiness.Controls.RecentEvidence.Should().ContainSingle().Subject;
        evidence.AuditId.Should().Be(audit.AuditId);
        evidence.IsExplained.Should().BeTrue();
        evidence.MissingFields.Should().BeEmpty();
        evidence.Reason.Should().Be("Operator submitted the paper order after reviewing risk posture.");
        readiness.Controls.ExplainableEvidenceCount.Should().Be(1);
        readiness.Controls.UnexplainedEvidenceCount.Should().Be(0);
        readiness.WorkItems.Should().NotContain(item => item.WorkItemId == "execution-evidence-incomplete");
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
    }

    [Fact]
    public async Task GetAsync_WithActivePaperSession_ShouldClearSessionBlockerAndMarkGateReady()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithActivePaperSession_ShouldClearSessionBlockerAndMarkGateReady));
        var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);
        await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-happy",
            StrategyName: "Happy Path Strategy",
            InitialCash: 100_000m));

        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(persistence)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.WorkItems.Should().NotContain(item => item.WorkItemId == "paper-session-missing");
        readiness.ActiveSession.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_WithActiveSessionAndVerifiedReplay_ShouldMarkBothSessionAndReplayGatesReady()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithActiveSessionAndVerifiedReplay_ShouldMarkBothSessionAndReplayGatesReady));
        var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-replay",
            StrategyName: "Replay Strategy",
            InitialCash: 50_000m,
            Symbols: ["AAPL"]));
        var replay = await persistence.VerifyReplayAsync(session.SessionId);

        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(persistence)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.Replay.Should().NotBeNull();
        readiness.Replay!.IsConsistent.Should().BeTrue();
        readiness.Replay.VerificationAuditId.Should().Be(replay!.VerificationAuditId);
        readiness.WorkItems.Should().NotContain(item =>
            item.WorkItemId == "paper-session-missing" ||
            item.WorkItemId == "replay-stale" ||
            item.WorkItemId == "replay-inconsistent");
    }

    [Fact]
    public async Task GetAsync_WithControlsRegistered_ShouldSurfaceControlsSnapshotAndClearAuditBlocker()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithControlsRegistered_ShouldSurfaceControlsSnapshotAndClearAuditBlocker));
        var controlsRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "trading-readiness",
            nameof(GetAsync_WithControlsRegistered_ShouldSurfaceControlsSnapshotAndClearAuditBlocker),
            Guid.NewGuid().ToString("N"),
            "controls");
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(controlsRoot),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        await controls.SetDefaultPositionLimitAsync(100_000m, "risk.lead", "Paper desk limit accepted.");

        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(controls)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.Controls.Should().NotBeNull();
        readiness.Controls.CircuitBreakerOpen.Should().BeFalse();
        readiness.Controls.ExplainableEvidenceCount.Should().Be(1);
        readiness.Controls.UnexplainedEvidenceCount.Should().Be(0);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
    }

    [Fact]
    public async Task GetAsync_WithVerifiedReplayOnly_ShouldKeepCorePaperGatesReadyAndGovernanceGatesInReview()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithVerifiedReplayOnly_ShouldKeepCorePaperGatesReadyAndGovernanceGatesInReview));
        var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-fully-ready",
            StrategyName: "Fully Ready Strategy",
            InitialCash: 100_000m,
            Symbols: ["AAPL", "MSFT"]));
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-ready-1", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-ready-1", "AAPL", 10m, 190m));
        await persistence.VerifyReplayAsync(session.SessionId);

        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(persistence)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.ActiveSession.Should().NotBeNull();
        readiness.Replay.Should().NotBeNull();
        readiness.Replay!.ComparedFillCount.Should().Be(readiness.ActiveSession!.FillCount);
        readiness.Replay.ComparedOrderCount.Should().Be(readiness.ActiveSession.OrderCount);
        readiness.Replay.ComparedLedgerEntryCount.Should().Be(readiness.ActiveSession.LedgerEntryCount);
        readiness.WorkItems.Should().NotContain(item =>
            item.WorkItemId.Contains("paper-replay-stale", StringComparison.OrdinalIgnoreCase) ||
            item.WorkItemId.Contains("paper-replay-mismatch", StringComparison.OrdinalIgnoreCase));
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "promotion" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "dk1-trust" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "report-pack" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "reconciliation" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.ReadyForPaperOperation.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithOneDowngradedGate_ShouldSetReadyForPaperOperationFalse()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithOneDowngradedGate_ShouldSetReadyForPaperOperationFalse));
        var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);
        await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-not-ready",
            StrategyName: "Not Ready Strategy",
            InitialCash: 100_000m,
            Symbols: ["AAPL"]));

        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(persistence)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.AcceptanceGates.Should().Contain(g => g.Status != TradingAcceptanceGateStatusDto.Ready);
        readiness.ReadyForPaperOperation.Should().BeFalse();
    }

    [Fact]
    public void EvaluateOverallPosture_WithBlockedRiskRuleGate_ShouldBlockReadiness()
    {
        var gates = new[]
        {
            ReadyGate("replay"),
            ReadyGate("reconciliation"),
            ReadyGate("audit-controls"),
            ReadyGate("promotion"),
            ReadyGate("dk1-trust"),
            ReadyGate("report-pack"),
            ReadyGate("session"),
            ReadyGate("brokerage-sync"),
            new TradingAcceptanceGateDto(
                GateId: "risk-rules",
                Label: "Risk rules healthy",
                Status: TradingAcceptanceGateStatusDto.Blocked,
                Detail: "PositionLimit: Symbol limit breached.")
        };

        var status = InvokeEvaluateOverallPosture(gates);

        status.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
    }


    [Fact]
    public async Task GetAsync_WithReplayCoverageDrift_ShouldDowngradeReplayGateAndEmitSingleStaleReplayWorkItem()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithReplayCoverageDrift_ShouldDowngradeReplayGateAndEmitSingleStaleReplayWorkItem));
        var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-precedence",
            StrategyName: "Precedence Strategy",
            InitialCash: 100_000m,
            Symbols: ["AAPL"]));
        await persistence.VerifyReplayAsync(session.SessionId);

        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(persistence)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(provider, NullLogger<TradingOperatorReadinessService>.Instance);

        var readinessBeforeDrift = await service.GetAsync();
        readinessBeforeDrift.AcceptanceGates.Should().ContainSingle(g =>
            g.GateId == "replay" && g.Status == TradingAcceptanceGateStatusDto.Ready);

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("drift-order-1", "AAPL", 5m));

        var readinessAfterDrift = await service.GetAsync();

        readinessAfterDrift.AcceptanceGates.Should().ContainSingle(g =>
            g.GateId == "session" && g.Status == TradingAcceptanceGateStatusDto.Ready);
        readinessAfterDrift.AcceptanceGates.Should().ContainSingle(g =>
            g.GateId == "replay" && g.Status == TradingAcceptanceGateStatusDto.ReviewRequired && g.Detail.Contains("stale", StringComparison.OrdinalIgnoreCase));
        readinessAfterDrift.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId.StartsWith("paper-replay-stale", StringComparison.OrdinalIgnoreCase));
        readinessAfterDrift.ReadyForPaperOperation.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_AfterRestart_ShouldPreserveReplayParityAndExecutionAuditEvidence()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(GetAsync_AfterRestart_ShouldPreserveReplayParityAndExecutionAuditEvidence));
        var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(artifacts.RootPath, "audit-trail")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var store = new JsonlFilePaperSessionStore(
            Path.Combine(artifacts.RootPath, "paper-sessions"),
            NullLogger<JsonlFilePaperSessionStore>.Instance);
        var firstPersistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            store,
            auditTrail);
        var session = await firstPersistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "ibkr-restart-parity",
            StrategyName: "IBKR Restart Parity",
            InitialCash: 100_000m,
            Symbols: ["AAPL", "MSFT"]));
        await firstPersistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("restart-order-1", "AAPL", 15m));
        await firstPersistence.RecordFillAsync(session.SessionId, CreateExecutionFill("restart-order-1", "AAPL", 15m, 201.25m));
        var verification = await firstPersistence.VerifyReplayAsync(session.SessionId);
        verification.Should().NotBeNull();

        var secondPersistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            store,
            auditTrail);
        using var provider = new ServiceCollection()
            .AddSingleton(auditTrail)
            .AddSingleton(secondPersistence)
            .BuildServiceProvider();
        var service = new TradingOperatorReadinessService(provider, NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.ActiveSession.Should().NotBeNull();
        readiness.Replay.Should().NotBeNull();
        readiness.Replay!.IsConsistent.Should().BeTrue();
        readiness.Replay.VerificationAuditId.Should().Be(verification!.VerificationAuditId);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.WorkItems.Should().NotContain(item =>
            item.WorkItemId.StartsWith("paper-replay-stale", StringComparison.OrdinalIgnoreCase) ||
            item.WorkItemId.StartsWith("paper-replay-mismatch", StringComparison.OrdinalIgnoreCase));
    }

    private static ExecutionAuditTrailService CreateAuditTrail(string scenario)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "trading-readiness",
            scenario,
            Guid.NewGuid().ToString("N"));

        return new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(root),
            NullLogger<ExecutionAuditTrailService>.Instance);
    }

    private static TradingAcceptanceGateDto ReadyGate(string gateId) => new(
        GateId: gateId,
        Label: gateId,
        Status: TradingAcceptanceGateStatusDto.Ready,
        Detail: "Ready.");

    private static TradingAcceptanceGateStatusDto InvokeEvaluateOverallPosture(
        IReadOnlyList<TradingAcceptanceGateDto> gates)
    {
        var method = typeof(TradingOperatorReadinessService).GetMethod(
            "EvaluateOverallPosture",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (TradingAcceptanceGateStatusDto)method!.Invoke(null, [gates])!;
    }

    private static OrderState CreateExecutionOrderState(string orderId, string symbol, decimal quantity) => new()
    {
        OrderId = orderId,
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = quantity,
        Status = OrderStatus.Accepted,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow
    };

    private static ExecutionReport CreateExecutionFill(string orderId, string symbol, decimal quantity, decimal fillPrice) => new()
    {
        OrderId = orderId,
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Buy,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = fillPrice,
        Timestamp = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetAsync_ShouldEmitRouteAndDestinationPairsThatResolveToKnownInboxDestinations()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        var expected = new Dictionary<OperatorWorkItemKindDto, (string Route, string PageTag)>
        {
            [OperatorWorkItemKindDto.PaperReplay] = (UiApiRoutes.WorkstationTradingReadiness, "TradingShell"),
            [OperatorWorkItemKindDto.PromotionReview] = (UiApiRoutes.WorkstationTradingReadiness, "TradingShell"),
            [OperatorWorkItemKindDto.ProviderTrustGate] = (UiApiRoutes.WorkstationTradingReadiness, "TradingShell"),
            [OperatorWorkItemKindDto.ReconciliationBreak] = (UiApiRoutes.ReconciliationBreakQueue, "AccountingShell"),
            [OperatorWorkItemKindDto.ReportPackApproval] = (UiApiRoutes.FundReportPacks, "ReportingShell")
        };

        foreach (var pair in expected)
        {
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == pair.Key &&
                item.TargetRoute == pair.Value.Route &&
                item.TargetPageTag == pair.Value.PageTag);
        }

        var knownRouteAndPageTagPairs = new HashSet<(string? Route, string? PageTag)>
        {
            (UiApiRoutes.WorkstationTradingReadiness, "TradingShell"),
            (UiApiRoutes.ReconciliationBreakQueue, "AccountingShell"),
            (UiApiRoutes.FundReportPacks, "ReportingShell")
        };

        readiness.WorkItems
            .Select(item => (item.TargetRoute, item.TargetPageTag))
            .Should()
            .OnlyContain(pair => knownRouteAndPageTagPairs.Contains(pair));
    }

    [Fact]
    public async Task GetAsync_RouteCompatibilityGuard_ShouldKeepExpectedKindRouteMapStable()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        var requiredMap = new[]
        {
            (OperatorWorkItemKindDto.PaperReplay, UiApiRoutes.WorkstationTradingReadiness),
            (OperatorWorkItemKindDto.PromotionReview, UiApiRoutes.WorkstationTradingReadiness),
            (OperatorWorkItemKindDto.ProviderTrustGate, UiApiRoutes.WorkstationTradingReadiness),
            (OperatorWorkItemKindDto.ReconciliationBreak, UiApiRoutes.ReconciliationBreakQueue),
            (OperatorWorkItemKindDto.ReportPackApproval, UiApiRoutes.FundReportPacks)
        };

        foreach (var (kind, route) in requiredMap)
        {
            readiness.WorkItems.Should().Contain(item => item.Kind == kind && item.TargetRoute == route,
                $"{kind} route mapping is a compatibility contract for operator inbox deep-links");
        }
    }
}
