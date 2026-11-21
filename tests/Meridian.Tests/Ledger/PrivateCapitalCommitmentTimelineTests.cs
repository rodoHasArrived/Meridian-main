using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class PrivateCapitalCommitmentTimelineTests
{
    [Fact]
    public void FutureReduction_ConstrainsEarlierCallAtTheReductionDate()
    {
        var revisions = new[] { Revision(1, 100m, 1), Revision(2, 50m, 20) };
        Check(revisions, [], 15, 80m).Should().Contain("revision 2 effective 2026-05-20");
        Check(revisions, [], 15, 40m).Should().BeNull();
    }

    [Fact]
    public void FutureIncrease_DoesNotAuthorizeAnEarlierOvercall()
    {
        var revisions = new[] { Revision(1, 50m, 1), Revision(2, 150m, 20) };
        Check(revisions, [], 15, 80m).Should().Contain("revision 1 effective 2026-05-15");
        Check(revisions, [], 25, 80m).Should().BeNull();
    }

    [Fact]
    public void FuturePostedIssuance_IsCountedAtItsOwnDateAgainstTheThenActiveCap()
    {
        var revisions = new[] { Revision(1, 100m, 1), Revision(2, 150m, 20) };
        (DateOnly EffectiveDate, decimal Amount)[] issued = [(Day(5), 80m), (Day(25), 50m)];
        Check(revisions, issued, 15, 20m).Should().BeNull();
        Check(revisions, issued, 15, 21m).Should().Contain("revision 1 effective 2026-05-15");
        Check(revisions, issued, 25, 21m).Should().Contain("revision 2 effective 2026-05-25");
    }

    [Fact]
    public void SameEffectiveDate_HighestApprovedRevisionSupersedesTheLowerCap()
    {
        var revisions = new[]
        {
            Revision(1, 100m, 1), Revision(2, 50m, 20), Revision(3, 150m, 20),
            Revision(4, 1m, 20) with { ApprovalStatus = PrivateCapitalCommitmentApprovalStatus.Draft }
        };
        Check(revisions, [], 15, 80m).Should().BeNull();
        Check(revisions.Reverse(), [], 15, 80m).Should().BeNull();
    }

    [Fact]
    public void Approval_ValidatesExistingIssuancesAtAllEffectiveCheckpoints()
    {
        var revisions = new[] { Revision(1, 100m, 1), Revision(2, 50m, 20) };
        PrivateCapitalAuthorityRules.FindIssuanceTimelineViolation("commitment", revisions, [(Day(15), 80m)])
            .Should().Contain("revision 2 effective 2026-05-20");
        PrivateCapitalAuthorityRules.FindIssuanceTimelineViolation("commitment", revisions, [(Day(15), 40m)])
            .Should().BeNull();
    }

    [Fact]
    public void NoEffectiveAuthorityAndArithmeticOverflow_FailClosed()
    {
        Check([Revision(1, 100m, 20)], [], 15, 1m).Should().Contain("no approved effective");
        Check([Revision(1, decimal.MaxValue, 1)], [(Day(5), decimal.MaxValue)], 15, 1m)
            .Should().Contain("decimal precision");
    }

    private static string? Check(IEnumerable<PrivateCapitalCommitmentDto> revisions,
        IEnumerable<(DateOnly EffectiveDate, decimal Amount)> issued, int day, decimal amount)
        => PrivateCapitalAuthorityRules.FindIssuanceTimelineViolation("commitment", revisions, issued, Day(day), amount);

    private static DateOnly Day(int day) => new(2026, 5, day);

    private static PrivateCapitalCommitmentDto Revision(int revision, decimal total, int day)
        => new("commitment", new("tenant", "company", "entity", "fund", Guid.NewGuid(), "USD"), revision,
            "investor", "capital", total, Day(1), Day(day), null, true, PrivateCapitalCommitmentApprovalStatus.Approved,
            ["evidence://terms"], "preparer", DateTimeOffset.UnixEpoch, "reviewer", DateTimeOffset.UnixEpoch);
}
