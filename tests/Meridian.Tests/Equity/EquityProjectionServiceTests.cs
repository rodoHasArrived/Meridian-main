using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Equity;
using Meridian.Contracts.Equity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Equity;

public sealed class EquityProjectionServiceTests
{
    [Fact]
    public async Task GetReferenceAsync_ReturnsDto_WhenRowExists()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IEquityReferenceProjectionStore>();
        var secMasterStore = Substitute.For<ISecurityMasterStore>();

        var row = new EquityProjectionRow(securityId, "Apple Inc", "USD", "Common", "OrdinaryVoting",
            "Common", "NASDAQ", "US", "Apple Inc", "AAPL", 1);
        projectionStore.GetEquityAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EquityProjectionRow?>(row));

        secMasterStore.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityProjectionRecord?>(MakeProjection(securityId, "Equity")));

        var service = new EquityProjectionService(secMasterStore, projectionStore);

        var result = await service.GetReferenceAsync(securityId);

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Apple Inc");
        result.ShareClass.Should().Be("Common");
        result.Classification.Should().Be("Common");
        result.ExchangeCode.Should().Be("NASDAQ");
    }

    [Fact]
    public async Task GetReferenceAsync_ReturnsNull_WhenAssetClassMismatch()
    {
        var securityId = Guid.NewGuid();
        var projectionStore = Substitute.For<IEquityReferenceProjectionStore>();
        var secMasterStore = Substitute.For<ISecurityMasterStore>();

        secMasterStore.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityProjectionRecord?>(MakeProjection(securityId, "Bond")));

        var service = new EquityProjectionService(secMasterStore, projectionStore);

        var result = await service.GetReferenceAsync(securityId);

        result.Should().BeNull();
        await projectionStore.DidNotReceive().GetEquityAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByExchangeAsync_ReturnsMatchingRows()
    {
        var projectionStore = Substitute.For<IEquityReferenceProjectionStore>();
        var secMasterStore = Substitute.For<ISecurityMasterStore>();
        IReadOnlyList<EquityProjectionRow> rows =
        [
            new EquityProjectionRow(Guid.NewGuid(), "AAPL", "USD", null, null, "Common", "NASDAQ", null, null, "AAPL", 1),
            new EquityProjectionRow(Guid.NewGuid(), "MSFT", "USD", null, null, "Common", "NASDAQ", null, null, "MSFT", 1)
        ];
        projectionStore.GetByExchangeAsync("NASDAQ", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows));

        var service = new EquityProjectionService(secMasterStore, projectionStore);

        var results = await service.GetByExchangeAsync("NASDAQ");

        results.Should().HaveCount(2);
        results.Select(r => r.PrimaryIdentifier).Should().Contain(["AAPL", "MSFT"]);
    }

    [Fact]
    public async Task GetByExchangeAsync_ReturnsEmpty_ForBlankInput()
    {
        var service = new EquityProjectionService(
            Substitute.For<ISecurityMasterStore>(),
            Substitute.For<IEquityReferenceProjectionStore>());

        var results = await service.GetByExchangeAsync("  ");

        results.Should().BeEmpty();
    }

    private static SecurityProjectionRecord MakeProjection(Guid securityId, string assetClass)
        => new(securityId, assetClass, SecurityStatusDto.Active, "Test", "USD",
            "Ticker", "TEST", JsonDocument.Parse("{}").RootElement,
            JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement,
            1, DateTimeOffset.UtcNow, null, [], []);
}
