using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Reporting;

/// <summary>
/// Comparison amounts stay decimals in accounting code and cross JSON boundaries as canonical
/// decimal strings. Numeric tokens remain readable so previously retained comparisons still load.
/// </summary>
public sealed class ReportingIncomeDecimalJsonConverter : JsonConverter<decimal>
{
    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadValue(ref reader);

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteStringValue(Format(value));

    internal static decimal ReadValue(ref Utf8JsonReader reader)
    {
        // Version 1 retained artifacts used JSON numbers. Read directly as decimal, never double.
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var legacyValue))
            return legacyValue;
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)
                && string.Equals(text, Format(value), StringComparison.Ordinal))
                return value;
        }
        throw new JsonException("A canonical decimal string or a retained legacy decimal number is required.");
    }
}

public sealed class ReportingIncomeNullableDecimalJsonConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : ReportingIncomeDecimalJsonConverter.ReadValue(ref reader);

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is { } amount)
            writer.WriteStringValue(ReportingIncomeDecimalJsonConverter.Format(amount));
        else
            writer.WriteNullValue();
    }
}
