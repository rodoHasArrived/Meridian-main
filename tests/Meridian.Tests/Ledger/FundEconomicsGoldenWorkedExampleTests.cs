using System.Text.Json;
using FluentAssertions;
using Meridian.FSharp.Ledger;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Golden-file coverage for the fund-economics calculation kernels against hand-computed worked
/// examples in <c>tests/fixtures/fund-economics/golden/</c>. Every expected figure in the fixture
/// was derived independently of the implementation, and the threaded series (performance-fee
/// high-water cycle, multi-distribution waterfall) assert each step so state transitions are pinned
/// end to end. A malformed or shrunken fixture is a test failure by design.
/// </summary>
public sealed class FundEconomicsGoldenWorkedExampleTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<JsonDocument> Fixture = new(LoadFixture);

    private static JsonElement Root => Fixture.Value.RootElement;

    [Fact]
    public void Fixture_CoversEveryKernelSection()
    {
        Root.GetProperty("managementFeeAccruals").GetArrayLength().Should().BeGreaterThan(0);
        Root.GetProperty("expenseAccruals").GetArrayLength().Should().BeGreaterThan(0);
        Root.GetProperty("performanceFeeSeries").GetProperty("periods").GetArrayLength().Should().BeGreaterThan(1,
            "the high-water cycle is only proven by a threaded multi-period series");
        Root.GetProperty("waterfallSeries").GetProperty("distributions").GetArrayLength().Should().BeGreaterThan(1,
            "cumulative waterfall tiers are only proven by a threaded multi-distribution series");
        Root.GetProperty("equalizations").GetArrayLength().Should().BeGreaterThan(0);
        Root.GetProperty("unitRegisterScenario").GetProperty("transactions").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ManagementFeeAccruals_MatchWorkedExamples()
    {
        foreach (var example in Root.GetProperty("managementFeeAccruals").EnumerateArray())
        {
            var fee = FundEconomics.managementFeeAccrual(
                example.GetProperty("netAssets").GetDecimal(),
                example.GetProperty("annualRate").GetDecimal(),
                example.GetProperty("accrualDays").GetInt32(),
                example.GetProperty("yearBasisDays").GetInt32());

            fee.Should().Be(
                example.GetProperty("expectedFee").GetDecimal(),
                because: "worked example '{0}' fixes the day-weighted management fee",
                example.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void ExpenseAccruals_MatchWorkedExamples()
    {
        foreach (var example in Root.GetProperty("expenseAccruals").EnumerateArray())
        {
            var accrual = FundEconomics.expenseAccrual(
                example.GetProperty("annualAmount").GetDecimal(),
                example.GetProperty("accrualDays").GetInt32(),
                example.GetProperty("yearBasisDays").GetInt32());

            accrual.Should().Be(
                example.GetProperty("expectedAccrual").GetDecimal(),
                because: "worked example '{0}' fixes the straight-line expense accrual",
                example.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void PerformanceFeeSeries_ThreadsHighWaterMarkThroughCrystallizationCycle()
    {
        var series = Root.GetProperty("performanceFeeSeries");
        var rate = series.GetProperty("performanceFeeRate").GetDecimal();
        var highWaterMark = series.GetProperty("openingHighWaterMark").GetDecimal();

        foreach (var period in series.GetProperty("periods").EnumerateArray())
        {
            var periodId = period.GetProperty("id").GetString();
            var accrual = FundEconomics.performanceFeeAccrual(
                period.GetProperty("endingNavBeforeFee").GetDecimal(),
                highWaterMark,
                period.GetProperty("hurdleAmount").GetDecimal(),
                rate,
                period.GetProperty("crystallize").GetBoolean());

            var expected = period.GetProperty("expected");
            accrual.GrossProfit.Should().Be(expected.GetProperty("grossProfit").GetDecimal(),
                because: "period '{0}' fixes appreciation above the high-water mark", periodId);
            accrual.FeeBase.Should().Be(expected.GetProperty("feeBase").GetDecimal(),
                because: "period '{0}' fixes the post-hurdle fee base", periodId);
            accrual.PerformanceFee.Should().Be(expected.GetProperty("performanceFee").GetDecimal(),
                because: "period '{0}' fixes the accrued fee", periodId);
            accrual.CrystallizedFee.Should().Be(expected.GetProperty("crystallizedFee").GetDecimal(),
                because: "period '{0}' fixes the crystallized fee", periodId);
            accrual.NewHighWaterMark.Should().Be(expected.GetProperty("newHighWaterMark").GetDecimal(),
                because: "period '{0}' fixes the carried high-water mark", periodId);

            highWaterMark = accrual.NewHighWaterMark;
        }
    }

    [Fact]
    public void WaterfallSeries_ThreadsCumulativeTiersAcrossDistributions()
    {
        var series = Root.GetProperty("waterfallSeries");
        var carryRate = series.GetProperty("carryRate").GetDecimal();
        var catchUpRate = series.GetProperty("catchUpRate").GetDecimal();
        var contributedCapital = series.GetProperty("contributedCapital").GetDecimal();
        var preferredReturnAccrued = series.GetProperty("preferredReturnAccrued").GetDecimal();

        var priorReturnOfCapital = 0m;
        var priorPreferredPaid = 0m;
        var priorGpCatchUp = 0m;
        var priorCatchUpPool = 0m;
        var cumulativeLp = 0m;
        var cumulativeGp = 0m;
        var cumulativeDistributed = 0m;

        foreach (var distribution in series.GetProperty("distributions").EnumerateArray())
        {
            var distributionId = distribution.GetProperty("id").GetString();
            var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
                contributedCapital,
                preferredReturnAccrued,
                distribution.GetProperty("amountToDistribute").GetDecimal(),
                carryRate,
                catchUpRate,
                priorReturnOfCapital,
                priorPreferredPaid,
                priorGpCatchUp,
                priorCatchUpPool));

            var expected = distribution.GetProperty("expected");
            result.ReturnOfCapital.Should().Be(expected.GetProperty("returnOfCapital").GetDecimal(),
                because: "distribution '{0}' fixes the return-of-capital tier", distributionId);
            result.PreferredReturn.Should().Be(expected.GetProperty("preferredReturn").GetDecimal(),
                because: "distribution '{0}' fixes the preferred-return tier", distributionId);
            result.GpCatchUp.Should().Be(expected.GetProperty("gpCatchUp").GetDecimal(),
                because: "distribution '{0}' fixes the GP catch-up", distributionId);
            result.LpCatchUp.Should().Be(expected.GetProperty("lpCatchUp").GetDecimal(),
                because: "distribution '{0}' fixes the LP catch-up leakage", distributionId);
            result.LpCarry.Should().Be(expected.GetProperty("lpCarry").GetDecimal(),
                because: "distribution '{0}' fixes the LP carry split", distributionId);
            result.GpCarry.Should().Be(expected.GetProperty("gpCarry").GetDecimal(),
                because: "distribution '{0}' fixes the GP carry split", distributionId);
            result.LpTotal.Should().Be(expected.GetProperty("lpTotal").GetDecimal(),
                because: "distribution '{0}' fixes the LP total", distributionId);
            result.GpTotal.Should().Be(expected.GetProperty("gpTotal").GetDecimal(),
                because: "distribution '{0}' fixes the GP total", distributionId);
            result.Distributed.Should().Be(
                distribution.GetProperty("amountToDistribute").GetDecimal(),
                because: "distribution '{0}' must pay out exactly what was distributed", distributionId);

            priorReturnOfCapital += result.ReturnOfCapital;
            priorPreferredPaid += result.PreferredReturn;
            priorGpCatchUp += result.GpCatchUp;
            priorCatchUpPool += result.GpCatchUp + result.LpCatchUp;
            cumulativeLp += result.LpTotal;
            cumulativeGp += result.GpTotal;
            cumulativeDistributed += result.Distributed;
        }

        var cumulativeExpected = series.GetProperty("cumulativeExpected");
        cumulativeGp.Should().Be(cumulativeExpected.GetProperty("gpTotal").GetDecimal());
        cumulativeLp.Should().Be(cumulativeExpected.GetProperty("lpTotal").GetDecimal());
        var profitAboveCapital = cumulativeDistributed - contributedCapital;
        (cumulativeGp / profitAboveCapital).Should().Be(
            cumulativeExpected.GetProperty("gpShareOfProfit").GetDecimal(),
            because: "across the fund's life the GP receives exactly its carry share of profit");
    }

    [Fact]
    public void PartialCatchUpWaterfall_MatchesWorkedExample()
    {
        var example = Root.GetProperty("partialCatchUpWaterfall");
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            example.GetProperty("contributedCapital").GetDecimal(),
            example.GetProperty("preferredReturnAccrued").GetDecimal(),
            example.GetProperty("amountToDistribute").GetDecimal(),
            example.GetProperty("carryRate").GetDecimal(),
            example.GetProperty("catchUpRate").GetDecimal()));

        var expected = example.GetProperty("expected");
        result.ReturnOfCapital.Should().Be(expected.GetProperty("returnOfCapital").GetDecimal());
        result.PreferredReturn.Should().Be(expected.GetProperty("preferredReturn").GetDecimal());
        result.GpCatchUp.Should().Be(expected.GetProperty("gpCatchUp").GetDecimal());
        result.LpCatchUp.Should().Be(expected.GetProperty("lpCatchUp").GetDecimal());
        result.LpCarry.Should().Be(expected.GetProperty("lpCarry").GetDecimal());
        result.GpCarry.Should().Be(expected.GetProperty("gpCarry").GetDecimal());
        result.LpTotal.Should().Be(expected.GetProperty("lpTotal").GetDecimal());
        result.GpTotal.Should().Be(expected.GetProperty("gpTotal").GetDecimal());
    }

    [Fact]
    public void Equalizations_MatchWorkedExamples()
    {
        foreach (var example in Root.GetProperty("equalizations").EnumerateArray())
        {
            var adjustment = EqualizationCalculator.Compute(
                example.GetProperty("navPerUnit").GetDecimal(),
                example.GetProperty("highWaterNavPerUnit").GetDecimal(),
                example.GetProperty("subscriptionUnits").GetDecimal(),
                example.GetProperty("performanceFeeRate").GetDecimal());

            adjustment.EqualisationCredit.Should().Be(
                example.GetProperty("expectedEqualisationCredit").GetDecimal(),
                because: "worked example '{0}' fixes the equalisation credit",
                example.GetProperty("id").GetString());
            adjustment.ContingentRedemption.Should().Be(
                example.GetProperty("expectedContingentRedemption").GetDecimal(),
                because: "worked example '{0}' fixes the contingent redemption",
                example.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void UnitRegisterScenario_ReproducesWorkedExample()
    {
        var scenario = Root.GetProperty("unitRegisterScenario");
        var classElement = scenario.GetProperty("shareClass");
        var shareClass = new ShareClass(
            classElement.GetProperty("shareClassId").GetString()!,
            classElement.GetProperty("fundProfileId").GetString()!,
            classElement.GetProperty("name").GetString()!,
            classElement.GetProperty("currency").GetString()!,
            classElement.GetProperty("managementFeeRateAnnual").GetDecimal(),
            classElement.GetProperty("performanceFeeRate").GetDecimal(),
            classElement.GetProperty("inceptionNavPerUnit").GetDecimal(),
            DateOnly.Parse(classElement.GetProperty("inceptionDate").GetString()!),
            classElement.GetProperty("equalising").GetBoolean()
                ? EqualizationMethod.Equalisation
                : EqualizationMethod.None);

        var transactions = scenario.GetProperty("transactions").EnumerateArray()
            .Select(transaction => new UnitTransaction(
                transaction.GetProperty("transactionId").GetString()!,
                shareClass.ShareClassId,
                transaction.GetProperty("investorId").GetString()!,
                DateOnly.Parse(transaction.GetProperty("dealingDate").GetString()!),
                Enum.Parse<UnitTransactionType>(transaction.GetProperty("type").GetString()!),
                transaction.GetProperty("navPerUnit").GetDecimal(),
                amount: transaction.TryGetProperty("amount", out var amount) ? amount.GetDecimal() : 0m,
                units: transaction.TryGetProperty("units", out var units) ? units.GetDecimal() : 0m))
            .ToList();

        var register = ShareClassUnitRegisterProjector.Project(
            shareClass,
            transactions,
            DateOnly.Parse(scenario.GetProperty("asOf").GetString()!),
            scenario.GetProperty("classNav").GetDecimal());

        var expected = scenario.GetProperty("expected");
        register.ValidationIssues.Should().BeEmpty();
        register.UnitsOutstanding.Should().Be(expected.GetProperty("unitsOutstanding").GetDecimal());
        register.NavPerUnit.Should().Be(expected.GetProperty("navPerUnit").GetDecimal());
        register.HighWaterNavPerUnit.Should().Be(expected.GetProperty("highWaterNavPerUnit").GetDecimal());
        if (expected.GetProperty("aggregateHoldingValueTiesToClassNav").GetBoolean())
        {
            register.AggregateHoldingValue.Should().Be(register.ClassNav,
                because: "the unitized holdings must tie back to the class NAV they were priced from");
        }

        var expectedMovements = expected.GetProperty("movements").EnumerateArray().ToList();
        register.Movements.Should().HaveCount(expectedMovements.Count);
        foreach (var expectedMovement in expectedMovements)
        {
            var transactionId = expectedMovement.GetProperty("transactionId").GetString();
            var movement = register.Movements.Should()
                .ContainSingle(item => item.TransactionId == transactionId).Which;
            movement.Units.Should().Be(expectedMovement.GetProperty("units").GetDecimal(),
                because: "movement '{0}' fixes its issued or cancelled units", transactionId);
            if (expectedMovement.TryGetProperty("amount", out var expectedAmount))
                movement.Amount.Should().Be(expectedAmount.GetDecimal(),
                    because: "movement '{0}' fixes its cash amount", transactionId);
            if (expectedMovement.TryGetProperty("equalisationCredit", out var expectedCredit))
                movement.EqualisationCredit.Should().Be(expectedCredit.GetDecimal(),
                    because: "movement '{0}' fixes its equalisation credit", transactionId);
            if (expectedMovement.TryGetProperty("contingentRedemption", out var expectedContingent))
                movement.ContingentRedemption.Should().Be(expectedContingent.GetDecimal(),
                    because: "movement '{0}' fixes its contingent redemption", transactionId);
        }

        var expectedHoldings = expected.GetProperty("holdings").EnumerateArray().ToList();
        register.Holdings.Should().HaveCount(expectedHoldings.Count);
        foreach (var expectedHolding in expectedHoldings)
        {
            var investorId = expectedHolding.GetProperty("investorId").GetString();
            var holding = register.Holdings.Should()
                .ContainSingle(item => item.InvestorId == investorId).Which;
            holding.Units.Should().Be(expectedHolding.GetProperty("units").GetDecimal());
            holding.SubscribedAmount.Should().Be(expectedHolding.GetProperty("subscribedAmount").GetDecimal());
            holding.RedeemedAmount.Should().Be(expectedHolding.GetProperty("redeemedAmount").GetDecimal());
            holding.EqualisationCredit.Should().Be(expectedHolding.GetProperty("equalisationCredit").GetDecimal());
            holding.ContingentRedemption.Should().Be(expectedHolding.GetProperty("contingentRedemption").GetDecimal());
        }
    }

    /// <summary>
    /// Prefers the fixture copied beside the test binaries (csproj None item with
    /// CopyToOutputDirectory), falling back to a repo-root walk for ad hoc runs, mirroring
    /// the golden corporate-action loader.
    /// </summary>
    private static JsonDocument LoadFixture()
    {
        var copied = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "fund-economics", "golden", "fund-economics-worked-examples.json");
        var path = File.Exists(copied) ? copied : WalkToRepoFixture();
        using var stream = File.OpenRead(path);
        return JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
    }

    private static string WalkToRepoFixture()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName, "tests", "fixtures", "fund-economics", "golden", "fund-economics-worked-examples.json");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "Golden fund-economics fixture 'fund-economics-worked-examples.json' was not found beside the test binaries or in the repository tree.");
    }
}
