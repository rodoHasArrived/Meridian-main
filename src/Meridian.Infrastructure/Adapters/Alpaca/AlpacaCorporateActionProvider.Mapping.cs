using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaCorporateActionProvider
{
    // A new mapping namespace appends an evidence-retaining revision to pre-v2 observations;
    // it never overwrites a previously staged proposal with different mapped terms/raw evidence.
    private const string MappingVersion = "alpaca-rest-v2";

    private CorporateActionCommand? MapToCommand(
        AlpacaAction action,
        JsonElement completeProviderPayload,
        Guid securityId,
        string ticker,
        string providerType,
        string evidenceHash)
    {
        if (providerType is "unit_split" or "redemption" or "name_change" or "worthless_removal"
            or "rights_distribution" or "partial_call" or "reorganization" or "capital_gains_distribution")
            return MapAdditionalToCommand(completeProviderPayload, securityId, ticker, providerType, evidenceHash);

        var isMerger = providerType is "cash_merger" or "stock_merger" or "stock_and_cash_merger";
        var sourceSymbol = providerType == "spin_off" ? action.SourceSymbol
            : isMerger ? action.AcquireeSymbol : action.Symbol;
        if (string.IsNullOrWhiteSpace(action.Id)
            || !string.Equals(sourceSymbol?.Trim(), ticker, StringComparison.OrdinalIgnoreCase))
            return null;

        var dateBasis = isMerger ? "effective_date" : "ex_date";
        if (!TryDate(isMerger ? action.EffectiveDate : action.ExDate, out var eventDate))
            return null;
        var recordDate = ParseOptionalDate(action.RecordDate);
        var payableDate = ParseOptionalDate(action.PayableDate);
        // Process date is operational evidence, not an ex-date or a legal effective date.
        _ = ParseOptionalDate(action.ProcessDate);

        var currency = string.IsNullOrWhiteSpace(action.Currency) ? null : action.Currency.Trim().ToUpperInvariant();
        if (currency is not null && (currency.Length != 3 || currency.Any(static c => c is < 'A' or > 'Z')))
            return null;

        string actionType;
        string description;
        decimal? amount = null;
        decimal? from = null;
        decimal? to = null;
        decimal? distributionRatio = null;
        decimal? exchangeRatio = null;
        decimal? cashPerUnit = null;
        string? successorSymbol = null;
        string? successorCusip = null;
        string? successorIsin = null;
        decimal? sourceRate = null;
        decimal? successorRate = null;

        switch (providerType)
        {
            case "cash_dividend":
                if (action.Rate is not > 0m)
                    return null;
                amount = action.Rate;
                actionType = action.SubType == "return_of_capital"
                    ? CorporateActionEventTypes.ReturnOfCapital : CorporateActionEventTypes.Dividend;
                description = action.SubType == "return_of_capital" ? "Return of capital"
                    : action.Special == true ? "Special cash dividend" : "Cash dividend";
                break;
            case "forward_split":
            case "reverse_split":
                if (action.OldRate is not > 0m || action.NewRate is not > 0m
                    || (providerType == "forward_split" ? action.NewRate <= action.OldRate : action.NewRate >= action.OldRate))
                    return null;
                from = action.OldRate;
                to = action.NewRate;
                actionType = providerType == "forward_split"
                    ? CorporateActionEventTypes.StockSplit : CorporateActionEventTypes.ReverseStockSplit;
                description = providerType == "forward_split" ? "Forward split" : "Reverse split";
                break;
            case "stock_dividend":
                if (action.Rate is not > 0m)
                    return null;
                actionType = CorporateActionEventTypes.StockDividend;
                description = "Stock dividend";
                // 0.05 means five additional shares per hundred held, never a 1:0.05 split.
                distributionRatio = action.Rate;
                break;
            case "spin_off":
                if (action.SourceRate is not > 0m || action.NewRate is not > 0m
                    || string.IsNullOrWhiteSpace(action.NewSymbol))
                    return null;
                actionType = CorporateActionEventTypes.SpinOff;
                description = "Spin-off";
                sourceRate = action.SourceRate;
                successorRate = action.NewRate;
                distributionRatio = action.NewRate / action.SourceRate;
                successorSymbol = action.NewSymbol;
                successorCusip = action.NewCusip;
                successorIsin = action.NewIsin;
                break;
            case "cash_merger":
                if (action.Rate is not > 0m)
                    return null;
                actionType = CorporateActionEventTypes.MergerAbsorption;
                description = "Cash merger";
                cashPerUnit = action.Rate;
                successorSymbol = action.AcquirerSymbol;
                successorCusip = action.AcquirerCusip;
                successorIsin = action.AcquirerIsin;
                break;
            case "stock_merger":
            case "stock_and_cash_merger":
                if (action.AcquireeRate is not > 0m || action.AcquirerRate is not > 0m
                    || string.IsNullOrWhiteSpace(action.AcquirerSymbol)
                    || (providerType == "stock_and_cash_merger" && action.CashRate is not >= 0m))
                    return null;
                actionType = CorporateActionEventTypes.MergerAbsorption;
                description = providerType == "stock_merger" ? "Stock merger" : "Stock and cash merger";
                sourceRate = action.AcquireeRate;
                successorRate = action.AcquirerRate;
                exchangeRatio = action.AcquirerRate / action.AcquireeRate;
                successorSymbol = action.AcquirerSymbol;
                successorCusip = action.AcquirerCusip;
                successorIsin = action.AcquirerIsin;
                // The schema does not specify how cash_rate scales with a non-unit acquiree_rate.
                // Retain it as source terms; only declare cash per held unit when the basis is one.
                cashPerUnit = providerType == "stock_and_cash_merger" && action.AcquireeRate == 1m
                    ? action.CashRate : null;
                break;
            default:
                return null;
        }

        var payload = (providerType is "cash_dividend" or "forward_split" or "reverse_split")
            && string.IsNullOrWhiteSpace(action.SubType)
                ? (JsonElement?)null
                : BuildEconomicPayload(action, providerType, dateBasis, sourceSymbol!, successorSymbol,
                    successorCusip, successorIsin, sourceRate, successorRate, cashPerUnit);
        var raw = completeProviderPayload.GetRawText();
        var nativeId = action.Id.Trim();
        return new CorporateActionCommand(
            SecurityId: securityId,
            ActionType: actionType,
            ExDate: eventDate,
            RecordDate: recordDate,
            PayableDate: payableDate,
            Amount: amount,
            Currency: currency,
            SplitFromFactor: from,
            SplitToFactor: to,
            Description: description,
            SourceProvider: ProviderId,
            SourceEventId: nativeId,
            SourceEventVersion: SourceVersion(evidenceHash, providerType),
            ObservedAtUtc: DateTimeOffset.UtcNow,
            EvidenceHash: evidenceHash,
            EvidenceReference: $"alpaca://corporate-actions/v1/{providerType}/{Uri.EscapeDataString(nativeId)}/versions/{MappingVersion}/{evidenceHash}",
            ReleaseStatus: ReleaseStatus)
        {
            Payload = payload,
            DistributionRatio = distributionRatio,
            ExchangeRatio = exchangeRatio,
            PreserveUnknownCurrency = true,
            SourceEvidence = new CorporateActionRetainedSourceEvidenceDto(
                providerType, "alpaca-market-data-corporate-actions-v1/meridian-v2", raw, Sha256Digest.ComputeUtf8(raw)),
        };
    }

    private static string SourceVersion(string evidenceHash, string providerType) =>
        $"payload-sha256:{evidenceHash}:{providerType}:{MappingVersion}";

    private static bool IsRelatedSymbolOnly(AlpacaAction action, string ticker, string type)
    {
        var source = type == "spin_off" ? action.SourceSymbol : action.AcquireeSymbol;
        var recipient = type == "spin_off" ? action.NewSymbol : action.AcquirerSymbol;
        return (type is "spin_off" or "cash_merger" or "stock_merger" or "stock_and_cash_merger")
            && !string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(source)
            && !string.Equals(source.Trim(), ticker, StringComparison.OrdinalIgnoreCase)
            && string.Equals(recipient?.Trim(), ticker, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement BuildEconomicPayload(
        AlpacaAction action, string type, string dateBasis, string sourceSymbol,
        string? successorSymbol, string? successorCusip, string? successorIsin,
        decimal? sourceRate, decimal? successorRate, decimal? cashPerUnit)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("providerEventType", type);
            writer.WriteString("dateBasis", dateBasis);
            WriteString("sourceSymbol", sourceSymbol);
            WriteString("sourceCusip", type == "spin_off" ? action.SourceCusip
                : type.Contains("merger", StringComparison.Ordinal) ? action.AcquireeCusip : action.Cusip);
            WriteString("sourceIsin", type == "spin_off" ? action.SourceIsin
                : type.Contains("merger", StringComparison.Ordinal) ? action.AcquireeIsin : action.Isin);
            WriteString("successorSymbol", successorSymbol);
            WriteString("successorCusip", successorCusip);
            WriteString("successorIsin", successorIsin);
            WriteNumber("sourceRate", sourceRate);
            WriteNumber("successorRate", successorRate);
            WriteNumber(CorporateActionPayloads.CashConsiderationPerUnit, cashPerUnit);
            if (type == "stock_and_cash_merger")
            {
                WriteNumber("sourceCashRate", action.CashRate);
                if (action.AcquireeRate != 1m)
                    writer.WriteString("cashRateBasis", "unresolved");
            }
            if (type == "stock_dividend")
                WriteNumber(CorporateActionPayloads.StockDividendRate, action.Rate);
            if (action.SubType == "return_of_capital")
                WriteNumber(CorporateActionPayloads.CapitalReturnPerShare, action.Rate);
            writer.WriteEndObject();

            void WriteString(string name, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    writer.WriteString(name, value.Trim().ToUpperInvariant());
            }
            void WriteNumber(string name, decimal? value)
            {
                if (value is { } number)
                    writer.WriteNumber(name, number);
            }
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateOnly? ParseOptionalDate(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : TryDate(value, out var date) ? date : throw new JsonException("Invalid corporate-action date.");

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("Duplicate corporate-action property.");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                ValidateUniqueProperties(child);
        }
    }

    private static string ComputePayloadHash(JsonElement payload) =>
        CorporateActionSourceProposalReplayComparer.ComputeSourceContentHash(payload);

    private sealed class AlpacaAction
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("symbol")] public string? Symbol { get; init; }
        [JsonPropertyName("cusip")] public string? Cusip { get; init; }
        [JsonPropertyName("isin")] public string? Isin { get; init; }
        [JsonPropertyName("ex_date")] public string? ExDate { get; init; }
        [JsonPropertyName("effective_date")] public string? EffectiveDate { get; init; }
        [JsonPropertyName("process_date")] public string? ProcessDate { get; init; }
        [JsonPropertyName("record_date")] public string? RecordDate { get; init; }
        [JsonPropertyName("payable_date")] public string? PayableDate { get; init; }
        [JsonPropertyName("currency")] public string? Currency { get; init; }
        [JsonPropertyName("sub_type")] public string? SubType { get; init; }
        [JsonPropertyName("rate")] public decimal? Rate { get; init; }
        [JsonPropertyName("special")] public bool? Special { get; init; }
        [JsonPropertyName("old_rate")] public decimal? OldRate { get; init; }
        [JsonPropertyName("new_rate")] public decimal? NewRate { get; init; }
        [JsonPropertyName("source_symbol")] public string? SourceSymbol { get; init; }
        [JsonPropertyName("source_cusip")] public string? SourceCusip { get; init; }
        [JsonPropertyName("source_isin")] public string? SourceIsin { get; init; }
        [JsonPropertyName("source_rate")] public decimal? SourceRate { get; init; }
        [JsonPropertyName("new_symbol")] public string? NewSymbol { get; init; }
        [JsonPropertyName("new_cusip")] public string? NewCusip { get; init; }
        [JsonPropertyName("new_isin")] public string? NewIsin { get; init; }
        [JsonPropertyName("acquiree_symbol")] public string? AcquireeSymbol { get; init; }
        [JsonPropertyName("acquiree_cusip")] public string? AcquireeCusip { get; init; }
        [JsonPropertyName("acquiree_isin")] public string? AcquireeIsin { get; init; }
        [JsonPropertyName("acquiree_rate")] public decimal? AcquireeRate { get; init; }
        [JsonPropertyName("acquirer_symbol")] public string? AcquirerSymbol { get; init; }
        [JsonPropertyName("acquirer_cusip")] public string? AcquirerCusip { get; init; }
        [JsonPropertyName("acquirer_isin")] public string? AcquirerIsin { get; init; }
        [JsonPropertyName("acquirer_rate")] public decimal? AcquirerRate { get; init; }
        [JsonPropertyName("cash_rate")] public decimal? CashRate { get; init; }
    }

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(AlpacaAction))]
    private sealed partial class AlpacaCorporateActionJsonContext : JsonSerializerContext;
}
