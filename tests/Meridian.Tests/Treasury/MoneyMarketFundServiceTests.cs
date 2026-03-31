using FluentAssertions;
using Meridian.Application.Treasury;
using Meridian.Contracts.Treasury;

namespace Meridian.Tests.Treasury;

/// <summary>
/// Unit tests for <see cref="InMemoryMoneyMarketFundService"/> covering the MMF reference
/// surface: GetById, Search (WAM cap, sweep flag, active-only), and SweepProfile.
/// </summary>
public sealed class MoneyMarketFundServiceTests
{
    [Fact]
    public async Task GetByIdAsync_WhenFundRegistered_ReturnsDetail()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Vanguard Prime MMF", "USD",
            fundFamily: "Vanguard", isSweepEligible: true,
            weightedAverageMaturityDays: 30, hasLiquidityFee: false);

        var result = await service.GetByIdAsync(securityId);

        result.Should().NotBeNull();
        result!.SecurityId.Should().Be(securityId);
        result.DisplayName.Should().Be("Vanguard Prime MMF");
        result.IsSweepEligible.Should().BeTrue();
        result.WeightedAverageMaturityDays.Should().Be(30);
        result.HasLiquidityFee.Should().BeFalse();
        result.Status.Should().Be("Active");
    }

    [Fact]
    public async Task GetByIdAsync_WhenFundNotRegistered_ReturnsNull()
    {
        var service = new InMemoryMoneyMarketFundService();

        var result = await service.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── WAM-band liquidity state tests ───────────────────────────────────────

    [Theory]
    [InlineData(7, MmfLiquidityState.Liquid)]
    [InlineData(60, MmfLiquidityState.Liquid)]
    [InlineData(61, MmfLiquidityState.Restricted)]
    [InlineData(90, MmfLiquidityState.Restricted)]
    public async Task GetLiquidityAsync_WamBands_ProduceExpectedState(
        int wamDays, MmfLiquidityState expectedState)
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "WAM Test Fund", "USD",
            fundFamily: null, isSweepEligible: false,
            weightedAverageMaturityDays: wamDays, hasLiquidityFee: false);

        var liquidity = await service.GetLiquidityAsync(securityId);

        liquidity.Should().NotBeNull();
        liquidity!.State.Should().Be(expectedState);
        liquidity.WeightedAverageMaturityDays.Should().Be(wamDays);
    }

    [Fact]
    public async Task GetLiquidityAsync_NullWam_DefaultsToLiquid()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "No-WAM Fund", "USD",
            fundFamily: null, isSweepEligible: true,
            weightedAverageMaturityDays: null, hasLiquidityFee: false);

        var liquidity = await service.GetLiquidityAsync(securityId);

        liquidity!.State.Should().Be(MmfLiquidityState.Liquid);
        liquidity.WeightedAverageMaturityDays.Should().BeNull();
    }

    [Fact]
    public async Task GetLiquidityAsync_InactiveFund_ReturnsInactiveState()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Dormant MMF", "USD",
            fundFamily: null, isSweepEligible: false,
            weightedAverageMaturityDays: 10, hasLiquidityFee: false,
            isActive: false);

        var liquidity = await service.GetLiquidityAsync(securityId);

        liquidity!.State.Should().Be(MmfLiquidityState.Inactive);
    }

    [Fact]
    public async Task GetLiquidityAsync_NonexistentSecurity_ReturnsNull()
    {
        var service = new InMemoryMoneyMarketFundService();

        var liquidity = await service.GetLiquidityAsync(Guid.NewGuid());

        liquidity.Should().BeNull();
    }

    // ── Sweep-state tests ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSweepProfileAsync_ReturnsSweepFlagsAndFamily()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Fidelity Cash MMF", "USD",
            fundFamily: "Fidelity", isSweepEligible: true,
            weightedAverageMaturityDays: 20, hasLiquidityFee: true);

        var sweep = await service.GetSweepProfileAsync(securityId);

        sweep.Should().NotBeNull();
        sweep!.SecurityId.Should().Be(securityId);
        sweep.IsSweepEligible.Should().BeTrue();
        sweep.HasLiquidityFee.Should().BeTrue();
        sweep.FundFamily.Should().Be("FIDELITY");
    }

    [Fact]
    public async Task GetSweepProfileAsync_NonexistentSecurity_ReturnsNull()
    {
        var service = new InMemoryMoneyMarketFundService();

        var sweep = await service.GetSweepProfileAsync(Guid.NewGuid());

        sweep.Should().BeNull();
    }

    [Fact]
    public async Task GetSweepProfileAsync_NonSweepEligibleFund_ReflectsFalseFlag()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Non-Sweep Fund", "USD",
            fundFamily: null, isSweepEligible: false,
            weightedAverageMaturityDays: 45, hasLiquidityFee: false);

        var sweep = await service.GetSweepProfileAsync(securityId);

        sweep!.IsSweepEligible.Should().BeFalse();
        sweep.HasLiquidityFee.Should().BeFalse();
        sweep.FundFamily.Should().BeNull();
    }

    // ── Search filter tests ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_FiltersBySweepEligible_ReturnsOnlyMatchingFunds()
    {
        var service = new InMemoryMoneyMarketFundService();
        var sweepId = Guid.NewGuid();
        var nonSweepId = Guid.NewGuid();
        await service.RegisterAsync(sweepId, "Sweep Fund", "USD", null, isSweepEligible: true, 30, false);
        await service.RegisterAsync(nonSweepId, "Non-Sweep Fund", "USD", null, isSweepEligible: false, 30, false);

        var results = await service.SearchAsync(new MmfSearchQuery(IsSweepEligible: true));

        results.Should().ContainSingle();
        results[0].SecurityId.Should().Be(sweepId);
    }

    [Fact]
    public async Task SearchAsync_FiltersByMaxWam_ExcludesFundsAboveThreshold()
    {
        var service = new InMemoryMoneyMarketFundService();
        var shortWamId = Guid.NewGuid();
        var longWamId = Guid.NewGuid();
        await service.RegisterAsync(shortWamId, "Short WAM", "USD", null, true, 20, false);
        await service.RegisterAsync(longWamId, "Long WAM", "USD", null, true, 80, false);

        var results = await service.SearchAsync(new MmfSearchQuery(MaxWamDays: 60));

        results.Should().ContainSingle();
        results[0].SecurityId.Should().Be(shortWamId);
    }

    [Fact]
    public async Task SearchAsync_ActiveOnlyDefault_ExcludesInactiveFunds()
    {
        var service = new InMemoryMoneyMarketFundService();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        await service.RegisterAsync(activeId, "Active Fund", "USD", null, true, 15, false, isActive: true);
        await service.RegisterAsync(inactiveId, "Inactive Fund", "USD", null, true, 15, false, isActive: false);

        var results = await service.SearchAsync(new MmfSearchQuery());

        results.Should().ContainSingle();
        results[0].SecurityId.Should().Be(activeId);
    }

    [Fact]
    public async Task SearchAsync_ActiveOnlyFalse_IncludesInactiveFunds()
    {
        var service = new InMemoryMoneyMarketFundService();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        await service.RegisterAsync(activeId, "Active Fund", "USD", null, true, 15, false, isActive: true);
        await service.RegisterAsync(inactiveId, "Inactive Fund", "USD", null, true, 15, false, isActive: false);

        var results = await service.SearchAsync(new MmfSearchQuery(ActiveOnly: false));

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_FiltersByHasLiquidityFee()
    {
        var service = new InMemoryMoneyMarketFundService();
        var feeId = Guid.NewGuid();
        var noFeeId = Guid.NewGuid();
        await service.RegisterAsync(feeId, "Fee Fund", "USD", null, false, 30, hasLiquidityFee: true);
        await service.RegisterAsync(noFeeId, "No-Fee Fund", "USD", null, false, 30, hasLiquidityFee: false);

        var results = await service.SearchAsync(new MmfSearchQuery(HasLiquidityFee: true));

        results.Should().ContainSingle();
        results[0].SecurityId.Should().Be(feeId);
    }

    [Fact]
    public async Task SearchAsync_FiltersByLiquidityState_ReturnsOnlyRestrictedFunds()
    {
        var service = new InMemoryMoneyMarketFundService();
        var liquidId = Guid.NewGuid();
        var restrictedId = Guid.NewGuid();
        await service.RegisterAsync(liquidId, "Liquid Fund", "USD", null, true, 30, false);
        await service.RegisterAsync(restrictedId, "Restricted Fund", "USD", null, false, 90, false);

        var results = await service.SearchAsync(new MmfSearchQuery(LiquidityState: MmfLiquidityState.Restricted));

        results.Should().ContainSingle();
        results[0].SecurityId.Should().Be(restrictedId);
    }

    [Fact]
    public async Task SearchAsync_PaginationSkipTake_Works()
    {
        var service = new InMemoryMoneyMarketFundService();
        for (var i = 0; i < 10; i++)
            await service.RegisterAsync(Guid.NewGuid(), $"Fund {i}", "USD", null, false, 20, false);

        var page1 = await service.SearchAsync(new MmfSearchQuery(Skip: 0, Take: 5));
        var page2 = await service.SearchAsync(new MmfSearchQuery(Skip: 5, Take: 5));

        page1.Should().HaveCount(5);
        page2.Should().HaveCount(5);
        page1.Select(f => f.SecurityId).Should().NotIntersectWith(page2.Select(f => f.SecurityId));
    }
}
