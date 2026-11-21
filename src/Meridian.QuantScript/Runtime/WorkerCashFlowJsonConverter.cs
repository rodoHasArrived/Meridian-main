using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meridian.Backtesting.Sdk;

namespace Meridian.QuantScript.Runtime;

/// <summary>Closed polymorphism for actual backtest cash flows across the worker boundary.</summary>
internal sealed class WorkerCashFlowJsonConverter : JsonConverter<CashFlowEntry>
{
    public override CashFlowEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("kind", out var kind) || !root.TryGetProperty("value", out var value))
            throw new JsonException("Worker cash flow omitted its kind or value.");
        var type = kind.GetString() switch
        {
            "trade" => typeof(TradeCashFlow),
            "commission" => typeof(CommissionCashFlow),
            "margin-interest" => typeof(MarginInterestCashFlow),
            "short-rebate" => typeof(ShortRebateCashFlow),
            "dividend" => typeof(DividendCashFlow),
            "cash-interest" => typeof(CashInterestCashFlow),
            "asset-event" => typeof(AssetEventCashFlow),
            _ => throw new JsonException("Worker cash flow kind is unsupported.")
        };
        return value.Deserialize(options.GetTypeInfo(type)) as CashFlowEntry
            ?? throw new JsonException("Worker cash flow value was null.");
    }

    public override void Write(Utf8JsonWriter writer, CashFlowEntry value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case TradeCashFlow flow:
                WriteFlow(writer, "trade", flow, options);
                break;
            case CommissionCashFlow flow:
                WriteFlow(writer, "commission", flow, options);
                break;
            case MarginInterestCashFlow flow:
                WriteFlow(writer, "margin-interest", flow, options);
                break;
            case ShortRebateCashFlow flow:
                WriteFlow(writer, "short-rebate", flow, options);
                break;
            case DividendCashFlow flow:
                WriteFlow(writer, "dividend", flow, options);
                break;
            case CashInterestCashFlow flow:
                WriteFlow(writer, "cash-interest", flow, options);
                break;
            case AssetEventCashFlow flow:
                WriteFlow(writer, "asset-event", flow, options);
                break;
            default:
                throw new JsonException("Worker cash flow type is unsupported.");
        }
    }

    private static void WriteFlow<T>(Utf8JsonWriter writer, string kind, T value, JsonSerializerOptions options)
        where T : CashFlowEntry
    {
        var metadata = options.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
            ?? throw new JsonException("Worker cash flow type is not registered in generated metadata.");
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value, metadata);
        writer.WriteEndObject();
    }
}
