using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The per-asset-class terms upgrade hook (ADR-024 decision 3). Reads a flat asset-specific-terms
/// payload stamped with an older <see cref="SecurityTermsEnvelope.AssetClassTermsVersionKey"/> at its
/// class's current <see cref="SecurityAssetClassDescriptor.TermsVersion"/> by running the class's
/// <see cref="SecurityAssetClassDescriptor.TermsUpgrades"/>.
/// </summary>
/// <remarks>
/// <para>Identity steps return the stored element itself: the same document and index, with no parse,
/// clone or allocation. A transform step returns a new in-memory payload stamped at the current
/// version. Nothing here writes a stored payload, and the <c>schema_version</c> and
/// <c>terms_format</c> columns stay derived from the stored blob.</para>
/// <para>The hook acts only on objects of the flat codec — <c>schemaVersion</c>
/// <see cref="AssetSpecificTermsSchema.Legacy"/> (absent included), flat by tag or by shape, and
/// carrying neither a <c>customProfileId</c> nor a <c>profileFields</c> object — stamped from version
/// 1 up to, but not including, the class's current version. Everything else passes through
/// unchanged: economic documents, custom-profile envelopes, other codec numbers, payloads already at
/// the current version and payloads stamped newer. It never refuses a payload; refusing a newer
/// stamp stays <see cref="SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms"/>'s job. It
/// throws only when a class's declared steps, or one of its transforms, are defective.</para>
/// <para>"Current" is the class's WRITE version (<see cref="SecurityAssetClassDescriptor.TermsVersion"/>).
/// A payload stamped above it and within <see cref="SecurityAssetClassDescriptor.ReadsTermsThrough"/>
/// passes through unchanged: every step from the write version upward is an identity step, so there
/// is nothing to run, and such a payload is not behind.</para>
/// </remarks>
public static class SecurityAssetClassTermsUpgrader
{
    /// <summary>
    /// The payload read at the class's current terms version: <paramref name="payload"/> itself unless
    /// a transform step applies. The class is resolved through
    /// <see cref="SecurityAssetClassCatalog.GetOrDefault"/>, so an alias reads as its class and an
    /// unknown or blank class reads as version 1, which passes everything through.
    /// </summary>
    public static JsonElement UpgradeForRead(string? assetClass, JsonElement payload)
        => UpgradeForRead(SecurityAssetClassCatalog.GetOrDefault(assetClass), payload);

    /// <summary><see cref="UpgradeForRead(string?, JsonElement)"/> for a descriptor already in hand.</summary>
    public static JsonElement UpgradeForRead(SecurityAssetClassDescriptor descriptor, JsonElement payload)
        => TryUpgradeForRead(descriptor, payload, out var upgraded) ? upgraded : payload;

    /// <summary>
    /// True, with a new payload stamped at the class's S2 transform floor, only when at least one
    /// transform step applies. Otherwise false, with <paramref name="upgraded"/> set to
    /// <paramref name="payload"/> itself, so a caller can branch without comparing
    /// <see cref="JsonElement"/> values.
    /// </summary>
    public static bool TryUpgradeForRead(string? assetClass, JsonElement payload, out JsonElement upgraded)
        => TryUpgradeForRead(SecurityAssetClassCatalog.GetOrDefault(assetClass), payload, out upgraded);

    /// <summary><see cref="TryUpgradeForRead(string?, JsonElement, out JsonElement)"/> for a descriptor already in hand.</summary>
    public static bool TryUpgradeForRead(
        SecurityAssetClassDescriptor descriptor,
        JsonElement payload,
        out JsonElement upgraded)
    {
        upgraded = payload;
        if (!TryGetPendingUpgrade(descriptor, payload, out var declared, out var current))
        {
            return false;
        }

        var steps = descriptor.TermsUpgrades ?? [];
        var transforms = false;
        for (var version = declared; version < current; version++)
        {
            transforms |= RequireStep(descriptor, steps, version, declared, current).Kind == SecurityTermsUpgradeKind.Transform;
        }

        if (!transforms)
        {
            return false;
        }

        upgraded = RunTransforms(descriptor, steps, payload, declared, current);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="payload"/> is inside the hook's domain and is stamped (or, when the
    /// stamp is absent or not an integer, reads as) below its class's current terms version. This is
    /// the single predicate the runner and the <c>SM_ASSET_CLASS_TERMS_VERSION_BEHIND</c> validation
    /// notice share. <paramref name="declaredVersion"/> is the version the payload reads as (1 when
    /// unstamped) and <paramref name="currentVersion"/> the class's current version.
    /// </summary>
    public static bool TryGetPendingUpgrade(
        string? assetClass,
        JsonElement payload,
        out int declaredVersion,
        out int currentVersion)
        => TryGetPendingUpgrade(SecurityAssetClassCatalog.GetOrDefault(assetClass), payload, out declaredVersion, out currentVersion);

    /// <summary><see cref="TryGetPendingUpgrade(string?, JsonElement, out int, out int)"/> for a descriptor already in hand.</summary>
    public static bool TryGetPendingUpgrade(
        SecurityAssetClassDescriptor descriptor,
        JsonElement payload,
        out int declaredVersion,
        out int currentVersion)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        currentVersion = descriptor.TermsVersion;
        declaredVersion = SecurityTermsEnvelope.DefaultAssetClassTermsVersion;

        // Cheapest exits first: every class still at version 1, and anything that is not an object.
        if (currentVersion <= SecurityTermsEnvelope.DefaultAssetClassTermsVersion || payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // A payload written since the class's last bump, a newer stamp (EnsureReadable refuses it
        // where a guard runs) and a stamp below 1 all leave here after one property lookup.
        declaredVersion = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(payload);
        if (declaredVersion >= currentVersion || declaredVersion < SecurityTermsEnvelope.DefaultAssetClassTermsVersion)
        {
            return false;
        }

        return IsFlatCodecPayload(payload);
    }

    /// <summary>
    /// The codec axis (<c>schemaVersion</c> is the flat codec's), the family axis (flat by tag or by
    /// shape) and the profile signals the mapping reads (<c>customProfileId</c>, <c>profileFields</c>):
    /// a profile-governed payload's fields belong to its profile, not to the class's terms version.
    /// </summary>
    private static bool IsFlatCodecPayload(JsonElement payload)
        => SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(payload) == AssetSpecificTermsSchema.Legacy
           && string.Equals(SecurityTermsEnvelope.ResolveFormat(payload), SecurityTermsFormats.Flat, StringComparison.Ordinal)
           && !SecurityTermsEnvelope.CarriesProfileEnvelope(payload);

    private static SecurityAssetClassTermsUpgrade RequireStep(
        SecurityAssetClassDescriptor descriptor,
        IReadOnlyList<SecurityAssetClassTermsUpgrade> steps,
        int fromVersion,
        int declared,
        int current)
    {
        SecurityAssetClassTermsUpgrade? match = null;
        var matches = 0;
        for (var index = 0; index < steps.Count; index++)
        {
            if (steps[index] is { } step && step.FromVersion == fromVersion)
            {
                match = step;
                matches++;
            }
        }

        if (matches != 1 || match is null)
        {
            throw new InvalidOperationException(
                $"Asset class '{descriptor.AssetClass}' is at terms version {current} but does not declare exactly one " +
                $"upgrade step for each version from {declared} to {current - 1}; declare them in " +
                $"{nameof(SecurityAssetClassDescriptor)}.{nameof(SecurityAssetClassDescriptor.TermsUpgrades)}.");
        }

        return match;
    }

    private static JsonElement RunTransforms(
        SecurityAssetClassDescriptor descriptor,
        IReadOnlyList<SecurityAssetClassTermsUpgrade> steps,
        JsonElement payload,
        int declared,
        int current)
    {
        // The input element is never mutated: each transform works on a private parsed copy.
        var node = (JsonObject)JsonNode.Parse(payload.GetRawText())!;
        for (var version = declared; version < current; version++)
        {
            var step = RequireStep(descriptor, steps, version, declared, current);
            if (step.Kind != SecurityTermsUpgradeKind.Transform)
            {
                continue;
            }

            node = step.Transform!(node)
                ?? throw new InvalidOperationException(
                    $"The terms upgrade from version {step.FromVersion} for asset class '{descriptor.AssetClass}' returned no payload.");
        }

        using var document = JsonDocument.Parse(SecurityTermsEnvelope.StampFlat(node.ToJsonString(), SecurityAssetClassCatalog.GetStampFloor(descriptor)));
        var result = document.RootElement;
        var hadSchemaVersion = payload.TryGetProperty(SecurityTermsEnvelope.SchemaVersionKey, out _);
        var hasSchemaVersion = result.TryGetProperty(SecurityTermsEnvelope.SchemaVersionKey, out _);
        if (hadSchemaVersion != hasSchemaVersion
            || SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(result) != AssetSpecificTermsSchema.Legacy)
        {
            throw new InvalidOperationException(
                $"The terms upgrade to version {current} for asset class '{descriptor.AssetClass}' changed the codec " +
                $"{SecurityTermsEnvelope.SchemaVersionKey}; a terms upgrade changes the class's terms, never the codec version.");
        }

        return result.Clone();
    }
}
