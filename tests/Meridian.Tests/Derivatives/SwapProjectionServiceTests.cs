using Meridian.Application.Derivatives;
using Meridian.Contracts.Derivatives;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using FluentAssertions;

namespace Meridian.Tests.Derivatives;

public sealed class SwapProjectionServiceTests
{
    private readonly ISecurityMasterStore _smStore = Substitute.For<ISecurityMasterStore>();
    private readonly ISwapReferenceProjectionStore _projStore = Substitute.For<ISwapReferenceProjectionStore>();
    private SwapProjectionService CreateSut() => new(_smStore, _projStore);

    [Fact]
    public async Task GetReferenceAsync_ReturnsNull_WhenAssetClassIsNotSwap()
    {
        _smStore.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeProjection(Guid.NewGuid(), "Equity"));

        var result = await CreateSut().GetReferenceAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReferenceAsync_ReturnsNull_WhenProjectionStoreReturnsNull()
    {
        var id = Guid.NewGuid();
        _smStore.GetProjectionAsync(id, Arg.Any<CancellationToken>())
            .Returns(MakeProjection(id, "Swap"));
        _projStore.GetSwapAsync(id, Arg.Any<CancellationToken>())
            .Returns((SwapProjectionRow?)null);

        var result = await CreateSut().GetReferenceAsync(id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReferenceAsync_ReturnsDto_WithLegsFromAssetSpecificTerms()
    {
        var id = Guid.NewGuid();
        var termsJson = System.Text.Json.JsonDocument.Parse(
            """{"legs":[{"legType":"Fixed","currency":"USD","fixedRate":0.0350},{"legType":"Float","currency":"USD","index":"SOFR"}]}""")
            .RootElement;

        _smStore.GetProjectionAsync(id, Arg.Any<CancellationToken>())
            .Returns(MakeProjection(id, "Swap", termsJson));
        _projStore.GetSwapAsync(id, Arg.Any<CancellationToken>())
            .Returns(BuildRow(id));

        var result = await CreateSut().GetReferenceAsync(id);

        result.Should().NotBeNull();
        result!.Legs.Should().HaveCount(2);
        result.Legs[0].LegType.Should().Be("Fixed");
        result.Legs[0].FixedRate.Should().BeApproximately(0.035m, 0.0001m);
        result.Legs[1].Index.Should().Be("SOFR");
    }

    [Fact]
    public async Task GetBySwapTypeAsync_ReturnsEmpty_ForBlankInput()
    {
        var results = await CreateSut().GetBySwapTypeAsync("   ");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMaturingBeforeAsync_DelegatesToStore()
    {
        var cutoff = new DateOnly(2026, 12, 31);
        _projStore.GetMaturingBeforeAsync(cutoff, Arg.Any<CancellationToken>())
            .Returns([BuildRow(Guid.NewGuid()), BuildRow(Guid.NewGuid())]);

        var results = await CreateSut().GetMaturingBeforeAsync(cutoff);

        results.Should().HaveCount(2);
    }

    private static SecurityProjectionRecord MakeProjection(Guid id, string assetClass,
        System.Text.Json.JsonElement assetSpecificTerms = default)
    {
        var emptyJson = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        return new(id, assetClass, SecurityStatusDto.Active, "IR Swap 5Y", "USD",
            "ISIN", "XS0001234567", emptyJson, assetSpecificTerms.ValueKind == System.Text.Json.JsonValueKind.Undefined ? emptyJson : assetSpecificTerms,
            emptyJson, 1, DateTimeOffset.UtcNow, null, [], []);
    }

    private static SwapProjectionRow BuildRow(Guid id)
        => new(id, "IR Swap 5Y", "USD", "InterestRate",
               new DateOnly(2024, 1, 15), new DateOnly(2029, 1, 15),
               "Active", "XS0001234567", 1);
}
