using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ISecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Unit tests for <see cref="SecurityMasterCostBasisAdjustmentService"/> — the enricher that reads
/// Security Master corporate actions, factor, and day-count terms and projects them into ledger
/// tax-lot basis adjustments.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterCostBasisAdjustmentServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private const string Ticker = "ACME";

    private static SecurityMasterCostBasisAdjustmentService BuildService(ISecurityMasterQueryService queryService)
        => new(queryService, NullLogger<SecurityMasterCostBasisAdjustmentService>.Instance);

    private static ISecurityMasterQueryService QueryServiceWith(
        IReadOnlyList<CorporateActionDto>? actions = null,
        SecurityDetailDto? security = null)
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(actions ?? []);
        queryService.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(security);
        return queryService;
    }

    private static CorporateActionDto Action(
        string eventType,
        DateOnly exDate,
        decimal? splitRatio = null,
        decimal? dividendPerShare = null,
        string? lifecycleState = null)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: eventType,
            ExDate: exDate,
            PayDate: exDate.AddDays(10),
            DividendPerShare: dividendPerShare,
            Currency: dividendPerShare is null ? null : "USD",
            SplitRatio: splitRatio,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: exDate.AddDays(1),
            LifecycleState: lifecycleState);

    private static SecurityDetailDto Security(string assetClass, object assetSpecificTerms)
        => new(
            SecurityId,
            assetClass,
            SecurityStatusDto.Active,
            "ACME Corp",
            "USD",
            JsonSerializer.SerializeToElement(new { displayName = "ACME Corp", currency = "USD" }),
            JsonSerializer.SerializeToElement(assetSpecificTerms),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, Ticker, true, DateTimeOffset.UtcNow)],
            [],
            1,
            DateTimeOffset.UtcNow,
            null);

    private static IReadOnlyList<LedgerTaxLot> Lots(params LedgerTaxLot[] lots) => lots;

    [Fact]
    public async Task BuildAdjustmentsAsync_ForwardSplit_ProducesSplitAdjustment()
    {
        var queryService = QueryServiceWith(actions: [Action("StockSplit", new DateOnly(2024, 6, 1), splitRatio: 2m)]);
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 10m, 90m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().ContainSingle();
        adjustments[0].Kind.Should().Be(LedgerTaxLotBasisAdjustmentKind.Split);
        adjustments[0].Value.Should().Be(2m);
        adjustments[0].EffectiveDate.Should().Be(new DateOnly(2024, 6, 1));
        adjustments[0].SecurityId.Should().Be(SecurityId);
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_ReturnOfCapital_ProducesBasisReduction()
    {
        var queryService = QueryServiceWith(actions: [Action("ReturnOfCapital", new DateOnly(2024, 3, 1), dividendPerShare: 1.5m)]);
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 10m, 50m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().ContainSingle();
        adjustments[0].Kind.Should().Be(LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital);
        adjustments[0].Value.Should().Be(1.5m);
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_CancelledCorporateAction_IsExcluded()
    {
        var queryService = QueryServiceWith(actions:
        [
            Action("StockSplit", new DateOnly(2024, 6, 1), splitRatio: 2m, lifecycleState: CorporateActionLifecycleStates.Cancelled),
        ]);
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 10m, 90m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_CorporateActionAfterAsOf_IsExcluded()
    {
        var queryService = QueryServiceWith(actions: [Action("StockSplit", new DateOnly(2026, 6, 1), splitRatio: 2m)]);
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 10m, 90m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_CurrentFactorBelowPrior_ProducesFactorPaydown()
    {
        var queryService = QueryServiceWith(security: Security("MortgageBacked", new { currentFactor = 0.75m }));
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 100m, 100m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().ContainSingle();
        adjustments[0].Kind.Should().Be(LedgerTaxLotBasisAdjustmentKind.Factor);
        adjustments[0].Value.Should().Be(0.75m);
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_FactorAtOne_ProducesNoFactorAdjustment()
    {
        var queryService = QueryServiceWith(security: Security("MortgageBacked", new { currentFactor = 1m }));
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 100m, 100m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_FactorSchedule_TakesPriorityOverScalarCurrentFactor()
    {
        // The dated schedule row in effect on the as-of date (0.70 from 2024-12-01) must drive the
        // paydown — not the stale scalar currentFactor (0.95) and not the 2025-06-01 row that is
        // still in the future — matching how the cash-flow projector seeds its outstanding factor.
        var queryService = QueryServiceWith(security: Security("MortgageBacked", new
        {
            currentFactor = 0.95m,
            factorSchedule = new[]
            {
                new { asOfDate = "2024-12-01", factor = 0.70m },
                new { asOfDate = "2025-06-01", factor = 0.60m },
            },
        }));
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 100m, 100m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().ContainSingle();
        adjustments[0].Kind.Should().Be(LedgerTaxLotBasisAdjustmentKind.Factor);
        adjustments[0].Value.Should().Be(0.70m);
        adjustments[0].Reference.Should().Be("factor-schedule");
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_VendorAliasAssetClass_StillAmortizes()
    {
        // "MBS" is a vendor alias for StructuredCredit in the shared asset-class catalog, so
        // amortization must not depend on the canonical spelling reaching this service.
        var queryService = QueryServiceWith(security: Security("MBS", new
        {
            maturityDate = "2030-01-01",
            dayCountConvention = "30/360",
        }));
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId,
            Lots(new LedgerTaxLot("lot-a", new DateOnly(2025, 1, 1), 10m, 110m, SecurityId)),
            new DateOnly(2027, 1, 1));

        adjustments.Should().ContainSingle(adjustment =>
            adjustment.Kind == LedgerTaxLotBasisAdjustmentKind.Amortization);
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_BondPremium_ProducesStraightLineAmortization()
    {
        var queryService = QueryServiceWith(security: Security("Bond", new
        {
            maturityDate = "2030-01-01",
            dayCountConvention = "30/360",
            par = 100m,
        }));
        var service = BuildService(queryService);

        // Lot bought at 110 (10-point premium), 5 of 10 years elapsed -> half the premium amortized.
        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2020, 1, 1), 100m, 110m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().ContainSingle();
        adjustments[0].Kind.Should().Be(LedgerTaxLotBasisAdjustmentKind.Amortization);
        adjustments[0].Value.Should().Be(-5m); // premium written down toward par
        adjustments[0].LotId.Should().Be("lot-a");
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_NonFixedIncome_ProducesNoAmortization()
    {
        var queryService = QueryServiceWith(security: Security("Equity", new { maturityDate = "2030-01-01", dayCountConvention = "30/360" }));
        var service = BuildService(queryService);

        var adjustments = await service.BuildAdjustmentsAsync(
            SecurityId, Lots(new LedgerTaxLot("lot-a", new DateOnly(2020, 1, 1), 100m, 110m, SecurityId)), new DateOnly(2025, 1, 1));

        adjustments.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectReliefAsync_AppliesSplitToReduceCostBasis()
    {
        var queryService = QueryServiceWith(actions: [Action("StockSplit", new DateOnly(2024, 6, 1), splitRatio: 2m)]);
        var service = BuildService(queryService);

        var account = LedgerAccounts.Securities(Ticker, "broker-1");
        var baseInput = new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2025, 1, 1),
            quantitySold: 12m,
            salePrice: 50m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 10m, 90m, SecurityId)]);

        var projection = await service.ProjectReliefAsync(baseInput, SecurityId);

        projection.IsBalanced.Should().BeTrue();
        projection.AppliedAdjustments.Should().ContainSingle();
        projection.EffectiveLots[0].Quantity.Should().Be(20m);
        projection.CostBasis.Should().Be(540m); // 12 @ 45 post-split
        projection.RealizedGainOrLoss.Should().Be(60m);
    }

    [Fact]
    public async Task ProjectReliefAsync_WithNoReferenceData_MatchesUnadjustedProjection()
    {
        var queryService = QueryServiceWith();
        var service = BuildService(queryService);

        var account = LedgerAccounts.Securities(Ticker, "broker-1");
        var baseInput = new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2025, 1, 1),
            quantitySold: 5m,
            salePrice: 120m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2024, 1, 1), 10m, 100m, SecurityId)]);

        var projection = await service.ProjectReliefAsync(baseInput, SecurityId);

        projection.AppliedAdjustments.Should().BeEmpty();
        projection.CostBasis.Should().Be(500m);
        projection.RealizedGainOrLoss.Should().Be(100m);
    }
}
