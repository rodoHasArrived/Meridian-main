using System.Text.Json;
using Meridian.Core.Serialization;

namespace Meridian.Core.Config;

/// <summary>
/// Centralized JSON serializer options for reading/writing AppConfig.
/// Extends MarketDataJsonContext options with custom converters for configuration-specific types.
/// </summary>
public static class AppConfigJsonOptions
{
    /// <summary>
    /// Options for reading AppConfig JSON files.
    /// Uses source-generated serializers with custom DataSourceKind converter.
    /// </summary>
    public static JsonSerializerOptions Read { get; } = CreateRead();

    /// <summary>
    /// Options for writing AppConfig JSON files (pretty-printed).
    /// Uses source-generated serializers with custom DataSourceKind converter.
    /// </summary>
    public static JsonSerializerOptions Write { get; } = CreateWrite();

    private static JsonSerializerOptions CreateRead()
    {
        var opts = new JsonSerializerOptions
        {
            TypeInfoResolver = MarketDataJsonContext.Default,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        opts.Converters.Add(new DataSourceKindConverter());
        return opts;
    }

    private static JsonSerializerOptions CreateWrite()
    {
        var opts = new JsonSerializerOptions
        {
            TypeInfoResolver = MarketDataJsonContext.Default,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        opts.Converters.Add(new DataSourceKindConverter());
        return opts;
    }
}
