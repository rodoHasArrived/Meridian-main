using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondWorkflowServiceTests
{
    private static readonly Guid Id = Guid.Parse("38cf377c-cf7d-448e-920c-14d18a48bbe1");
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RetainedNegativeCouponsProduceCashAndUniqueYieldWithoutPretendingCurveCoverage()
    {
        var service = Service(Bond(-2m));
        var result = (await service.CalculateAsync(new(Id, Context(), 1000m, 100m)))!;
        result.IsBlocked.Should().BeFalse();
        result.CashFlows.Sum(f => f.Interest).Should().Be(-40m);
        result.CashFlows.Sum(f => f.Principal).Should().Be(1000m);
        result.Yield.Should().BeApproximately(-0.02m, 0.000001m);
        result.PresentValue.Should().BeNull();
        result.TotalCash.Should().Be(1000m);
        result.Metrics.Should().Contain(m => m.Name == "Projection" && m.Available);
        result.Metrics.Should().Contain(m => m.Name == "Valuation" && !m.Available);
        result.Inputs.Should().Contain(i => i.Kind == "SecurityTerms" && i.Version == 9);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public async Task DeclaredCompanionCannotSilentlyLoseItsEconomicsWhileOlderResidueRemainsInert(int stamp, bool blocked)
    {
        var security = Bond();
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(security.AssetSpecificTerms.GetRawText())!;
        terms["assetClassTermsVersion"] = stamp;
        terms["cashFlowContract"] = new { version = 1, currency = "USD", streams = new[] { new { principal = 500m } } };
        security = security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        var result = (await Service(security).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        result.IsBlocked.Should().Be(blocked);
        if (blocked)
            result.Issues.Should().Contain(issue => issue.Code == "ContractCompanionUnsupported");
    }

    [Fact]
    public async Task CurveValuationScalesActualFaceAndRebasesSettlementDiscountFactors()
    {
        var store = Curves();
        var service = Service(Bond(), store);
        var quote = (await service.CalculateAsync(new(Id, Context(), 1000m, 100m)))!;
        var shiftedContext = Context() with { SettlementDate = new DateOnly(2026, 7, 1) };
        var shifted = (await service.CalculateAsync(new(Id, shiftedContext, 1000m, 100m, new DateOnly(2026, 1, 1))))!;
        shifted.IsBlocked.Should().BeFalse();
        shifted.CalibratedSpread.Should().BeApproximately(quote.CalibratedSpread!.Value, 0.00000001m);
        shifted.DirtyConsideration.Should().BeGreaterThan(quote.DirtyConsideration!.Value);
        shifted.AccruedInterest.Should().Be(25m);
        shifted.Dv01.Should().NotBeNull();
        shifted.Inputs.Should().Contain(i => i.Kind == "Curve:Discount" && i.VersionId != null);
    }

    [Fact]
    public async Task MissingDeclaredRateUnitBlocksRatherThanInterpretingSmallCouponByMagnitude()
    {
        var security = Bond();
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(security.AssetSpecificTerms.GetRawText())!;
        map["scheduleConventions"] = new { accrualAdjusted = false, couponCashBasis = "Level" };
        security = security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(map) };
        var result = (await Service(security).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        result.IsBlocked.Should().BeTrue();
        result.CashFlows.Should().BeEmpty();
        result.Issues.Should().Contain(i => i.Code == "RateUnitRequired");
    }

    [Fact]
    public async Task SameDateSettlementHasNoPurchasedCouponAfterItHasPaid()
    {
        var result = (await Service(Bond()).CalculateAsync(new(Id, Context() with
        { SettlementDate = new DateOnly(2027, 1, 1) }, CleanPricePer100: 100m)))!;
        result.IsBlocked.Should().BeFalse();
        result.AccruedInterest.Should().Be(0m);
        result.CashFlows.Should().OnlyContain(f => f.PaymentDate > new DateOnly(2027, 1, 1));
    }

    [Fact]
    public async Task DatedScalarFactorRollsThroughLaterContractualPrincipalExactlyOnce()
    {
        var bond = Bond();
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        payload["currentFactor"] = 1m;
        payload["factorDate"] = "2026-01-01";
        payload["principalSchedule"] = new[] { new { paymentDate = "2026-07-01", amount = 40m } };
        bond = bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(payload) };
        var result = (await Service(bond).CalculateAsync(new(Id, Context() with
        { SettlementDate = new DateOnly(2026, 8, 1) }, 1000m, 100m)))!;
        result.IsBlocked.Should().BeFalse();
        result.CurrentFace.Should().Be(600m);
        result.CashFlows.Sum(f => f.Principal).Should().Be(600m);
        result.CleanConsideration.Should().Be(600m);
    }

    [Fact]
    public async Task QuotePublishedAfterEconomicCutoffCannotLeakThroughLaterKnowledgeCutoff()
    {
        var quotes = Substitute.For<IBondWorkflowQuoteSource>();
        var quote = Quote(AsOf.AddHours(1));
        quotes.GetAsync(quote.ObservationId, Arg.Any<CancellationToken>()).Returns(quote);
        var service = Service(Bond(), quotes: quotes);
        var result = (await service.CalculateAsync(new(Id, Context() with { KnownAt = AsOf.AddDays(1) })
        { QuoteObservationId = quote.ObservationId }))!;
        result.IsBlocked.Should().BeTrue();
        result.Issues.Should().Contain(i => i.Code == "RetainedQuoteUnavailable");
    }

    [Fact]
    public async Task EqualPriceFromDifferentProviderCannotClaimGoldenCopyReceipt()
    {
        var quotes = Substitute.For<IBondWorkflowQuoteSource>();
        var quote = Quote(AsOf);
        quotes.GetAsync(quote.ObservationId, Arg.Any<CancellationToken>()).Returns(quote);
        var pricing = Substitute.For<ISecurityMasterPricingService>();
        var receiptId = Guid.NewGuid();
        pricing.GetGoldenCopySelectionAsync(Id, null, receiptId, Arg.Any<CancellationToken>())
            .Returns(new SecurityPriceGoldenCopyDto(Id, 100m, SecurityPriceKind.MarketGoldenCopy,
                "different-provider", AsOf, false, 0, [], SecurityPriceUnit.PercentOfPar,
                KnowledgeAsOf: AsOf, SelectionReceiptId: receiptId));
        var result = (await Service(Bond(), quotes: quotes, pricing: pricing).CalculateAsync(new(Id, Context())
        { QuoteObservationId = quote.ObservationId, PriceSelectionReceiptId = receiptId }))!;
        result.IsBlocked.Should().BeTrue();
        result.Issues.Should().Contain(i => i.Code == "PriceSelectionUnavailable");
    }

    [Fact]
    public async Task BackwardCarryBeforeCurveOriginLeavesProjectionAvailable()
    {
        var bond = Bond();
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        payload["issueDate"] = "2025-01-01";
        bond = bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(payload), EffectiveFrom = AsOf.AddYears(-1) };
        var result = (await Service(bond, Curves()).CalculateAsync(new(Id, Context(), 1000m, 100m,
            new DateOnly(2025, 12, 15))))!;
        result.IsBlocked.Should().BeFalse();
        result.CashFlows.Should().NotBeEmpty();
        result.PresentValue.Should().BeNull();
        result.DirtyConsideration.Should().BeNull();
        result.Issues.Should().Contain(i => i.Operation == "Valuation" && i.Reason.Contains("before the retained curve"));
    }

    [Theory]
    [InlineData("valid", false)]
    [InlineData("missing-knowledge", true)]
    [InlineData("future-evaluation", true)]
    [InlineData("different-security", true)]
    [InlineData("different-receipt", true)]
    public async Task PriceSelectionMustRetainExactSubjectReceiptAndEligibleCutoffs(string scenario, bool blocked)
    {
        var quotes = Substitute.For<IBondWorkflowQuoteSource>();
        var quote = Quote(AsOf);
        quotes.GetAsync(quote.ObservationId, Arg.Any<CancellationToken>()).Returns(quote);
        var pricing = Substitute.For<ISecurityMasterPricingService>();
        var receiptId = Guid.NewGuid();
        pricing.GetGoldenCopySelectionAsync(Id, null, receiptId, Arg.Any<CancellationToken>())
            .Returns(new SecurityPriceGoldenCopyDto(scenario == "different-security" ? Guid.NewGuid() : Id,
                100m, SecurityPriceKind.MarketGoldenCopy, "provider-one", AsOf, false, 0, [], SecurityPriceUnit.PercentOfPar,
                EvaluatedAsOf: scenario == "future-evaluation" ? AsOf.AddDays(1) : AsOf,
                KnowledgeAsOf: scenario == "missing-knowledge" ? null : AsOf,
                SelectionReceiptId: scenario == "different-receipt" ? Guid.NewGuid() : receiptId));
        var result = (await Service(Bond(), quotes: quotes, pricing: pricing).CalculateAsync(new(Id, Context())
        { QuoteObservationId = quote.ObservationId, PriceSelectionReceiptId = receiptId }))!;
        result.IsBlocked.Should().Be(blocked);
        if (blocked)
            result.Issues.Should().Contain(issue => issue.Code == "PriceSelectionUnavailable");
    }

    [Fact]
    public async Task IncomeScheduleCanReachMaturityWithoutInventingFurtherCash()
    {
        var result = (await Service(Bond()).CalculateIncomeScheduleAsync(new(Id, Context() with
        { AsOf = AsOf.AddYears(2), SettlementDate = new DateOnly(2028, 1, 1) }), new(2026, 1, 1)))!;
        result.IsBlocked.Should().BeFalse();
        result.CashFlows.Should().BeEmpty();
    }

    private static BondCalculationContext Context() => new(AsOf, new DateOnly(2026, 1, 1), AsOf);
    private static BondWorkflowService Service(SecurityDetailDto bond, ISecurityCurveStore? curves = null,
        IBondWorkflowQuoteSource? quotes = null, ISecurityMasterPricingService? pricing = null)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(bond);
        query.GetRecordedByIdAsOfAsync(Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(bond);
        return new(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), curves,
            quotes: quotes, pricing: pricing);
    }

    private static SecurityDetailDto Bond(decimal rate = 5m)
        => new(Id, "Bond", SecurityStatusDto.Active, "Synthetic workflow bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2,
                issueDate = "2026-01-01",
                maturity = "2028-01-01",
                couponType = "Fixed",
                couponRate = rate,
                dayCount = "30/360",
                par = 100m,
                paymentFrequency = "Annual",
                isCallable = false,
                scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
            }), [], [], 9, AsOf, null);

    private static ISecurityCurveStore Curves()
    {
        var store = Substitute.For<ISecurityCurveStore>();
        var curveId = Guid.Parse("632050fe-a56d-4c2a-a3e1-fc0c56d7aafa");
        var definition = new SecurityCurveDefinitionDto(curveId, "USD-SYNTHETIC", SecurityCurveKind.Discount,
            "USD", null, null, "ACT/365", SecurityCurveInterpolation.Linear, "synthetic", null, "test", AsOf);
        var version = new SecurityCurveVersionDto(Guid.NewGuid(), curveId, definition.CurveKey, 1,
            new DateOnly(2026, 1, 1), AsOf, AsOf, "synthetic", "test",
            [new(new(2026, 1, 1), 1m), new(new(2027, 1, 1), 0.95m), new(new(2028, 1, 1), 0.9m)], new string('a', 64));
        store.ListDefinitionsAsync(SecurityCurveKind.Discount, "USD", null, Arg.Any<CancellationToken>())
            .Returns(new[] { definition });
        store.GetLatestVersionAsOfAsync(curveId, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(version);
        return store;
    }

    private static BondMarketObservationDto Quote(DateTimeOffset published) => new(Guid.NewGuid(),
        new("quote-test", 1, BondMarketObservationKind.Quote, "provider-one", "bond-quote", new(2026, 1, 1),
            BondMarketValueUnit.PercentOfPar, 100m, "Provider One", new string('a', 64), "synthetic",
            published, Id, "USD", Quote: new(BondQuoteCategory.IndicativeQuote, BondQuotePriceBasis.Clean, new(2026, 1, 1))),
        published, "test", new string('b', 64));
}
