using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Remediation slice 2, command side. Staging an asset-terms edit records the canonical base it was
/// drafted against. Publishing surfaces a stale-proposal refusal as itself, leaving the revision
/// Approved, and returns the canonical-merge outcome the merge handler reported.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    [Fact]
    public async Task UpdateSecurityField_RecordsTheCanonicalBaseTheProposalWasDraftedAgainst()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new { issueDate = "2026-01-01", maturity = "2030-01-01", par = 100m });

        var result = await harness.Service.UpdateSecurityFieldAsync(new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.maturity",
            NewValue: "2031-06-15",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Vendor corrected the maturity."));

        var revision = await harness.Revisions.GetAsync(result.RevisionId);
        revision!.FieldBaseRecorded.Should().BeTrue();
        revision.BaseVersion.Should().Be(3);
        revision.FieldBaseSnapshot.Should().Contain("\"maturity\":\"2030-01-01\"");
    }

    [Fact]
    public async Task UpdateSecurityField_AnnotationPath_RecordsNoBase()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new { issueDate = "2026-01-01", maturity = "2030-01-01", par = 100m });

        var result = await harness.Service.UpdateSecurityFieldAsync(new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "marketData.rating",
            NewValue: "AA+",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Rating annotation."));

        (await harness.Revisions.GetAsync(result.RevisionId))!.FieldBaseRecorded.Should().BeFalse(
            "annotation paths never merge into the canonical terms, so there is nothing to go stale");
    }

    [Fact]
    public async Task Publish_StaleProposal_IsRefusedAsItselfAndLeavesTheRevisionApproved()
    {
        var stale = new StaleFieldProposalException(SecurityId, Guid.NewGuid(), "assetSpecificTerms.par", baseVersion: 3, currentVersion: 4);
        var harness = new Harness(currentVersion: 4, handlers: [new ThrowingPublishHandler(stale)]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);

        await harness.Service.Invoking(s => s.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(SecurityId, revisionId, "ops.publisher", "ops.reviewer")))
            .Should().ThrowAsync<StaleFieldProposalException>();

        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task Publish_ReturnsTheCanonicalMergeOutcome()
    {
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        var reporter = new ReportingPublishHandler(new SecurityMasterCanonicalMergeOutcomeDto(
            SecurityMasterCanonicalMergeOutcomeKind.Merged, "merged", 5));
        harness = new Harness(currentVersion: 4, handlers: [reporter], revisions: harness.Revisions);

        var result = await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(SecurityId, revisionId, "ops.publisher", "ops.reviewer"));

        result.CanonicalMerge.Should().NotBeNull();
        result.CanonicalMerge!.Outcome.Should().Be(SecurityMasterCanonicalMergeOutcomeKind.Merged);
        result.CanonicalMerge.CanonicalVersion.Should().Be(5);
    }

    private sealed class ThrowingPublishHandler(Exception error) : ISecurityMasterRevisionPublishedHandler
    {
        public int Order => 5;

        public Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
            => Task.FromException(error);
    }

    private sealed class ReportingPublishHandler(SecurityMasterCanonicalMergeOutcomeDto outcome)
        : ISecurityMasterRevisionPublishedHandler, ISecurityMasterCanonicalMergeOutcomeReporter
    {
        private Guid? _handled;

        public int Order => 5;

        public Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
        {
            _handled = evt.RevisionId;
            return Task.CompletedTask;
        }

        public SecurityMasterCanonicalMergeOutcomeDto? TakeOutcome(Guid revisionId)
            => _handled == revisionId ? outcome : null;
    }
}
