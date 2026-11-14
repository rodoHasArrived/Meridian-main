using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.FundStructure;

internal static class IntercompanyExternalReviewValidation
{
    internal static void Validate(IntercompanyExternalReviewDto review, string submittedHash,
        string actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(review.ExternalReviewer) || string.IsNullOrWhiteSpace(review.ReviewerReference)
            || string.IsNullOrWhiteSpace(review.Rationale) || review.ReviewedOn == default
            || review.ReviewedOn > DateOnly.FromDateTime(now.UtcDateTime)
            || string.Equals(review.ExternalReviewer.Trim(), actor.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new IntercompanyAgreementValidationException(
                "External review requires a distinct named external reviewer, reviewer reference, non-future review date and attestation rationale.");
        if (!Sha256Digest.FixedEquals(review.SubmittedHash, submittedHash))
            throw new IntercompanyAgreementConflictException("External review must bind the exact submitted subject hash.");
        if (review.Evidence is null || string.IsNullOrWhiteSpace(review.Evidence.Reference))
            throw new IntercompanyAgreementValidationException("An exact retained external-review document is required.");
    }

    internal static IntercompanyExternalReviewAttestationDto Retain(IntercompanyExternalReviewDto review,
        IntercompanyAgreementEvidenceDto resolved, string submittedHash, string actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(resolved.Reference) || string.IsNullOrWhiteSpace(resolved.ArtifactId)
            || !Sha256Digest.IsCanonical(resolved.ContentHash))
            throw new IntercompanyAgreementValidationException("External-review authority must resolve an immutable document artifact and canonical source hash.");
        return new(review.ExternalReviewer.Trim(), review.ReviewerReference.Trim(), review.ReviewedOn,
            resolved, review.Rationale.Trim(), submittedHash, actor, now);
    }
}
