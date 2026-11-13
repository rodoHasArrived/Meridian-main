using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Backtesting.Sdk;

/// <summary>Preserves an uncomputed metric as JSON null and named infinite profit factors.</summary>
public class UnavailableMetricJsonConverter : JsonConverter<double>
{
    protected virtual bool AllowsInfinity => true;

    public override bool HandleNull => true;

    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return double.NaN;
        if (reader.TokenType == JsonTokenType.Number)
        {
            var value = reader.GetDouble();
            if (!AllowsInfinity && double.IsInfinity(value))
                throw new JsonException("Expected a finite win rate or null.");
            return value;
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "NaN" => double.NaN, // Legacy named sentinel.
                "Infinity" when AllowsInfinity => double.PositiveInfinity,
                "-Infinity" when AllowsInfinity => double.NegativeInfinity,
                _ => throw new JsonException("Expected a numeric metric or null.")
            };
        }
        throw new JsonException("Expected a numeric metric or null.");
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsNaN(value))
            writer.WriteNullValue();
        else if (!AllowsInfinity && double.IsInfinity(value))
            throw new JsonException("Expected a finite win rate or null.");
        else if (double.IsPositiveInfinity(value))
            writer.WriteStringValue("Infinity");
        else if (double.IsNegativeInfinity(value))
            writer.WriteStringValue("-Infinity");
        else
            writer.WriteNumberValue(value);
    }
}

/// <summary>Win rate has no meaningful infinite outcome; null still denotes unavailable evidence.</summary>
public sealed class UnavailableWinRateJsonConverter : UnavailableMetricJsonConverter
{
    protected override bool AllowsInfinity => false;
}
