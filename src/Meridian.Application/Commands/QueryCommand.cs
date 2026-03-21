using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles --query CLI command for quick data queries against stored data.
/// Supports: last, count, summary, symbols queries.
/// </summary>
internal sealed class QueryCommand : ICliCommand
{
    private readonly HistoricalDataQueryService _queryService;
    private readonly ILogger _log;

    public QueryCommand(HistoricalDataQueryService queryService, ILogger log)
    {
        _queryService = queryService;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return args.Any(a => a.Equals("--query", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var queryStr = CliArguments.GetValue(args, "--query");
        if (string.IsNullOrWhiteSpace(queryStr))
        {
            PrintUsage();
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        var parts = queryStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            PrintUsage();
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        var command = parts[0].ToLowerInvariant();
        var symbol = parts.Length > 1 ? parts[1].ToUpperInvariant() : null;

        // Parse optional --from and --to from args (not from query string)
        var fromStr = CliArguments.GetValue(args, "--from");
        var toStr = CliArguments.GetValue(args, "--to");
        DateOnly? from = DateOnly.TryParse(fromStr, out var f) ? f : null;
        DateOnly? to = DateOnly.TryParse(toStr, out var t) ? t : null;

        return command switch
        {
            "last" => await RunLastQuery(symbol, ct),
            "count" => await RunCountQuery(symbol, from, to, ct),
            "summary" => await RunSummaryQuery(symbol, from, to, ct),
            "symbols" or "list" => RunSymbolsList(),
            "range" => RunDateRange(symbol),
            _ => HandleUnknownQuery(command)
        };
    }

    private async Task<CliResult> RunLastQuery(string? symbol, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.Error.WriteLine("  Error: 'last' query requires a symbol");
            Console.Error.WriteLine("  Example: --query \"last SPY\"");
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        var query = new HistoricalDataQuery(
            Symbol: symbol,
            From: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            Limit: 1
        );

        // Read the latest record by reading recent files in reverse
        var result = await _queryService.QueryAsync(
            query with { Limit = 100 }, ct);

        if (!result.Success || result.Records.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  No recent data found for {symbol}");
            Console.WriteLine("  Check that data has been collected for this symbol.");
            Console.WriteLine();
            return CliResult.Ok();
        }

        // Get the most recent record
        var latest = result.Records.OrderByDescending(r => r.Timestamp).First();

        Console.WriteLine();
        Console.WriteLine($"  {symbol} | Last event: {latest.Timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC");
        Console.WriteLine($"  Type: {latest.EventType ?? "unknown"} | Source: {latest.SourceFile}");

        // Try to extract price from raw JSON
        TryPrintPriceInfo(latest.RawJson);

        Console.WriteLine($"  Query: {result.TotalRecords} records in {result.FilesProcessed} files ({result.QueryTimeMs}ms)");
        Console.WriteLine();

        return CliResult.Ok();
    }

    private async Task<CliResult> RunCountQuery(string? symbol, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.Error.WriteLine("  Error: 'count' query requires a symbol");
            Console.Error.WriteLine("  Example: --query \"count SPY\" --from 2026-01-01 --to 2026-01-31");
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        Console.WriteLine();
        Console.WriteLine($"  Counting records for {symbol}...");

        // Count trades
        var tradesResult = await _queryService.QueryAsync(
            new HistoricalDataQuery(symbol, from, to, DataType: "trades"), ct);

        // Count quotes
        var quotesResult = await _queryService.QueryAsync(
            new HistoricalDataQuery(symbol, from, to, DataType: "quotes"), ct);

        // Count bars
        var barsResult = await _queryService.QueryAsync(
            new HistoricalDataQuery(symbol, from, to, DataType: "bars"), ct);

        // Total across all types
        var totalResult = await _queryService.QueryAsync(
            new HistoricalDataQuery(symbol, from, to), ct);

        var dateRange = from.HasValue || to.HasValue
            ? $" ({from?.ToString("yyyy-MM-dd") ?? "start"} to {to?.ToString("yyyy-MM-dd") ?? "now"})"
            : "";

        Console.WriteLine();
        Console.WriteLine($"  {symbol} Record Count{dateRange}");
        Console.WriteLine("  " + new string('-', 50));
        Console.WriteLine($"  Trades:  {tradesResult.TotalRecords:N0} ({tradesResult.TotalFiles} files)");
        Console.WriteLine($"  Quotes:  {quotesResult.TotalRecords:N0} ({quotesResult.TotalFiles} files)");
        Console.WriteLine($"  Bars:    {barsResult.TotalRecords:N0} ({barsResult.TotalFiles} files)");
        Console.WriteLine($"  Total:   {totalResult.TotalRecords:N0} ({totalResult.TotalFiles} files)");
        Console.WriteLine($"  Query time: {totalResult.QueryTimeMs}ms");
        Console.WriteLine();

        return CliResult.Ok();
    }

    private async Task<CliResult> RunSummaryQuery(string? symbol, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.Error.WriteLine("  Error: 'summary' query requires a symbol");
            Console.Error.WriteLine("  Example: --query \"summary SPY\" --from 2026-02-01");
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        var dateRange = _queryService.GetDateRange(symbol);

        if (!dateRange.HasData)
        {
            Console.WriteLine();
            Console.WriteLine($"  No data found for {symbol}");
            Console.WriteLine();
            return CliResult.Ok();
        }

        var result = await _queryService.QueryAsync(
            new HistoricalDataQuery(symbol, from ?? dateRange.EarliestDate, to ?? dateRange.LatestDate), ct);

        // Calculate trading days in range
        var startDate = from ?? dateRange.EarliestDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = to ?? dateRange.LatestDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
        var weekdays = CountWeekdays(startDate, endDate);

        // Count distinct dates in records
        var recordDates = result.Records
            .Select(r => DateOnly.FromDateTime(r.Timestamp.UtcDateTime))
            .Distinct()
            .Count();

        var completeness = weekdays > 0 ? (double)recordDates / weekdays * 100 : 0;

        Console.WriteLine();
        Console.WriteLine($"  {symbol} Summary");
        Console.WriteLine("  " + new string('-', 50));
        Console.WriteLine($"  Date range:     {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} ({totalDays} days)");
        Console.WriteLine($"  Trading days:   {weekdays} (weekdays)");
        Console.WriteLine($"  Days with data: {recordDates}");
        Console.WriteLine($"  Completeness:   {completeness:F1}%");
        Console.WriteLine($"  Total records:  {result.TotalRecords:N0}");
        Console.WriteLine($"  Files:          {result.TotalFiles}");
        Console.WriteLine($"  Query time:     {result.QueryTimeMs}ms");
        Console.WriteLine();

        return CliResult.Ok();
    }

    private CliResult RunSymbolsList()
    {
        var symbols = _queryService.GetAvailableSymbols();

        Console.WriteLine();
        if (symbols.Count == 0)
        {
            Console.WriteLine("  No data found in storage.");
            Console.WriteLine("  Run the collector first to collect market data.");
        }
        else
        {
            Console.WriteLine($"  Symbols with stored data ({symbols.Count}):");
            Console.WriteLine("  " + new string('-', 50));

            foreach (var symbol in symbols)
            {
                var range = _queryService.GetDateRange(symbol);
                if (range.HasData)
                {
                    Console.WriteLine($"  {symbol,-10} {range.EarliestDate:yyyy-MM-dd} to {range.LatestDate:yyyy-MM-dd} ({range.FileCount} files)");
                }
                else
                {
                    Console.WriteLine($"  {symbol,-10} (no date info)");
                }
            }
        }

        Console.WriteLine();
        return CliResult.Ok();
    }

    private CliResult RunDateRange(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.Error.WriteLine("  Error: 'range' query requires a symbol");
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        var range = _queryService.GetDateRange(symbol);

        Console.WriteLine();
        if (!range.HasData)
        {
            Console.WriteLine($"  No data found for {symbol}");
        }
        else
        {
            Console.WriteLine($"  {symbol} Date Range");
            Console.WriteLine("  " + new string('-', 50));
            Console.WriteLine($"  Earliest: {range.EarliestDate:yyyy-MM-dd}");
            Console.WriteLine($"  Latest:   {range.LatestDate:yyyy-MM-dd}");
            Console.WriteLine($"  Files:    {range.FileCount}");
        }

        Console.WriteLine();
        return CliResult.Ok();
    }

    private static CliResult HandleUnknownQuery(string command)
    {
        Console.Error.WriteLine($"  Unknown query command: '{command}'");
        PrintUsage();
        return CliResult.Fail(ErrorCode.ValidationFailed);
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("  Quick Query Usage:");
        Console.WriteLine("  " + new string('-', 50));
        Console.WriteLine("  --query \"last SPY\"            Last known event for symbol");
        Console.WriteLine("  --query \"count SPY\"           Record count by type");
        Console.WriteLine("  --query \"summary SPY\"         Date range and completeness");
        Console.WriteLine("  --query \"symbols\"             List all symbols with data");
        Console.WriteLine("  --query \"range SPY\"           Date range for a symbol");
        Console.WriteLine();
        Console.WriteLine("  Options:");
        Console.WriteLine("    --from 2026-01-01           Start date filter");
        Console.WriteLine("    --to 2026-01-31             End date filter");
        Console.WriteLine();
        Console.WriteLine("  Examples:");
        Console.WriteLine("    dotnet run -- --query \"last SPY\"");
        Console.WriteLine("    dotnet run -- --query \"count AAPL\" --from 2026-01-01 --to 2026-01-31");
        Console.WriteLine("    dotnet run -- --query \"summary SPY\" --from 2026-02-01");
        Console.WriteLine("    dotnet run -- --query \"symbols\"");
        Console.WriteLine();
    }

    private static void TryPrintPriceInfo(string? rawJson)
    {
        if (string.IsNullOrEmpty(rawJson))
            return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("price", out var price))
            {
                Console.WriteLine($"  Price: {price}");
            }
            else if (root.TryGetProperty("last", out var last))
            {
                Console.WriteLine($"  Last: {last}");
            }
            else if (root.TryGetProperty("close", out var close))
            {
                Console.WriteLine($"  Close: {close}");
            }

            if (root.TryGetProperty("volume", out var vol))
            {
                Console.WriteLine($"  Volume: {vol}");
            }
        }
        catch
        {
            // Ignore JSON parse errors - price display is best-effort
        }
    }

    private static int CountWeekdays(DateOnly start, DateOnly end)
    {
        var count = 0;
        var current = start;
        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                count++;
            current = current.AddDays(1);
        }
        return count;
    }
}
