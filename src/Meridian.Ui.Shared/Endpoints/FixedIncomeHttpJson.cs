using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Exact decimal text at the HTTP boundary only. Retained calculations, receipt bytes and
/// content hashes continue to use their existing canonical serialization options.
/// </summary>
public static class FixedIncomeHttpJson
{
    public static JsonSerializerOptions Options(JsonSerializerOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var options = new JsonSerializerOptions(source);
        options.Converters.Insert(0, new DecimalTextConverter());
        return options;
    }

    private sealed class DecimalTextConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetDecimal();
            if (reader.TokenType == JsonTokenType.String
                && decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            throw new JsonException("Expected an exact decimal string.");
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
