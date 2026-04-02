using System.Text.Json;
using Meridian.Application.Serialization;

namespace Meridian.Application.Config;

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
        // Start with the source-generated context options
        var opts = new JsonSerializerOptions
        {
            TypeInfoResolver = MarketDataJsonContext.Default,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Allow C-style // comments so the sample config can be used directly
            ReadCommentHandling = JsonCommentHandling.Skip,
            // Allow trailing commas as a defensive measure for hand-edited config files
            AllowTrailingCommas = true
        };

        // Add custom converter for DataSourceKind enum parsing
        opts.Converters.Add(new DataSourceKindConverter());
        return opts;
    }

    private static JsonSerializerOptions CreateWrite()
    {
        // Start with the source-generated context options
        var opts = new JsonSerializerOptions
        {
            TypeInfoResolver = MarketDataJsonContext.Default,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        // Add custom converter for DataSourceKind enum parsing
        opts.Converters.Add(new DataSourceKindConverter());
        return opts;
    }
}
