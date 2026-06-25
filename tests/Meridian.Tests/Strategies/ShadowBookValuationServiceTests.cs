using FluentAssertions;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Strategies;

public sealed class ShadowBookValuationServiceTests
{
    [Fact]
    public async Task RunAsync_IsDeterministic_ForEquivalentInputs()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo);

        var request = BuildRequest(externalNav: 2_000_000m);
        var first = await service.RunAsync(request);
        var second = await service.RunAsync(request with { OperatorId = "operator-b" });

        first.Snapshot.VersionHash.Should().Be(second.Snapshot.VersionHash);
        first.Snapshot.ShadowNav.Should().Be(second.Snapshot.ShadowNav);
    }

    [Fact]
    public async Task RunAsync_Breach_EmitsBreak_AndBlocksCloseWhenPolicyEnabled()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(5m, 1m)
        });

        var result = await service.RunAsync(BuildRequest(externalNav: 1_000_000m));

        result.Snapshot.Breached.Should().BeTrue();
        result.EmittedBreak.Should().NotBeNull();
        result.CloseBlocked.Should().BeTrue();
        var queue = await repo.GetAllAsync();
        queue.Should().ContainSingle(item => item.BreakId == result.EmittedBreak!.BreakId);
    }

    [Fact]
    public async Task RunAsync_WithinThreshold_DoesNotEmitBreak()
    {
        var repo = new FileReconciliationBreakQueueRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var service = new ShadowBookValuationService(repo, new Dictionary<string, ShadowBookVarianceThresholds>
        {
            ["Macro"] = new ShadowBookVarianceThresholds(100_000m, 10_000m)
        });

        var result = await service.RunAsync(BuildRequest(externalNav: 259_500m) with { BlockCloseOnBreach = false });

        result.Snapshot.Breached.Should().BeFalse();
        result.EmittedBreak.Should().BeNull();
        result.CloseBlocked.Should().BeFalse();
    }

    private static ShadowBookValuationRequest BuildRequest(decimal externalNav) => new(
        AccountId: "acct-001",
        StrategyProfile: "Macro",
        PeriodEnd: new DateOnly(2026, 3, 31),
        Positions:
        [
            new ShadowBookPositionInput("AAPL", 1000m, "USD"),
            new ShadowBookPositionInput("SAP", 500m, "EUR")
        ],
        PricesBySymbol: new Dictionary<string, decimal> { ["AAPL"] = 180m, ["SAP"] = 130m },
        FxRatesByCurrency: new Dictionary<string, decimal> { ["USD"] = 1m, ["EUR"] = 1.1m },
        Fees: 2_500m,
        Accruals: 10_000m,
        ExternalReferenceNav: externalNav,
        BlockCloseOnBreach: true,
        OperatorId: "operator-a");
}
