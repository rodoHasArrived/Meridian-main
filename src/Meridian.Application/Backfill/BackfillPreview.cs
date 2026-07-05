using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.Backfill;

/// <summary>
/// Result of a backfill preview operation.
/// </summary>
public sealed record BackfillPreviewResult(
    string Provider,
    string ProviderDisplayName,
    DateOnly From,
    DateOnly To,
    int TotalDays,
    int EstimatedTradingDays,
    SymbolPreview[] Symbols,
    int EstimatedDurationSeconds,
    string[] Notes
);

/// <summary>
/// Preview information for a single symbol.
/// </summary>
public sealed record SymbolPreview(
    string Symbol,
    string DateRange,
    int EstimatedBars,
    ExistingDataInfo ExistingData,
    bool WouldOverwrite
);

/// <summary>
/// Information about existing data for a symbol.
/// </summary>
public sealed record ExistingDataInfo(
    bool HasData,
    bool IsComplete,
    DateOnly? ExistingFrom,
    DateOnly? ExistingTo,
    int FileCount,
    long TotalSizeBytes
);

/// <summary>
/// Computes backfill preview estimates — trading days, bar counts, run duration, and existing
/// on-disk coverage — without fetching any data. Pure estimation support for
/// <see cref="BackfillCoordinator.PreviewAsync"/>.
/// </summary>
internal static class BackfillPreviewPlanner
{
    public static BackfillPreviewResult BuildPreview(
        BackfillRequest request,
        IHistoricalDataProvider? provider,
        string dataRoot)
    {
        var from = request.From ?? DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
        var to = request.To ?? DateOnly.FromDateTime(DateTime.Today);
        var totalDays = to.DayNumber - from.DayNumber + 1;
        var tradingDays = EstimateTradingDays(from, to);
        var estimatedBarsPerSymbol = EstimateBarsPerSymbol(request.Granularity, tradingDays);

        var symbolPreviews = new List<SymbolPreview>();
        foreach (var symbol in request.Symbols)
        {
            // Check if data already exists for this symbol
            var existingDataInfo = GetExistingDataInfo(dataRoot, symbol, from, to, request.Granularity);

            symbolPreviews.Add(new SymbolPreview(
                Symbol: symbol.ToUpperInvariant(),
                DateRange: $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}",
                EstimatedBars: estimatedBarsPerSymbol,
                ExistingData: existingDataInfo,
                WouldOverwrite: existingDataInfo.HasData && !existingDataInfo.IsComplete
            ));
        }

        return new BackfillPreviewResult(
            Provider: provider?.Name ?? request.Provider,
            ProviderDisplayName: provider?.DisplayName ?? request.Provider,
            From: from,
            To: to,
            TotalDays: totalDays,
            EstimatedTradingDays: tradingDays,
            Symbols: symbolPreviews.ToArray(),
            EstimatedDurationSeconds: EstimateBackfillDuration(request.Symbols.Count, tradingDays, request.Granularity, provider),
            Notes: GetProviderNotes(provider)
        );
    }

    private static int EstimateTradingDays(DateOnly from, DateOnly to)
    {
        // Rough estimate: ~252 trading days per year, exclude weekends
        var days = 0;
        var current = from;
        while (current <= to)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                days++;
            }
            current = current.AddDays(1);
        }
        return days;
    }

    private static ExistingDataInfo GetExistingDataInfo(
        string dataRoot,
        string symbol,
        DateOnly from,
        DateOnly to,
        DataGranularity granularity)
    {
        var symbolDirs = GetExistingSymbolDirectories(dataRoot, symbol);
        if (symbolDirs.Length == 0)
        {
            return new ExistingDataInfo(
                HasData: false,
                IsComplete: false,
                ExistingFrom: null,
                ExistingTo: null,
                FileCount: 0,
                TotalSizeBytes: 0
            );
        }

        var files = symbolDirs
            .SelectMany(dir => Directory.GetFiles(dir, "*.jsonl*", SearchOption.AllDirectories))
            .Where(file => FileMatchesGranularity(file, granularity))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            return new ExistingDataInfo(
                HasData: false,
                IsComplete: false,
                ExistingFrom: null,
                ExistingTo: null,
                FileCount: 0,
                TotalSizeBytes: 0
            );
        }

        var totalSize = files.Sum(f => new FileInfo(f).Length);

        // Try to determine date range from file names
        DateOnly? existingFrom = null;
        DateOnly? existingTo = null;
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // Try to extract date from filename (common patterns: YYYY-MM-DD, YYYYMMDD)
            if (TryExtractDateFromFileName(name, out var date))
            {
                if (existingFrom is null || date < existingFrom.Value)
                    existingFrom = date;
                if (existingTo is null || date > existingTo.Value)
                    existingTo = date;
            }
        }

        var expectedFrom = FirstWeekdayOnOrAfter(from);
        var expectedTo = LastWeekdayOnOrBefore(to);
        var isComplete = expectedFrom is not null &&
            expectedTo is not null &&
            existingFrom <= expectedFrom.Value &&
            existingTo >= expectedTo.Value;

        return new ExistingDataInfo(
            HasData: true,
            IsComplete: isComplete,
            ExistingFrom: existingFrom,
            ExistingTo: existingTo,
            FileCount: files.Length,
            TotalSizeBytes: totalSize
        );
    }

    private static DateOnly? FirstWeekdayOnOrAfter(DateOnly date)
    {
        var current = date;
        for (var i = 0; i < 7; i++)
        {
            if (IsWeekday(current))
                return current;

            current = current.AddDays(1);
        }

        return null;
    }

    private static DateOnly? LastWeekdayOnOrBefore(DateOnly date)
    {
        var current = date;
        for (var i = 0; i < 7; i++)
        {
            if (IsWeekday(current))
                return current;

            current = current.AddDays(-1);
        }

        return null;
    }

    private static bool IsWeekday(DateOnly date) =>
        date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;

    private static string[] GetExistingSymbolDirectories(string dataRoot, string symbol)
    {
        var normalizedSymbol = symbol.ToUpperInvariant();
        var candidates = new[]
        {
            // Current JsonlStorageSink layout: <dataRoot>/<symbol>/<event-type>/<date>.jsonl[.gz]
            Path.Combine(dataRoot, normalizedSymbol),
            // Legacy preview layout retained for compatibility with older local archives.
            Path.Combine(dataRoot, "historical", normalizedSymbol)
        };

        return candidates
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool FileMatchesGranularity(string filePath, DataGranularity granularity)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (granularity.IsIntraday())
            return fileName.Contains(granularity.ToStorageFilePrefix(), StringComparison.OrdinalIgnoreCase);

        if (fileName.Contains(DataGranularity.Daily.ToStorageFilePrefix(), StringComparison.OrdinalIgnoreCase))
            return true;

        return !GetIntradayStoragePrefixes()
            .Any(prefix => fileName.Contains(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetIntradayStoragePrefixes() =>
    [
        DataGranularity.Minute1.ToStorageFilePrefix(),
        DataGranularity.Minute5.ToStorageFilePrefix(),
        DataGranularity.Minute15.ToStorageFilePrefix(),
        DataGranularity.Minute30.ToStorageFilePrefix(),
        DataGranularity.Hour1.ToStorageFilePrefix(),
        DataGranularity.Hour4.ToStorageFilePrefix()
    ];

    private static bool TryExtractDateFromFileName(string name, out DateOnly date)
    {
        date = default;

        // Try YYYY-MM-DD pattern
        if (name.Length >= 10)
        {
            for (var i = 0; i <= name.Length - 10; i++)
            {
                var segment = name.Substring(i, 10);
                if (DateOnly.TryParseExact(segment, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out date))
                    return true;
            }
        }

        // Try YYYYMMDD pattern
        if (name.Length >= 8)
        {
            for (var i = 0; i <= name.Length - 8; i++)
            {
                var segment = name.Substring(i, 8);
                if (DateOnly.TryParseExact(segment, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out date))
                    return true;
            }
        }

        return false;
    }

    private static int EstimateBarsPerSymbol(DataGranularity granularity, int tradingDays)
    {
        if (tradingDays <= 0)
            return 0;

        var barsPerTradingDay = granularity switch
        {
            DataGranularity.Minute1 => 390,
            DataGranularity.Minute5 => 78,
            DataGranularity.Minute15 => 26,
            DataGranularity.Minute30 => 13,
            DataGranularity.Hour1 => 7,
            DataGranularity.Hour4 => 2,
            _ => 1
        };

        return tradingDays * barsPerTradingDay;
    }

    private static int EstimateBackfillDuration(
        int symbolCount,
        int tradingDays,
        DataGranularity granularity,
        IHistoricalDataProvider? provider)
    {
        var requestsPerSymbol = granularity switch
        {
            DataGranularity.Minute1 => Math.Max(1, (int)Math.Ceiling(tradingDays / 8d)),
            DataGranularity.Minute5 or DataGranularity.Minute15 or DataGranularity.Minute30 => Math.Max(1, (int)Math.Ceiling(tradingDays / 60d)),
            DataGranularity.Hour1 or DataGranularity.Hour4 => Math.Max(1, (int)Math.Ceiling(tradingDays / 730d)),
            _ => 1
        };

        var estimatedRequests = Math.Max(1, symbolCount * requestsPerSymbol);
        var requestsPerSecond = provider?.RateLimitDelay > TimeSpan.Zero
            ? 1d / provider.RateLimitDelay.TotalSeconds
            : 0.5d;

        return (int)Math.Ceiling(estimatedRequests / Math.Max(requestsPerSecond, 0.1d)) + symbolCount;
    }

    /// <summary>
    /// Gets provider notes from the centralized ProviderCatalog.
    /// Eliminates per-provider conditionals in favor of standardized catalog lookup.
    /// </summary>
    private static string[] GetProviderNotes(IHistoricalDataProvider? provider)
    {
        if (provider is null)
        {
            return new[] { "Provider not found. Backfill may fail." };
        }

        // Use centralized ProviderCatalog instead of hardcoded per-provider conditionals
        var catalogNotes = ProviderCatalog.GetProviderNotes(provider.Name);
        if (catalogNotes.Length > 0)
        {
            return catalogNotes;
        }

        // Fallback: generate notes from provider's own metadata
        var notes = new List<string>();

        if (!string.IsNullOrEmpty(provider.Description))
        {
            notes.Add(provider.Description);
        }

        if (provider.MaxRequestsPerWindow < int.MaxValue)
        {
            var window = provider.RateLimitWindow.TotalMinutes >= 1
                ? $"{provider.RateLimitWindow.TotalMinutes:F0} minute(s)"
                : $"{provider.RateLimitWindow.TotalSeconds:F0} second(s)";
            notes.Add($"Rate limit: {provider.MaxRequestsPerWindow} requests/{window}.");
        }

        if (provider is IHistoricalAggregateBarProvider aggregateProvider)
        {
            var supported = aggregateProvider.SupportedGranularities
                .Select(g => g.ToDisplayName())
                .OrderBy(name => name)
                .ToArray();
            if (supported.Length > 0)
                notes.Add($"Intraday granularities: {string.Join(", ", supported)}.");
        }

        if (provider.RateLimitDelay > TimeSpan.Zero)
        {
            notes.Add($"Minimum delay between requests: {provider.RateLimitDelay.TotalMilliseconds:F0}ms.");
        }

        return notes.ToArray();
    }
}
