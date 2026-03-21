using System.Text.Json.Serialization;
using Meridian.Application.Services;
using Meridian.Contracts.Catalog;

namespace Meridian.McpServer.Tools;

/// <summary>
/// MCP tools for inspecting the storage catalog and querying stored market data.
/// </summary>
[McpServerToolType]
[ImplementsAdr("ADR-002", "Exposes tiered storage catalog via MCP")]
[ImplementsAdr("ADR-004", "All async methods accept CancellationToken")]
public sealed class StorageTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IStorageCatalogService _catalog;
    private readonly HistoricalDataQueryService _query;
    private readonly ILogger<StorageTools> _log;

    /// <summary>
    /// Initialises with DI-supplied storage services.
    /// </summary>
    public StorageTools(
        IStorageCatalogService catalog,
        HistoricalDataQueryService query,
        ILogger<StorageTools> log)
    {
        _catalog = catalog;
        _query = query;
        _log = log;
    }

    /// <summary>
    /// Returns high-level statistics about the local storage catalog.
    /// </summary>
    [McpServerTool, Description(
        "Get statistics about the local storage catalog: total files, total size, " +
        "unique symbols, date range, and event type breakdown.")]
    public string GetStorageStats()
    {
        _log.LogInformation("MCP tool {Tool} called", nameof(GetStorageStats));
        var stats = _catalog.GetStatistics();
        return JsonSerializer.Serialize(stats, JsonOpts);
    }

    /// <summary>
    /// Returns the list of symbols that have data stored locally.
    /// </summary>
    [McpServerTool, Description(
        "List all ticker symbols that have data stored in the local storage catalog, " +
        "along with the number of data files and the covered date range for each symbol.")]
    public string ListStoredSymbols()
    {
        _log.LogInformation("MCP tool {Tool} called", nameof(ListStoredSymbols));
        var catalog = _catalog.GetCatalog();

        var symbols = catalog.Symbols
            .Values
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

        return JsonSerializer.Serialize(new { count = symbols.Length, symbols }, JsonOpts);
    }

    /// <summary>
    /// Queries stored data records for a symbol.
    /// </summary>
    [McpServerTool, Description(
        "Query stored historical data records for a symbol within a date range. " +
        "Returns an array of raw JSON records from the storage files.")]
    public async Task<string> QueryStoredData(
        [Description("Ticker symbol to query (e.g. \"SPY\").")]
        string symbol,
        [Description("Start date in yyyy-MM-dd format. Omit to return all available data.")]
        string? from = null,
        [Description("End date in yyyy-MM-dd format. Omit to use today.")]
        string? to = null,
        [Description("Data type filter: \"bars\", \"trades\", \"quotes\". Omit for all types.")]
        string? dataType = null,
        [Description("Maximum number of records to return. Defaults to 50.")]
        int limit = 50,
        CancellationToken ct = default)
    {
        _log.LogInformation("MCP tool {Tool} called — symbol={Symbol}", nameof(QueryStoredData), symbol);

        if (string.IsNullOrWhiteSpace(symbol))
            return JsonSerializer.Serialize(new { error = "Symbol is required." }, JsonOpts);

        limit = Math.Clamp(limit, 1, 500);

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

        var queryReq = new HistoricalDataQuery(
            symbol.Trim().ToUpperInvariant(),
            fromDate,
            toDate,
            DataType: dataType,
            Limit: limit);

        HistoricalDataQueryResult result;
        try
        {
            result = await _query.QueryAsync(queryReq, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Query was cancelled." }, JsonOpts);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Storage query failed for symbol={Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }

        var records = result.Records
            .Select(r => new
            {
                r.Symbol,
                Timestamp = r.Timestamp.ToString("o"),
                r.EventType,
                r.SourceFile
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            symbol = result.Symbol,
            from = result.From?.ToString("yyyy-MM-dd"),
            to = result.To?.ToString("yyyy-MM-dd"),
            count = records.Length,
            totalAvailable = result.TotalRecords,
            filesProcessed = result.FilesProcessed,
            queryTimeMs = result.QueryTimeMs,
            result.Message,
            records
        }, JsonOpts);
    }
}

