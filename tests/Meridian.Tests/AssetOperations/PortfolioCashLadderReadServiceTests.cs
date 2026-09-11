using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
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

    [Theory]
    [InlineData("Completed", -1, "")]
    [InlineData("Completed", -1, "EUR")]
    [InlineData("Failed", 1, "")]
    [InlineData("Failed", 1, "EUR")]
    public async Task GetCashLadderAsync_IgnoresCurrencyFromUnselectedProjectionRuns(
        string unusedStatus, int generatedDayOffset, string unusedCurrency)
    {
        var detail = BuildDetail(Guid.NewGuid(), "Current USD bond", couponAmount: 100m);
        var currentRun = detail.CashFlowProjectionRuns.Single();
        var unusedRun = currentRun with
        {
            ProjectionRunId = Guid.NewGuid(),
            Status = unusedStatus,
            GeneratedAt = currentRun.GeneratedAt.AddDays(generatedDayOffset)
        };
        var unusedFlow = detail.ProjectedCashFlows.Single() with
        {
            ProjectionRunId = unusedRun.ProjectionRunId,
            Currency = unusedCurrency
        };
        var service = BuildService(detail with
        {
            CashFlowProjectionRuns = [currentRun, unusedRun],
            ProjectedCashFlows = [.. detail.ProjectedCashFlows, unusedFlow]
        });

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeTrue();
        ladder.BlockingReasons.Should().BeEmpty();
        ladder.Contributions.Should().ContainSingle()
            .Which.ProjectionRunId.Should().Be(currentRun.ProjectionRunId);
        ladder.Contributions.Single().Amount.Should().Be(100m);
    }

    [Theory]
    [InlineData("flow", -1, "")]
    [InlineData("flow", 30, "")]
    [InlineData("flow", 60, "EUR")]
    [InlineData("capital", -1, "")]
    [InlineData("capital", 30, "")]
    [InlineData("capital", 60, "EUR")]
    public async Task GetCashLadderAsync_IgnoresCurrencyFromAmountsOutsideTheWindow(
        string unusedSource, int dueDayOffset, string unusedCurrency)
    {
        var detail = BuildDetail(Guid.NewGuid(), "In-window USD bond", couponAmount: 100m);
        var unusedFlow = detail.ProjectedCashFlows.Single() with
        {
            DueDate = Today.AddDays(dueDayOffset),
            Currency = unusedCurrency
        };
        var capital = new PortfolioCapitalActivityDto(Guid.NewGuid(), "Redemption",
            Today.AddDays(dueDayOffset), 1_000m, unusedCurrency, "Capital", "excluded", "Out-of-window redemption");
        var service = BuildService(
            unusedSource == "flow" ? detail with { ProjectedCashFlows = [.. detail.ProjectedCashFlows, unusedFlow] } : detail,
            unusedSource == "capital" ? [capital] : []);

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeTrue();
        ladder.BlockingReasons.Should().BeEmpty();
        ladder.Contributions.Should().ContainSingle().Which.Amount.Should().Be(100m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EUR")]
    public async Task GetCashLadderAsync_UnsupportedCapitalCurrencyDoesNotBlockRecognizedContributions(
        string unusedCurrency)
    {
        var detail = BuildDetail(Guid.NewGuid(), "USD bond", couponAmount: 100m);
        var service = BuildService(detail, [
            new PortfolioCapitalActivityDto(Guid.NewGuid(), "UnknownProviderKind", Today.AddDays(2),
                1_000m, unusedCurrency, "Capital", "unsupported", "Unsupported provider row"),
            new PortfolioCapitalActivityDto(Guid.NewGuid(), "Redemption", Today.AddDays(2),
                1_000m, "USD", "Capital", "supported", "Scheduled redemption")]);

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeTrue();
        ladder.BlockingReasons.Should().BeEmpty();
        ladder.Contributions.Should().HaveCount(2);
        ladder.Contributions.Should().NotContain(row => row.SourceEntityId == "unsupported");
        ladder.Contributions.Should().Contain(row => row.SourceEntityId == "supported" && row.Amount == -1_000m);
        ladder.Warnings.Should().ContainMatch("*UnknownProviderKind*excluded from the ladder*");
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("flow")]
    [InlineData("capital")]
    public async Task GetCashLadderAsync_MissingCurrencyBlocksBeforeAmountArithmetic(string missingSource)
    {
        var detail = BuildDetail(Guid.NewGuid(), "Large amount bond", couponAmount: decimal.MaxValue);
        if (missingSource == "flow")
        {
            detail = detail with
            {
                ProjectedCashFlows = detail.ProjectedCashFlows.Select(flow => flow with { Currency = "" }).ToArray()
            };
        }

        var cash = BuildCashBalanceProvider();
        if (missingSource == "cash")
        {
            cash.GetCashBalancesAsync(Arg.Any<CancellationToken>()).Returns([
                new PortfolioCashBalanceDto("cash-1", "Known cash", decimal.MaxValue, "USD", "Ledger", "cash-1"),
                new PortfolioCashBalanceDto("cash-2", "Unidentified cash", decimal.MaxValue, "", "Ledger", "cash-2")]);
        }

        var service = BuildService(detail, [
            new PortfolioCapitalActivityDto(Guid.NewGuid(), "Redemption", Today.AddDays(2),
                decimal.MinValue, missingSource == "capital" ? "" : "USD", "Capital", "redemption", "Large redemption")],
            quantity: 2m, cash: cash);

        var ladder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));

        ladder.IsDecisionReady.Should().BeFalse();
        ladder.Buckets.Should().BeEmpty();
        ladder.Contributions.Should().BeEmpty();
        ladder.BlockingReasons.Should().ContainMatch("*missing currency evidence*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("EUR")]
    public async Task GetCashLadderAsync_EarlyCallValidatesFuturePrincipalBeforePullingItIntoTheWindow(
        string principalCurrency)
    {
        var detail = BuildDetail(Guid.NewGuid(), "Callable USD bond", couponAmount: 100m);
        var terms = new AssetTermsVersionDto(Guid.NewGuid(), detail.Subject.SecurityId, 1,
            "callable-terms", Today.AddYears(-1), detail.CashFlowProjectionRuns.Single().GeneratedAt.AddDays(-1),
            "SecurityMaster", detail.Subject.SecurityId.ToString("D"), "Retained call terms",
            JsonSerializer.SerializeToElement(new { callDate = Today.AddDays(15).ToString("yyyy-MM-dd") }));
        var principal = detail.ProjectedCashFlows.Single() with
        {
            FlowType = "Principal",
            DueDate = Today.AddDays(60),
            Amount = decimal.MaxValue,
            Currency = principalCurrency
        };
        var service = BuildService(detail with
        {
            TermsHistory = [terms],
            ProjectedCashFlows = [.. detail.ProjectedCashFlows, principal]
        }, quantity: 2m);

        var baseLadder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(HorizonDays: 30));
        var earlyCallLadder = await service.GetCashLadderAsync(new PortfolioCashLadderQuery(
            HorizonDays: 30, ScenarioId: PortfolioCashLadderEngine.EarlyCallScenarioId));

        baseLadder.IsDecisionReady.Should().BeTrue();
        baseLadder.Contributions.Should().ContainSingle().Which.Amount.Should().Be(200m);
        earlyCallLadder.IsDecisionReady.Should().BeFalse();
        earlyCallLadder.Buckets.Should().BeEmpty();
        earlyCallLadder.Contributions.Should().BeEmpty();
        earlyCallLadder.BlockingReasons.Should().ContainMatch(principalCurrency.Length == 0
            ? "*missing currency evidence*" : "*no authoritative FX conversion source*");
    }

    private static PortfolioCashLadderReadService BuildService(
        AssetOperationsDetailDto detail,
        IReadOnlyList<PortfolioCapitalActivityDto>? capitalRows = null,
        decimal quantity = 1m,
        IPortfolioCashBalanceProvider? cash = null)
    {
        var assetOperations = Substitute.For<IAssetOperationsQueryService>();
        assetOperations.GetOperationsAsync(detail.Subject.SecurityId, Arg.Any<CancellationToken>()).Returns(detail);
        var holdings = Substitute.For<IPortfolioHoldingsSource>();
        holdings.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new PortfolioHoldingDto(detail.Subject.SecurityId, quantity)]);
        var capital = Substitute.For<IPortfolioCapitalScheduleProvider>();
        capital.GetCapitalActivityAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(capitalRows ?? []);
        return new PortfolioCashLadderReadService(assetOperationsQueryService: assetOperations,
            holdingsSource: holdings, cashBalanceProvider: cash ?? BuildCashBalanceProvider(), capitalScheduleProvider: capital);
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
