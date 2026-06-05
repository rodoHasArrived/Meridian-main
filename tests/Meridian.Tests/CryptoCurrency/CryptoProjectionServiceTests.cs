using Meridian.Instruments.CryptoCurrency;
using Meridian.Contracts.CryptoCurrency;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using FluentAssertions;

namespace Meridian.Tests.CryptoCurrency;

public sealed class CryptoProjectionServiceTests
{
    private readonly ISecurityMasterStore _smStore = Substitute.For<ISecurityMasterStore>();
    private readonly ICryptoReferenceProjectionStore _projStore = Substitute.For<ICryptoReferenceProjectionStore>();
    private CryptoProjectionService CreateSut() => new(_smStore, _projStore);

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
            .Returns(MakeProjection(id, "CryptoCurrency"));
        _projStore.GetCryptoAsync(id, Arg.Any<CancellationToken>())
            .Returns(BuildRow(id));

        var result = await CreateSut().GetReferenceAsync(id);

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be("BTC");
        result.Network.Should().Be("Bitcoin");
    }

    [Fact]
    public async Task GetByNetworkAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetByNetworkAsync("  ");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByBaseCurrencyAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetByBaseCurrencyAsync("");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByNetworkAsync_DelegatesToStore()
    {
        _projStore.GetByNetworkAsync("Ethereum", Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid()), BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetByNetworkAsync("Ethereum");

        results.Should().HaveCount(2);
    }

    private static SecurityProjectionRecord MakeProjection(Guid id, string assetClass)
    {
        var empty = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        return new(id, assetClass, SecurityStatusDto.Active, "BTC/USD", "USD",
            "Symbol", "BTCUSD", empty, empty, empty, 1, DateTimeOffset.UtcNow, null, [], []);
    }

    private static CryptoProjectionRow BuildRow(Guid id)
        => new(id, "BTC/USD", "BTC", "USD", "Bitcoin", "BTCUSD", 1);
}
