using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Core;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityMasterMapping
{
    private static FSharpOption<VotingRightsCat> ToVotingRightsCatOption(string? raw)
        => raw switch
        {
            "FullVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.FullVoting),
            "LimitedVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.LimitedVoting),
            "NonVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.NonVoting),
            "DualClass" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.DualClass),
            "SuperVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.SuperVoting),
            not null => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.NewOtherVotingRights(raw)),
            null => FSharpOption<VotingRightsCat>.None
        };

    private static DividendType ToDividendType(string raw)
        => raw switch
        {
            "Fixed" => DividendType.Fixed,
            "Floating" => DividendType.Floating,
            "Cumulative" => DividendType.Cumulative,
            _ => throw new InvalidOperationException($"Unsupported dividend type '{raw}'.")
        };

    private static FSharpOption<ParticipationTerms> ToParticipationTermsOption(SchemaBoundTerms? terms)
        => terms is { } participation
            ? FSharpOption<ParticipationTerms>.Some(new ParticipationTerms(
                participation.OptionalBoolean("participatesInCommonDividends") ?? false,
                ToOption(participation.OptionalDecimal("additionalDividendThreshold"))))
            : FSharpOption<ParticipationTerms>.None;

    private static LiquidationPreference ToLiquidationPreference(SchemaBoundTerms terms)
        => terms.RequiredString("kind") switch
        {
            "Pari" => LiquidationPreference.Pari,
            "Senior" => LiquidationPreference.NewSenior(terms.RequiredDecimal("multiple")),
            "Subordinated" => LiquidationPreference.Subordinated,
            var raw => throw new InvalidOperationException($"Unsupported liquidation preference '{raw}'.")
        };

    // The schema declares preferredTerms as an object but registers no inner shape for it, so its
    // members (and the two blocks nested in it) read through the ledgered view
    // (UnboundCompatibilityReads) until the shape is declared.
    private static PreferredTerms ToPreferredTerms(SchemaBoundTerms preferredTerms)
    {
        var terms = preferredTerms.Unbound;
        return new(
            ToOption(terms.OptionalDecimal("dividendRate")),
            ToDividendType(terms.RequiredString("dividendType")),
            ToOption(terms.OptionalDecimal("redemptionPrice")),
            ToOption(terms.OptionalDateOnly("redemptionDate")),
            ToOption(terms.OptionalDateOnly("callableDate")),
            ToParticipationTermsOption(terms.OptionalObject("participationTerms")),
            ToLiquidationPreference(terms.RequiredObject("liquidationPreference")));
    }

    // Bound to the inner shape the schema registers as Equity.convertibleTerms.
    private static ConvertibleTerms ToConvertibleTerms(SchemaBoundTerms terms)
        => new(
            SecurityId.NewSecurityId(terms.RequiredGuid("underlyingSecurityId")),
            terms.RequiredDecimal("conversionRatio"),
            ToOption(terms.OptionalDecimal("conversionPrice")),
            ToOption(terms.OptionalDateOnly("conversionStartDate")),
            ToOption(terms.OptionalDateOnly("conversionEndDate")));

    private static FSharpOption<EquityClassification> ToEquityClassificationOption(
        SchemaBoundTerms terms, SecurityKindMappingMode mode)
    {
        var raw = terms.OptionalString("classification");
        return raw switch
        {
            "Common" => FSharpOption<EquityClassification>.Some(EquityClassification.Common),
            "Preferred" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewPreferred(ToPreferredTerms(terms.RequiredObject("preferredTerms")))),
            "Convertible" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewConvertible(ToConvertibleTerms(terms.RequiredObject("convertibleTerms")))),
            "ConvertiblePreferred" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewConvertiblePreferred(
                    ToPreferredTerms(terms.RequiredObject("preferredTerms")),
                    ToConvertibleTerms(terms.RequiredObject("convertibleTerms")))),
            // A write selecting "Other" must NAME the classification: defaulting a missing
            // otherClassification to the placeholder "Other" would persist an economically
            // meaningless label as the security's classification.
            "Other" when mode == SecurityKindMappingMode.Write =>
                terms.OptionalString("otherClassification") is { } named && !string.IsNullOrWhiteSpace(named)
                    ? FSharpOption<EquityClassification>.Some(EquityClassification.NewOther(named))
                    : throw new InvalidOperationException(
                        "An equity classification of 'Other' requires a non-empty 'otherClassification' naming the " +
                        "classification. Supply it, or use one of the declared classifications " +
                        "(Common, Preferred, Convertible, ConvertiblePreferred)."),
            "Other" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewOther(terms.OptionalString("otherClassification") ?? "Other")),
            null => FSharpOption<EquityClassification>.None,
            // A WRITE fails closed on an unrecognized discriminant: silently persisting a typo
            // ("Commmon") as Other(raw) would change the security's economic classification.
            _ when mode == SecurityKindMappingMode.Write =>
                throw new InvalidOperationException(
                    $"Unknown equity classification '{raw}'. Declared classifications are Common, Preferred, " +
                    "Convertible, and ConvertiblePreferred; use 'Other' with 'otherClassification' for anything else."),
            // Read tolerance: rows written before the serializer emitted the "Other" discriminant
            // carry the raw label in the classification slot. Treat any unrecognized value as an
            // Other classification instead of failing every read of the row.
            _ => FSharpOption<EquityClassification>.Some(EquityClassification.NewOther(raw))
        };
    }

    public static JsonElement BuildPreferredEquityTermsPatch(SecurityProjectionRecord current, AmendPreferredEquityTermsRequest request)
    {
        if (!SecurityAssetClassCatalog.GetOrDefault(current.AssetClass).SupportsEquityClassification)
            throw new InvalidOperationException($"Security '{current.SecurityId}' is not an equity and cannot accept preferred term amendments.");

        var assetSpecificNode = JsonNode.Parse(current.AssetSpecificTerms.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("Current asset-specific terms payload is not a JSON object.");
        var classification = assetSpecificNode["classification"]?.GetValue<string>();

        if (classification is not ("Preferred" or "ConvertiblePreferred"))
            throw new InvalidOperationException($"Security '{current.SecurityId}' does not currently have preferred-equity terms.");

        var existingJson = (assetSpecificNode["preferredTerms"] as JsonObject)?.ToJsonString();
        var preferredTermsNode = (existingJson is not null
            ? JsonNode.Parse(existingJson) as JsonObject
            : null) ?? new JsonObject();

        if (request.DividendRate is not null)
            preferredTermsNode["dividendRate"] = JsonValue.Create(request.DividendRate);
        if (request.DividendType is not null)
            preferredTermsNode["dividendType"] = request.DividendType;
        if (request.RedemptionPrice is not null)
            preferredTermsNode["redemptionPrice"] = JsonValue.Create(request.RedemptionPrice);
        if (request.RedemptionDate is not null)
            preferredTermsNode["redemptionDate"] = request.RedemptionDate.Value.ToString("yyyy-MM-dd");
        if (request.CallableDate is not null)
            preferredTermsNode["callableDate"] = request.CallableDate.Value.ToString("yyyy-MM-dd");

        if (request.ParticipatesInCommonDividends is not null || request.AdditionalDividendThreshold is not null)
        {
            var existingPart = (preferredTermsNode["participationTerms"] as JsonObject)?.ToJsonString();
            var participationNode = (existingPart is not null
                ? JsonNode.Parse(existingPart) as JsonObject
                : null) ?? new JsonObject();
            if (request.ParticipatesInCommonDividends is not null)
                participationNode["participatesInCommonDividends"] = JsonValue.Create(request.ParticipatesInCommonDividends.Value);
            if (request.AdditionalDividendThreshold is not null)
                participationNode["additionalDividendThreshold"] = JsonValue.Create(request.AdditionalDividendThreshold);
            preferredTermsNode["participationTerms"] = participationNode;
        }

        if (request.LiquidationPreferenceKind is not null)
        {
            var existingLiq = (preferredTermsNode["liquidationPreference"] as JsonObject)?.ToJsonString();
            var liquidationNode = (existingLiq is not null
                ? JsonNode.Parse(existingLiq) as JsonObject
                : null) ?? new JsonObject();
            liquidationNode["kind"] = request.LiquidationPreferenceKind;
            if (request.LiquidationPreferenceMultiple is not null)
                liquidationNode["multiple"] = JsonValue.Create(request.LiquidationPreferenceMultiple);
            preferredTermsNode["liquidationPreference"] = liquidationNode;
        }

        assetSpecificNode["preferredTerms"] = preferredTermsNode;
        return JsonSerializer.SerializeToElement(assetSpecificNode);
    }


    public static JsonElement BuildConvertibleEquityTermsPatch(SecurityProjectionRecord current, AmendConvertibleEquityTermsRequest request)
    {
        if (!SecurityAssetClassCatalog.GetOrDefault(current.AssetClass).SupportsEquityClassification)
            throw new InvalidOperationException($"Security '{current.SecurityId}' is not an equity and cannot accept convertible term amendments.");

        var assetSpecificNode = JsonNode.Parse(current.AssetSpecificTerms.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("Current asset-specific terms payload is not a JSON object.");
        var classification = assetSpecificNode["classification"]?.GetValue<string>();

        if (classification is not ("Convertible" or "ConvertiblePreferred"))
            throw new InvalidOperationException($"Security '{current.SecurityId}' does not currently have convertible-equity terms.");

        var convertibleTermsNode = new JsonObject
        {
            ["underlyingSecurityId"] = request.UnderlyingSecurityId,
            ["conversionRatio"] = JsonValue.Create(request.ConversionRatio),
            ["conversionPrice"] = JsonValue.Create(request.ConversionPrice),
            ["conversionStartDate"] = JsonValue.Create(request.ConversionStartDate),
            ["conversionEndDate"] = JsonValue.Create(request.ConversionEndDate)
        };

        assetSpecificNode["convertibleTerms"] = convertibleTermsNode;
        return JsonSerializer.SerializeToElement(assetSpecificNode);
    }
}
