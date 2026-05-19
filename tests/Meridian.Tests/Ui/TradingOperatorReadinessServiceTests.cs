using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
            "report-pack-lineage");
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
    public async Task GetAsync_WithStaleReplay_ShouldKeepStableWorkItemSeverityAndAuditReference()
    {
        await using var auditTrail = CreateAuditTrail(nameof(GetAsync_WithStaleReplay_ShouldKeepStableWorkItemSeverityAndAuditReference));
        var firstTimestamp = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        var secondTimestamp = firstTimestamp.AddMinutes(1);
        var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-stale",
            StrategyName: "Stale Strategy",
            InitialCash: 50_000m,
            Symbols: ["AAPL"]));
        await persistence.RecordOrderUpdateAsync(session.SessionId, new OrderState
        {
            OrderId = "o1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            Status = OrderStatus.Filled,
            CreatedAt = firstTimestamp,
            LastUpdatedAt = firstTimestamp,
            AverageFillPrice = 100m
        });
        await persistence.RecordFillAsync(session.SessionId, new ExecutionReport
        {
            OrderId = "o1",
            ReportType = ExecutionReportType.Fill,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            FillPrice = 100m,
            Timestamp = firstTimestamp
        });
        var verification = await persistence.VerifyReplayAsync(session.SessionId);
        await persistence.RecordOrderUpdateAsync(session.SessionId, new OrderState
        {
            OrderId = "o2",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            Status = OrderStatus.Filled,
            CreatedAt = secondTimestamp,
            LastUpdatedAt = secondTimestamp,
            AverageFillPrice = 101m
        });

        using var provider = new ServiceCollection().AddSingleton(auditTrail).AddSingleton(persistence).BuildServiceProvider();
        var service = new TradingOperatorReadinessService(provider, NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await service.GetAsync();

        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Warning &&
            item.AuditReference == verification!.VerificationAuditId &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness);
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
}
