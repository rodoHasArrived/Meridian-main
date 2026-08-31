using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Testing;
using NSubstitute;

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
        first.ReadyForLiveOperation.Should().BeFalse();
        first.LiveOperationBlockers.Should().Contain([
            "acceptanceGate:session",
            "acceptanceGate:replay",
            "promotion:approved-live-trace",
            "brokerageSync:account-scope-required",
            "brokerExecutionReconciliation:unavailable"
        ]);
        first.LiveOperationRequirements.Select(static requirement => requirement.RequirementId).Should().Contain([
            "live-approval",
            "trusted-data",
            "paper-validation",
            "reconciliation-evidence",
            "accounting-records",
            "governed-reporting",
            "governance-signoff",
            "exception-handling",
            "rollback-kill-switch",
            "audit-retention"
        ]);
        first.LiveOperationRequirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "governance-signoff" &&
            requirement.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            requirement.ChecklistItem == PromotionApprovalChecklist.GovernanceSignoffReviewed &&
            requirement.ChecklistSatisfied == false &&
            requirement.EvidenceSatisfied == false);
        first.ReportPack.Should().NotBeNull();
        first.ReportPack!.Status.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        first.EvidenceCompleteness.Should().NotBeNull();
        first.EvidenceCompleteness!.BlockingGateIds.Should().Contain(["session", "replay"]);
        first.EvidenceCompleteness.ReviewGateIds.Should().Contain(["risk-rules", "report-pack"]);
        first.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "risk-rules" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.Detail.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
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
    public async Task GetAsync_WithCleanBrokerExecutionReconciliation_ShouldSurfaceReadyGateWithoutWorkItem()
    {
        var localOrder = CreateExecutionOrderState("order-1", "AAPL", 10m);
        var brokerOrder = CreateBrokerOrder("broker-1", "order-1", "AAPL", 10m);
        var gateway = CreateBrokerageGateway([brokerOrder]);
        var orderManager = CreateOrderManager([localOrder]);
        using var provider = CreateExecutionReconciliationProvider(gateway, orderManager);
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.ExecutionReconciliation.Should().NotBeNull();
        readiness.ExecutionReconciliation!.Status.Should().Be(TradingAcceptanceGateStatusDto.Ready);
        readiness.ExecutionReconciliation.BreakCount.Should().Be(0);
        readiness.ExecutionReconciliation.MatchedOpenOrderCount.Should().Be(1);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "broker-execution-reconciliation" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.WorkItems.Should().NotContain(item =>
            item.Kind == OperatorWorkItemKindDto.BrokerExecutionReconciliation);
    }

    [Fact]
    public async Task GetAsync_WithBrokerExecutionReconciliationBreak_ShouldBlockLiveGateAndEmitWorkItem()
    {
        var localOrder = CreateExecutionOrderState("order-1", "AAPL", 10m);
        var brokerOrder = CreateBrokerOrder("broker-1", "order-1", "AAPL", 12m);
        var gateway = CreateBrokerageGateway([brokerOrder]);
        var orderManager = CreateOrderManager([localOrder]);
        using var provider = CreateExecutionReconciliationProvider(gateway, orderManager);
        var service = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.ExecutionReconciliation.Should().NotBeNull();
        readiness.ExecutionReconciliation!.Status.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        readiness.ExecutionReconciliation.Breaks.Should().ContainSingle(item =>
            item.Kind == "QuantityMismatch" &&
            item.LocalOrderId == "order-1" &&
            item.BrokerOrderId == "broker-1" &&
            item.LocalValue == "10" &&
            item.BrokerValue == "12");
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "broker-execution-reconciliation" &&
            gate.Status == TradingAcceptanceGateStatusDto.Blocked &&
            gate.RequiredNextAction == "Review broker/OMS open-order breaks before live operation.");
        readiness.WorkItems.Should().ContainSingle(item =>
            item.Kind == OperatorWorkItemKindDto.BrokerExecutionReconciliation &&
            item.Tone == OperatorWorkItemToneDto.Critical &&
            item.Workspace == "Trading" &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness &&
            item.TargetPageTag == "TradingShell");
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


    [Fact]
    public void BuildReportPackReadinessForRuns_WithCriticalValidationIssue_ShouldBlockReadiness()
    {
        var snapshot = CreateReportPackSnapshot(
            status: GovernanceReportPackStatusDto.ReviewRequired,
            warnings: [],
            validationIssues:
            [
                new FundReportPackValidationIssueDto(
                    Code: "report-pack.missing-ledger-postings",
                    Severity: GovernanceReportValidationSeverityDto.Critical,
                    Title: "Missing ledger postings",
                    Message: "Missing ledger postings for as-of snapshot.")
            ]);

        var readiness = InvokeBuildReportPackReadinessForRuns(snapshot, ["run-1"]);

        readiness.Should().NotBeNull();
        readiness!.Status.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        readiness.Detail.Should().Contain("critical validation issue");
    }

    [Fact]
    public void BuildReportPackReadinessForRuns_WithValidatedStatusAndNoIssues_ShouldMarkReady()
    {
        var snapshot = CreateReportPackSnapshot(
            status: GovernanceReportPackStatusDto.Validated,
            warnings: [],
            validationIssues: []);

        var readiness = InvokeBuildReportPackReadinessForRuns(snapshot, ["run-1"]);

        readiness.Should().NotBeNull();
        readiness!.Status.Should().Be(TradingAcceptanceGateStatusDto.Ready);
    }

    [Fact]
    public void GetMissingPromotionTraceFields_WithLivePromotionMissingBrokerExecutionEvidence_ShouldNameMissingToken()
    {
        var evidenceReferences = CreateLivePromotionEvidenceReferences()
            .Where(static item => !item.StartsWith(PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed, StringComparison.Ordinal))
            .ToArray();
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            evidenceReferences);

        var missing = InvokeGetMissingPromotionTraceFields(promotion);

        missing.Should().Equal($"evidenceReferences:{PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed}");
    }

    [Fact]
    public void GetMissingPromotionTraceFields_WithLivePromotionEvidenceReferenceNoRetainedValue_ShouldNameInvalidField()
    {
        var evidenceReferences = CreateLivePromotionEvidenceReferences()
            .Select(static item => item.StartsWith(PromotionApprovalChecklist.AuditRetentionReviewed, StringComparison.Ordinal)
                ? $"{PromotionApprovalChecklist.AuditRetentionReviewed}:"
                : item)
            .ToArray();
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            evidenceReferences);

        var missing = InvokeGetMissingPromotionTraceFields(promotion);

        missing.Should().Equal($"evidenceReferences:{PromotionApprovalChecklist.AuditRetentionReviewed}:retainedEvidence");
    }

    [Fact]
    public void GetMissingPromotionTraceFields_WithLiveOverrideEvidenceReferencesStaleOverride_ShouldNameInvalidField()
    {
        var evidenceReferences = CreateLivePromotionEvidenceReferences()
            .Select(static item => item.StartsWith(PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.Ordinal)
                ? $"{PromotionApprovalChecklist.LiveOverrideReviewed}:manual-override/override-live-stale"
                : item)
            .ToArray();
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            evidenceReferences);

        var missing = InvokeGetMissingPromotionTraceFields(promotion);

        missing.Should().Equal($"evidenceReferences:{PromotionApprovalChecklist.LiveOverrideReviewed}:activeOverride");
    }

    [Fact]
    public void BuildPromotionGate_WithLivePromotionMissingBrokerChecklistItem_ShouldBlockReadiness()
    {
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)
            .Where(static item => !string.Equals(item, PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed, StringComparison.Ordinal))
            .ToArray();
        var promotion = CreateLivePromotionReadiness(checklist, CreateLivePromotionEvidenceReferences());

        var gate = InvokeBuildPromotionGate(promotion);

        gate.Status.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        gate.Detail.Should().Contain($"approvalChecklist:{PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed}");
    }

    [Fact]
    public void BuildLiveOperationBlockers_WithCompleteLiveProof_ShouldClearBlockers()
    {
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            CreateLivePromotionEvidenceReferences());

        var blockers = InvokeBuildLiveOperationBlockers(
            TradingAcceptanceGateStatusDto.Ready,
            [new TradingAcceptanceGateDto("promotion", "Promotion trace complete", TradingAcceptanceGateStatusDto.Ready, "Live approved.")],
            promotion,
            CreateHealthyBrokerageSyncStatus(),
            CreateReadyExecutionReconciliation());

        blockers.Should().BeEmpty();
    }

    [Fact]
    public void BuildLiveOperationBlockers_WithMissingW7PromotionEvidence_ShouldNameEachMissingEvidenceItem()
    {
        var evidenceReferences = CreateLivePromotionEvidenceReferences()
            .Where(static item =>
                !item.StartsWith(PromotionApprovalChecklist.GovernanceSignoffReviewed, StringComparison.Ordinal) &&
                !item.StartsWith(PromotionApprovalChecklist.AuditRetentionReviewed, StringComparison.Ordinal))
            .ToArray();
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            evidenceReferences);

        var blockers = InvokeBuildLiveOperationBlockers(
            TradingAcceptanceGateStatusDto.Ready,
            [new TradingAcceptanceGateDto("promotion", "Promotion trace complete", TradingAcceptanceGateStatusDto.Ready, "Live approved.")],
            promotion,
            CreateHealthyBrokerageSyncStatus(),
            CreateReadyExecutionReconciliation());

        blockers.Should().Contain("promotion:approved-live-trace");
        blockers.Should().Contain([
            $"promotion:evidenceReferences:{PromotionApprovalChecklist.GovernanceSignoffReviewed}",
            $"promotion:evidenceReferences:{PromotionApprovalChecklist.AuditRetentionReviewed}"
        ]);
        blockers.Should().NotContain("brokerageSync:account-scope-required");
        blockers.Should().NotContain("brokerExecutionReconciliation:unavailable");
    }

    [Fact]
    public void BuildLiveOperationRequirements_WithCompleteLiveProof_ShouldMarkEachRequirementReady()
    {
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            CreateLivePromotionEvidenceReferences());

        var requirements = InvokeBuildLiveOperationRequirements(
            promotion,
            CreateReadyExecutionReconciliation());

        requirements.Should().OnlyContain(requirement => requirement.Status == TradingAcceptanceGateStatusDto.Ready);
        requirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "live-approval" &&
            requirement.EvidenceReference == "audit-live-promotion" &&
            requirement.BlockerCode == null);
        requirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "broker-execution-reconciliation" &&
            requirement.ChecklistItem == PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed &&
            requirement.EvidenceReference == $"{PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed}:evidence/{PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed.ToLowerInvariant()}" &&
            requirement.ChecklistSatisfied &&
            requirement.EvidenceSatisfied);
        requirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "audit-retention" &&
            requirement.ChecklistItem == PromotionApprovalChecklist.AuditRetentionReviewed);
    }

    [Fact]
    public void BuildLiveOperationRequirements_WithMissingW7PromotionEvidence_ShouldExposeSpecificRequirementBlocker()
    {
        var evidenceReferences = CreateLivePromotionEvidenceReferences()
            .Where(static item => !item.StartsWith(PromotionApprovalChecklist.GovernanceSignoffReviewed, StringComparison.Ordinal))
            .ToArray();
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            evidenceReferences);

        var requirements = InvokeBuildLiveOperationRequirements(
            promotion,
            CreateReadyExecutionReconciliation());

        requirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "governance-signoff" &&
            requirement.Status == TradingAcceptanceGateStatusDto.Blocked &&
            requirement.ChecklistSatisfied &&
            requirement.EvidenceSatisfied == false &&
            requirement.EvidenceReference == null &&
            requirement.BlockerCode == $"promotion:evidenceReferences:{PromotionApprovalChecklist.GovernanceSignoffReviewed}" &&
            requirement.Detail.Contains(PromotionApprovalChecklist.GovernanceSignoffReviewed, StringComparison.Ordinal));
        requirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "live-approval" &&
            requirement.Status == TradingAcceptanceGateStatusDto.Blocked &&
            requirement.BlockerCode == "promotion:approved-live-trace");
    }

    [Fact]
    public void BuildLiveOperationRequirements_WithInvalidRetainedEvidence_ShouldExposeSpecificRequirementBlocker()
    {
        var evidenceReferences = CreateLivePromotionEvidenceReferences()
            .Select(static item => item.StartsWith(PromotionApprovalChecklist.AuditRetentionReviewed, StringComparison.Ordinal)
                ? $"{PromotionApprovalChecklist.AuditRetentionReviewed}:"
                : item)
            .ToArray();
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            evidenceReferences);

        var requirements = InvokeBuildLiveOperationRequirements(
            promotion,
            CreateReadyExecutionReconciliation());

        requirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "audit-retention" &&
            requirement.Status == TradingAcceptanceGateStatusDto.Blocked &&
            requirement.ChecklistSatisfied &&
            requirement.EvidenceSatisfied == false &&
            requirement.EvidenceReference == $"{PromotionApprovalChecklist.AuditRetentionReviewed}:" &&
            requirement.BlockerCode == $"promotion:evidenceReferences:{PromotionApprovalChecklist.AuditRetentionReviewed}:retainedEvidence" &&
            requirement.Detail.Contains("retained evidence", StringComparison.OrdinalIgnoreCase));
        requirements.Should().ContainSingle(requirement =>
            requirement.RequirementId == "live-approval" &&
            requirement.Status == TradingAcceptanceGateStatusDto.Blocked);
    }

    [Fact]
    public void BuildLiveOperationBlockers_WithInvalidLiveOverrideEvidence_ShouldNameActiveOverrideBlocker()
    {
        var evidenceReferences = CreateLivePromotionEvidenceReferences()
            .Select(static item => item.StartsWith(PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.Ordinal)
                ? $"{PromotionApprovalChecklist.LiveOverrideReviewed}:manual-override/override-live-stale"
                : item)
            .ToArray();
        var promotion = CreateLivePromotionReadiness(
            PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            evidenceReferences);

        var blockers = InvokeBuildLiveOperationBlockers(
            TradingAcceptanceGateStatusDto.Ready,
            [new TradingAcceptanceGateDto("promotion", "Promotion trace complete", TradingAcceptanceGateStatusDto.Ready, "Live approved.")],
            promotion,
            CreateHealthyBrokerageSyncStatus(),
            CreateReadyExecutionReconciliation());

        blockers.Should().Contain("promotion:approved-live-trace");
        blockers.Should().Contain($"promotion:evidenceReferences:{PromotionApprovalChecklist.LiveOverrideReviewed}:activeOverride");
    }


    private static TradingReportPackReadinessDto? InvokeBuildReportPackReadinessForRuns(
        FundReportPackSnapshotDto snapshot,
        IReadOnlyList<string> candidateRunIds)
    {
        var method = typeof(TradingOperatorReadinessService).GetMethod(
            "BuildReportPackReadinessForRuns",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (TradingReportPackReadinessDto?)method!.Invoke(null, [snapshot, candidateRunIds, null]);
    }

    private static IReadOnlyList<string> InvokeGetMissingPromotionTraceFields(TradingPromotionReadinessDto promotion)
    {
        var method = typeof(TradingOperatorReadinessService).GetMethod(
            "GetMissingPromotionTraceFields",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (IReadOnlyList<string>)method!.Invoke(null, [promotion])!;
    }

    private static TradingAcceptanceGateDto InvokeBuildPromotionGate(TradingPromotionReadinessDto promotion)
    {
        var method = typeof(TradingOperatorReadinessService).GetMethod(
            "BuildPromotionGate",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (TradingAcceptanceGateDto)method!.Invoke(null, [promotion])!;
    }

    private static IReadOnlyList<string> InvokeBuildLiveOperationBlockers(
        TradingAcceptanceGateStatusDto overallStatus,
        IReadOnlyList<TradingAcceptanceGateDto> acceptanceGates,
        TradingPromotionReadinessDto? promotion,
        WorkstationBrokerageSyncStatusDto? brokerageStatus,
        TradingExecutionReconciliationReadinessDto? executionReconciliation)
    {
        var method = typeof(TradingOperatorReadinessService).GetMethod(
            "BuildLiveOperationBlockers",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (IReadOnlyList<string>)method!.Invoke(null, [overallStatus, acceptanceGates, promotion, brokerageStatus, executionReconciliation])!;
    }

    private static IReadOnlyList<TradingLiveOperationRequirementDto> InvokeBuildLiveOperationRequirements(
        TradingPromotionReadinessDto? promotion,
        TradingExecutionReconciliationReadinessDto? executionReconciliation)
    {
        var method = typeof(TradingOperatorReadinessService).GetMethod(
            "BuildLiveOperationRequirements",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (IReadOnlyList<TradingLiveOperationRequirementDto>)method!.Invoke(null, [promotion, executionReconciliation])!;
    }

    private static TradingPromotionReadinessDto CreateLivePromotionReadiness(
        IReadOnlyList<string> approvalChecklist,
        IReadOnlyList<string> evidenceReferences)
        => new(
            State: "Approved",
            Reason: "Live readiness reviewed.",
            RequiresReview: false,
            SourceRunId: "run-live-source",
            TargetRunId: "run-live",
            SuggestedNextMode: RunType.Live.ToString(),
            AuditReference: "audit-live-promotion",
            ApprovalStatus: "Approved",
            ManualOverrideId: "override-live",
            ApprovedBy: "ops",
            ApprovalChecklist: approvalChecklist,
            EvidenceReferences: evidenceReferences);

    private static string[] CreateLivePromotionEvidenceReferences()
        => PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)
            .Select(static item => item switch
            {
                _ when string.Equals(item, PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.Ordinal)
                    => $"{item}:manual-override/override-live",
                _ when string.Equals(item, PromotionApprovalChecklist.PaperExecutionModelReviewed, StringComparison.Ordinal)
                    => $"{item}:paper-match/1+paper-cost/1",
                _ => $"{item}:evidence/{item.ToLowerInvariant()}"
            })
            .ToArray();

    private static WorkstationBrokerageSyncStatusDto CreateHealthyBrokerageSyncStatus()
        => new(
            FundAccountId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ProviderId: "alpaca",
            ExternalAccountId: "acct-live",
            Health: WorkstationBrokerageSyncHealth.Healthy,
            IsLinked: true,
            IsStale: false,
            LastAttemptedSyncAt: DateTimeOffset.UtcNow,
            LastSuccessfulSyncAt: DateTimeOffset.UtcNow,
            LastError: null,
            PositionCount: 2,
            OpenOrderCount: 1,
            FillCount: 4,
            CashTransactionCount: 3,
            SecurityMissingCount: 0,
            Warnings: []);

    private static TradingExecutionReconciliationReadinessDto CreateReadyExecutionReconciliation()
        => new(
            Status: TradingAcceptanceGateStatusDto.Ready,
            GatewayId: "alpaca",
            BrokerDisplayName: "Alpaca",
            BrokerHealthy: true,
            BrokerConnected: true,
            MatchedOpenOrderCount: 1,
            BreakCount: 0,
            ReconciledAt: DateTimeOffset.UtcNow,
            Detail: "Broker and OMS open orders match.",
            Breaks: []);

    private static FundReportPackSnapshotDto CreateReportPackSnapshot(
        GovernanceReportPackStatusDto status,
        IReadOnlyList<string> warnings,
        IReadOnlyList<FundReportPackValidationIssueDto> validationIssues)
        => new(
            ReportId: Guid.NewGuid(),
            FundProfileId: "fund-1",
            DisplayName: "Fund 1",
            ReportKind: GovernanceReportKindDto.TrialBalance,
            Currency: "USD",
            AsOf: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            GeneratedAt: new DateTimeOffset(2026, 5, 1, 1, 0, 0, TimeSpan.Zero),
            TotalNetAssets: 100m,
            AuditActor: "ops",
            CorrelationId: "corr-1",
            DecisionRationale: null,
            Provenance: new FundReportPackProvenanceDto(
                RelatedRunIds: ["run-1"],
                JournalEntryCount: 0,
                LedgerEntryCount: 0,
                TrialBalanceLineCount: 1,
                ReconciliationRunCount: 0,
                OpenReconciliationBreakCount: 0,
                SecurityResolvedCount: 0,
                SecurityMissingCount: 0,
                LineagePointers: [],
                SourceSnapshotHash: new string('a', 64)),
            Artifacts:
            [
                new FundReportPackArtifactDto(
                    ArtifactKind: "trial-balance",
                    Format: GovernanceReportArtifactFormatDto.Json,
                    RelativePath: "trial-balance.json",
                    SizeBytes: 10,
                    ChecksumSha256: new string('b', 64))
            ],
            Warnings: warnings)
        {
            Status = status,
            ValidationIssues = validationIssues
        };

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

    private static BrokerOrder CreateBrokerOrder(
        string brokerOrderId,
        string clientOrderId,
        string symbol,
        decimal quantity) => new()
        {
            OrderId = brokerOrderId,
            ClientOrderId = clientOrderId,
            Symbol = symbol,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = quantity,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static IBrokerageGateway CreateBrokerageGateway(IReadOnlyList<BrokerOrder> openOrders)
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        gateway.GatewayId.Returns("alpaca");
        gateway.BrokerDisplayName.Returns("Alpaca Markets");
        gateway.IsConnected.Returns(true);
        gateway.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BrokerHealthStatus.Healthy("ready")));
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(openOrders));
        return gateway;
    }

    private static IOrderManager CreateOrderManager(IReadOnlyList<OrderState> openOrders)
    {
        var orderManager = Substitute.For<IOrderManager>();
        orderManager.GetOpenOrders().Returns(openOrders);
        return orderManager;
    }

    private static ServiceProvider CreateExecutionReconciliationProvider(
        IBrokerageGateway gateway,
        IOrderManager orderManager)
        => new ServiceCollection()
            .AddSingleton<IExecutionGateway>(gateway)
            .AddSingleton<IOrderManager>(orderManager)
            .AddSingleton(new BrokerageExecutionReconciliationService(
                NullLogger<BrokerageExecutionReconciliationService>.Instance))
            .BuildServiceProvider();

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


    [Theory]
    [InlineData("alpaca", "/settings#alpaca-provider-setup")]
    [InlineData("ALPACA", "/settings#alpaca-provider-setup")]
    [InlineData("ib", "/settings#ibkr-provider-setup")]
    [InlineData("ibkr", "/settings#ibkr-provider-setup")]
    [InlineData("interactive-brokers", "/settings#ibkr-provider-setup")]
    [InlineData("stocksharp", "/settings#stocksharp-provider-setup")]
    [InlineData("robinhood", "/settings#robinhood-provider-setup")]
    [InlineData("unknown-provider", "/settings#provider-connection-center")]
    [InlineData("", "/settings#provider-connection-center")]
    public void ProviderConnectionRouteMapper_ShouldResolveProviderAwareRoutesWithFallback(string providerId, string expectedRoute)
    {
        var route = ProviderNavigationRouteMapper.ResolveProviderConnectionSettingsRoute(providerId);

        route.Should().Be(expectedRoute);
    }


}
