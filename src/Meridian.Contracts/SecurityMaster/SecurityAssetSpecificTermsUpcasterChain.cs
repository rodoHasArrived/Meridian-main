using System.IO;
using System.Text.Json;
using Meridian.Contracts.Schema;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Converts a structured economic-terms payload (<see cref="EconomicTermsSchema.Current"/>, the
/// nested maturity/coupon/discount/accrual/payment shape) into the flat legacy asset-specific-terms
/// shape (<see cref="AssetSpecificTermsSchema.Legacy"/>) that the mapping, resolver, and projection
/// paths read. This is the bridge that closes the cross-family trap where an economic-terms
/// document landing in the asset-specific-terms slot failed every read with
/// "Unsupported schemaVersion '2'": routed through this upcaster, the same document reads as a
/// valid v1 payload with its economics preserved. The authoritative economic record remains stored
/// separately — this conversion only feeds read compatibility.
/// </summary>
public sealed class SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster : ISchemaUpcaster<SecurityAssetSpecificTerms>
{
    /// <summary>Shared stateless instance for callers that resolve the upcaster outside DI.</summary>
    public static SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster Instance { get; } = new();

    public int FromSchemaVersion => EconomicTermsSchema.Current;

    public int ToSchemaVersion => AssetSpecificTermsSchema.Legacy;

    public SecurityAssetSpecificTerms? Upcast(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Convert(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Flattens a v2 economic-terms element into a v1 asset-specific-terms element. Only fields
    /// present in the source are written; the coupon block's payment frequency and day count win
    /// over the payment/accrual blocks' when both are present (they describe the same economics at
    /// different granularities).
    /// </summary>
    public static SecurityAssetSpecificTerms Convert(JsonElement economicTerms)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", AssetSpecificTermsSchema.Legacy);

            var maturity = GetObject(economicTerms, "maturity");
            WriteIfPresent(writer, "maturityDate", maturity, "maturityDate");
            WriteIfPresent(writer, "issueDate", maturity, "issueDate");
            WriteIfPresent(writer, "effectiveDate", maturity, "effectiveDate");

            var coupon = GetObject(economicTerms, "coupon");
            WriteIfPresent(writer, "couponType", coupon, "couponType");
            WriteIfPresent(writer, "couponRate", coupon, "couponRate");

            var payment = GetObject(economicTerms, "payment");
            if (!WriteIfPresent(writer, "paymentFrequency", coupon, "paymentFrequency"))
            {
                WriteIfPresent(writer, "paymentFrequency", payment, "paymentFrequency");
            }

            var accrual = GetObject(economicTerms, "accrual");
            if (!WriteIfPresent(writer, "dayCount", coupon, "dayCount"))
            {
                WriteIfPresent(writer, "dayCount", accrual, "dayCount");
            }

            WriteIfPresent(writer, "accrualStartDate", accrual, "accrualStartDate");

            var discount = GetObject(economicTerms, "discount");
            WriteIfPresent(writer, "discountRate", discount, "discountRate");
            WriteIfPresent(writer, "yieldRate", discount, "yieldRate");

            writer.WriteEndObject();
        }

        using var flattened = JsonDocument.Parse(buffer.ToArray());
        return new SecurityAssetSpecificTerms(flattened.RootElement.Clone(), AssetSpecificTermsSchema.Legacy);
    }

    private static JsonElement? GetObject(JsonElement source, string propertyName)
        => source.ValueKind == JsonValueKind.Object
           && source.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static bool WriteIfPresent(Utf8JsonWriter writer, string targetName, JsonElement? source, string sourceName)
    {
        if (source is not JsonElement element
            || !element.TryGetProperty(sourceName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        writer.WritePropertyName(targetName);
        value.WriteTo(writer);
        return true;
    }
}

/// <summary>
/// The composed migrate-on-read chain for asset-specific-terms payloads — the single entry point
/// that turns "whatever version is stored" into a payload current readers accept:
/// <list type="bullet">
/// <item><description>unstamped (version 0) → stamped <see cref="AssetSpecificTermsSchema.Legacy"/>
/// via <see cref="SecurityAssetSpecificTermsV0ToCurrentUpcaster"/>;</description></item>
/// <item><description><see cref="EconomicTermsSchema.Current"/> (a cross-family economic-terms
/// document) → flattened v1 via <see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster"/>;</description></item>
/// <item><description>accepted versions pass through unchanged; unknown future versions pass
/// through with their version preserved — acceptance stays the guard's decision, so newer payloads
/// degrade with a precise diagnostic instead of failing in the chain.</description></item>
/// </list>
/// </summary>
public static class SecurityAssetSpecificTermsUpcasterChain
{
    /// <summary>Normalizes a stored asset-specific-terms payload to the newest readable form.</summary>
    public static SecurityAssetSpecificTerms Normalize(JsonElement payload)
    {
        var version = SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(payload);
        return version == EconomicTermsSchema.Current
            ? SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(payload)
            : SecurityAssetSpecificTermsV0ToCurrentUpcaster.Normalize(payload);
    }

    /// <summary>Text-input variant; returns <see langword="null"/> when the text is not JSON.</summary>
    public static SecurityAssetSpecificTerms? Normalize(string json)
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
}
