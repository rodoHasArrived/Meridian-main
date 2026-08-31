using System.IO;
using System.Text.Json;
using Meridian.Contracts.Schema;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// An asset-specific-terms payload paired with the schema version it resolves to. Produced by
/// <see cref="SecurityAssetSpecificTermsV0ToCurrentUpcaster"/> so callers get both the normalized
/// (explicitly versioned) JSON and the version to persist into the queryable <c>schema_version</c>
/// column in one step.
/// </summary>
public sealed record SecurityAssetSpecificTerms(JsonElement Payload, int SchemaVersion);

/// <summary>
/// Migrate-on-read upcaster for Security Master asset-specific-terms payloads. Legacy payloads were
/// persisted before explicit versioning and carry no <c>schemaVersion</c>; on read they are treated
/// as <see cref="SecurityMasterSchemaVersions.LegacyAssetSpecificTerms"/> and stamped so every
/// downstream consumer — validation, projection, and the promoted <c>schema_version</c> column —
/// sees an explicitly versioned payload without a bulk data migration.
/// </summary>
public sealed class SecurityAssetSpecificTermsV0ToCurrentUpcaster : ISchemaUpcaster<SecurityAssetSpecificTerms>
{
    /// <summary>Shared stateless instance for callers that resolve the upcaster outside DI.</summary>
    public static SecurityAssetSpecificTermsV0ToCurrentUpcaster Instance { get; } = new();

    /// <summary>The unstamped legacy payload family (no <c>schemaVersion</c> property).</summary>
    public int FromSchemaVersion => 0;

    /// <summary>The version an unstamped payload is normalized to.</summary>
    public int ToSchemaVersion => SecurityMasterSchemaVersions.DefaultAssetSpecificTerms;

    /// <summary>
    /// Reads a stored asset-specific-terms JSON document and returns its normalized form, or
    /// <see langword="null"/> when the text cannot be parsed as JSON.
    /// </summary>
    public SecurityAssetSpecificTerms? Upcast(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Normalize(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the schema version stamped on the payload, or
    /// <see cref="SecurityMasterSchemaVersions.DefaultAssetSpecificTerms"/> when the payload predates
    /// explicit versioning. This is the single place "what version is this payload?" is decided.
    /// </summary>
    public static int ResolveSchemaVersion(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("schemaVersion", out var version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var value))
        {
            return value;
        }

        return SecurityMasterSchemaVersions.DefaultAssetSpecificTerms;
    }

    /// <summary>
    /// Stamps a missing <c>schemaVersion</c> onto an object payload so downstream code never has to
    /// re-derive the legacy default. Payloads that already carry a numeric <c>schemaVersion</c>, and
    /// non-object payloads, are returned unchanged. The returned <see cref="JsonElement"/> is cloned
    /// so it is safe to use after the source document is disposed.
    /// </summary>
    public static SecurityAssetSpecificTerms Normalize(JsonElement payload)
    {
        var version = ResolveSchemaVersion(payload);
        var alreadyStamped = payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("schemaVersion", out var existing)
            && existing.ValueKind == JsonValueKind.Number;

        if (payload.ValueKind != JsonValueKind.Object || alreadyStamped)
        {
            return new SecurityAssetSpecificTerms(payload.Clone(), version);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", version);
            foreach (var property in payload.EnumerateObject())
            {
                if (property.NameEquals("schemaVersion"))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var stamped = JsonDocument.Parse(buffer.ToArray());
        return new SecurityAssetSpecificTerms(stamped.RootElement.Clone(), version);
    }
}
