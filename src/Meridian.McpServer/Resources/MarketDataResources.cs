using System.Text.Json.Serialization;

namespace Meridian.McpServer.Resources;

/// <summary>
/// MCP resources that expose read-only market data catalogs to the LLM context.
/// </summary>
[McpServerResourceType]
[ImplementsAdr("ADR-005", "Attribute-based MCP resource discovery")]
public sealed class MarketDataResources
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly BackfillCoordinator _coordinator;
    private readonly IStorageCatalogService _catalog;
    private readonly ConfigStore _store;

    /// <summary>
    /// Initialises with DI-supplied services.
    /// </summary>
    public MarketDataResources(
        BackfillCoordinator coordinator,
        IStorageCatalogService catalog,
        ConfigStore store)
    {
        _coordinator = coordinator;
        _catalog = catalog;
        _store = store;
    }

    /// <summary>
    /// Provider catalog resource — complete list of available data providers.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mdc://providers/catalog",
        Name = "Provider Catalog",
        MimeType = "application/json",
        Title = "Complete catalog of all registered historical and streaming data providers " +
                "with their capabilities, priority, rate limits, and supported markets.")]
    public string GetProviderCatalog()
    {
        var providers = _coordinator.DescribeProviders();
        return JsonSerializer.Serialize(new
        {
            generatedAt = DateTimeOffset.UtcNow.ToString("o"),
            providers
        }, JsonOpts);
    }

    /// <summary>
    /// Storage catalog resource — summary of locally stored market data.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mdc://storage/catalog",
        Name = "Storage Catalog",
        MimeType = "application/json",
        Title = "Summary of locally stored market data: symbols, date ranges, file counts, " +
                "and storage statistics.")]
    public string GetStorageCatalog()
    {
        var catalog = _catalog.GetCatalog();
        var stats = _catalog.GetStatistics();

        var symbols = catalog.Symbols.Values
            .Select(s => new
            {
                s.Symbol,
                s.FileCount,
                s.EventCount,
                EarliestDate = s.DateRange?.Earliest.ToString("yyyy-MM-dd"),
                LatestDate = s.DateRange?.Latest.ToString("yyyy-MM-dd"),
                s.EventTypes,
                s.Sources
            })
            .OrderBy(s => s.Symbol)
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            generatedAt = DateTimeOffset.UtcNow.ToString("o"),
            statistics = stats,
            symbols
        }, JsonOpts);
    }

    /// <summary>
    /// Active configuration resource — current symbol subscriptions and data source settings.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mdc://config/active",
        Name = "Active Configuration",
        MimeType = "application/json",
        Title = "Current Meridian configuration: active data source, " +
                "configured symbols, backfill defaults, and storage settings.")]
    public string GetActiveConfiguration()
    {
        var cfg = _store.Load();

        return JsonSerializer.Serialize(new
        {
            generatedAt = DateTimeOffset.UtcNow.ToString("o"),
            configPath = _store.ConfigPath,
            dataSource = cfg.DataSource.ToString(),
            dataRoot = cfg.DataRoot,
            symbolCount = cfg.Symbols?.Length ?? 0,
            symbols = cfg.Symbols?.Select(s => s.Symbol).ToArray() ?? [],
            backfillProvider = cfg.Backfill?.Provider ?? "stooq",
            backfillEnabled = cfg.Backfill?.Enabled ?? false
        }, JsonOpts);
    }
}
