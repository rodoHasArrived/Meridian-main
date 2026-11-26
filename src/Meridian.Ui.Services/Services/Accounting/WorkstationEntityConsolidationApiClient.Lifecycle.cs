using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Services.Services.Accounting;

public sealed record EntityLifecycleReviewPreviewDto(string SubmittedHash);

public partial interface IWorkstationEntityConsolidationApiClient
{
    Task<EntityLifecycleReviewPreviewDto> PreviewLifecycleReviewAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
        => Task.FromException<EntityLifecycleReviewPreviewDto>(new InvalidOperationException("Lifecycle review preview is unavailable."));
    Task<EntityLifecycleRevisionDto> AppendLifecycleRevisionAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
        => Task.FromException<EntityLifecycleRevisionDto>(new InvalidOperationException("Reviewed lifecycle changes are unavailable."));
}

public sealed partial class WorkstationEntityConsolidationApiClient
{
    public async Task<EntityLifecycleReviewPreviewDto> PreviewLifecycleReviewAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
        => Unwrap(await api.PostWithResponseAsync<EntityLifecycleReviewPreviewDto>($"/api/fund-structure/entities/{request.EntityId:D}/lifecycle/review-preview", request with { ExternalReview = null }, ct).ConfigureAwait(false));
    public async Task<EntityLifecycleRevisionDto> AppendLifecycleRevisionAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
    {
        var result = Unwrap(await api.PostWithResponseAsync<EntityLifecycleRevisionDto>($"/api/fund-structure/entities/{request.EntityId:D}/lifecycle/revisions", request, ct).ConfigureAwait(false));
        if (result.EntityId != request.EntityId || result.Revision != request.ExpectedRevision + 1)
            throw new InvalidOperationException("The retained lifecycle revision belongs to a different entity or predecessor.");
        return result;
    }
}
