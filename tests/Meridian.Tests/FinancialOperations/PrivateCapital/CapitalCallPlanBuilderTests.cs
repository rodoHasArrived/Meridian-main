using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed class CapitalCallPlanBuilderTests
{
    private static CommitmentRollForward RollForward(string investor, decimal total, decimal called)
    {
        var commitment = new InvestorCommitment(
            $"commitment:fund-a:{investor}:1",
            "fund-a",
            null,
            $"ca:{investor}",
            investor,
            "USD",
            total,
            new DateOnly(2026, 1, 1),
            new DateOnly(2031, 1, 1),
            CommitmentStatus.Active);
        var events = called > 0m
            ? new[] { new CommitmentActivityEvent("seed", ManualJournalEntryTypeDto.CapitalCall, new DateOnly(2026, 1, 2), called) }
            : [];
        return CommitmentRollForwardCalculator.Build(commitment, events);
    }

    [Fact]
    public void ProRataByUncalled_AllocatesFullAmountAcrossLps()
    {
        var request = new CapitalCallPlanRequest(
            "call-2026Q2",
            "fund-a",
            3_000_000m,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 15),
            [RollForward("lp-1", 10_000_000m, 0m), RollForward("lp-2", 5_000_000m, 0m)]);

        var plan = CapitalCallPlanBuilder.Build(request);

        plan.IsExecutable.Should().BeTrue();
        plan.AllocatedAmount.Should().Be(3_000_000m);
        plan.Lines.Single(line => line.Commitment.InvestorId == "lp-1").CallAmount.Should().Be(2_000_000m);
        plan.Lines.Single(line => line.Commitment.InvestorId == "lp-2").CallAmount.Should().Be(1_000_000m);
    }

    [Fact]
    public void OverCapacity_IsFlaggedCriticalAndNotExecutable()
    {
        var request = new CapitalCallPlanRequest(
            "call-big",
            "fund-a",
            20_000_000m,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 15),
            [RollForward("lp-1", 10_000_000m, 0m)]);

        var plan = CapitalCallPlanBuilder.Build(request);

        plan.IsExecutable.Should().BeFalse();
        plan.ValidationIssues.Should().Contain(issue =>
            issue.Code == "private-capital.capital-call-exceeds-uncalled");
    }

    [Fact]
    public void PartiallyCalledLp_IsCappedAtRemainingUncalled()
    {
        // lp-1 has 1,000,000 uncalled; lp-2 has 5,000,000 uncalled. Call 5,000,000.
        var request = new CapitalCallPlanRequest(
            "call-2026Q3",
            "fund-a",
            5_000_000m,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 15),
            [RollForward("lp-1", 10_000_000m, 9_000_000m), RollForward("lp-2", 5_000_000m, 0m)]);

        var plan = CapitalCallPlanBuilder.Build(request);

        plan.IsExecutable.Should().BeTrue();
        plan.AllocatedAmount.Should().Be(5_000_000m);
        plan.Lines.Single(line => line.Commitment.InvestorId == "lp-1").CallAmount.Should().BeLessThanOrEqualTo(1_000_000m);
        plan.Lines.Sum(line => line.CallAmount).Should().Be(5_000_000m);
    }

    [Fact]
    public void PlanLine_BuildsNoticedInstallment()
    {
        var request = new CapitalCallPlanRequest(
            "call-2026Q2",
            "fund-a",
            1_000_000m,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 15),
            [RollForward("lp-1", 10_000_000m, 0m)]);

        var line = CapitalCallPlanBuilder.Build(request).Lines.Single();
        var installment = line.BuildInstallment(request.NoticeDate, request.DueDate, sequence: 1);

        installment.Status.Should().Be(DrawdownInstallmentStatus.Noticed);
        installment.CallAmount.Should().Be(line.CallAmount);
        installment.DueDate.Should().Be(new DateOnly(2026, 4, 15));
    }
}
