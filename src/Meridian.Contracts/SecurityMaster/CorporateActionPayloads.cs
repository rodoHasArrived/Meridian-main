using System.Globalization;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Well-known keys and typed readers for the generic corporate-action payload envelope
/// (<see cref="CorporateActionDto.Payload"/>). The wide <c>corporate_actions</c> table declared
/// eight typed columns for eighteen event types, so six declared types (and every future one) had
/// no columns for their own economics; the JSONB payload is the envelope that generalizes the
/// shape. Keys here are conventions, not a closed schema: an event type may carry additional keys,
/// and readers must tolerate absence — the payload is evidence-bearing detail, and the lifecycle
/// columns (event type, dates, supersede chain) remain the authoritative spine.
/// </summary>
public static class CorporateActionPayloads
{
    /// <summary>TenderOffer: offered price per share/unit in the event's currency.</summary>
    public const string OfferPricePerShare = "offerPricePerShare";

    /// <summary>TenderOffer: date the offer expires.</summary>
    public const string OfferExpiryDate = "offerExpiryDate";

    /// <summary>TenderOffer: true for a partial (pro-rated) tender.</summary>
    public const string IsPartialTender = "isPartialTender";

    /// <summary>ReturnOfCapital: capital returned per share/unit (reduces cost basis, not income).</summary>
    public const string CapitalReturnPerShare = "capitalReturnPerShare";

    /// <summary>PrincipalPaydown: principal amount repaid per unit of original face.</summary>
    public const string PrincipalAmount = "principalAmount";

    /// <summary>PrincipalPaydown: the pool factor effective after the paydown.</summary>
    public const string PostPaydownFactor = "postPaydownFactor";

    /// <summary>CryptoFork: symbol of the new asset minted by the fork.</summary>
    public const string ForkNewAssetSymbol = "forkNewAssetSymbol";

    /// <summary>CryptoFork: units of the new asset received per unit held.</summary>
    public const string ForkRatio = "forkRatio";

    /// <summary>OptionContractAdjustment: contract multiplier after the adjustment.</summary>
    public const string AdjustedMultiplier = "adjustedMultiplier";

    /// <summary>OptionContractAdjustment: strike price after the adjustment.</summary>
    public const string AdjustedStrike = "adjustedStrike";

    /// <summary>OptionContractAdjustment: human-readable deliverable description after adjustment.</summary>
    public const string AdjustedDeliverable = "adjustedDeliverable";

    /// <summary>Delisting: the final trading date on the delisting venue.</summary>
    public const string FinalTradingDate = "finalTradingDate";

    /// <summary>Delisting: reason category (e.g. "Acquired", "Bankruptcy", "Exchange").</summary>
    public const string DelistingReason = "delistingReason";

    /// <summary>Reads a decimal payload value; null when the payload or key is absent or mistyped.</summary>
    public static decimal? ReadDecimal(JsonElement? payload, string key)
    {
        if (TryGetValue(payload, key, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>Reads a date payload value; null when the payload or key is absent or unparseable.</summary>
    public static DateOnly? ReadDate(JsonElement? payload, string key)
        => TryGetValue(payload, key, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>Reads a string payload value; null when the payload or key is absent or blank.</summary>
    public static string? ReadString(JsonElement? payload, string key)
        => TryGetValue(payload, key, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>Reads a boolean payload value; null when the payload or key is absent or mistyped.</summary>
    public static bool? ReadBoolean(JsonElement? payload, string key)
        => TryGetValue(payload, key, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null,
            }
            : null;

    private static bool TryGetValue(JsonElement? payload, string key, out JsonElement value)
    {
        value = default;
        return payload is { ValueKind: JsonValueKind.Object } envelope
            && envelope.TryGetProperty(key, out value);
    }
}
