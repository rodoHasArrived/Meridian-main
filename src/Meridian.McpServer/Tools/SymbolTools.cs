using System.Text.Json.Serialization;
using Meridian.Contracts.Configuration;

namespace Meridian.McpServer.Tools;

/// <summary>
/// MCP tools for reading and managing symbol configurations.
/// </summary>
[McpServerToolType]
[ImplementsAdr("ADR-011", "Reads and writes symbol config via centralised ConfigStore")]
public sealed class SymbolTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConfigStore _store;
    private readonly ILogger<SymbolTools> _log;

    /// <summary>
    /// Initialises with the composition-root-supplied <see cref="ConfigStore"/>.
    /// </summary>
    public SymbolTools(ConfigStore store, ILogger<SymbolTools> log)
    {
        _store = store;
        _log = log;
    }

    /// <summary>
    /// Returns all symbols currently configured for market data collection.
    /// </summary>
    [McpServerTool, Description(
        "List all ticker symbols currently configured in appsettings.json for real-time " +
        "collection and backfill, including their depth levels and data-type subscriptions.")]
    public string ListConfiguredSymbols()
    {
        _log.LogInformation("MCP tool {Tool} called", nameof(ListConfiguredSymbols));
        var cfg = _store.Load();
        var symbols = cfg.Symbols ?? [];

        return JsonSerializer.Serialize(new
        {
            count = symbols.Length,
            configPath = _store.ConfigPath,
            symbols = symbols.Select(s => new
            {
                s.Symbol,
                s.DepthLevels,
                SubscribeTrades = s.SubscribeTrades,
                SubscribeDepth = s.SubscribeDepth,
                s.SecurityType,
                s.Exchange,
                s.Currency
            }).ToArray()
        }, JsonOpts);
    }

    /// <summary>
    /// Adds a symbol to the configuration file and returns the updated symbol list.
    /// </summary>
    [McpServerTool, Description(
        "Add a ticker symbol to the collection configuration (appsettings.json). " +
        "The symbol is enabled for trades and depth by default.")]
    public async Task<string> AddSymbol(
        [Description("Ticker symbol to add (e.g. \"AAPL\"). Case is preserved.")]
        string symbol,
        [Description("Number of order-book depth levels to capture. Defaults to 10.")]
        int depthLevels = 10,
        [Description("Whether to subscribe to trade data. Defaults to true.")]
        bool subscribeTrades = true,
        [Description("Whether to subscribe to order-book depth data. Defaults to true.")]
        bool subscribeDepth = true,
        CancellationToken ct = default)
    {
        _log.LogInformation("MCP tool {Tool} called — symbol={Symbol}", nameof(AddSymbol), symbol);

        if (string.IsNullOrWhiteSpace(symbol))
            return JsonSerializer.Serialize(new { error = "Symbol is required." }, JsonOpts);

        symbol = symbol.Trim().ToUpperInvariant();

        var cfg = _store.Load();
        var existing = cfg.Symbols ?? [];

        if (existing.Any(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            return JsonSerializer.Serialize(new { message = $"Symbol '{symbol}' is already configured." }, JsonOpts);

        var newEntry = new SymbolConfig(
            symbol,
            SubscribeTrades: subscribeTrades,
            SubscribeDepth: subscribeDepth,
            DepthLevels: depthLevels);

        var updated = cfg with { Symbols = [.. existing, newEntry] };

        await _store.SaveAsync(updated, ct).ConfigureAwait(false);

        _log.LogInformation("Symbol {Symbol} added to configuration", symbol);
        return JsonSerializer.Serialize(new
        {
            message = $"Symbol '{symbol}' added successfully.",
            totalSymbols = updated.Symbols!.Length
        }, JsonOpts);
    }

    /// <summary>
    /// Removes a symbol from the configuration file.
    /// </summary>
    [McpServerTool, Description(
        "Remove a ticker symbol from the collection configuration (appsettings.json). " +
        "This does not delete any stored data — it only stops future collection for this symbol.")]
    public async Task<string> RemoveSymbol(
        [Description("Ticker symbol to remove (e.g. \"AAPL\"). Case-insensitive.")]
        string symbol,
        CancellationToken ct = default)
    {
        _log.LogInformation("MCP tool {Tool} called — symbol={Symbol}", nameof(RemoveSymbol), symbol);

        if (string.IsNullOrWhiteSpace(symbol))
            return JsonSerializer.Serialize(new { error = "Symbol is required." }, JsonOpts);

        symbol = symbol.Trim().ToUpperInvariant();

        var cfg = _store.Load();
        var existing = cfg.Symbols ?? [];

        if (!existing.Any(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            return JsonSerializer.Serialize(new { message = $"Symbol '{symbol}' was not found in configuration." }, JsonOpts);

        var updated = cfg with
        {
            Symbols = existing
                .Where(s => !string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };

        await _store.SaveAsync(updated, ct).ConfigureAwait(false);

        _log.LogInformation("Symbol {Symbol} removed from configuration", symbol);
        return JsonSerializer.Serialize(new
        {
            message = $"Symbol '{symbol}' removed successfully.",
            totalSymbols = updated.Symbols!.Length
        }, JsonOpts);
    }
}
