using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed class CommitmentRollForwardCalculatorTests
{
    private static InvestorCommitment Commitment(decimal total = 10_000_000m, decimal recallCapPercent = 1m)
        => new(
            "commitment:fund-a:lp-1:1",
            "fund-a",
            ledgerBookId: null,
            "ca:lp-1",
            "lp-1",
            "USD",
            total,
            new DateOnly(2026, 1, 1),
            new DateOnly(2031, 1, 1),
            CommitmentStatus.Active,
            recallCapPercent);

    private static CommitmentActivityEvent Call(string id, decimal amount, DateOnly date)
        => new(id, ManualJournalEntryTypeDto.CapitalCall, date, amount);

    private static CommitmentActivityEvent RecallableReturn(string id, decimal amount, DateOnly date)
        => new(id, ManualJournalEntryTypeDto.Distribution, date, amount, DistributionRecallability.RecallableReturnOfCapital);

    [Fact]
    public void SingleCall_ReducesUncalled()
    {
        var result = CommitmentRollForwardCalculator.Build(
            Commitment(),
            [Call("call-1", 2_500_000m, new DateOnly(2026, 3, 31))]);

        result.NetCalled.Should().Be(2_500_000m);
        result.Uncalled.Should().Be(7_500_000m);
        result.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public void FourQuarterlyCalls_FullyCallCommitment()
    {
        var commitment = Commitment();
        var events = new[]
        {
            Call("call-1", 2_500_000m, new DateOnly(2026, 3, 31)),
            Call("call-2", 2_500_000m, new DateOnly(2026, 6, 30)),
            Call("call-3", 2_500_000m, new DateOnly(2026, 9, 30)),
            Call("call-4", 2_500_000m, new DateOnly(2026, 12, 31)),
        };

        var result = CommitmentRollForwardCalculator.Build(commitment, events);

        result.Uncalled.Should().Be(0m);
        result.NetCalled.Should().Be(10_000_000m);
        result.Steps.Select(step => step.RunningUncalled)
            .Should().BeInDescendingOrder();
        result.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public void InvariantHolds_AtEveryStep()
    {
        var commitment = Commitment();
        var events = new[]
        {
            Call("call-1", 4_000_000m, new DateOnly(2026, 3, 31)),
            RecallableReturn("dist-1", 1_000_000m, new DateOnly(2026, 6, 30)),
            Call("call-2", 3_000_000m, new DateOnly(2026, 9, 30)),
        };

        var result = CommitmentRollForwardCalculator.Build(commitment, events);

        foreach (var step in result.Steps)
        {
            (step.RunningNetCalled + step.RunningUncalled + result.CumulativeExpired)
                .Should().BeApproximately(commitment.TotalCommitment, 0.0001m,
                    "net-called + uncalled + expired must equal total at every step");
        }

        result.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public void OverCall_IsFlaggedCritical()
    {
        var result = CommitmentRollForwardCalculator.Build(
            Commitment(total: 5_000_000m),
            [Call("call-1", 6_000_000m, new DateOnly(2026, 3, 31))]);

        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == CommitmentRollForwardCalculator.OverCallIssueCode
            && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        result.InvariantHolds.Should().BeFalse();
    }

    [Fact]
    public void RecallableReturn_RestoresUncalled()
    {
        var commitment = Commitment();
        var events = new[]
        {
            Call("call-1", 6_000_000m, new DateOnly(2026, 3, 31)),
            RecallableReturn("dist-1", 2_000_000m, new DateOnly(2026, 6, 30)),
        };

        var result = CommitmentRollForwardCalculator.Build(commitment, events);

        result.Uncalled.Should().Be(6_000_000m);
        result.NetCalled.Should().Be(4_000_000m);
        result.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public void RecallCap_LimitsRestoration()
    {
        var commitment = Commitment(recallCapPercent: 0.5m); // cap = 5,000,000
        var events = new[]
        {
            Call("call-1", 8_000_000m, new DateOnly(2026, 3, 31)),
            RecallableReturn("dist-1", 6_000_000m, new DateOnly(2026, 6, 30)),
        };

        var result = CommitmentRollForwardCalculator.Build(commitment, events);

        result.CumulativeRecallableRestored.Should().Be(5_000_000m);
        result.RemainingRecallableCapacity.Should().Be(0m);
        var recallable = result.RecallableDistributions.Single();
        recallable.RestoredToUncalled.Should().Be(5_000_000m);
        recallable.PermanentPortion.Should().Be(1_000_000m);
        result.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public void RecallCappedAtCapitalDrawn_CannotPushUncalledAboveCommitment()
    {
        var commitment = Commitment(); // 10M, cap 1.0
        var events = new[]
        {
            Call("call-1", 1_000_000m, new DateOnly(2026, 3, 31)),
            RecallableReturn("dist-1", 5_000_000m, new DateOnly(2026, 6, 30)),
        };

        var result = CommitmentRollForwardCalculator.Build(commitment, events);

        // Only the 1M actually drawn can be recalled, so uncalled returns to the full commitment
        // rather than ballooning to 14M.
        result.CumulativeRecallableRestored.Should().Be(1_000_000m);
        result.Uncalled.Should().Be(10_000_000m);
        result.Uncalled.Should().BeLessThanOrEqualTo(commitment.TotalCommitment);
        result.RecallableDistributions.Single().PermanentPortion.Should().Be(4_000_000m);
        result.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public void NonRecallableDistribution_DoesNotRestoreUncalled()
    {
        var commitment = Commitment();
        var events = new[]
        {
            Call("call-1", 6_000_000m, new DateOnly(2026, 3, 31)),
            new CommitmentActivityEvent("dist-1", ManualJournalEntryTypeDto.Distribution, new DateOnly(2026, 6, 30), 2_000_000m),
        };

        var result = CommitmentRollForwardCalculator.Build(commitment, events);

        result.Uncalled.Should().Be(4_000_000m);
        result.CumulativeRecallableRestored.Should().Be(0m);
    }

    [Fact]
    public void GovernedExpiry_ReleasesResidualUncalled()
    {
        var commitment = Commitment(total: 10_000_000m) with { };
        var events = new[] { Call("call-1", 6_000_000m, new DateOnly(2026, 3, 31)) };
        var expiry = new CommitmentExpiryEvent(
            "expiry-1",
            commitment.CommitmentId,
            new DateOnly(2031, 1, 2),
            4_000_000m,
            "ops-controller");

        var result = CommitmentRollForwardCalculator.Build(commitment, events, [expiry]);

        result.CumulativeExpired.Should().Be(4_000_000m);
        result.Uncalled.Should().Be(0m);
        result.InvariantHolds.Should().BeTrue();
    }
}
