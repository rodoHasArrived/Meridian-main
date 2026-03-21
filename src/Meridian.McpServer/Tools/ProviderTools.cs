using System.Text.Json.Serialization;

namespace Meridian.McpServer.Tools;

/// <summary>
/// MCP tools for inspecting and managing market data providers.
/// Exposes provider identity, capabilities, and health status to an LLM.
/// </summary>
[McpServerToolType]
[ImplementsAdr("ADR-001", "Wraps IHistoricalDataProvider and BackfillCoordinator for MCP access")]
public sealed class ProviderTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly BackfillCoordinator _coordinator;
    private readonly ILogger<ProviderTools> _log;

    /// <summary>
    /// Initialises with the composition-root-supplied <see cref="BackfillCoordinator"/>.
    /// </summary>
    public ProviderTools(BackfillCoordinator coordinator, ILogger<ProviderTools> log)
    {
        _coordinator = coordinator;
        _log = log;
    }

    /// <summary>
    /// Returns a JSON array describing every registered historical-data provider
    /// including name, display name, description, priority, and supported capabilities.
    /// </summary>
    [McpServerTool, Description(
        "List all registered historical data providers with their names, descriptions, " +
        "priorities, and capabilities (adjusted prices, dividends, intraday, etc.).")]
    public string ListBackfillProviders()
    {
        _log.LogInformation("MCP tool {Tool} called", nameof(ListBackfillProviders));
        var providers = _coordinator.DescribeProviders();
        return JsonSerializer.Serialize(providers, JsonOpts);
    }

    /// <summary>
    /// Returns the most recent backfill status persisted to disk, or null when no
    /// backfill has been executed yet.
    /// </summary>
    [McpServerTool, Description(
        "Get the result of the most recent backfill run: provider, symbols, date range, " +
        "bars written, success/failure, and error message if applicable.")]
    public string GetLastBackfillResult()
    {
        _log.LogInformation("MCP tool {Tool} called", nameof(GetLastBackfillResult));
        var result = _coordinator.TryReadLast();
        if (result is null)
            return JsonSerializer.Serialize(new { message = "No backfill has been run yet." }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            result.Success,
            result.Provider,
            result.Symbols,
            From = result.From?.ToString("yyyy-MM-dd"),
            To = result.To?.ToString("yyyy-MM-dd"),
            result.BarsWritten,
            StartedUtc = result.StartedUtc.ToString("o"),
            CompletedUtc = result.CompletedUtc.ToString("o"),
            result.Error
        }, JsonOpts);
    }
}
