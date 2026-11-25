using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Review B1: the shadow comparison reports agreement, per-date differences and one-sided blocks.</summary>
[Trait("Category", "Unit")]
public sealed class CashFlowEngineShadowComparerTests
{
    private static readonly DateOnly Valuation = new(2026, 1, 1);

    [Fact]
    public async Task SameCash_Matches_AndDifferentCash_ReportsTheDate()
    {
        var id = Guid.NewGuid();
        var (comparer, legacy, contractual) = Compose(id);
        legacy.GetProjectionAsync(id, StructuredCashFlowScenario.Base, Arg.Any<CancellationToken>())
            .Returns(Projection(id, (new DateOnly(2026, 7, 1), 3_000m, 0m), (new DateOnly(2027, 1, 1), 3_000m, 100_000m)));
        contractual.PreviewSecurityAsync(id, Arg.Any<ContractualCashFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result([(new DateOnly(2026, 7, 1), 3_000m, 0m), (new DateOnly(2027, 1, 1), 3_000m, 100_000m)]));

        (await comparer.CompareAsync(id, Valuation)).Outcome.Should().Be(CashFlowShadowOutcome.Match);

        contractual.PreviewSecurityAsync(id, Arg.Any<ContractualCashFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result([(new DateOnly(2026, 7, 1), 2_958.90m, 0m), (new DateOnly(2027, 1, 1), 3_000m, 100_000m)]));
        var differing = await comparer.CompareAsync(id, Valuation);

        differing.Outcome.Should().Be(CashFlowShadowOutcome.Differs);
        differing.Differences.Should().ContainSingle(d => d.Date == new DateOnly(2026, 7, 1) && d.ContractualInterest == 2_958.90m);
    }

    [Fact]
    public async Task OneSidedBlock_IsReportedWithItsReason()
    {
        var id = Guid.NewGuid();
        var (comparer, legacy, contractual) = Compose(id);
        legacy.GetProjectionAsync(id, StructuredCashFlowScenario.Base, Arg.Any<CancellationToken>())
            .Returns(Projection(id, (new DateOnly(2026, 7, 1), 3_000m, 0m)));
        contractual.PreviewSecurityAsync(id, Arg.Any<ContractualCashFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result([], new CashFlowCalculationBlocker("UnitUndeclared", "Rate unit is undeclared.")));

        var comparison = await comparer.CompareAsync(id, Valuation);

        comparison.Outcome.Should().Be(CashFlowShadowOutcome.ContractualOnlyBlocked);
        comparison.ContractualBlockers.Should().ContainSingle().Which.Should().StartWith("UnitUndeclared");
    }

    private static (CashFlowEngineShadowComparer, ISecurityMasterCashFlowService, IContractualCashFlowPreviewService) Compose(Guid id)
    {
        var legacy = Substitute.For<ISecurityMasterCashFlowService>();
        var contractual = Substitute.For<IContractualCashFlowPreviewService>();
        return (new CashFlowEngineShadowComparer(legacy, contractual), legacy, contractual);
    }

    private static StructuredCashFlowProjectionDto Projection(Guid id, params (DateOnly Date, decimal Interest, decimal Principal)[] rows)
        => new(id, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base, DateTimeOffset.UtcNow,
            rows.Select(row => new StructuredCashFlowScheduleEntry(
                new DateTimeOffset(row.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), row.Principal, row.Interest, 1m)).ToArray(),
            StructuredCashFlowStaleness.Fresh, null, [], null);

    private static ContractualCashFlowResult Result(
        IReadOnlyList<(DateOnly Date, decimal Interest, decimal Principal)> rows, params CashFlowCalculationBlocker[] blockers)
        => new(
            rows.Select(row => new ContractualCashFlowEvent($"e-{row.Date}", "s", null, row.Date, null, null, 0m, row.Interest, 0m,
                row.Principal, 0m, 0m, 0m, 0m, 0m, CashFlowLegDirection.Receive)).ToArray(),
            [], [], blockers,
            new ContractualCashFlowEvidence("e", "m", "p", "k", "i", "o", "t", "v", false, [], [], []));
}
