using System.Text.Json.Serialization;

namespace Meridian.McpServer.Tools;

/// <summary>
/// MCP tools for running and inspecting historical data backfill operations.
/// </summary>
[McpServerToolType]
[ImplementsAdr("ADR-001", "Wraps BackfillCoordinator for MCP-initiated backfill")]
[ImplementsAdr("ADR-004", "All async methods accept CancellationToken")]
public sealed class BackfillTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly BackfillCoordinator _coordinator;
    private readonly ILogger<BackfillTools> _log;

    /// <summary>
    /// Initialises with the composition-root-supplied <see cref="BackfillCoordinator"/>.
    /// </summary>
    public BackfillTools(BackfillCoordinator coordinator, ILogger<BackfillTools> log)
    {
        _coordinator = coordinator;
        _log = log;
    }

    /// <summary>
    /// Executes a historical data backfill and returns the result as JSON.
    /// </summary>
    [McpServerTool, Description(
        "Run a historical data backfill for one or more symbols. " +
        "Returns a JSON result containing success status, bars written, and any errors. " +
        "Use list_backfill_providers first to discover valid provider names.")]
    public async Task<string> RunBackfill(
        [Description("Comma-separated list of ticker symbols to backfill (e.g. \"SPY,AAPL,MSFT\").")]
        string symbols,
        [Description("Historical data provider to use (e.g. \"stooq\", \"yahoo\", \"tiingo\"). " +
                     "Call list_backfill_providers to see all options.")]
        string provider = "stooq",
        [Description("Inclusive start date in yyyy-MM-dd format. Omit to use provider default.")]
        string? from = null,
        [Description("Inclusive end date in yyyy-MM-dd format. Omit to use today.")]
        string? to = null,
        CancellationToken ct = default)
    {
        _log.LogInformation("MCP tool {Tool} called — provider={Provider} symbols={Symbols}",
            nameof(RunBackfill), provider, symbols);

        var symbolList = symbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (symbolList.Length == 0)
            return JsonSerializer.Serialize(new { error = "At least one symbol is required." }, JsonOpts);

        DateOnly? fromDate = null;
        DateOnly? toDate = null;

        if (from is not null && !DateOnly.TryParse(from, out var parsedFrom))
            return JsonSerializer.Serialize(new { error = $"Invalid 'from' date: '{from}'. Use yyyy-MM-dd." }, JsonOpts);
        else if (from is not null)
            fromDate = DateOnly.Parse(from);

        if (to is not null && !DateOnly.TryParse(to, out var parsedTo))
            return JsonSerializer.Serialize(new { error = $"Invalid 'to' date: '{to}'. Use yyyy-MM-dd." }, JsonOpts);
        else if (to is not null)
            toDate = DateOnly.Parse(to);

        var request = new BackfillRequest(provider, symbolList, fromDate, toDate);

        BackfillResult result;
        try
        {
            result = await _coordinator.RunAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Backfill was cancelled." }, JsonOpts);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Backfill failed for provider={Provider}", provider);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }

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
            DurationSeconds = (result.CompletedUtc - result.StartedUtc).TotalSeconds,
            result.Error
        }, JsonOpts);
    }
}
