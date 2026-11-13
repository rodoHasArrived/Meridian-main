using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;

namespace Meridian.Storage.Ledger;

/// <summary>Immutable reviewed route operands and a nonforking, forward-only reporting path.</summary>
public static class ConsolidationTransitionRules
{
    internal static string RequireTenant(string? tenant)
        => !string.IsNullOrWhiteSpace(tenant) && !tenant.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? tenant.Trim().ToLowerInvariant() : throw new TenantScopeRejectedException("reporting transition authority without an owning tenant");
    public static string PreviewFingerprint(ConsolidationTransitionPreviewDto preview)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(preview with
        { PreviewId = Guid.Empty, PreparedAt = default, Fingerprint = string.Empty }, ConsolidationTransitionJsonContext.Default.ConsolidationTransitionPreviewDto));

    public static Guid PreviewId(ConsolidationTransitionPreviewDto preview)
        => new(Sha256Digest.ComputeBytesUtf8($"{preview.Request.TransitionId:D}:{PreviewFingerprint(preview)}").AsSpan(0, 16));

    public static string ReceiptHash(ConsolidationTransitionReceiptDto receipt)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(receipt with { ContentHash = string.Empty }, ConsolidationTransitionJsonContext.Default.ConsolidationTransitionReceiptDto));

    public static void ValidatePreview(ConsolidationTransitionPreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ValidateRequest(preview.Request);
        if (preview.PreviewId == Guid.Empty || preview.PreparedAt == default || string.IsNullOrWhiteSpace(preview.PreparedBy)
            || string.IsNullOrWhiteSpace(preview.TenantId) || preview.RootVersion < 1
            || preview.PreviousTransitionId == Guid.Empty || (preview.RootVersion == 1) != (preview.PreviousTransitionId is null))
            throw new InvalidOperationException("A transition preview requires retained identity, actor, scope and root version.");
        if (preview.CanApprove && (string.IsNullOrWhiteSpace(preview.PreviousBookEvidenceDigest) || preview.PreviousPostedJournalIds is null
            || preview.PreviousPostedJournalIds.Count == 0 || preview.PreviousPostedJournalIds.Any(id => id == Guid.Empty)
            || preview.PreviousPostedJournalIds.Distinct().Count() != preview.PreviousPostedJournalIds.Count
            || preview.PreviousLatestPostedAsOf is not { } latest || latest >= preview.Request.CutoverDate
            || preview.NewEvidence?.Request != new ConsolidationRequestDto(preview.Request.OrganizationId, preview.Request.OwnershipRootId,
                preview.Request.NewEliminationBookId, preview.Request.NewPeriodId, preview.Request.CutoverDate)))
            throw new InvalidOperationException("A reporting transition requires complete retained old-book history, a later cutover and matching new-book evidence.");
        if (!Sha256Digest.FixedEquals(preview.Fingerprint, PreviewFingerprint(preview)) || preview.PreviewId != PreviewId(preview))
            throw new InvalidOperationException("The retained transition preview fingerprint or identity does not match its operands.");
    }

    public static void ValidateReceipt(ConsolidationTransitionReceiptDto receipt, ConsolidationTransitionPreviewDto preview)
    {
        ValidatePreview(preview);
        if (!preview.CanApprove || receipt.TransitionId != receipt.Request.TransitionId
            || receipt.TransitionId != preview.Request.TransitionId || receipt.PreviewId != preview.PreviewId
            || receipt.Request != preview.Request || receipt.RootVersion != preview.RootVersion
            || receipt.PreviousTransitionId != preview.PreviousTransitionId || receipt.TenantId != preview.TenantId
            || receipt.CompanyId != preview.CompanyId || receipt.PreparedBy != preview.PreparedBy
            || string.IsNullOrWhiteSpace(receipt.ApprovedBy) || receipt.ApprovedAt < preview.PreparedAt
            || string.Equals(receipt.PreparedBy.Trim(), receipt.ApprovedBy.Trim(), StringComparison.OrdinalIgnoreCase)
            || receipt.PreviewFingerprint != preview.Fingerprint || receipt.PreviousBookEvidenceDigest != preview.PreviousBookEvidenceDigest
            || receipt.PreviousLatestPostedAsOf != preview.PreviousLatestPostedAsOf
            || !receipt.PreviousPostedJournalIds.SequenceEqual(preview.PreviousPostedJournalIds)
            || !receipt.PreviousEntityIds.SequenceEqual(preview.PreviousEntityIds)
            || JsonSerializer.Serialize(receipt.NewEvidence, ConsolidationTransitionJsonContext.Default.ConsolidationEvidenceDto)
                != JsonSerializer.Serialize(preview.NewEvidence, ConsolidationTransitionJsonContext.Default.ConsolidationEvidenceDto)
            || !Sha256Digest.FixedEquals(receipt.ContentHash, ReceiptHash(receipt)))
            throw new InvalidOperationException("Reporting transition approval requires an independent actor and the exact retained preview operands.");
    }

    public static void ValidateAppend(ConsolidationTransitionReceiptDto receipt, IReadOnlyList<ConsolidationTransitionReceiptDto> history)
    {
        var head = history.OrderBy(item => item.RootVersion).LastOrDefault();
        if (receipt.RootVersion != (head?.RootVersion ?? 0) + 1 || receipt.PreviousTransitionId != head?.TransitionId)
            throw new InvalidOperationException("The reporting root revision changed; prepare and review a new transition preview.");
        if (history.Any(item => item.TenantId != receipt.TenantId || item.CompanyId != receipt.CompanyId
            || item.Request.OrganizationId != receipt.Request.OrganizationId || item.Request.OwnershipRootId != receipt.Request.OwnershipRootId))
            throw new InvalidOperationException("Reporting transition history cannot change its root or owner scope.");
        if (head is not null && (receipt.Request.PreviousEliminationBookId != head.Request.NewEliminationBookId
            || receipt.Request.CutoverDate <= head.Request.CutoverDate))
            throw new InvalidOperationException("A transition must continue the current reporting path at a strictly later cutover.");
        if (history.Any(item => item.Request.PreviousEliminationBookId == receipt.Request.NewEliminationBookId
            || item.Request.NewEliminationBookId == receipt.Request.NewEliminationBookId))
            throw new InvalidOperationException("A historical elimination book cannot be reused as a new reporting book.");
    }

    public static void ValidateRequest(ConsolidationTransitionRequestDto request)
    {
        if (request is null || request.TransitionId == Guid.Empty || request.OrganizationId == Guid.Empty
            || request.OwnershipRootId == Guid.Empty || request.PreviousEliminationBookId == Guid.Empty
            || request.NewEliminationBookId == Guid.Empty || request.NewPeriodId == Guid.Empty
            || request.PreviousEliminationBookId == request.NewEliminationBookId || request.CutoverDate == default
            || string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.EvidenceReference))
            throw new InvalidOperationException("A reporting transition requires distinct books, root, period, date, reason and evidence.");
    }
}
