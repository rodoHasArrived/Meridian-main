using System.Text;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private readonly IEvidenceArtifactStore? _assetEvidenceArtifacts;
    private readonly IAccountingPostingCandidatePostService? _assetCandidatePosting;
    private readonly IAssetAccountingEventSpineService? _assetAccountingSource;
    private static readonly JsonSerializerOptions AssetReceiptJson = new(JsonSerializerDefaults.Web);
    private const int MaxAssetApprovalReceiptBytes = 1024 * 1024;

    private sealed record ApprovalDecision(int ProtocolVersion, string Decision, Guid JournalEntryId,
        string ApprovalId, string Actor, DateTimeOffset ApprovedAtUtc, string? TenantId, string? CompanyId,
        string FundProfileId, Guid LedgerBookId, Guid PeriodId, AccountingBasisKindDto AccountingBasis,
        Guid EventId, long EventVersion, string CandidateFingerprint, string SubjectId, string? Notes);

    private static bool HasPendingAssetPost(ManualJournalEntryDraftDto draft)
        => draft.AssetAccountingPostIntent is not null && draft.AssetAccountingPostingResult is null;

    private static void EnsureNoPendingAssetPost(ManualJournalEntryDraftDto draft)
    {
        if (HasPendingAssetPost(draft))
            throw new InvalidOperationException("A retained canonical asset posting must finish before another journal mutation or validation.");
    }

    private async Task EnsureCurrentAssetApprovalSourceAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        EnsureAssetAccountingDraftMatchesSource(draft);
        var source = draft.AssetAccountingDraft!.Spine;
        var service = _assetAccountingSource ?? throw new InvalidOperationException("The governed asset posting workflow source is unavailable.");
        var current = await service.GetLatestAsync(source.EventId, source.EventVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained asset candidate authority is unavailable.");
        if (current.PostedJournalImpact is not null || current.DraftedCandidateFingerprint != source.DraftedCandidateFingerprint ||
            current.DraftedCandidateResultFingerprint != source.DraftedCandidateResultFingerprint ||
            current.DraftedLotMutationFingerprint != source.DraftedLotMutationFingerprint ||
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(current.Scope) !=
                AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(source.Scope))
            throw new InvalidOperationException("The retained asset candidate no longer matches the reviewed draft.");
        var period = _journalStore is null ? null : await _journalStore.GetPeriodAsync(source.Scope.PeriodId, ct).ConfigureAwait(false);
        if (period is null || period.LedgerBookId != source.Scope.LedgerBookId || period.Status != "Open" ||
            period.Version != source.DraftedCandidate!.ExpectedPeriodVersion)
            throw new InvalidOperationException("The retained asset candidate period is missing, closed or stale.");
    }

    private async Task<JournalEntryLifecycleActionResultDto> ApproveAssetAccountingDraftAsync(
        ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionRequestDto request, DateTimeOffset now, CancellationToken ct)
    {
        EnsureLifecycleActorIndependentFromPreparer(draft, request);
        EnsureNoPendingAssetPost(draft);
        await EnsureCurrentAssetApprovalSourceAsync(draft, ct).ConfigureAwait(false);
        if (draft.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
            throw new InvalidOperationException("Asset approval is blocked while critical validation issues remain.");
        var source = draft.AssetAccountingDraft!.Spine;
        var draftedAt = source.Stages.SingleOrDefault(stage => stage.Stage == AssetAccountingLifecycleStageDto.Drafted)?.AttestedAtUtc;
        if (draftedAt is null || draftedAt > now)
            throw new InvalidOperationException("Approval requires a current non-future retained Drafted attestation.");
        var approvalId = draft.ApprovalId ?? $"asset-approval-{draft.JournalEntryId:N}";
        var fingerprint = source.DraftedCandidateFingerprint!;
        var subject = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(source.EventId, source.EventVersion,
            draft.FundProfileId, source.Scope.LedgerBookId, source.Scope.PeriodId, draft.AccountingBasis,
            approvalId, fingerprint, draft.TenantId, draft.CompanyId);
        var decision = new ApprovalDecision(1, "Approve", draft.JournalEntryId, approvalId, request.Actor,
            now, draft.TenantId, draft.CompanyId, draft.FundProfileId, source.Scope.LedgerBookId,
            source.Scope.PeriodId, draft.AccountingBasis, source.EventId, source.EventVersion, fingerprint, subject, request.Notes);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(decision, AssetReceiptJson);
        if (bytes.Length > MaxAssetApprovalReceiptBytes)
            throw new InvalidOperationException("The approval command receipt exceeds the supported size.");
        var receipt = new AssetAccountingApprovalReceiptDto(approvalId, request.Actor, now, fingerprint, subject,
            Convert.ToBase64String(bytes), Sha256Digest.Compute(bytes));
        return await ApplyStatusTransitionAsync(draft, request, ManualJournalEntryStatusDto.Approved,
            "manual-je.asset-approve", now, ct, item => item with
            {
                ApprovalId = approvalId,
                ApprovedAtUtc = now,
                ApprovedBy = request.Actor,
                AssetAccountingApproval = receipt
            }).ConfigureAwait(false);
    }

    private static byte[] ValidateAssetApprovalReceipt(ManualJournalEntryDraftDto draft)
    {
        EnsureAssetAccountingDraftMatchesSource(draft);
        var receipt = draft.AssetAccountingApproval ?? throw new InvalidOperationException("The asset draft has no authenticated approval receipt.");
        if (string.IsNullOrWhiteSpace(receipt.ApprovalId) || string.IsNullOrWhiteSpace(receipt.Approver) ||
            string.IsNullOrWhiteSpace(receipt.CandidateFingerprint) || string.IsNullOrWhiteSpace(receipt.SubjectId) ||
            string.IsNullOrWhiteSpace(receipt.CanonicalContentBase64) || string.IsNullOrWhiteSpace(receipt.ContentHashSha256) ||
            receipt.CanonicalContentBase64.Length > ((MaxAssetApprovalReceiptBytes + 2) / 3) * 4 ||
            receipt.ApprovedAtUtc == default || receipt.ApprovedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The asset approval receipt identity, timestamp or content is incomplete.");
        byte[] bytes;
        try
        { bytes = Convert.FromBase64String(receipt.CanonicalContentBase64); }
        catch (FormatException) { throw new InvalidOperationException("The asset approval receipt bytes are invalid."); }
        if (bytes.Length == 0 || bytes.Length > MaxAssetApprovalReceiptBytes ||
            !string.Equals(Sha256Digest.Compute(bytes), receipt.ContentHashSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The asset approval receipt content hash is invalid.");
        ApprovalDecision decision;
        try
        {
            decision = JsonSerializer.Deserialize<ApprovalDecision>(bytes, AssetReceiptJson)
            ?? throw new InvalidOperationException("The asset approval command receipt is empty.");
        }
        catch (JsonException) { throw new InvalidOperationException("The asset approval command receipt is malformed."); }
        var source = draft.AssetAccountingDraft!.Spine;
        var expectedSubject = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(source.EventId, source.EventVersion,
            draft.FundProfileId, source.Scope.LedgerBookId, source.Scope.PeriodId, draft.AccountingBasis,
            receipt.ApprovalId, source.DraftedCandidateFingerprint!, draft.TenantId, draft.CompanyId);
        if (decision.ProtocolVersion != 1 || decision.Decision != "Approve" || decision.JournalEntryId != draft.JournalEntryId ||
            decision.ApprovalId != receipt.ApprovalId || receipt.ApprovalId != draft.ApprovalId ||
            decision.Actor != receipt.Approver || receipt.Approver != draft.ApprovedBy ||
            string.Equals(receipt.Approver, draft.PreparedBy, StringComparison.OrdinalIgnoreCase) ||
            decision.ApprovedAtUtc != receipt.ApprovedAtUtc || receipt.ApprovedAtUtc != draft.ApprovedAtUtc ||
            receipt.ApprovedAtUtc > DateTimeOffset.UtcNow || decision.TenantId != draft.TenantId || decision.CompanyId != draft.CompanyId ||
            decision.FundProfileId != draft.FundProfileId || decision.LedgerBookId != draft.LedgerBookId ||
            decision.PeriodId != source.Scope.PeriodId || decision.AccountingBasis != draft.AccountingBasis ||
            decision.EventId != source.EventId || decision.EventVersion != source.EventVersion ||
            decision.CandidateFingerprint != source.DraftedCandidateFingerprint || receipt.CandidateFingerprint != source.DraftedCandidateFingerprint ||
            decision.SubjectId != expectedSubject || receipt.SubjectId != expectedSubject ||
            !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(decision, AssetReceiptJson)))
            throw new InvalidOperationException("The asset approval receipt does not match the exact authorized decision, source or scope.");
        return bytes;
    }
}
