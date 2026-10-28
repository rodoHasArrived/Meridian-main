using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Request to replace one nested term block (an object or array term such as a preferred-terms
/// object, a call schedule or swap legs) on any asset class.
/// </summary>
public sealed record AmendSecurityTermBlockRequest(
    long ExpectedVersion,
    JsonElement Block,
    DateTimeOffset EffectiveFrom,
    string SourceSystem,
    string UpdatedBy,
    string? SourceRecordId,
    string? Reason);

/// <summary>
/// The nested term blocks each asset class declares, taken from the asset-class registry: every
/// <see cref="SecurityAssetTermFieldType.Object"/> or <see cref="SecurityAssetTermFieldType.Array"/>
/// field. Scalars go through the governed field-edit route; blocks are replaced whole here, so no
/// asset class needs its own amendment endpoint for its nested structures.
/// </summary>
public static class SecurityTermBlocks
{
    /// <summary>The event type recorded for a generic block replacement.</summary>
    public const string AmendedEventType = "TermBlockAmended";

    public static IReadOnlyList<SecurityAssetTermField> For(string assetClass)
        => SecurityAssetClassRegistry.TryGet(assetClass, out var definition)
            ? definition.Fields
                .Where(static field => field.Type is SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array)
                .ToArray()
            : [];

    /// <summary>
    /// Whether <paramref name="blockKey"/> names a block <paramref name="assetClass"/> declares but this
    /// node only reads (ADR-024, readable before writable), with the not-yet-writable reason a block
    /// amendment of it is refused with.
    /// </summary>
    public static bool TryDescribeReadableOnlyBlock(string assetClass, string blockKey, [NotNullWhen(true)] out string? reason)
    {
        reason = null;
        var key = blockKey.Trim();
        if (SecurityAssetTermsSchema.Field(assetClass, key) is not { } declared
            || !string.Equals(declared.Key, key, StringComparison.Ordinal)
            || declared.Type is not (SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array)
            || declared.SinceTermsVersion <= SecurityAssetClassCatalog.GetTermsVersion(assetClass))
        {
            return false;
        }

        reason = SecurityTermsEnvelope.DescribeNotYetWritable(assetClass, key, declared.SinceTermsVersion);
        return true;
    }

    /// <summary>
    /// Returns the asset-specific terms with <paramref name="blockKey"/> replaced by
    /// <paramref name="block"/>. Every other term is carried unchanged. Throws
    /// <see cref="ArgumentException"/> when the class declares no such block or the JSON kind does not
    /// match the declared type.
    /// </summary>
    public static JsonElement BuildPatch(string assetClass, JsonElement currentTerms, string blockKey, JsonElement block)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockKey);

        var blocks = For(assetClass);
        var field = blocks.FirstOrDefault(candidate => string.Equals(candidate.Key, blockKey.Trim(), StringComparison.Ordinal))
            ?? throw new ArgumentException(
                TryDescribeReadableOnlyBlock(assetClass, blockKey, out var readableOnly)
                    ? readableOnly
                    : $"Asset class '{assetClass}' declares no term block '{blockKey}'. Declared blocks: " +
                      (blocks.Count == 0 ? "none" : string.Join(", ", blocks.Select(static candidate => candidate.Key))) + ".",
                nameof(blockKey));

        var expectedKind = field.Type == SecurityAssetTermFieldType.Array ? JsonValueKind.Array : JsonValueKind.Object;
        var nullable = !field.Required && block.ValueKind == JsonValueKind.Null;
        if (block.ValueKind != expectedKind && !nullable)
        {
            throw new ArgumentException(
                $"Term block '{field.Key}' on '{assetClass}' must be a JSON {expectedKind.ToString().ToLowerInvariant()}" +
                (field.Required ? "." : " or null."),
                nameof(block));
        }

        if (currentTerms.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null))
        {
            throw new ArgumentException("Current asset-specific terms must be a JSON object.", nameof(currentTerms));
        }

        var terms = currentTerms.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(currentTerms.GetRawText())!.AsObject()
            : new JsonObject();
        // A block stored under a declared alias is replaced, not duplicated beside the canonical key.
        foreach (var alias in field.Aliases)
        {
            terms.Remove(alias);
        }

        if (nullable)
        {
            terms.Remove(field.Key);
        }
        else
        {
            terms[field.Key] = JsonNode.Parse(block.GetRawText());
        }

        using var document = JsonDocument.Parse(terms.ToJsonString());
        return document.RootElement.Clone();
    }
}
