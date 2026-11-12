using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.UnitTests.Accounting;

public sealed class BondIncomeCalculatorTests
{
    [Theory]
    [InlineData("USD", "0.617", "0.62", "0.61")]
    [InlineData("USD", "-0.617", "-0.62", "-0.61")]
    [InlineData("JPY", "0.6", "1", "0")]
    [InlineData("JPY", "-0.6", "-1", "0")]
    [InlineData("KWD", "0.617", "0.617", "0.617")]
    [InlineData("BHD", "-0.617", "-0.617", "-0.617")]
    public void CouponCashRoundsHolderObligationOnce_ThenAllocatesQuantaToLots(string currency,
        string rawText, string firstText, string secondText)
    {
        var raw = decimal.Parse(rawText, System.Globalization.CultureInfo.InvariantCulture);
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var allocation = BondIncomeCouponRounding.Allocate(new(2026, 1, 1), new(2025, 1, 1), new(2026, 1, 1), currency,
            [new(second, 3, 50m, raw, 0m), new(first, 5, 50m, raw, 0m)]);
        allocation.Lots.Single(l => l.TaxLotRecordId == first).CashAmount.Should()
            .Be(decimal.Parse(firstText, System.Globalization.CultureInfo.InvariantCulture));
        allocation.Lots.Single(l => l.TaxLotRecordId == second).CashAmount.Should()
            .Be(decimal.Parse(secondText, System.Globalization.CultureInfo.InvariantCulture));
        allocation.Lots.Sum(l => l.CashAmount).Should().Be(LedgerCurrencyRounding.RoundCurrency(raw * 2m, currency));
        allocation.Lots.Sum(l => l.RawAmount).Should().Be(raw * 2m);
    }

    [Theory]
    [InlineData("1.235", "1.24")]
    [InlineData("-1.235", "-1.24")]
    public void CompletedIncomeUsesAllocatedNativeCash_WhileCarryingMathKeepsRawEconomics(string rawText, string expectedText)
    {
        var raw = decimal.Parse(rawText, System.Globalization.CultureInfo.InvariantCulture);
        var expected = decimal.Parse(expectedText, System.Globalization.CultureInfo.InvariantCulture);
        var f = new Fixture(coupon: raw);
        var first = f.Instruction.CurrentProjection.CashFlows[0];
        var allocation = BondIncomeCouponRounding.Allocate(first.PaymentDate, first.AccrualStart, first.AccrualEnd, "USD",
            [new(f.Lot.TaxLotRecordId, f.Lot.Version, 100m, raw, 0m)]);
        var result = BondIncomeCalculator.Project(f.Lot, f.Instruction with { CouponCashAllocations = [allocation] });
        result.CouponIncome.Should().Be(expected);
        result.CouponCashRoundingAdjustment.Should().Be(expected - raw);
        result.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
        f.Instruction.CurrentProjection.CashFlows[0].Interest.Should().Be(raw);
    }

    [Fact]
    public void OpposingCouponObligationsCannotBeNettedInsideOneCashRoundingPool()
    {
        var action = () => BondIncomeCouponRounding.Allocate(new(2026, 1, 1), new(2025, 1, 1), new(2026, 1, 1), "USD",
            [new(Guid.NewGuid(), 1, 100m, 1.235m, 0m), new(Guid.NewGuid(), 1, 100m, -0.617m, 0m)]);
        action.Should().Throw<ArgumentException>().WithMessage("*one signed obligation*");
    }

    [Fact]
    public void ThreeDecimalNativeCashRemainsExactAtTheBookedTargetBoundary()
    {
        BondIncomeBookedPrecision.RoundTarget(1.234m).Should().Be(1.234m);
        BondIncomeBookedPrecision.Targets(0.123456789012m, 0.123456789012m).BookedTransactionTarget.Should().Be(0.1234567890m);
        BondIncomeBookedPrecision.Targets(0.123456789012m, 0.123456789012m).TransactionRoundingResidual.Should().Be(0.000000000012m);
        var action = () => BondIncomeBookedPrecision.RequireRepresentable(0.123456789012m);
        action.Should().Throw<ArgumentException>().WithMessage("*reviewed rounded opening facts*");
    }

    [Fact]
    public void ParBond_FirstCouponSeparatesCashIncomeFromUnchangedPrincipalBasis()
    {
        var f = new Fixture();
        var result = BondIncomeCalculator.Project(f.Lot, f.Instruction);
        result.CouponIncome.Should().Be(5m);
        result.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
        result.PremiumDiscountIncome.Should().BeApproximately(0m, 0.00000001m);
        result.EffectiveAnnualYield.Should().BeApproximately(0.05m, 0.000000001m);
    }

    [Fact]
    public void ParBond_InterimAccrualDoesNotInventPremiumAmortization()
    {
        var f = new Fixture();
        var accrued = 5m * 182m / 365m;
        var input = f.Instruction with
        {
            ThroughDate = new(2025, 7, 2),
            CurrentAccruedInterest = accrued,
            AmortizationProjection = f.Instruction.AmortizationProjection with { AccruedInterest = accrued }
        };
        var result = BondIncomeCalculator.Project(f.Lot, input);
        result.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
        result.PremiumDiscountIncome.Should().BeApproximately(0m, 0.00000001m);
        result.CouponIncome.Should().BeApproximately(accrued, 0.000000000001m);
    }

    [Fact]
    public void AtMaturityZeroCouponUsesAnnualCompoundYield_AndFinalIncomeAllowsEmptyEndingSnapshot()
    {
        var f = new Fixture(cost: 81m, coupon: 0m);
        var flow = f.Flow(new(2027, 1, 1), 0m, 100m) with
        {
            AccrualStart = f.AcquisitionDate,
            AccrualFraction = 2m,
            PeriodUnits = 1m,
            OutstandingPrincipal = 0m
        };
        var input = f.Instruction with
        {
            ZeroCoupon = true,
            AcquisitionProjection = f.Projection([flow]),
            CurrentProjection = f.Projection([flow]),
            AmortizationProjection = f.Projection([flow])
        };
        var interim = BondIncomeCalculator.Project(f.Lot, input);
        interim.EffectiveAnnualYield.Should().BeApproximately(1m / 9m, 0.000000001m);
        interim.ClosingCarryingBasis.Should().BeApproximately(90m, 0.00000001m);
        interim.PremiumDiscountIncome.Should().BeApproximately(9m, 0.00000001m);
        var mature = BondIncomeCalculator.Project(f.Lot, input with
        {
            ThroughDate = new(2027, 1, 1),
            AmortizationProjection = f.Projection([])
        });
        mature.ClosingCarryingBasis.Should().Be(100m, "principal basis is relieved only by the distinct governed payment workflow");
        mature.PremiumDiscountIncome.Should().Be(19m);
    }

    [Fact]
    public void MidPeriodParAcquisitionKeepsPurchasedAccruedOutsideEarnedIncomeAndBasis()
    {
        var f = new Fixture();
        var acquired = new DateOnly(2025, 7, 2);
        var purchased = 5m * 182m / 365m;
        var evidence = f.Lot.Acquisition.Evidence[0] with { EffectiveDate = acquired };
        var profile = f.Lot.Acquisition.BondIncome! with
        {
            PurchasedAccruedInterest = purchased,
            KnownAt = new(acquired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            Evidence = [evidence]
        };
        var lot = f.Lot with
        {
            AcquiredDate = acquired,
            Acquisition = f.Lot.Acquisition with
            { HoldingPeriodStartDate = acquired, Evidence = [evidence], BondIncome = profile }
        };
        var opening = f.Instruction.AcquisitionProjection with
        {
            Context = f.Instruction.AcquisitionProjection.Context with { SettlementDate = acquired },
            AccruedInterest = purchased,
            SettlementAccrualFraction = 182m / 365m,
            SettlementPeriodAccrualFraction = 1m,
            SettlementAccrualPeriodUnits = 182m / 365m,
            SettlementPeriodUnits = 1m
        };
        var input = f.Instruction with
        {
            AcquisitionDate = acquired,
            AcquisitionProjection = opening,
            AcquisitionAccruedInterest = purchased,
            Evidence = [evidence]
        };
        var result = BondIncomeCalculator.Project(lot, input);
        result.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
        result.PremiumDiscountIncome.Should().BeApproximately(0m, 0.00000001m);
        result.CouponIncome.Should().BeApproximately(5m - purchased, 0.000000000001m);
    }

    [Fact]
    public void DirectCostsAreCapitalizedOnce_WhileExpenseAndTaxRemainDistinct()
    {
        var f = new Fixture(cost: 101m, capitalized: 1m, expense: 2m, tax: 3m);
        var result = BondIncomeCalculator.Project(f.Lot, f.Instruction);
        // Independently solve 101*z^2 - 5*z - 105 = 0, then grow opening basis and pay coupon.
        var z = (5d + Math.Sqrt(42445d)) / 202d;
        result.ClosingCarryingBasis.Should().BeApproximately(101m * (decimal)z - 5m, 0.00000001m);
        result.CapitalizedFees.Should().Be(1m);
        result.ExpensedFees.Should().Be(2m);
        result.WithholdingTax.Should().Be(3m);
        f.Invoking(x => BondIncomeCalculator.Project(x.Lot, x.Instruction with
        { Fees = x.Instruction.Fees.Concat([x.Instruction.Fees[0]]).ToArray() }))
            .Should().Throw<ArgumentException>().WithMessage("*retained acquisition*");
    }

    [Fact]
    public void NegativeCouponsProducePayablesAndContraIncome_WithoutZeroFloor()
    {
        var f = new Fixture(coupon: -1m);
        var through = new DateOnly(2025, 7, 2);
        var accrued = -182m / 365m;
        var input = f.Instruction with
        {
            ThroughDate = through,
            CurrentAccruedInterest = accrued,
            AmortizationProjection = f.Instruction.AmortizationProjection with { AccruedInterest = accrued }
        };
        var result = BondIncomeCalculator.Project(f.Lot, input);
        result.EffectiveAnnualYield.Should().BeApproximately(-0.01m, 0.000000001m);
        result.CouponIncome.Should().BeApproximately(accrued, 0.000000000001m);
        result.InterestReceivable.Should().Be(0m);
        result.InterestPayable.Should().BeApproximately(-accrued, 0.000000000001m);
        result.JournalComponents.Should().Contain(x => x.Component == "InterestPayable" && x.Credit > 0m && x.Debit == 0m);
        result.JournalComponents.Should().Contain(x => x.Component == "CouponIncome" && x.Debit > 0m && x.Credit == 0m);
        result.JournalComponents.Sum(x => x.Debit).Should().Be(result.JournalComponents.Sum(x => x.Credit));
    }

    [Fact]
    public void ProspectiveResetRecalibratesRemainingYield_AcquisitionPolicyKeepsItsSchedule()
    {
        var f = new Fixture();
        var reset = f.Projection([f.Flow(new(2027, 1, 1), 6m, 100m)]);
        var prospective = f.Instruction with
        {
            Policy = f.Instruction.Policy with { FloaterPolicy = BondIncomeFloaterPolicy.ProspectiveReset },
            Resets = [new(new(2026, 1, 1), reset)]
        };
        BondIncomeCalculator.Project(f.Lot, f.Instruction).EffectiveAnnualYield.Should().BeApproximately(0.05m, 0.000000001m);
        var revised = BondIncomeCalculator.Project(f.Lot, prospective);
        revised.EffectiveAnnualYield.Should().BeApproximately(0.06m, 0.000000001m);
        revised.CouponIncome.Should().Be(5m, "a prospective yield reset cannot rewrite earned coupon income");
        revised.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
    }

    [Fact]
    public void ContractualPrincipalDoesNotBecomeIncomeOrGetReleasedTwice()
    {
        var f = new Fixture();
        var flows = new[] { f.Flow(new(2026, 1, 1), 5m, 50m), f.Flow(new(2027, 1, 1), 2.5m, 50m) };
        var lot = f.Lot with { CurrentFactor = 0.5m, OpenTransactionCostBasis = 50m, OpenFunctionalCostBasis = 50m };
        var input = f.Instruction with { AcquisitionProjection = f.Projection(flows), CurrentProjection = f.Projection(flows) };
        var result = BondIncomeCalculator.Project(lot, input);
        result.ScheduledPrincipal.Should().Be(50m);
        result.ClosingCarryingBasis.Should().BeApproximately(50m, 0.00000001m);
        result.PremiumDiscountIncome.Should().BeApproximately(0m, 0.00000001m);
        result.TotalAccountingIncome.Should().BeApproximately(5m, 0.00000001m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedAmortizerKeepsIndexedCashDistinctFromUnindexedFace(bool indexChanged)
    {
        var f = new Fixture(cost: 120m, coupon: 0m, index: 1.2m);
        var baseline = new[]
        {
            f.Flow(new(2026, 1, 1), 0m, 60m) with { OutstandingPrincipal = 50m, IndexRatio = 1.2m },
            f.Flow(new(2027, 1, 1), 0m, 60m) with { OutstandingPrincipal = 0m, IndexRatio = 1.2m }
        };
        var current = baseline.Select(flow => flow with
        { Principal = indexChanged ? 65m : 60m, IndexRatio = indexChanged ? 1.3m : 1.2m }).ToArray();
        var lot = f.Lot with { CurrentFactor = 0.5m, OpenTransactionCostBasis = 60m, OpenFunctionalCostBasis = 60m };
        var input = f.Instruction with
        {
            AcquisitionProjection = f.Projection(baseline),
            CurrentProjection = f.Projection(current),
            CurrentIndexRatio = indexChanged ? 1.3m : 1.2m
        };
        var result = BondIncomeCalculator.Project(lot, input);
        result.ClosingCarryingBasis.Should().BeApproximately(indexChanged ? 65m : 60m, 0.00000001m);
        result.IndexationIncome.Should().Be(indexChanged ? 10m : 0m,
            "recognized indexation includes both paid and surviving principal, but paid indexation is not still carrying basis");
        result.PremiumDiscountIncome.Should().BeApproximately(0m, 0.00000001m);
    }

    [Fact]
    public void ObservedIndexationIsNoncashIncome_SeparateFromAcquisitionIndexation()
    {
        var f = new Fixture(cost: 120m, coupon: 6m, index: 1.2m);
        var input = f.Instruction with
        {
            AcquisitionProjection = f.Projection([f.Flow(new(2026, 1, 1), 6m, 0m), f.Flow(new(2027, 1, 1), 6m, 120m)]),
            CurrentProjection = f.Projection([f.Flow(new(2026, 1, 1), 6.5m, 0m), f.Flow(new(2027, 1, 1), 6.5m, 130m)]),
            CurrentIndexRatio = 1.3m,
            PrincipalForIndexation = 100m
        };
        var result = BondIncomeCalculator.Project(f.Lot, input);
        result.IndexationIncome.Should().Be(10m);
        result.CouponIncome.Should().Be(6.5m);
        result.ClosingCarryingBasis.Should().BeApproximately(130m, 0.00000001m);
        result.PremiumDiscountIncome.Should().BeApproximately(0m, 0.00000001m);
    }

    [Fact]
    public void CallablePremiumUsesNextPresetCall_WithoutInventingPrincipalCash()
    {
        var f = new Fixture(cost: 110m);
        var input = f.Instruction with { Calls = [new(new(2026, 1, 1), 100m)] };
        var result = BondIncomeCalculator.Project(f.Lot, input);
        result.ClosingCarryingBasis.Should().BeApproximately(100m, 0.00000001m);
        result.PremiumDiscountIncome.Should().BeApproximately(-10m, 0.00000001m);
        result.ScheduledPrincipal.Should().Be(0m, "an eligible call is not an observed exercise");
    }

    [Fact]
    public void CallableAmortizerIncludesPrincipalPaidBeforeTheCall()
    {
        var f = new Fixture(cost: 110m);
        var flows = new[]
        {
            f.Flow(new(2026, 1, 1), 5m, 50m) with { OutstandingPrincipal = 50m },
            f.Flow(new(2027, 1, 1), 2.5m, 0m) with { OutstandingPrincipal = 50m },
            f.Flow(new(2028, 1, 1), 2.5m, 50m) with { OutstandingPrincipal = 0m }
        };
        var input = f.Instruction with
        {
            ThroughDate = new(2027, 1, 1),
            AcquisitionProjection = f.Projection(flows),
            CurrentProjection = f.Projection(flows),
            Calls = [new(new(2027, 1, 1), 100m)]
        };
        var lot = f.Lot with { CurrentFactor = 0.5m, OpenTransactionCostBasis = 60m, OpenFunctionalCostBasis = 60m };
        BondIncomeCalculator.Project(lot, input).ClosingCarryingBasis.Should().BeApproximately(50m, 0.00000001m);
    }

    [Fact]
    public void EarnedForecastCouponsAndAmbiguousYieldRootsAreExplicitRefusals()
    {
        var f = new Fixture();
        var forecast = f.Instruction with
        { CurrentProjection = f.Projection([f.Flow(new(2026, 1, 1), 5m, 0m) with { IsForecast = true }]) };
        f.Invoking(x => BondIncomeCalculator.Project(x.Lot, forecast))
            .Should().Throw<ArgumentException>().WithMessage("*observed inputs*");
        var ambiguous = f.Instruction with
        {
            AcquisitionProjection = f.Projection(
            [f.Flow(new(2026, 1, 1), 230m, 0m), f.Flow(new(2027, 1, 1), -132m, 0m)])
        };
        f.Invoking(x => BondIncomeCalculator.Project(x.Lot, ambiguous))
            .Should().Throw<ArgumentException>().WithMessage("*YieldUniquenessNotEstablished*");
    }

    [Fact]
    public void PolicyMustBeExplicitlyAdoptedAndOldPayloadsOmitNewFields()
    {
        var record = new AccountingPolicyDto("gaap", AccountingBasisKindDto.Gaap, "v1", "GAAP", new(2020, 1, 1),
            null, false, "{}", DateTimeOffset.UtcNow);
        var action = () => BondIncomeCalculator.ReadPolicy(record);
        action.Should().Throw<ArgumentException>().WithMessage("*no adopted*");
        var f = new Fixture();
        JsonSerializer.Serialize(f.Lot.Acquisition with { BondIncome = null }).Should().NotContain("BondIncome");
        var adopted = record with { RulesJson = JsonSerializer.Serialize(new { bondIncome = f.Instruction.Policy }) };
        BondIncomeCalculator.ReadPolicy(adopted).Should().Be(f.Instruction.Policy);
    }

    private sealed class Fixture
    {
        public Guid SecurityId { get; } = Guid.NewGuid();
        public DateOnly AcquisitionDate { get; } = new(2025, 1, 1);
        public OpenLotDto Lot { get; }
        public BondIncomeInstructionDto Instruction { get; }
        private readonly DateTimeOffset _cutoff = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public Fixture(decimal cost = 100m, decimal coupon = 5m, decimal index = 1m,
            decimal capitalized = 0m, decimal expense = 0m, decimal tax = 0m)
        {
            var id = Guid.NewGuid();
            var evidence = new RetainedEvidenceIdentityDto("acquired", "urn:synthetic:acquisition", new string('a', 64), "Synthetic",
                "source1", "Accepted", "reviewer", new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), AcquisitionDate,
                1, new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), "reviewer", "OpenLotAcquisition", id.ToString("D"));
            var fees = new List<BondIncomeFeeDto>();
            if (capitalized != 0m)
                fees.Add(new("direct", capitalized, BondIncomeFeeTreatment.EligibleDirectAcquisitionCost, evidence));
            if (expense != 0m)
                fees.Add(new("expense", expense, BondIncomeFeeTreatment.Expense, evidence));
            if (tax != 0m)
                fees.Add(new("tax", tax, BondIncomeFeeTreatment.WithholdingTax, evidence));
            var acquired = new BondIncomeAcquisitionDto(new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), cost - capitalized, 0m, index, fees, [evidence]);
            var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, cost, cost, AcquisitionDate,
                new(100m, 1m, BondAmortizationMethod.NoAmortization, null), [evidence])
            { BondIncome = acquired };
            Lot = new(id, SecurityId, Guid.NewGuid(), Guid.NewGuid(), "lot", AcquisitionDate, 100m, 100m, cost, cost, 1, acquisition);
            var flows = new[] { Flow(new(2026, 1, 1), coupon, 0m), Flow(new(2027, 1, 1), coupon, 100m) };
            Instruction = new(Lot.LedgerBookId, "gaap", "v1", new(BondIncomeCalculator.ModelVersion,
                BondIncomeFloaterPolicy.AcquisitionIndex, true, true, true), AcquisitionDate, new(2026, 1, 1), _cutoff,
                Projection(flows), Projection(flows), Projection(flows), [], [], fees, 0m, 0m, index, index, 100m, [evidence]);
        }

        public BondWorkflowCashFlow Flow(DateOnly date, decimal interest, decimal principal)
            => new(date, date.AddYears(-1), date, interest, principal, 100m - principal, 1m, false, [])
            { AccrualFraction = 1m, PeriodUnits = 1m };

        public BondWorkflowResult Projection(IReadOnlyList<BondWorkflowCashFlow> flows)
            => new(SecurityId, "USD", "bond-workflow-v1", new(_cutoff, AcquisitionDate, _cutoff), 100m,
                100m, 0m, null, null, 0m, null, null, null, null, null, null, null, flows,
                [new("SecurityTerms", SecurityId.ToString("D"), 1, new string('a', 64))], [], []);
    }
}
