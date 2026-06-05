using FluentAssertions;
using Meridian.Instruments.MoneyMarketFunds;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.MoneyMarketFunds;

public sealed class MoneyMarketFundProjectionServiceTests
{
    private readonly ISecurityMasterStore _smStore = Substitute.For<ISecurityMasterStore>();
    private readonly IMoneyMarketFundReferenceProjectionStore _projectionStore = Substitute.For<IMoneyMarketFundReferenceProjectionStore>();

    private MoneyMarketFundProjectionService CreateSut() => new(_smStore, _projectionStore);

    [Fact]
    public async Task GetReferenceAsync_ReturnsNull_WhenAssetClassMismatch()
    {
        _smStore.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeProjection(Guid.NewGuid(), "Equity"));

        var result = await CreateSut().GetReferenceAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReferenceAsync_ReturnsDto_WhenRowExists()
    {
        var id = Guid.NewGuid();
        _smStore.GetProjectionAsync(id, Arg.Any<CancellationToken>())
            .Returns(MakeProjection(id, "MoneyMarketFund"));
        _projectionStore.GetMoneyMarketFundAsync(id, Arg.Any<CancellationToken>())
            .Returns(BuildRow(id));

        var result = await CreateSut().GetReferenceAsync(id);

        result.Should().NotBeNull();
        result!.FundFamily.Should().Be("Meridian Liquidity");
        result.SweepEligible.Should().BeTrue();
    }

    [Fact]
    public async Task GetByFundFamilyAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetByFundFamilyAsync("   ");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByFundFamilyAsync_DelegatesToStore()
    {
        _projectionStore.GetByFundFamilyAsync("Meridian Liquidity", Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid()), BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetByFundFamilyAsync("Meridian Liquidity");

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBySweepEligibilityAsync_DelegatesToStore()
    {
        _projectionStore.GetBySweepEligibilityAsync(true, Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetBySweepEligibilityAsync(true);

        results.Should().ContainSingle();
    }

    private static SecurityProjectionRecord MakeProjection(Guid id, string assetClass)
    {
        var empty = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        return new(id, assetClass, SecurityStatusDto.Active, "Meridian Prime MMF", "USD",
            "Ticker", "MPRIME", empty, empty, empty, 1, DateTimeOffset.UtcNow, null, [], []);
    }

    private static MoneyMarketFundProjectionRow BuildRow(Guid id)
        => new(id, "Meridian Prime MMF", "USD", "Meridian Liquidity", true, 32, true, "MPRIME", 1);
}
