using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed partial class BondWorkflowServiceTests
{
    [Theory]
    [InlineData("European")]
    [InlineData("Bermudan")]
    public async Task FinalDateParExerciseReplacesTheNaturalBalloonAndKeepsEveryGridScenarioCovered(string style)
    {
        var bond = ExerciseBond(style);
        var result = (await Service(bond).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m, Fees: 2m)))!;

        result.IsBlocked.Should().BeFalse();
        result.ModelVersion.Should().Be("bond-workflow-v3");
        result.CashFlows.Sum(row => row.Principal).Should().Be(100m);
        result.Yield.Should().BeApproximately(0m, .0000000001m);
        result.ExerciseScenarios.Should().HaveCount(style == "European" ? 1 : 2);
        result.ExerciseScenarios.Should().OnlyContain(row => row.Issue == null && row.Yield != null);
        foreach (var scenario in result.ExerciseScenarios)
            scenario.Yield.Should().BeApproximately(0m, .0000000001m, "par redemption pays one original face, including on the final payment date");
        result.YieldToWorst.Should().BeApproximately(0m, .0000000001m);
        result.DirtyConsideration.Should().Be(100m);
        result.Fees.Should().Be(2m);
        result.TotalCash.Should().Be(102m, "purchase fees stay separate from conventional quoted cash-flow yield");
    }

    [Theory]
    [InlineData(0, 110, 0, .10)]
    [InlineData(40, 110, 0, .06)]
    [InlineData(100, 110, 0, 0)]
    [InlineData(40, 110, 5, .11)]
    [InlineData(0, 100, -5, -.05)]
    public async Task FinalExerciseRetainsExplicitInstallmentsAndSignedCouponsBeforeReplacingOnlyResidualPrincipal(
        int finalInstallment, int exercisePrice, int coupon, double expectedYield)
    {
        var bond = ExerciseBond("European", coupon, exercisePrice);
        if (finalInstallment != 0)
            bond = WithExerciseTerms(bond, terms => terms["principalSchedule"] =
                new[] { new { paymentDate = "2027-01-01", amount = finalInstallment } });
        var result = (await Service(bond).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.CashFlows.Sum(row => row.Principal).Should().Be(100m);
        result.CashFlows.Sum(row => row.Interest).Should().Be(coupon);
        var scenario = result.ExerciseScenarios.Should().ContainSingle().Which;
        scenario.Issue.Should().BeNull();
        // At one year: coupon + the par installment + remaining face at the exercise price.
        scenario.Yield.Should().BeApproximately((decimal)expectedYield, .0000000001m);
        result.YieldToWorst.Should().BeApproximately(Math.Min(coupon / 100m, (decimal)expectedYield), .0000000001m);
    }

    [Fact]
    public async Task EarlierAndFinalInstallmentsRemainInTheExerciseCashFlowAtTheirOwnDates()
    {
        var bond = WithExerciseTerms(ExerciseBond("European", exercisePrice: 110m), terms =>
        {
            terms["maturity"] = "2028-01-01";
            terms["callDate"] = "2028-01-01";
            terms["callSchedule"] = new[] { new { callDate = "2028-01-01", callPrice = 110m, exerciseStyle = "European", priceBasis = "PercentOfPar" } };
            terms["principalSchedule"] = new[]
            {
                new { paymentDate = "2027-01-01", amount = 20m },
                new { paymentDate = "2028-01-01", amount = 40m }
            };
        });
        var result = (await Service(bond).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;

        result.IsBlocked.Should().BeFalse();
        result.CashFlows.Where(row => row.PaymentDate == new DateOnly(2027, 1, 1)).Sum(row => row.Principal).Should().Be(20m);
        result.CashFlows.Where(row => row.PaymentDate == new DateOnly(2028, 1, 1)).Sum(row => row.Principal).Should().Be(80m);
        // 100 = 20/(1+y) + (40 + 40*1.10)/(1+y)^2.
        var expected = (decimal)((20d + Math.Sqrt(34_000d)) / 200d - 1d);
        result.ExerciseScenarios.Single().Yield.Should().BeApproximately(expected, .0000000001m);
    }

    [Fact]
    public async Task EarlierFullExplicitRepaymentDoesNotAcquireAnotherRedemptionOrANegativeFactor()
    {
        var bond = WithExerciseTerms(ExerciseBond("European", exercisePrice: 110m), terms =>
            terms["principalSchedule"] = new[] { new { paymentDate = "2026-07-01", amount = 100m } });
        var result = (await Service(bond).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;

        result.IsBlocked.Should().BeFalse();
        result.CashFlows.Sum(row => row.Principal).Should().Be(100m);
        result.ExerciseScenarios.Single().Issue.Should().BeNull();
        result.ExerciseScenarios.Single().Yield.Should().BeApproximately(0m, .0000000001m);
    }

    [Theory]
    [InlineData("Preceding", "2027-12-31")]
    [InlineData("Following", "2028-01-03")]
    public async Task ExerciseAndNaturalBalloonUseTheSameActualAdjustedFinalPaymentDate(string convention, string expectedDate)
    {
        var bond = WithExerciseTerms(ExerciseBond("European"), terms =>
        {
            terms["maturity"] = "2028-01-01";
            terms["callDate"] = "2028-01-01";
            terms["callSchedule"] = new[] { new { callDate = "2028-01-01", callPrice = 100m, exerciseStyle = "European", priceBasis = "PercentOfPar" } };
            terms["scheduleConventions"] = new
            {
                accrualAdjusted = false,
                couponRateUnit = "Percent",
                couponCashBasis = "Level",
                paymentCalendars = new[] { "WEEKENDS" },
                businessDayConvention = convention
            };
        });
        var result = (await Service(bond).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;

        result.IsBlocked.Should().BeFalse();
        var date = DateOnly.ParseExact(expectedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        result.CashFlows.Single(row => row.Principal != 0m).PaymentDate.Should().Be(date);
        var exercise = result.ExerciseScenarios.Should().ContainSingle().Which;
        exercise.ExerciseDate.Should().Be(date, "the declared date resolver applies the same payment adjustment");
        exercise.Issue.Should().BeNull();
        exercise.Yield.Should().BeApproximately(0m, .0000000001m);
    }

    [Theory]
    [InlineData(110, 1.0, true, .06)]
    [InlineData(100, 1.1, false, .166)]
    public async Task IndexedFinalExerciseReplacesTheBalloonInInvoiceCashWithoutApplyingIndexationOrFloorsTwice(
        int indexLevel, double redemptionFloor, bool realClean, double expectedYield)
    {
        var bond = WithExerciseTerms(ExerciseBond("European", exercisePrice: 110m), terms =>
        {
            terms["assetClassTermsVersion"] = 5;
            terms["couponType"] = "InflationLinked";
            terms["inflationIndex"] = "CPI";
            terms["inflationBaseIndexValue"] = 100m;
            terms["bondWorkflow"] = new { lagMonths = 0, interpolateMonthly = false, redemptionFloorRatio = (decimal)redemptionFloor };
            terms["principalSchedule"] = new[] { new { paymentDate = "2027-01-01", amount = 40m } };
        });
        var observations = Substitute.For<IBondWorkflowObservationSource>();
        observations.GetAsync("CPI", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), AsOf, Arg.Any<CancellationToken>())
            .Returns(new[] { new BondWorkflowRateObservation("CPI", new(2026, 1, 1), indexLevel,
                "synthetic-index", AsOf, AsOf, new string('d', 64)) });
        var service = Service(bond, InflationCurve(indexLevel), observations: observations);
        var result = (await service.CalculateAsync(new(Id, Context(), CleanPricePer100: 100m,
            PriceBasis: realClean ? BondWorkflowPriceBasis.RealClean : BondWorkflowPriceBasis.NominalClean)))!;

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.CashFlows.Sum(row => row.Principal).Should().Be(110m);
        result.DirtyConsideration.Should().Be(realClean ? 110m : 100m);
        result.ExerciseScenarios.Single().Issue.Should().BeNull();
        // Final cash is 40*1.10 + 60*1.10*1.10 = 116.6; invoice cash is 110 real or 100 nominal.
        result.ExerciseScenarios.Single().Yield.Should().BeApproximately((decimal)expectedYield, .0000000001m);
    }

    [Fact]
    public async Task HolderPutYieldRemainsSeparateAndAnUncoveredIssuerCallStillWithholdsYieldToWorst()
    {
        var put = WithExerciseTerms(ExerciseBond("European"), terms =>
        {
            terms["isCallable"] = false;
            terms.Remove("callDate");
            terms["callSchedule"] = Array.Empty<object>();
            terms["putSchedule"] = new[] { new { putDate = "2027-01-01", putPrice = 90m, exerciseStyle = "European", priceBasis = "PercentOfPar" } };
        });
        var result = (await Service(put).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        result.ExerciseScenarios.Should().ContainSingle().Which.Kind.Should().Be("Put");
        result.ExerciseScenarios.Single().Yield.Should().BeApproximately(-.1m, .0000000001m);
        result.YieldToWorst.Should().BeApproximately(0m, .0000000001m);

        var call = WithExerciseTerms(ExerciseBond("European"), terms =>
        {
            terms["callDate"] = "2026-01-02";
            terms["callSchedule"] = new object[]
            {
                new { callDate = "2026-01-02", callPrice = 100m, exerciseStyle = "European", priceBasis = "PercentOfPar", minimumNoticeDays = 5, noticeDayType = "Calendar" },
                new { callDate = "2027-01-01", callPrice = 100m, exerciseStyle = "European", priceBasis = "PercentOfPar" }
            };
        });
        var uncovered = (await Service(call).CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        uncovered.Issues.Should().NotContain(issue => issue.Code == "ExerciseUnsupported",
            string.Join("; ", uncovered.Issues.Select(issue => issue.Reason)));
        uncovered.ExerciseScenarios.Should().HaveCount(2);
        uncovered.ExerciseScenarios.Should().Contain(row => row.Issue == "NoticeEvidenceRequired");
        uncovered.ExerciseScenarios.Should().Contain(row => row.ExerciseDate == new DateOnly(2027, 1, 1) && row.Issue == null);
        uncovered.YieldToWorst.Should().BeNull("every applicable issuer-call scenario must be covered");
    }

    [Fact]
    public async Task SignedFinalCashWithoutAUniqueRootRemainsAValidForecastWithUnavailableYield()
    {
        var result = (await Service(ExerciseBond("European", coupon: -105m))
            .CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        result.IsBlocked.Should().BeFalse();
        result.CashFlows.Sum(row => row.Total).Should().Be(-5m);
        result.ExerciseScenarios.Single().Yield.Should().BeNull();
        result.ExerciseScenarios.Single().Issue.Should().Be("YieldNoRoot");
        result.YieldToWorst.Should().BeNull();
    }

    [Theory]
    [InlineData(BondWorkflowModelVersions.Legacy)]
    [InlineData(BondWorkflowModelVersions.Previous)]
    public async Task HistoricalExerciseVersionsRetainTheirOriginalDoubleBalloonArithmeticAndExactReplay(string version)
    {
        var service = Service(ExerciseBond("European"));
        var retained = (await service.CalculateAsync(new BondWorkflowRequest(Id, Context(), CleanPricePer100: 100m) { ModelVersion = version }))!;
        retained.ModelVersion.Should().Be(version);
        retained.Yield.Should().BeApproximately(0m, .0000000001m);
        retained.ExerciseScenarios.Single().Yield.Should().BeApproximately(1m, .0000000001m,
            "the historical one-year par exercise retained 100 maturity plus 100 redemption");
        var json = JsonSerializer.Serialize(retained);
        var reopened = JsonSerializer.Deserialize<BondWorkflowResult>(json)!;
        JsonSerializer.Serialize(await Service(ExerciseBond("European")).ReplayAsync(reopened)).Should().Be(json);
        var current = (await service.CalculateAsync(new(Id, Context(), CleanPricePer100: 100m)))!;
        current.ExerciseScenarios.Single().Yield.Should().BeApproximately(0m, .0000000001m);
        current.Inputs.Should().Contain(row => row.Kind == "CalculationModel" && row.Key == "bond-workflow-v3");
        retained.Inputs.Should().Contain(row => row.Kind == "CalculationModel" && row.Key == version);
    }

    private static SecurityDetailDto ExerciseBond(string style, decimal coupon = 0m, decimal exercisePrice = 100m)
        => WithExerciseTerms(Bond(coupon), terms =>
        {
            terms["assetClassTermsVersion"] = 3;
            terms["maturity"] = "2027-01-01";
            terms["couponType"] = coupon == 0m ? "ZeroCoupon" : "Fixed";
            terms["isCallable"] = true;
            terms["callDate"] = style == "Bermudan" ? "2026-07-01" : "2027-01-01";
            terms["callSchedule"] = new[] { new { callDate = style == "Bermudan" ? "2026-07-01" : "2027-01-01",
                callEndDate = "2027-01-01", callPrice = exercisePrice, exerciseStyle = style,
                exerciseFrequency = "SemiAnnual", priceBasis = "PercentOfPar" } };
        });

    private static SecurityDetailDto WithExerciseTerms(SecurityDetailDto security, Action<Dictionary<string, object?>> change)
    {
        var terms = JsonSerializer.Deserialize<Dictionary<string, object?>>(security.AssetSpecificTerms.GetRawText())!;
        change(terms);
        return security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
    }

    private static ISecurityCurveStore InflationCurve(decimal level)
    {
        var store = Substitute.For<ISecurityCurveStore>();
        var id = Guid.Parse("610c497b-6145-4aeb-9530-54ff7289bbfd");
        var versionId = Guid.Parse("50366061-5879-4a42-b16b-ae063b948a76");
        var definition = new SecurityCurveDefinitionDto(id, "SYNTHETIC-CPI", SecurityCurveKind.Inflation,
            "USD", "CPI", null, "ACT/365", SecurityCurveInterpolation.Linear, "synthetic", null, "reviewer", AsOf);
        var version = new SecurityCurveVersionDto(versionId, id, definition.CurveKey, 1, new(2026, 1, 1),
            AsOf, AsOf, "synthetic", "reviewer", [new(new(2026, 1, 1), level), new(new(2027, 1, 1), level)], new string('e', 64));
        store.ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityCurveDefinitionDto>());
        store.ListDefinitionsAsync(SecurityCurveKind.Inflation, "USD", "CPI", Arg.Any<CancellationToken>()).Returns(new[] { definition });
        store.GetLatestVersionAsOfAsync(id, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(version);
        return store;
    }
}
