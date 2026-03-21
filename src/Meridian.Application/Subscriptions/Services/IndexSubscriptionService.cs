using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.UI;
using Serilog;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for auto-subscribing to index components.
/// </summary>
/// <remarks>
/// <para><b>Current Implementation:</b> Uses built-in static index component data for major indices
/// (SPX, NDX, DJI, RUT, MID). This data is suitable for development and testing.</para>
/// <para><b>Production Enhancement:</b> For production use with real-time index rebalancing,
/// implement an external API integration (e.g., S&amp;P Global, NASDAQ, IEX Cloud) by extending
/// <see cref="GetIndexComponentsAsync"/> to fetch live constituent data.</para>
/// </remarks>
public sealed class IndexSubscriptionService
{
    private readonly ConfigStore _configStore;
    private readonly MetadataEnrichmentService _metadataService;
    private readonly ILogger _log;

    // Built-in index component data for development/testing.
    // For production: extend GetIndexComponentsAsync to fetch from external API.
    private static readonly Dictionary<string, IndexComponents> BuiltInIndices = InitializeBuiltInIndices();

    public IndexSubscriptionService(
        ConfigStore configStore,
        MetadataEnrichmentService metadataService,
        ILogger? log = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _log = log ?? LoggingSetup.ForContext<IndexSubscriptionService>();
    }

    /// <summary>
    /// Get available indices for subscription.
    /// </summary>
    public IReadOnlyList<IndexDefinition> GetAvailableIndices()
    {
        return KnownIndices.All;
    }

    /// <summary>
    /// Get components for a specific index.
    /// </summary>
    /// <remarks>
    /// Currently returns built-in static data. For production use, this method is the extension
    /// point for integrating external index constituent APIs (e.g., S&amp;P Global, IEX Cloud).
    /// </remarks>
    public Task<IndexComponents?> GetIndexComponentsAsync(
        string indexId,
        CancellationToken ct = default)
    {
        if (BuiltInIndices.TryGetValue(indexId, out var components))
        {
            return Task.FromResult<IndexComponents?>(components);
        }

        // Extension point: integrate external API for live index constituents.
        // Example providers: S&P Global Market Intelligence, IEX Cloud, Polygon.io
        return Task.FromResult<IndexComponents?>(null);
    }

    /// <summary>
    /// Subscribe to all components of an index.
    /// </summary>
    public async Task<IndexSubscribeResult> SubscribeToIndexAsync(
        IndexSubscribeRequest request,
        CancellationToken ct = default)
    {
        var components = await GetIndexComponentsAsync(request.IndexId, ct);
        if (components is null)
        {
            return new IndexSubscribeResult(
                request.IndexId,
                ComponentsSubscribed: 0,
                ComponentsSkipped: 0,
                SubscribedSymbols: Array.Empty<string>(),
                SkippedSymbols: Array.Empty<string>(),
                Message: $"Index '{request.IndexId}' not found"
            );
        }

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        var defaults = request.Defaults ?? new TemplateSubscriptionDefaults();
        var subscribed = new List<string>();
        var skipped = new List<string>();

        // Filter components
        var componentsToProcess = components.Components.AsEnumerable();

        if (request.FilterSectors is { Length: > 0 })
        {
            var sectorFilter = new HashSet<string>(request.FilterSectors, StringComparer.OrdinalIgnoreCase);
            componentsToProcess = componentsToProcess.Where(c =>
                !string.IsNullOrEmpty(c.Sector) && sectorFilter.Contains(c.Sector));
        }

        // Order by weight (highest first) if available
        componentsToProcess = componentsToProcess
            .OrderByDescending(c => c.Weight ?? 0)
            .ToList();

        // Limit to max components if specified
        if (request.MaxComponents.HasValue)
        {
            componentsToProcess = componentsToProcess.Take(request.MaxComponents.Value);
        }

        if (request.ReplaceExisting)
        {
            existingSymbols.Clear();
        }

        foreach (var component in componentsToProcess)
        {
            if (existingSymbols.ContainsKey(component.Symbol) && !request.ReplaceExisting)
            {
                skipped.Add(component.Symbol);
                continue;
            }

            var symbolConfig = new SymbolConfig(
                Symbol: component.Symbol,
                SubscribeTrades: defaults.SubscribeTrades,
                SubscribeDepth: defaults.SubscribeDepth,
                DepthLevels: defaults.DepthLevels,
                SecurityType: defaults.SecurityType,
                Exchange: defaults.Exchange,
                Currency: defaults.Currency
            );

            existingSymbols[component.Symbol] = symbolConfig;
            subscribed.Add(component.Symbol);
        }

        var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
        await _configStore.SaveAsync(next);

        _log.Information(
            "Subscribed to {Count} components of index {IndexId}, skipped {Skipped}",
            subscribed.Count, request.IndexId, skipped.Count);

        return new IndexSubscribeResult(
            IndexId: request.IndexId,
            ComponentsSubscribed: subscribed.Count,
            ComponentsSkipped: skipped.Count,
            SubscribedSymbols: subscribed.ToArray(),
            SkippedSymbols: skipped.ToArray(),
            Message: $"Successfully subscribed to {subscribed.Count} components of {components.Name}"
        );
    }

    /// <summary>
    /// Unsubscribe from all components of an index.
    /// </summary>
    public async Task<IndexSubscribeResult> UnsubscribeFromIndexAsync(
        string indexId,
        CancellationToken ct = default)
    {
        var components = await GetIndexComponentsAsync(indexId, ct);
        if (components is null)
        {
            return new IndexSubscribeResult(
                indexId,
                ComponentsSubscribed: 0,
                ComponentsSkipped: 0,
                SubscribedSymbols: Array.Empty<string>(),
                SkippedSymbols: Array.Empty<string>(),
                Message: $"Index '{indexId}' not found"
            );
        }

        var cfg = _configStore.Load();
        var indexSymbols = new HashSet<string>(
            components.Components.Select(c => c.Symbol),
            StringComparer.OrdinalIgnoreCase);

        var remaining = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .Where(s => !indexSymbols.Contains(s.Symbol))
            .ToArray();

        var removed = (cfg.Symbols?.Length ?? 0) - remaining.Length;

        var next = cfg with { Symbols = remaining };
        await _configStore.SaveAsync(next);

        _log.Information("Unsubscribed from {Count} components of index {IndexId}", removed, indexId);

        return new IndexSubscribeResult(
            IndexId: indexId,
            ComponentsSubscribed: 0,
            ComponentsSkipped: 0,
            SubscribedSymbols: Array.Empty<string>(),
            SkippedSymbols: indexSymbols.ToArray(),
            Message: $"Removed {removed} components of {components.Name}"
        );
    }

    /// <summary>
    /// Check which index components are currently subscribed.
    /// </summary>
    public async Task<IndexSubscriptionStatus> GetSubscriptionStatusAsync(
        string indexId,
        CancellationToken ct = default)
    {
        var components = await GetIndexComponentsAsync(indexId, ct);
        if (components is null)
        {
            return new IndexSubscriptionStatus(indexId, null, 0, 0, Array.Empty<string>(), Array.Empty<string>());
        }

        var cfg = _configStore.Load();
        var subscribedSymbols = new HashSet<string>(
            (cfg.Symbols ?? Array.Empty<SymbolConfig>()).Select(s => s.Symbol),
            StringComparer.OrdinalIgnoreCase);

        var subscribed = new List<string>();
        var notSubscribed = new List<string>();

        foreach (var component in components.Components)
        {
            if (subscribedSymbols.Contains(component.Symbol))
                subscribed.Add(component.Symbol);
            else
                notSubscribed.Add(component.Symbol);
        }

        return new IndexSubscriptionStatus(
            IndexId: indexId,
            IndexName: components.Name,
            TotalComponents: components.Components.Length,
            SubscribedCount: subscribed.Count,
            SubscribedSymbols: subscribed.ToArray(),
            NotSubscribedSymbols: notSubscribed.ToArray()
        );
    }

    private static Dictionary<string, IndexComponents> InitializeBuiltInIndices()
    {
        var indices = new Dictionary<string, IndexComponents>(StringComparer.OrdinalIgnoreCase);

        // S&P 500 (Top 30 by weight as sample)
        indices["SPX"] = new IndexComponents(
            IndexId: "SPX",
            Name: "S&P 500",
            Components: new[]
            {
                new IndexComponent("AAPL", "Apple Inc.", 0.072m, "Technology"),
                new IndexComponent("MSFT", "Microsoft Corporation", 0.068m, "Technology"),
                new IndexComponent("AMZN", "Amazon.com Inc.", 0.034m, "Consumer Discretionary"),
                new IndexComponent("NVDA", "NVIDIA Corporation", 0.031m, "Technology"),
                new IndexComponent("GOOGL", "Alphabet Inc. Class A", 0.021m, "Technology"),
                new IndexComponent("META", "Meta Platforms Inc.", 0.019m, "Technology"),
                new IndexComponent("TSLA", "Tesla Inc.", 0.018m, "Consumer Discretionary"),
                new IndexComponent("BRK.B", "Berkshire Hathaway Inc.", 0.017m, "Financials"),
                new IndexComponent("UNH", "UnitedHealth Group", 0.014m, "Healthcare"),
                new IndexComponent("JNJ", "Johnson & Johnson", 0.012m, "Healthcare"),
                new IndexComponent("JPM", "JPMorgan Chase & Co.", 0.012m, "Financials"),
                new IndexComponent("V", "Visa Inc.", 0.011m, "Financials"),
                new IndexComponent("XOM", "Exxon Mobil Corporation", 0.011m, "Energy"),
                new IndexComponent("PG", "Procter & Gamble", 0.010m, "Consumer Staples"),
                new IndexComponent("MA", "Mastercard Incorporated", 0.010m, "Financials"),
                new IndexComponent("HD", "The Home Depot", 0.009m, "Consumer Discretionary"),
                new IndexComponent("CVX", "Chevron Corporation", 0.009m, "Energy"),
                new IndexComponent("MRK", "Merck & Co.", 0.008m, "Healthcare"),
                new IndexComponent("LLY", "Eli Lilly and Company", 0.008m, "Healthcare"),
                new IndexComponent("ABBV", "AbbVie Inc.", 0.008m, "Healthcare"),
                new IndexComponent("PEP", "PepsiCo Inc.", 0.007m, "Consumer Staples"),
                new IndexComponent("KO", "The Coca-Cola Company", 0.007m, "Consumer Staples"),
                new IndexComponent("COST", "Costco Wholesale", 0.007m, "Consumer Staples"),
                new IndexComponent("AVGO", "Broadcom Inc.", 0.007m, "Technology"),
                new IndexComponent("WMT", "Walmart Inc.", 0.006m, "Consumer Staples"),
                new IndexComponent("BAC", "Bank of America Corp", 0.006m, "Financials"),
                new IndexComponent("TMO", "Thermo Fisher Scientific", 0.006m, "Healthcare"),
                new IndexComponent("CSCO", "Cisco Systems", 0.006m, "Technology"),
                new IndexComponent("MCD", "McDonald's Corporation", 0.005m, "Consumer Discretionary"),
                new IndexComponent("ACN", "Accenture plc", 0.005m, "Technology")
            },
            LastUpdated: DateTimeOffset.UtcNow,
            Source: "Built-in"
        );

        // NASDAQ 100
        indices["NDX"] = new IndexComponents(
            IndexId: "NDX",
            Name: "NASDAQ 100",
            Components: new[]
            {
                new IndexComponent("AAPL", "Apple Inc.", 0.127m, "Technology"),
                new IndexComponent("MSFT", "Microsoft Corporation", 0.119m, "Technology"),
                new IndexComponent("AMZN", "Amazon.com Inc.", 0.059m, "Consumer Discretionary"),
                new IndexComponent("NVDA", "NVIDIA Corporation", 0.054m, "Technology"),
                new IndexComponent("META", "Meta Platforms Inc.", 0.042m, "Technology"),
                new IndexComponent("GOOGL", "Alphabet Inc. Class A", 0.037m, "Technology"),
                new IndexComponent("GOOG", "Alphabet Inc. Class C", 0.036m, "Technology"),
                new IndexComponent("TSLA", "Tesla Inc.", 0.032m, "Consumer Discretionary"),
                new IndexComponent("AVGO", "Broadcom Inc.", 0.024m, "Technology"),
                new IndexComponent("PEP", "PepsiCo Inc.", 0.019m, "Consumer Staples"),
                new IndexComponent("COST", "Costco Wholesale", 0.018m, "Consumer Staples"),
                new IndexComponent("CSCO", "Cisco Systems", 0.014m, "Technology"),
                new IndexComponent("AMD", "Advanced Micro Devices", 0.013m, "Technology"),
                new IndexComponent("ADBE", "Adobe Inc.", 0.013m, "Technology"),
                new IndexComponent("NFLX", "Netflix Inc.", 0.012m, "Technology"),
                new IndexComponent("INTC", "Intel Corporation", 0.011m, "Technology"),
                new IndexComponent("QCOM", "Qualcomm Inc.", 0.011m, "Technology"),
                new IndexComponent("TXN", "Texas Instruments", 0.010m, "Technology"),
                new IndexComponent("INTU", "Intuit Inc.", 0.010m, "Technology"),
                new IndexComponent("CMCSA", "Comcast Corporation", 0.009m, "Communication Services")
            },
            LastUpdated: DateTimeOffset.UtcNow,
            Source: "Built-in"
        );

        // Dow Jones Industrial Average
        indices["DJI"] = new IndexComponents(
            IndexId: "DJI",
            Name: "Dow Jones Industrial Average",
            Components: new[]
            {
                new IndexComponent("UNH", "UnitedHealth Group", 0.099m, "Healthcare"),
                new IndexComponent("GS", "Goldman Sachs Group", 0.069m, "Financials"),
                new IndexComponent("MSFT", "Microsoft Corporation", 0.062m, "Technology"),
                new IndexComponent("HD", "The Home Depot", 0.058m, "Consumer Discretionary"),
                new IndexComponent("CAT", "Caterpillar Inc.", 0.052m, "Industrials"),
                new IndexComponent("MCD", "McDonald's Corporation", 0.051m, "Consumer Discretionary"),
                new IndexComponent("V", "Visa Inc.", 0.045m, "Financials"),
                new IndexComponent("AMGN", "Amgen Inc.", 0.044m, "Healthcare"),
                new IndexComponent("BA", "Boeing Company", 0.041m, "Industrials"),
                new IndexComponent("HON", "Honeywell International", 0.038m, "Industrials"),
                new IndexComponent("CRM", "Salesforce Inc.", 0.038m, "Technology"),
                new IndexComponent("TRV", "The Travelers Companies", 0.036m, "Financials"),
                new IndexComponent("AXP", "American Express", 0.034m, "Financials"),
                new IndexComponent("JPM", "JPMorgan Chase & Co.", 0.032m, "Financials"),
                new IndexComponent("IBM", "IBM Corporation", 0.030m, "Technology"),
                new IndexComponent("AAPL", "Apple Inc.", 0.030m, "Technology"),
                new IndexComponent("JNJ", "Johnson & Johnson", 0.028m, "Healthcare"),
                new IndexComponent("PG", "Procter & Gamble", 0.027m, "Consumer Staples"),
                new IndexComponent("CVX", "Chevron Corporation", 0.027m, "Energy"),
                new IndexComponent("MRK", "Merck & Co.", 0.020m, "Healthcare"),
                new IndexComponent("NKE", "Nike Inc.", 0.018m, "Consumer Discretionary"),
                new IndexComponent("DIS", "The Walt Disney Company", 0.017m, "Communication Services"),
                new IndexComponent("MMM", "3M Company", 0.017m, "Industrials"),
                new IndexComponent("KO", "The Coca-Cola Company", 0.011m, "Consumer Staples"),
                new IndexComponent("DOW", "Dow Inc.", 0.010m, "Materials"),
                new IndexComponent("CSCO", "Cisco Systems", 0.009m, "Technology"),
                new IndexComponent("WMT", "Walmart Inc.", 0.009m, "Consumer Staples"),
                new IndexComponent("INTC", "Intel Corporation", 0.006m, "Technology"),
                new IndexComponent("VZ", "Verizon Communications", 0.006m, "Communication Services"),
                new IndexComponent("WBA", "Walgreens Boots Alliance", 0.004m, "Healthcare")
            },
            LastUpdated: DateTimeOffset.UtcNow,
            Source: "Built-in"
        );

        // Russell 2000 (sample of top components)
        indices["RUT"] = new IndexComponents(
            IndexId: "RUT",
            Name: "Russell 2000",
            Components: new[]
            {
                new IndexComponent("SMCI", "Super Micro Computer", 0.008m, "Technology"),
                new IndexComponent("LUMN", "Lumen Technologies", 0.004m, "Communication Services"),
                new IndexComponent("SAIA", "Saia Inc.", 0.004m, "Industrials"),
                new IndexComponent("CROX", "Crocs Inc.", 0.003m, "Consumer Discretionary"),
                new IndexComponent("KNSL", "Kinsale Capital Group", 0.003m, "Financials"),
                new IndexComponent("PRIM", "Primoris Services", 0.003m, "Industrials"),
                new IndexComponent("CVLT", "Commvault Systems", 0.003m, "Technology"),
                new IndexComponent("COOP", "Mr. Cooper Group", 0.002m, "Financials"),
                new IndexComponent("PIPR", "Piper Sandler Companies", 0.002m, "Financials"),
                new IndexComponent("SANM", "Sanmina Corporation", 0.002m, "Technology")
            },
            LastUpdated: DateTimeOffset.UtcNow,
            Source: "Built-in"
        );

        // S&P 400 MidCap (sample)
        indices["MID"] = new IndexComponents(
            IndexId: "MID",
            Name: "S&P 400 MidCap",
            Components: new[]
            {
                new IndexComponent("DECK", "Deckers Outdoor Corporation", 0.012m, "Consumer Discretionary"),
                new IndexComponent("TTEK", "Tetra Tech Inc.", 0.008m, "Industrials"),
                new IndexComponent("BWXT", "BWX Technologies", 0.007m, "Industrials"),
                new IndexComponent("RBC", "RBC Bearings", 0.006m, "Industrials"),
                new IndexComponent("LSTR", "Landstar System", 0.006m, "Industrials"),
                new IndexComponent("MTDR", "Matador Resources", 0.005m, "Energy"),
                new IndexComponent("JBL", "Jabil Inc.", 0.005m, "Technology"),
                new IndexComponent("SKX", "Skechers U.S.A.", 0.005m, "Consumer Discretionary"),
                new IndexComponent("GTLS", "Chart Industries", 0.005m, "Industrials"),
                new IndexComponent("DINO", "HF Sinclair Corporation", 0.004m, "Energy")
            },
            LastUpdated: DateTimeOffset.UtcNow,
            Source: "Built-in"
        );

        return indices;
    }
}

/// <summary>
/// Status of index subscription coverage.
/// </summary>
public sealed record IndexSubscriptionStatus(
    string IndexId,
    string? IndexName,
    int TotalComponents,
    int SubscribedCount,
    string[] SubscribedSymbols,
    string[] NotSubscribedSymbols
);
