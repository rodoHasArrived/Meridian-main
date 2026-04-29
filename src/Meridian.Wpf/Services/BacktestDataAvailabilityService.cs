using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Meridian.Wpf.Services;

/// <summary>
/// Scans the local data directory tree to determine which months have data present
/// for each symbol in a requested date range, without requiring a running server.
/// Results are cached per unique (symbols, from, to, dataRoot) key.
/// </summary>
public sealed class BacktestDataAvailabilityService
{
    private static readonly Regex _dateInFilenameRegex = new(@"\d{4}-\d{2}-\d{2}", RegexOptions.Compiled);

    private record CacheKey(string Symbols, DateOnly From, DateOnly To, string DataRoot);

    private readonly ConcurrentDictionary<CacheKey, Dictionary<string, Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>>> _cache = new();

    /// <summary>
    /// Returns coverage data per symbol, per month for the given inputs.
    /// If more than 12 symbols are requested the result is collapsed to a single
    /// "Universe Average" row.
    /// </summary>
    public Task<Dictionary<string, Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>>>
        GetCoverageAsync(
            IReadOnlyList<string> symbols,
            DateOnly from,
            DateOnly to,
            string dataRoot,
            CancellationToken cancellationToken = default)
    {
        var key = new CacheKey(
            string.Join(",", symbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)),
            from, to, dataRoot);

        if (_cache.TryGetValue(key, out var cached))
            return Task.FromResult(cached);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = BuildCoverage(symbols, from, to, dataRoot, cancellationToken);
            _cache.TryAdd(key, result);
            return result;
        }, cancellationToken);
    }

    private static Dictionary<string, Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>>
        BuildCoverage(
            IReadOnlyList<string> symbols,
            DateOnly from,
            DateOnly to,
            string dataRoot,
            CancellationToken cancellationToken)
    {
        var perSymbol = new Dictionary<string, Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbolDir = Path.Combine(dataRoot, symbol);
            var presentDates = new HashSet<DateOnly>();

            if (Directory.Exists(symbolDir))
            {
                foreach (var file in Directory.EnumerateFiles(symbolDir, "*.jsonl", SearchOption.AllDirectories))
                {
                    var match = _dateInFilenameRegex.Match(Path.GetFileName(file));
                    if (!match.Success)
                        continue;
                    if (!DateOnly.TryParse(match.Value, out var date))
                        continue;
                    if (date >= from && date <= to)
                        presentDates.Add(date);
                }
            }

            perSymbol[symbol] = BuildMonthMap(from, to, presentDates);
        }

        if (symbols.Count > 12)
            return CollapseToUniverseAverage(perSymbol, from, to);

        return perSymbol;
    }

    private static Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>
        BuildMonthMap(DateOnly from, DateOnly to, HashSet<DateOnly> presentDates)
    {
        var result = new Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>();
        var current = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);

        while (current <= lastMonth)
        {
            var tradingDays = CountTradingDaysInMonth(current.Year, current.Month, from, to);
            var present = presentDates.Count(d => d.Year == current.Year && d.Month == current.Month);
            result[(current.Year, current.Month)] = (present, tradingDays);
            current = current.AddMonths(1);
        }

        return result;
    }

    private static int CountTradingDaysInMonth(int year, int month, DateOnly from, DateOnly to)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var start = monthStart > from ? monthStart : from;
        var end = monthEnd < to ? monthEnd : to;
        var count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        }
        return count;
    }

    private static Dictionary<string, Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>>
        CollapseToUniverseAverage(
            Dictionary<string, Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>> perSymbol,
            DateOnly from,
            DateOnly to)
    {
        // Collect all months across the range
        var allMonths = new HashSet<(int Year, int Month)>();
        foreach (var map in perSymbol.Values)
            foreach (var k in map.Keys)
                allMonths.Add(k);

        var avgMap = new Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>();
        foreach (var month in allMonths)
        {
            var totalPresent = 0;
            var tradingDays = 0;
            var symbolCount = 0;
            foreach (var map in perSymbol.Values)
            {
                if (map.TryGetValue(month, out var entry))
                {
                    totalPresent += entry.DaysPresent;
                    tradingDays = entry.TradingDaysInMonth; // same across symbols for same month
                    symbolCount++;
                }
            }
            var avgPresent = symbolCount > 0 ? (int)Math.Round((double)totalPresent / symbolCount) : 0;
            avgMap[month] = (avgPresent, tradingDays);
        }

        return new Dictionary<string, Dictionary<(int Year, int Month), (int DaysPresent, int TradingDaysInMonth)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Universe Average"] = avgMap
        };
    }

    /// <summary>Clears the coverage cache, forcing a re-scan on next request.</summary>
    public void InvalidateCache() => _cache.Clear();
}
