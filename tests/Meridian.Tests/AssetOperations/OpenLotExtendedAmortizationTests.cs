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
    public void BermudanCallWindow_ContributesEveryRegularGridDateAsItsOwnAlternative()
    {
        // Annual coupon; Bermudan window from 2026-01-01 to maturity 2027-01-01 at 100: two call dates on the grid.
        var instruction = Callable(110m, 10m, 100m, 90m, style: "Bermudan", endDate: "2027-01-01");
        var comparison = OpenLotYieldToWorst.Select(instruction.ExpectedLot, instruction.Security);

        comparison.Alternatives.Where(item => item.Kind == "Call").Select(item => item.Date)
            .Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        comparison.Selected.Kind.Should().Be("Call");
        comparison.Selected.Date.Should().Be(new DateOnly(2026, 1, 1), "the first window date at 100 against a 110 cost is the worst issuer outcome");
        comparison.Selected.AnnualYield.Should().BeApproximately(0m, 0.00000000001m);
        comparison.Alternatives.Single(item => item.Kind == "Call" && item.Date == new DateOnly(2027, 1, 1)).AnnualYield
            .Should().Be(comparison.Alternatives.Single(item => item.Kind == "Maturity").AnnualYield, "a par call at maturity is the maturity yield");
        var retained = OpenLotAmortization.Project(instruction with { YieldToWorst = comparison });
        retained.TransactionCostBasis.Should().Be(100m);
    }

    [Fact]
    public void BermudanWindow_DefaultsItsEndToTheNextRowOrMaturity_AndRefusesEndsPastMaturityOrOffGridCadence()
    {
        var open = Callable(110m, 10m, 100m, 90m, style: "Bermudan");
        OpenLotYieldToWorst.Select(open.ExpectedLot, open.Security).Alternatives.Count(item => item.Kind == "Call").Should().Be(2, "an open window runs to maturity");

        var pastMaturity = Callable(110m, 10m, 100m, 90m, style: "Bermudan", endDate: "2028-01-01");
        var select = () => OpenLotYieldToWorst.Select(pastMaturity.ExpectedLot, pastMaturity.Security);
        select.Should().Throw<ArgumentException>().WithMessage("*past maturity*");

        var offGrid = Callable(110m, 10m, 100m, 90m, style: "Bermudan", endDate: "2027-01-01", exerciseFrequency: "SemiAnnual");
        var semiAnnual = () => OpenLotYieldToWorst.Select(offGrid.ExpectedLot, offGrid.Security);
        semiAnnual.Should().Throw<ArgumentException>("a semi-annual exercise cadence on an annual coupon grid is irregular for the acquisition-price solver");

        var european = Callable(110m, 10m, 100m, 90m, exerciseFrequency: "Annual");
        var cadenceOnEuropean = () => OpenLotYieldToWorst.Select(european.ExpectedLot, european.Security);
        cadenceOnEuropean.Should().Throw<ArgumentException>().WithMessage("*Bermudan windows only*");
    }

    [Fact]
    public void ScheduleRows_AreOrderedChronologically_SoAnAuthoredOrderCannotWidenAWindow()
    {
        // Authored out of order: the European row at maturity comes first. The Bermudan window at 95 must
        // still end the day before 2027-01-01, so it contributes 2026-01-01 only, never a 95 alternative at maturity.
        var reversed = Callable(110m, 10m, 100m, 90m, callSchedule: [CallRow("2027-01-01", 100m), CallRow("2026-01-01", 95m, "Bermudan")]);
        var comparison = OpenLotYieldToWorst.Select(reversed.ExpectedLot, reversed.Security);

        comparison.Alternatives.Where(item => item.Kind == "Call").Select(item => (item.Date, item.PricePercentOfPar))
            .Should().Equal((new DateOnly(2026, 1, 1), 95m), (new DateOnly(2027, 1, 1), 100m));

        var duplicateStarts = Callable(110m, 10m, 100m, 90m, callSchedule: [CallRow("2026-01-01", 100m), CallRow("2026-01-01", 95m, "Bermudan")]);
        var select = () => OpenLotYieldToWorst.Select(duplicateStarts.ExpectedLot, duplicateStarts.Security);
        select.Should().Throw<ArgumentException>().WithMessage("*distinct start dates*");
    }

    [Fact]
    public void OverlappingWindows_AreRefusedBeforeAnyAlternativeIsPriced()
    {
        var overlapping = Callable(110m, 10m, 100m, 90m, callSchedule: [CallRow("2026-01-01", 95m, "Bermudan", endDate: "2027-01-01"), CallRow("2026-07-01", 100m)]);
        var select = () => OpenLotYieldToWorst.Select(overlapping.ExpectedLot, overlapping.Security);
        select.Should().Throw<ArgumentException>().WithMessage("*cannot overlap the next row*");
    }

    [Fact]
    public void ExerciseCadence_MustBeASupportedFrequency_WhateverItsCasing()
    {
        var malformed = Callable(110m, 10m, 100m, 90m, style: "Bermudan", endDate: "2027-01-01", exerciseFrequency: new { });
        var objectCadence = () => OpenLotYieldToWorst.Select(malformed.ExpectedLot, malformed.Security);
        objectCadence.Should().Throw<ArgumentException>().WithMessage("*supported regular frequency*");

        var otherCase = Callable(110m, 10m, 100m, 90m, style: "Bermudan", endDate: "2027-01-01", exerciseFrequency: "SemiAnnual", exerciseFrequencyKey: "ExerciseFrequency");
        var semiAnnual = () => OpenLotYieldToWorst.Select(otherCase.ExpectedLot, otherCase.Security);
        semiAnnual.Should().Throw<ArgumentException>("a differently cased cadence is still the declared cadence, and semi-annual is off the annual grid");
    }

    [Fact]
    public void BermudanWindow_ThatBeganBeforeAcquisition_KeepsItsFutureExerciseDates()
    {
        var instruction = Callable(110m, 10m, 100m, 90m, style: "Bermudan", date: "2020-01-01", endDate: "2027-01-01");
        var acquired = instruction.ExpectedLot.AcquiredDate;
        var comparison = OpenLotYieldToWorst.Select(instruction.ExpectedLot, instruction.Security);

        var calls = comparison.Alternatives.Where(item => item.Kind == "Call").Select(item => item.Date).ToArray();
        calls.Should().NotBeEmpty().And.OnlyContain(date => date > acquired);
        calls.Should().Contain(new DateOnly(2027, 1, 1));
        var elapsed = Callable(110m, 10m, 100m, 90m, style: "Bermudan", date: "2020-01-01", endDate: acquired.ToString("yyyy-MM-dd"));
        var select = () => OpenLotYieldToWorst.Select(elapsed.ExpectedLot, elapsed.Security);
        select.Should().Throw<ArgumentException>().WithMessage("*within the lot's life*");
    }

    [Fact]
    public void HistoricalJsonOmitsExtendedPolicyAndSelection()
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);
        var json = JsonSerializer.Serialize(instruction);
        json.Should().NotContain("AmortizeRemainingFace").And.NotContain("YieldToWorst");
    }

    private static Dictionary<string, object> CallRow(string date, decimal callPrice, string style = "European", string? endDate = null, string priceType = "Fixed")
    {
        var call = new Dictionary<string, object> { ["callDate"] = date, ["callPrice"] = callPrice, ["exerciseStyle"] = style, ["priceBasis"] = "PercentOfPar", ["priceType"] = priceType };
        if (endDate is not null)
            call["callEndDate"] = endDate;
        return call;
    }

    private static OpenLotAmortizationInstructionDto Callable(decimal price, decimal coupon, decimal callPrice, decimal putPrice,
        string priceType = "Fixed", string style = "European", string basis = "PercentOfPar", string date = "2026-01-01",
        string? endDate = null, object? exerciseFrequency = null, string exerciseFrequencyKey = "exerciseFrequency", Dictionary<string, object>[]? callSchedule = null)
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(price, coupon, BondAmortizationMethod.ConstantYield, 0.05m);
        var call = new Dictionary<string, object> { ["callDate"] = date, ["callPrice"] = callPrice, ["exerciseStyle"] = style, ["priceBasis"] = basis, ["priceType"] = priceType };
        if (endDate is not null)
            call["callEndDate"] = endDate;
        if (exerciseFrequency is not null)
            call[exerciseFrequencyKey] = exerciseFrequency;
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
                callSchedule = callSchedule ?? new[] { call },
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
