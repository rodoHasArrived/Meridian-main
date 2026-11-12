using FluentAssertions;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Contracts.UnitTests;

public sealed class ContractualCashFlowRunComparerTests
{
    private static readonly DateOnly Issue = new(2026, 1, 1);
    private static readonly DateOnly Maturity = new(2027, 1, 1);
    private static readonly Guid Subject = Guid.Parse("3c1f0a9e-6b2d-4d8e-9a1b-0c2d3e4f5a6b");
    private static readonly ContractualCashFlowRequest Base = new(Issue, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static ContractualCashFlowRun Run(decimal rate, ContractualCashFlowRequest? request = null, Guid? runId = null)
    {
        var contract = new ContractualCashFlowContract(Issue, Maturity, "USD", [new("main", 1000, 3, DayCountConvention.Actual365, rate)]);
        var result = ContractualCashFlowEngine.Calculate(contract, request ?? Base);
        return new(runId ?? Guid.NewGuid(), "Security", Subject, Base.KnowledgeCutoff, contract, request ?? Base, result);
    }

    [Fact]
    public void Compare_ReportsPerDateNetCashDeltas_AndVerifiesBothRetainedPackets()
    {
        var left = Run(0.10m);
        var right = Run(0.12m);

        var comparison = ContractualCashFlowRunComparer.Compare(left, right);

        comparison.LeftReplayVerified.Should().BeTrue();
        comparison.RightReplayVerified.Should().BeTrue();
        comparison.SameRequest.Should().BeTrue();
        comparison.LeftOutputHash.Should().Be(left.Result.Evidence.OutputHash);
        comparison.CashDeltas.Should().NotBeEmpty();
        comparison.CashDeltas.Should().OnlyContain(delta => delta.StreamId == "main" && delta.Delta > 0m, "a higher coupon pays more on every coupon date");
        comparison.CashDeltas.Should().BeInAscendingOrder(delta => delta.Date);
        comparison.TotalDelta.Should().Be(right.Result.Events.Sum(e => e.NetCash) - left.Result.Events.Sum(e => e.NetCash));
        comparison.BlockerDeltas.Should().BeEmpty();
    }

    [Fact]
    public void Compare_IsEmptyForIdenticalPackets_AndFlagsRequestDifferences()
    {
        var left = Run(0.10m);
        var same = left with { RunId = Guid.NewGuid() };
        ContractualCashFlowRunComparer.Compare(left, same).CashDeltas.Should().BeEmpty();

        var shocked = Run(0.10m, Base with { RateScenario = StructuredCashFlowScenario.Up200 });
        var comparison = ContractualCashFlowRunComparer.Compare(left, shocked);
        comparison.SameRequest.Should().BeFalse();
        comparison.LeftReplayVerified.Should().BeTrue();
        comparison.RightReplayVerified.Should().BeTrue();
    }

    [Fact]
    public void Compare_ReportsBlockerSymmetricDifference_AndDetectsATamperedPacket()
    {
        var left = Run(0.10m);
        var blocker = new CashFlowCalculationBlocker("CurvePinRequired", "Pin the SOFR curve.", "float");
        var right = left with { RunId = Guid.NewGuid(), Result = left.Result with { Blockers = [blocker] } };

        var comparison = ContractualCashFlowRunComparer.Compare(left, right);

        comparison.BlockerDeltas.Should().ContainSingle().Which.Should().Be(new CashFlowBlockerDelta("RightOnly", "CurvePinRequired", "Pin the SOFR curve.", "float"));
        comparison.RightReplayVerified.Should().BeFalse("the retained output no longer hashes to its evidence");
        comparison.LeftReplayVerified.Should().BeTrue();
    }

    [Fact]
    public void Compare_RefusesDifferentSubjectsAndTheSameRun()
    {
        var left = Run(0.10m);
        var otherSubject = () => ContractualCashFlowRunComparer.Compare(left, Run(0.10m) with { SubjectId = Guid.NewGuid() });
        otherSubject.Should().Throw<ArgumentException>().WithMessage("*same subject*");
        var sameRun = () => ContractualCashFlowRunComparer.Compare(left, left);
        sameRun.Should().Throw<ArgumentException>().WithMessage("*two different retained runs*");
        var eur = Run(0.10m);
        var otherCurrency = () => ContractualCashFlowRunComparer.Compare(left, eur with { Contract = eur.Contract with { Currency = "eur" } });
        otherCurrency.Should().Throw<ArgumentException>().WithMessage("*same contract currency*");
        var sameCurrencyOtherCase = ContractualCashFlowRunComparer.Compare(left, eur with { Contract = eur.Contract with { Currency = "usd" } });
        sameCurrencyOtherCase.CashDeltas.Should().BeEmpty("the currency comparison is case-insensitive and the schedules agree");
    }
}
