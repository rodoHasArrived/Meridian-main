using System.Text.Json.Serialization;

namespace Meridian.Application.Backfill;

/// <summary>
/// Storage-owned ADR-014 source-generated serialization context for persisted backfill status
/// and checkpoint sidecars. Keeps checkpoint persistence on the high-performance JSON path.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(BackfillResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
internal sealed partial class BackfillStatusStoreJsonContext : JsonSerializerContext;
