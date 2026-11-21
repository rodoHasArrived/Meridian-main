using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.Application;

public sealed class StructuredCashFlowLedgerGateTests
{
    private static readonly Guid SecurityId = Guid.Parse("77777777-1111-2222-3333-777777777777");

    private static StructuredCashFlowProjectionDto BuildProjection(
        StructuredCashFlowStaleness staleness = StructuredCashFlowStaleness.Fresh,
        StructuredCashFlowScenario scenario = StructuredCashFlowScenario.Base,
        decimal firstInterest = 5m,
        decimal secondInterest = 5m)
        => new(
            SecurityId,
            StructuredCashFlowSourceKind.CalculatedBullet,
            scenario,
            DateTimeOffset.UtcNow,
            new[]
            {
                new StructuredCashFlowScheduleEntry(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), 0m, firstInterest, 1m),
                new StructuredCashFlowScheduleEntry(new DateTimeOffset(2030, 7, 1, 0, 0, 0, TimeSpan.Zero), 100m, secondInterest, 0m)
            },
            staleness);

    [Theory]
    [InlineData(StructuredCashFlowStaleness.Unknown, StructuredCashFlowScenario.Base, "freshness is unknown")]
    [InlineData(StructuredCashFlowStaleness.Stale, StructuredCashFlowScenario.Base, "stale")]
    [InlineData(StructuredCashFlowStaleness.Fresh, StructuredCashFlowScenario.Up100, "what-if")]
    public void SourceAndScenarioGate_RemainsOnTheLiveAccrualPath(
        StructuredCashFlowStaleness staleness, StructuredCashFlowScenario scenario, string reason)
        => StructuredCashFlowLedgerGate.EvaluateBlockReason(BuildProjection(staleness, scenario)).Should().Contain(reason);

    [Fact]
    public void NegativeInterest_BlocksTheWholeSchedule()
        => StructuredCashFlowLedgerGate.EvaluateBlockReason(BuildProjection(secondInterest: -1m))
            .Should().Be(SecurityInterestSupport.NegativeInterestReason);

    [Fact]
    public void NormalizedSchedule_RemainsBlockedEvenWithoutTextReason()
        => StructuredCashFlowLedgerGate.EvaluateBlockReason(BuildProjection() with { IsNormalizedPer100 = true })
            .Should().Contain("normalized");

    [Fact]
    public void DeclaredSchedule_RemainsBlockedAndKeepsUpstreamReason()
    {
        var projection = BuildProjection() with
        {
            ScheduleEvidence = new StructuredCashFlowScheduleEvidence(ScheduleGenerator.KernelVersion,
                Meridian.Contracts.SecurityMaster.ScheduleSpecFactory.DeclaredMappingVersion, [], [])
        };
        StructuredCashFlowLedgerGate.EvaluateBlockReason(projection).Should().Contain("Declared-schedule");
        StructuredCashFlowLedgerGate.EvaluateBlockReason(projection with { BlockedReason = "Upstream reason." })
            .Should().Be("Upstream reason.");
    }

    [Fact]
    public void FreshBaseProjection_PassesTheGate()
        => StructuredCashFlowLedgerGate.IsPostable(BuildProjection()).Should().BeTrue();
}
