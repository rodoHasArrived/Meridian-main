using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Curves;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Calendars;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.Storage;
using Meridian.Tests.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondConsistencyTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task IndependentStubFixture_AgreesAcrossAnalyticsCashFlowsSettlementAndV5(int fixtureIndex)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "fixed-income", "bond-consistency.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var f = document.RootElement[fixtureIndex];
        var issue = DateOnly.Parse(f.GetProperty("issue").GetString()!);
        var date = Utc(issue);
        var security = Bond(new()
        {
            ["issueDate"] = f.GetProperty("issue").GetString(),
            ["dayCount"] = f.GetProperty("dayCount").GetString(),
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level", firstRegularDate = f.GetProperty("firstRegular").GetString() }
        }) with
        { EffectiveFrom = date };
        var query = Query(security);
        var analytics = new BondAnalyticsService(query);
        var priced = (await analytics.CalculateAsync(new(security.SecurityId, new(date), Yield: 0m)))!;
        priced.Blockers.Should().BeEmpty();
        priced.CashFlows[0].Interest.Should().BeApproximately(f.GetProperty("firstCoupon").GetDecimal(), 0.000000000001m);
        priced.DirtyPrice.Should().Be(f.GetProperty("zeroYieldDirty").GetDecimal());
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(date)))!;
        projection.BlockedReason.Should().BeNull();
        projection.Schedule[0].InterestAmount.Should().Be(decimal.Round(f.GetProperty("firstCoupon").GetDecimal(), 4, MidpointRounding.AwayFromZero));
        var assets = new AssetObligationProjectionService().ProjectFromSecurityMaster(security, new(date), projection);
        assets.ProjectedCashFlows.Single(flow => flow.FlowType == "Coupon" && flow.DueDate == new DateOnly(2026, 1, 1)).Amount.Should().Be(projection.Schedule[0].InterestAmount);
        var settlement = await new BondSettlementConsiderationService(analytics).PrepareAsync(new(
            new(security.SecurityId, new(date), Yield: 0m), 100m, 1m, 0m, 105.42m,
            "cash-fixture", "Assets/Cash", "Assets/PurchasedInterest"));
        settlement.Blockers.Should().BeEmpty();
        settlement.Settlement!.CleanConsideration.Should().Be(105.42m);
        settlement.Settlement.PurchasedAccruedInterest.Should().Be(0m);
        var baseline = AtomicTaxLotJournalStoreTests.AmortPureInstruction(priced.CleanPrice!.Value, 5m, BondAmortizationMethod.ConstantYield, 0m);
        var retainedSecurity = baseline.Security with
        {
            CommonTerms = security.CommonTerms,
            AssetSpecificTerms = security.AssetSpecificTerms,
            EffectiveFrom = date
        };
        var lot = baseline.ExpectedLot with
        {
            AcquiredDate = issue,
            Acquisition = baseline.ExpectedLot.Acquisition with
            {
                HoldingPeriodStartDate = issue,
                Evidence = baseline.ExpectedLot.Acquisition.Evidence.Select(item => item with { EffectiveDate = issue }).ToArray()
            }
        };
        var instruction = baseline with
        {
            ExpectedLot = lot,
            Security = retainedSecurity,
            SecurityEvidence = baseline.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(retainedSecurity) },
            AsOfDate = new(2026, 1, 1),
            CalculationVersion = OpenLotAmortization.UnifiedDeclaredModelVersion
        };
        OpenLotAmortization.Project(instruction).TransactionCostBasis.Should().BeApproximately(105m, 0.0000000001m);
        OpenLotAmortization.Project(instruction with { AsOfDate = new(2027, 1, 1) }).TransactionCostBasis.Should().Be(100m);
        // V4's different stub convention remains available and cannot silently adopt v5 economics.
        var old = () => OpenLotAmortization.Project(instruction with { CalculationVersion = OpenLotAmortization.DeclaredModelVersion });
        if (fixtureIndex == 0)
            old.Should().Throw<ArgumentException>().WithMessage("*yield does not reconcile*");
    }

    [Theory]
    [InlineData(false, 0.04)]
    [InlineData(true, null)]
    public async Task MissingOrExplicitlyNullReset_CannotUseCurrentOrEarlierRate(bool exactNull, double? earlier)
    {
        var security = Floating(new()
        {
            ["currentIndexRate"] = 4m,
            ["currentIndexResetDate"] = "2026-01-01",
            ["currentIndexRateSource"] = "observed-source",
            ["resetSchedule"] = new[] { new { resetDate = exactNull ? "2026-01-01" : "2025-07-01", fixingRate = earlier } }
        });
        // In the missing case the scalar is deliberately stale; in the null case it must not override null.
        if (!exactNull)
            security = Change(security, "currentIndexResetDate", "2025-07-01");
        var result = await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf));
        result!.BlockedReason.Should().Contain("observed fixing is missing");
        result.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitUnitsRequired_AndScalarFixingMustMatchTheReset()
    {
        var security = Floating(new() { ["currentIndexRate"] = 4m, ["currentIndexResetDate"] = "2026-01-01", ["currentIndexRateSource"] = "fixing:SOFR:2026-01-01" });
        var valid = await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf));
        valid!.BlockedReason.Should().BeNull();
        valid.Schedule[0].RateEvidence!.IsObserved.Should().BeTrue();
        var undeclaredUnit = Change(security, "scheduleConventions", new { accrualAdjusted = false });
        var invalid = await CashFlows(undeclaredUnit).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf));
        invalid!.BlockedReason.Should().Contain("explicit");
    }

    [Theory]
    [InlineData(0, null, 0, 0.5)]
    [InlineData(4, 4.5, 2, 2.25)]
    public async Task RateShock_RecomputesZeroForecastAndCapsWhileKeepingObservedCashFixed(double index, double? cap, double observedCash, double forecastCash)
    {
        var security = Floating(new() { ["capRate"] = cap, ["resetSchedule"] = new[] { new { resetDate = "2026-01-01", fixingRate = index } } });
        var projection = (await CashFlows(security, (decimal)index / 100m).GetProjectionAsync(security.SecurityId,
            StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        projection.BlockedReason.Should().BeNull();
        var ladder = PortfolioCashLadderEngine.Build(Inputs(security, projection), PortfolioCashLadderEngine.RatesUpScenarioId);
        ladder.BlockingReasons.Should().BeEmpty();
        var coupons = ladder.Contributions.Where(row => row.FlowType == "Coupon").OrderBy(row => row.DueDate).ToArray();
        coupons[0].Amount.Should().Be((decimal)observedCash);
        coupons[1].Amount.Should().Be((decimal)forecastCash);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0d, true)]
    public async Task DownShock_UsesContractualFloor_AndRefusesUnsupportedNegativeCash(double? floor, bool supported)
    {
        var security = Floating(new() { ["floorRate"] = floor, ["resetSchedule"] = new[] { new { resetDate = "2026-01-01", fixingRate = 0m } } });
        var projection = (await CashFlows(security, 0m).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        var ladder = PortfolioCashLadderEngine.Build(Inputs(security, projection), PortfolioCashLadderEngine.RatesDownScenarioId);
        ladder.IsDecisionReady.Should().Be(supported);
        if (!supported)
        { ladder.BlockingReasons.Should().Contain(reason => reason.Contains("Negative floating")); ladder.Buckets.Should().BeEmpty(); }
    }

    [Theory]
    [InlineData("early-call", "CallRedemption", 101)]
    [InlineData("holder-put", "PutRedemption", 105)]
    public async Task DeclaredCallAndPut_UseSeparateContractualNonParProceeds(string scenario, string flowType, int price)
    {
        var security = Bond(new()
        {
            ["isCallable"] = true,
            ["callSchedule"] = new[] { new { callDate = "2026-07-01", exerciseStyle = "European", callPrice = 101m, priceBasis = "PercentOfPar", priceType = "Fixed" } },
            ["putSchedule"] = new[] { new { putDate = "2026-07-01", exerciseStyle = "European", putPrice = 105m, priceBasis = "PercentOfPar" } }
        });
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        var inputs = Inputs(security, projection);
        var ladder = PortfolioCashLadderEngine.Build(inputs, scenario);
        ladder.BlockingReasons.Should().BeEmpty();
        ladder.Contributions.Single(row => row.FlowType == flowType).Amount.Should().Be(price);
        ladder.Contributions.Single(row => row.FlowType == "Coupon").Amount.Should().Be(2.5m);
        ladder.Contributions.Should().NotContain(row => row.DueDate > new DateOnly(2026, 7, 1));
        var legacy = LegacyPortfolioCashLadderEngine.Build(inputs, scenario);
        legacy.EngineVersion.Should().Be("portfolio-cash-ladder-v1");
    }

    [Fact]
    public async Task ScenarioRefusesUnboundTerms_AndRetainedAnalyticsVersionReplays()
    {
        var security = Bond();
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        var inputs = Inputs(security, projection);
        var position = inputs.Positions[0];
        var changed = position.Operations with { CashFlowProjectionRuns = position.Operations.CashFlowProjectionRuns.Select(run => run with { ExtensionPayload = null }).ToArray() };
        var blocked = PortfolioCashLadderEngine.Build(inputs with { Positions = [position with { Operations = changed }] }, PortfolioCashLadderEngine.EarlyCallScenarioId);
        blocked.IsDecisionReady.Should().BeFalse();
        blocked.BlockingReasons.Should().Contain(reason => reason.Contains("exact retained terms"));
        var analytics = new BondAnalyticsService(Query(security));
        var old = (await analytics.CalculateAsync(new(security.SecurityId, new(AsOf), Yield: 0.05m) { CalculationVersion = "bond-analytics-v1" }))!;
        old.ModelVersion.Should().Be("bond-analytics-v1");
        old.Inputs.Should().NotContain(input => input.Kind == "ContractualEconomics");
    }

    [Fact]
    public async Task OffCouponCall_IncludesContractualAccruedCash()
    {
        var security = Bond(new()
        {
            ["isCallable"] = true,
            ["callSchedule"] = new[] { new { callDate = "2026-04-01", exerciseStyle = "European", callPrice = 101m } }
        });
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        var ladder = PortfolioCashLadderEngine.Build(Inputs(security, projection), PortfolioCashLadderEngine.EarlyCallScenarioId);
        ladder.BlockingReasons.Should().BeEmpty();
        // 101 principal consideration + 100 * .05 * 90/360 accrued coupon.
        ladder.Contributions.Should().ContainSingle().Which.Amount.Should().Be(102.25m);
    }

    [Fact]
    public async Task PartiallyRepaidPrincipal_IsRedeemedOnceAtTheContractualPrice()
    {
        var security = Bond(new()
        {
            ["isCallable"] = true,
            ["principalSchedule"] = new[] { new { paymentDate = "2026-04-01", amount = 40m } },
            ["callSchedule"] = new[] { new { callDate = "2026-07-01", exerciseStyle = "European", callPrice = 101m } }
        });
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        projection.BlockedReason.Should().BeNull();
        var ladder = PortfolioCashLadderEngine.Build(Inputs(security, projection), PortfolioCashLadderEngine.EarlyCallScenarioId);
        ladder.BlockingReasons.Should().BeEmpty();
        ladder.Contributions.Single(row => row.FlowType == "CallRedemption").Amount.Should().Be(60.6m);
        ladder.Contributions.Single(row => row.FlowType == "PrincipalRepayment").Amount.Should().Be(40m);
        ladder.Contributions.Single(row => row.FlowType == "Coupon").Amount.Should().Be(2m);
        ladder.Contributions.Should().NotContain(row => row.DueDate > new DateOnly(2026, 7, 1));
    }

    [Theory]
    [InlineData("MakeWhole")]
    [InlineData("AccretedValue")]
    public async Task UnsupportedExercise_DoesNotFabricateParCash(string priceType)
    {
        var security = Bond(new()
        {
            ["isCallable"] = true,
            ["callSchedule"] = new[] { new { callDate = "2026-07-01", exerciseStyle = "European", callPrice = 101m, priceType } }
        });
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        var ladder = PortfolioCashLadderEngine.Build(Inputs(security, projection), PortfolioCashLadderEngine.EarlyCallScenarioId);
        ladder.IsDecisionReady.Should().BeFalse();
        ladder.Contributions.Should().BeEmpty();
    }

    [Fact]
    public async Task HolderPutNeverBecomesAnIssuerCall()
    {
        var security = Bond(new() { ["putSchedule"] = new[] { new { putDate = "2026-07-01", exerciseStyle = "European", putPrice = 105m } } });
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        var ladder = PortfolioCashLadderEngine.Build(Inputs(security, projection), PortfolioCashLadderEngine.EarlyCallScenarioId);
        ladder.BlockingReasons.Should().BeEmpty();
        ladder.Contributions.Should().NotContain(row => row.FlowType.Contains("Redemption"));
        ladder.Contributions.Single(row => row.FlowType == "Maturity").DueDate.Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public async Task FixedCoupon_AndRetainedForecastInputs_AreNotChangedByLadderShocks()
    {
        var security = Bond();
        var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
        var inputs = Inputs(security, projection);
        var baseLadder = PortfolioCashLadderEngine.Build(inputs);
        var shocked = PortfolioCashLadderEngine.Build(inputs, PortfolioCashLadderEngine.RatesUpScenarioId);
        shocked.Contributions.Select(row => row.Amount).Should().Equal(baseLadder.Contributions.Select(row => row.Amount));
        shocked.BlockingReasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2024-01-31", "2025-01-31", "ACT/365F", 105.0)]
    [InlineData("2024-01-31", "2025-01-31", "ACT/ACT ICMA", 105.0)]
    public async Task MonthEndAndLeapYear_KeepLevelCouponCash(string issue, string maturity, string dayCount, double expectedDirty)
    {
        var date = Utc(DateOnly.Parse(issue));
        var security = Bond(new()
        {
            ["issueDate"] = issue,
            ["maturity"] = maturity,
            ["dayCount"] = dayCount,
            ["scheduleConventions"] = new { accrualAdjusted = false, endOfMonthRule = true, couponRateUnit = "Percent", couponCashBasis = "Level" }
        }) with
        { EffectiveFrom = date };
        var result = (await new BondAnalyticsService(Query(security)).CalculateAsync(new(security.SecurityId, new(date), Yield: 0m)))!;
        result.Blockers.Should().BeEmpty();
        result.DirtyPrice.Should().Be((decimal)expectedDirty);
        result.CashFlows.Select(flow => flow.PaymentDate).Should().Equal(new DateOnly(2024, 7, 31), new DateOnly(2025, 1, 31));
        result.CashFlows.Should().OnlyContain(flow => flow.Interest == 2.5m);
    }

    [Fact]
    public async Task PaymentLag_KeepsContractualCashAndAddsActualDiscountTime()
    {
        var date = new DateTimeOffset(2026, 1, 30, 0, 0, 0, TimeSpan.Zero);
        var security = Bond(new()
        {
            ["issueDate"] = "2026-01-30",
            ["maturity"] = "2027-01-30",
            ["scheduleConventions"] = new
            {
                accrualAdjusted = false,
                couponRateUnit = "Percent",
                couponCashBasis = "Level",
                paymentLagBusinessDays = 2,
                paymentCalendars = new[] { "WEEKENDS" }
            }
        }) with
        { EffectiveFrom = date };
        var calendars = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var result = (await new BondAnalyticsService(Query(security), calendars).CalculateAsync(new(security.SecurityId, new(date), Yield: 0m)))!;
        result.Blockers.Should().BeEmpty();
        result.CashFlows[0].Interest.Should().Be(2.5m);
        // Jul 30 2026 is Thursday: two business days settle on Monday Aug 3, four calendar days later.
        result.CashFlows[0].PaymentDate.Should().Be(new DateOnly(2026, 8, 3));
        result.CashFlows[0].TimeInYears.Should().Be(0.5m + 4m / 365m);
    }

    [Fact]
    public async Task SettlementWithinStub_AccruesReferencePeriodCash()
    {
        var security = Bond(new()
        {
            ["issueDate"] = "2025-12-01",
            ["dayCount"] = "ACT/365F",
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level", firstRegularDate = "2026-01-01" }
        })
            with
        { EffectiveFrom = new(2025, 12, 1, 0, 0, 0, TimeSpan.Zero) };
        var date = new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero);
        var analytics = new BondAnalyticsService(Query(security));
        var result = (await analytics.CalculateAsync(new(security.SecurityId, new(date), Yield: 0m)))!;
        result.Blockers.Should().BeEmpty();
        // 14 elapsed days in a 184-day reference half-year: 2.5 * 14/184.
        result.AccruedInterest.Should().Be(0.190217391304m);
        result.CleanPrice.Should().Be(105.230978260870m);
        var settlement = await new BondSettlementConsiderationService(analytics).PrepareAsync(new(
            new(security.SecurityId, new(date), Yield: 0m), 100m, 1m, 0m, 105.42m, "cash-stub", "Assets/Cash", "Assets/PurchasedInterest"));
        settlement.Blockers.Should().BeEmpty();
        settlement.Settlement!.CleanConsideration.Should().Be(105.23m);
        settlement.Settlement.PurchasedAccruedInterest.Should().Be(0.19m);
    }

    [Fact]
    public async Task BothReceiptVersions_ReplayTheirOriginalStubArithmetic()
    {
        var date = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var security = Bond(new()
        {
            ["issueDate"] = "2025-12-01",
            ["dayCount"] = "ACT/365F",
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level", firstRegularDate = "2026-01-01" }
        }) with
        { EffectiveFrom = date };
        var receipts = Substitute.For<ISecurityCashFlowReceiptStore>();
        var retained = new Dictionary<string, SecurityCashFlowCalculationReceipt>();
        receipts.RetainAsync(Arg.Any<SecurityCashFlowCalculationReceipt>(), Arg.Any<CancellationToken>())
            .Returns(call => { var r = call.Arg<SecurityCashFlowCalculationReceipt>(); retained.Add(r.ContentSha256, r); return Task.CompletedTask; });
        receipts.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(retained.GetValueOrDefault(call.Arg<string>())));
        var service = CashFlows(security, receipts: receipts);
        var current = (await service.CaptureProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base, new(date)))!;
        current.ModelVersion.Should().Be("bond-cash-flow-context-v2");
        current.Projection.Schedule[0].InterestAmount.Should().Be(0.4212m);
        var legacyMethod = typeof(SecurityMasterCashFlowService).GetMethod("BuildCalculatedProjection",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var legacyProjection = (StructuredCashFlowProjectionDto)legacyMethod.Invoke(null,
            [security, current.Source.SourceKind, StructuredCashFlowScenario.Base, current.Projection.Staleness,
             current.Source.LastUpdatedUtc, date, null, null, null])!;
        // Frozen v1 uses 100 * .05 * 31/365 for this stub rather than Level reference units.
        legacyProjection.Schedule[0].InterestAmount.Should().Be(0.4247m);
        legacyProjection = legacyProjection with
        {
            InputManifest = current.Projection.InputManifest!
            .Where(input => input.Kind != "ContractualEconomics")
            .Select(input => input.Kind == "CalculationModel" ? input with { Key = "bond-cash-flow-context-v1" } : input).ToArray()
        };
        var old = current with { ModelVersion = "bond-cash-flow-context-v1", Projection = legacyProjection, ContentSha256 = string.Empty };
        old = old with { ContentSha256 = SecurityMasterCashFlowService.ReceiptHash(old) };
        retained.Add(old.ContentSha256, old);
        (await service.ReplayProjectionAsync(old.ContentSha256))!.Schedule.Should().Equal(legacyProjection.Schedule);
        (await service.ReplayProjectionAsync(current.ContentSha256))!.Schedule.Should().Equal(current.Projection.Schedule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bond-analytics-v1")]
    public async Task CompanionContractsCannotBeIgnoredByYieldAnalytics(string? calculationVersion)
    {
        var security = Bond(new() { ["assetClassTermsVersion"] = 4, ["cashFlowContract"] = new { version = 1 } });
        var result = (await new BondAnalyticsService(Query(security)).CalculateAsync(
            new(security.SecurityId, new(AsOf), Yield: 0.05m) { CalculationVersion = calculationVersion }))!;
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("UnsupportedCashFlowContract");
    }

    internal static PortfolioCashLadderInputs Inputs(SecurityDetailDto security, StructuredCashFlowProjectionDto projection)
    {
        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security, new(AsOf), projection);
        return new(new DateOnly(2026, 1, 1), 400, "USD", [new(detail, 1m)], [new("cash", "Cash", 100m, "USD", "fixture", null)], [], 0m);
    }

    private static SecurityMasterCashFlowService CashFlows(SecurityDetailDto security, decimal forward = 0.04m, ISecurityCashFlowReceiptStore? receipts = null)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(security.SecurityId,
            StructuredCashFlowSourceKind.CalculatedBullet, security.EffectiveFrom, false, null, null));
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new("SOFR", new(2026, 1, 1), [new(new(2026, 1, 1), forward), new(new(2027, 1, 1), forward)]));
        return new(store, [], Query(security), NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: curves, receiptStore: receipts);
    }

    private static ISecurityMasterQueryService Query(SecurityDetailDto security)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        return query;
    }

    private static SecurityDetailDto Floating(Dictionary<string, object?> overrides)
    { overrides["couponType"] = "Floating"; overrides["floatingIndex"] = "SOFR"; overrides["spreadBps"] = 0m; return Bond(overrides); }

    private static SecurityDetailDto Bond(Dictionary<string, object?>? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 3,
            ["issueDate"] = "2026-01-01",
            ["maturity"] = "2027-01-01",
            ["couponType"] = "Fixed",
            ["couponRate"] = 5m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["isCallable"] = false,
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
        };
        foreach (var item in overrides ?? [])
            payload[item.Key] = item.Value;
        if (payload.TryGetValue("callSchedule", out var calls))
        {
            var rows = JsonSerializer.SerializeToElement(calls);
            if (rows.GetArrayLength() > 0 && !payload.ContainsKey("callDate"))
                payload["callDate"] = rows[0].GetProperty("callDate").GetString();
        }
        return new(Guid.NewGuid(), "Bond", SecurityStatusDto.Active, "Consistency bond", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(payload), [], [], 1, AsOf, null);
    }

    private static SecurityDetailDto Change(SecurityDetailDto security, string name, object value)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(security.AssetSpecificTerms.GetRawText())!;
        values[name] = JsonSerializer.SerializeToElement(value);
        return security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(values) };
    }

    private static DateTimeOffset Utc(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
