using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed partial class IntercompanyAgreementServiceTests
{
    [Fact]
    public async Task ExternalReview_AllowsPreparerAttestationAndRetainsDistinctReviewerIdentity()
    {
        var (fixture, command) = await SubmittedExternalReviewAsync();
        var result = await fixture.Service.ApproveAsync(fixture.Author, command);
        var revision = Assert.Single(result.Detail.Revisions);
        Assert.Equal(fixture.Author.Actor, revision.Approver);
        Assert.Contains(fixture.Author.Actor, revision.Authors);
        Assert.Equal("External counsel", revision.ExternalReview!.ExternalReviewer);
        Assert.Equal(fixture.Author.Actor, revision.ExternalReview.Operator);
        Assert.Equal(command.SubmittedHash, revision.ExternalReview.SubmittedHash);
        Assert.Equal(fixture.Evidence.Hash, revision.ExternalReview.Evidence.ContentHash);
        Assert.Equal(revision.ExternalReview, result.Detail.History.Last().ExternalReview);
        Assert.Equal(revision.ExternalReview, result.Detail.Proposals.Last().ExternalReview);
        Assert.False(result.Detail.Capabilities.CanAttestExternalReview);
    }

    [Fact]
    public async Task ExternalReview_IsExplicitAndDoesNotWeakenIndependentDefault()
    {
        var (fixture, command) = await SubmittedExternalReviewAsync();
        var detail = (await fixture.Service.GetAsync(fixture.Author, fixture.AgreementId))!;
        Assert.False(detail.Capabilities.CanApprove);
        Assert.True(detail.Capabilities.CanAttestExternalReview);
        await Assert.ThrowsAsync<IntercompanyAgreementForbiddenException>(() => fixture.Service.ApproveAsync(fixture.Author,
            command with { ExternalReview = null }));
        Assert.Empty(fixture.Store.State!.Revisions);
        var approved = await fixture.Service.ApproveAsync(fixture.Reviewer, command with { ExternalReview = null });
        Assert.Null(approved.Detail.Revisions.Single().ExternalReview);
    }

    [Fact]
    public async Task ExternalReview_RejectsMissingReviewerFutureDateAndChangedSubject()
    {
        var (fixture, command) = await SubmittedExternalReviewAsync();
        var external = command.ExternalReview!;
        foreach (var invalid in new[]
        {
            external with { ExternalReviewer = "" },
            external with { ExternalReviewer = fixture.Author.Actor },
            external with { ReviewerReference = "" },
            external with { Rationale = "" },
            external with { ReviewedOn = new(2027, 1, 1) },
            external with { SubmittedHash = Sha256Digest.ComputeUtf8("different subject") }
        })
        {
            await Assert.ThrowsAnyAsync<IntercompanyAgreementValidationException>(() => fixture.Service.ApproveAsync(fixture.Author,
                command with { CommandId = Guid.NewGuid(), ExternalReview = invalid }));
        }
        Assert.Empty(fixture.Store.State!.Revisions);
        Assert.Equal(2, fixture.Store.State.AggregateVersion);
    }

    [Fact]
    public async Task ExternalReview_ExactRetryPreservesReceiptAndChangedAttestationConflicts()
    {
        var (fixture, command) = await SubmittedExternalReviewAsync();
        var original = await fixture.Service.ApproveAsync(fixture.Author, command);
        await fixture.ApproveAsync(fixture.Draft with
        {
            Action = IntercompanyAgreementActionDto.Amend,
            EffectiveFrom = new(2026, 4, 1),
            Terms = new(FixedAnnualRate: 0.07m)
        });
        var retry = await fixture.Service.ApproveAsync(fixture.Author, command);
        Assert.True(retry.Replayed);
        Assert.Equal(original.Receipt, retry.Receipt);
        Assert.Equal(6, fixture.Store.State!.AggregateVersion);
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => fixture.Service.ApproveAsync(fixture.Author,
            command with { ExternalReview = command.ExternalReview! with { ReviewerReference = "different-matter" } }));
    }

    [Fact]
    public async Task ExternalReview_RejectionRetainsAttestationWithoutCreatingApprovedRevision()
    {
        var (fixture, command) = await SubmittedExternalReviewAsync();
        var result = await fixture.Service.RejectAsync(fixture.Author, command);
        Assert.Empty(result.Detail.Revisions);
        Assert.Equal(IntercompanyAgreementProposalStateDto.Rejected, result.Detail.Proposals.Last().State);
        Assert.Equal(fixture.Author.Actor, result.Detail.History.Last().ExternalReview!.Operator);
        Assert.Equal("External counsel", result.Detail.History.Last().ExternalReview!.ExternalReviewer);
    }

    [Fact]
    public void ExternalReview_NullMetadataDoesNotChangeLegacyCommandOrRevisionWireShape()
    {
        var fixture = new Fixture();
        var command = fixture.Command(fixture.Draft);
        var serialized = JsonSerializer.Serialize(command, FundStructureContractsJsonContext.Default.IntercompanyAgreementCommandDto);
        Assert.DoesNotContain("externalReview", serialized);
        var revision = Revision(fixture, 1, January);
        Assert.DoesNotContain("externalReview", JsonSerializer.Serialize(revision,
            FundStructureContractsJsonContext.Default.IntercompanyAgreementRevisionDto));
    }

    private static async Task<(Fixture Fixture, IntercompanyAgreementCommandDto Command)> SubmittedExternalReviewAsync()
    {
        var fixture = new Fixture();
        fixture.Evidence.Hash = Sha256Digest.ComputeUtf8("immutable external legal review bytes");
        var create = fixture.Command(fixture.Draft);
        await fixture.Service.CreateAsync(fixture.Author, create);
        var submitted = await fixture.Service.SubmitAsync(fixture.Author, fixture.Command(proposalId: create.ProposalId));
        var hash = submitted.Detail.Proposals.Last().SubmittedHash!;
        var external = new IntercompanyExternalReviewDto("External counsel", "counsel:matter-1", new(2026, 11, 11),
            new("vault:agreement", "artifact-1", fixture.Evidence.Hash), "I attest the retained external review covers these exact submitted terms.", hash);
        return (fixture, fixture.Command(proposalId: create.ProposalId) with { SubmittedHash = hash, ExternalReview = external });
    }
}
