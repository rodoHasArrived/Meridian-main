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

    [Theory]
    [InlineData("Repo")]
    [InlineData("MoneyMarketFund")]
    [InlineData("CommercialPaper")]
    [InlineData("CertificateOfDeposit")]
    public async Task GetGoldenCopyPriceAsync_DoesNotInventCalculatedPrices(string assetClass)
    {
        var (sut, _, query) = BuildSut();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(BuildSecurity(assetClass));
        (await sut.GetGoldenCopyPriceAsync(SecurityId, null)).Should().BeNull();
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

        store.GetHierarchyAsOfAsync(SecurityId, null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(hierarchy);

        store.GetRawPricesAsync(SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new List<SecurityRawPriceDto>
            {
                new("refinitiv", 150.25m, DateTimeOffset.UtcNow.AddHours(-2), SecurityPriceUnit.CurrencyPerUnit),
                new("bloomberg", 150.20m, DateTimeOffset.UtcNow.AddHours(-4), SecurityPriceUnit.CurrencyPerUnit)
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

        store.GetHierarchyAsOfAsync(SecurityId, null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(hierarchy);

        store.GetRawPricesAsync(SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new List<SecurityRawPriceDto>
            {
                new("refinitiv", 99.50m, DateTimeOffset.UtcNow.AddDays(-5), SecurityPriceUnit.CurrencyPerUnit)  // 5 days old > MaxDaysStale=1
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

        store.GetHierarchyAsOfAsync(SecurityId, null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns((SecurityPricingHierarchyDto?)null);

        var result = await sut.GetGoldenCopyPriceAsync(SecurityId, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HistoricalSelection_UsesCutoffHierarchyAndLatestEligibleObservation_WithComparableUnitsOnly()
    {
        var asOf = new DateTimeOffset(2026, 6, 30, 23, 59, 0, TimeSpan.Zero);
        var (sut, store, query) = BuildSut();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(BuildSecurity("Repo"));
        store.GetHierarchyAsOfAsync(SecurityId, null, asOf, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new SecurityPricingHierarchyDto(SecurityId, null,
                [new(1, "market", "Market", 3)], asOf.AddDays(-10), "operator"));
        store.GetRawPricesAsync(SecurityId, asOf, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new SecurityRawPriceDto[] {
                new("market", 97m, asOf.AddDays(-2), SecurityPriceUnit.PercentOfPar),
                new("market", 98m, asOf.AddDays(-1), SecurityPriceUnit.PercentOfPar),
                new("market", 105m, asOf.AddDays(1), SecurityPriceUnit.PercentOfPar),
                new("comparison", 99m, asOf.AddDays(-1), SecurityPriceUnit.PercentOfPar),
                new("currency", 980m, asOf.AddDays(-1), SecurityPriceUnit.CurrencyPerUnit),
                new("legacy", 100m, asOf.AddDays(-1), SecurityPriceUnit.Unspecified)
            });
        var result = await sut.GetGoldenCopyPriceAsOfAsync(SecurityId, null, asOf);
        result!.GoldenCopyPrice.Should().Be(98m);
        result.Unit.Should().Be(SecurityPriceUnit.PercentOfPar);
        result.EvaluatedAsOf.Should().Be(asOf);
        result.HierarchyAsOf.Should().Be(asOf.AddDays(-10));
        result.SelectionReceiptId.Should().NotBeNull();
        result.HierarchySnapshot!.Entries.Single().SourceId.Should().Be("market");
        await store.Received(1).RetainPriceSelectionAsync(result, null, Arg.Any<CancellationToken>());
        result.ComparisonPrices.Single(p => p.SourceId == "comparison").PctDiffFromGoldenCopy.Should().Be(1.0204m);
        result.ComparisonPrices.Where(p => p.SourceId != "comparison").Should().OnlyContain(p => p.PctDiffFromGoldenCopy == null);
    }

    [Fact]
    public async Task MissingUnit_CannotSupportGoldenCopyOrNewObservation()
    {
        var (sut, store, query) = BuildSut();
        var asOf = DateTimeOffset.UtcNow;
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(BuildSecurity("Bond"));
        store.GetHierarchyAsOfAsync(SecurityId, null, asOf, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new SecurityPricingHierarchyDto(SecurityId, null, [new(1, "legacy", "Legacy", 3)], asOf, "operator"));
        store.GetRawPricesAsync(SecurityId, asOf, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new SecurityRawPriceDto[] { new("legacy", 100m, asOf, SecurityPriceUnit.Unspecified) });
        (await sut.GetGoldenCopyPriceAsOfAsync(SecurityId, null, asOf)).Should().BeNull();
        await sut.Invoking(s => s.RecordRawPriceAsync(new(SecurityId, "legacy", 100m, asOf, "operator")))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FutureHierarchy_CannotRewriteHistoricalSelection()
    {
        var (sut, store, query) = BuildSut();
        var asOf = DateTimeOffset.UtcNow;
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(BuildSecurity("Bond"));
        store.GetHierarchyAsOfAsync(SecurityId, null, asOf, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new SecurityPricingHierarchyDto(SecurityId, null, [new(1, "market", "Market", 3)], asOf.AddDays(1), "operator"));
        (await sut.GetGoldenCopyPriceAsOfAsync(SecurityId, null, asOf)).Should().BeNull();
    }

    [Fact]
    public async Task ReceiptReplay_UsesExactRetainedResult_AndMissingReceiptDoesNotRecalculate()
    {
        var (sut, store, query) = BuildSut();
        var receiptId = Guid.NewGuid();
        var date = DateTimeOffset.UtcNow;
        var retained = new SecurityPriceGoldenCopyDto(SecurityId, 98m, SecurityPriceKind.MarketGoldenCopy,
            "source", date, false, 0, [], SecurityPriceUnit.PercentOfPar, date, date, date,
            receiptId, new(SecurityId, "account-a", [new(1, "source", "Source", 3)], date, "operator"));
        store.GetPriceSelectionAsync(SecurityId, "account-a", receiptId, Arg.Any<CancellationToken>()).Returns(retained);
        (await sut.GetGoldenCopySelectionAsync(SecurityId, "account-a", receiptId)).Should().BeSameAs(retained);
        (await sut.GetGoldenCopySelectionAsync(SecurityId, "account-b", receiptId)).Should().BeNull();
        (await sut.GetGoldenCopySelectionAsync(SecurityId, "account-a", Guid.NewGuid())).Should().BeNull();
        await query.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().GetRawPricesAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
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
