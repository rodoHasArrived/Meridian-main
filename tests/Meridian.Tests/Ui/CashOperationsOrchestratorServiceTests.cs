using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class CashOperationsOrchestratorServiceTests
{
    [Fact]
    public void BuildDailySyncWindows_FiltersUnavailableSources()
    {
        var sut = new CashOperationsOrchestratorService();
        var windows = new[]
        {
            new CashSyncWindow("US", "UTC", new TimeOnly(12,0), new TimeOnly(13,0), new Dictionary<string, CashSyncSourceAvailability>{{"A", CashSyncSourceAvailability.Offline}}, [Guid.NewGuid()]),
            new CashSyncWindow("EU", "UTC", new TimeOnly(8,0), new TimeOnly(9,0), new Dictionary<string, CashSyncSourceAvailability>{{"B", CashSyncSourceAvailability.Available}}, [Guid.NewGuid()])
        };

        var result = sut.BuildDailySyncWindows(DateTimeOffset.UtcNow, windows);
        result.Should().HaveCount(1);
        result[0].Region.Should().Be("EU");
    }

    [Fact]
    public void EvaluateApproval_HighValueAndRiskBucket_RequiresDualControl()
    {
        var sut = new CashOperationsOrchestratorService();
        var policy = new Meridian.Contracts.Workstation.ApprovalPolicy(0, 1_000_000m, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "USD" }, null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "high" }, false);

        var decision = sut.EvaluateApproval(2_000_000m, "USD", Guid.NewGuid(), "high", policy);
        decision.Queue.Should().Be("risk-approval");
        decision.DualControlRequired.Should().BeTrue();
    }

    [Fact]
    public void ResolveSettlementDateWithCutoff_AfterCutoff_PushesOneBusinessDay()
    {
        var sut = new CashOperationsOrchestratorService();
        var booking = new DateTimeOffset(2026, 5, 27, 18, 0, 0, TimeSpan.Zero);
        var result = sut.ResolveSettlementDateWithCutoff(booking, new TimeOnly(17, 0), 2);
        result.Should().Be(new DateOnly(2026, 5, 30));
    }

    [Fact]
    public void BuildStpTrail_InvalidValidation_FallsBackToException()
    {
        var sut = new CashOperationsOrchestratorService();
        var trail = sut.BuildStpTrail(Guid.NewGuid(), validationPassed: false, approved: false, settled: false, exceptionReason: "missing counterparty");
        trail.Should().ContainSingle(t => t.State == StpProcessingState.Exception);
        trail[^1].BreakQueueReason.Should().Be("missing counterparty");
    }
}
