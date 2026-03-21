using System.Collections.Frozen;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.FSharp.Canonicalization;
using Serilog;

namespace Meridian.Application.Canonicalization;

/// <summary>
/// Maps provider-specific venue identifiers to ISO 10383 MIC (Market Identifier Code).
/// Loaded from <c>config/venue-mapping.json</c> at startup.
/// Thread-safe after initialization (uses frozen dictionary).
/// </summary>
public sealed class VenueMicMapper
{
    private readonly ILogger _log = LoggingSetup.ForContext<VenueMicMapper>();

    /// <summary>
    /// Lookup: (PROVIDER, rawVenue) -> ISO 10383 MIC code (or null for unmappable venues like IB "SMART").
    /// </summary>
    private readonly FrozenDictionary<(string Provider, string RawVenue), string?> _map;

    /// <summary>
    /// Version of the mapping table (from the JSON file).
    /// </summary>
    public int Version { get; }

    private VenueMicMapper(
        FrozenDictionary<(string Provider, string RawVenue), string?> map,
        int version)
    {
        _map = map;
        Version = version;
    }

    /// <summary>
    /// Loads the venue mapping from a JSON file.
    /// </summary>
    public static VenueMicMapper LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warning("Venue mapping file not found at {Path}, using empty mappings", path);
            return new VenueMicMapper(
                FrozenDictionary<(string, string), string?>.Empty, 0);
        }

        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    /// <summary>
    /// Loads the venue mapping from a JSON string.
    /// </summary>
    public static VenueMicMapper LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var version = root.TryGetProperty("version", out var vProp) ? vProp.GetInt32() : 0;
        var dict = new Dictionary<(string, string), string?>();

        if (root.TryGetProperty("mappings", out var mappings))
        {
            foreach (var providerProp in mappings.EnumerateObject())
            {
                var provider = VenueMappingRules.NormalizeProvider(providerProp.Name);
                foreach (var venueProp in providerProp.Value.EnumerateObject())
                {
                    var rawVenue = venueProp.Name;
                    var mic = venueProp.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : venueProp.Value.GetString();
                    dict[(provider, rawVenue)] = mic;
                }
            }
        }

        return new VenueMicMapper(dict.ToFrozenDictionary(), version);
    }

    /// <summary>
    /// Tries to map a raw venue identifier to an ISO 10383 MIC code.
    /// </summary>
    /// <param name="rawVenue">Raw venue string from the provider (may be null).</param>
    /// <param name="provider">Provider name (e.g., "ALPACA", "POLYGON", "IB").</param>
    /// <returns>The ISO MIC code, or null if the venue is unmappable or unknown.</returns>
    public string? TryMapVenue(string? rawVenue, string provider)
    {
        if (rawVenue is null)
        {
            return null;
        }
        return VenueMappingRules.TryMapVenue(_map, rawVenue, provider);
    }

    /// <summary>
    /// Gets the total number of mappings loaded.
    /// </summary>
    public int MappingCount => _map.Count;
}
