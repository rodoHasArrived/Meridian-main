using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    internal async Task<AssetAccountingDraftCapabilitiesDto> GetAssetAccountingCapabilitiesAsync(
        ManualJournalEntryDraftDto draft, string actor, bool canCertify, CancellationToken ct)
    {
        var source = draft.AssetAccountingDraft?.Spine
            ?? throw new InvalidOperationException("This journal is not an asset accounting review draft.");
        var pending = HasPendingAssetPost(draft);
        var independent = !string.Equals(actor, draft.PreparedBy, StringComparison.OrdinalIgnoreCase);
        string? approveReason = null;
        string? postReason = null;
        if (!canCertify || !independent)
            approveReason = postReason = "An authenticated AdminMaintenance operator independent from the preparer is required.";
        if (pending)
            approveReason = "A retained canonical posting command must finish before another lifecycle action.";
        else if (draft.Status != ManualJournalEntryStatusDto.Submitted)
            approveReason ??= "Submit the immutable asset draft for review before approval.";
        if (draft.Status != ManualJournalEntryStatusDto.Approved)
            postReason ??= "The asset draft requires a retained independent approval before posting.";
        if (_assetAccountingSource is null)
            approveReason ??= "The governed asset posting workflow source is unavailable.";
        if (_assetEvidenceArtifacts is null || _assetCandidatePosting is null)
            postReason ??= "The governed asset posting workflow requires canonical posting and retained evidence storage.";
        if (draft.AssetAccountingApproval is { } receipt && !string.Equals(actor, receipt.Approver, StringComparison.OrdinalIgnoreCase))
            postReason ??= "Only the independent operator who approved this candidate may post it.";
        if (pending && !string.Equals(actor, draft.AssetAccountingPostIntent!.OriginalCommand.Actor, StringComparison.OrdinalIgnoreCase))
            postReason ??= "The retained posting command belongs to another independent operator.";
        try
        {
            EnsureAssetAccountingDraftMatchesSource(draft);
            if (!pending && (approveReason is null || postReason is null))
                await EnsureCurrentAssetApprovalSourceAsync(draft, ct).ConfigureAwait(false);
            if (postReason is null)
                _ = ValidateAssetApprovalReceipt(draft);
        }
        catch (InvalidOperationException ex)
        {
            approveReason ??= ex.Message;
            postReason ??= ex.Message;
        }
        return new AssetAccountingDraftCapabilitiesDto(1, draft.JournalEntryId, draft.Version, draft.FundProfileId,
            source.Scope.LedgerBookId, source.Scope.PeriodId, source.DraftedCandidateFingerprint!,
            approveReason is null, postReason is null, approveReason, postReason, draft.AssetAccountingApproval,
            pending, pending && postReason is null ? draft.AssetAccountingPostIntent!.OriginalCommand : null,
            draft.AssetAccountingPostingResult?.PostedJournal, actor);
    }
}
