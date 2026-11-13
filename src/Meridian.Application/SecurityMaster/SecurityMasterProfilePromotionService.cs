using System.Collections.Generic;
using System.Text.Json;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Builds the report-first promotion inventory for one reclassifying profile (ADR-022): every active
/// <c>CustomAsset</c> record pinned to the profile, with the result of a dry run under the profile's
/// target class. The dry run applies the main checks an envelope amendment applies on the write path:
/// the classification-metadata check, the target decode, the target kind's domain invariants, the
/// profile catalog validation and asset-pack admission (schema write validation and the schedule-terms
/// write rules). It is not full write-path parity: it decodes with the read-tolerant mapping rather
/// than the strict write-mode command mapping and does not run the re-serialization round-trip guards,
/// so a Ready candidate can still be refused when it is amended. Nothing is written.
/// <para>Promotion itself is any write that carries the pinned envelope as its terms patch: an
/// envelope amendment, an approved workbench field edit or a term-block patch (both rebuild a full
/// envelope patch from the stored terms). A write without a terms patch, and a deactivation, keep the
/// stored <c>CustomAsset</c> class.</para>
/// </summary>
public sealed class SecurityMasterProfilePromotionService : ISecurityMasterProfilePromotionService
{
    internal const string SourceTermsUnreadable = "profile_promotion_source_terms_unreadable";
    internal const string ClassificationConflict = "profile_promotion_classification_conflict";
    internal const string TargetDecodeFailed = "profile_promotion_target_decode_failed";
    internal const string AdmissionRefused = "profile_promotion_admission_refused";

    private readonly ISecurityMasterStore _securityStore;
    private readonly ISecurityAssetProfileCatalog? _assetProfileCatalog;
    private readonly ILogger<SecurityMasterProfilePromotionService> _logger;

    public SecurityMasterProfilePromotionService(
        ISecurityMasterStore securityStore,
        ILogger<SecurityMasterProfilePromotionService> logger,
        ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        _securityStore = securityStore;
        _logger = logger;
        _assetProfileCatalog = assetProfileCatalog;
    }

    public async Task<SecurityMasterProfilePromotionReportDto> BuildReportAsync(string profileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            || !SecurityAssetPackRegistry.ProfileAssetClasses.TryGetValue(profileId, out var targetAssetClass))
        {
            throw new ArgumentException(
                $"'{profileId}' is not a reclassifying profile; only a profile a class declares in ReclassifyingProfileIds can be promoted.",
                nameof(profileId));
        }

        var securities = await _securityStore.LoadActiveAsync(ct).ConfigureAwait(false);
        var candidates = new List<SecurityMasterProfilePromotionCandidateDto>();
        var scanned = 0;
        var alreadyPromoted = 0;
        var evaluatedAt = DateTimeOffset.UtcNow;

        foreach (var projection in securities)
        {
            ct.ThrowIfCancellationRequested();
            if (projection.Status != SecurityStatusDto.Active)
                continue;

            scanned++;
            if (!IsPinnedTo(projection.AssetSpecificTerms, profileId))
                continue;

            if (string.Equals(projection.AssetClass, targetAssetClass, StringComparison.OrdinalIgnoreCase))
            {
                alreadyPromoted++;
                continue;
            }

            if (!SecurityAssetClassCatalog.GetOrDefault(projection.AssetClass).RequiresProfileReference)
                continue;

            candidates.Add(Evaluate(projection, profileId, targetAssetClass, evaluatedAt));
        }

        var ordered = candidates
            .OrderBy(static candidate => candidate.Disposition)
            .ThenBy(static candidate => candidate.SecurityId)
            .ToArray();
        var ready = ordered.Count(static candidate => candidate.Disposition == SecurityMasterProfilePromotionDisposition.Ready);

        _logger.LogInformation(
            "Profile promotion report built for {ProfileId} to {TargetAssetClass}: {Scanned} securities scanned, {Candidates} candidates ({Ready} ready, {Blocked} blocked), {AlreadyPromoted} already promoted.",
            profileId,
            targetAssetClass,
            scanned,
            ordered.Length,
            ready,
            ordered.Length - ready,
            alreadyPromoted);

        return new SecurityMasterProfilePromotionReportDto(
            evaluatedAt,
            profileId,
            targetAssetClass,
            scanned,
            alreadyPromoted,
            ordered.Length,
            ready,
            ordered.Length - ready,
            ordered);
    }

    private SecurityMasterProfilePromotionCandidateDto Evaluate(
        SecurityProjectionRecord projection,
        string profileId,
        string targetAssetClass,
        DateTimeOffset evaluatedAt)
    {
        var blockers = new List<SecurityMasterProfilePromotionBlockerDto>();

        if (!TryDecode(projection, out var sourceError))
        {
            blockers.Add(new SecurityMasterProfilePromotionBlockerDto(
                SourceTermsUnreadable,
                $"The stored {projection.AssetClass} terms could not be read: {sourceError}"));
        }

        try
        {
            // The same rule the write path applies to a submitted envelope: classification metadata
            // that names another class's routing key is refused rather than resolved.
            SecurityMasterService.TryResolveProfileBackedAlternativeAssetClass(projection.AssetSpecificTerms, out _);
        }
        catch (SecurityMasterWriteRefusedException ex)
        {
            blockers.Add(new SecurityMasterProfilePromotionBlockerDto(ClassificationConflict, ex.Message));
        }

        var target = projection with { AssetClass = targetAssetClass };
        SecurityMasterRecord? targetRecord = null;
        try
        {
            targetRecord = SecurityMasterMapping.ToRecord(target);
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            blockers.Add(new SecurityMasterProfilePromotionBlockerDto(
                TargetDecodeFailed,
                $"The pinned envelope does not decode as {targetAssetClass}: {ex.Message}"));
        }

        if (targetRecord is not null)
        {
            foreach (var error in SecurityMasterCommandFacade.ValidateKindInvariants(targetRecord.Kind))
                blockers.Add(new SecurityMasterProfilePromotionBlockerDto(error.Code, error.Message));
        }

        if (_assetProfileCatalog is not null)
        {
            var validator = new SecurityAssetProfileAssetClassValidator(
                targetAssetClass,
                _assetProfileCatalog,
                requireProfileReference: false,
                enforceWriteTimeGovernance: true);
            foreach (var issue in validator.Validate(new SecurityValidationContext(target, evaluatedAt)))
            {
                if (issue.Severity == SecurityValidationSeverityDto.Error)
                    blockers.Add(new SecurityMasterProfilePromotionBlockerDto(issue.Code, issue.Message));
            }
        }

        if (targetRecord is not null)
        {
            // The asset-pack admission an amendment runs before it persists, judged against the stored
            // terms as retained declarations exactly as the amendment path does.
            try
            {
                SecurityAssetPackAdmission.EnsureAdmitted(target, evaluatedAt, projection.AssetSpecificTerms);
            }
            catch (InvalidOperationException ex) // includes SecurityMasterWriteRefusedException
            {
                blockers.Add(new SecurityMasterProfilePromotionBlockerDto(AdmissionRefused, ex.Message));
            }
        }

        return new SecurityMasterProfilePromotionCandidateDto(
            projection.SecurityId,
            projection.DisplayName,
            projection.PrimaryIdentifierKind,
            projection.PrimaryIdentifierValue,
            projection.Version,
            projection.AssetClass,
            profileId,
            ReadProfileVersion(projection.AssetSpecificTerms),
            targetAssetClass,
            blockers.Count == 0
                ? SecurityMasterProfilePromotionDisposition.Ready
                : SecurityMasterProfilePromotionDisposition.Blocked,
            blockers);
    }

    private static bool TryDecode(SecurityProjectionRecord projection, out string? error)
    {
        try
        {
            SecurityMasterMapping.ToRecord(projection);
            error = null;
            return true;
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsDecodeFailure(Exception ex)
        => ex is InvalidOperationException or JsonException or FormatException or ArgumentException or KeyNotFoundException;

    private static bool IsPinnedTo(JsonElement terms, string profileId)
        => terms.ValueKind == JsonValueKind.Object
           && terms.TryGetProperty("customProfileId", out var pinned)
           && pinned.ValueKind == JsonValueKind.String
           && string.Equals(pinned.GetString(), profileId, StringComparison.OrdinalIgnoreCase);

    private static int? ReadProfileVersion(JsonElement terms)
        => terms.TryGetProperty("profileVersion", out var version)
           && version.ValueKind == JsonValueKind.Number
           && version.TryGetInt32(out var value)
            ? value
            : null;
}
