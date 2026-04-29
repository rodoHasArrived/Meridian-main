using FluentAssertions;
using Meridian.Contracts.Workstation;
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
            "dk1-trust-packet-unavailable");
        secondIds.Should().Equal(firstIds);
        firstIds.Should().NotContain(static id => id.StartsWith("operator-", StringComparison.OrdinalIgnoreCase));

        first.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Critical);
        first.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        first.ReadyForPaperOperation.Should().BeFalse();
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
