using System.Text.Json.Serialization;
using Meridian.Contracts.Etl;

namespace Meridian.DataIntegration.Etl;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EtlJobDefinition))]
internal sealed partial class EtlOperationJsonContext : JsonSerializerContext;
