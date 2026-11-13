using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed partial class BondWorkflowServiceTests
{
    private static readonly Guid Id = Guid.Parse("38cf377c-cf7d-448e-920c-14d18a48bbe1");
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("mandatoryPutDate", "2027-01-01", false)]
    [InlineData("preRefundDate", "2027-01-01", false)]
    [InlineData("mandatoryPutDate", "2027-01-01", true)]
    [InlineData("preRefundDate", "2027-01-01", true)]
    [InlineData("mandatoryPutDate", "2028-01-01", false)]
    [InlineData("preRefundDate", "2029-01-01", false)]
    [InlineData("mandatoryPutDate", "", false)]
    [InlineData("preRefundDate", "not-a-date", false)]
    public async Task MandatoryRedemptionTermsRefuseMaturityCashValuationAndIncome(string key, string value, bool profile)
    {
        var bond = Bond();
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        if (profile)
            terms["profileFields"] = new Dictionary<string, object?> { [key] = value };
        else
            terms[key] = value;
        var service = Service(bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) }, Curves());
        foreach (var version in new[] { BondWorkflowModelVersions.Current, BondWorkflowModelVersions.Legacy })
        {
            var request = new BondWorkflowRequest(Id, Context(), 1000m, 100m) { ModelVersion = version };
            var result = (await service.CalculateAsync(request))!;
            result.IsBlocked.Should().BeTrue();
            result.Issues.Should().Contain(issue => issue.Code == "MandatoryRedemptionTermsUnsupported" && issue.BlocksCalculation);
            result.CashFlows.Should().BeEmpty();
            result.PresentValue.Should().BeNull();
            result.DirtyConsideration.Should().BeNull();
            result.Yield.Should().BeNull();
            result.YieldToWorst.Should().BeNull();
            var income = (await service.CalculateIncomeScheduleAsync(request, new(2026, 1, 1)))!;
            income.IsBlocked.Should().BeTrue();
            income.CashFlows.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AbsentMandatoryRedemptionDatesPreserveOrdinaryMaturityCash()
    {
        var bond = Bond();
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        terms["mandatoryPutDate"] = null;
        terms["profileFields"] = new Dictionary<string, object?> { ["preRefundDate"] = null };
        var result = (await Service(bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) }, Curves())
            .CalculateAsync(new(Id, Context(), 1000m, 100m)))!;
        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.CashFlows.Sum(flow => flow.Principal).Should().Be(1000m);
        result.PresentValue.Should().NotBeNull();
    }

    [Fact]
    public async Task ExerciseWithEarnedDeferredCouponMarksOnlyExerciseUnavailableAndKeepsBaseCashFlows()
    {
        var bond = Bond();
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        terms["assetClassTermsVersion"] = 3;
        terms["isCallable"] = true;
        terms["callDate"] = "2027-01-01";
        terms["callSchedule"] = new[] { new { callDate = "2027-01-01", callPrice = 100m,
            exerciseStyle = "European", priceBasis = "PercentOfPar" } };
        terms["scheduleConventions"] = new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" },
            paymentLagBusinessDays = 2
        };
        var result = (await Service(bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) })
            .CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        result.IsBlocked.Should().BeFalse();
        result.ExerciseScenarios.Should().ContainSingle(exercise => exercise.Kind == "Call",
            string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.ExerciseScenarios.Single(exercise => exercise.Kind == "Call").Issue.Should().Be("DelayedCouponEntitlementUnsupported");
        result.CashFlows.Should().Contain(flow => flow.AccrualEnd == new DateOnly(2027, 1, 1)
            && flow.PaymentDate == new DateOnly(2027, 1, 5) && flow.Interest == 5m);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task ExerciseOnEarlyCouponPaymentDoesNotAddThePaidCouponAsAccruedAgain(int rate)
    {
        var bond = Bond(rate);
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        terms["assetClassTermsVersion"] = 3;
        terms["maturity"] = "2029-01-01";
        terms["isCallable"] = true;
        terms["callDate"] = "2027-12-31";
        terms["callSchedule"] = new[] { new { callDate = "2027-12-31", callPrice = 100m,
            exerciseStyle = "European", priceBasis = "PercentOfPar" } };
        terms["scheduleConventions"] = new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" },
            businessDayConvention = "Preceding"
        };
        var result = (await Service(bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) })
            .CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.ExerciseScenarios.Should().ContainSingle(exercise => exercise.Kind == "Call",
            string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        var call = result.ExerciseScenarios.Single(exercise => exercise.Kind == "Call");
        call.Issue.Should().BeNull();
        // One coupon at one year, then coupon plus par one day before two years. Adding the
        // already-paid active coupon again would move this yield by roughly 2.5 percentage points.
        call.Yield.Should().BeApproximately(rate / 100m, .0002m);
    }

    [Fact]
    public async Task OnlyCurrentFrozenIncomeCanReachTheRetainedLaggedFinalCashDate()
    {
        var bond = Bond();
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        terms["scheduleConventions"] = new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" },
            paymentLagBusinessDays = 2
        };
        var service = Service(bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) });
        var date = new DateOnly(2028, 1, 4);
        var at = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var request = new BondWorkflowRequest(Id, new(at, date, at));
        var income = (await service.CalculateIncomeScheduleAsync(request, new(2026, 1, 1)))!;
        income.IsBlocked.Should().BeFalse(string.Join("; ", income.Issues.Select(issue => issue.Reason)));
        income.CashFlows.Should().BeEmpty();
        var after = (await service.CalculateIncomeScheduleAsync(request with { Context = new(at.AddDays(1), date.AddDays(1), at.AddDays(1)) }, new(2026, 1, 1)))!;
        after.Issues.Should().Contain(issue => issue.Code == "SettlementOutsideLife");
        (await service.CalculateAsync(request))!.Issues.Should().Contain(issue => issue.Code == "SettlementOutsideLife");
        (await service.CalculateIncomeScheduleAsync(request with { ModelVersion = BondWorkflowModelVersions.Legacy }, new(2026, 1, 1)))!
            .Issues.Should().Contain(issue => issue.Code == "SettlementOutsideLife");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task EarlyPaidActiveCouponIsExcludedFromPurchaserInvoiceAndRetainsBookBoundaryEvidence(int rate)
    {
        var bond = Bond(rate);
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        terms["scheduleConventions"] = new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" },
            businessDayConvention = "Preceding"
        };
        var service = Service(bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) });
        var date = new DateOnly(2027, 12, 31);
        var at = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var request = new BondWorkflowRequest(Id, new(at, date, at));
        var result = (await service.CalculateAsync(request))!;
        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.AccruedInterest.Should().Be(0m);
        result.HasPaidActiveCoupon.Should().BeTrue();
        result.CashFlows.Should().BeEmpty();
        var old = (await service.CalculateAsync(request with { ModelVersion = BondWorkflowModelVersions.Legacy }))!;
        old.AccruedInterest.Should().Be(0m);
        old.HasPaidActiveCoupon.Should().BeNull();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task PaymentLagSeparatesActiveAndDeferredInvoiceInterest_AndLegacyReceiptPinsItsOriginalArithmetic(int rate)
    {
        var bond = Bond(rate);
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        terms["scheduleConventions"] = new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" },
            paymentLagBusinessDays = 2
        };
        bond = bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        var service = Service(bond);
        var date = new DateOnly(2027, 1, 4);
        var at = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var request = new BondWorkflowRequest(Id, new(at, date, at), CleanPricePer100: 100m);
        var current = (await service.CalculateAsync(request))!;
        current.IsBlocked.Should().BeFalse(string.Join("; ", current.Issues.Select(issue => issue.Reason)));
        current.ModelVersion.Should().Be(BondWorkflowModelVersions.Current);
        current.ContractualAccruedInterest.Should().BeApproximately(rate * 3m / 360m, 0.000000000001m);
        current.DeferredCouponInterest.Should().Be(rate);
        current.AccruedInterest.Should().Be(Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(rate + rate * 3m / 360m, "USD"));
        current.Request!.ModelVersion.Should().Be(BondWorkflowModelVersions.Current);
        var paidDate = new DateOnly(2027, 1, 5);
        var paid = (await service.CalculateAsync(request with { Context = new(at.AddDays(1), paidDate, at.AddDays(1)) }))!;
        paid.DeferredCouponInterest.Should().Be(0m);
        paid.ContractualAccruedInterest.Should().BeApproximately(rate * 4m / 360m, 0.000000000001m);
        var legacy = (await service.CalculateAsync(request with { ModelVersion = BondWorkflowModelVersions.Legacy }))!;
        legacy.ContractualAccruedInterest.Should().Be(rate);
        legacy.DeferredCouponInterest.Should().BeNull();
        var oldReceipt = legacy with { Request = legacy.Request! with { ModelVersion = null } };
        var replay = await service.ReplayAsync(oldReceipt);
        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(oldReceipt));
        JsonSerializer.Serialize(oldReceipt.Request).Should().NotContain("ModelVersion");
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyObservedFixingReceiptPinCannotSilentlySelectLatestTypedObservations(bool invalidIdentity)
    {
        var observations = Substitute.For<IBondWorkflowObservationSource>();
        var curves = Curves();
        var result = (await Service(Bond(), curves, observations: observations).CalculateAsync(new(Id,
            Context() with { ObservedFixingReceiptIds = [invalidIdentity ? Guid.Empty : Guid.NewGuid()] }, CleanPricePer100: 100m)))!;

        result.IsBlocked.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "UnsupportedObservedFixingSelection"
            && issue.Reason.Contains("floating v3", StringComparison.Ordinal));
        await observations.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await curves.DidNotReceive().ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("matching", false)]
    [InlineData("clean-dirty", true)]
    [InlineData("quotation-unit", true)]
    [InlineData("settlement-date", true)]
    [InlineData("settlement-basis", true)]
    [InlineData("missing-typed-basis", true)]
    public async Task SelectionQuotationMustBindExactTypedQuoteEconomicMeaning(string scenario, bool blocked)
    {
        var quotes = Substitute.For<IBondWorkflowQuoteSource>();
        var quote = Quote(AsOf);
        quote = quote with
        {
            Input = quote.Input with
            {
                Quote = quote.Input.Quote! with
                { SettlementBasis = scenario == "missing-typed-basis" ? null : "regular-way" }
            }
        };
        quotes.GetAsync(quote.ObservationId, Arg.Any<CancellationToken>()).Returns(quote);
        var pricing = Substitute.For<ISecurityMasterPricingService>();
        var receiptId = Guid.NewGuid();
        pricing.GetGoldenCopySelectionAsync(Id, null, receiptId, Arg.Any<CancellationToken>())
            .Returns(new SecurityPriceGoldenCopyDto(Id, 100m, SecurityPriceKind.MarketGoldenCopy, "provider-one", AsOf,
                false, 0, [], SecurityPriceUnit.PercentOfPar, EvaluatedAsOf: AsOf, KnowledgeAsOf: AsOf,
                SelectionReceiptId: receiptId)
            {
                Quotation = new(scenario == "clean-dirty" ? SecurityPriceBasis.Dirty : SecurityPriceBasis.Clean,
                    scenario == "quotation-unit" ? SecurityQuotationConvention.CurrencyPerUnit : SecurityQuotationConvention.Per100Face,
                    SecurityQuoteStatus.Mid, scenario == "settlement-date" ? new(2026, 1, 2) : new(2026, 1, 1),
                    scenario == "settlement-basis" ? "cash-settlement" : "regular-way")
            });

        var result = (await Service(Bond(), quotes: quotes, pricing: pricing).CalculateAsync(new(Id, Context())
        { QuoteObservationId = quote.ObservationId, PriceSelectionReceiptId = receiptId }))!;

        result.IsBlocked.Should().Be(blocked);
        if (blocked)
            result.Issues.Should().Contain(issue => issue.Code == "PriceSelectionQuotationMismatch");
    }

    [Fact]
    public void AbsentOptionalTypedSettlementBasisDoesNotChangeLegacyQuoteBytes()
        => JsonSerializer.Serialize(Quote(AsOf)).Should().NotContain("SettlementBasis").And.NotContain("settlementBasis");

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

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task ExCouponInvoiceAdjustmentRetainsSeparateSignedContractualHeldAccrual(int rate)
    {
        var bond = Bond(rate);
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        terms["assetClassTermsVersion"] = 5;
        terms["bondWorkflow"] = new { exCouponBusinessDays = 10 };
        terms["scheduleConventions"] = new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" },
            businessDayConvention = "Following"
        };
        bond = bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        var date = new DateOnly(2026, 12, 28);
        var result = (await Service(bond).CalculateIncomeScheduleAsync(new(Id,
            new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), date, AsOf)), date))!;

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.ContractualAccruedInterest.Should().HaveValue();
        Math.Sign(result.ContractualAccruedInterest!.Value).Should().Be(Math.Sign(rate));
        (Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(result.ContractualAccruedInterest!.Value, "USD")
            - result.AccruedInterest).Should().Be(rate);
        result.HasDetachedCoupon.Should().BeTrue();
    }

    private static BondCalculationContext Context() => new(AsOf, new DateOnly(2026, 1, 1), AsOf);
    private static BondWorkflowService Service(SecurityDetailDto bond, ISecurityCurveStore? curves = null,
        IBondWorkflowQuoteSource? quotes = null, ISecurityMasterPricingService? pricing = null,
        IBondWorkflowObservationSource? observations = null)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(bond);
        query.GetRecordedByIdAsOfAsync(Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(bond);
        return new(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), curves,
            observations: observations, quotes: quotes, pricing: pricing);
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
