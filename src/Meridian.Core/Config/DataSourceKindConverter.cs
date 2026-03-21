using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Application.Config;

/// <summary>
/// Parses <see cref="DataSourceKind"/> from config JSON strings in a forgiving way
/// (case-insensitive, defaults to IB on unknown values).
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
        }
        else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var num))
        {
            if (Enum.IsDefined(typeof(DataSourceKind), num))
                return (DataSourceKind)num;
        }

        return DataSourceKind.IB;
    }

    public override void Write(Utf8JsonWriter writer, DataSourceKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
