using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotUnifiedAmortizationTests
{
    [Fact]
    public void OddFirstPeriod_PaysTheContractualStubAndRetainsPrincipalAtMaturity()
    {
        var acquired = new DateOnly(2026, 2, 15);
        var maturity = new DateOnly(2027, 4, 1);
        // 30/360: first stub is 46 days, then four quarterly 1% coupons; zero yield.
        var price = decimal.Round(100m + 4m + 4m * 46m / 360m, 12);
        var instruction = Instruction(acquired, acquired, maturity, new DateOnly(2026, 4, 1), price,
            new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level", firstRegularDate = "2026-04-01" });

        var result = OpenLotAmortization.Project(instruction);

        result.TransactionCostBasis.Should().BeApproximately(104m, 0.0000000001m);
        result.FunctionalCostBasis.Should().BeApproximately(114.4m, 0.0000000001m);
        OpenLotAmortization.Project(instruction with { AsOfDate = maturity }).TransactionCostBasis.Should().Be(100m);
        instruction.CalculationVersion.Should().Be("canonical-lot-amortization-v5");
        var old = () => OpenLotAmortization.Project(instruction with { CalculationVersion = OpenLotAmortization.CalendarModelVersion });
        old.Should().Throw<ArgumentException>().WithMessage("*does not support declared schedule conventions*");
    }

    [Fact]
    public void OddFinalPeriod_PricesRemainingStubAndConvergesToPar()
    {
        var acquired = new DateOnly(2026, 1, 1);
        var maturity = new DateOnly(2027, 2, 15);
        // Four regular quarters plus a 44/360 final stub, rather than a fabricated regular coupon.
        var stub = 4m * 44m / 360m;
        var instruction = Instruction(acquired, acquired, maturity, new DateOnly(2027, 1, 1),
            decimal.Round(104m + stub, 12),
            new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" });

        OpenLotAmortization.Project(instruction).TransactionCostBasis.Should().BeApproximately(100m + stub, 0.0000000001m);
        OpenLotAmortization.Project(instruction with { AsOfDate = maturity }).TransactionCostBasis.Should().Be(100m);
    }

    [Fact]
    public void AcquisitionInsideCouponPeriod_SeparatesPurchasedAccruedInterestFromCleanBasis()
    {
        var acquired = new DateOnly(2026, 2, 15);
        var issue = new DateOnly(2026, 1, 1);
        var maturity = new DateOnly(2027, 1, 1);
        var purchasedAccrued = 4m * 44m / 360m;
        var instruction = Instruction(acquired, issue, maturity, new DateOnly(2026, 4, 1),
            decimal.Round(104m - purchasedAccrued, 12),
            new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" });

        OpenLotAmortization.Project(instruction).TransactionCostBasis.Should().BeApproximately(103m, 0.0000000001m);
        var forged = instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            {
                Acquisition = instruction.ExpectedLot.Acquisition with
                { FaceValueTerms = instruction.ExpectedLot.Acquisition.FaceValueTerms! with { EffectiveYield = 0.05m } }
            }
        };
        var invalid = () => OpenLotAmortization.Project(forged);
        invalid.Should().Throw<ArgumentException>().WithMessage("*yield does not reconcile*");
    }

    [Fact]
    public void DeclaredStraightLine_UsesContractualContextAndHasNoCumulativeRoundingDrift()
    {
        var acquired = new DateOnly(2026, 1, 1);
        var maturity = new DateOnly(2027, 2, 15);
        var instruction = Instruction(acquired, acquired, maturity, new DateOnly(2026, 7, 1), 105m,
            new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "DayCount" });
        instruction = instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            {
                Acquisition = instruction.ExpectedLot.Acquisition with
                { FaceValueTerms = instruction.ExpectedLot.Acquisition.FaceValueTerms! with { AmortizationMethod = BondAmortizationMethod.StraightLine } }
            }
        };
        var first = OpenLotAmortization.Project(instruction);
        var updated = instruction.ExpectedLot with
        { OpenTransactionCostBasis = first.TransactionCostBasis, OpenFunctionalCostBasis = first.FunctionalCostBasis };
        var last = OpenLotAmortization.Project(instruction with { ExpectedLot = updated, AsOfDate = maturity });

        (first.TransactionMovement + last.TransactionMovement).Should().Be(-5m);
        last.TransactionCostBasis.Should().Be(100m);
        last.FunctionalCostBasis.Should().Be(110m);
    }

    private static OpenLotAmortizationInstructionDto Instruction(DateOnly acquired, DateOnly issue, DateOnly maturity,
        DateOnly asOf, decimal price, object conventions)
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(price, 4m, BondAmortizationMethod.ConstantYield, 0m);
        var security = instruction.Security with
        {
            EffectiveFrom = new DateTimeOffset(issue.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            CommonTerms = JsonSerializer.SerializeToElement(new { }),
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                termsFormat = "flat",
                assetClassTermsVersion = 2,
                issueDate = issue.ToString("yyyy-MM-dd"),
                maturity = maturity.ToString("yyyy-MM-dd"),
                couponType = "Fixed",
                couponRate = 4m,
                dayCount = "30/360",
                par = 100m,
                paymentFrequency = "Quarterly",
                isCallable = false,
                subclass = "Corporate",
                scheduleConventions = conventions
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
            { EffectiveDate = acquired, ContentHashSha256 = OpenLotAmortization.SecurityHash(security) },
            AsOfDate = asOf,
            CalculationVersion = OpenLotAmortization.UnifiedDeclaredModelVersion
        };
    }
}
