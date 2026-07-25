using System.Text.Json.Serialization;

namespace Meridian.Storage.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(QualityTrendPoint))]
[JsonSerializable(typeof(QualityTrendChainRecord))]
[JsonSerializable(typeof(QualityTrendChainHead))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, double>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
internal sealed partial class QualityTrendStoreJsonContext : JsonSerializerContext;
