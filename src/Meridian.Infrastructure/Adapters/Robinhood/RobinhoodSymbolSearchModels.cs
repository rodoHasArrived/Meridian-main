using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Response envelope from the Robinhood /instruments/ endpoint.
/// </summary>
internal sealed class RobinhoodInstrumentsResponse
{
    [JsonPropertyName("results")]
    public RobinhoodInstrumentItem[]? Results { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("previous")]
    public string? Previous { get; init; }
}

/// <summary>
/// A single instrument record returned by the Robinhood instruments API.
/// </summary>
internal sealed class RobinhoodInstrumentItem
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("simple_name")]
    public string? SimpleName { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("market")]
    public string? Market { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("tradeable")]
    public bool? Tradeable { get; init; }
}

/// <summary>
/// ADR-014 source-generated JSON context for Robinhood symbol search DTOs.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(RobinhoodInstrumentsResponse))]
[JsonSerializable(typeof(RobinhoodInstrumentItem))]
[JsonSerializable(typeof(RobinhoodInstrumentItem[]))]
internal sealed partial class RobinhoodJsonContext : JsonSerializerContext;
