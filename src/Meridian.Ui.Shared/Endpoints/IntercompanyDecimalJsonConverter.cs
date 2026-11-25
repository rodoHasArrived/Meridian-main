using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>New monitor transport preserves decimal digits without changing retained schedule serialization or hashes.</summary>
internal sealed class IntercompanyDecimalJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)
            : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
