using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

public static partial class ScheduleTermsWriteRules
{
    private static void EnsureAmendmentDoesNotPromoteStrayScalars(
        string assetClass, JsonElement retained, JsonElement? replacement)
    {
        var canonical = SecurityAssetClassCatalog.Canonicalize(assetClass);
        if (!SecurityAssetTermsCodecBindings.ByAssetClass.TryGetValue(canonical, out var binding)
            || binding.VersionedOptionalScalarKeys.Count == 0)
            return;
        // Only modules opting into scalar read activation participate. Earlier scalar codecs keep
        // their historical amendment behavior, including their existing explicit-null byte shapes.
        var keys = binding.VersionedOptionalScalarKeys.ToHashSet(StringComparer.Ordinal);
        var stamp = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(retained);
        var writes = SecurityAssetClassCatalog.GetTermsVersion(canonical);
        var replacementStamp = replacement is { } submitted
            ? SecurityTermsEnvelope.ResolveAssetClassTermsVersion(submitted) : 0;
        var populatedReplacement = replacement is { } document
            ? SecurityAssetTermsSchema.PopulatedTermsAbove(canonical, document, 1)
                .Select(static term => term.Field.Key).ToHashSet(StringComparer.Ordinal)
            : null;
        foreach (var term in SecurityAssetTermsSchema.PopulatedTermsAbove(canonical, retained, stamp)
                     .Where(term => keys.Contains(term.Field.Key) && term.Field.AuthoringEnabled
                         && term.Field.SinceTermsVersion <= writes))
        {
            // A replacement is the complete submitted terms, so omission explicitly clears a
            // scalar. A higher stamp explicitly reviews all expanded terms in that replacement;
            // changing one field without that review must not activate another retained scalar.
            if (populatedReplacement is not null && (!populatedReplacement.Contains(term.Field.Key)
                    || replacementStamp >= term.Field.SinceTermsVersion))
                continue;
            Refuse($"Retained undeclared scalar '{term.Field.Key}' cannot be promoted by an unrelated amendment; explicitly clear it or submit complete reviewed terms stamped at version {term.Field.SinceTermsVersion} or later.");
        }
    }
}
