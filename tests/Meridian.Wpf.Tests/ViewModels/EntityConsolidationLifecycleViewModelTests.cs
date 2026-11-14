using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.ViewModels.Accounting;
using Xunit;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class EntityConsolidationLifecycleViewModelTests
{
    [Fact]
    public async Task EditingTimelineInvalidatesExactReviewHashAndBlocksScopeNavigation()
    {
        var client = new Client();
        var view = await ViewAsync(client);
        view.BeginLifecycleRevisionCommand.Execute(null);
        view.LifecycleReason = "Reviewed legal wind-down timeline";
        view.LifecycleEvidenceReference = "evidence://evidence-vault/legal";
        await view.PreviewLifecycleReviewCommand.ExecuteAsync(null);
        Assert.NotEmpty(view.LifecycleSubjectHash);
        Assert.True(view.RetainLifecycleRevisionCommand.CanExecute(null));
        view.LifecycleDraftWindows[0].Status = LegalEntityLifecycleStatusDto.WindingDown;
        Assert.Empty(view.LifecycleSubjectHash);
        Assert.False(view.RetainLifecycleRevisionCommand.CanExecute(null));
        var cutoff = view.AsOf;
        view.AsOf = cutoff!.Value.AddDays(1);
        Assert.Equal(cutoff, view.AsOf);
        Assert.Contains("Retain or discard", view.Status);
    }

    [Fact]
    public async Task ExternalReviewRefusalPreservesCompleteTimelineAndReviewerEvidence()
    {
        var client = new Client();
        var view = await ViewAsync(client);
        view.BeginLifecycleRevisionCommand.Execute(null);
        view.LifecycleReason = "Legal status confirmed";
        view.LifecycleEvidenceReference = "evidence://evidence-vault/legal";
        await view.PreviewLifecycleReviewCommand.ExecuteAsync(null);
        view.LifecycleExternalReviewer = "External counsel";
        view.LifecycleReviewerReference = "Matter-42";
        view.LifecycleReviewedOn = "2026-11-12";
        view.LifecycleReviewEvidence = "evidence://evidence-vault/review";
        view.LifecycleReviewArtifact = "retained-document";
        view.LifecycleReviewRationale = "Reviewed exact full timeline";
        await view.RetainLifecycleRevisionCommand.ExecuteAsync(null);
        Assert.NotNull(client.LastAppend);
        Assert.Equal(client.Entity.EntityId, client.LastAppend!.EntityId);
        Assert.Equal(2, client.LastAppend.ExpectedRevision);
        Assert.Equal("External counsel", client.LastAppend.ExternalReview!.ExternalReviewer);
        Assert.Equal(view.LifecycleSubjectHash, client.LastAppend.ExternalReview.SubmittedHash);
        Assert.True(view.IsLifecycleEditing);
        Assert.Single(view.LifecycleDraftWindows);
        Assert.Equal("External counsel", view.LifecycleExternalReviewer);
        Assert.Contains("Authority refused", view.Status);
    }

    [Fact]
    public async Task ReadOnlyTimelineDoesNotOfferLifecycleMutation()
    {
        var client = new Client { CanAttest = false };
        var view = await ViewAsync(client);
        Assert.False(view.BeginLifecycleRevisionCommand.CanExecute(null));
        Assert.False(view.RetainLifecycleRevisionCommand.CanExecute(null));
    }

    [Fact]
    public async Task AccountChangeClearsDirtyLifecycleDraftReviewEvidenceAndCapabilities()
    {
        var view = await ViewAsync(new Client());
        view.BeginLifecycleRevisionCommand.Execute(null);
        view.LifecycleExternalReviewer = "Prior-account counsel";
        view.LifecycleReviewEvidence = "prior-account review";
        view.ResetSession();
        Assert.Empty(view.LifecycleDraftWindows);
        Assert.Empty(view.LifecycleExternalReviewer);
        Assert.Empty(view.LifecycleReviewEvidence);
        Assert.False(view.IsLifecycleDirty);
        Assert.False(view.IsLifecycleEditing);
        Assert.Null(view.SelectedEntity);
        Assert.False(view.BeginLifecycleRevisionCommand.CanExecute(null));
        Assert.Empty(view.TimelineRows);
    }

    private static async Task<EntityConsolidationViewModel> ViewAsync(Client client)
    {
        var view = new EntityConsolidationViewModel(client)
        {
            SelectedOrganization = new(Guid.NewGuid(), "ORG", "Institution", "USD", true, DateTimeOffset.UtcNow.AddYears(-1), null, []),
            SelectedEntity = client.Entity,
            AsOf = new DateTime(2026, 11, 13)
        };
        await view.LoadTimelineAsync();
        return view;
    }
    private sealed class Client : IWorkstationEntityConsolidationApiClient
    {
        public bool CanAttest { get; init; } = true;
        public LegalEntitySummaryDto Entity { get; } = new(Guid.NewGuid(), LegalEntityTypeDto.Other, "ENTITY", "Legal entity", "US", "USD", true, DateTimeOffset.UtcNow.AddYears(-1), null);
        public AppendEntityLifecycleRevisionRequestDto? LastAppend { get; private set; }
        public Task<OrganizationStructureGraphDto> GetStructureAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EntityTimelineDto> GetTimelineAsync(Guid organizationId, Guid entityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            var state = new EntityLifecycleStateDto(Guid.NewGuid(), new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation);
            var revision = new EntityLifecycleRevisionDto("tenant", entityId, 2, DateTimeOffset.UtcNow, "operator", "Reviewed baseline", "evidence://baseline", "", "retained-hash", [state]);
            return Task.FromResult(new EntityTimelineDto(organizationId, Entity, asOf, [revision], [], [], [], [], []) { CanAttestExternalLifecycleReview = CanAttest });
        }
        public Task<IReadOnlyList<LedgerPeriodDto>> GetPeriodsAsync(Guid ledgerBookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConsolidationViewDto> PreviewAsync(ConsolidationRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConsolidationViewDto> CreateDraftsAsync(ConsolidationRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EntityLifecycleReviewPreviewDto> PreviewLifecycleReviewAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
            => Task.FromResult(new EntityLifecycleReviewPreviewDto(EntityLifecycleExternalReviewIntegrity.SubmittedHash(request)));
        public Task<EntityLifecycleRevisionDto> AppendLifecycleRevisionAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
        { LastAppend = request; return Task.FromException<EntityLifecycleRevisionDto>(new InvalidOperationException("Authority refused stale evidence")); }
    }
}
