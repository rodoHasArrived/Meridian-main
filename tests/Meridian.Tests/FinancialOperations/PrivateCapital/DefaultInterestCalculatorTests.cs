using FluentAssertions;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed class DefaultInterestCalculatorTests
{
    private static InvestorCommitment Commitment(
        decimal rate = 0.10m,
        int graceDays = 10,
        DefaultInterestConvention convention = DefaultInterestConvention.Actual365Fixed)
        => new(
            "commitment:fund-a:lp-1:1",
            "fund-a",
            null,
            "ca:lp-1",
            "lp-1",
            "USD",
            10_000_000m,
            new DateOnly(2026, 1, 1),
            new DateOnly(2031, 1, 1),
            CommitmentStatus.Active,
            defaultInterestRateAnnual: rate,
            defaultInterestConvention: convention,
            defaultGraceDays: graceDays);

    private static DrawdownInstallment Installment(decimal amount, DateOnly due)
        => new("inst-1", "commitment:fund-a:lp-1:1", 1, due.AddDays(-14), due, callPercent: null, callAmount: amount, DrawdownInstallmentStatus.Called);

    [Fact]
    public void Actual365_SimpleInterest_MatchesFormula()
    {
        // Grace ends 2026-04-10; asOf 2026-05-01 => 21 days.
        var interest = DefaultInterestCalculator.ComputeSimpleInterest(
            1_000_000m, 0.10m, new DateOnly(2026, 4, 10), new DateOnly(2026, 5, 1),
            DefaultInterestConvention.Actual365Fixed);

        interest.Should().Be(Math.Round(1_000_000m * 0.10m * 21m / 365m, 2, MidpointRounding.ToEven));
    }

    [Fact]
    public void DefaultDetected_AfterGracePeriod()
    {
        var commitment = Commitment();
        var installment = Installment(1_000_000m, new DateOnly(2026, 3, 31)); // grace end 2026-04-10

        var defaults = DefaultInterestCalculator.Evaluate(
            commitment,
            [installment],
            fundingReceipts: [],
            asOf: new DateOnly(2026, 5, 1));

        var capitalDefault = defaults.Should().ContainSingle().Subject;
        capitalDefault.Status.Should().Be(DrawdownInstallmentStatus.Defaulted);
        capitalDefault.DefaultedAmount.Should().Be(1_000_000m);
        capitalDefault.AccruedInterest.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void FundedInFull_ProducesNoDefault()
    {
        var commitment = Commitment();
        var installment = Installment(1_000_000m, new DateOnly(2026, 3, 31));

        var defaults = DefaultInterestCalculator.Evaluate(
            commitment,
            [installment],
            fundingReceipts: [new CapitalCallFundingReceipt("inst-1", new DateOnly(2026, 3, 30), 1_000_000m)],
            asOf: new DateOnly(2026, 5, 1));

        defaults.Should().BeEmpty();
    }

    [Fact]
    public void LateFullFunding_CuresAndStopsAccrual()
    {
        var commitment = Commitment();
        var installment = Installment(1_000_000m, new DateOnly(2026, 3, 31));

        var defaults = DefaultInterestCalculator.Evaluate(
            commitment,
            [installment],
            fundingReceipts: [new CapitalCallFundingReceipt("inst-1", new DateOnly(2026, 4, 20), 1_000_000m)],
            asOf: new DateOnly(2026, 5, 1));

        var capitalDefault = defaults.Should().ContainSingle().Subject;
        capitalDefault.Status.Should().Be(DrawdownInstallmentStatus.Cured);
        capitalDefault.CuredDate.Should().Be(new DateOnly(2026, 4, 20));
        capitalDefault.Accruals.Single().AccrualTo.Should().Be(new DateOnly(2026, 4, 20));
    }

    [Fact]
    public void Thirty360_DayCount_DiffersFromActual()
    {
        var thirty360 = DefaultInterestCalculator.ComputeSimpleInterest(
            1_000_000m, 0.12m, new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 31),
            DefaultInterestConvention.Thirty360);
        // 30/360 from Jan-31 to Mar-31 = 60 days.
        thirty360.Should().Be(Math.Round(1_000_000m * 0.12m * 60m / 360m, 2, MidpointRounding.ToEven));
    }

    [Fact]
    public void Thirty360_NormalizesEndOfFebruary()
    {
        // US 30/360 counts 2026-02-28 (last day of Feb) to 2026-03-31 as 30 days, not 33.
        var interest = DefaultInterestCalculator.ComputeSimpleInterest(
            1_000_000m, 0.12m, new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31),
            DefaultInterestConvention.Thirty360);

        interest.Should().Be(Math.Round(1_000_000m * 0.12m * 30m / 360m, 2, MidpointRounding.ToEven));
    }

    [Fact]
    public void Thirty360_NormalizesLeapDayEndOfFebruary()
    {
        // 2024 is a leap year: 2024-02-29 is the last day of February, normalized to day 30.
        var interest = DefaultInterestCalculator.ComputeSimpleInterest(
            1_000_000m, 0.12m, new DateOnly(2024, 2, 29), new DateOnly(2024, 3, 30),
            DefaultInterestConvention.Thirty360);

        interest.Should().Be(Math.Round(1_000_000m * 0.12m * 30m / 360m, 2, MidpointRounding.ToEven));
    }

    [Fact]
    public void PostGracePartialPayment_AmortizesInterestBearingPrincipal()
    {
        var commitment = Commitment(graceDays: 10);
        var installment = Installment(1_000_000m, new DateOnly(2026, 3, 31)); // grace end 2026-04-10

        var defaults = DefaultInterestCalculator.Evaluate(
            commitment,
            [installment],
            fundingReceipts: [new CapitalCallFundingReceipt("inst-1", new DateOnly(2026, 4, 25), 400_000m)],
            asOf: new DateOnly(2026, 5, 25));

        var capitalDefault = defaults.Should().ContainSingle().Subject;
        capitalDefault.Accruals.Should().HaveCount(2);
        capitalDefault.Accruals[0].Principal.Should().Be(1_000_000m); // before the partial payment
        capitalDefault.Accruals[1].Principal.Should().Be(600_000m);   // after the partial payment
        capitalDefault.Status.Should().Be(DrawdownInstallmentStatus.Defaulted);
    }

    [Fact]
    public void FutureReceipt_IsIgnoredForHistoricalAsOf()
    {
        var commitment = Commitment(graceDays: 10);
        var installment = Installment(1_000_000m, new DateOnly(2026, 3, 31));

        // Funding lands 2026-05-20 but the report is as of 2026-05-01 — it must not cure or shorten.
        var defaults = DefaultInterestCalculator.Evaluate(
            commitment,
            [installment],
            fundingReceipts: [new CapitalCallFundingReceipt("inst-1", new DateOnly(2026, 5, 20), 1_000_000m)],
            asOf: new DateOnly(2026, 5, 1));

        var capitalDefault = defaults.Should().ContainSingle().Subject;
        capitalDefault.Status.Should().Be(DrawdownInstallmentStatus.Defaulted);
        capitalDefault.CuredDate.Should().BeNull();
        capitalDefault.Accruals.Single().AccrualTo.Should().Be(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public void PartialFunding_LeavesUnfundedPrincipalInDefault()
    {
        var commitment = Commitment();
        var installment = Installment(1_000_000m, new DateOnly(2026, 3, 31));

        var defaults = DefaultInterestCalculator.Evaluate(
            commitment,
            [installment],
            fundingReceipts: [new CapitalCallFundingReceipt("inst-1", new DateOnly(2026, 4, 5), 400_000m)],
            asOf: new DateOnly(2026, 5, 1));

        var capitalDefault = defaults.Should().ContainSingle().Subject;
        capitalDefault.DefaultedAmount.Should().Be(600_000m);
        capitalDefault.Status.Should().Be(DrawdownInstallmentStatus.Defaulted);
    }
}
