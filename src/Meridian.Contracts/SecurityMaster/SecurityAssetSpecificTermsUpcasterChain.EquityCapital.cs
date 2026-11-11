using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Contracts.SecurityMaster;

public sealed partial class SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster
{
    /// <summary>Whether the document carries the complete typed equity codec image.</summary>
    public static bool CanRecoverEquityCapital(JsonElement economic, string? assetClass)
        => TryRecoverEquityCapital(economic, assetClass, out _);

    // An absent/partial module keeps the existing historical refusal. A complete typed module
    // attests the discriminant and all preferred/convertible members, including explicit nulls.
    private static bool TryRecoverEquityCapital(JsonElement economic, string? assetClass, out JsonElement flat)
    {
        flat = default;
        if (!SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsEquityClassification
            || GetObject(economic, "equityCapital") is not { } capital
            || !HasExactMembers(capital, "shareClass", "votingRightsCat", "classification", "otherClassification", "preferredTerms", "convertibleTerms")
            || !NullableText(capital, "shareClass")
            || !NullableText(capital, "votingRightsCat")
            || !NullableText(capital, "classification")
            || !NullableText(capital, "otherClassification"))
            return false;

        var classification = capital.GetProperty("classification");
        var label = classification.ValueKind == JsonValueKind.Null ? null : classification.GetString();
        if (label != "Other" && capital.GetProperty("otherClassification").ValueKind != JsonValueKind.Null)
            return false;
        var preferred = capital.GetProperty("preferredTerms");
        var convertible = capital.GetProperty("convertibleTerms");
        var complete = label switch
        {
            null or "Common" => preferred.ValueKind == JsonValueKind.Null && convertible.ValueKind == JsonValueKind.Null,
            "Preferred" => CompletePreferred(preferred) && convertible.ValueKind == JsonValueKind.Null,
            "Convertible" => preferred.ValueKind == JsonValueKind.Null && CompleteConvertible(convertible),
            "ConvertiblePreferred" => CompletePreferred(preferred) && CompleteConvertible(convertible),
            "Other" => preferred.ValueKind == JsonValueKind.Null && convertible.ValueKind == JsonValueKind.Null
                && capital.GetProperty("otherClassification").ValueKind == JsonValueKind.String,
            _ => false
        };
        if (!complete)
            return false;

        // Preserve any additional imported economic modules until the normal identity gate proves
        // that the complete flat record reproduces them. Full equity recovery never drops evidence.
        var recovered = JsonNode.Parse(capital.GetRawText())!.AsObject();
        recovered[SecurityTermsEnvelope.RetainedEconomicTermsKey] = JsonNode.Parse(economic.GetRawText());
        using var document = JsonDocument.Parse(SecurityTermsEnvelope.StampFlat(recovered.ToJsonString(), assetClass!));
        flat = document.RootElement.Clone();
        return true;
    }

    private static bool CompletePreferred(JsonElement value)
    {
        if (!HasExactMembers(value, "dividendRate", "dividendType", "redemptionPrice", "redemptionDate", "callableDate", "participationTerms", "liquidationPreference")
            || !NullableDecimal(value, "dividendRate") || !NullableDecimal(value, "redemptionPrice")
            || !NullableDate(value, "redemptionDate") || !NullableDate(value, "callableDate")
            || value.GetProperty("dividendType").ValueKind != JsonValueKind.String
            || value.GetProperty("dividendType").GetString() is not ("Fixed" or "Floating" or "Cumulative"))
            return false;
        var participation = value.GetProperty("participationTerms");
        if (participation.ValueKind != JsonValueKind.Null
            && (!HasExactMembers(participation, "participatesInCommonDividends", "additionalDividendThreshold")
                || participation.GetProperty("participatesInCommonDividends").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !NullableDecimal(participation, "additionalDividendThreshold")))
            return false;
        var liquidation = value.GetProperty("liquidationPreference");
        if (!HasExactMembers(liquidation, "kind", "multiple")
            || liquidation.GetProperty("kind").ValueKind != JsonValueKind.String
            || !NullableDecimal(liquidation, "multiple"))
            return false;
        return liquidation.GetProperty("kind").GetString() switch
        {
            "Pari" or "Subordinated" => liquidation.GetProperty("multiple").ValueKind == JsonValueKind.Null,
            "Senior" => liquidation.GetProperty("multiple").ValueKind == JsonValueKind.Number,
            _ => false
        };
    }

    private static bool CompleteConvertible(JsonElement value)
        => HasExactMembers(value, "underlyingSecurityId", "conversionRatio", "conversionPrice", "conversionStartDate", "conversionEndDate")
            && value.GetProperty("underlyingSecurityId").ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetProperty("underlyingSecurityId").GetString(), out _)
            && value.GetProperty("conversionRatio").ValueKind == JsonValueKind.Number
            && value.GetProperty("conversionRatio").TryGetDecimal(out _)
            && NullableDecimal(value, "conversionPrice")
            && NullableDate(value, "conversionStartDate") && NullableDate(value, "conversionEndDate");

    private static bool HasExactMembers(JsonElement value, params string[] names)
        => value.ValueKind == JsonValueKind.Object
            && value.EnumerateObject().Count() == names.Length
            && names.All(name => value.TryGetProperty(name, out _));

    private static bool NullableText(JsonElement value, string name)
        => value.GetProperty(name).ValueKind is JsonValueKind.Null or JsonValueKind.String;

    private static bool NullableDecimal(JsonElement value, string name)
        => value.GetProperty(name).ValueKind == JsonValueKind.Null
            || value.GetProperty(name).ValueKind == JsonValueKind.Number && value.GetProperty(name).TryGetDecimal(out _);

    private static bool NullableDate(JsonElement value, string name)
        => value.GetProperty(name).ValueKind == JsonValueKind.Null
            || value.GetProperty(name).ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(value.GetProperty(name).GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
