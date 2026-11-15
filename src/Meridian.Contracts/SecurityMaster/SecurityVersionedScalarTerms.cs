using System.Text;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compatibility boundaries for optional scalar terms introduced after a class's original codec.</summary>
public static class SecurityVersionedScalarTerms
{
    /// <summary>A retained scalar cannot activate above its document stamp; writes use the node's write ceiling.</summary>
    public static bool CanDecode(string assetClass, JsonElement terms, string key, bool writeMode)
    {
        var field = SecurityAssetTermsSchema.Field(assetClass, key);
        if (field is null || (writeMode && !field.AuthoringEnabled))
            return false;
        var decoding = SecurityVersionedTerms.Decoding(assetClass, terms, writeMode);
        return !decoding.ProfileGoverned && decoding.Through >= field.SinceTermsVersion;
    }

    /// <summary>
    /// Removes only absent optional additions from generated output. Original members retain their
    /// ordering and JSON values, so reading an older record does not acquire new null fields.
    /// </summary>
    public static string OmitAbsent(string json, string[] introducedKeys)
    {
        using var document = JsonDocument.Parse(json);
        var keys = introducedKeys.ToHashSet(StringComparer.Ordinal);
        if (!document.RootElement.EnumerateObject().Any(property => keys.Contains(property.Name)
                && property.Value.ValueKind == JsonValueKind.Null))
            return json;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (keys.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.Null)
                    continue;
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
