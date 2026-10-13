using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Supplement missing terms without rewriting a retained draft or its original approval.</summary>
internal static class SpineAcquisitionLotCompletion
{
    internal static AssetAcquisitionLotDto? Resolve(PostPostingRuleJournalCandidateRequestDto request)
    {
        var candidate = request.Candidate;
        var acquisition = candidate.AssetLotMutation?.Acquisition;
        if (request.AcquisitionTermsCompletion is not { } completion)
            return acquisition;
        if (completion.AmortizationMethod is null)
            throw new InvalidOperationException("Acquisition terms completion requires an explicit amortization method.");

        if (candidate.SourceEventType != AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Acquisition)
            || candidate.AssetLotMutation is not { Intent: AssetLotMutationIntentDto.Acquire }
            || acquisition is not { HasFaceValueTerms: true, AmortizationMethod: null }
            || acquisition.EffectiveYield is not null
            || completion.TaxLotRecordId != acquisition.TaxLotRecordId
            || completion.DraftedCandidateFingerprint != AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate))
            throw new InvalidOperationException("Acquisition terms completion must bind the exact retained face draft and lot whose amortization terms are missing.");

        var evidence = completion.Evidence;
        if (!RetainedEvidenceIdentityValidator.IsComplete(evidence)
            || evidence.SubjectType != AssetAcquisitionTermsCompletionDto.EvidenceSubjectType
            || evidence.SubjectId != completion.ApprovalSubjectId()
            || evidence.EffectiveDate != candidate.EffectiveDate
            || !string.Equals(evidence.ReviewedBy, request.Actor.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(evidence.ReviewedBy, candidate.Actor.Trim(), StringComparison.OrdinalIgnoreCase)
            || evidence.ReviewedAtUtc < candidate.AccountingTimestamp
            || evidence.RetainedAtUtc < evidence.ReviewedAtUtc
            || evidence.ReviewedAtUtc > DateTimeOffset.UtcNow || evidence.RetainedAtUtc > DateTimeOffset.UtcNow
            || candidate.RetainedEvidence.Concat(request.ApprovalEvidence)
                .Any(item => string.Equals(item.EvidenceId, evidence.EvidenceId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Acquisition terms completion requires independent retained review bound to this draft, lot, method and yield.");

        var completed = acquisition with
        {
            AmortizationMethod = completion.AmortizationMethod,
            EffectiveYield = completion.EffectiveYield
        };
        var issues = AssetLotMutationInstructionValidator.Validate(
            AssetAccountingEventKindDto.Acquisition, candidate.AssetLotMutation with { Acquisition = completed },
            candidate.EventAmount, candidate.EffectiveDate, candidate.RetainedEvidence);
        if (issues.Count > 0)
            throw new InvalidOperationException($"Acquisition terms completion is invalid: {string.Join(" ", issues)}");
        try
        {
            SpineAcquisitionLotFacts.RequireCompleteNewAcquisition(completed);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"Acquisition terms completion is invalid: {exception.Message}", exception);
        }
        return completed;
    }

    internal static IReadOnlyList<RetainedEvidenceIdentityDto> PostingApprovalEvidence(PostPostingRuleJournalCandidateRequestDto request)
        => request.AcquisitionTermsCompletion is { } completion
            ? [.. request.ApprovalEvidence, completion.Evidence]
            : request.ApprovalEvidence;
}
