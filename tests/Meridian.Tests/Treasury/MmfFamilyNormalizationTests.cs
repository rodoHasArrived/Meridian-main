using FluentAssertions;
using Meridian.Application.Treasury;
using Meridian.Contracts.Treasury;

namespace Meridian.Tests.Treasury;

/// <summary>
/// Unit tests for fund-family normalisation in <see cref="InMemoryMoneyMarketFundService"/>.
/// Verifies that family names are stored and matched in a case-insensitive, upper-case normalised form,
/// and that family grouping projections are built correctly.
/// </summary>
public sealed class MmfFamilyNormalizationTests
{
    [Theory]
    [InlineData("Vanguard", "VANGUARD")]
    [InlineData("  fidelity  ", "FIDELITY")]
    [InlineData("BlackRock", "BLACKROCK")]
    [InlineData("T. Rowe Price", "T. ROWE PRICE")]
    public async Task RegisteredFundFamily_IsStoredInNormalisedUpperCaseForm(
        string inputFamily, string expectedNormalized)
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "Test Fund", "USD",
            fundFamily: inputFamily, isSweepEligible: false,
            weightedAverageMaturityDays: 30, hasLiquidityFee: false);

        var detail = await service.GetByIdAsync(securityId);

        detail!.FundFamily.Should().Be(expectedNormalized);
    }

    [Fact]
    public async Task RegisteredFundFamily_NullInput_StoredAsNull()
    {
        var service = new InMemoryMoneyMarketFundService();
        var securityId = Guid.NewGuid();
        await service.RegisterAsync(securityId, "No-Family Fund", "USD",
            fundFamily: null, isSweepEligible: false,
            weightedAverageMaturityDays: 30, hasLiquidityFee: false);

        var detail = await service.GetByIdAsync(securityId);

        detail!.FundFamily.Should().BeNull();
    }

    [Fact]
    public async Task GetFundFamilyAsync_GroupsMembersUnderNormalisedFamily()
    {
        var service = new InMemoryMoneyMarketFundService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await service.RegisterAsync(id1, "Vanguard Prime", "USD", "vanguard", true, 20, false);
        await service.RegisterAsync(id2, "Vanguard Federal", "USD", "VANGUARD", true, 30, false);
        await service.RegisterAsync(otherId, "Fidelity MMMF", "USD", "fidelity", false, 45, false);

        var family = await service.GetFundFamilyAsync("vanguard");

        family.Should().NotBeNull();
        family!.NormalizedFamilyName.Should().Be("VANGUARD");
        family.MemberCount.Should().Be(2);
        family.MemberSecurityIds.Should().Contain(id1).And.Contain(id2);
        family.MemberSecurityIds.Should().NotContain(otherId);
    }

    [Fact]
    public async Task GetFundFamilyAsync_CaseInsensitiveLookup_MatchesNormalisedKey()
    {
        var service = new InMemoryMoneyMarketFundService();
        var id = Guid.NewGuid();
        await service.RegisterAsync(id, "Schwab Government MMF", "USD", "Schwab", true, 25, false);

        var familyLower = await service.GetFundFamilyAsync("schwab");
        var familyUpper = await service.GetFundFamilyAsync("SCHWAB");
        var familyMixed = await service.GetFundFamilyAsync("Schwab");

        familyLower.Should().NotBeNull();
        familyUpper.Should().NotBeNull();
        familyMixed.Should().NotBeNull();
        familyLower!.MemberCount.Should().Be(1);
        familyUpper!.MemberCount.Should().Be(1);
        familyMixed!.MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task GetFundFamilyAsync_UnknownFamily_ReturnsNull()
    {
        var service = new InMemoryMoneyMarketFundService();

        var family = await service.GetFundFamilyAsync("UnknownFamily");

        family.Should().BeNull();
    }

    [Fact]
    public async Task GetByFamilyAsync_ReturnsOnlyMembersOfThatFamily()
    {
        var service = new InMemoryMoneyMarketFundService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await service.RegisterAsync(id1, "Fidelity Prime", "USD", "fidelity", true, 20, false);
        await service.RegisterAsync(id2, "Fidelity US Gov", "USD", "Fidelity", false, 40, false);
        await service.RegisterAsync(otherId, "Other Fund", "USD", "other", false, 30, false);

        IMmfLiquidityService liquidityService = service;
        var members = await liquidityService.GetByFamilyAsync("fidelity");

        members.Should().HaveCount(2);
        members.Select(static m => m.SecurityId).Should().Contain(id1).And.Contain(id2);
        members.Select(static m => m.SecurityId).Should().NotContain(otherId);
    }

    [Fact]
    public async Task GetByFamilyAsync_NoMembersForFamily_ReturnsEmptyList()
    {
        var service = new InMemoryMoneyMarketFundService();
        await service.RegisterAsync(Guid.NewGuid(), "Other Fund", "USD", "other", false, 30, false);

        IMmfLiquidityService liquidityService = service;
        var members = await liquidityService.GetByFamilyAsync("nonexistent");

        members.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_FiltersByFundFamily_NormalisedComparison()
    {
        var service = new InMemoryMoneyMarketFundService();
        var vanguardId = Guid.NewGuid();
        var fidelityId = Guid.NewGuid();
        await service.RegisterAsync(vanguardId, "Vanguard Prime", "USD", "Vanguard", true, 20, false);
        await service.RegisterAsync(fidelityId, "Fidelity Cash", "USD", "Fidelity", false, 35, false);

        var results = await service.SearchAsync(new MmfSearchQuery(FundFamily: "vanguard"));

        results.Should().ContainSingle();
        results[0].SecurityId.Should().Be(vanguardId);
    }
}
