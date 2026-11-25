using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Published-revision side effect (<c>Order = 5</c>, runs before the projection rebuild): merges an
/// approved <c>assetSpecificTerms.*</c> field edit into the CANONICAL security terms by emitting a
/// complete economic-definition amendment through <see cref="IGovernedPublicationAmender"/> — the current
/// definition plus the one typed field change — so replay stays correct and the approved correction
/// reaches cash-flow projection, amortization, pricing, and NAV instead of living only in the
/// override overlay. Paths outside the asset-terms namespace stay overlay annotations by design
/// (the documented D2 rationale), as do CLEARs (withdrawing an override reveals the canonical
/// value, so there is nothing to merge) and paths nested deeper than the shapes the workbench
/// validates (top-level declared terms, the <c>profileFields</c> envelope, and one-level
/// <c>profileFields.*</c> fields).
///
/// <para>Idempotent: the merged document is rebuilt from the CURRENT canonical terms on every run,
/// so a retried publish whose merge already landed detects a no-op (the patched document equals the
/// stored one) and skips the amendment instead of appending a duplicate event. A failure leaves the
/// revision Approved and the publish retryable, per the handler seam's contract.</para>
///
/// <para>The amendment names <c>operator-workbench</c> as its source system, so the amend seam's
/// cross-source conflict detection records the vendor-versus-operator disagreement as usual; the
/// handler then best-effort resolves those freshly opened workbench-challenger conflicts in favor
/// of the operator value — the maker-checker approval that published this revision already
/// adjudicated it, and leaving the conflict open would queue a second governance decision for a
/// value a reviewer just decided.</para>
///
/// <para>No-op in composition roots without a Security Master backend (no projection store).</para>
/// </summary>
public sealed class ApprovedFieldEditCanonicalMergeHandler : ISecurityMasterRevisionPublishedHandler, ISecurityMasterCanonicalMergeOutcomeReporter
{
    private const string OperatorSourceSystem = SecurityMasterWriteAuthority.WorkbenchSourceSystem;
    private const string ProfileFieldsKey = "profileFields";

    private readonly ISecurityMasterRevisionStore _revisions;
    private readonly ILogger<ApprovedFieldEditCanonicalMergeHandler> _logger;
    private readonly ISecurityMasterStore? _projectionStore;
    private readonly IGovernedPublicationAmender? _amender;
    private readonly ISecurityMasterConflictService? _conflictService;
    private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? _assetProfileCatalog;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SecurityMasterCanonicalMergeOutcomeDto> _outcomes = new();

    public ApprovedFieldEditCanonicalMergeHandler(
        ISecurityMasterRevisionStore revisions,
        ILogger<ApprovedFieldEditCanonicalMergeHandler> logger,
        ISecurityMasterStore? projectionStore = null,
        IGovernedPublicationAmender? amender = null,
        ISecurityMasterConflictService? conflictService = null,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _projectionStore = projectionStore;
        _amender = amender;
        _conflictService = conflictService;
        _assetProfileCatalog = assetProfileCatalog;
    }

    /// <summary>
    /// Runs BEFORE the projection rebuild (Order = 10) and coverage invalidation (Order = 20): both
    /// must observe the merged canonical terms, not the pre-merge definition.
    /// </summary>
    public int Order => 5;

    /// <inheritdoc />
    public SecurityMasterCanonicalMergeOutcomeDto? TakeOutcome(Guid revisionId)
        => _outcomes.TryRemove(revisionId, out var outcome) ? outcome : null;

    public async Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _outcomes.TryRemove(evt.RevisionId, out _);

        if (_projectionStore is null || _amender is null)
        {
            // No Security Master backend is configured, so there are no canonical terms to merge into.
            return;
        }

        var fieldPath = evt.ChangedFields
            .FirstOrDefault(static path => SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(path));
        if (fieldPath is null)
        {
            // Annotation-surface paths stay overlay-only by design; lifecycle revisions without
            // field metadata have nothing to merge.
            Record(evt, SecurityMasterCanonicalMergeOutcomeKind.NotApplicable,
                "The revision changes no asset-specific term, so it stays an overlay annotation.");
            return;
        }

        // The revision is the durable record of the exact value the approval governed. The publish
        // service verified the revision exists and is Approved just before this fan-out, so a
        // missing row here is a store inconsistency the retryable publish failure should surface.
        var revision = await _revisions.GetAsync(evt.RevisionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Revision '{evt.RevisionId:D}' was not found while merging its approved field edit into " +
                $"security '{evt.SecurityId:D}'; the publish can be retried once the revision store is consistent.");

        if (!revision.FieldValueRecorded)
        {
            // Legacy revision predating value persistence: the governed value cannot be known here,
            // so the edit remains an overlay annotation (the pre-merge-path behavior).
            _logger.LogWarning(
                "Revision {RevisionId} for {SecurityId} predates field-value persistence; its approved edit to {FieldPath} stays overlay-only.",
                evt.RevisionId, evt.SecurityId, fieldPath);
            Record(evt, SecurityMasterCanonicalMergeOutcomeKind.OverlayOnly,
                "The revision predates field-value persistence, so its approved value stays overlay-only.");
            return;
        }

        if (revision.FieldValue is null)
        {
            // A CLEAR withdraws the overlay value and reveals the canonical one — nothing to merge.
            Record(evt, SecurityMasterCanonicalMergeOutcomeKind.NotApplicable,
                "The revision clears an overlay value, which reveals the canonical value; nothing is merged.");
            return;
        }

        var projection = await _projectionStore.GetProjectionAsync(evt.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Security '{evt.SecurityId:D}' has no canonical projection to merge the approved field edit into; " +
                "the publish can be retried once the projection store is consistent.");

        if (projection.AssetSpecificTerms.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Security '{evt.SecurityId:D}' stores non-object asset-specific terms " +
                $"({projection.AssetSpecificTerms.ValueKind}), so the approved edit to '{fieldPath}' cannot be merged.");
        }

        var remainder = fieldPath.Trim()[SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix.Length..];
        var separatorIndex = remainder.IndexOf('.', StringComparison.Ordinal);
        var key = separatorIndex < 0 ? remainder : remainder[..separatorIndex];
        var nestedPath = separatorIndex < 0 ? string.Empty : remainder[(separatorIndex + 1)..];

        var isProfileFieldsRoot = string.Equals(key, ProfileFieldsKey, StringComparison.OrdinalIgnoreCase);
        if (nestedPath.Length > 0 && (!isProfileFieldsRoot || nestedPath.Contains('.', StringComparison.Ordinal)))
        {
            // The workbench only stages typed values at the shapes handled below; anything deeper is
            // dynamic pass-through that cannot be safely re-typed into the canonical document.
            _logger.LogInformation(
                "Approved edit to {FieldPath} on {SecurityId} nests deeper than the merge path models; it stays overlay-only.",
                fieldPath, evt.SecurityId);
            Record(evt, SecurityMasterCanonicalMergeOutcomeKind.OverlayOnly,
                $"'{fieldPath}' nests deeper than the canonical merge models, so the approved value stays overlay-only.");
            return;
        }

        var original = JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException(
                $"Security '{evt.SecurityId:D}' asset-specific terms could not be parsed for the merge.");
        var proposed = new SecurityMasterProposedTermsBuilder(_assetProfileCatalog).Build(projection, revision);
        var merged = JsonNode.Parse(proposed.GetRawText())!.AsObject();

        if (JsonNode.DeepEquals(original, merged))
        {
            // Retried publish whose merge already landed, or an edit asserting the canonical value.
            _logger.LogInformation(
                "Approved edit to {FieldPath} on {SecurityId} already matches the canonical terms; no amendment needed.",
                fieldPath, evt.SecurityId);
            Record(evt, SecurityMasterCanonicalMergeOutcomeKind.AlreadyCanonical,
                "The canonical terms already hold the approved value.", projection.Version);
            return;
        }

        // A proposal drafted against an older canonical value must not overwrite a newer one. The
        // revision recorded the stored keys this merge overwrites; if they changed since the draft,
        // refuse instead of merging. Changes to other fields leave the snapshot equal and still merge
        // onto the fresh projection below. Revisions without a recorded base publish as before.
        EnsureProposalIsNotStale(evt, revision, projection, fieldPath);

        var mergeStartedAt = DateTimeOffset.UtcNow;
        var amendRequest = new AmendSecurityTermsRequest(
            SecurityId: evt.SecurityId,
            ExpectedVersion: projection.Version,
            CommonTerms: null,
            AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(merged),
            IdentifiersToAdd: Array.Empty<SecurityIdentifierDto>(),
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: evt.EffectiveFrom,
            SourceSystem: OperatorSourceSystem,
            UpdatedBy: evt.Actor,
            SourceRecordId: revision.RevisionId.ToString("D"),
            Reason: string.IsNullOrWhiteSpace(revision.FieldJustification)
                ? $"Approved workbench revision {revision.RevisionId:D} merged into canonical terms."
                : revision.FieldJustification);

        var detail = await _amender.AmendApprovedRevisionAsync(amendRequest, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Merged approved field edit {FieldPath} (revision {RevisionId}) into canonical terms for {SecurityId}; canonical version advanced to {Version}.",
            fieldPath, evt.RevisionId, evt.SecurityId, detail.Version);
        Record(evt, SecurityMasterCanonicalMergeOutcomeKind.Merged,
            $"The approved value was merged into the canonical terms at version {detail.Version}.", detail.Version);

        await PostgresSecurityMasterMutation.AfterCommitAsync(evt.SecurityId,
            () => TryResolveWorkbenchChallengerConflictsAsync(evt, fieldPath, mergeStartedAt)).ConfigureAwait(false);
    }

    private void Record(
        SecurityMasterRevisionPublishedEvent evt,
        SecurityMasterCanonicalMergeOutcomeKind outcome,
        string detail,
        long? canonicalVersion = null)
        => _outcomes[evt.RevisionId] = new SecurityMasterCanonicalMergeOutcomeDto(outcome, detail, canonicalVersion);

    private void EnsureProposalIsNotStale(
        SecurityMasterRevisionPublishedEvent evt,
        SecurityMasterRevisionRecord revision,
        SecurityProjectionRecord projection,
        string fieldPath)
    {
        if (!revision.FieldBaseRecorded)
        {
            _logger.LogInformation(
                "Revision {RevisionId} for {SecurityId} recorded no canonical base, so its approved edit to {FieldPath} merges without a stale-proposal check.",
                evt.RevisionId, evt.SecurityId, fieldPath);
            return;
        }

        if (projection.Version == revision.BaseVersion)
        {
            return;
        }

        var current = SecurityAssetTermsFieldSnapshot.Capture(projection.AssetClass, projection.AssetSpecificTerms, fieldPath);
        if (string.Equals(current, revision.FieldBaseSnapshot, StringComparison.Ordinal))
        {
            return;
        }

        throw new StaleFieldProposalException(
            evt.SecurityId, evt.RevisionId, fieldPath, revision.BaseVersion!.Value, projection.Version);
    }

    /// <summary>
    /// Best-effort post-merge cleanup: the amendment's cross-source conflict detection records the
    /// vendor-versus-operator disagreement the merge created, but the maker-checker approval that
    /// published this revision already adjudicated it — so freshly opened conflicts whose CHALLENGER
    /// is the workbench are resolved in the operator's favor here, with the revision named in the
    /// resolution reason. A failure leaves the conflict open (visible, resolvable by an operator)
    /// rather than failing a merge that has already durably landed.
    /// </summary>
    private async Task TryResolveWorkbenchChallengerConflictsAsync(
        SecurityMasterRevisionPublishedEvent evt, string fieldPath, DateTimeOffset mergeStartedAt)
    {
        if (_conflictService is null)
        {
            return;
        }

        try
        {
            // Post-commit: the merge amendment is durable, so a canceled request token must not
            // abandon the cleanup mid-sweep.
            var openConflicts = await _conflictService.GetOpenConflictsAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var conflict in openConflicts)
            {
                if (conflict.SecurityId != evt.SecurityId
                    || !string.Equals(conflict.ProviderB, OperatorSourceSystem, StringComparison.OrdinalIgnoreCase)
                    || conflict.DetectedAt < mergeStartedAt)
                {
                    continue;
                }

                await _conflictService.ResolveAsync(
                    new ResolveConflictRequest(
                        conflict.ConflictId,
                        Resolution: "Resolve",
                        ResolvedBy: evt.Actor,
                        Reason: $"Auto-resolved: approved workbench revision {evt.RevisionId:D} merged {fieldPath} into canonical terms through the maker-checker gate.",
                        ChosenWinnerSource: OperatorSourceSystem),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Resolving workbench-challenger conflicts after merging {FieldPath} for {SecurityId} failed; the conflicts stay open for operator resolution.",
                fieldPath, evt.SecurityId);
        }
    }
}
