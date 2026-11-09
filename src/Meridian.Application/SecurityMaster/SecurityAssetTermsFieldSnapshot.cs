using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// A canonical snapshot of every stored key an approved <c>assetSpecificTerms.*</c> field edit would
/// overwrite when <see cref="ApprovedFieldEditCanonicalMergeHandler"/> merges it. The snapshot is
/// taken at staging and recorded on the revision as its base, then taken again at publish. If the
/// two differ, the canonical field changed after the proposal was drafted, and publishing it would
/// silently overwrite that newer value.
/// </summary>
/// <remarks>
/// The keys mirror what the merge removes before writing:
/// <list type="bullet">
/// <item><description>A top-level declared term: every case variant of its canonical key and of each
/// declared alias.</description></item>
/// <item><description>The <c>profileFields</c> envelope: every case variant of that key.</description></item>
/// <item><description><c>profileFields.x</c>: every case variant of <c>x</c> inside the envelope.</description></item>
/// </list>
/// Keys are sorted with ordinal comparison and values are written as compact JSON, so the snapshot
/// compares as a string. Absent keys snapshot as <c>{}</c>.
/// </remarks>
public static class SecurityAssetTermsFieldSnapshot
{
    private const string ProfileFieldsKey = "profileFields";

    /// <summary>
    /// Snapshots the keys <paramref name="fieldPath"/> governs in <paramref name="assetSpecificTerms"/>,
    /// or returns null when the path is not one the merge writes (outside the asset-terms namespace,
    /// nested deeper than the merge models, or terms that are not a JSON object).
    /// </summary>
    public static string? Capture(string assetClass, JsonElement assetSpecificTerms, string fieldPath)
    {
        if (!SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(fieldPath)
            || assetSpecificTerms.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var remainder = fieldPath.Trim()[SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix.Length..];
        var separatorIndex = remainder.IndexOf('.', StringComparison.Ordinal);
        var key = separatorIndex < 0 ? remainder : remainder[..separatorIndex];
        var nestedPath = separatorIndex < 0 ? string.Empty : remainder[(separatorIndex + 1)..];
        var isProfileFieldsRoot = string.Equals(key, ProfileFieldsKey, StringComparison.OrdinalIgnoreCase);
        if (nestedPath.Length > 0 && (!isProfileFieldsRoot || nestedPath.Contains('.', StringComparison.Ordinal)))
        {
            return null;
        }

        var captured = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (isProfileFieldsRoot && nestedPath.Length > 0)
        {
            foreach (var envelope in assetSpecificTerms.EnumerateObject()
                .Where(property => string.Equals(property.Name, ProfileFieldsKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (envelope.Value.ValueKind != JsonValueKind.Object)
                {
                    captured[$"{envelope.Name}"] = JsonNode.Parse(envelope.Value.GetRawText());
                    continue;
                }

                foreach (var property in envelope.Value.EnumerateObject()
                    .Where(property => string.Equals(property.Name, nestedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    captured[$"{envelope.Name}.{property.Name}"] = JsonNode.Parse(property.Value.GetRawText());
                }
            }
        }
        else
        {
            var keys = new List<string> { key };
            if (!isProfileFieldsRoot && SecurityAssetTermsSchema.Field(assetClass, key) is { } field)
            {
                keys[0] = field.Key;
                keys.AddRange(field.Aliases);
            }

            foreach (var property in assetSpecificTerms.EnumerateObject()
                .Where(property => keys.Any(candidate => string.Equals(property.Name, candidate, StringComparison.OrdinalIgnoreCase))))
            {
                captured[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        var snapshot = new JsonObject();
        foreach (var (name, value) in captured)
        {
            snapshot[name] = value;
        }

        return snapshot.ToJsonString();
    }
}
