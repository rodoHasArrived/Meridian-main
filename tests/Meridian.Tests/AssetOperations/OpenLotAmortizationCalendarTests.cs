using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotAmortizationCalendarTests
{
    private const decimal AnnualYield = 0.06m;
    private static readonly string[] ActualConventions = ["Actual/360", "Actual/365", "Actual/Actual ISDA"];

    public static IEnumerable<object[]> RegularSchedules()
    {
        foreach (var convention in ActualConventions)
            foreach (var frequency in new[] { 1, 2, 4, 12 })
                foreach (var coupon in new[] { 9m, 3m })
                    yield return [convention, frequency, coupon];
    }

    [Theory]
    [MemberData(nameof(RegularSchedules))]
    public void ActualConventions_CountCalendarCoupons_AndMatchRemainingCashFlowValue(
        string convention, int frequency, decimal coupon)
    {
        // The interval crosses leap year 2024: its Actual year fraction times frequency
        // is not its contractual coupon count. Price the known two-year payment stream.
        var acquired = new DateOnly(2023, 7, 1);
        var maturity = new DateOnly(2025, 7, 1);
        var periods = 2 * frequency;
        var price = PresentValue(coupon, frequency, periods);
        var instruction = Instruction(acquired, maturity, acquired.AddMonths(12 / frequency),
            convention, frequency, coupon, price);

        for (var paid = 1; paid <= periods; paid++)
        {
            var result = OpenLotAmortization.Project(instruction with { AsOfDate = acquired.AddMonths(paid * 12 / frequency) });
            var expected = PresentValue(coupon, frequency, periods - paid);
            result.TransactionCostBasis.Should().BeApproximately(expected, 0.00000000001m);
            result.FunctionalCostBasis.Should().BeApproximately(expected * 1.1m, 0.00000000002m);
        }

        OpenLotAmortization.Project(instruction with { AsOfDate = maturity })
            .TransactionCostBasis.Should().Be(100m);
    }

    [Theory]
    [InlineData("Actual/360")]
    [InlineData("Actual/365")]
    [InlineData("Actual/Actual ISDA")]
    public void PartialPeriod_UsesItsOwnCouponBoundariesAcrossCalendarYears(string convention)
    {
        var acquired = new DateOnly(2023, 10, 1);
        var maturity = new DateOnly(2025, 10, 1);
        var instruction = Instruction(acquired, maturity, new DateOnly(2025, 1, 1),
            convention, 2, 9m, PresentValue(9m, 2, 4));
        // Current coupon period is Oct 1 2024 to Apr 1 2025: 92 days in leap year
        // 2024 and 90 days in 2025. ISDA weights those calendar-year segments separately.
        var weight = convention == "Actual/Actual ISDA"
            ? (92m / 366m) / (92m / 366m + 90m / 365m)
            : 92m / 182m;
        var startBasis = PresentValue(9m, 2, 2);
        var endBasis = PresentValue(9m, 2, 1);
        var expected = startBasis + (endBasis - startBasis) * weight;

        var result = OpenLotAmortization.Project(instruction);

        result.TransactionCostBasis.Should().BeApproximately(expected, 0.00000000001m);
        result.FunctionalCostBasis.Should().BeApproximately(expected * 1.1m, 0.00000000002m);
    }

    public static IEnumerable<object[]> EndOfMonthSchedules()
    {
        foreach (var convention in ActualConventions)
        {
            yield return [convention, "2024-01-31", "2025-01-31", "2024-02-29", 12, 12, 1];
            yield return [convention, "2024-01-31", "2025-01-31", "2024-03-31", 12, 12, 2];
            yield return [convention, "2023-08-31", "2025-08-31", "2024-02-29", 2, 4, 1];
            yield return [convention, "2023-11-30", "2024-11-30", "2024-02-29", 4, 4, 1];
            yield return [convention, "2024-02-29", "2027-02-28", "2025-02-28", 1, 3, 1];
            // A regular day-30 schedule clips February but recovers day 30 in March.
            yield return [convention, "2024-01-30", "2024-04-30", "2024-02-29", 12, 3, 1];
            yield return [convention, "2024-01-30", "2024-04-30", "2024-03-30", 12, 3, 2];
        }
    }

    [Theory]
    [MemberData(nameof(EndOfMonthSchedules))]
    public void RegularSchedules_PreserveMonthEndLeapDaysAndContractualDay(
        string convention, string acquired, string maturity, string asOf, int frequency, int periods, int paid)
    {
        var instruction = Instruction(Date(acquired), Date(maturity), Date(asOf), convention,
            frequency, 9m, PresentValue(9m, frequency, periods));

        var result = OpenLotAmortization.Project(instruction);

        result.TransactionCostBasis.Should().BeApproximately(PresentValue(9m, frequency, periods - paid), 0.00000000001m);
        OpenLotAmortization.Project(instruction with { AsOfDate = Date(maturity) })
            .TransactionCostBasis.Should().Be(100m);
    }

    [Theory]
    [InlineData("Actual/360")]
    [InlineData("Actual/365")]
    [InlineData("Actual/Actual ISDA")]
    public void SharedKernel_SolvesYieldFromTheSameCalendarCashFlows(string convention)
    {
        var acquired = new DateOnly(2023, 7, 1);
        var lot = new FaceValueLot("actual-calendar", Guid.NewGuid(), acquired, 100m, PresentValue(9m, 2, 4));

        var result = lot.ConstantYieldAmortizedBasisAsOf(DayCountConventions.Parse(convention),
            new DateOnly(2025, 7, 1), new DateOnly(2024, 1, 1), 9m, 2);

        result.Should().BeApproximately(PresentValue(9m, 2, 3), 0.00000000001m);
    }

    [Theory]
    [InlineData("Actual/360")]
    [InlineData("Actual/365")]
    [InlineData("Actual/Actual ISDA")]
    public void FinalPartialCoupon_ConvergesToParUsingTheActualFinalPeriod(string convention)
    {
        var maturity = new DateOnly(2025, 7, 31);
        var instruction = Instruction(new DateOnly(2024, 7, 31), maturity, maturity.AddDays(-1),
            convention, 12, 9m, PresentValue(9m, 12, 12));
        // June 30 to July 31 is 31 days, with 30 elapsed on July 30.
        var lastCouponBasis = PresentValue(9m, 12, 1);
        var expected = lastCouponBasis + (100m - lastCouponBasis) * 30m / 31m;

        var result = OpenLotAmortization.Project(instruction);

        result.TransactionCostBasis.Should().BeApproximately(expected, 0.00000000001m);
        result.TransactionCostBasis.Should().BeGreaterThan(100m).And.BeLessThan(lastCouponBasis);
        OpenLotAmortization.Project(instruction with { AsOfDate = maturity })
            .TransactionCostBasis.Should().Be(100m);
    }

    [Theory]
    [InlineData("2025-01-01", "2025-12-27", 1, "Actual/360")]
    [InlineData("2025-01-01", "2025-05-01", 4, "Actual/365")]
    [InlineData("2025-01-15", "2026-01-14", 1, "Actual/Actual ISDA")]
    [InlineData("2024-01-31", "2024-03-30", 12, "Actual/365")]
    public void OddCalendarSchedules_AreRefusedEvenWhenTheDayCountSuggestsWholePeriods(
        string acquired, string maturity, int frequency, string convention)
    {
        // The first example is precisely 360 days, previously admitted as one annual coupon.
        var instruction = Instruction(Date(acquired), Date(maturity), Date(maturity), convention,
            frequency, 0m, 100m, annualYield: 0m);

        var project = () => OpenLotAmortization.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*calendar coupon*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(12)]
    public void CalendarSchedule_Accepts1200CouponsAndRejects1201(int frequency)
    {
        var acquired = new DateOnly(2024, 1, 1);
        var lastAllowed = acquired.AddMonths(1200 * 12 / frequency);
        var instruction = Instruction(acquired, lastAllowed, lastAllowed, "Actual/365", frequency,
            0m, 100m, annualYield: 0m);

        OpenLotAmortization.Project(instruction).TransactionCostBasis.Should().Be(100m);

        var tooMany = lastAllowed.AddMonths(12 / frequency);
        var invalid = Instruction(acquired, tooMany, tooMany, "Actual/365", frequency,
            0m, 100m, annualYield: 0m);
        var project = () => OpenLotAmortization.Project(invalid);
        project.Should().Throw<ArgumentException>().WithMessage("*one to 1200*");
    }

    [Fact]
    public void CalendarSchedule_StillRequiresTheRetainedYieldToPriceEveryCouponAtMaturity()
    {
        var instruction = Instruction(new DateOnly(2023, 7, 1), new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 1),
            "Actual/360", 2, 9m, PresentValue(9m, 2, 3));

        var project = () => OpenLotAmortization.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*yield does not reconcile*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalInstruction_DeserializesWithoutVersion_AndRetainsItsExactJsonAndCalculation(bool webDefaults)
    {
        var instruction = Instruction(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2025, 7, 1),
            "Actual/365", 2, 10m, 110m, annualYield: 0m);
        var options = new JsonSerializerOptions(webDefaults ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);
        // Reproduce the original five-property shape, independently of the new DTO serializer.
        var historicalJson = JsonSerializer.Serialize(new
        {
            instruction.ExpectedLot,
            instruction.Security,
            instruction.SecurityEvidence,
            instruction.ExpectedBookPositionVersion,
            instruction.AsOfDate
        }, options);

        var restored = JsonSerializer.Deserialize<OpenLotAmortizationInstructionDto>(historicalJson, options)!;
        var historical = OpenLotAmortization.Project(restored);
        var current = OpenLotAmortization.Project(instruction);

        restored.CalculationVersion.Should().BeNull();
        JsonSerializer.Serialize(restored, options).Should().Be(historicalJson,
            "historical commands must retain their serialized fingerprint input");
        historical.TransactionCostBasis.Should().Be(105.041095890411m);
        historical.TransactionMovement.Should().Be(-4.958904109589m);
        instruction.CalculationVersion.Should().Be("canonical-lot-amortization-v2");
        current.TransactionCostBasis.Should().Be(105m);
        current.TransactionMovement.Should().Be(-5m);
        var currentJson = JsonSerializer.Serialize(instruction, options);
        JsonSerializer.Deserialize<OpenLotAmortizationInstructionDto>(currentJson, options)!.CalculationVersion
            .Should().Be(OpenLotAmortization.ModelVersion);
    }

    [Fact]
    public void HistoricalInstruction_PreservesPreviouslyAccepted360DaySchedule()
    {
        var instruction = Instruction(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 27), new DateOnly(2025, 7, 1),
            "Actual/360", 1, 10m, 110m, annualYield: 0m);
        var historical = instruction with { CalculationVersion = null };

        OpenLotAmortization.Project(historical).TransactionMovement.Should().Be(-5.027777777778m);
        var current = () => OpenLotAmortization.Project(instruction);
        current.Should().Throw<ArgumentException>().WithMessage("*calendar coupon*");
    }

    [Fact]
    public void HistoricalInstruction_PreservesPriorRefusalOfFractionalDayCountPeriods()
    {
        var instruction = Instruction(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2025, 7, 1),
            "Actual/360", 2, 10m, 110m, annualYield: 0m);
        var historical = () => OpenLotAmortization.Project(instruction with { CalculationVersion = null });

        historical.Should().Throw<ArgumentException>().WithMessage("*one to 1200 level coupon periods*");
        OpenLotAmortization.Project(instruction).TransactionMovement.Should().Be(-5m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("canonical-lot-amortization-v1")]
    [InlineData("canonical-lot-amortization-v3")]
    public void UnsupportedCalculationVersion_IsRefused(string version)
    {
        var instruction = Instruction(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2025, 7, 1),
            "Actual/365", 2, 10m, 110m, annualYield: 0m) with
        { CalculationVersion = version };

        var project = () => OpenLotAmortization.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*calculation version is unsupported*");
    }

    // Independent annuity present value, expressed per 100 face. The production kernel
    // discounts each coupon individually and rolls carrying value forward.
    private static decimal PresentValue(decimal couponPercent, int frequency, int remainingPeriods)
    {
        var yield = AnnualYield / frequency;
        var accumulation = 1m;
        for (var period = 0; period < remainingPeriods; period++)
            accumulation *= 1m + yield;
        var discount = 1m / accumulation;
        return 100m * discount + couponPercent / frequency * (1m - discount) / yield;
    }

    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static OpenLotAmortizationInstructionDto Instruction(DateOnly acquired, DateOnly maturity, DateOnly asOf,
        string convention, int frequency, decimal coupon, decimal price, decimal annualYield = AnnualYield)
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(decimal.Round(price, 12), coupon,
            BondAmortizationMethod.ConstantYield, annualYield);
        var security = instruction.Security with
        {
            EffectiveFrom = new DateTimeOffset(acquired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            CommonTerms = JsonSerializer.SerializeToElement(new
            {
                maturityDate = maturity.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                dayCountConvention = convention,
                couponRate = coupon,
                paymentFrequency = frequency.ToString(CultureInfo.InvariantCulture),
                couponType = coupon == 0m ? "ZeroCoupon" : "Fixed",
                isCallable = false
            })
        };
        var acquisition = instruction.ExpectedLot.Acquisition with
        {
            HoldingPeriodStartDate = acquired,
            Evidence = instruction.ExpectedLot.Acquisition.Evidence.Select(item => item with { EffectiveDate = acquired }).ToArray()
        };
        return instruction with
        {
            ExpectedLot = instruction.ExpectedLot with { AcquiredDate = acquired, Acquisition = acquisition },
            Security = security,
            SecurityEvidence = instruction.SecurityEvidence with
            {
                EffectiveDate = acquired,
                ContentHashSha256 = OpenLotAmortization.SecurityHash(security)
            },
            AsOfDate = asOf
        };
    }
}
