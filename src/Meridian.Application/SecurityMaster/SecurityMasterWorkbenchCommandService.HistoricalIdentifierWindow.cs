using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Governed historical identifier windows (Security Master architecture review F1). Bulk imports
/// stamp identifier <c>ValidFrom</c> with the ingest time, so an identifier the security carried
/// before ingest cannot come from a file. This seam stages it as a Draft revision on the reserved
/// <see cref="SecurityMasterHistoricalIdentifierWindow.FieldPath"/>; the existing submit → approve →
/// publish lifecycle governs it, with an independent reviewer always required (backdating is
/// exactly what the import's anti-forgery stamping refuses), and
/// <see cref="ApprovedHistoricalIdentifierWindowHandler"/> applies it on publish.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService
{
    private const string HistoricalIdentifierWindowProposedEventType = "historical-identifier-window-proposed";

    public async Task<SecurityMasterEditResultDto> ProposeHistoricalIdentifierWindowAsync(
        ProposeHistoricalIdentifierWindowRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);

        var problem = SecurityMasterHistoricalIdentifierWindow.Validate(
            request.Kind,
            request.Value,
            request.Provider,
            request.ValidFrom,
            request.ValidTo,
            request.EvidenceReference,
            request.Justification,
            now: DateTimeOffset.UtcNow);
        if (problem is not null)
        {
            throw new ArgumentException($"Historical identifier window refused: {problem}", nameof(request));
        }

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);
        if (currentVersion == 0)
        {
            throw new InvalidOperationException(
                $"Security '{request.SecurityId:D}' was not found; a historical identifier window cannot be proposed for it.");
        }

        if (request.ExpectedVersion != currentVersion)
        {
            throw new SecurityMasterConcurrencyException(request.SecurityId, request.ExpectedVersion, currentVersion);
        }

        var payload = new SecurityMasterHistoricalIdentifierWindowPayload(
            request.Kind,
            request.Value.Trim(),
            string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider.Trim(),
            request.ValidFrom,
            request.ValidTo,
            request.EvidenceReference.Trim(),
            request.Justification.Trim(),
            request.Actor.Trim());

        // The proposal stages no overlay value, so it only needs the revision write. It still runs
        // under the per-security gate and mutation scope so it serializes with approvals that list
        // this security's staged revisions.
        var gate = await AcquireFieldEditGateAsync(request.SecurityId, ct).ConfigureAwait(false);
        SecurityMasterRevisionRecord revision;
        try
        {
            revision = await ExecuteMutationAsync(request.SecurityId, () => _revisions.CreateDraftAsync(
                    request.SecurityId,
                    request.Actor,
                    SecurityMasterHistoricalIdentifierWindow.FieldPath,
                    request.ValidFrom,
                    payload.Justification,
                    request.FundProfileId,
                    new SecurityMasterRevisionFieldValue(SecurityMasterHistoricalIdentifierWindow.Serialize(payload)),
                    ct), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Dispose();
        }

        _logger.LogInformation(
            "Historical identifier window {Kind} proposed for {SecurityId} from {ValidFrom} as draft revision {RevisionId} by {Actor}",
            request.Kind, request.SecurityId, request.ValidFrom, revision.RevisionId, SanitizeForLog(request.Actor));

        var changeEntry = BuildChangeEntry(
            currentVersion,
            eventType: HistoricalIdentifierWindowProposedEventType,
            actor: request.Actor,
            effectiveFrom: request.ValidFrom,
            sourceRecordId: payload.EvidenceReference,
            reason: payload.Justification,
            summary: $"Proposed historical {request.Kind} window from {request.ValidFrom:O}.",
            changedFields: [SecurityMasterHistoricalIdentifierWindow.FieldPath]);

        return new SecurityMasterEditResultDto(
            request.SecurityId,
            revision.RevisionId,
            currentVersion,
            SecurityMasterRevisionStateDto.Draft,
            changeEntry);
    }

    /// <summary>
    /// The window path is reserved for <see cref="ProposeHistoricalIdentifierWindowAsync"/>, which
    /// requires the evidence reference and always requires independent review. Staging it as a field
    /// edit would let a free-form value reach the publish-time window amendment.
    /// </summary>
    private static void EnsureFieldEditDoesNotTargetWindowPath(UpdateSecurityFieldRequest request)
    {
        if (SecurityMasterHistoricalIdentifierWindow.IsWindowPath(request.FieldPath))
        {
            throw new ArgumentException(
                $"'{SecurityMasterHistoricalIdentifierWindow.FieldPath}' is reserved for governed historical identifier " +
                "window proposals; use the historical identifier window proposal instead of a field edit.",
                nameof(request));
        }
    }

    /// <summary>The proposer recorded in a window revision's payload, or null for other revisions.</summary>
    private static string? TryGetWindowProposer(SecurityMasterRevisionRecord revision)
    {
        if (!SecurityMasterHistoricalIdentifierWindow.IsWindowRevision(revision))
        {
            return null;
        }

        try
        {
            return SecurityMasterHistoricalIdentifierWindow.Parse(revision.RevisionId, revision.FieldValue).ProposedBy;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
