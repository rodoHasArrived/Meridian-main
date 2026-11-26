using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>Pure field-overlay construction shared by impact preview and governed publication.</summary>
public sealed class SecurityMasterProposedTermsBuilder(
    Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
{
    private const string ProfileFieldsKey = "profileFields";
    private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? _assetProfileCatalog = assetProfileCatalog;

    public JsonElement Build(SecurityProjectionRecord projection, SecurityMasterRevisionRecord revision)
    {
        if (!revision.FieldValueRecorded || revision.FieldValue is null || revision.FieldPath is null
            || !SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(revision.FieldPath))
            return projection.AssetSpecificTerms.Clone();
        var remainder = revision.FieldPath.Trim()[SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix.Length..];
        var separator = remainder.IndexOf('.', StringComparison.Ordinal);
        var key = separator < 0 ? remainder : remainder[..separator];
        var nested = separator < 0 ? string.Empty : remainder[(separator + 1)..];
        var profile = string.Equals(key, ProfileFieldsKey, StringComparison.OrdinalIgnoreCase);
        if (nested.Length > 0 && (!profile || nested.Contains('.', StringComparison.Ordinal)))
            return projection.AssetSpecificTerms.Clone();
        var merged = JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())!.AsObject();
        if (profile && nested.Length > 0)
            ApplyProfileField(merged, projection, nested, revision.FieldValue);
        else if (profile)
        {
            RemoveKeyVariants(merged, ProfileFieldsKey);
            merged[ProfileFieldsKey] = JsonNode.Parse(revision.FieldValue);
        }
        else
            ApplyDeclaredTerm(merged, projection.AssetClass, key, revision.FieldValue);
        return JsonSerializer.SerializeToElement(merged);
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
            : field?.Type is SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array
                ? JsonNode.Parse(value.Trim())
                : TryParseJsonNode(value.Trim()) ?? JsonValue.Create(value);
        if (field?.Type is SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array
            || field?.Key == SecurityRecordRateUnit.FieldKey
            || SecurityAssetTermsCodecBindings.ByAssetClass.TryGetValue(assetClass, out var codec)
                && codec.VersionedOptionalScalarKeys.Contains(canonicalKey, StringComparer.Ordinal))
        {
            // The approved whole-container edit explicitly authors this declaration. Match the
            // staging preflight's stamp promotion so repairing an old row's stray container does
            // not become an unrelated amendment by the time it reaches canonical validation.
            var replacement = JsonSerializer.SerializeToElement(new Dictionary<string, JsonNode?> { [canonicalKey] = merged[canonicalKey]?.DeepClone() });
            var since = SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, replacement, 1)
                .Where(term => term.Field.SinceTermsVersion <= SecurityAssetClassCatalog.GetTermsVersion(assetClass))
                .Select(term => term.Field.SinceTermsVersion).DefaultIfEmpty(1).Max();
            var stamp = merged[SecurityTermsEnvelope.AssetClassTermsVersionKey]?.GetValue<int>() ?? 1;
            if (since > stamp)
                merged[SecurityTermsEnvelope.AssetClassTermsVersionKey] = since;
        }
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

}
