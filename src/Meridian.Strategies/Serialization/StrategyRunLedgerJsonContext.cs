using System.Text.Json.Serialization;
using Meridian.Strategies.Storage;

namespace Meridian.Strategies.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StrategyRunLedgerHeader))]
internal sealed partial class StrategyRunLedgerJsonContext : JsonSerializerContext;
