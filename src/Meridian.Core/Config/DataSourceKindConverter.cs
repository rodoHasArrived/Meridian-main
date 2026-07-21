using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Core.Config;

/// <summary>
/// Parses <see cref="DataSourceKind"/> from config JSON strings (case-insensitive).
/// Unknown values are a configuration error: coercing them to a default provider would
/// silently route the operator to the wrong data source, so parsing fails closed instead.
/// </summary>
public sealed class DataSourceKindConverter : JsonConverter<DataSourceKind>
{
    public override DataSourceKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<DataSourceKind>(value, ignoreCase: true, out var parsed))
                return parsed;

            throw new JsonException(
                $"Unknown DataSource '{value}'. Valid values: {string.Join(", ", Enum.GetNames<DataSourceKind>())}.");
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var num))
        {
            // DataSourceKind is byte-backed; range-check before casting so out-of-range
            // numbers cannot wrap around onto a defined member.
            if (num >= 0 && num <= byte.MaxValue && Enum.IsDefined((DataSourceKind)num))
                return (DataSourceKind)num;

            throw new JsonException(
                $"Unknown DataSource value '{num}'. Valid values: {string.Join(", ", Enum.GetNames<DataSourceKind>())}.");
        }

        throw new JsonException(
            $"DataSource must be a string or number, not {reader.TokenType}. " +
            $"Valid values: {string.Join(", ", Enum.GetNames<DataSourceKind>())}.");
    }

    public override void Write(Utf8JsonWriter writer, DataSourceKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
