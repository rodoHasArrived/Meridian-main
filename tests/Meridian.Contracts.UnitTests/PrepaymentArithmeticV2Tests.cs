using FluentAssertions;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Contracts.UnitTests;

/// <summary>
/// Prepayment arithmetic v2 keeps survival in decimal (review Priority 1, step 3). Runs recorded under
/// v1 still replay with the original double arithmetic, so retained packets keep verifying.
/// </summary>
public sealed class PrepaymentArithmeticV2Tests
{
    private static readonly DateOnly Issue = new(2026, 1, 1);
    private static readonly DateOnly Maturity = new(2027, 1, 1);
    private static readonly ContractualCashFlowRequest Request = new(Issue, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static ContractualCashFlowContract Contract()
        => new(Issue, Maturity, "USD", [new ContractualCashFlowStream("main", 1000, 1, DayCountConvention.Actual365, 0m) { PrepaymentAllowed = true }]);

    [Fact]
    public void WholeMonthSmm_IsExact()
    {
        var assumption = new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Smm, .02m);

        ContractualCashFlowEngine.PrepaymentFraction(assumption, Issue, new(2026, 3, 1)).Should().Be(1m - .98m * .98m);
    }

    [Fact]
    public void FullYearCpr_IsExact()
    {
        var assumption = new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Cpr, .12m);

        // 2026 has 365 days, so the exponents across its months sum to exactly one year.
        ContractualCashFlowEngine.PrepaymentFraction(assumption, Issue, Maturity).Should().BeApproximately(.12m, .0000000000000000000001m);
    }

    [Fact]
    public void V2_AgreesWithV1_WithinDoublePrecision()
    {
        var assumptions = new[]
        {
            new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Smm, .0137m),
            new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Cpr, .085m),
            new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Psa, 1.5m, Issue)
        };

        foreach (var assumption in assumptions)
        {
            var start = Issue.AddDays(11);
            var end = Issue.AddDays(200);
            var v1 = ContractualCashFlowEngine.PrepaymentFraction(assumption, start, end, ContractualCashFlowVersions.PrepaymentV1);
            var v2 = ContractualCashFlowEngine.PrepaymentFraction(assumption, start, end, ContractualCashFlowVersions.Prepayment);
            v2.Should().BeApproximately(v1, .000000000001m, assumption.Method.ToString());
        }
    }

    [Theory]
    [InlineData(.98, .5)]
    [InlineData(.5, .3333)]
    [InlineData(.001, .75)]
    [InlineData(.999999, .0833)]
    [InlineData(.25, 2.5)]
    public void DecimalPower_MatchesMathPow(double baseValue, double exponent)
        => DecimalMath.Power((decimal)baseValue, (decimal)exponent)
            .Should().BeApproximately((decimal)Math.Pow(baseValue, exponent), .000000000000001m);

    [Fact]
    public void DecimalPower_EdgeCases_AreExact()
    {
        DecimalMath.Power(.7m, 0m).Should().Be(1m);
        DecimalMath.Power(1m, .4m).Should().Be(1m);
        DecimalMath.Power(0m, .4m).Should().Be(0m);
        DecimalMath.Power(.7m, 1m).Should().Be(.7m);
        DecimalMath.Power(.7m, 3m).Should().Be(.343m);
    }

    [Fact]
    public void NewRuns_RecordV2_AndVerify()
    {
        var request = Request with { Prepayments = [new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Cpr, .1m)] };
        var result = ContractualCashFlowEngine.Calculate(Contract(), request);

        result.Evidence.PrepaymentVersion.Should().Be(ContractualCashFlowVersions.Prepayment);
        ContractualCashFlowEngine.Verify(Run(request, result)).Should().BeTrue();
    }

    [Fact]
    public void RunsRecordedUnderV1_StillReplayAndVerify()
    {
        var request = Request with { Prepayments = [new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Smm, .0137m)] };
        var v1 = ContractualCashFlowEngine.Calculate(Contract(), request, null, null, "explicit-contract-v1", null, ContractualCashFlowVersions.PrepaymentV1);

        v1.Evidence.PrepaymentVersion.Should().Be(ContractualCashFlowVersions.PrepaymentV1);
        ContractualCashFlowEngine.Verify(Run(request, v1)).Should().BeTrue();
    }

    [Fact]
    public void RunsWithAnUnknownPrepaymentVersion_FailVerification()
    {
        var result = ContractualCashFlowEngine.Calculate(Contract(), Request);
        var tampered = result with { Evidence = result.Evidence with { PrepaymentVersion = "survival-cpr-smm-psa-v9" } };

        ContractualCashFlowEngine.Verify(Run(Request, tampered)).Should().BeFalse();
    }

    private static ContractualCashFlowRun Run(ContractualCashFlowRequest request, ContractualCashFlowResult result)
        => new(Guid.NewGuid(), "Security", Guid.NewGuid(), request.KnowledgeCutoff, Contract(), request, result);
}
