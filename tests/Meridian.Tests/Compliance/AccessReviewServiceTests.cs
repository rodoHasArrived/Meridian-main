using FluentAssertions;
using Meridian.Audit.Compliance;
using Xunit;

namespace Meridian.Tests.Compliance;

/// <summary>
/// Guards the dormant-permission access review. The failure mode under guard: a
/// privileged role left on an actor who has not used it in over 90 days survives the
/// periodic review, widening the standing-privilege surface.
/// </summary>
public sealed class AccessReviewServiceTests
{
    [Fact]
    public void ReviewDormantPermissions_LastUsedOver90DaysAgo_RemovesAllRoles()
    {
        var service = new AccessReviewService();
        string[] roles = ["TreasuryOperator", "OverrideApprover"];

        var review = service.ReviewDormantPermissions(
            actorId: "treasury-ops-1",
            reviewedBy: "compliance-lead",
            currentRoles: roles,
            lastUsedAtUtc: DateTimeOffset.UtcNow.AddDays(-120));

        review.RemovedRoles.Should().BeEquivalentTo(roles,
            "dormant privileged roles must be stripped by the periodic review");
        review.Reason.Should().Be("Dormant permissions cleanup.");
        review.ActorId.Should().Be("treasury-ops-1");
        review.ReviewedBy.Should().Be("compliance-lead");
    }

    [Fact]
    public void ReviewDormantPermissions_RecentlyUsed_RemovesNothing()
    {
        var service = new AccessReviewService();

        var review = service.ReviewDormantPermissions(
            actorId: "treasury-ops-1",
            reviewedBy: "compliance-lead",
            currentRoles: ["TreasuryOperator"],
            lastUsedAtUtc: DateTimeOffset.UtcNow.AddDays(-30));

        review.RemovedRoles.Should().BeEmpty(
            "actively used roles must survive the review untouched");
        review.Reason.Should().Be("No dormant permissions.");
    }

    [Fact]
    public void GetReviews_AccumulatesEveryReviewRecord()
    {
        var service = new AccessReviewService();

        var dormant = service.ReviewDormantPermissions(
            "actor-a", "compliance-lead", ["RulesAdmin"], DateTimeOffset.UtcNow.AddDays(-365));
        var active = service.ReviewDormantPermissions(
            "actor-b", "compliance-lead", ["ReconciliationOfficer"], DateTimeOffset.UtcNow.AddDays(-1));

        service.GetReviews().Should().HaveCount(2,
            "the review trail is compliance evidence and must retain every outcome")
            .And.ContainInOrder(dormant, active);
    }
}
