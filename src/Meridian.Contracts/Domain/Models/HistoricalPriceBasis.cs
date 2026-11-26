using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Declared adjustment basis of one complete historical OHLC price set. This describes prices,
/// not source authenticity, volume units, adjustment vintage, or accounting eligibility.
/// </summary>
[JsonConverter(typeof(HistoricalPriceBasisJsonConverter))]
public enum HistoricalPriceBasis
{
    /// <summary>The source has not established a supported, precise price basis.</summary>
    Unknown = 0,

    /// <summary>Original as-traded prices, without corporate-action adjustments.</summary>
    Raw = 1,

    /// <summary>Prices adjusted for stock splits, without cash-dividend adjustments.</summary>
    SplitAdjusted = 2,

    /// <summary>Prices adjusted for cash dividends, without stock-split adjustments.</summary>
    DividendAdjusted = 3,

    /// <summary>
    /// Prices adjusted for both splits and dividends under the source's methodology.
    /// Different providers or vintages are not necessarily directly comparable.
    /// </summary>
    FullyAdjusted = 4
}

/// <summary>Exact-name parsing shared by typed storage and projected historical exports.</summary>
public static class HistoricalPriceBasisValues
{
    private static readonly HistoricalPriceBasis[] SupportedValues = Enum.GetValues<HistoricalPriceBasis>();

    /// <summary>
    /// Parses one supported name. Enum flag combinations and numeric strings are not declarations.
    /// </summary>
    public static bool TryParse(string? value, out HistoricalPriceBasis basis)
    {
        basis = HistoricalPriceBasis.Unknown;
        if (value is null)
            return false;

        var name = value.Trim();
        foreach (var candidate in SupportedValues)
        {
            if (string.Equals(name, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                basis = candidate;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Serializes readable basis names and rejects composite strings that Enum.TryParse would
/// otherwise combine into an unrelated defined value, such as "Unknown, Raw" becoming Raw.
/// </summary>
public sealed class HistoricalPriceBasisJsonConverter : JsonConverter<HistoricalPriceBasis>
{
    public override HistoricalPriceBasis Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            HistoricalPriceBasisValues.TryParse(reader.GetString(), out var basis))
            return basis;

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric) &&
            Enum.IsDefined((HistoricalPriceBasis)numeric))
            return (HistoricalPriceBasis)numeric;

        throw new JsonException("A single supported historical price basis is required.");
    }

    public override void Write(Utf8JsonWriter writer, HistoricalPriceBasis value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
            throw new JsonException("Unsupported historical price basis.");
        writer.WriteStringValue(value.ToString());
    }
}
