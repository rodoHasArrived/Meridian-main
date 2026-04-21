using System.Text.Json.Serialization;

namespace Meridian.Application.Backfill;

/// <summary>
/// ADR-014 source-generated serialization context for persisted backfill status
/// and checkpoint sidecars. Keeps checkpoint persistence on the high-performance
/// JSON path without widening any public DTO surface.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(BackfillResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
internal sealed partial class BackfillStatusStoreJsonContext : JsonSerializerContext;
