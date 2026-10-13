using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed partial class AssetAcquisitionLotPostgresRoundTripTests
{
    [LedgerDatabaseFact]
    public Task LegacyFaceDraft_ReviewedCompletionPostsAndReplaysWithoutRewritingDraft()
        => AssertSpineAcquisitionAsync(true, BondAmortizationMethod.NoAmortization, legacyDraft: true);

    [LedgerDatabaseFact]
    public Task LegacyApprovedFaceDraft_ReviewedCompletionPostsAndReplaysWithoutRewritingApproval()
        => AssertSpineAcquisitionAsync(true, BondAmortizationMethod.NoAmortization, legacyDraft: true, retainedApproval: true);

    private static PostPostingRuleJournalCandidateRequestDto CompleteLegacyTerms(
        PostPostingRuleJournalCandidateRequestDto request, BondAmortizationMethod method, decimal? effectiveYield = null)
    {
        var reviewedAt = DateTimeOffset.UtcNow;
        var evidence = request.ApprovalEvidence[0] with
        {
            EvidenceId = $"acquisition-completion-{request.ApprovalId}",
            EvidenceUri = $"document://acquisitions/{request.Candidate.SourceEventId:D}/terms-completion",
            ContentHashSha256 = new string('f', 64),
            SourceReference = $"completion:{request.ApprovalId}",
            ReviewedAtUtc = reviewedAt,
            RetainedAtUtc = reviewedAt,
            SubjectType = AssetAcquisitionTermsCompletionDto.EvidenceSubjectType
        };
        var completion = new AssetAcquisitionTermsCompletionDto(
            request.Candidate.AssetLotMutation!.Acquisition!.TaxLotRecordId,
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request.Candidate), method, effectiveYield, evidence);
        return request with
        {
            AcquisitionTermsCompletion = completion with
            { Evidence = evidence with { SubjectId = completion.ApprovalSubjectId() } }
        };
    }

    private static IEnumerable<(PostPostingRuleJournalCandidateRequestDto Request, string Error)> InvalidReviewedCompletions(
        PostPostingRuleJournalCandidateRequestDto request)
    {
        yield return (CompleteLegacyTerms(request, BondAmortizationMethod.ConstantYield, -1m), "*effective yield greater than -1*");
        yield return (CompleteLegacyTerms(request, BondAmortizationMethod.ConstantYield, -1.01m), "*effective yield greater than -1*");
        var completion = request.AcquisitionTermsCompletion!;
        yield return (request with
        {
            AcquisitionTermsCompletion = completion with
            { Evidence = completion.Evidence with { ReviewedAtUtc = request.Candidate.AccountingTimestamp.AddTicks(-1) } }
        }, "*completion review cannot precede the retained Drafted attestation*");
    }

    private sealed class LegacyAcquisitionDraftFixtureStore(IAssetAccountingEventProjectionStore inner)
        : IAssetAccountingEventProjectionStore
    {
        public Task<AssetAccountingEventProjectionRecord?> GetAsync(Guid eventId, long eventVersion, long spineVersion,
            CancellationToken ct = default) => inner.GetAsync(eventId, eventVersion, spineVersion, ct);

        public Task<AssetAccountingEventProjectionRecord?> GetLatestAsync(Guid eventId, long eventVersion,
            CancellationToken ct = default) => inner.GetLatestAsync(eventId, eventVersion, ct);

        public Task<AssetAccountingEventAppendResult> AppendAsync(AssetAccountingEventSpineDto projection,
            long expectedSpineVersion, long expectedBookPositionVersion, CancellationToken ct = default)
        {
            if (projection.Stages[^1].Stage == AssetAccountingLifecycleStageDto.Drafted)
            {
                // Seed the wire shape retained by the previous writer before its first append.
                // Historical records and immutable-row triggers are never rewritten or disabled.
                var instruction = projection.DraftedLotMutation! with
                { Acquisition = projection.DraftedLotMutation!.Acquisition! with { AmortizationMethod = null, EffectiveYield = null } };
                var candidate = projection.DraftedCandidate! with { AssetLotMutation = instruction };
                projection = projection with
                {
                    DraftedCandidate = candidate,
                    DraftedCandidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate),
                    DraftedLotMutation = instruction,
                    DraftedLotMutationFingerprint = AssetLotMutationInstructionValidator.Fingerprint(instruction)
                };
            }
            return inner.AppendAsync(projection, expectedSpineVersion, expectedBookPositionVersion, ct);
        }
    }
}
