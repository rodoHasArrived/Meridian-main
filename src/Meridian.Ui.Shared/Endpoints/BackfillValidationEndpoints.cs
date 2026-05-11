using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering backfill validation API endpoints.
/// Provides gap detection and completeness analysis for historical data.
/// </summary>
public static class BackfillValidationEndpoints
{
    public static void MapBackfillValidationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Backfill Validation");

        // GET /api/backfill/validation — comprehensive validation report for all symbols
        group.MapGet(UiApiRoutes.BackfillValidation, async (
            ConfigStore store,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();

            if (searchService is null)
            {
                return Results.Json(new BackfillCompletenessSummary(
                    TotalSymbols: symbols.Length,
                    CompleteSymbols: 0,
                    IncompleteSymbols: symbols.Length
                ), jsonOptions);
            }

            try
            {
                var results = new List<BackfillValidationResult>();

                foreach (var symbol in symbols.Select(s => s.Symbol).Distinct())
                {
                    var searchResult = await searchService.SearchFilesAsync(
                        new FileSearchQuery(Symbols: [symbol], Take: 10_000),
                        ct);

                    if (searchResult.TotalMatches == 0)
                    {
                        results.Add(new BackfillValidationResult(
                            Symbol: symbol,
                            IsComplete: false,
                            TotalDays: 0,
                            CoveredDays: 0,
                            Completeness: 0.0,
                            Status: "No data"
                        ));
                        continue;
                    }

                    var tradingDates = GetTradingDates(searchResult.Results);
                    var completeness = CalculateSymbolCompleteness(searchResult.Results);
                    var gaps = DetectSymbolGaps(searchResult.Results, symbol);
                    var totalDays = tradingDates.Count == 0
                        ? 0
                        : CalculateExpectedTradingDays(tradingDates.First(), tradingDates.Last());
                    DateTime? firstDataPoint = tradingDates.Count == 0 ? null : tradingDates.First();
                    DateTime? lastDataPoint = tradingDates.Count == 0 ? null : tradingDates.Last();

                    results.Add(new BackfillValidationResult(
                        Symbol: symbol,
                        IsComplete: completeness >= 0.95,
                        TotalDays: totalDays,
                        CoveredDays: tradingDates.Count,
                        Completeness: completeness,
                        Gaps: gaps.Select(g => g.ToString()).ToArray(),
                        FirstDataPoint: firstDataPoint,
                        LastDataPoint: lastDataPoint,
                        Status: completeness >= 0.95 ? "Complete" : (completeness >= 0.80 ? "Good" : "Incomplete")
                    ));
                }

                var completeCount = results.Count(r => r.IsComplete);
                var avgCompleteness = results.Count == 0 ? 0.0 : results.Average(r => r.Completeness);

                return Results.Json(new BackfillCompletenessSummary(
                    TotalSymbols: symbols.Length,
                    CompleteSymbols: completeCount,
                    IncompleteSymbols: symbols.Length - completeCount,
                    AverageCompleteness: avgCompleteness,
                    Symbols: results.ToArray()
                ), jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Backfill validation failed: {ex.Message}");
            }
        })
        .WithName("GetBackfillValidation")
        .WithDescription("Returns comprehensive backfill validation report for all configured symbols.")
        .Produces<BackfillCompletenessSummary>(200);

        // GET /api/backfill/validation/{symbol} — validation report for one symbol
        group.MapGet(UiApiRoutes.BackfillValidationBySymbol, async (
            string symbol,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
            {
                return Results.Json(new BackfillValidationResult(
                    Symbol: symbol,
                    IsComplete: false,
                    Status: "Storage service unavailable"
                ), jsonOptions);
            }

            try
            {
                var query = new FileSearchQuery(Symbols: new[] { symbol }, Take: 1000);
                var result = await searchService.SearchFilesAsync(query, ct);

                if (result.TotalMatches == 0)
                {
                    return Results.Json(new BackfillValidationResult(
                        Symbol: symbol,
                        IsComplete: false,
                        Status: "No data found"
                    ), jsonOptions);
                }

                var completeness = CalculateSymbolCompleteness(result.Results);
                var gaps = DetectSymbolGaps(result.Results);
                var tradingDates = GetTradingDates(result.Results);
                var totalDays = tradingDates.Count == 0
                    ? 0
                    : CalculateExpectedTradingDays(tradingDates.First(), tradingDates.Last());
                DateTime? firstDataPoint = tradingDates.Count == 0 ? null : tradingDates.First();
                DateTime? lastDataPoint = tradingDates.Count == 0 ? null : tradingDates.Last();

                return Results.Json(new BackfillValidationResult(
                    Symbol: symbol,
                    IsComplete: completeness >= 0.95,
                    TotalDays: totalDays,
                    CoveredDays: tradingDates.Count,
                    Completeness: completeness,
                    Gaps: gaps.Select(g => $"{g.StartDate:yyyy-MM-dd} to {g.EndDate:yyyy-MM-dd}").ToArray(),
                    FirstDataPoint: firstDataPoint,
                    LastDataPoint: lastDataPoint,
                    Status: completeness >= 0.95 ? "Complete" : (completeness >= 0.80 ? "Good" : "Incomplete")
                ), jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Backfill validation for {symbol} failed: {ex.Message}");
            }
        })
        .WithName("GetBackfillValidationBySymbol")
        .WithDescription("Returns backfill validation report for a specific symbol.")
        .Produces<BackfillValidationResult>(200);

        // GET /api/backfill/gaps — gap detection across all symbols
        group.MapGet(UiApiRoutes.BackfillGapDetection, async (
            ConfigStore store,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();
            var allGaps = new List<BackfillGapInfo>();

            if (searchService is not null)
            {
                try
                {
                    foreach (var symbol in symbols.Select(s => s.Symbol))
                    {
                        var result = await searchService.SearchFilesAsync(
                            new FileSearchQuery(Symbols: [symbol], Take: 10_000),
                            ct);
                        allGaps.AddRange(DetectSymbolGaps(result.Results, symbol));
                    }
                }
                catch { /* non-critical */ }
            }

            return Results.Json(new
            {
                totalGaps = allGaps.Count,
                gaps = allGaps.OrderBy(g => g.StartDate).ToArray(),
                summary = new
                {
                    symbolsWithGaps = allGaps.Select(g => g.Symbol).Distinct().Count(),
                    totalDaysGapped = allGaps.Sum(g => g.DaysGap)
                }
            }, jsonOptions);
        })
        .WithName("GetBackfillGaps")
        .WithDescription("Detects and reports data gaps across all symbols.")
        .Produces(200);

        // GET /api/backfill/completeness — overall completeness summary
        group.MapGet(UiApiRoutes.BackfillCompleteness, async (
            ConfigStore store,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            var cfg = store.Load();
            var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();
            var completenessScores = new Dictionary<string, double>();

            if (searchService is not null)
            {
                try
                {
                    foreach (var symbol in symbols.Select(s => s.Symbol))
                    {
                        var result = await searchService.SearchFilesAsync(
                            new FileSearchQuery(Symbols: [symbol], Take: 10_000),
                            ct);
                        completenessScores[symbol] = CalculateSymbolCompleteness(result.Results);
                    }
                }
                catch { /* non-critical */ }
            }

            var complete = completenessScores.Count(kv => kv.Value >= 0.95);
            var good = completenessScores.Count(kv => kv.Value >= 0.80 && kv.Value < 0.95);
            var poor = completenessScores.Count(kv => kv.Value < 0.80);
            var average = completenessScores.Any() ? completenessScores.Values.Average() : 0.0;

            return Results.Json(new
            {
                summary = new
                {
                    complete = complete,
                    good = good,
                    poor = poor,
                    average = average
                },
                bySymbol = completenessScores.OrderByDescending(kv => kv.Value).Select(kv => new
                {
                    symbol = kv.Key,
                    completeness = Math.Round(kv.Value * 100, 2),
                    status = kv.Value >= 0.95 ? "Complete" : (kv.Value >= 0.80 ? "Good" : "Incomplete")
                }).ToArray()
            }, jsonOptions);
        })
        .WithName("GetBackfillCompleteness")
        .WithDescription("Returns completeness summary across all symbols.")
        .Produces(200);
    }

    private static double CalculateSymbolCompleteness(IEnumerable<FileSearchResult>? files)
    {
        if (files is null || !files.Any())
            return 0.0;

        var dates = GetTradingDates(files);
        if (!dates.Any())
            return 0.0;

        var expectedTradingDays = CalculateExpectedTradingDays(dates.First(), dates.Last());
        if (expectedTradingDays == 0)
            return 0.0;

        return Math.Min(1.0, (double)dates.Count / expectedTradingDays);
    }

    private static int CalculateExpectedTradingDays(DateTime start, DateTime end)
    {
        var count = 0;
        var current = start.Date;
        while (current <= end.Date)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                count++;
            current = current.AddDays(1);
        }
        return count;
    }

    private static List<BackfillGapInfo> DetectSymbolGaps(IEnumerable<FileSearchResult>? files, string symbol = "")
    {
        var gaps = new List<BackfillGapInfo>();

        if (files is null || !files.Any())
            return gaps;

        var dates = GetTradingDates(files);

        if (dates.Count < 2)
            return gaps;

        for (int i = 0; i < dates.Count - 1; i++)
        {
            var current = dates[i];
            var next = dates[i + 1];
            var expectedNext = GetNextTradingDay(current);

            if (expectedNext < next)
            {
                gaps.Add(new BackfillGapInfo(
                    Symbol: symbol,
                    StartDate: expectedNext,
                    EndDate: next.AddDays(-1),
                    DaysGap: (int)(next - expectedNext).TotalDays,
                    Reason: "Data gap"
                ));
            }
        }

        return gaps;
    }

    private static List<DateTime> GetTradingDates(IEnumerable<FileSearchResult> files)
        => files
            .Select(f => f.Date.UtcDateTime.Date)
            .Distinct()
            .Where(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
            .OrderBy(d => d)
            .ToList();

    private static DateTime GetNextTradingDay(DateTime date)
    {
        var next = date.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }
}
