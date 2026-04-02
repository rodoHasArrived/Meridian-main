
namespace Meridian.Ui.Services;

/// <summary>
/// Service for global search functionality across the application.
/// Searches symbols, providers, settings, and help content.
/// </summary>
public sealed class SearchService
{
    private static readonly Lazy<SearchService> _instance = new(() => new SearchService());
    private readonly ConfigService _configService;
    private readonly WatchlistService _watchlistService;

    /// <summary>
    /// Gets the singleton instance of the SearchService.
    /// </summary>
    public static SearchService Instance => _instance.Value;

    private SearchService()
    {
        _configService = new ConfigService();
        _watchlistService = WatchlistService.Instance;
    }

    /// <summary>
    /// Performs a global search across all searchable content.
    /// </summary>
    public async Task<SearchResults> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResults();
        }

        options ??= new SearchOptions();
        var normalizedQuery = query.Trim().ToUpperInvariant();
        var results = new SearchResults { Query = query };

        // Search in parallel for performance
        var tasks = new List<Task>();

        if (options.SearchSymbols)
        {
            tasks.Add(SearchSymbolsAsync(normalizedQuery, results));
        }

        if (options.SearchProviders)
        {
            SearchProviders(normalizedQuery, results);
        }

        if (options.SearchPages)
        {
            SearchPages(normalizedQuery, results);
        }

        if (options.SearchActions)
        {
            SearchActions(normalizedQuery, results);
        }

        if (options.SearchHelp)
        {
            SearchHelp(normalizedQuery, results);
        }

        await Task.WhenAll(tasks);

        return results;
    }

    /// <summary>
    /// Gets search suggestions for autocomplete.
    /// </summary>
    public async Task<List<SearchSuggestion>> GetSuggestionsAsync(string query, CancellationToken ct = default)
    {
        var suggestions = new List<SearchSuggestion>();
        if (string.IsNullOrWhiteSpace(query))
            return suggestions;

        var normalizedQuery = query.Trim().ToUpperInvariant();

        // Symbol suggestions
        var config = await _configService.LoadConfigAsync();
        if (config?.Symbols != null)
        {
            foreach (var symbol in config.Symbols.Where(s => s.Symbol?.ToUpperInvariant().Contains(normalizedQuery) == true))
            {
                suggestions.Add(new SearchSuggestion
                {
                    Text = symbol.Symbol ?? string.Empty,
                    Category = "Symbol",
                    Icon = "\uE9D9",
                    NavigationTarget = $"symbol:{symbol.Symbol}"
                });
            }
        }

        // Watchlist suggestions
        var watchlist = await _watchlistService.LoadWatchlistAsync(ct);
        foreach (var item in watchlist.Symbols.Where(s => s.Symbol.Contains(normalizedQuery)))
        {
            if (!suggestions.Any(s => s.Text == item.Symbol))
            {
                suggestions.Add(new SearchSuggestion
                {
                    Text = item.Symbol,
                    Category = "Watchlist",
                    Icon = "\uE728",
                    NavigationTarget = $"symbol:{item.Symbol}"
                });
            }
        }

        // Popular symbol suggestions
        var popularSymbols = new[] { "SPY", "QQQ", "AAPL", "MSFT", "NVDA", "TSLA", "AMZN", "META", "GOOG", "GOOGL" };
        foreach (var symbol in popularSymbols.Where(s => s.Contains(normalizedQuery)))
        {
            if (!suggestions.Any(s => s.Text == symbol))
            {
                suggestions.Add(new SearchSuggestion
                {
                    Text = symbol,
                    Category = "Popular Symbol",
                    Icon = "\uE8D6",
                    NavigationTarget = $"symbol:{symbol}"
                });
            }
        }

        // Page suggestions
        var pages = GetNavigationPages();
        foreach (var page in pages.Where(p => p.Name.ToUpperInvariant().Contains(normalizedQuery) ||
                                               p.Keywords.Any(k => k.ToUpperInvariant().Contains(normalizedQuery))))
        {
            suggestions.Add(new SearchSuggestion
            {
                Text = page.Name,
                Category = "Page",
                Icon = page.Icon,
                NavigationTarget = $"page:{page.Tag}"
            });
        }

        return suggestions.Take(10).ToList();
    }

    private async Task SearchSymbolsAsync(string query, SearchResults results, CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync();

        // Search configured symbols
        if (config?.Symbols != null)
        {
            foreach (var symbol in config.Symbols)
            {
                if (MatchesQuery(symbol.Symbol, query) ||
                    MatchesQuery(symbol.Exchange, query) ||
                    MatchesQuery(symbol.LocalSymbol, query))
                {
                    results.Symbols.Add(new SearchResultItem
                    {
                        Title = symbol.Symbol ?? string.Empty,
                        Description = $"{symbol.Exchange} - {GetSubscriptionDescription(symbol)}",
                        Icon = "\uE9D9",
                        NavigationTarget = $"symbol:{symbol.Symbol}",
                        Category = "Configured Symbol"
                    });
                }
            }
        }

        // Search watchlist
        var watchlist = await _watchlistService.LoadWatchlistAsync(ct);
        foreach (var item in watchlist.Symbols)
        {
            if (MatchesQuery(item.Symbol, query) || MatchesQuery(item.Notes, query))
            {
                if (!results.Symbols.Any(s => s.Title == item.Symbol))
                {
                    results.Symbols.Add(new SearchResultItem
                    {
                        Title = item.Symbol,
                        Description = item.Notes ?? "Watchlist symbol",
                        Icon = "\uE728", // Default icon (IsFavorite not available)
                        NavigationTarget = $"symbol:{item.Symbol}",
                        Category = "Watchlist"
                    });
                }
            }
        }
    }

    private void SearchProviders(string query, SearchResults results)
    {
        var providers = new[]
        {
            ("Interactive Brokers", "IB", "Professional-grade L2 depth and tick-by-tick data via TWS/Gateway", "\uE912"),
            ("Alpaca Markets", "Alpaca", "Commission-free trading API with real-time WebSocket streaming", "\uE8AB"),
            ("Polygon.io", "Polygon", "Comprehensive market data API with trades, quotes, and aggregates", "\uE9D9"),
            ("Yahoo Finance", "Yahoo", "Free historical daily bar data", "\uE787"),
            ("Stooq", "Stooq", "Free historical data provider", "\uE787"),
            ("Tiingo", "Tiingo", "Historical and real-time market data with free tier", "\uE787"),
            ("Alpha Vantage", "AlphaVantage", "Free market data API with rate limits", "\uE787"),
            ("Finnhub", "Finnhub", "Real-time market data and fundamentals", "\uE787"),
            ("Nasdaq Data Link", "NasdaqDataLink", "Premium historical data", "\uE787")
        };

        foreach (var (name, tag, description, icon) in providers)
        {
            if (MatchesQuery(name, query) || MatchesQuery(tag, query))
            {
                results.Providers.Add(new SearchResultItem
                {
                    Title = name,
                    Description = description,
                    Icon = icon,
                    NavigationTarget = $"provider:{tag}",
                    Category = "Data Provider"
                });
            }
        }
    }

    private void SearchPages(string query, SearchResults results)
    {
        var pages = GetNavigationPages();

        foreach (var page in pages)
        {
            if (MatchesQuery(page.Name, query) ||
                page.Keywords.Any(k => MatchesQuery(k, query)))
            {
                results.Pages.Add(new SearchResultItem
                {
                    Title = page.Name,
                    Description = page.Description,
                    Icon = page.Icon,
                    NavigationTarget = $"page:{page.Tag}",
                    Category = "Page"
                });
            }
        }
    }

    private void SearchActions(string query, SearchResults results)
    {
        var actions = new[]
        {
            ("Start Collector", "Begin collecting real-time market data", "\uE768", "action:start"),
            ("Stop Collector", "Stop the data collector", "\uE71A", "action:stop"),
            ("Run Backfill", "Download historical data", "\uE787", "action:backfill"),
            ("Add Symbol", "Add a new symbol to track", "\uE710", "action:addsymbol"),
            ("Export Data", "Export your market data", "\uEDE1", "action:export"),
            ("Verify Archives", "Check archive integrity", "\uE9F5", "action:verify"),
            ("View Logs", "Open the service logs", "\uE756", "action:logs"),
            ("Settings", "Open application settings", "\uE713", "action:settings"),
            ("Refresh Status", "Refresh connection status", "\uE72C", "action:refresh")
        };

        foreach (var (name, description, icon, target) in actions)
        {
            if (MatchesQuery(name, query))
            {
                results.Actions.Add(new SearchResultItem
                {
                    Title = name,
                    Description = description,
                    Icon = icon,
                    NavigationTarget = target,
                    Category = "Action"
                });
            }
        }
    }

    private void SearchHelp(string query, SearchResults results)
    {
        var helpTopics = new[]
        {
            ("Getting Started", "How to set up and start using Meridian", "\uE897", "help:getting-started",
                new[] { "setup", "start", "begin", "introduction", "tutorial" }),
            ("Provider Setup", "How to configure data providers", "\uE703", "help:providers",
                new[] { "api", "key", "credentials", "connect", "configure" }),
            ("Historical Backfill", "How to download historical data", "\uE787", "help:backfill",
                new[] { "historical", "download", "history", "past" }),
            ("Data Storage", "Understanding data storage and organization", "\uE8B7", "help:storage",
                new[] { "files", "compression", "archive", "disk" }),
            ("Data Quality", "Monitoring and maintaining data quality", "\uE9F5", "help:quality",
                new[] { "integrity", "gaps", "verification", "health" }),
            ("Export Options", "Exporting data to different formats", "\uEDE1", "help:export",
                new[] { "csv", "parquet", "json", "download" }),
            ("Troubleshooting", "Common issues and solutions", "\uE90F", "help:troubleshooting",
                new[] { "error", "problem", "fix", "issue", "help" }),
            ("Keyboard Shortcuts", "Available keyboard shortcuts", "\uE765", "help:shortcuts",
                new[] { "keys", "hotkeys", "commands" })
        };

        foreach (var (title, description, icon, target, keywords) in helpTopics)
        {
            if (MatchesQuery(title, query) || keywords.Any(k => MatchesQuery(k, query)))
            {
                results.Help.Add(new SearchResultItem
                {
                    Title = title,
                    Description = description,
                    Icon = icon,
                    NavigationTarget = target,
                    Category = "Help"
                });
            }
        }
    }

    private static List<NavigationPage> GetNavigationPages()
    {
        return new List<NavigationPage>
        {
            new("Dashboard", "Dashboard", "\uE80F", "Main dashboard with metrics and status", new[] { "home", "main", "overview", "status" }),
            new("Watchlist", "Watchlist", "\uE728", "Your favorite symbols", new[] { "favorites", "tracked", "symbols" }),
            new("Data Provider", "Provider", "\uE703", "Configure data providers", new[] { "source", "api", "connection" }),
            new("Data Sources", "DataSources", "\uE943", "Manage multiple data sources", new[] { "sources", "providers" }),
            new("Storage", "Storage", "\uE8B7", "Storage configuration", new[] { "files", "disk", "compression" }),
            new("Symbols", "Symbols", "\uE9D9", "Manage symbols", new[] { "stocks", "tickers", "securities" }),
            new("Backfill", "Backfill", "\uE787", "Historical data download", new[] { "historical", "download", "history" }),
            new("Collection Sessions", "CollectionSessions", "\uE8F1", "Session management", new[] { "sessions", "runs" }),
            new("Archive Health", "ArchiveHealth", "\uE9F5", "Archive verification", new[] { "health", "integrity", "verify" }),
            new("Service Manager", "ServiceManager", "\uE912", "Service monitoring", new[] { "services", "logs", "status" }),
            new("Data Export", "DataExport", "\uEDE1", "Export data", new[] { "export", "download", "csv", "parquet" }),
            new("Trading Hours", "TradingHours", "\uE823", "Market hours", new[] { "hours", "schedule", "time" }),
            new("Settings", "Settings", "\uE713", "Application settings", new[] { "preferences", "options", "config" }),
            new("Help", "Help", "\uE897", "Help and documentation", new[] { "help", "docs", "faq" })
        };
    }

    private static bool MatchesQuery(string? text, string query)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.ToUpperInvariant().Contains(query);
    }

    private static string GetSubscriptionDescription(SymbolConfig symbol)
    {
        var parts = new List<string>();
        if (symbol.SubscribeTrades)
            parts.Add("Trades");
        if (symbol.SubscribeDepth)
            parts.Add($"Depth L{symbol.DepthLevels}");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// Options for search behavior.
/// </summary>
public sealed class SearchOptions
{
    public bool SearchSymbols { get; set; } = true;
    public bool SearchProviders { get; set; } = true;
    public bool SearchPages { get; set; } = true;
    public bool SearchActions { get; set; } = true;
    public bool SearchHelp { get; set; } = true;
    public int MaxResults { get; set; } = 20;
}

/// <summary>
/// Search results container.
/// </summary>
public sealed class SearchResults
{
    public string Query { get; set; } = string.Empty;
    public List<SearchResultItem> Symbols { get; } = new();
    public List<SearchResultItem> Providers { get; } = new();
    public List<SearchResultItem> Pages { get; } = new();
    public List<SearchResultItem> Actions { get; } = new();
    public List<SearchResultItem> Help { get; } = new();

    public int TotalCount => Symbols.Count + Providers.Count + Pages.Count + Actions.Count + Help.Count;

    public IEnumerable<SearchResultItem> AllResults =>
        Symbols.Concat(Providers).Concat(Pages).Concat(Actions).Concat(Help);
}

/// <summary>
/// Individual search result.
/// </summary>
public sealed class SearchResultItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE721";
    public string NavigationTarget { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// Search suggestion for autocomplete.
/// </summary>
public sealed class SearchSuggestion
{
    public string Text { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE721";
    public string NavigationTarget { get; set; } = string.Empty;
}

/// <summary>
/// Navigation page info for search.
/// </summary>
public sealed class NavigationPage
{
    public string Name { get; }
    public string Tag { get; }
    public string Icon { get; }
    public string Description { get; }
    public string[] Keywords { get; }

    public NavigationPage(string name, string tag, string icon, string description, string[] keywords)
    {
        Name = name;
        Tag = tag;
        Icon = icon;
        Description = description;
        Keywords = keywords;
    }
}
