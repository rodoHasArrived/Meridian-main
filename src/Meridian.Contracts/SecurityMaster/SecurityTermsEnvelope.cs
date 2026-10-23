using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The payload families a Security Master terms document can belong to. Written into every flat
/// terms payload as <see cref="SecurityTermsEnvelope.FormatKey"/> so readers dispatch on what the
/// document says it is instead of on its bare <c>schemaVersion</c> integer.
/// </summary>
public static class SecurityTermsFormats
{
    /// <summary>Flat per-asset-class terms (<see cref="AssetSpecificTermsSchema.Legacy"/>).</summary>
    public const string Flat = "flat";

    /// <summary>Structured economic-terms modules (<see cref="EconomicTermsSchema.Current"/>).</summary>
    public const string Economic = "economic";

    /// <summary>Custom-asset profile envelope (<see cref="AssetSpecificTermsSchema.CustomAssetProfile"/>).</summary>
    public const string CustomProfile = "customProfile";

    /// <summary>Every format name this node understands.</summary>
    public static IReadOnlyList<string> All { get; } = [Flat, Economic, CustomProfile];

    /// <summary>Whether <paramref name="format"/> names a known format (case-sensitive, as written).</summary>
    public static bool IsKnown(string? format)
        => format is Flat or Economic or CustomProfile;
}

/// <summary>
/// The envelope keys a Security Master terms payload carries alongside its terms, and the single
/// place that decides which payload family a stored document belongs to.
/// <para>
/// The asset-specific-terms slot historically held two payload families behind one integer
/// <c>schemaVersion</c> key, so the reader had to guess the family from the integer plus the
/// presence of module keys — which is why flat version 2 was permanently reserved. Every payload
/// now states its family in <see cref="FormatKey"/> and, for flat payloads, the asset-class terms
/// contract version it was written against in <see cref="AssetClassTermsVersionKey"/>. Readers that
/// see an explicit format dispatch on it; payloads written before the tag existed fall back to the
/// historical shape heuristic, which stays correct for every document already stored.
/// </para>
/// </summary>
public static class SecurityTermsEnvelope
{
    /// <summary>The codec version of the payload family.</summary>
    public const string SchemaVersionKey = "schemaVersion";

    /// <summary>The payload family (<see cref="SecurityTermsFormats"/>).</summary>
    public const string FormatKey = "termsFormat";

    /// <summary>
    /// The asset-class terms contract version a flat payload was written against
    /// (<see cref="SecurityAssetClassDescriptor.TermsVersion"/>). Absent means version 1. An older
    /// version is read through <see cref="SecurityAssetClassTermsUpgrader"/>.
    /// </summary>
    public const string AssetClassTermsVersionKey = "assetClassTermsVersion";

    /// <summary>
    /// The verbatim economic-terms document a flat payload was reconstructed from. Written only by
    /// the economic → flat bridge so that nothing the economic document carried is lost when the
    /// flat family cannot express it.
    /// </summary>
    public const string RetainedEconomicTermsKey = "retainedEconomicTerms";

    /// <summary>The asset-class terms version a flat payload without an explicit version is read as.</summary>
    public const int DefaultAssetClassTermsVersion = 1;

    /// <summary>
    /// Envelope keys that describe the payload rather than the security. They are never term fields,
    /// never operator-editable, and are excluded from term-schema key comparisons.
    /// </summary>
    public static IReadOnlySet<string> ReservedKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SchemaVersionKey,
        FormatKey,
        AssetClassTermsVersionKey,
        RetainedEconomicTermsKey,
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty
    };

    /// <summary>Whether <paramref name="key"/> is an envelope key rather than a term field.</summary>
    public static bool IsReservedKey(string? key)
        => key is not null && ReservedKeys.Contains(key);

    /// <summary>
    /// Returns the explicit <see cref="FormatKey"/> value, or <see langword="null"/> when the payload
    /// predates the tag (or is not an object).
    /// </summary>
    public static string? GetDeclaredFormat(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty(FormatKey, out var format)
           && format.ValueKind == JsonValueKind.String
            ? format.GetString()
            : null;

    /// <summary>
    /// Resolves the payload family. An explicit <see cref="FormatKey"/> wins; an untagged payload is
    /// classified by the historical shape rules: a profile envelope is
    /// <see cref="SecurityTermsFormats.CustomProfile"/>, a version-2 document carrying economic
    /// module keys is <see cref="SecurityTermsFormats.Economic"/>, and anything else is
    /// <see cref="SecurityTermsFormats.Flat"/>.
    /// </summary>
    public static string ResolveFormat(JsonElement payload)
    {
        if (GetDeclaredFormat(payload) is { } declared)
        {
            return declared;
        }

        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("customProfileId", out var profileId)
            && profileId.ValueKind == JsonValueKind.String
            && payload.TryGetProperty("profileFields", out var profileFields)
            && profileFields.ValueKind == JsonValueKind.Object)
        {
            return SecurityTermsFormats.CustomProfile;
        }

        return SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsShape(payload)
            ? SecurityTermsFormats.Economic
            : SecurityTermsFormats.Flat;
    }

    /// <summary>
    /// The asset-class terms version a flat payload declares, or
    /// <see cref="DefaultAssetClassTermsVersion"/> when it predates the key.
    /// </summary>
    public static int ResolveAssetClassTermsVersion(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty(AssetClassTermsVersionKey, out var version)
           && version.ValueKind == JsonValueKind.Number
           && version.TryGetInt32(out var value)
            ? value
            : DefaultAssetClassTermsVersion;

    /// <summary>
    /// Stamps <see cref="FormatKey"/> = <see cref="SecurityTermsFormats.Flat"/> and the asset class's
    /// current <see cref="AssetClassTermsVersionKey"/> onto a serialized flat terms document,
    /// immediately after its <c>schemaVersion</c>. Keys the document already carries are replaced, so
    /// stamping is idempotent. Non-object JSON is returned unchanged.
    /// </summary>
    public static string StampFlat(string json, string assetClass)
        => StampFlat(json, SecurityAssetClassCatalog.GetTermsVersion(assetClass));

    /// <summary>
    /// <see cref="StampFlat(string, string)"/> with an explicit asset-class terms version: the stamping
    /// routine every flat writer and the upgrade hook's transform path share
    /// (<see cref="SecurityAssetClassTermsUpgrader"/>). The envelope keys go immediately after
    /// <c>schemaVersion</c> when the document carries one, and last otherwise; keys the document
    /// already carries are replaced, so stamping is idempotent. Non-object JSON is returned unchanged.
    /// </summary>
    public static string StampFlat(string json, int assetClassTermsVersion)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentOutOfRangeException.ThrowIfLessThan(assetClassTermsVersion, DefaultAssetClassTermsVersion);
        if (JsonNode.Parse(json) is not JsonObject source)
        {
            return json;
        }

        var stamped = new JsonObject();
        var wroteEnvelope = false;
        void WriteEnvelope()
        {
            stamped[FormatKey] = SecurityTermsFormats.Flat;
            stamped[AssetClassTermsVersionKey] = assetClassTermsVersion;
            wroteEnvelope = true;
        }

        foreach (var (key, value) in source.ToArray())
        {
            if (key is FormatKey or AssetClassTermsVersionKey)
            {
                continue;
            }

            source.Remove(key);
            stamped[key] = value;
            if (key == SchemaVersionKey)
            {
                WriteEnvelope();
            }
        }

        if (!wroteEnvelope)
        {
            WriteEnvelope();
        }

        return stamped.ToJsonString();
    }

    /// <summary>
    /// Fails when a flat payload declares an asset-class terms version newer than this node's
    /// contract for <paramref name="assetClass"/>, or declares a format other than flat or custom
    /// profile. Reading such a payload with the current decoder would silently drop whatever the
    /// newer contract added, so the read is refused with a precise reason instead.
    /// </summary>
    public static void EnsureReadableAsAssetSpecificTerms(string assetClass, JsonElement payload)
    {
        var declaredFormat = GetDeclaredFormat(payload);
        if (declaredFormat is not null
            && declaredFormat is not (SecurityTermsFormats.Flat or SecurityTermsFormats.CustomProfile))
        {
            throw new InvalidOperationException(
                $"Terms payload for asset class '{assetClass}' declares {FormatKey} '{declaredFormat}', which is not an " +
                "asset-specific-terms format; route it through SecurityAssetSpecificTermsUpcasterChain before reading.");
        }

        var supported = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var declaredVersion = ResolveAssetClassTermsVersion(payload);
        if (declaredVersion > supported || declaredVersion < DefaultAssetClassTermsVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported {AssetClassTermsVersionKey} '{declaredVersion}' for asset class '{assetClass}'; " +
                $"this node reads versions {DefaultAssetClassTermsVersion} through {supported}.");
        }
    }

    /// <summary>
    /// Returns the economic-terms document a flattened payload retained under
    /// <see cref="RetainedEconomicTermsKey"/>, when it carries one.
    /// </summary>
    public static bool TryGetRetainedEconomicTerms(JsonElement flatTerms, out JsonElement retained)
    {
        if (flatTerms.ValueKind == JsonValueKind.Object
            && flatTerms.TryGetProperty(RetainedEconomicTermsKey, out var value)
            && value.ValueKind == JsonValueKind.Object)
        {
            retained = value;
            return true;
        }

        retained = default;
        return false;
    }

    /// <summary>
    /// Carries a retained economic-terms document from <paramref name="previousFlatTerms"/> onto
    /// <paramref name="nextFlatTerms"/> when the next revision was rebuilt through the domain codec
    /// (which knows only the declared flat keys) and so no longer carries it. Returns
    /// <paramref name="nextFlatTerms"/> unchanged when there is nothing to carry or it already has
    /// its own retained document.
    /// </summary>
    public static JsonElement CarryRetainedEconomicTerms(JsonElement previousFlatTerms, JsonElement nextFlatTerms)
    {
        if (!TryGetRetainedEconomicTerms(previousFlatTerms, out var retained)
            || nextFlatTerms.ValueKind != JsonValueKind.Object
            || TryGetRetainedEconomicTerms(nextFlatTerms, out _)
            || JsonNode.Parse(nextFlatTerms.GetRawText()) is not JsonObject next)
        {
            return nextFlatTerms;
        }

        next[RetainedEconomicTermsKey] = JsonNode.Parse(retained.GetRawText());
        if (previousFlatTerms.TryGetProperty(
                SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty,
                out var marker)
            && !next.ContainsKey(SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty))
        {
            next[SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty] = JsonNode.Parse(marker.GetRawText());
        }

        using var document = JsonDocument.Parse(next.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Folds a retained economic-terms document back into economic terms rebuilt from flat terms.
    /// Rebuilt values always win; the retained document only fills what the flat family could not
    /// carry: a module the rebuilt document has as null or absent, a field a rebuilt module has as
    /// null or absent, or a schedule it has as an empty array. Envelope keys of the retained document
    /// are never folded. The result is the economic record the original economic event described,
    /// updated by whatever the flat revision changed.
    /// </summary>
    public static JsonElement FoldRetainedEconomicTerms(JsonElement rebuiltEconomicTerms, JsonElement retainedEconomicTerms)
    {
        if (rebuiltEconomicTerms.ValueKind != JsonValueKind.Object
            || retainedEconomicTerms.ValueKind != JsonValueKind.Object
            || JsonNode.Parse(rebuiltEconomicTerms.GetRawText()) is not JsonObject rebuilt)
        {
            return rebuiltEconomicTerms;
        }

        foreach (var module in retainedEconomicTerms.EnumerateObject())
        {
            if (IsReservedKey(module.Name) || module.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var existing = rebuilt[module.Name];
            if (existing is null)
            {
                rebuilt[module.Name] = JsonNode.Parse(module.Value.GetRawText());
                continue;
            }

            if (existing is not JsonObject rebuiltModule || module.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var field in module.Value.EnumerateObject())
            {
                if (field.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    continue;
                }

                var current = rebuiltModule[field.Name];
                var fill = current is null
                           || (current is JsonArray currentArray
                               && currentArray.Count == 0
                               && field.Value.ValueKind == JsonValueKind.Array
                               && field.Value.GetArrayLength() > 0);
                if (fill)
                {
                    rebuiltModule[field.Name] = JsonNode.Parse(field.Value.GetRawText());
                }
            }
        }

        using var document = JsonDocument.Parse(rebuilt.ToJsonString());
        return document.RootElement.Clone();
    }
}
