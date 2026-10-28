using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private async Task<(string VaultId, string DocumentId, RetainedEvidenceIdentityDto Evidence)> RetainAssetApprovalEvidenceAsync(
        ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        var bytes = ValidateAssetApprovalReceipt(draft);
        var receipt = draft.AssetAccountingApproval!;
        var store = _assetEvidenceArtifacts ?? throw new InvalidOperationException("The governed asset posting workflow requires retained evidence storage.");
        var matches = await store.FindByLinkageAsync(new EvidenceVaultLookupRequestDto(receipt.SubjectId, null,
            draft.PeriodId, null, null, draft.JournalEntryId.ToString("D"))
        { TenantId = draft.TenantId, Scope = draft.CompanyId }, ct).ConfigureAwait(false);
        if (matches.Count > 1)
            throw new InvalidOperationException("Multiple retained approval artifacts match this immutable decision; resolve the ambiguity before posting.");
        string vaultId;
        string documentId;
        if (matches.Count == 1)
        {
            var identity = matches[0];
            var documents = identity.Documents.Where(document => document.SourceHashSha256 == receipt.ContentHashSha256).ToArray();
            if (documents.Length != 1)
                throw new InvalidOperationException("The retained approval artifact does not identify one exact receipt document.");
            vaultId = identity.VaultId;
            documentId = documents[0].DocumentId;
        }
        else
        {
            var publication = await store.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
                "approval", receipt.SubjectId, "authenticated-accounting-approval", $"{draft.JournalEntryId:N}-approval.json",
                Convert.ToBase64String(bytes), "application/json", "Meridian.AssetAccountingApproval", receipt.ApprovalId,
                receipt.Approver, receipt.ContentHashSha256,
                Linkage: new EvidenceSubjectLinkageDto(receipt.SubjectId, null, draft.PeriodId, null, null,
                    draft.JournalEntryId.ToString("D")))
            {
                Actor = receipt.Approver,
                TenantId = draft.TenantId,
                Scope = draft.CompanyId,
                ReviewerState = new EvidenceDocumentReviewStateDto(EvidenceDocumentReviewStatusDto.Accepted,
                    receipt.Approver, receipt.ApprovedAtUtc)
                {
                    ConfirmedFields = [new EvidenceDocumentConfirmedFieldDto("approvalDecision", "Approve",
                        receipt.Approver, receipt.ApprovedAtUtc)]
                }
            }, ct).ConfigureAwait(false);
            vaultId = publication.VaultIdentity.VaultId;
            documentId = publication.Document?.DocumentId
                ?? throw new InvalidOperationException("The retained approval publication did not return its document identity.");
        }
        var evidence = await VerifyAssetApprovalEvidenceAsync(draft, vaultId, documentId, ct).ConfigureAwait(false);
        return (vaultId, documentId, evidence);
    }

    private async Task<RetainedEvidenceIdentityDto> VerifyAssetApprovalEvidenceAsync(
        ManualJournalEntryDraftDto draft, string vaultId, string documentId, CancellationToken ct)
    {
        var bytes = ValidateAssetApprovalReceipt(draft);
        var receipt = draft.AssetAccountingApproval!;
        var store = _assetEvidenceArtifacts ?? throw new InvalidOperationException("The governed asset posting workflow requires retained evidence storage.");
        var identity = await store.TryGetVaultIdentityAsync(vaultId, draft.TenantId!, draft.CompanyId!, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The scoped retained approval artifact is unavailable.");
        var document = identity.Documents.SingleOrDefault(item => item.DocumentId == documentId);
        if (identity.TenantId != draft.TenantId || identity.Scope != draft.CompanyId || identity.SubjectKind != "approval" ||
            identity.SubjectId != receipt.SubjectId || document is null || document.TenantId != draft.TenantId ||
            document.Scope != draft.CompanyId || document.SourceHashSha256 != receipt.ContentHashSha256 ||
            document.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted ||
            document.ReviewerState.Reviewer != receipt.Approver || document.ReviewerState.ReviewedAt != receipt.ApprovedAtUtc ||
            !document.ReviewerState.ConfirmedFields.Any(field => field.FieldName == "approvalDecision" &&
                field.ConfirmedValue == "Approve" && field.ConfirmedBy == receipt.Approver && field.ConfirmedAt == receipt.ApprovedAtUtc) ||
            identity.RetainedAt < receipt.ApprovedAtUtc ||
            !await store.VerifyRetainedContentAsync(vaultId, draft.TenantId!, draft.CompanyId!, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The approval artifact content, review or ownership does not match the authenticated decision.");
        var file = await store.TryOpenDocumentContentAsync(vaultId, documentId, draft.TenantId!, draft.CompanyId!, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained approval receipt bytes are unavailable.");
        await using (file.Content.ConfigureAwait(false))
        {
            using var captured = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await file.Content.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                if (captured.Length + count > MaxAssetApprovalReceiptBytes)
                    throw new InvalidOperationException("The retained approval receipt exceeds the supported size.");
                captured.Write(buffer, 0, count);
            }
            if (!captured.ToArray().AsSpan().SequenceEqual(bytes) || Sha256Digest.Compute(captured.ToArray()) != receipt.ContentHashSha256)
                throw new InvalidOperationException("The retained approval receipt bytes differ from the authenticated decision.");
        }
        return new RetainedEvidenceIdentityDto($"asset-approval:{receipt.ApprovalId}", $"document://{vaultId}/{documentId}",
            receipt.ContentHashSha256, "Meridian.AssetAccountingApproval", receipt.ApprovalId,
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus, receipt.Approver, receipt.ApprovedAtUtc,
            draft.AccountingDate, 1, identity.RetainedAt, receipt.Approver,
            AssetAccountingEvidenceSubjects.PostingApproval, receipt.SubjectId);
    }
}
