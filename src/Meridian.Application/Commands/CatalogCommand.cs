using Meridian.Application.ResultTypes;
using Meridian.Storage.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles --catalog CLI command for searching and discovering stored market data.
/// Supports subcommands: search, symbols, timeline, coverage.
/// Usage:
///   --catalog search "AAPL trades 2025"
///   --catalog search --symbol AAPL --type trades --from 2025-01-01 --to 2025-12-31
///   --catalog symbols
///   --catalog coverage
///   --catalog timeline --symbol AAPL
/// </summary>
internal sealed class CatalogCommand : ICliCommand
{
    private readonly IStorageSearchService _searchService;
    private readonly ILogger _log;

    public CatalogCommand(IStorageSearchService searchService, ILogger log)
    {
        _searchService = searchService;
        _log = log;
    }

    public bool CanHandle(string[] args)
        => args.Any(a => a.Equals("--catalog", StringComparison.OrdinalIgnoreCase));

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        // Find the subcommand: first positional arg after --catalog
        var catalogIdx = Array.FindIndex(args, a => a.Equals("--catalog", StringComparison.OrdinalIgnoreCase));
        var subcommand = catalogIdx >= 0 && catalogIdx + 1 < args.Length
            ? args[catalogIdx + 1]
            : string.Empty;

        // If there is no subcommand or it starts with '--', default to "search" with empty query
        if (string.IsNullOrWhiteSpace(subcommand) || subcommand.StartsWith("--"))
        {
            PrintUsage();
            return CliResult.Ok();
        }

        return subcommand.ToLowerInvariant() switch
        {
            "search" => await RunSearchAsync(args, catalogIdx + 2, ct),
            "symbols" => await RunSymbolsAsync(ct),
            "coverage" => await RunCoverageAsync(ct),
            "timeline" => await RunTimelineAsync(args, ct),
            _ => await RunSearchAsync(args, catalogIdx + 1, ct) // treat unknown token as a query string
        };
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private async Task<CliResult> RunSearchAsync(string[] args, int queryStartIdx, CancellationToken ct)
    {
        // Collect natural-language query tokens until the next flag
        var queryTokens = new List<string>();
        for (var i = queryStartIdx; i < args.Length && !args[i].StartsWith("--"); i++)
            queryTokens.Add(args[i]);

        var naturalQuery = string.Join(" ", queryTokens);

        // Parse structured overrides
        var symbolArg = CliArguments.GetValue(args, "--symbol");
        var typeArg = CliArguments.GetValue(args, "--type");
        var fromArg = CliArguments.GetValue(args, "--from");
        var toArg = CliArguments.GetValue(args, "--to");
        var take = CliArguments.GetInt(args, "--take", 20);

        // Start from the natural-language parse result if available
        StorageQuery? parsed = null;
        if (!string.IsNullOrWhiteSpace(naturalQuery))
        {
            parsed = _searchService.ParseNaturalLanguageQuery(naturalQuery);
            if (parsed != null)
                Console.WriteLine($"  Interpreted: {DescribeQuery(parsed)}");
        }

        // Explicit flags override NL results
        string[]? symbols = !string.IsNullOrWhiteSpace(symbolArg)
            ? symbolArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : parsed?.Symbols;

        MarketEventType[]? types = ParseTypeArg(typeArg) ?? parsed?.Types;

        DateTimeOffset? from = DateTimeOffset.TryParse(fromArg, out var fd) ? fd : parsed?.From;

        DateTimeOffset? to = DateTimeOffset.TryParse(toArg, out var td) ? td.AddDays(1) : parsed?.To;

        var query = new FileSearchQuery(
            Symbols: symbols,
            Types: types,
            From: from,
            To: to,
            Skip: 0,
            Take: take,
            Descending: true
        );

        try
        {
            var result = await _searchService.SearchFilesAsync(query, ct);
            PrintSearchResults(result, take);
            return CliResult.Ok();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Catalog search failed");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            return CliResult.Fail(ErrorCode.StorageError);
        }
    }

    // ── Symbols ───────────────────────────────────────────────────────────────

    private async Task<CliResult> RunSymbolsAsync(CancellationToken ct)
    {
        try
        {
            var catalog = await _searchService.DiscoverAsync(new DiscoveryQuery(), ct);

            Console.WriteLine();
            Console.WriteLine($"  Data Catalog — {catalog.Symbols.Count} symbol(s) found");
            Console.WriteLine($"  Root: {catalog.RootPath}");
            Console.WriteLine();

            if (catalog.Symbols.Count == 0)
            {
                Console.WriteLine("  No stored data found. Run a backfill or start collection first.");
                Console.WriteLine();
                return CliResult.Ok();
            }

            Console.WriteLine($"  {"Symbol",-10}  {"First Date",-12}  {"Last Date",-12}  {"Events",12}  Types");
            Console.WriteLine($"  {new string('-', 10)}  {new string('-', 12)}  {new string('-', 12)}  {new string('-', 12)}  -----");

            foreach (var s in catalog.Symbols)
            {
                var types = string.Join(", ", s.EventTypes);
                Console.WriteLine($"  {s.Symbol,-10}  {s.FirstDate:yyyy-MM-dd}    {s.LastDate:yyyy-MM-dd}    {s.TotalEvents,12:N0}  {types}");
            }

            Console.WriteLine();
            Console.WriteLine($"  Total events: {catalog.TotalEvents:N0}  |  Storage: {FormatBytes(catalog.TotalBytes)}");
            Console.WriteLine();
            return CliResult.Ok();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Catalog symbols listing failed");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            return CliResult.Fail(ErrorCode.StorageError);
        }
    }

    // ── Coverage ──────────────────────────────────────────────────────────────

    private async Task<CliResult> RunCoverageAsync(CancellationToken ct)
    {
        try
        {
            var catalog = await _searchService.DiscoverAsync(new DiscoveryQuery(), ct);

            Console.WriteLine();
            Console.WriteLine("  Data Coverage Summary");
            Console.WriteLine($"  Generated:    {catalog.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"  Root:         {catalog.RootPath}");
            Console.WriteLine($"  Symbols:      {catalog.Symbols.Count}");
            Console.WriteLine($"  Total events: {catalog.TotalEvents:N0}");
            Console.WriteLine($"  Total bytes:  {FormatBytes(catalog.TotalBytes)}");

            if (catalog.DateRange.Start != DateTimeOffset.MaxValue)
            {
                Console.WriteLine($"  Date range:   {catalog.DateRange.Start:yyyy-MM-dd} → {catalog.DateRange.End:yyyy-MM-dd}");
            }

            if (catalog.EventTypes.Count > 0)
                Console.WriteLine($"  Event types:  {string.Join(", ", catalog.EventTypes)}");

            if (catalog.Sources.Count > 0)
                Console.WriteLine($"  Sources:      {string.Join(", ", catalog.Sources)}");

            Console.WriteLine();
            return CliResult.Ok();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Catalog coverage summary failed");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            return CliResult.Fail(ErrorCode.StorageError);
        }
    }

    // ── Timeline ──────────────────────────────────────────────────────────────

    private async Task<CliResult> RunTimelineAsync(string[] args, CancellationToken ct)
    {
        var symbolArg = CliArguments.GetValue(args, "--symbol");
        var fromArg = CliArguments.GetValue(args, "--from");
        var toArg = CliArguments.GetValue(args, "--to");

        string[]? symbols = !string.IsNullOrWhiteSpace(symbolArg)
            ? symbolArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        DateTimeOffset? from = DateTimeOffset.TryParse(fromArg, out var fd) ? fd : null;
        DateTimeOffset? to = DateTimeOffset.TryParse(toArg, out var td) ? td.AddDays(1) : null;

        var query = new FileSearchQuery(
            Symbols: symbols,
            From: from,
            To: to,
            Skip: 0,
            Take: 10_000,
            Descending: false
        );

        try
        {
            var result = await _searchService.SearchFilesAsync(query, ct);

            if (result.TotalMatches == 0)
            {
                Console.WriteLine();
                Console.WriteLine("  No data found for the given filters.");
                Console.WriteLine();
                return CliResult.Ok();
            }

            var grouped = result.Results
                .GroupBy(f => f.Symbol)
                .OrderBy(g => g.Key);

            Console.WriteLine();
            Console.WriteLine("  Data Timeline (Gantt view)");
            Console.WriteLine();

            foreach (var symbolGroup in grouped)
            {
                var sym = symbolGroup.Key;
                var days = symbolGroup
                    .GroupBy(f => f.Date.ToString("yyyy-MM-dd"))
                    .OrderBy(g => g.Key)
                    .ToList();

                var first = days[0].Key;
                var last = days[^1].Key;
                var coverage = days.Count;
                var types = symbolGroup.Select(f => f.EventType).Distinct().OrderBy(t => t);

                Console.WriteLine($"  {sym,-10}  {first} → {last}  ({coverage} day(s))  [{string.Join(", ", types)}]");

                // Print a simple ASCII bar showing coverage gaps
                PrintCoverageBar(days.Select(d => d.Key).ToList(), first, last);
            }

            Console.WriteLine();
            return CliResult.Ok();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Catalog timeline failed");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            return CliResult.Fail(ErrorCode.StorageError);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PrintSearchResults(SearchResult<FileSearchResult> result, int take)
    {
        Console.WriteLine();
        Console.WriteLine($"  Found {result.TotalMatches} file(s) matching your query" +
            (result.TotalMatches > take ? $" (showing first {take})" : string.Empty));
        Console.WriteLine();

        if (result.Results.Count == 0)
        {
            Console.WriteLine("  No results. Try broadening the date range or removing type filters.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  {"Symbol",-10}  {"Date",-12}  {"Type",-14}  {"Events",10}  {"Size",10}  Source");
        Console.WriteLine($"  {new string('-', 10)}  {new string('-', 12)}  {new string('-', 14)}  {new string('-', 10)}  {new string('-', 10)}  ------");

        foreach (var f in result.Results)
        {
            Console.WriteLine($"  {f.Symbol,-10}  {f.Date:yyyy-MM-dd}    {f.EventType,-14}  {f.EventCount,10:N0}  {FormatBytes(f.SizeBytes),10}  {f.Source}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Renders a compact ASCII bar (max 60 chars) showing which days have data vs gaps.
    /// ■ = data present, · = gap.
    /// </summary>
    private static void PrintCoverageBar(IReadOnlyList<string> coveredDays, string firstDay, string lastDay)
    {
        if (!DateOnly.TryParse(firstDay, out var start) || !DateOnly.TryParse(lastDay, out var end))
            return;

        var totalDays = (end.DayNumber - start.DayNumber) + 1;
        if (totalDays <= 0)
            return;

        const int barWidth = 60;
        var daysPerChar = Math.Max(1.0, totalDays / (double)barWidth);
        var bar = new char[Math.Min(totalDays, barWidth)];
        var coveredSet = new HashSet<string>(coveredDays);

        for (var i = 0; i < bar.Length; i++)
        {
            var dayOffset = (int)(i * daysPerChar);
            var date = start.AddDays(dayOffset).ToString("yyyy-MM-dd");
            bar[i] = coveredSet.Contains(date) ? '■' : '·';
        }

        Console.WriteLine($"  {new string(' ', 12)}  [{new string(bar)}]");
    }

    private static string DescribeQuery(StorageQuery q)
    {
        var parts = new List<string>();
        if (q.Symbols?.Length > 0)
            parts.Add($"symbols={string.Join(",", q.Symbols)}");
        if (q.Types?.Length > 0)
            parts.Add($"types={string.Join(",", q.Types)}");
        if (q.From.HasValue)
            parts.Add($"from={q.From.Value:yyyy-MM-dd}");
        if (q.To.HasValue)
            parts.Add($"to={q.To.Value:yyyy-MM-dd}");
        return parts.Count > 0 ? string.Join(", ", parts) : "(no filters)";
    }

    private static MarketEventType[]? ParseTypeArg(string? typeArg)
    {
        if (string.IsNullOrWhiteSpace(typeArg))
            return null;
        var mapped = typeArg
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant() switch
            {
                "trade" or "trades" => (MarketEventType?)MarketEventType.Trade,
                "quote" or "quotes" or "bbo" => MarketEventType.BboQuote,
                "depth" or "l2" or "snapshot" => MarketEventType.L2Snapshot,
                "bar" or "bars" => MarketEventType.AggregateBar,
                _ => (MarketEventType?)null
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToArray();
        return mapped.Length > 0 ? mapped : null;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024:F1} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("  Usage: --catalog <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("  Subcommands:");
        Console.WriteLine("    search <query>   Natural-language or structured search over stored data");
        Console.WriteLine("    symbols          List all symbols with stored data");
        Console.WriteLine("    coverage         Show aggregate coverage summary");
        Console.WriteLine("    timeline         Show per-day coverage bars (Gantt view)");
        Console.WriteLine();
        Console.WriteLine("  Search examples:");
        Console.WriteLine("    --catalog search \"AAPL trades 2025\"");
        Console.WriteLine("    --catalog search --symbol AAPL --type trades --from 2025-01-01");
        Console.WriteLine("    --catalog search --symbol SPY,AAPL --from 2025-01-01 --to 2025-06-30");
        Console.WriteLine();
        Console.WriteLine("  Timeline examples:");
        Console.WriteLine("    --catalog timeline");
        Console.WriteLine("    --catalog timeline --symbol AAPL");
        Console.WriteLine("    --catalog timeline --symbol AAPL --from 2025-01-01");
        Console.WriteLine();
    }
}
