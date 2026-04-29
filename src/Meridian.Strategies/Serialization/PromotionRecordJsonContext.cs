using System.Text.Json.Serialization;
using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Serialization;

/// <summary>
/// Source-generated JSON context for durable promotion history records.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StrategyPromotionRecord))]
[JsonSerializable(typeof(List<StrategyPromotionRecord>))]
internal sealed partial class PromotionRecordJsonContext : JsonSerializerContext
{
}
