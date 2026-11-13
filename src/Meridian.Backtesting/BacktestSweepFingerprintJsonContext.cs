using System.Text.Json.Serialization;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting;

// Keep the default serializer contract: sweep-v2 already retains its PascalCase property names,
// numeric enums, nulls, decimal scale, and dictionary enumeration order in the fingerprint.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BacktestRequest))]
internal sealed partial class BacktestSweepFingerprintJsonContext : JsonSerializerContext;
