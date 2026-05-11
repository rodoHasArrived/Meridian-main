using FluentAssertions;
using Meridian.Application.FxSpot;
using Meridian.Contracts.FxSpot;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FxSpot;

public sealed class FxSpotProjectionServiceTests
{
    [Fact]
    public async Task GetByPairCodeAsync_ReturnsRow_WhenPairExists()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IFxSpotReferenceProjectionStore>();
        var row = new FxSpotProjectionRow(securityId, "EUR/USD", "EUR", "USD", "EURUSD", "EURUSD", 1);
        projectionStore.GetByPairCodeAsync("EURUSD", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FxSpotProjectionRow?>(row));

        var service = new FxSpotProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var result = await service.GetByPairCodeAsync("eurusd");

        result.Should().NotBeNull();
        result!.PairCode.Should().Be("EURUSD");
        result.BaseCurrency.Should().Be("EUR");
        result.QuoteCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task GetByCurrencyAsync_ReturnsBothSidesOfPair()
    {
        var projectionStore = Substitute.For<IFxSpotReferenceProjectionStore>();
        IReadOnlyList<FxSpotProjectionRow> rows =
        [
            new FxSpotProjectionRow(Guid.NewGuid(), "EUR/USD", "EUR", "USD", "EURUSD", "EURUSD", 1),
            new FxSpotProjectionRow(Guid.NewGuid(), "EUR/GBP", "EUR", "GBP", "EURGBP", "EURGBP", 1)
        ];
        projectionStore.GetByCurrencyAsync("EUR", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));

        var service = new FxSpotProjectionService(Substitute.For<ISecurityMasterStore>(), projectionStore);

        var results = await service.GetByCurrencyAsync("EUR");

        results.Should().HaveCount(2);
        results.Select(r => r.PairCode).Should().Contain(["EURUSD", "EURGBP"]);
    }

    [Fact]
    public async Task GetByPairCodeAsync_ReturnsNull_ForBlankInput()
    {
        var service = new FxSpotProjectionService(
            Substitute.For<ISecurityMasterStore>(),
            Substitute.For<IFxSpotReferenceProjectionStore>());

        var result = await service.GetByPairCodeAsync("  ");

        result.Should().BeNull();
    }
}
