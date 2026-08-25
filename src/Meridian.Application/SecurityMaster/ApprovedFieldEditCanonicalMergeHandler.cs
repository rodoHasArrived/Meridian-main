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
/// complete economic-definition amendment through <see cref="ISecurityMasterAmender"/> — the current
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
public sealed class ApprovedFieldEditCanonicalMergeHandler : ISecurityMasterRevisionPublishedHandler
{
    private const string OperatorSourceSystem = "operator-workbench";
    private const string ProfileFieldsKey = "profileFields";

    private readonly ISecurityMasterRevisionStore _revisions;
    private readonly ILogger<ApprovedFieldEditCanonicalMergeHandler> _logger;
    private readonly ISecurityMasterStore? _projectionStore;
    private readonly ISecurityMasterAmender? _amender;
    private readonly ISecurityMasterConflictService? _conflictService;
    private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? _assetProfileCatalog;

    public ApprovedFieldEditCanonicalMergeHandler(
        ISecurityMasterRevisionStore revisions,
        ILogger<ApprovedFieldEditCanonicalMergeHandler> logger,
        ISecurityMasterStore? projectionStore = null,
        ISecurityMasterAmender? amender = null,
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

    public async Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

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
            return;
        }

        if (revision.FieldValue is null)
        {
            // A CLEAR withdraws the overlay value and reveals the canonical one — nothing to merge.
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
            return;
        }

        var original = JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException(
                $"Security '{evt.SecurityId:D}' asset-specific terms could not be parsed for the merge.");
        var merged = JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())!.AsObject();

        if (isProfileFieldsRoot && nestedPath.Length > 0)
        {
            ApplyProfileField(merged, projection, nestedPath, revision.FieldValue);
        }
        else if (isProfileFieldsRoot)
        {
            RemoveKeyVariants(merged, ProfileFieldsKey);
            merged[ProfileFieldsKey] = JsonNode.Parse(revision.FieldValue);
        }
        else
        {
            ApplyDeclaredTerm(merged, projection.AssetClass, key, revision.FieldValue);
        }

        if (JsonNode.DeepEquals(original, merged))
        {
            // Retried publish whose merge already landed, or an edit asserting the canonical value.
            _logger.LogInformation(
                "Approved edit to {FieldPath} on {SecurityId} already matches the canonical terms; no amendment needed.",
                fieldPath, evt.SecurityId);
            return;
        }

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

        var detail = await _amender.AmendTermsAsync(amendRequest, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Merged approved field edit {FieldPath} (revision {RevisionId}) into canonical terms for {SecurityId}; canonical version advanced to {Version}.",
            fieldPath, evt.RevisionId, evt.SecurityId, detail.Version);

        await TryResolveWorkbenchChallengerConflictsAsync(evt, fieldPath, mergeStartedAt).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a top-level declared term with the JSON shape its declared schema type demands —
    /// string-valued types (String, Date, Guid) STAY strings even when the text parses as a JSON
    /// literal, matching the workbench's effective-overlay typing — and removes the declared key's
    /// casing variants and legacy aliases so a stale alias spelling cannot shadow the merged value.
    /// </summary>
    private static void ApplyDeclaredTerm(JsonObject merged, string assetClass, string key, string value)
    {
        var field = SecurityAssetTermsSchema.Field(assetClass, key);
        var canonicalKey = field?.Key ?? key;

        RemoveKeyVariants(merged, canonicalKey);
        if (field is not null)
        {
            foreach (var alias in field.Aliases)
            {
                RemoveKeyVariants(merged, alias);
            }
        }

        merged[canonicalKey] = field?.Type
            is SecurityAssetTermFieldType.String
            or SecurityAssetTermFieldType.Date
            or SecurityAssetTermFieldType.Guid
            ? JsonValue.Create(value)
            : TryParseJsonNode(value.Trim()) ?? JsonValue.Create(value);
    }

    /// <summary>
    /// Applies one profile-governed field inside the <c>profileFields</c> envelope, typed by the
    /// record's pinned profile definition when the catalog resolves it (a Text/Date field whose
    /// value happens to parse as a number must stay a string); undeclared keys keep the
    /// parse-then-string fallback the dynamic pass-through surface uses.
    /// </summary>
    private void ApplyProfileField(JsonObject merged, SecurityProjectionRecord projection, string key, string value)
    {
        if (merged[ProfileFieldsKey] is not JsonObject profileFields)
        {
            RemoveKeyVariants(merged, ProfileFieldsKey);
            profileFields = new JsonObject();
            merged[ProfileFieldsKey] = profileFields;
        }

        var canonicalKey = key;
        SecurityAssetProfileFieldTypeDto? declaredType = null;
        if (_assetProfileCatalog is not null
            && projection.AssetSpecificTerms.TryGetProperty("customProfileId", out var profileId)
            && profileId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(profileId.GetString())
            && projection.AssetSpecificTerms.TryGetProperty("profileVersion", out var versionElement)
            && versionElement.TryGetInt32(out var profileVersion)
            && _assetProfileCatalog.TryGetProfile(profileId.GetString()!, profileVersion, out var profile))
        {
            var declared = profile.Fields.FirstOrDefault(
                f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            if (declared is not null)
            {
                canonicalKey = declared.Key;
                declaredType = declared.FieldType;
            }
        }

        RemoveKeyVariants(profileFields, canonicalKey);
        profileFields[canonicalKey] = declaredType switch
        {
            SecurityAssetProfileFieldTypeDto.Decimal or SecurityAssetProfileFieldTypeDto.Integer =>
                TryParseJsonNode(value.Trim()) is JsonNode numeric && numeric.GetValueKind() == JsonValueKind.Number
                    ? numeric
                    : JsonValue.Create(value),
            SecurityAssetProfileFieldTypeDto.Boolean =>
                bool.TryParse(value.Trim(), out var parsedBool) ? JsonValue.Create(parsedBool) : JsonValue.Create(value),
            null => TryParseJsonNode(value.Trim()) ?? JsonValue.Create(value),
            _ => JsonValue.Create(value),
        };
    }

    private static void RemoveKeyVariants(JsonObject envelope, string key)
    {
        foreach (var variantKey in envelope
            .Where(property => string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(static property => property.Key)
            .ToArray())
        {
            envelope.Remove(variantKey);
        }
    }

    private static JsonNode? TryParseJsonNode(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
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
