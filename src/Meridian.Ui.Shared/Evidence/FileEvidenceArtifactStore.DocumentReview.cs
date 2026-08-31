using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    private async Task<EvidenceVaultDocumentReviewResponseDto?> ReviewDocumentUnderLockAsync(
        string safeVaultId,
        string normalizedDocumentId,
        string tenantId,
        string scope,
        string reviewer,
        EvidenceVaultDocumentReviewRequestDto request,
        CancellationToken ct)
    {
        var reviewedAt = DateTimeOffset.UtcNow;
        var indexPath = Path.Combine(_rootDirectory, "_vault", $"{safeVaultId}.json");
        var identity = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
        if (identity is null || !MatchesIdentityScope(identity, tenantId, scope))
        {
            return null;
        }

        var manifestPath = ResolveVaultManifestPath(identity, safeVaultId);
        if (manifestPath is null)
        {
            return null;
        }

        var manifest = await TryReadRetainedManifestAsync(manifestPath, ct).ConfigureAwait(false);
        if (manifest is null
            || !TryResolveManifestAuthority(
                manifest,
                identity,
                tenantId,
                scope,
                out var manifestIdentity)
            || manifestIdentity is null)
        {
            return null;
        }

        // The embedded manifest identity is the committed semantic authority. If a previous
        // review reached its manifest write but not its index write, continue from that newer
        // review state and heal the locator index on this successful mutation.
        identity = manifestIdentity;
        var document = ResolveIdentityDocuments(identity)
            .FirstOrDefault(item =>
                string.Equals(item.DocumentId, normalizedDocumentId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Scope, scope, StringComparison.OrdinalIgnoreCase));
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

        var reviewedManifest = manifest with
        {
            VaultIdentity = reviewedIdentity
        };
        reviewedIdentity = RefreshVaultIdentityContentHash(reviewedIdentity, reviewedManifest);
        reviewedManifest = reviewedManifest with
        {
            VaultIdentity = reviewedIdentity
        };
        var originalManifestJson = JsonSerializer.Serialize(manifest, _jsonOptions);
        await AtomicFileWriter
            .WriteAsync(manifestPath, JsonSerializer.Serialize(reviewedManifest, _jsonOptions), ct)
            .ConfigureAwait(false);

        try
        {
            await WriteVaultIndexAsync(reviewedIdentity, ct).ConfigureAwait(false);
        }
        catch (Exception writeException) when (writeException is IOException
                                               or UnauthorizedAccessException
                                               or OperationCanceledException)
        {
            try
            {
                await AtomicFileWriter
                    .WriteAsync(manifestPath, originalManifestJson, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(
                    rollbackException,
                    "Evidence vault review rollback could not restore manifest '{ManifestPath}' after index write failure.",
                    manifestPath);
            }

            throw;
        }

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
