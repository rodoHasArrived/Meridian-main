using FluentAssertions;
using Meridian.Contracts.Commodities;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.Commodities;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Commodities;

public sealed class CommodityProjectionServiceTests
{
    private readonly ISecurityMasterStore _smStore = Substitute.For<ISecurityMasterStore>();
    private readonly ICommodityReferenceProjectionStore _projStore = Substitute.For<ICommodityReferenceProjectionStore>();
    private CommodityProjectionService CreateSut() => new(_smStore, _projStore);

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
            .Returns(MakeProjection(id, "Commodity"));
        _projStore.GetCommodityAsync(id, Arg.Any<CancellationToken>())
            .Returns(BuildRow(id));

        var result = await CreateSut().GetReferenceAsync(id);

        result.Should().NotBeNull();
        result!.CommodityType.Should().Be("Metal");
        result.ExchangeCode.Should().Be("COMEX");
    }

    [Fact]
    public async Task GetByCommodityTypeAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetByCommodityTypeAsync("  ");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByExchangeAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetByExchangeAsync("");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCommodityTypeAsync_DelegatesToStore()
    {
        _projStore.GetByCommodityTypeAsync("Energy", Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid()), BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetByCommodityTypeAsync("Energy");

        results.Should().HaveCount(2);
    }

    private static SecurityProjectionRecord MakeProjection(Guid id, string assetClass)
    {
        var emptyJson = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        return new(id, assetClass, SecurityStatusDto.Active, "Gold Spot", "USD",
            "ISIN", "XS0009999", emptyJson, emptyJson, emptyJson,
            1, DateTimeOffset.UtcNow, null, [], []);
    }

    private static CommodityProjectionRow BuildRow(Guid id)
        => new(id, "Gold Spot", "USD", "Metal", "USD/troy oz", 100m, "COMEX", "US", "XS0009999", 1);
}
