using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Serialization;

/// <summary>
/// Source-generated JSON context for durable Strategy Builder draft history.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StrategyDesignDocument))]
[JsonSerializable(typeof(List<StrategyDesignDocument>))]
internal sealed partial class StrategyDesignJsonContext : JsonSerializerContext
{
}
