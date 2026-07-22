using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

// ISecurityMasterQueryService is declared in both Meridian.Application.SecurityMaster and
// Meridian.Contracts.SecurityMaster; the pricing service binds to the Application one.
using ISecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterPricingServiceTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();

    private static (SecurityMasterPricingService Service, ISecurityMasterPricingStore Store, ISecurityMasterQueryService QueryService)
        BuildSut()
    {
        var store = Substitute.For<ISecurityMasterPricingStore>();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = new SecurityMasterPricingService(
            store, queryService, NullLogger<SecurityMasterPricingService>.Instance);
        return (service, store, queryService);
    }

    [Fact]
    public async Task GetGoldenCopyPriceAsync_ReturnsNull_WhenSecurityNotFound()
    {
        var (sut, _, queryService) = BuildSut();
        queryService.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns((SecurityDetailDto?)null);

        var result = await sut.GetGoldenCopyPriceAsync(SecurityId, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGoldenCopyPriceAsync_ReturnsCalculatedPar_ForRepoAssetClass()
    {
        var (sut, _, queryService) = BuildSut();
        queryService.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity("Repo"));

        var result = await sut.GetGoldenCopyPriceAsync(SecurityId, null);

        result.Should().NotBeNull();
        result!.PriceKind.Should().Be(SecurityPriceKind.CalculatedPar);
        result.GoldenCopyPrice.Should().Be(100m);
        result.SelectedSource.Should().Be("calculated");
    }

    [Fact]
    public async Task GetGoldenCopyPriceAsync_ReturnsCalculatedPar_ForMoneyMarketFund()
    {
        var (sut, _, queryService) = BuildSut();
        queryService.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity("MoneyMarketFund"));

        var result = await sut.GetGoldenCopyPriceAsync(SecurityId, null);

        result!.GoldenCopyPrice.Should().Be(1m);
        result.PriceKind.Should().Be(SecurityPriceKind.CalculatedPar);
    }

    [Fact]
    public async Task GetGoldenCopyPriceAsync_SelectsHighestPriorityFreshSource()
    {
        var (sut, store, queryService) = BuildSut();
        queryService.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity("Equity"));

        var hierarchy = new SecurityPricingHierarchyDto(
            SecurityId, null,
            [
                new PricingHierarchyEntryDto(1, "refinitiv", "LSEG/Refinitiv", MaxDaysStale: 3),
                new PricingHierarchyEntryDto(2, "bloomberg", "Bloomberg BVAL", MaxDaysStale: 3)
            ],
            DateTimeOffset.UtcNow, "operator");

        store.GetHierarchyAsync(SecurityId, null, Arg.Any<CancellationToken>())
            .Returns(hierarchy);

        store.GetRawPricesAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new List<(string, decimal, DateTimeOffset)>
            {
                ("refinitiv", 150.25m, DateTimeOffset.UtcNow.AddHours(-2)),
                ("bloomberg", 150.20m, DateTimeOffset.UtcNow.AddHours(-4))
            });

        var result = await sut.GetGoldenCopyPriceAsync(SecurityId, null);

        result.Should().NotBeNull();
        result!.SelectedSource.Should().Be("refinitiv");
        result.GoldenCopyPrice.Should().Be(150.25m);
        result.IsStaleFallback.Should().BeFalse();
        result.ComparisonPrices.Should().HaveCount(1);
        result.ComparisonPrices[0].SourceId.Should().Be("bloomberg");
    }

    [Fact]
    public async Task GetGoldenCopyPriceAsync_FallsBackToStaleSource_WhenAllExceedMaxDaysStale()
    {
        var (sut, store, queryService) = BuildSut();
        queryService.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity("Equity"));

        var hierarchy = new SecurityPricingHierarchyDto(
            SecurityId, null,
            [new PricingHierarchyEntryDto(1, "refinitiv", "LSEG/Refinitiv", MaxDaysStale: 1)],
            DateTimeOffset.UtcNow, "operator");

        store.GetHierarchyAsync(SecurityId, null, Arg.Any<CancellationToken>())
            .Returns(hierarchy);

        store.GetRawPricesAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new List<(string, decimal, DateTimeOffset)>
            {
                ("refinitiv", 99.50m, DateTimeOffset.UtcNow.AddDays(-5))  // 5 days old > MaxDaysStale=1
            });

        var result = await sut.GetGoldenCopyPriceAsync(SecurityId, null);

        result.Should().NotBeNull();
        result!.IsStaleFallback.Should().BeTrue();
        result.DaysStale.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task GetGoldenCopyPriceAsync_ReturnsNull_WhenNoHierarchyConfigured()
    {
        var (sut, store, queryService) = BuildSut();
        queryService.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity("Bond"));

        store.GetHierarchyAsync(SecurityId, null, Arg.Any<CancellationToken>())
            .Returns((SecurityPricingHierarchyDto?)null);

        var result = await sut.GetGoldenCopyPriceAsync(SecurityId, null);

        result.Should().BeNull();
    }

    private static SecurityDetailDto BuildSecurity(string assetClass)
        => new(
            SecurityId, assetClass, SecurityStatusDto.Active, $"Test {assetClass}", "USD",
            System.Text.Json.JsonSerializer.SerializeToElement(new { currency = "USD" }),
            System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>(),
            Version: 1,
            DateTimeOffset.UtcNow.AddDays(-30),
            null);
}
