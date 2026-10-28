using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    /// <summary>
    /// Admits a server-retained asset candidate into the real review queue, preserving its typed
    /// atomic-lot authority. Generic manual approval/posting cannot process this source.
    /// </summary>
    internal async Task<ManualJournalEntryDraftDto> SaveAssetAccountingDraftAsync(
        AssetAccountingPostingCandidateDto retained, string actor, CancellationToken ct)
    {
        var source = retained.Spine.DraftedCandidate
            ?? throw new InvalidOperationException("Asset draft intake requires a retained drafted candidate.");
        var scope = retained.Spine.Scope;
        var journalId = retained.Candidate.JournalEntryId
            ?? throw new InvalidOperationException("Asset draft intake requires its canonical journal identity.");
        if (journalId == Guid.Empty || retained.Spine.DraftedLotMutation?.FactorPaydown is null ||
            retained.Spine.PostedJournalImpact is not null || !string.Equals(source.Actor, actor, StringComparison.Ordinal))
            throw new InvalidOperationException("Asset draft intake requires an unposted factor candidate owned by the preparer.");
        var existing = await _draftStore.GetAsync(scope.FundProfileId, journalId, ct, scope.TenantId, scope.CompanyId).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.AssetAccountingDraft is null || !SameAssetCandidate(existing.AssetAccountingDraft, retained))
                throw new InvalidOperationException("The workbench journal identity already belongs to a different source.");
            EnsureAssetAccountingDraftMatchesSource(existing);
            return existing;
        }
        var lines = retained.Candidate.GeneratedPostingLines.Select(line => new ManualJournalEntryLineDto(
            line.LineId, line.Side, line.Amount, line.Currency, line.AccountPath,
            SecurityId: scope.SecurityId, Description: line.Description,
            EvidenceLink: retained.Candidate.EvidenceLinks.FirstOrDefault(), Dimensions: line.Dimensions)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var dto = new ManualJournalEntryDraftDto(journalId, ManualJournalEntryStatusDto.Draft, scope.FundProfileId,
            scope.LedgerBookId, scope.AccountingBasis, retained.Spine.EffectiveDate, scope.PeriodId.ToString("D"),
            source.Dimensions?.EntityId, null, retained.Spine.Currency, source.Description, actor, now, now, 0,
            lines, retained.Candidate.EvidenceLinks, [], TenantId: scope.TenantId, CompanyId: scope.CompanyId,
            Dimensions: source.Dimensions)
        { AssetAccountingDraft = retained };
        return await SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(dto, actor,
            CorrelationId: $"asset-accounting-draft:{retained.Spine.EventId:D}:{retained.Spine.EventVersion}",
            LedgerBookId: scope.LedgerBookId, TenantId: scope.TenantId, CompanyId: scope.CompanyId), ct).ConfigureAwait(false);
    }

    private static bool SameAssetCandidate(AssetAccountingPostingCandidateDto left, AssetAccountingPostingCandidateDto right)
        => string.Equals(AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(left),
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(right), StringComparison.Ordinal);

    private static void EnsureAssetAccountingSaveAuthority(ManualJournalEntryDraftDto draft,
        ManualJournalEntryDraftDto? existing, bool trustedAutomatedIntake)
    {
        if (existing?.AssetAccountingDraft is not null)
            throw new InvalidOperationException("Asset accounting review drafts are immutable; repair the governed source and create a new candidate.");
        if ((draft.AssetAccountingDraft is not null || draft.AssetAccountingApproval is not null ||
             draft.AssetAccountingPostIntent is not null || draft.AssetAccountingPostingResult is not null) && !trustedAutomatedIntake)
            throw new InvalidOperationException("Asset accounting authority can be retained only by governed server intake.");
    }

    private void EnsureAssetAccountingLifecycleAuthority(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action)
    {
        if (draft.AssetAccountingDraft is not null &&
            ((action == JournalEntryLifecycleActionDto.Approve && _assetAccountingSource.Value is null) ||
             (action == JournalEntryLifecycleActionDto.Post && (_assetCandidatePosting.Value is null || _assetEvidenceArtifacts is null)) ||
             action is not (JournalEntryLifecycleActionDto.Validate or JournalEntryLifecycleActionDto.Submit or
                JournalEntryLifecycleActionDto.Reject or JournalEntryLifecycleActionDto.Approve or JournalEntryLifecycleActionDto.Post)))
            throw new InvalidOperationException("Asset accounting drafts require typed retained approval evidence and the governed asset posting workflow; generic manual approval, posting and corrections are unavailable.");
    }

    private static void EnsureAssetAccountingDraftMatchesSource(ManualJournalEntryDraftDto draft)
    {
        if (draft.AssetAccountingDraft is not { } retained)
        {
            if (draft.AssetAccountingApproval is not null || draft.AssetAccountingPostIntent is not null || draft.AssetAccountingPostingResult is not null)
                throw new InvalidOperationException("Asset accounting receipts require their immutable governed source draft.");
            return;
        }
        var source = retained.Spine.DraftedCandidate
            ?? throw new InvalidOperationException("The asset review draft has no retained candidate authority.");
        var scope = retained.Spine.Scope;
        var lines = retained.Candidate.GeneratedPostingLines;
        if (!string.Equals(retained.Spine.DraftedCandidateFingerprint,
                AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(source), StringComparison.Ordinal) ||
            !string.Equals(retained.Spine.DraftedCandidateResultFingerprint,
                AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(retained.Candidate), StringComparison.Ordinal) ||
            !string.Equals(retained.Spine.DraftedLotMutationFingerprint,
                AssetLotMutationInstructionValidator.Fingerprint(retained.Spine.DraftedLotMutation), StringComparison.Ordinal))
            throw new InvalidOperationException("The asset review draft no longer matches the retained source fingerprints.");
        if (retained.Candidate.JournalEntryId != draft.JournalEntryId || scope.LedgerBookId != draft.LedgerBookId ||
            scope.PeriodId.ToString("D") != draft.PeriodId || scope.AccountingBasis != draft.AccountingBasis ||
            scope.FundProfileId != draft.FundProfileId || scope.TenantId != draft.TenantId || scope.CompanyId != draft.CompanyId ||
            retained.Spine.EffectiveDate != draft.AccountingDate || retained.Spine.Currency != draft.Currency ||
            source.Description != draft.Memo || source.Actor != draft.PreparedBy ||
            retained.Spine.DraftedLotMutation?.FactorPaydown is null || lines.Count != draft.Lines.Count ||
            !retained.Candidate.EvidenceLinks.All(uri => draft.EvidenceLinks.Contains(uri, StringComparer.Ordinal)))
            throw new InvalidOperationException("The asset accounting review draft differs from its immutable source authority.");
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].LineId != draft.Lines[i].LineId || lines[i].AccountPath != draft.Lines[i].AccountPath ||
                lines[i].Amount != draft.Lines[i].Amount || lines[i].Currency != draft.Lines[i].Currency || lines[i].Side != draft.Lines[i].Side)
                throw new InvalidOperationException("The asset accounting review lines differ from their immutable generated candidate.");
    }
}
