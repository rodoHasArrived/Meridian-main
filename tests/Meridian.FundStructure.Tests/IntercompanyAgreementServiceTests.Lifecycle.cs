using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed partial class IntercompanyAgreementServiceTests
{
    [Theory]
    [InlineData(LegalEntityLifecycleStatusDto.Draft)]
    [InlineData(LegalEntityLifecycleStatusDto.Forming)]
    [InlineData(LegalEntityLifecycleStatusDto.WindingDown)]
    [InlineData(LegalEntityLifecycleStatusDto.Dissolved)]
    [InlineData(LegalEntityLifecycleStatusDto.Merged)]
    public async Task NativeDraftRemainsEditableButNewCommitmentSubmissionUsesEffectiveLifecycle(LegalEntityLifecycleStatusDto status)
    {
        var lifecycle = new InMemoryEntityLifecycleStore();
        var fixture = new Fixture(lifecycle);
        await LifecycleBaselineAsync(lifecycle, fixture, status);
        var create = fixture.Command(fixture.Draft);
        var draft = await fixture.Service.CreateAsync(fixture.Author, create);
        Assert.True(draft.Detail.Capabilities.CanEdit);

        await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() => fixture.Service.SubmitAsync(fixture.Author,
            fixture.Command(proposalId: create.ProposalId)));

        Assert.Equal(1, fixture.Store.State!.AggregateVersion);
        Assert.Empty(fixture.Store.State.Revisions);
    }

    [Fact]
    public async Task NativeApprovalRechecksCurrentLifecycleEvenForEarlierActiveEffectiveDate()
    {
        var lifecycle = new InMemoryEntityLifecycleStore();
        var fixture = new Fixture(lifecycle);
        await LifecycleBaselineAsync(lifecycle, fixture, LegalEntityLifecycleStatusDto.Active);
        var create = fixture.Command(fixture.Draft);
        await fixture.Service.CreateAsync(fixture.Author, create);
        var submitted = await fixture.Service.SubmitAsync(fixture.Author, fixture.Command(proposalId: create.ProposalId));
        await EnterWindingDownAsync(lifecycle, fixture);

        await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() => fixture.Service.ApproveAsync(fixture.Reviewer,
            fixture.Command(proposalId: create.ProposalId) with { SubmittedHash = submitted.Detail.Proposals[^1].SubmittedHash }));

        Assert.Equal(2, fixture.Store.State!.AggregateVersion);
        Assert.Equal(IntercompanyAgreementProposalStateDto.Submitted, fixture.Store.State.Proposals[^1].State);
        Assert.Empty(fixture.Store.State.Revisions);
    }

    [Fact]
    public async Task NativeExactApprovalReceiptSurvivesLaterLifecycleChangeWithoutAnotherAuditFact()
    {
        var lifecycle = new InMemoryEntityLifecycleStore();
        var fixture = new Fixture(lifecycle);
        await LifecycleBaselineAsync(lifecycle, fixture, LegalEntityLifecycleStatusDto.Active);
        var approval = await fixture.ApproveAsync(fixture.Draft);
        var retained = fixture.Store.State!.Receipts[^1];
        await EnterWindingDownAsync(lifecycle, fixture);

        var replay = await fixture.Service.ApproveAsync(fixture.Reviewer, approval);

        Assert.True(replay.Replayed);
        Assert.Equal(retained, replay.Receipt);
        Assert.Equal(3, fixture.Store.State.AggregateVersion);
        Assert.Equal(3, fixture.Store.State.History.Count);
    }

    [Fact]
    public async Task NativeLegacyActiveSnapshotsRetainExplicitBaselineMigrationWarnings()
    {
        var fixture = new Fixture(new InMemoryEntityLifecycleStore());
        var approval = await fixture.ApproveAsync(fixture.Draft);
        var result = await fixture.Service.ApproveAsync(fixture.Reviewer, approval);
        Assert.Equal(2, result.Detail.MigrationWarnings!.Count);
        Assert.All(result.Detail.MigrationWarnings, warning => Assert.Contains("baseline", warning));
        Assert.Equal(result.Detail.MigrationWarnings, result.Detail.History[^1].MigrationWarnings);
    }

    [Fact]
    public async Task NativeTerminationCanCloseAnExistingCommitmentDuringWindingDown()
    {
        var lifecycle = new InMemoryEntityLifecycleStore();
        var fixture = new Fixture(lifecycle);
        await LifecycleBaselineAsync(lifecycle, fixture, LegalEntityLifecycleStatusDto.Active);
        await fixture.ApproveAsync(fixture.Draft);
        await EnterWindingDownAsync(lifecycle, fixture);
        await fixture.ApproveAsync(fixture.Draft with
        {
            Action = IntercompanyAgreementActionDto.Terminate,
            TerminationEffectiveDate = new(2026, 11, 30)
        });
        var detail = await fixture.Service.GetAsync(fixture.Author, fixture.AgreementId, new(2026, 11, 30));
        Assert.Equal(IntercompanyAgreementStatusDto.Terminated, detail!.EffectiveStatus);
        Assert.Equal(2, fixture.Store.State!.Revisions.Count);
    }

    private static async Task LifecycleBaselineAsync(InMemoryEntityLifecycleStore store, Fixture fixture, LegalEntityLifecycleStatusDto status)
    {
        foreach (var id in new[] { fixture.Draft.PartyOneId, fixture.Draft.PartyTwoId })
        {
            var revision = EntityLifecycleRevisionIntegrity.Seal(new("tenant-a", id, 1, new Clock().GetUtcNow(), "lifecycle-reviewer",
                "Retained entity lifecycle baseline", "evidence://entity-baseline", "", "",
                [new(Guid.NewGuid(), new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null, status,
                    LegalEntityLifecycleEventKindDto.Formation)], LegacySnapshot: status == LegalEntityLifecycleStatusDto.Merged));
            await store.AppendAsync(revision, 0);
        }
    }
    private static async Task EnterWindingDownAsync(InMemoryEntityLifecycleStore store, Fixture fixture)
    {
        foreach (var id in new[] { fixture.Draft.PartyOneId, fixture.Draft.PartyTwoId })
        {
            var previous = (await store.GetLatestAsync("tenant-a", id))!;
            var cutoff = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            var next = EntityLifecycleRevisionIntegrity.Seal(previous with
            {
                Revision = previous.Revision + 1,
                PreviousHash = previous.ContentHash,
                ContentHash = "",
                Timeline =
                [previous.Timeline[0] with { EffectiveTo = cutoff }, new(Guid.NewGuid(), cutoff, null,
                    LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Dissolution)]
            });
            await store.AppendAsync(next, previous.Revision);
        }
    }
}
