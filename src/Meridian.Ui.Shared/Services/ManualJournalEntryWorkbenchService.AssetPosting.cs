using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private async Task<JournalEntryLifecycleActionResultDto> PostAssetAccountingDraftAsync(
        ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionRequestDto request, CancellationToken ct)
    {
        EnsureHumanOrigin(request.ActionOrigin, "post an asset accounting draft");
        EnsureAssetAccountingDraftMatchesSource(draft);
        EnsureLifecycleActorIndependentFromPreparer(draft, request);
        _ = ValidateAssetApprovalReceipt(draft);
        if (!string.Equals(request.Actor, draft.AssetAccountingApproval!.Approver, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The independent operator who approved this asset candidate must execute its posting.");
        var poster = _assetCandidatePosting ?? throw new InvalidOperationException("The governed asset posting workflow is unavailable.");
        var command = _mutationCommand ?? throw new InvalidOperationException("Canonical asset posting requires a retained command lease.");
        if (HasPendingAssetPost(draft))
        {
            if (draft.AssetAccountingPostIntent!.OriginalCommandHash != command.RequestHash)
                throw new InvalidOperationException("Retry the exact retained canonical asset posting command before another mutation.");
        }
        else
        {
            if (draft.Status != ManualJournalEntryStatusDto.Approved || draft.Version != request.Version)
                throw new InvalidOperationException("The asset review draft is not the approved version selected for posting.");
            await EnsureCurrentAssetApprovalSourceAsync(draft, ct).ConfigureAwait(false);
            var publication = await RetainAssetApprovalEvidenceAsync(draft, ct).ConfigureAwait(false);
            var source = draft.AssetAccountingDraft!.Spine.DraftedCandidate!;
            var receipt = draft.AssetAccountingApproval!;
            var post = new PostPostingRuleJournalCandidateRequestDto(source, receipt.Approver, receipt.ApprovalId,
                ApprovalNotes: JsonSerializer.Deserialize<ApprovalDecision>(ValidateAssetApprovalReceipt(draft), AssetReceiptJson)!.Notes,
                EvidenceLinks: [publication.Evidence.EvidenceUri], CorrelationId: receipt.ApprovalId,
                ActionOrigin: request.ActionOrigin, TenantId: draft.TenantId, CompanyId: draft.CompanyId)
            { ApprovalEvidence = [publication.Evidence] };
            var plan = new AssetAccountingPostIntentDto(command.RequestHash, request, post,
                publication.VaultId, publication.DocumentId);
            var planned = draft with { AssetAccountingPostIntent = plan, Version = draft.Version + 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
            // This checkpoint records a command, never financial impact. Its own durable recovery
            // receipt finishes the checkpoint even if the process stops before the draft write.
            _mutationCommand = command with { Key = Sha256Digest.ComputeUtf8(command.Key + "|asset-post-plan") };
            try
            {
                await PersistMutationAsync([planned], ["manual-je.asset-post-intent"], request.Actor, request.CorrelationId,
                    request.ReportGroupPrincipalIds, planned, ct, assetPostPlanForCommandKey: command.Key).ConfigureAwait(false);
            }
            finally { _mutationCommand = command; }
            draft = planned;
        }
        var intent = draft.AssetAccountingPostIntent!;
        var verified = await VerifyAssetApprovalEvidenceAsync(draft, intent.VaultId, intent.DocumentId, ct).ConfigureAwait(false);
        ValidateAssetPostIntent(draft, intent, verified);
        var posted = await poster.PostCandidateAsync(intent.CanonicalRequest, ct).ConfigureAwait(false);
        ValidateAssetPostingOutcome(draft, posted);
        var at = posted.PostedJournal.PostedAtUtc!.Value;
        var transition = BuildTransition(draft.Status, ManualJournalEntryStatusDto.Posted, request, at);
        var next = draft with
        {
            Status = ManualJournalEntryStatusDto.Posted,
            Version = draft.Version + 1,
            PostedAtUtc = at,
            PostedBy = request.Actor,
            UpdatedAtUtc = at,
            AssetAccountingPostingResult = posted,
            LifecycleTransitions = draft.LifecycleTransitions.Append(transition).ToArray()
        };
        var result = new JournalEntryLifecycleActionResultDto(next, transition, PostedJournal: posted.PostedJournal);
        await PersistMutationAsync([next], ["manual-je.asset-post"], request.Actor, request.CorrelationId,
            request.ReportGroupPrincipalIds, result, ct).ConfigureAwait(false);
        return result;
    }

    private static void ValidateAssetPostIntent(ManualJournalEntryDraftDto draft, AssetAccountingPostIntentDto plan,
        RetainedEvidenceIdentityDto verified)
    {
        var receipt = draft.AssetAccountingApproval!;
        var request = plan.CanonicalRequest;
        var original = plan.OriginalCommand;
        var hash = FingerprintRequest(JsonSerializer.SerializeToElement(original,
            ManualJournalMutationJsonContext.Default.JournalEntryLifecycleActionRequestDto), null);
        if (plan.OriginalCommandHash != hash || original.Action != JournalEntryLifecycleActionDto.Post ||
            !string.Equals(original.Actor, receipt.Approver, StringComparison.OrdinalIgnoreCase) || original.JournalEntryId != draft.JournalEntryId ||
            original.FundProfileId != draft.FundProfileId || original.LedgerBookId != draft.LedgerBookId ||
            original.TenantId != draft.TenantId || original.CompanyId != draft.CompanyId || original.RebookLines.Count != 0 ||
            request.Actor != receipt.Approver || request.ApprovalId != receipt.ApprovalId ||
            request.TenantId != draft.TenantId || request.CompanyId != draft.CompanyId ||
            request.CorrelationId != receipt.ApprovalId || request.ActionOrigin != OperationsActionOriginDto.HumanOperator ||
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request.Candidate) != receipt.CandidateFingerprint ||
            request.ApprovalEvidence.Count != 1 || request.ApprovalEvidence[0] != verified ||
            request.EvidenceLinks.Count != 1 || request.EvidenceLinks[0] != verified.EvidenceUri ||
            request.ApprovalNotes != JsonSerializer.Deserialize<ApprovalDecision>(ValidateAssetApprovalReceipt(draft), AssetReceiptJson)!.Notes)
            throw new InvalidOperationException("The retained canonical asset posting intent differs from its exact authorized command.");
    }

    private static void ValidateAssetPostingOutcome(ManualJournalEntryDraftDto draft, PostedPostingRuleJournalCandidateResultDto posted)
    {
        var scope = draft.AssetAccountingDraft!.Spine.Scope;
        var actual = posted.PostedJournal;
        var impact = posted.JournalImpact;
        var approvalAttestedAt = draft.AssetAccountingPostIntent!.CanonicalRequest.ApprovalEvidence.Max(static evidence =>
            evidence.RetainedAtUtc > evidence.ReviewedAtUtc ? evidence.RetainedAtUtc : evidence.ReviewedAtUtc);
        if (actual.JournalEntryId != draft.JournalEntryId || actual.LedgerBookId != draft.LedgerBookId ||
            actual.PeriodId != scope.PeriodId || actual.AccountingBasis != draft.AccountingBasis ||
            actual.SourceEventId != draft.AssetAccountingDraft.Spine.EventId || actual.PostedAtUtc is null ||
            actual.PostedAtUtc > DateTimeOffset.UtcNow ||
            impact is null || impact.JournalEntryId != actual.JournalEntryId || impact.LedgerBookId != scope.LedgerBookId ||
            impact.PeriodId != scope.PeriodId || impact.AccountingBasis != scope.AccountingBasis ||
            impact.PostingStatus != JournalPostingStatusDto.Posted || !impact.IsBalanced ||
            impact.PostedAtUtc != actual.PostedAtUtc || impact.Currency != draft.Currency ||
            impact.TotalDebits != draft.TotalDebits || impact.TotalCredits != draft.TotalCredits ||
            posted.TaxLotMutationBatchId is null || posted.TaxLotMutationBatchId == Guid.Empty ||
            !posted.LifecycleStages.Any(stage => stage.Stage == AssetAccountingLifecycleStageDto.Approved &&
                stage.AttestedBy == draft.AssetAccountingApproval!.Approver &&
                stage.AttestedAtUtc == approvalAttestedAt && stage.ReferenceId == draft.AssetAccountingApproval.ApprovalId) ||
            !posted.LifecycleStages.Any(stage => stage.Stage == AssetAccountingLifecycleStageDto.Posted &&
                stage.AttestedBy == draft.AssetAccountingApproval!.Approver && stage.AttestedAtUtc == actual.PostedAtUtc))
            throw new InvalidOperationException("The canonical asset posting result does not establish the exact reviewed journal and atomic lot impact.");
    }

    private async Task<bool> VerifyCommittedAssetPostingAsync(ManualJournalMutationIntent intent, CancellationToken ct)
    {
        var expected = intent.After.SingleOrDefault(draft => draft.AssetAccountingPostingResult is not null);
        if (expected is null)
            return false;
        var plan = expected.AssetAccountingPostIntent ?? throw new InvalidOperationException("The posted asset receipt has no canonical command.");
        var actual = expected.AssetAccountingPostingResult!;
        var ledger = _journalStore ?? throw new InvalidOperationException("Asset posting recovery requires the authoritative ledger.");
        var matches = await ledger.FindPostingIdentityCollisionsAsync(new LedgerPostingIdentity(actual.PostedJournal.JournalEntryId,
            actual.PostedJournal.AggregateId, actual.PostedJournal.CommandId, actual.PostedJournal.SourceEventId,
            actual.PostedJournal.IdempotencyKey), ct).ConfigureAwait(false);
        if (matches.Count != 1 || matches[0].Entry.JournalEntryId != actual.PostedJournal.JournalEntryId)
            throw new InvalidOperationException("The retained canonical asset journal is missing or conflicting; recovery cannot recreate it.");
        var verified = await VerifyAssetApprovalEvidenceAsync(expected, plan.VaultId, plan.DocumentId, ct).ConfigureAwait(false);
        ValidateAssetPostIntent(expected, plan, verified);
        var replay = await (_assetCandidatePosting ?? throw new InvalidOperationException("The governed asset posting workflow is unavailable."))
            .PostCandidateAsync(plan.CanonicalRequest, ct).ConfigureAwait(false);
        ValidateAssetPostingOutcome(expected, replay);
        if (AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(new
        { actual.PostedJournal, actual.JournalImpact, actual.TaxLotMutationBatchId, actual.LifecycleStages }) !=
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(new
            { replay.PostedJournal, replay.JournalImpact, replay.TaxLotMutationBatchId, replay.LifecycleStages }))
            throw new InvalidOperationException("Canonical asset posting recovery differs from the retained financial outcome.");
        return true;
    }
}
