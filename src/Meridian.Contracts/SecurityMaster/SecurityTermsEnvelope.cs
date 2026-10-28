using System.Diagnostics.CodeAnalysis;
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
    /// (<see cref="SecurityAssetClassDescriptor.TermsVersion"/> of the writing node, or the newer
    /// version its populated terms need). Absent means version 1. An older version is read through
    /// <see cref="SecurityAssetClassTermsUpgrader"/>; a version above the write version and within
    /// <see cref="SecurityAssetClassDescriptor.ReadsTermsThrough"/> is readable but not writable.
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
    /// <see cref="StampFlat(string, string)"/> at the version a writer's populated terms need: the
    /// class's write version, or, when the record carries terms introduced above it, the highest
    /// readable version among them (a row read from a node that writes the newer version). Throws
    /// when the version is below the write version or above <see cref="SecurityAssetClassCatalog.GetReadsTermsThrough"/>.
    /// </summary>
    public static string StampFlat(string json, string assetClass, int assetClassTermsVersion)
    {
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var reads = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
        if (assetClassTermsVersion < writes || assetClassTermsVersion > reads)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetClassTermsVersion),
                assetClassTermsVersion,
                $"Asset class '{assetClass}' writes {AssetClassTermsVersionKey} {writes} and reads versions {DefaultAssetClassTermsVersion} through {reads}.");
        }

        return StampFlat(json, assetClassTermsVersion);
    }

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
    /// Fails when a flat payload declares an asset-class terms version newer than this node reads for
    /// <paramref name="assetClass"/> (<see cref="SecurityAssetClassCatalog.GetReadsTermsThrough"/>), or
    /// declares a format other than flat or custom profile. Reading such a payload with the current
    /// decoder would silently drop whatever the newer contract added, so the read is refused with a
    /// precise reason instead.
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

        var supported = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
        var declaredVersion = ResolveAssetClassTermsVersion(payload);
        if (declaredVersion > supported || declaredVersion < DefaultAssetClassTermsVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported {AssetClassTermsVersionKey} '{declaredVersion}' for asset class '{assetClass}'; " +
                $"this node reads versions {DefaultAssetClassTermsVersion} through {supported}.");
        }
    }

    /// <summary>
    /// <see cref="EnsureReadableAsAssetSpecificTerms"/> for a write: also fails when the payload is
    /// readable but not writable on this node (<see cref="TryGetWriteRefusal"/>). Every create, amend,
    /// import, term-block amend and approved field-edit merge maps its terms through this check.
    /// </summary>
    public static void EnsureWritableAsAssetSpecificTerms(string assetClass, JsonElement payload)
    {
        // A write reaches the caller, so it names what to submit rather than the read path's conversion.
        var declaredFormat = GetDeclaredFormat(payload);
        if (declaredFormat is not null
            && declaredFormat is not (SecurityTermsFormats.Flat or SecurityTermsFormats.CustomProfile))
        {
            throw new InvalidOperationException(
                $"Terms for asset class '{assetClass}' declare {FormatKey} '{declaredFormat}', which a write does not accept. " +
                $"Submit asset-specific terms: omit {FormatKey}, or set it to '{SecurityTermsFormats.Flat}' or " +
                $"'{SecurityTermsFormats.CustomProfile}'.");
        }

        EnsureReadableAsAssetSpecificTerms(assetClass, payload);
        if (TryGetWriteRefusal(assetClass, payload, out var reason))
        {
            throw new InvalidOperationException(reason);
        }
    }

    /// <summary>
    /// Why a payload this node reads cannot be written by it: it is stamped above the class's write
    /// version, or it populates a term introduced above that version (ADR-024, readable before
    /// writable). Absent, null, <c>[]</c> and all-null objects mean unset and are accepted. A
    /// profile-governed payload (<see cref="IsProfileGovernedPayload"/>) has no versioned class terms,
    /// so only its stamp is checked. A class that reads exactly what it writes is never refused here,
    /// and neither is a payload stamped above what this node reads: that is the read guard's refusal
    /// (<see cref="EnsureReadableAsAssetSpecificTerms"/>), which names the newer version.
    /// </summary>
    public static bool TryGetWriteRefusal(string assetClass, JsonElement payload, [NotNullWhen(true)] out string? reason)
    {
        reason = null;
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var reads = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
        if (reads == writes)
        {
            return false;
        }

        var declaredVersion = ResolveAssetClassTermsVersion(payload);
        if (declaredVersion > reads)
        {
            return false;
        }

        if (declaredVersion > writes)
        {
            // A flat payload can populate a term introduced above its own stamp; only a node that
            // writes that term's version can apply it.
            var needed = declaredVersion;
            if (!IsProfileGovernedPayload(assetClass, payload))
            {
                foreach (var (_, field) in SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, declaredVersion))
                {
                    needed = Math.Max(needed, field.SinceTermsVersion);
                }
            }

            reason =
                $"{AssetClassTermsVersionKey} '{declaredVersion}' for asset class '{assetClass}' is readable but not writable on this node: " +
                $"it reads versions {DefaultAssetClassTermsVersion} through {reads} and writes version {writes}. " +
                $"Submit the terms at version {writes}, or apply the change from a node that writes version {needed}.";
            return true;
        }

        if (IsProfileGovernedPayload(assetClass, payload))
        {
            return false;
        }

        var populated = SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, writes);
        if (populated.Count == 0)
        {
            return false;
        }

        reason = DescribeNotYetWritable(assetClass, populated[0].Path, populated[0].Field.SinceTermsVersion);
        return true;
    }

    /// <summary>
    /// The shared reason a term this node reads cannot be set by it: the refusal every write path,
    /// term-block amend and field edit gives for a term introduced above the class's write version.
    /// </summary>
    public static string DescribeNotYetWritable(string assetClass, string path, int sinceTermsVersion)
        => $"'{path}' is an asset-class terms version {sinceTermsVersion} term of '{assetClass}'. This node reads it but writes " +
           $"{AssetClassTermsVersionKey} {SecurityAssetClassCatalog.GetTermsVersion(assetClass)}, so a create, amend, import or field edit " +
           "cannot set it yet. Leave it absent, null or empty (ADR-024).";

    /// <summary>
    /// Whether a payload of <paramref name="assetClass"/> is a profile envelope: the class supports
    /// profile-backed terms and the payload carries a <c>customProfileId</c> or a <c>profileFields</c>
    /// object. Its fields belong to the approved profile, not to the class's terms version, so
    /// versioned class terms are never walked, decoded or validated in it; only its stamp is checked.
    /// </summary>
    public static bool IsProfileGovernedPayload(string assetClass, JsonElement payload)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms
           && CarriesProfileEnvelope(payload);

    /// <summary>
    /// Whether a terms object carries a profile envelope signal: a non-blank string
    /// <c>customProfileId</c> or a <c>profileFields</c> object. Non-objects carry none.
    /// </summary>
    public static bool CarriesProfileEnvelope(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && ((payload.TryGetProperty("customProfileId", out var profileId)
                && profileId.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(profileId.GetString()))
               || (payload.TryGetProperty("profileFields", out var profileFields)
                   && profileFields.ValueKind == JsonValueKind.Object));

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
