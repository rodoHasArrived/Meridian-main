using FluentAssertions;
using Meridian.Application.Treasury;
using Meridian.Contracts.Treasury;

namespace Meridian.Tests.Treasury;

/// <summary>
/// Unit tests for <see cref="IMmfLiquidityService"/> surface of <see cref="InMemoryMoneyMarketFundService"/>.
/// Covers treasury/governance liquidity views: liquid-fund portfolio listing, family projections,
/// liquidity-state overrides, and the WAM-band transition boundary.
/// </summary>
public sealed class MmfLiquidityServiceTests
{
    // ── GetAllLiquidFundsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetAllLiquidFundsAsync_ReturnsOnlyActiveLiquidFunds()
    {
        var service = new InMemoryMoneyMarketFundService();
        await service.RegisterAsync(Guid.NewGuid(), "Liquid A", "USD", "Vanguard", true, 30, false);
        await service.RegisterAsync(Guid.NewGuid(), "Liquid B", "USD", "Fidelity", false, 45, false);
        await service.RegisterAsync(Guid.NewGuid(), "Restricted C", "USD", null, false, 75, false);
        await service.RegisterAsync(Guid.NewGuid(), "Inactive D", "USD", null, false, 20, false, isActive: false);

        IMmfLiquidityService liquidityService = service;
        var liquid = await liquidityService.GetAllLiquidFundsAsync();

        liquid.Should().HaveCount(2, because: "only active funds with WAM ≤ 60 are liquid");
        liquid.Should().AllSatisfy(f => f.State.Should().Be(MmfLiquidityState.Liquid));
    }

    [Fact]
    public async Task GetAllLiquidFundsAsync_EmptyRegistry_ReturnsEmptyList()
    {
        IMmfLiquidityService liquidityService = new InMemoryMoneyMarketFundService();

        var liquid = await liquidityService.GetAllLiquidFundsAsync();

        liquid.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllLiquidFundsAsync_AllRestrictedFunds_ReturnsEmptyList()
    {
        var service = new InMemoryMoneyMarketFundService();
        await service.RegisterAsync(Guid.NewGuid(), "Restricted A", "USD", null, false, 90, false);
        await service.RegisterAsync(Guid.NewGuid(), "Restricted B", "USD", null, false, 120, false);

        IMmfLiquidityService liquidityService = service;
        var liquid = await liquidityService.GetAllLiquidFundsAsync();

        liquid.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllLiquidFundsAsync_IncludesNullWamFunds_TreatedAsLiquid()
    {
        var service = new InMemoryMoneyMarketFundService();
        var id = Guid.NewGuid();
        await service.RegisterAsync(id, "Operational MMF", "USD", null, true, null, false);

        IMmfLiquidityService liquidityService = service;
        var liquid = await liquidityService.GetAllLiquidFundsAsync();

        liquid.Should().ContainSingle(f => f.SecurityId == id);
    }

    // ── GetFamilyProjectionAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetFamilyProjectionAsync_ReturnsFamilyWithAllMembers()
    {
        var service = new InMemoryMoneyMarketFundService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await service.RegisterAsync(id1, "T. Rowe Price Gov", "USD", "T. Rowe Price", true, 20, false);
        await service.RegisterAsync(id2, "T. Rowe Price Prime", "USD", "T. Rowe Price", false, 35, false);

        IMmfLiquidityService liquidityService = service;
        var projection = await liquidityService.GetFamilyProjectionAsync("T. Rowe Price");

        projection.Should().NotBeNull();
        projection!.NormalizedFamilyName.Should().Be("T. ROWE PRICE");
        projection.MemberCount.Should().Be(2);
        projection.MemberSecurityIds.Should().Contain(id1).And.Contain(id2);
    }

    [Fact]
    public async Task GetFamilyProjectionAsync_UnknownFamily_ReturnsNull()
    {
        IMmfLiquidityService liquidityService = new InMemoryMoneyMarketFundService();

        var projection = await liquidityService.GetFamilyProjectionAsync("NoSuchFamily");

        projection.Should().BeNull();
    }

    // ── GetLiquidityStateAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetLiquidityStateAsync_WithSuspendedOverride_ReturnsSuspended()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Suspended Fund", "USD",
            null, isSweepEligible: false, weightedAverageMaturityDays: 15, hasLiquidityFee: false,
            liquidityStateOverride: MmfLiquidityState.Suspended);

        IMmfLiquidityService liquidityService = service;
        var result = await liquidityService.GetLiquidityStateAsync(securityId);

        result.Should().NotBeNull();
        result!.State.Should().Be(MmfLiquidityState.Suspended,
            because: "manual override takes precedence over WAM-based classification");
    }

    [Fact]
    public async Task GetLiquidityStateAsync_WithRestrictedOverride_ReturnsRestricted()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Operationally Restricted", "USD",
            null, isSweepEligible: false, weightedAverageMaturityDays: 10, hasLiquidityFee: false,
            liquidityStateOverride: MmfLiquidityState.Restricted);

        IMmfLiquidityService liquidityService = service;
        var result = await liquidityService.GetLiquidityStateAsync(securityId);

        // WAM of 10 days would normally be Liquid, but override wins
        result!.State.Should().Be(MmfLiquidityState.Restricted);
    }

    [Fact]
    public async Task GetLiquidityStateAsync_NonexistentSecurity_ReturnsNull()
    {
        IMmfLiquidityService liquidityService = new InMemoryMoneyMarketFundService();

        var result = await liquidityService.GetLiquidityStateAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── WAM boundary: 60-day threshold ───────────────────────────────────────

    [Theory]
    [InlineData(59, MmfLiquidityState.Liquid)]
    [InlineData(60, MmfLiquidityState.Liquid)]
    [InlineData(61, MmfLiquidityState.Restricted)]
    public async Task GetLiquidityStateAsync_WamAtBoundary_ClassifiedCorrectly(
        int wamDays, MmfLiquidityState expected)
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Boundary Fund", "USD",
            null, false, wamDays, false);

        IMmfLiquidityService liquidityService = service;
        var result = await liquidityService.GetLiquidityStateAsync(securityId);

        result!.State.Should().Be(expected);
    }

    // ── Treasury view: governance consumers ─────────────────────────────────

    [Fact]
    public async Task TreasuryView_MixedPortfolio_CorrectLiquidityDistribution()
    {
        var service = new InMemoryMoneyMarketFundService();
        var liquidIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var restrictedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        foreach (var id in liquidIds)
            await service.RegisterAsync(id, $"Liquid {id}", "USD", null, true, 30, false);

        foreach (var id in restrictedIds)
            await service.RegisterAsync(id, $"Restricted {id}", "USD", null, false, 90, false);

        IMmfLiquidityService liquidityService = service;
        var liquidFunds = await liquidityService.GetAllLiquidFundsAsync();

        liquidFunds.Should().HaveCount(3);
        liquidFunds.Select(f => f.SecurityId).Should().BeEquivalentTo(liquidIds);
    }

    [Fact]
    public async Task TreasuryView_GetByFamily_SupportsGovernanceDrillIn()
    {
        var service = new InMemoryMoneyMarketFundService();
        var govId1 = Guid.NewGuid();
        var govId2 = Guid.NewGuid();
        await service.RegisterAsync(govId1, "BlackRock Liquidity TempFund", "USD", "BlackRock", true, 20, false);
        await service.RegisterAsync(govId2, "BlackRock Fed Fund", "USD", "BlackRock", true, 35, false);
        await service.RegisterAsync(Guid.NewGuid(), "Other MMF", "USD", "Other", false, 25, false);

        IMmfLiquidityService liquidityService = service;
        var members = await liquidityService.GetByFamilyAsync("blackrock");

        members.Should().HaveCount(2);
        members.Should().AllSatisfy(m => m.FundFamily.Should().Be("BLACKROCK"));
    }
}
