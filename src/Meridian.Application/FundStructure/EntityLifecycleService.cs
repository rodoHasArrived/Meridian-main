using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>Authorizes history against the scoped graph; storage owns immutable concurrency.</summary>
public sealed class EntityLifecycleService(
    IFundStructureService structure,
    IEntityLifecycleStore store,
    IFundScopeTenantAccessor tenantAccessor,
    IEntityLifecycleExternalReviewEvidenceAuthority? externalReviewEvidence = null,
    TimeProvider? timeProvider = null) : IEntityLifecycleService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private string Tenant() => !string.IsNullOrWhiteSpace(tenantAccessor.ResolveCallerTenant())
        && !tenantAccessor.ResolveCallerTenant()!.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
        ? tenantAccessor.ResolveCallerTenant()!.Trim().ToLowerInvariant()
        : throw new FundStructureTenantScopeException("An explicit tenant is required for entity lifecycle history.");

    private async Task<OrganizationStructureGraphDto> RequireEntityAsync(Guid entityId, CancellationToken ct)
    {
        var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false), ct).ConfigureAwait(false);
        if (!graph.Entities.Any(entity => entity.EntityId == entityId))
            throw new InvalidOperationException("The legal entity was not found in the calling tenant scope.");
        return graph;
    }

    public async Task<EntityLifecycleRevisionDto?> GetAsync(Guid entityId, long? revision = null, CancellationToken ct = default)
    {
        var tenant = Tenant();
        await RequireEntityAsync(entityId, ct).ConfigureAwait(false);
        return revision.HasValue
            ? await store.GetRevisionAsync(tenant, entityId, revision.Value, ct).ConfigureAwait(false)
            : await store.GetLatestAsync(tenant, entityId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityLifecycleRevisionDto>> GetHistoryAsync(Guid entityId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        await RequireEntityAsync(entityId, ct).ConfigureAwait(false);
        return await store.GetHistoryAsync(tenant, entityId, ct).ConfigureAwait(false);
    }

    public async Task<EntityLifecycleRevisionDto> TransitionAsync(AppendEntityLifecycleRevisionRequestDto request, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = Tenant();
        if (string.IsNullOrWhiteSpace(actor))
            throw new InvalidOperationException("An authenticated lifecycle actor is required.");
        var graph = await RequireEntityAsync(request.EntityId, ct).ConfigureAwait(false);
        EntityLifecyclePolicy.ValidateTimeline(request.EntityId, request.Timeline);
        if (request.ExternalReview is { } retryReview && request.ExpectedRevision >= 0 && request.ExpectedRevision < long.MaxValue)
        {
            var retryRevision = await store.GetRevisionAsync(tenant, request.EntityId, request.ExpectedRevision + 1, ct).ConfigureAwait(false);
            if (retryRevision?.ExternalReview is { } receipt && receipt.Operator == actor.Trim()
                && receipt.SubmittedHash == EntityLifecycleExternalReviewIntegrity.SubmittedHash(request)
                && SameExternalReview(retryReview, receipt))
            {
                await ApplyLatestProjectionAsync(tenant, request.EntityId, ct).ConfigureAwait(false);
                return retryRevision;
            }
        }
        foreach (var successorId in request.Timeline.Where(state => state.SuccessorEntityId.HasValue).Select(state => state.SuccessorEntityId!.Value).Distinct())
        {
            if (!graph.Entities.Any(entity => entity.EntityId == successorId))
                throw new InvalidOperationException("A lifecycle successor must be visible in the same tenant scope.");
            var visited = new HashSet<Guid> { request.EntityId };
            var next = successorId;
            while (true)
            {
                if (!visited.Add(next))
                    throw new InvalidOperationException("Lifecycle successors cannot form a cycle.");
                var history = await store.GetLatestAsync(tenant, next, ct).ConfigureAwait(false);
                var successor = history?.Timeline.LastOrDefault()?.SuccessorEntityId;
                if (!successor.HasValue)
                    break;
                next = successor.Value;
            }
        }
        var previous = await store.GetLatestAsync(tenant, request.EntityId, ct).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        IntercompanyExternalReviewAttestationDto? externalReview = null;
        if (request.ExternalReview is { } review)
        {
            var subjectHash = EntityLifecycleExternalReviewIntegrity.SubmittedHash(request);
            IntercompanyExternalReviewValidation.Validate(review, subjectHash, actor, now);
            var authority = externalReviewEvidence ?? throw new InvalidOperationException("Retained lifecycle external-review authority is unavailable.");
            var resolved = await authority.ResolveAsync(tenant, actor, request.EntityId, review.Evidence, ct).ConfigureAwait(false);
            externalReview = IntercompanyExternalReviewValidation.Retain(review, resolved, subjectHash, actor.Trim(), now);
        }
        var revision = EntityLifecyclePolicy.Seal(new EntityLifecycleRevisionDto(tenant, request.EntityId,
            request.ExpectedRevision + 1, now, actor.Trim(), request.Reason, request.EvidenceReference,
            previous?.ContentHash ?? string.Empty, string.Empty, request.Timeline)
        { ExternalReview = externalReview });
        var retained = await store.AppendAsync(revision, request.ExpectedRevision, ct).ConfigureAwait(false);
        await ApplyLatestProjectionAsync(tenant, request.EntityId, ct).ConfigureAwait(false);
        return retained;
    }

    private async Task ApplyLatestProjectionAsync(string tenant, Guid entityId, CancellationToken ct)
    {
        if (structure is IEntityLifecycleProjectionSink projection)
        {
            // Appending history and persisting a local profile are separate operations. A retry
            // must repair that projection without replaying an older receipt over later history.
            await using var lease = await store.AcquireReadLeaseAsync(tenant, [entityId], ct).ConfigureAwait(false);
            var latest = await store.GetLatestAsync(tenant, entityId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Retained lifecycle history is unavailable for projection.");
            await projection.ApplyLifecycleProjectionAsync(latest, ct).ConfigureAwait(false);
        }
    }

    private static bool SameExternalReview(IntercompanyExternalReviewDto requested,
        IntercompanyExternalReviewAttestationDto retained)
        => requested.ExternalReviewer?.Trim() == retained.ExternalReviewer
            && requested.ReviewerReference?.Trim() == retained.ReviewerReference
            && requested.ReviewedOn == retained.ReviewedOn && requested.Rationale?.Trim() == retained.Rationale
            && requested.SubmittedHash == retained.SubmittedHash && requested.Evidence is not null
            && requested.Evidence.Reference == retained.Evidence.Reference
            && (string.IsNullOrWhiteSpace(requested.Evidence.ArtifactId) || requested.Evidence.ArtifactId == retained.Evidence.ArtifactId)
            && (string.IsNullOrWhiteSpace(requested.Evidence.ContentHash) || string.Equals(requested.Evidence.ContentHash,
                retained.Evidence.ContentHash, StringComparison.OrdinalIgnoreCase));
}
