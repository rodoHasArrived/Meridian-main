using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

public sealed class PortfolioCashLadderReadServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    [Fact]
    public async Task GetCashLadderAsync_WithHoldingsSource_ForecastsOnlyHeldSecuritiesScaledByQuantity()
    {
        var heldId = Guid.NewGuid();
        var unheldId = Guid.NewGuid();
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(heldId, Arg.Any<CancellationToken>())
            .Returns(BuildDetail(heldId, "Held Bond", couponAmount: 100m));
        assetOperations.GetOperationsAsync(unheldId, Arg.Any<CancellationToken>())
            .Returns(BuildDetail(unheldId, "Unheld Bond", couponAmount: 500m));
        var holdings = Substitute.For<IPortfolioHoldingsSource>();
        holdings.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new PortfolioHoldingDto(heldId, 3m) });

        var service = new PortfolioCashLadderReadService(
            securityMasterQueryService: Substitute.For<ISecurityMasterQueryService>(),
            assetOperationsQueryService: assetOperations,
            holdingsSource: holdings);

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.SecuritiesEvaluated.Should().Be(1);
        ladder.Contributions.Should().ContainSingle()
            .Which.Should().Match<PortfolioCashLadderContributionDto>(row =>
                row.DisplayName == "Held Bond" && row.Amount == 300m);
        ladder.Warnings.Should().NotContainMatch("*No holdings source is wired*");
        await assetOperations.DidNotReceive().GetOperationsAsync(unheldId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCashLadderAsync_WithoutHoldingsSource_FallsBackToActiveSecuritiesAndWarnsAboutUnitQuantity()
    {
        var securityId = Guid.NewGuid();
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SecuritySummaryDto(securityId, "Bond", SecurityStatusDto.Active, "Active Bond", "CUSIP:1", "USD", 1)
            });
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildDetail(securityId, "Active Bond", couponAmount: 100m));

        var service = new PortfolioCashLadderReadService(
            securityMasterQueryService: securityMaster,
            assetOperationsQueryService: assetOperations);

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.Contributions.Should().ContainSingle()
            .Which.Amount.Should().Be(100m, "the fallback uses unit quantity");
        ladder.Warnings.Should().ContainMatch("*No holdings source is wired*");
    }

    [Fact]
    public async Task GetCashLadderAsync_WhenHoldingsExceedCap_WarnsAboutOmittedSecurities()
    {
        const int cap = 500;
        var holdings = Enumerable.Range(0, cap + 25)
            .Select(_ => new PortfolioHoldingDto(Guid.NewGuid(), 1m))
            .ToArray();
        var holdingsSource = Substitute.For<IPortfolioHoldingsSource>();
        holdingsSource.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(holdings);
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((AssetOperationsDetailDto?)null);

        var service = new PortfolioCashLadderReadService(
            assetOperationsQueryService: assetOperations,
            holdingsSource: holdingsSource);

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.Warnings.Should().ContainMatch($"*first {cap} of {cap + 25} held securities*")
            .And.ContainMatch("*25 were omitted*");
    }

    private static AssetOperationsDetailDto BuildDetail(Guid securityId, string displayName, decimal couponAmount)
    {
        var runId = Guid.NewGuid();
        var subject = new AssetOperationSubjectDto(
            securityId,
            "Bond",
            displayName,
            $"CUSIP:{securityId:N}",
            ["Identity", "TermsHistory", "ProjectedCashFlows"]);
        var run = new AssetCashFlowProjectionRunDto(
            runId,
            securityId,
            Today,
            "asset-obligation-projection-v1",
            "Completed",
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            securityId.ToString("D"));
        var flow = new AssetProjectedCashFlowDto(
            Guid.NewGuid(),
            runId,
            securityId,
            1,
            "Coupon",
            Today.AddDays(10),
            couponAmount,
            "USD",
            "Projected",
            SourceDomain: "SecurityMaster",
            SourceEntityId: securityId.ToString("D"));
        var readiness = new AssetOperationsReadinessDto(
            securityId,
            "Ready",
            subject.OperationalProfile,
            subject.OperationalProfile,
            [],
            [],
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            securityId.ToString("D"));

        return new AssetOperationsDetailDto(
            subject,
            [],
            [],
            [run],
            [flow],
            [],
            [],
            [],
            [],
            readiness,
            []);
    }
}
