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
            holdingsSource: holdings,
            cashBalanceProvider: BuildCashBalanceProvider());

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.SecuritiesEvaluated.Should().Be(1);
        ladder.IsDecisionReady.Should().BeTrue();
        ladder.Contributions.Should().ContainSingle()
            .Which.Should().Match<PortfolioCashLadderContributionDto>(row =>
                row.DisplayName == "Held Bond" && row.Amount == 300m);
        ladder.Warnings.Should().NotContainMatch("*No holdings source is wired*");
        await assetOperations.DidNotReceive().GetOperationsAsync(unheldId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCashLadderAsync_WithoutHoldingsSource_BlocksInsteadOfFabricatingUnitQuantity()
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
            assetOperationsQueryService: assetOperations,
            cashBalanceProvider: BuildCashBalanceProvider());

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeFalse();
        ladder.Buckets.Should().BeEmpty("a blocked ladder must not emit liquidity-breach flags");
        ladder.Contributions.Should().BeEmpty();
        ladder.BlockingReasons.Should().ContainMatch("*No authoritative holdings source*");
        await assetOperations.DidNotReceiveWithAnyArgs().GetOperationsAsync(default, default);
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
            holdingsSource: holdingsSource,
            cashBalanceProvider: BuildCashBalanceProvider());

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.Warnings.Should().ContainMatch($"*first {cap} of {cap + 25} held securities*")
            .And.ContainMatch("*25 were omitted*");
    }

    [Fact]
    public async Task GetCashLadderAsync_WithoutHoldingsSource_DoesNotEnumerateSecurityMasterSubjects()
    {
        const int cap = 500;
        var summaries = Enumerable.Range(0, cap + 1)
            .Select(i => new SecuritySummaryDto(Guid.NewGuid(), "Bond", SecurityStatusDto.Active, $"Bond {i}", $"CUSIP:{i}", "USD", 1))
            .ToArray();
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(summaries);
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => BuildDetail(callInfo.ArgAt<Guid>(0), "Active Bond", couponAmount: 100m));

        var service = new PortfolioCashLadderReadService(
            securityMasterQueryService: securityMaster,
            assetOperationsQueryService: assetOperations,
            cashBalanceProvider: BuildCashBalanceProvider());

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeFalse();
        ladder.SecuritiesEvaluated.Should().Be(0);
        await securityMaster.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }

    [Fact]
    public async Task GetCashLadderAsync_WhenHeldSecurityHasNoProjection_WarnsInsteadOfSilentlyDropping()
    {
        var projectableId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(projectableId, Arg.Any<CancellationToken>())
            .Returns(BuildDetail(projectableId, "Projectable Bond", couponAmount: 100m));
        assetOperations.GetOperationsAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((AssetOperationsDetailDto?)null);
        var holdingsSource = Substitute.For<IPortfolioHoldingsSource>();
        holdingsSource.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new PortfolioHoldingDto(projectableId, 1m), new PortfolioHoldingDto(missingId, 1m) });

        var service = new PortfolioCashLadderReadService(
            assetOperationsQueryService: assetOperations,
            holdingsSource: holdingsSource,
            cashBalanceProvider: BuildCashBalanceProvider());

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.Warnings.Should().ContainMatch("*1 of 2 held securities have no asset-operations projection*");
        ladder.IsDecisionReady.Should().BeFalse();
        ladder.Buckets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCashLadderAsync_WhenFundAccountScoped_WarnsScopeIsNotApplied()
    {
        var securityId = Guid.NewGuid();
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildDetail(securityId, "Held Bond", couponAmount: 100m));
        var holdingsSource = Substitute.For<IPortfolioHoldingsSource>();
        holdingsSource.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new PortfolioHoldingDto(securityId, 1m) });

        var service = new PortfolioCashLadderReadService(
            assetOperationsQueryService: assetOperations,
            holdingsSource: holdingsSource,
            cashBalanceProvider: BuildCashBalanceProvider());

        var ladder = await service.GetCashLadderAsync(
            new PortfolioCashLadderQuery(HorizonDays: 30, FundAccountId: "fund-123"));

        ladder.Warnings.Should().ContainMatch("*Fund-account scope 'fund-123' is not yet applied*");
        ladder.IsDecisionReady.Should().BeFalse();
        ladder.Buckets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCashLadderAsync_WhenForeignCurrencyHasNoFxSource_BlocksWithoutBreachFlags()
    {
        var securityId = Guid.NewGuid();
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        var detail = BuildDetail(securityId, "EUR Bond", couponAmount: 100m);
        assetOperations.GetOperationsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(detail with
            {
                ProjectedCashFlows = detail.ProjectedCashFlows
                    .Select(flow => flow with { Currency = "EUR" })
                    .ToArray()
            });
        var holdings = Substitute.For<IPortfolioHoldingsSource>();
        holdings.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new PortfolioHoldingDto(securityId, 1m)]);

        var service = new PortfolioCashLadderReadService(
            assetOperationsQueryService: assetOperations,
            holdingsSource: holdings,
            cashBalanceProvider: BuildCashBalanceProvider());

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeFalse();
        ladder.Buckets.Should().BeEmpty();
        ladder.BlockingReasons.Should().ContainMatch("*no authoritative FX conversion source*");
    }

    [Theory]
    [InlineData("cash", "")]
    [InlineData("cash", " ")]
    [InlineData("flow", "")]
    [InlineData("flow", " ")]
    [InlineData("capital", "")]
    [InlineData("capital", " ")]
    public async Task GetCashLadderAsync_WhenAnyAmountLacksCurrency_BlocksDespiteOtherUsdEvidence(
        string missingSource, string missingCurrency)
    {
        var securityId = Guid.NewGuid();
        var detail = BuildDetail(securityId, "Held bond", couponAmount: 100m);
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(detail with
            {
                ProjectedCashFlows = detail.ProjectedCashFlows
                    .Select(flow => flow with { Currency = missingSource == "flow" ? missingCurrency : "USD" })
                    .ToArray()
            });
        var holdings = Substitute.For<IPortfolioHoldingsSource>();
        holdings.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new PortfolioHoldingDto(securityId, 1m)]);
        var cash = BuildCashBalanceProvider();
        cash.GetCashBalancesAsync(Arg.Any<CancellationToken>()).Returns([
            new PortfolioCashBalanceDto("cash-1", "Known cash", 100m, "USD", "Ledger", "cash-1"),
            new PortfolioCashBalanceDto("cash-2", "Additional cash", 100m,
                missingSource == "cash" ? missingCurrency : "USD", "Ledger", "cash-2")]);
        var capital = Substitute.For<IPortfolioCapitalScheduleProvider>();
        capital.GetCapitalActivityAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new PortfolioCapitalActivityDto(Guid.NewGuid(), "Redemption", Today.AddDays(2),
                1000m, missingSource == "capital" ? missingCurrency : "USD", "Capital", "redemption-1", "Scheduled outflow")]);
        var service = new PortfolioCashLadderReadService(assetOperationsQueryService: assetOperations,
            holdingsSource: holdings, cashBalanceProvider: cash, capitalScheduleProvider: capital);

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeFalse();
        ladder.Buckets.Should().BeEmpty("unidentified currency cannot support cash or breach decisions");
        ladder.BlockingReasons.Should().ContainMatch("*missing currency evidence*");
    }

    private static IPortfolioCashBalanceProvider BuildCashBalanceProvider()
    {
        var provider = Substitute.For<IPortfolioCashBalanceProvider>();
        provider.GetCashBalancesAsync(Arg.Any<CancellationToken>())
            .Returns([new PortfolioCashBalanceDto("cash-1", "Operating cash", 1_000_000m, "USD", "Ledger", "cash-1")]);
        return provider;
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
