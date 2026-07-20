using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Backtesting.Sdk;
using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Serialization;

/// <summary>Source-generated JSON metadata for durable strategy-run snapshots.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StrategyRunEntry))]
[JsonSerializable(typeof(BacktestResult))]
[JsonSerializable(typeof(TradeCashFlow))]
[JsonSerializable(typeof(MarginInterestCashFlow))]
[JsonSerializable(typeof(ShortRebateCashFlow))]
[JsonSerializable(typeof(CommissionCashFlow))]
[JsonSerializable(typeof(DividendCashFlow))]
[JsonSerializable(typeof(CashInterestCashFlow))]
[JsonSerializable(typeof(AssetEventCashFlow))]
[JsonSerializable(typeof(List<JournalEntry>))]
internal sealed partial class StrategyRunPersistenceJsonContext : JsonSerializerContext;

internal static class StrategyRunPersistenceJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(StrategyRunPersistenceJsonContext.Default.Options);
        options.Converters.Add(new CashFlowEntryJsonConverter());
        options.Converters.Add(new ReadOnlyLedgerJsonConverter());
        return options;
    }
}

internal sealed class CashFlowEntryJsonConverter : JsonConverter<CashFlowEntry>
{
    public override CashFlowEntry Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("kind", out var kindElement) ||
            !root.TryGetProperty("value", out var valueElement))
        {
            throw new JsonException("Cash-flow history is missing its kind or value.");
        }

        return kindElement.GetString() switch
        {
            nameof(TradeCashFlow) => Deserialize<TradeCashFlow>(valueElement, options),
            nameof(MarginInterestCashFlow) => Deserialize<MarginInterestCashFlow>(valueElement, options),
            nameof(ShortRebateCashFlow) => Deserialize<ShortRebateCashFlow>(valueElement, options),
            nameof(CommissionCashFlow) => Deserialize<CommissionCashFlow>(valueElement, options),
            nameof(DividendCashFlow) => Deserialize<DividendCashFlow>(valueElement, options),
            nameof(CashInterestCashFlow) => Deserialize<CashInterestCashFlow>(valueElement, options),
            nameof(AssetEventCashFlow) => Deserialize<AssetEventCashFlow>(valueElement, options),
            var kind => throw new JsonException($"Unsupported retained cash-flow kind '{kind}'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        CashFlowEntry value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.GetType().Name);
        writer.WritePropertyName("value");
        switch (value)
        {
            case TradeCashFlow trade:
                JsonSerializer.Serialize(writer, trade, options);
                break;
            case MarginInterestCashFlow margin:
                JsonSerializer.Serialize(writer, margin, options);
                break;
            case ShortRebateCashFlow rebate:
                JsonSerializer.Serialize(writer, rebate, options);
                break;
            case CommissionCashFlow commission:
                JsonSerializer.Serialize(writer, commission, options);
                break;
            case DividendCashFlow dividend:
                JsonSerializer.Serialize(writer, dividend, options);
                break;
            case CashInterestCashFlow interest:
                JsonSerializer.Serialize(writer, interest, options);
                break;
            case AssetEventCashFlow assetEvent:
                JsonSerializer.Serialize(writer, assetEvent, options);
                break;
            default:
                throw new JsonException($"Unsupported cash-flow kind '{value.GetType().FullName}'.");
        }
        writer.WriteEndObject();
    }

    private static T Deserialize<T>(JsonElement value, JsonSerializerOptions options)
        where T : CashFlowEntry =>
        value.Deserialize<T>(options)
        ?? throw new JsonException($"Retained cash-flow '{typeof(T).Name}' was null.");
}

internal sealed class ReadOnlyLedgerJsonConverter : JsonConverter<IReadOnlyLedger>
{
    public override IReadOnlyLedger Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var entries = JsonSerializer.Deserialize<List<JournalEntry>>(ref reader, options)
            ?? throw new JsonException("Retained ledger journal was null.");
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var entry in entries)
        {
            ledger.Post(entry);
        }

        return ledger;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyLedger value,
        JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Journal.ToList(), options);
}
