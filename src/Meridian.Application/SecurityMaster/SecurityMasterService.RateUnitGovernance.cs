using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterService
{
    private async Task EnsureRateUnitAmendmentAuthorizedAsync(
        AmendSecurityTermsRequest request,
        SecurityProjectionRecord current,
        bool approvedPublication,
        CancellationToken ct)
    {
        if (request.AssetSpecificTermsPatch is not { } submitted
            || SecurityTermsEnvelope.IsProfileGovernedPayload(current.AssetClass, current.AssetSpecificTerms)
            || SecurityAssetTermsSchema.Field(current.AssetClass, SecurityRecordRateUnit.FieldKey) is not { } field)
            return;

        static string? RawUnit(JsonElement document)
            => document.ValueKind == JsonValueKind.Object
                && document.TryGetProperty(SecurityRecordRateUnit.FieldKey, out var unit)
                && unit.ValueKind != JsonValueKind.Null ? unit.GetRawText() : null;

        var rawBefore = RawUnit(current.AssetSpecificTerms);
        var rawAfter = RawUnit(submitted);
        var before = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(current.AssetSpecificTerms) >= field.SinceTermsVersion
            ? rawBefore : null;
        var after = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(submitted) >= field.SinceTermsVersion
            ? rawAfter : null;
        if (string.Equals(before, after, StringComparison.Ordinal)
            && string.Equals(rawBefore, rawAfter, StringComparison.Ordinal)
            && (before is not null || rawAfter is null))
            return;

        // Every amendment of this interpretation-changing field is a governed correction, even
        // when the effective profile permits other direct edits or the caller is system ingest.
        if (!approvedPublication || _revisionStore is null
            || !Guid.TryParse(request.SourceRecordId, out var revisionId))
            throw new SecurityMasterWriteRefusedException(
                "Changing rateUnit requires an approved workbench field revision; direct and system-ingest amendments cannot declare, change or clear a record's rate unit.");

        var revision = await _revisionStore.GetAsync(revisionId, ct).ConfigureAwait(false);
        if (revision is null
            || revision.SecurityId != request.SecurityId
            || revision.State is not (Meridian.Contracts.Workstation.SecurityMasterRevisionStateDto.Approved
                or Meridian.Contracts.Workstation.SecurityMasterRevisionStateDto.Published)
            || revision.FieldPath != "assetSpecificTerms.rateUnit"
            || !revision.FieldValueRecorded || revision.FieldValue is null
            || !string.Equals(after, JsonSerializer.Serialize(revision.FieldValue), StringComparison.Ordinal))
            throw new SecurityMasterWriteRefusedException(
                "The approved revision must govern assetSpecificTerms.rateUnit and retain the exact unit being published.");
    }
    private static void EnsureRateUnitRoundTripsSafely(SecurityProjectionRecord projection, JsonElement? patch)
    {
        var field = SecurityAssetTermsSchema.Field(projection.AssetClass, SecurityRecordRateUnit.FieldKey);
        var terms = projection.AssetSpecificTerms;
        if (field is null || terms.ValueKind != JsonValueKind.Object
            || SecurityTermsEnvelope.IsProfileGovernedPayload(projection.AssetClass, terms)
            || SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms) < field.SinceTermsVersion
            || !terms.TryGetProperty(SecurityRecordRateUnit.FieldKey, out var retained)
            || retained.ValueKind is JsonValueKind.String or JsonValueKind.Null)
            return;
        if (patch is { ValueKind: JsonValueKind.Object } replacement
            && SecurityTermsEnvelope.ResolveAssetClassTermsVersion(replacement) >= field.SinceTermsVersion
            && replacement.TryGetProperty(SecurityRecordRateUnit.FieldKey, out var repaired)
            && repaired.ValueKind == JsonValueKind.String
            && SecurityRecordRateUnit.Parse(repaired.GetString()) is { } parsed && parsed != RateUnit.Unrecognized)
            return;
        throw new SecurityMasterWriteRefusedException(
            "The retained rateUnit declaration is not a string and cannot round-trip through this codec. "
            + "Publish an approved assetSpecificTerms.rateUnit repair before amending or deactivating this security; its declaration and field default are not changed.");
    }

}
