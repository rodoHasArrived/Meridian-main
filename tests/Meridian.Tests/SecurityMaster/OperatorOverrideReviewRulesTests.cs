using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class OperatorOverrideReviewRulesTests
{
    private static OperatorOverridesDto Overlay(string stagedBy)
        => new(Guid.NewGuid(), new Dictionary<string, string> { ["sector"] = "Technology" }, stagedBy, DateTimeOffset.UtcNow)
        {
            ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
        };

    [Theory]
    [InlineData("editor", "editor")]
    [InlineData("editor", " EDITOR ")]
    public void Approval_ByTheStagingActor_IsRefused(string stagedBy, string reviewer)
    {
        var overlay = Overlay(stagedBy);

        var act = () => OperatorOverrideReviewRules.EnsureIndependentReview(
            overlay.SecurityId, overlay, new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, reviewer));

        act.Should().Throw<OperatorOverrideSelfApprovalException>();
    }

    [Fact]
    public void Approval_ByAnotherActor_IsAllowed()
    {
        var overlay = Overlay("editor");

        var act = () => OperatorOverrideReviewRules.EnsureIndependentReview(
            overlay.SecurityId, overlay, new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "reviewer"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Rejection_ByTheStagingActor_IsAllowed()
    {
        var overlay = Overlay("editor");

        var act = () => OperatorOverrideReviewRules.EnsureIndependentReview(
            overlay.SecurityId, overlay, new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Rejected, "editor"));

        act.Should().NotThrow();
    }

    [Fact]
    public void RestorationOfAPriorIndependentDecision_IsAllowed()
    {
        var overlay = Overlay("editor");

        var act = () => OperatorOverrideReviewRules.EnsureIndependentReview(
            overlay.SecurityId,
            overlay,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "editor") { RestoresPriorDecision = true });

        act.Should().NotThrow();
    }
}
