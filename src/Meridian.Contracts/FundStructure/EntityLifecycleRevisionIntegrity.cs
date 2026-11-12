using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meridian.Contracts.FundStructure;

/// <summary>Canonical immutable revision integrity shared by domain and storage boundaries.</summary>
public static class EntityLifecycleRevisionIntegrity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void ValidateTimeline(Guid entityId, IReadOnlyList<EntityLifecycleStateDto> timeline)
    {
        if (entityId == Guid.Empty || timeline is null || timeline.Count == 0)
            throw new InvalidOperationException("Lifecycle history requires an entity and at least one status window.");
        var ordered = timeline.OrderBy(state => state.EffectiveFrom).ToArray();
        if (ordered.Select(state => state.TransitionId).Any(id => id == Guid.Empty)
            || ordered.Select(state => state.TransitionId).Distinct().Count() != ordered.Length)
            throw new InvalidOperationException("Lifecycle transitions require distinct nonempty identifiers.");
        for (var index = 0; index < ordered.Length; index++)
        {
            var state = ordered[index];
            if (!Enum.IsDefined(state.Status) || !Enum.IsDefined(state.EventKind)
                || state.EffectiveTo.HasValue && state.EffectiveTo.Value <= state.EffectiveFrom)
                throw new InvalidOperationException("Lifecycle status windows must be valid, nonempty and half-open.");
            if (state.SuccessorEntityId == Guid.Empty || state.SuccessorEntityId == entityId)
                throw new InvalidOperationException("A lifecycle successor must be a different entity.");
            if (index + 1 < ordered.Length && (!state.EffectiveTo.HasValue || state.EffectiveTo.Value != ordered[index + 1].EffectiveFrom))
                throw new InvalidOperationException("Lifecycle windows must be contiguous and cannot overlap.");
        }
        if (ordered[^1].EffectiveTo.HasValue)
            throw new InvalidOperationException("The latest lifecycle status must have an open effective window.");
    }

    public static EntityLifecycleRevisionDto Seal(EntityLifecycleRevisionDto revision)
    {
        ValidateTimeline(revision.EntityId, revision.Timeline);
        if (!revision.LegacySnapshot && revision.Timeline.Any(state => state.Status == LegalEntityLifecycleStatusDto.Merged && !state.SuccessorEntityId.HasValue))
            throw new InvalidOperationException("A merged entity requires an evidenced successor entity.");
        if (string.IsNullOrWhiteSpace(revision.TenantId) || string.IsNullOrWhiteSpace(revision.RecordedBy)
            || string.IsNullOrWhiteSpace(revision.Reason) || string.IsNullOrWhiteSpace(revision.EvidenceReference)
            || revision.Revision < 1)
            throw new InvalidOperationException("Lifecycle revisions require tenant, actor, reason, evidence and a positive revision.");
        var normalized = revision with
        {
            TenantId = revision.TenantId.Trim().ToLowerInvariant(),
            RecordedAtUtc = revision.RecordedAtUtc.ToUniversalTime(),
            Timeline = revision.Timeline.OrderBy(state => state.EffectiveFrom).Select(state => state with
            {
                EffectiveFrom = state.EffectiveFrom.ToUniversalTime(),
                EffectiveTo = state.EffectiveTo?.ToUniversalTime()
            }).ToArray(),
            ContentHash = string.Empty
        };
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized, JsonOptions))));
        return normalized with { ContentHash = hash };
    }

    public static void ValidateRevision(EntityLifecycleRevisionDto revision, EntityLifecycleRevisionDto? previous, long expectedRevision)
    {
        var sealedRevision = Seal(revision);
        if (!string.Equals(revision.ContentHash, sealedRevision.ContentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Lifecycle revision content hash does not match its retained payload.");
        if ((previous?.Revision ?? 0) != expectedRevision || revision.Revision != expectedRevision + 1)
            throw new InvalidOperationException("The entity lifecycle revision changed; reload before applying a transition.");
        if (!string.Equals(revision.PreviousHash, previous?.ContentHash ?? string.Empty, StringComparison.Ordinal))
            throw new InvalidOperationException("Lifecycle revision predecessor does not match retained history.");
        if (previous is not null && (!string.Equals(revision.TenantId, previous.TenantId, StringComparison.Ordinal)
            || revision.EntityId != previous.EntityId))
            throw new InvalidOperationException("Lifecycle revisions cannot change entity or tenant ownership.");
    }
}
