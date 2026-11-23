using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaCorporateActionProvider
{
    /// <summary>
    /// Maps the remaining REST families as review-only source terms. These records deliberately
    /// do not infer mastered successor IDs, cash-price units, lot allocations, or accounting.
    /// </summary>
    private CorporateActionCommand? MapAdditionalToCommand(
        JsonElement payload, Guid securityId, string ticker, string providerType, string evidenceHash)
    {
        var sourceField = AdditionalSourceField(providerType);
        if (sourceField is null)
            return null;

        var nativeId = AdditionalRequiredText(payload, "id");
        var sourceSymbol = AdditionalRequiredText(payload, sourceField);
        if (!string.Equals(sourceSymbol, ticker.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        var dateBasis = providerType switch
        {
            "unit_split" or "reorganization" => "effective_date",
            "rights_distribution" or "capital_gains_distribution" => "ex_date",
            _ => "process_date",
        };
        var eventDate = AdditionalRequiredDate(payload, dateBasis);
        var recordDate = AdditionalOptionalDate(payload, "record_date");
        var payableDate = AdditionalOptionalDate(payload, "payable_date");
        var currency = AdditionalText(payload, "currency")?.ToUpperInvariant();
        if (currency is not null && (currency.Length != 3 || currency.Any(static c => c is < 'A' or > 'Z')))
            throw new JsonException("Invalid corporate-action currency.");

        var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        var dates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        var legs = new List<AdditionalStockLeg>();
        string? legsField = null;
        decimal? distributionRatio = null;
        string actionType;
        string description;

        switch (providerType)
        {
            case "unit_split":
            {
                actionType = CorporateActionEventTypes.UnitSplit;
                description = "Unit split";
                var sourceRate = AdditionalPositiveNumber(payload, "old_rate");
                numbers["sourceRate"] = sourceRate;
                legs.Add(AdditionalLeg(payload, "new_", sourceRate, "new_rate"));
                legs.Add(AdditionalLeg(payload, "alternate_", sourceRate, "alternate_rate"));
                legsField = "successorLegs";
                break;
            }
            case "redemption":
                actionType = CorporateActionEventTypes.SecurityRedemption;
                description = "Security redemption";
                numbers["redemptionRate"] = AdditionalNonnegativeNumber(payload, "rate", required: true)!.Value;
                // The schema calls this 'rate' without declaring a percent-of-par or per-unit basis.
                strings["rateBasis"] = "provider_rate";
                break;
            case "name_change":
                actionType = CorporateActionEventTypes.NameChange;
                description = "Name or symbol change";
                AddSuccessor("new_");
                break;
            case "worthless_removal":
                actionType = CorporateActionEventTypes.WorthlessRemoval;
                description = "Worthless security removal";
                break;
            case "rights_distribution":
                actionType = CorporateActionEventTypes.RightsDistribution;
                description = "Rights distribution";
                distributionRatio = AdditionalPositiveNumber(payload, "rate");
                numbers[CorporateActionPayloads.RightsPerHeldUnit] = distributionRatio.Value;
                AddSuccessor("new_");
                payableDate = AdditionalRequiredDate(payload, "payable_date");
                AddDate("expiration_date", "expirationDate");
                break;
            case "partial_call":
                actionType = CorporateActionEventTypes.PartialCall;
                description = "Partial call";
                AddNumber("price", "price");
                AddNumber("dividend_rate", "dividendRate");
                var lotteryType = AdditionalText(payload, "lottery_type");
                if (lotteryType is not null)
                {
                    if (lotteryType is not ("original" or "supplemental"))
                        throw new JsonException("Invalid corporate-action lottery type.");
                    strings["lotteryType"] = lotteryType;
                }
                AddDate("lottery_date", "lotteryDate");
                AddDate("results_publication_date", "resultsPublicationDate");
                break;
            case "reorganization":
                actionType = CorporateActionEventTypes.Reorganization;
                description = "Reorganization";
                AddNumber("cash_rate", "cashRate");
                if (payload.TryGetProperty("stock_movements", out var movements))
                {
                    if (movements.ValueKind != JsonValueKind.Array)
                        throw new JsonException("Invalid corporate-action stock movements.");
                    foreach (var movement in movements.EnumerateArray())
                    {
                        if (movement.ValueKind != JsonValueKind.Object)
                            throw new JsonException("Invalid corporate-action stock movement.");
                        legs.Add(AdditionalLeg(movement, string.Empty,
                            AdditionalPositiveNumber(movement, "source_rate"), "new_rate"));
                    }
                    legsField = "stockMovements";
                }
                break;
            case "capital_gains_distribution":
                actionType = CorporateActionEventTypes.CapitalGainsDistribution;
                description = "Capital gains distribution";
                AddNumber("long_term_rate", "longTermRate");
                AddNumber("short_term_rate", "shortTermRate");
                if (numbers.Count == 0)
                    throw new JsonException("Capital gains distribution requires a rate component.");
                break;
            default:
                return null;
        }

        var sourcePrefix = sourceField == "symbol" ? string.Empty : sourceField[..^"symbol".Length];
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("providerEventType", providerType);
            writer.WriteString("dateBasis", dateBasis);
            if (dateBasis == "process_date")
                writer.WriteBoolean("eventDateUnspecified", true);
            WriteIdentifier(writer, "sourceSymbol", sourceSymbol);
            WriteIdentifier(writer, "sourceCusip", AdditionalText(payload, sourcePrefix + "cusip"));
            WriteIdentifier(writer, "sourceIsin", AdditionalText(payload, sourcePrefix + "isin"));
            foreach (var (key, value) in numbers)
                writer.WriteNumber(key, value);
            foreach (var (key, value) in strings)
                writer.WriteString(key, value);
            foreach (var (key, value) in dates)
                writer.WriteString(key, value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (legsField is not null)
            {
                writer.WriteStartArray(legsField);
                foreach (var leg in legs)
                {
                    writer.WriteStartObject();
                    WriteIdentifier(writer, "symbol", leg.Symbol);
                    WriteIdentifier(writer, "cusip", leg.Cusip);
                    WriteIdentifier(writer, "isin", leg.Isin);
                    writer.WriteNumber("sourceRate", leg.SourceRate);
                    writer.WriteNumber("newRate", leg.NewRate);
                    writer.WriteNumber("distributionRatio", leg.NewRate / leg.SourceRate);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        using var economicDocument = JsonDocument.Parse(buffer.WrittenMemory);
        var raw = payload.GetRawText();
        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: actionType,
            ExDate: eventDate,
            RecordDate: recordDate,
            PayableDate: payableDate,
            // Separate rate components remain in the typed payload; no cash event is collapsed
            // into the legacy dividend Amount column or assigned an undeclared monetary basis.
            Amount: null,
            Currency: currency,
            SplitFromFactor: null,
            SplitToFactor: null,
            Description: description,
            SourceProvider: ProviderId,
            SourceEventId: nativeId,
            SourceEventVersion: SourceVersion(evidenceHash, providerType),
            ObservedAtUtc: DateTimeOffset.UtcNow,
            EvidenceHash: evidenceHash,
            EvidenceReference: $"alpaca://corporate-actions/v1/{providerType}/{Uri.EscapeDataString(nativeId)}/versions/{MappingVersion}/{evidenceHash}",
            ReleaseStatus: ReleaseStatus)
        {
            Payload = economicDocument.RootElement.Clone(),
            DistributionRatio = distributionRatio,
            PreserveUnknownCurrency = true,
            SourceEvidence = new CorporateActionRetainedSourceEvidenceDto(
                providerType, "alpaca-market-data-corporate-actions-v1/meridian-v2", raw, Sha256Digest.ComputeUtf8(raw)),
        };

        void AddSuccessor(string prefix)
        {
            strings["successorSymbol"] = AdditionalRequiredText(payload, prefix + "symbol").ToUpperInvariant();
            if (AdditionalText(payload, prefix + "cusip") is { } cusip)
                strings["successorCusip"] = cusip.ToUpperInvariant();
            if (AdditionalText(payload, prefix + "isin") is { } isin)
                strings["successorIsin"] = isin.ToUpperInvariant();
        }
        void AddNumber(string field, string key)
        {
            if (AdditionalNonnegativeNumber(payload, field) is { } number)
                numbers[key] = number;
        }
        void AddDate(string field, string key)
        {
            if (AdditionalOptionalDate(payload, field) is { } date)
                dates[key] = date;
        }
    }

    private static bool IsAdditionalRelatedSymbolOnly(JsonElement payload, string ticker, string providerType)
    {
        var sourceField = AdditionalSourceField(providerType);
        if (sourceField is null || AdditionalText(payload, sourceField) is not { } source
            || string.Equals(source, ticker.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        bool Matches(string field) => string.Equals(AdditionalText(payload, field), ticker.Trim(), StringComparison.OrdinalIgnoreCase);
        if (providerType is "unit_split" or "name_change" or "rights_distribution")
            return Matches("new_symbol") || (providerType == "unit_split" && Matches("alternate_symbol"));
        if (providerType == "reorganization" && payload.TryGetProperty("stock_movements", out var movements))
        {
            if (movements.ValueKind != JsonValueKind.Array)
                throw new JsonException("Invalid corporate-action stock movements.");
            return movements.EnumerateArray().Any(movement =>
                string.Equals(AdditionalText(movement, "symbol"), ticker.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    private static string? AdditionalSourceField(string type) => type switch
    {
        "unit_split" or "name_change" => "old_symbol",
        "rights_distribution" => "source_symbol",
        "redemption" or "worthless_removal" or "partial_call" or "reorganization"
            or "capital_gains_distribution" => "symbol",
        _ => null,
    };

    private static string? AdditionalText(JsonElement payload, string field)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("Invalid corporate-action record.");
        if (!payload.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException("Invalid corporate-action text field.");
        return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim();
    }

    private static string AdditionalRequiredText(JsonElement payload, string field) =>
        AdditionalText(payload, field) ?? throw new JsonException("Missing corporate-action text field.");

    private static DateOnly? AdditionalOptionalDate(JsonElement payload, string field) =>
        AdditionalText(payload, field) is { } value
            ? TryDate(value, out var date) ? date : throw new JsonException("Invalid corporate-action date.")
            : null;

    private static DateOnly AdditionalRequiredDate(JsonElement payload, string field) =>
        AdditionalOptionalDate(payload, field) ?? throw new JsonException("Missing corporate-action date.");

    private static decimal? AdditionalNonnegativeNumber(JsonElement payload, string field, bool required = false)
    {
        if (!payload.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return required ? throw new JsonException("Missing corporate-action rate.") : null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number) || number < 0m)
            throw new JsonException("Invalid corporate-action rate.");
        return number;
    }

    private static decimal AdditionalPositiveNumber(JsonElement payload, string field)
    {
        var number = AdditionalNonnegativeNumber(payload, field, required: true)!.Value;
        return number > 0m ? number : throw new JsonException("Corporate-action ratio must be positive.");
    }

    private static AdditionalStockLeg AdditionalLeg(JsonElement payload, string prefix, decimal sourceRate, string rateField)
        => new(AdditionalRequiredText(payload, prefix + "symbol"), AdditionalText(payload, prefix + "cusip"),
            AdditionalText(payload, prefix + "isin"), sourceRate, AdditionalPositiveNumber(payload, rateField));

    private static void WriteIdentifier(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value.ToUpperInvariant());
    }

    private sealed record AdditionalStockLeg(string Symbol, string? Cusip, string? Isin, decimal SourceRate, decimal NewRate);
}
