using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    private async Task<EvidenceVaultDocumentReviewResponseDto?> ReviewDocumentUnderLockAsync(
        string safeVaultId,
        string normalizedDocumentId,
        string reviewer,
        EvidenceVaultDocumentReviewRequestDto request,
        CancellationToken ct)
    {
        var reviewedAt = DateTimeOffset.UtcNow;
        var indexPath = Path.Combine(_rootDirectory, "_vault", $"{safeVaultId}.json");
        var identity = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
        if (identity is null)
        {
            return null;
        }

        var document = ResolveIdentityDocuments(identity)
            .FirstOrDefault(item => string.Equals(item.DocumentId, normalizedDocumentId, StringComparison.OrdinalIgnoreCase));
        if (document is null)
        {
            return null;
        }

        var confirmedFields = NormalizeConfirmedFields(request.ConfirmedFields, reviewer, reviewedAt);
        if (request.Status == EvidenceDocumentReviewStatusDto.Accepted && confirmedFields.Count == 0)
        {
            throw new ArgumentException(
                "Accepted evidence vault document reviews require at least one human-confirmed field.",
                nameof(request));
        }

        var extractionStatus = request.ExtractionStatus ?? ResolveReviewedExtractionStatus(document.ExtractionStatus, request.Status);
        var reviewState = new EvidenceDocumentReviewStateDto(
            request.Status,
            reviewer,
            reviewedAt,
            NormalizeOptional(request.Notes))
        {
            ConfirmedFields = confirmedFields
        };
        var auditEvent = new EvidenceDocumentAuditEventDto(
            reviewedAt,
            reviewer,
            "DocumentReviewRecorded",
            confirmedFields.Count == 0
                ? $"Document review state set to {request.Status}."
                : $"Document review state set to {request.Status} with {confirmedFields.Count} human-confirmed field(s).",
            NormalizeOptional(request.CorrelationId));
        var reviewedDocument = document with
        {
            ExtractionStatus = extractionStatus,
            ReviewerState = reviewState,
            AuditTrail = document.AuditTrail
                .Concat([auditEvent])
                .OrderBy(static item => item.RecordedAt)
                .ThenBy(static item => item.Action, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var reviewedIdentity = ReplaceIdentityDocument(identity, reviewedDocument);
        var manifestPath = ResolveVaultManifestPath(identity, safeVaultId);
        if (manifestPath is not null)
        {
            var manifest = await TryReadRetainedManifestAsync(manifestPath, ct).ConfigureAwait(false);
            if (manifest is not null)
            {
                var reviewedManifest = manifest with
                {
                    VaultIdentity = reviewedIdentity
                };
                reviewedIdentity = RefreshVaultIdentityContentHash(reviewedIdentity, reviewedManifest);
                reviewedManifest = reviewedManifest with
                {
                    VaultIdentity = reviewedIdentity
                };
                await AtomicFileWriter
                    .WriteAsync(manifestPath, JsonSerializer.Serialize(reviewedManifest, _jsonOptions), ct)
                    .ConfigureAwait(false);
            }
        }

        await WriteVaultIndexAsync(reviewedIdentity, ct).ConfigureAwait(false);

        var entry = new EvidenceVaultDocumentEntryDto(
            reviewedDocument,
            reviewedIdentity.VaultId,
            reviewedIdentity.SubjectKind,
            reviewedIdentity.SubjectId,
            reviewedIdentity.ManifestRoute,
            reviewedIdentity.RetainedAt,
            reviewedIdentity.StorageKind,
            reviewedIdentity.SupportRequests.Count(static supportRequest =>
                string.Equals(supportRequest.Status, "Open", StringComparison.OrdinalIgnoreCase)),
            reviewedIdentity.SupportRequests);
        return new EvidenceVaultDocumentReviewResponseDto(entry, auditEvent);
    }
}
