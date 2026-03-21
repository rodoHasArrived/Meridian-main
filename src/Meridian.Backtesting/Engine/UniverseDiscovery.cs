using Meridian.Storage.Services;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Discovers which symbols have locally-stored JSONL data for the requested date range.
/// Uses the <see cref="StorageCatalogService"/> when available, falling back to a direct
/// filesystem scan so it works even without a pre-built catalog.
/// </summary>
internal static class UniverseDiscovery
{
    /// <summary>
    /// Returns the intersection of requested symbols and symbols that have data on disk
    /// in the given date range.
    /// </summary>
    public static async Task<IReadOnlySet<string>> DiscoverAsync(
        StorageCatalogService catalogService,
        string dataRoot,
        IReadOnlyList<string>? requestedSymbols,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var available = await DiscoverAvailableSymbolsAsync(catalogService, dataRoot, from, to, ct);

        if (requestedSymbols is null or { Count: 0 })
            return available;

        var requested = new HashSet<string>(requestedSymbols, StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(available.Where(s => requested.Contains(s)), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlySet<string>> DiscoverAvailableSymbolsAsync(
        StorageCatalogService catalogService,
        string dataRoot,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        // Try catalog first
        try
        {
            var catalog = catalogService.GetCatalog();
            if (catalog?.Symbols is { Count: > 0 } symbolDict)
            {
                // Catalog keys ARE the symbol names
                var symbols = symbolDict.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (symbols.Count > 0)
                    return symbols;
            }
        }
        catch
        {
            // Catalog not initialised — fall through to filesystem scan
        }

        await Task.Yield();  // honour cancellation token without blocking
        ct.ThrowIfCancellationRequested();

        return ScanFilesystem(dataRoot, from, to);
    }

    private static IReadOnlySet<string> ScanFilesystem(string dataRoot, DateOnly from, DateOnly to)
    {
        if (!Directory.Exists(dataRoot))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Support naming patterns: {root}/{symbol}/..., {root}/live/{provider}/{symbol}/...
        // Discover all JSONL files and extract the symbol from the filename or path segment.
        foreach (var file in Directory.EnumerateFiles(dataRoot, "*.jsonl*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(name);  // strip .jsonl from .jsonl.gz

            // Common file naming: SPY_trades_2024-01-05 or SPY_quotes_2024-01-05
            var parts = name.Split('_');
            if (parts.Length >= 2)
            {
                var candidate = parts[0].ToUpperInvariant();
                if (IsValidSymbol(candidate))
                    symbols.Add(candidate);
            }

            // Also check the directory path: .../live/{provider}/{date}/{symbol}_...
            var dir = Path.GetDirectoryName(file);
            if (dir is not null)
            {
                var dirName = Path.GetFileName(dir)?.ToUpperInvariant();
                if (dirName is not null && IsValidSymbol(dirName))
                    symbols.Add(dirName);
            }
        }

        return symbols;
    }

    private static bool IsValidSymbol(string s) =>
        s.Length is >= 1 and <= 10 && s.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '/');
}
