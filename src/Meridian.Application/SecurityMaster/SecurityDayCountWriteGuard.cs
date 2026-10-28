using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>Prevents typed day-count rewrites from erasing invalid historical source values.</summary>
internal static class SecurityDayCountWriteGuard
{
    // Invalid historical day counts remain readable as Unknown, but cannot be rewritten over the
    // original text by a common-terms amendment or deactivation. A full replacement terms document
    // is the repair path, and the write-mode codec validates its supplied conventions.
    internal static void EnsureRoundTrip(
        SecurityProjectionRecord projection, JsonElement? assetSpecificTermsPatch)
    {
        if (assetSpecificTermsPatch is JsonElement)
            return;
        var invalid = InvalidDayCountPaths(projection.AssetClass, projection.AssetSpecificTerms, "").ToArray();
        if (invalid.Length > 0)
            throw new SecurityMasterWriteRefusedException(
                $"Security '{projection.SecurityId:D}' carries unsupported day-count values at {string.Join(", ", invalid)}. " +
                "The write is refused because re-serializing would replace the original values with Unknown. " +
                "Repair the security with a complete asset-terms document supplying supported day-count conventions first.");
    }

    private static IEnumerable<string> InvalidDayCountPaths(string assetClass, JsonElement document, string path)
    {
        if (document.ValueKind != JsonValueKind.Object)
            yield break;
        if (path.Length == 0 && document.TryGetProperty("profileFields", out var profileFields)
            && profileFields.ValueKind == JsonValueKind.Object)
            document = profileFields;
        var fields = path.Length == 0
            ? SecurityAssetTermsSchema.Fields(assetClass)
            : SecurityAssetTermsSchema.ElementFields(assetClass, path);
        foreach (var field in fields)
        {
            foreach (var key in new[] { field.Key }.Concat(field.Aliases))
            {
                if (!document.TryGetProperty(key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;
                var fieldPath = path.Length == 0 ? field.Key : $"{path}.{field.Key}";
                if (field.Convention == SecurityTermConventionKind.DayCount)
                {
                    if (value.ValueKind != JsonValueKind.String ||
                        (!string.IsNullOrWhiteSpace(value.GetString()) && !DayCountConventions.TryParseTerm(value.GetString(), out _)))
                        yield return fieldPath;
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                        foreach (var invalid in InvalidDayCountPaths(assetClass, item, fieldPath))
                            yield return invalid;
                }
                else if (value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var invalid in InvalidDayCountPaths(assetClass, value, fieldPath))
                        yield return invalid;
                }
            }
        }
    }

}
