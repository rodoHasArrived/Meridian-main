using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.FundStructure;

/// <summary>A human operator attests an external review of this exact submitted subject.</summary>
public sealed record IntercompanyExternalReviewDto(
    string ExternalReviewer, string ReviewerReference, DateOnly ReviewedOn,
    IntercompanyAgreementEvidenceDto Evidence, string Rationale, string SubmittedHash);

/// <summary>The external reviewer is not represented as an authenticated Meridian approver.</summary>
public sealed record IntercompanyExternalReviewAttestationDto(
    string ExternalReviewer, string ReviewerReference, DateOnly ReviewedOn,
    IntercompanyAgreementEvidenceDto Evidence, string Rationale, string SubmittedHash,
    string Operator, DateTimeOffset RecordedAtUtc);

/// <summary>Retained legal review authority is either an independent actor or an explicit external attestation.</summary>
public static class IntercompanyAgreementReviewIntegrity
{
    public static void Validate(IntercompanyAgreementRevisionDto revision)
    {
        if (string.IsNullOrWhiteSpace(revision.Approver) || string.IsNullOrWhiteSpace(revision.Submitter)
            || revision.Authors is null || revision.Authors.Count == 0)
            throw new IntercompanyAgreementValidationException("Approved legal revisions require retained authors, submitter and authenticated decision actor.");
        var sameActor = string.Equals(revision.Approver, revision.Submitter, StringComparison.OrdinalIgnoreCase)
            || revision.Authors.Any(author => string.Equals(author, revision.Approver, StringComparison.OrdinalIgnoreCase));
        if (revision.ExternalReview is not { } review)
        {
            if (sameActor)
                throw new IntercompanyAgreementForbiddenException("A preparer cannot independently approve legal terms without a retained external-review attestation.");
            return;
        }
        if (!Sha256Digest.FixedEquals(review.SubmittedHash, revision.PayloadHash)
            || review.Operator != revision.Approver || review.RecordedAtUtc != revision.ApprovedAtUtc
            || string.IsNullOrWhiteSpace(review.ExternalReviewer) || string.IsNullOrWhiteSpace(review.ReviewerReference)
            || string.Equals(review.ExternalReviewer, review.Operator, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(review.Rationale) || review.ReviewedOn == default
            || review.ReviewedOn > DateOnly.FromDateTime(review.RecordedAtUtc.UtcDateTime)
            || review.Evidence is null || string.IsNullOrWhiteSpace(review.Evidence.Reference)
            || string.IsNullOrWhiteSpace(review.Evidence.ArtifactId) || !Sha256Digest.IsCanonical(review.Evidence.ContentHash))
            throw new IntercompanyAgreementValidationException("Retained external legal review must bind this exact payload, operator, decision date and immutable document source.");
    }
}

public sealed record EntityLifecycleReviewPayloadDto(Guid EntityId, long ExpectedRevision,
    IReadOnlyList<EntityLifecycleStateDto> Timeline, string Reason, string EvidenceReference);

/// <summary>Computes the legal lifecycle subject separately from its external-review receipt.</summary>
public static class EntityLifecycleExternalReviewIntegrity
{
    public static string SubmittedHash(AppendEntityLifecycleRevisionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = new EntityLifecycleReviewPayloadDto(request.EntityId, request.ExpectedRevision,
            request.Timeline.OrderBy(state => state.EffectiveFrom).Select(state => state with
            {
                EffectiveFrom = state.EffectiveFrom.ToUniversalTime(),
                EffectiveTo = state.EffectiveTo?.ToUniversalTime()
            }).ToArray(), request.Reason, request.EvidenceReference);
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(payload,
            IntercompanyExternalReviewJsonContext.Default.EntityLifecycleReviewPayloadDto));
    }
}

/// <summary>Resolves immutable, scoped external-review document evidence for legal lifecycle facts.</summary>
public interface IEntityLifecycleExternalReviewEvidenceAuthority
{
    Task<IntercompanyAgreementEvidenceDto> ResolveAsync(string tenantId, string actor, Guid entityId,
        IntercompanyAgreementEvidenceDto evidence, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntercompanyExternalReviewDto))]
[JsonSerializable(typeof(IntercompanyExternalReviewAttestationDto))]
[JsonSerializable(typeof(EntityLifecycleReviewPayloadDto))]
public sealed partial class IntercompanyExternalReviewJsonContext : JsonSerializerContext;
