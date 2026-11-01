using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotExtendedAmortizationTests
{
    [Fact]
    public void V3WithoutReviewedTreatment_IsRefused()
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m,
            BondAmortizationMethod.StraightLine, null) with
        { CalculationVersion = OpenLotAmortization.ExtendedModelVersion };
        var project = () => OpenLotAmortization.Project(instruction);
        project.Should().Throw<ArgumentException>().WithMessage("*explicit v3 reviewed instruction*");
    }

    [Theory]
    [InlineData(90, 95)]
    [InlineData(110, 105)]
    public void RemainingFace_AmortizesOnlySurvivingBasisAndRetainsAcquisitionFx(int price, int fullTarget)
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(price, 10m,
            BondAmortizationMethod.StraightLine, null);
        var lot = instruction.ExpectedLot;
        instruction = instruction with
        {
            CalculationVersion = OpenLotAmortization.ExtendedModelVersion,
            AmortizeRemainingFace = true,
            ExpectedLot = lot with
            {
                CurrentFactor = 0.5m,
                OpenTransactionCostBasis = price / 2m,
                OpenFunctionalCostBasis = price * 1.1m / 2m
            }
        };
        var result = OpenLotAmortization.Project(instruction);
        result.TransactionCostBasis.Should().Be(fullTarget / 2m);
        result.FunctionalCostBasis.Should().Be(fullTarget / 2m * 1.1m);
        var again = instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            { OpenTransactionCostBasis = result.TransactionCostBasis, OpenFunctionalCostBasis = result.FunctionalCostBasis, Version = 2 }
        };
        OpenLotAmortization.Project(again).FunctionalMovement.Should().Be(0m);
        var legacy = () => OpenLotAmortization.Project(instruction with { CalculationVersion = OpenLotAmortization.ModelVersion });
        legacy.Should().Throw<ArgumentException>().WithMessage("*explicit v3*");
    }

    [Theory]
    [InlineData(90)]
    [InlineData(120)]
    public void YieldToWorst_SelectsIssuerCallAndValuesHolderPutSeparately(int putPrice)
    {
        var instruction = Callable(110m, 10m, 100m, putPrice);
        var selected = OpenLotYieldToWorst.Select(instruction.ExpectedLot, instruction.Security);
        selected.Selected.Kind.Should().Be("Call");
        selected.Selected.Date.Should().Be(new DateOnly(2026, 1, 1));
        selected.Selected.AnnualYield.Should().BeApproximately(0m, 0.00000000001m);
        selected.Alternatives.Should().ContainSingle(item => item.Kind == "Put");
        selected.Alternatives.Single(item => item.Kind == "Put").AnnualYield
            .Should().BeApproximately((putPrice + 10m) / 110m - 1m, 0.00000000001m);
        instruction = instruction with { YieldToWorst = selected };
        OpenLotAmortization.Project(instruction).TransactionCostBasis.Should().Be(100m);
        var partial = OpenLotAmortization.Project(instruction with { AsOfDate = new DateOnly(2025, 7, 1) });
        partial.TransactionCostBasis.Should().Be(105m);
        partial.FunctionalCostBasis.Should().Be(115.5m);
        var changed = () => OpenLotAmortization.Project(instruction with
        { YieldToWorst = selected with { Selected = selected.Selected with { PricePercentOfPar = 101m } } });
        changed.Should().Throw<ArgumentException>().WithMessage("*differs*");
    }

    [Fact]
    public void DiscountZeroCoupon_ChoosesMaturityRatherThanHigherCallYield()
    {
        var instruction = Callable(90m, 0m, 100m, 100m);
        var comparison = OpenLotYieldToWorst.Select(instruction.ExpectedLot, instruction.Security);
        comparison.Selected.Kind.Should().Be("Maturity");
        comparison.Selected.AnnualYield.Should().BeApproximately((decimal)Math.Sqrt(100d / 90d) - 1m, 0.00000000001m);
        var projected = OpenLotAmortization.Project(instruction with { YieldToWorst = comparison });
        projected.TransactionCostBasis.Should().BeApproximately((decimal)Math.Sqrt(90d * 100d), 0.00000000001m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void NonParCall_PreservesActualCouponAndBothQuoteDenominations(int parBasis)
    {
        var instruction = Callable(110m, 10m, 105m, 90m);
        var lot = instruction.ExpectedLot;
        instruction = instruction with
        {
            ExpectedLot = lot with
            {
                Acquisition = lot.Acquisition with
                { FaceValueTerms = lot.Acquisition.FaceValueTerms! with { ParBasis = parBasis } }
            }
        };
        var comparison = OpenLotYieldToWorst.Select(instruction.ExpectedLot, instruction.Security);
        comparison.Selected.Kind.Should().Be("Call");
        comparison.Selected.AnnualYield.Should().BeApproximately(115m / 110m - 1m, 0.00000000001m);
        var result = OpenLotAmortization.Project(instruction with { YieldToWorst = comparison });
        result.TransactionCostBasis.Should().Be(105m);
        result.FunctionalCostBasis.Should().Be(115.5m);
    }

    [Fact]
    public void RemainingFace_AppliesPartialDisposalAndFactorExactlyOnce()
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m,
            BondAmortizationMethod.StraightLine, null);
        instruction = instruction with
        {
            CalculationVersion = OpenLotAmortization.ExtendedModelVersion,
            AmortizeRemainingFace = true,
            ExpectedLot = instruction.ExpectedLot with
            {
                OpenQuantity = 50m,
                CurrentFactor = 0.5m,
                OpenTransactionCostBasis = 27.5m,
                OpenFunctionalCostBasis = 30.25m
            }
        };
        var result = OpenLotAmortization.Project(instruction);
        result.TransactionCostBasis.Should().Be(26.25m);
        result.FunctionalCostBasis.Should().Be(28.875m);
    }

    [Fact]
    public void DuplicateExerciseMembersCannotHideAdverseAuthority()
    {
        var instruction = Callable(110m, 10m, 100m, 100m);
        var raw = instruction.Security.CommonTerms.GetRawText().Replace("\"exerciseStyle\":\"European\"",
            "\"exerciseStyle\":\"European\",\"ExerciseStyle\":\"American\"", StringComparison.Ordinal);
        var security = instruction.Security with { CommonTerms = JsonDocument.Parse(raw).RootElement.Clone() };
        var select = () => OpenLotYieldToWorst.Select(instruction.ExpectedLot, security);
        select.Should().Throw<ArgumentException>().WithMessage("*duplicate members*");
    }

    [Theory]
    [InlineData("MakeWhole", "European", "PercentOfPar", "2026-01-01")]
    [InlineData("Fixed", "American", "PercentOfPar", "2026-01-01")]
    [InlineData("Fixed", "European", "PercentOfAccretedValue", "2026-01-01")]
    [InlineData("Fixed", "European", "PercentOfPar", "2025-10-01")]
    public void IncompleteOrUnsupportedExerciseEconomicsCannotReceiveBulletDefaults(
        string priceType, string style, string basis, string date)
    {
        var instruction = Callable(110m, 10m, 100m, 100m, priceType, style, basis, date);
        var select = () => OpenLotYieldToWorst.Select(instruction.ExpectedLot, instruction.Security);
        select.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HistoricalJsonOmitsExtendedPolicyAndSelection()
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);
        var json = JsonSerializer.Serialize(instruction);
        json.Should().NotContain("AmortizeRemainingFace").And.NotContain("YieldToWorst");
    }

    private static OpenLotAmortizationInstructionDto Callable(decimal price, decimal coupon, decimal callPrice, decimal putPrice,
        string priceType = "Fixed", string style = "European", string basis = "PercentOfPar", string date = "2026-01-01")
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(price, coupon, BondAmortizationMethod.ConstantYield, 0.05m);
        var security = instruction.Security with
        {
            CommonTerms = JsonSerializer.SerializeToElement(new
            {
                maturityDate = "2027-01-01",
                dayCountConvention = "30/360",
                couponRate = coupon,
                paymentFrequency = "Annual",
                couponType = coupon == 0m ? "ZeroCoupon" : "Fixed",
                isCallable = true,
                callSchedule = new[] { new { callDate = date, callPrice, exerciseStyle = style, priceBasis = basis, priceType } },
                putSchedule = new[] { new { putDate = "2026-01-01", putPrice, exerciseStyle = "European", priceBasis = "PercentOfPar" } }
            })
        };
        return instruction with
        {
            CalculationVersion = OpenLotAmortization.ExtendedModelVersion,
            Security = security,
            SecurityEvidence = instruction.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(security) }
        };
    }
}
