using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class TradingOperatorReadinessAggregationTests
{
    [Theory]
    [InlineData("broker-execution-reconciliation", TradingAcceptanceGateStatusDto.Blocked)]
    [InlineData("broker-execution-reconciliation", TradingAcceptanceGateStatusDto.ReviewRequired)]
    [InlineData("brokerage-portfolio-recovery", TradingAcceptanceGateStatusDto.Blocked)]
    [InlineData("brokerage-portfolio-recovery", TradingAcceptanceGateStatusDto.ReviewRequired)]
    [InlineData("future-gate", TradingAcceptanceGateStatusDto.Blocked)]
    public void AggregateReadiness_WithAddedNonreadyGate_AgreesWithEvidence(
        string gateId,
        TradingAcceptanceGateStatusDto status)
    {
        IReadOnlyList<TradingAcceptanceGateDto> gates =
            [.. ReadyGates(), new(gateId, gateId, status, "Outstanding evidence.")];

        var overallStatus = Invoke<TradingAcceptanceGateStatusDto>("EvaluateOverallPosture", gates);
        var evidence = Invoke<EvidenceCompletenessSummaryDto>(
            "BuildEvidenceCompleteness", gates, Array.Empty<OperatorWorkItemDto>());

        overallStatus.Should().Be(status);
        evidence.Status.Should().Be(overallStatus);
        evidence.TotalGateCount.Should().Be(10);
        evidence.ReadyGateCount.Should().Be(9);
        evidence.ScorePercent.Should().Be(90);
        if (status == TradingAcceptanceGateStatusDto.Blocked)
            evidence.BlockingGateIds.Should().Contain(gateId);
        else
            evidence.ReviewGateIds.Should().Contain(gateId);
    }

    [Theory]
    [InlineData(TradingAcceptanceGateStatusDto.Blocked)]
    [InlineData(TradingAcceptanceGateStatusDto.ReviewRequired)]
    [InlineData(TradingAcceptanceGateStatusDto.Unknown)]
    public void LiveOperationBlockers_WithReadyAggregate_RetainsPortfolioRecoveryGateBlocker(
        TradingAcceptanceGateStatusDto status)
    {
        IReadOnlyList<TradingAcceptanceGateDto> gates =
            [.. ReadyGates(), new("brokerage-portfolio-recovery", "Broker portfolio", status, "Synchronization required.")];

        var blockers = Invoke<IReadOnlyList<string>>(
            "BuildLiveOperationBlockers", TradingAcceptanceGateStatusDto.Ready, gates, null, null, null);

        blockers.Should().Contain("acceptanceGate:brokerage-portfolio-recovery");
        blockers.Where(static blocker => blocker.StartsWith("acceptanceGate:", StringComparison.Ordinal))
            .Should().ContainSingle();
    }

    [Fact]
    public void AggregateReadiness_WithUnknownGateStatus_RequiresReviewInEvidence()
    {
        IReadOnlyList<TradingAcceptanceGateDto> gates =
            [.. ReadyGates(), new("brokerage-portfolio-recovery", "Broker portfolio", TradingAcceptanceGateStatusDto.Unknown, "Not evaluated.")];

        var overallStatus = Invoke<TradingAcceptanceGateStatusDto>("EvaluateOverallPosture", gates);
        var evidence = Invoke<EvidenceCompletenessSummaryDto>(
            "BuildEvidenceCompleteness", gates, Array.Empty<OperatorWorkItemDto>());

        overallStatus.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        evidence.Status.Should().Be(overallStatus);
        evidence.ReviewGateIds.Should().Contain("brokerage-portfolio-recovery");
    }

    private static TradingAcceptanceGateDto[] ReadyGates() =>
        new[] { "replay", "reconciliation", "audit-controls", "risk-rules", "promotion", "dk1-trust", "report-pack", "session", "brokerage-sync" }
            .Select(static gateId => new TradingAcceptanceGateDto(gateId, gateId, TradingAcceptanceGateStatusDto.Ready, "Ready."))
            .ToArray();

    private static T Invoke<T>(string methodName, params object?[] arguments)
    {
        var method = typeof(TradingOperatorReadinessService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (T)method!.Invoke(null, arguments)!;
    }
}
