using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The optional, governed scalar rate-unit declaration for classes without schedule anchors.
/// Historical payloads do not acquire a declaration from stray keys or an upgrade. Setting the
/// field is an explicit amendment; absence never creates a field default or infers one from value.
/// </summary>
public static class SecurityRecordRateUnit
{
    public const string FieldKey = "rateUnit";

    /// <summary>The record's explicit unit, or null when the record declares none.</summary>
    public static RateUnit? Read(string assetClass, JsonElement terms)
    {
        var field = SecurityAssetTermsSchema.Field(assetClass, FieldKey);
        if (field is not null && terms.ValueKind == JsonValueKind.Object
            && SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms) > SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass))
            return RateUnit.Unrecognized;
        if (field is null || terms.ValueKind != JsonValueKind.Object
            || SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, terms)
            || SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms) < field.SinceTermsVersion
            || !terms.TryGetProperty(FieldKey, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String
            ? Parse(value.GetString()) : RateUnit.Unrecognized;
    }

    /// <summary>Reads the canonical economic module when no retained flat document is available.</summary>
    public static RateUnit? ReadEconomic(JsonElement economics)
    {
        if (economics.ValueKind != JsonValueKind.Object
            || !economics.TryGetProperty("rateConvention", out var module) || module.ValueKind == JsonValueKind.Null)
            return null;
        if (module.ValueKind != JsonValueKind.Object || !module.TryGetProperty(FieldKey, out var value))
            return RateUnit.Unrecognized;
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? Parse(value.GetString()) : RateUnit.Unrecognized;
    }

    public static RateUnit? Parse(string? token) => token switch
    {
        "Fraction" => RateUnit.Fraction,
        "Percent" => RateUnit.Percent,
        "BasisPoints" => RateUnit.BasisPoints,
        null => null,
        _ => RateUnit.Unrecognized
    };

    /// <summary>Reads verbatim for codec replay, gated by the stored terms version.</summary>
    public static string? ReadToken(string assetClass, JsonElement terms, bool writeMode = false)
    {
        var field = SecurityAssetTermsSchema.Field(assetClass, FieldKey);
        if (field is null || terms.ValueKind != JsonValueKind.Object
            || SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, terms))
            return null;
        // Explicit write submissions declare supported fields at the current ceiling. The service
        // requires governance before an amendment can activate a previously inert declaration.
        var through = writeMode ? SecurityAssetClassCatalog.GetTermsVersion(assetClass)
            : SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms);
        if (through < field.SinceTermsVersion || through > SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass))
            return null;
        return terms.TryGetProperty(FieldKey, out var unit) && unit.ValueKind == JsonValueKind.String
            ? unit.GetString() : null;
    }
}
