using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.UI;
using Meridian.Storage.Archival;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for managing symbol watchlists (groups of symbols for organization and bulk operations).
/// </summary>
public sealed class WatchlistService
{
    private readonly ConfigStore _configStore;
    private readonly string _watchlistsPath;

    public WatchlistService(ConfigStore configStore, string? watchlistsPath = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _watchlistsPath = watchlistsPath ?? Path.Combine(
            Path.GetDirectoryName(configStore.ConfigPath) ?? ".",
            "watchlists.json");
    }

    /// <summary>
    /// Get all watchlists.
    /// </summary>
    public async Task<IReadOnlyList<Watchlist>> GetAllWatchlistsAsync(CancellationToken ct = default)
    {
        return await LoadWatchlistsAsync(ct);
    }

    /// <summary>
    /// Get watchlist summaries with subscription counts.
    /// </summary>
    public async Task<IReadOnlyList<WatchlistSummary>> GetWatchlistSummariesAsync(CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        var cfg = _configStore.Load();
        var subscribedSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        return watchlists.Select(w =>
        {
            var active = w.Symbols.Count(s => subscribedSymbols.ContainsKey(s) && subscribedSymbols[s].SubscribeTrades);
            var inactive = w.Symbols.Length - active;

            return new WatchlistSummary(
                Id: w.Id,
                Name: w.Name,
                Description: w.Description,
                Color: w.Color,
                SymbolCount: w.Symbols.Length,
                ActiveSubscriptions: active,
                InactiveSubscriptions: inactive,
                IsPinned: w.IsPinned,
                IsActive: w.IsActive,
                ModifiedAt: w.ModifiedAt
            );
        }).ToList();
    }

    /// <summary>
    /// Get a specific watchlist by ID.
    /// </summary>
    public async Task<Watchlist?> GetWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        return watchlists.FirstOrDefault(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Create a new watchlist.
    /// </summary>
    public async Task<Watchlist> CreateWatchlistAsync(CreateWatchlistRequest request, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var watchlist = new Watchlist(
            Id: $"wl_{Guid.NewGuid():N}"[..16],
            Name: request.Name,
            Description: request.Description,
            Symbols: (request.Symbols ?? Array.Empty<string>()).Select(s => s.ToUpperInvariant()).Distinct().ToArray(),
            Color: request.Color,
            SortOrder: watchlists.Count,
            IsPinned: false,
            IsActive: request.IsActive,
            Defaults: request.Defaults ?? new WatchlistDefaults(),
            CreatedAt: now,
            ModifiedAt: now
        );

        watchlists.Add(watchlist);
        await SaveWatchlistsAsync(watchlists, ct);

        return watchlist;
    }

    /// <summary>
    /// Update an existing watchlist.
    /// </summary>
    public async Task<Watchlist?> UpdateWatchlistAsync(string watchlistId, UpdateWatchlistRequest request, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        var index = watchlists.FindIndex(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return null;

        var existing = watchlists[index];
        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            Description = request.Description ?? existing.Description,
            Color = request.Color ?? existing.Color,
            SortOrder = request.SortOrder ?? existing.SortOrder,
            IsPinned = request.IsPinned ?? existing.IsPinned,
            IsActive = request.IsActive ?? existing.IsActive,
            Defaults = request.Defaults ?? existing.Defaults,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        watchlists[index] = updated;
        await SaveWatchlistsAsync(watchlists, ct);

        return updated;
    }

    /// <summary>
    /// Delete a watchlist.
    /// </summary>
    public async Task<bool> DeleteWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        var removed = watchlists.RemoveAll(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            await SaveWatchlistsAsync(watchlists, ct);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Add symbols to a watchlist.
    /// </summary>
    public async Task<WatchlistOperationResult> AddSymbolsAsync(AddSymbolsToWatchlistRequest request, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        var index = watchlists.FindIndex(w => w.Id.Equals(request.WatchlistId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return new WatchlistOperationResult(false, request.WatchlistId, 0, "Watchlist not found");
        }

        var watchlist = watchlists[index];
        var existingSymbols = new HashSet<string>(watchlist.Symbols, StringComparer.OrdinalIgnoreCase);
        var newSymbols = request.Symbols
            .Select(s => s.ToUpperInvariant())
            .Where(s => !existingSymbols.Contains(s))
            .ToArray();

        if (newSymbols.Length == 0)
        {
            return new WatchlistOperationResult(true, request.WatchlistId, 0, "All symbols already in watchlist");
        }

        var updatedSymbols = watchlist.Symbols.Concat(newSymbols).ToArray();
        watchlists[index] = watchlist with
        {
            Symbols = updatedSymbols,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await SaveWatchlistsAsync(watchlists, ct);

        // Subscribe new symbols if requested
        if (request.SubscribeImmediately)
        {
            await SubscribeSymbolsAsync(newSymbols, watchlist.Defaults, ct);
        }

        return new WatchlistOperationResult(true, request.WatchlistId, newSymbols.Length,
            $"Added {newSymbols.Length} symbols", newSymbols);
    }

    /// <summary>
    /// Remove symbols from a watchlist.
    /// </summary>
    public async Task<WatchlistOperationResult> RemoveSymbolsAsync(RemoveSymbolsFromWatchlistRequest request, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        var index = watchlists.FindIndex(w => w.Id.Equals(request.WatchlistId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return new WatchlistOperationResult(false, request.WatchlistId, 0, "Watchlist not found");
        }

        var watchlist = watchlists[index];
        var symbolsToRemove = new HashSet<string>(request.Symbols, StringComparer.OrdinalIgnoreCase);
        var removedSymbols = watchlist.Symbols.Where(s => symbolsToRemove.Contains(s)).ToArray();
        var remainingSymbols = watchlist.Symbols.Where(s => !symbolsToRemove.Contains(s)).ToArray();

        watchlists[index] = watchlist with
        {
            Symbols = remainingSymbols,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await SaveWatchlistsAsync(watchlists, ct);

        // Optionally unsubscribe orphaned symbols
        if (request.UnsubscribeIfOrphaned && removedSymbols.Length > 0)
        {
            var allWatchlistSymbols = watchlists
                .SelectMany(w => w.Symbols)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orphanedSymbols = removedSymbols
                .Where(s => !allWatchlistSymbols.Contains(s))
                .ToArray();

            if (orphanedSymbols.Length > 0)
            {
                await UnsubscribeSymbolsAsync(orphanedSymbols, ct);
            }
        }

        return new WatchlistOperationResult(true, request.WatchlistId, removedSymbols.Length,
            $"Removed {removedSymbols.Length} symbols", removedSymbols);
    }

    /// <summary>
    /// Subscribe to all symbols in a watchlist.
    /// </summary>
    public async Task<WatchlistOperationResult> SubscribeWatchlistAsync(WatchlistSubscriptionRequest request, CancellationToken ct = default)
    {
        var watchlist = await GetWatchlistAsync(request.WatchlistId, ct);
        if (watchlist is null)
        {
            return new WatchlistOperationResult(false, request.WatchlistId, 0, "Watchlist not found");
        }

        var defaults = new WatchlistDefaults(
            SubscribeTrades: request.SubscribeTrades ?? watchlist.Defaults.SubscribeTrades,
            SubscribeDepth: request.SubscribeDepth ?? watchlist.Defaults.SubscribeDepth,
            DepthLevels: request.DepthLevels ?? watchlist.Defaults.DepthLevels,
            SecurityType: watchlist.Defaults.SecurityType,
            Exchange: watchlist.Defaults.Exchange,
            Currency: watchlist.Defaults.Currency
        );

        await SubscribeSymbolsAsync(watchlist.Symbols, defaults, ct);

        return new WatchlistOperationResult(true, request.WatchlistId, watchlist.Symbols.Length,
            $"Subscribed {watchlist.Symbols.Length} symbols", watchlist.Symbols);
    }

    /// <summary>
    /// Unsubscribe from all symbols in a watchlist.
    /// </summary>
    public async Task<WatchlistOperationResult> UnsubscribeWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        var watchlist = await GetWatchlistAsync(watchlistId, ct);
        if (watchlist is null)
        {
            return new WatchlistOperationResult(false, watchlistId, 0, "Watchlist not found");
        }

        await UnsubscribeSymbolsAsync(watchlist.Symbols, ct);

        return new WatchlistOperationResult(true, watchlistId, watchlist.Symbols.Length,
            $"Unsubscribed {watchlist.Symbols.Length} symbols", watchlist.Symbols);
    }

    /// <summary>
    /// Get all watchlists that contain a specific symbol.
    /// </summary>
    public async Task<IReadOnlyList<Watchlist>> GetWatchlistsForSymbolAsync(string symbol, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        return watchlists.Where(w => w.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Import watchlist from JSON.
    /// </summary>
    public async Task<Watchlist?> ImportWatchlistAsync(string json, CancellationToken ct = default)
    {
        try
        {
            var imported = JsonSerializer.Deserialize<Watchlist>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (imported is null)
                return null;

            var request = new CreateWatchlistRequest(
                Name: imported.Name,
                Description: imported.Description,
                Symbols: imported.Symbols,
                Color: imported.Color,
                IsActive: imported.IsActive,
                Defaults: imported.Defaults
            );

            return await CreateWatchlistAsync(request, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Export watchlist to JSON.
    /// </summary>
    public async Task<string?> ExportWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        var watchlist = await GetWatchlistAsync(watchlistId, ct);
        if (watchlist is null)
            return null;

        return JsonSerializer.Serialize(watchlist, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Reorder watchlists.
    /// </summary>
    public async Task ReorderWatchlistsAsync(string[] watchlistIds, CancellationToken ct = default)
    {
        var watchlists = await LoadWatchlistsAsync(ct);
        var idToWatchlist = watchlists.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < watchlistIds.Length; i++)
        {
            if (idToWatchlist.TryGetValue(watchlistIds[i], out var watchlist))
            {
                idToWatchlist[watchlistIds[i]] = watchlist with { SortOrder = i };
            }
        }

        var reordered = idToWatchlist.Values.OrderBy(w => w.SortOrder).ToList();
        await SaveWatchlistsAsync(reordered, ct);
    }

    private async Task<List<Watchlist>> LoadWatchlistsAsync(CancellationToken ct)
    {
        if (!File.Exists(_watchlistsPath))
            return new List<Watchlist>();

        try
        {
            var json = await File.ReadAllTextAsync(_watchlistsPath, ct);
            var watchlists = JsonSerializer.Deserialize<List<Watchlist>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return watchlists ?? new List<Watchlist>();
        }
        catch
        {
            return new List<Watchlist>();
        }
    }

    private async Task SaveWatchlistsAsync(List<Watchlist> watchlists, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(watchlists, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await AtomicFileWriter.WriteAsync(_watchlistsPath, json, ct);
    }

    private async Task SubscribeSymbolsAsync(string[] symbols, WatchlistDefaults defaults, CancellationToken ct)
    {
        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            var config = new SymbolConfig(
                Symbol: symbol,
                SubscribeTrades: defaults.SubscribeTrades,
                SubscribeDepth: defaults.SubscribeDepth,
                DepthLevels: defaults.DepthLevels,
                SecurityType: defaults.SecurityType,
                Exchange: defaults.Exchange,
                Currency: defaults.Currency
            );
            existingSymbols[symbol] = config;
        }

        var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
        await _configStore.SaveAsync(next);
    }

    private async Task UnsubscribeSymbolsAsync(string[] symbols, CancellationToken ct)
    {
        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .Where(s => !symbols.Contains(s.Symbol, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var next = cfg with { Symbols = existingSymbols };
        await _configStore.SaveAsync(next);
    }
}
