using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Read-only watchlist access used by shell surfaces and workstation services.
/// </summary>
public interface IWatchlistReader
{
    Task<IReadOnlyList<Watchlist>> GetAllWatchlistsAsync(CancellationToken ct = default);
}

/// <summary>
/// Service for managing symbol watchlists in the WPF application.
/// Provides local persistence and optional synchronization with the backend API.
/// </summary>
public sealed class WatchlistService : IWatchlistReader
{
    private static readonly Lazy<WatchlistService> _instance = new(() => new WatchlistService());
    private static readonly HttpClient _httpClient = new();

    private readonly string _watchlistsPath;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private List<Watchlist> _watchlists = new();
    private bool _isLoaded;

    private string _baseUrl = "http://localhost:8080";

    /// <summary>
    /// Gets the singleton instance of the WatchlistService.
    /// </summary>
    public static WatchlistService Instance => _instance.Value;

    /// <summary>
    /// Gets or sets the base URL for the API.
    /// </summary>
    public string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value;
    }

    /// <summary>
    /// Occurs when watchlists are updated.
    /// </summary>
    public event EventHandler<WatchlistsChangedEventArgs>? WatchlistsChanged;

    private WatchlistService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var configDir = Path.Combine(appData, "Meridian");
        Directory.CreateDirectory(configDir);
        _watchlistsPath = Path.Combine(configDir, "watchlists.json");
    }

    /// <summary>
    /// Gets all watchlists.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of watchlists.</returns>
    public async Task<IReadOnlyList<Watchlist>> GetAllWatchlistsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        lock (_lock)
        {
            return _watchlists.OrderBy(w => w.SortOrder).ToList();
        }
    }

    /// <summary>
    /// Gets a specific watchlist by ID.
    /// </summary>
    /// <param name="watchlistId">The watchlist ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The watchlist, or null if not found.</returns>
    public async Task<Watchlist?> GetWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        lock (_lock)
        {
            return _watchlists.FirstOrDefault(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Creates a new watchlist.
    /// </summary>
    /// <param name="name">The watchlist name.</param>
    /// <param name="symbols">Initial symbols.</param>
    /// <param name="color">Optional color.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created watchlist.</returns>
    public async Task<Watchlist> CreateWatchlistAsync(
        string name,
        IEnumerable<string>? symbols = null,
        string? color = null,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var watchlist = new Watchlist
        {
            Id = $"wl_{Guid.NewGuid():N}"[..16],
            Name = name,
            Symbols = (symbols ?? Enumerable.Empty<string>()).Select(s => s.ToUpperInvariant()).Distinct().ToList(),
            Color = color,
            SortOrder = _watchlists.Count,
            CreatedAt = now,
            ModifiedAt = now
        };

        lock (_lock)
        {
            _watchlists.Add(watchlist);
        }

        await SaveWatchlistsAsync(ct);
        OnWatchlistsChanged(WatchlistChangeType.Created, watchlist);

        return watchlist;
    }

    /// <summary>
    /// Updates an existing watchlist.
    /// </summary>
    /// <param name="watchlistId">The watchlist ID.</param>
    /// <param name="name">New name (null to keep current).</param>
    /// <param name="color">New color (null to keep current).</param>
    /// <param name="isPinned">New pinned state (null to keep current).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated watchlist, or null if not found.</returns>
    public async Task<Watchlist?> UpdateWatchlistAsync(
        string watchlistId,
        string? name = null,
        string? color = null,
        bool? isPinned = null,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        Watchlist? updated = null;
        lock (_lock)
        {
            var index = _watchlists.FindIndex(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = _watchlists[index];
                updated = new Watchlist
                {
                    Id = existing.Id,
                    Name = name ?? existing.Name,
                    Symbols = existing.Symbols,
                    Color = color ?? existing.Color,
                    SortOrder = existing.SortOrder,
                    IsPinned = isPinned ?? existing.IsPinned,
                    IsActive = existing.IsActive,
                    CreatedAt = existing.CreatedAt,
                    ModifiedAt = DateTimeOffset.UtcNow
                };
                _watchlists[index] = updated;
            }
        }

        if (updated != null)
        {
            await SaveWatchlistsAsync(ct);
            OnWatchlistsChanged(WatchlistChangeType.Updated, updated);
        }

        return updated;
    }

    /// <summary>
    /// Deletes a watchlist.
    /// </summary>
    /// <param name="watchlistId">The watchlist ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    public async Task<bool> DeleteWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        Watchlist? deleted = null;
        lock (_lock)
        {
            var index = _watchlists.FindIndex(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                deleted = _watchlists[index];
                _watchlists.RemoveAt(index);
            }
        }

        if (deleted != null)
        {
            await SaveWatchlistsAsync(ct);
            OnWatchlistsChanged(WatchlistChangeType.Deleted, deleted);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Adds symbols to a watchlist.
    /// </summary>
    /// <param name="watchlistId">The watchlist ID.</param>
    /// <param name="symbols">Symbols to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of symbols added.</returns>
    public async Task<int> AddSymbolsAsync(string watchlistId, IEnumerable<string> symbols, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        int added = 0;
        Watchlist? updated = null;

        lock (_lock)
        {
            var index = _watchlists.FindIndex(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = _watchlists[index];
                var existingSymbols = new HashSet<string>(existing.Symbols, StringComparer.OrdinalIgnoreCase);
                var newSymbols = symbols
                    .Select(s => s.ToUpperInvariant())
                    .Where(s => !existingSymbols.Contains(s))
                    .ToList();

                if (newSymbols.Count > 0)
                {
                    updated = new Watchlist
                    {
                        Id = existing.Id,
                        Name = existing.Name,
                        Symbols = existing.Symbols.Concat(newSymbols).ToList(),
                        Color = existing.Color,
                        SortOrder = existing.SortOrder,
                        IsPinned = existing.IsPinned,
                        IsActive = existing.IsActive,
                        CreatedAt = existing.CreatedAt,
                        ModifiedAt = DateTimeOffset.UtcNow
                    };
                    _watchlists[index] = updated;
                    added = newSymbols.Count;
                }
            }
        }

        if (updated != null)
        {
            await SaveWatchlistsAsync(ct);
            OnWatchlistsChanged(WatchlistChangeType.Updated, updated);
        }

        return added;
    }

    /// <summary>
    /// Removes symbols from a watchlist.
    /// </summary>
    /// <param name="watchlistId">The watchlist ID.</param>
    /// <param name="symbols">Symbols to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of symbols removed.</returns>
    public async Task<int> RemoveSymbolsAsync(string watchlistId, IEnumerable<string> symbols, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        int removed = 0;
        Watchlist? updated = null;

        lock (_lock)
        {
            var index = _watchlists.FindIndex(w => w.Id.Equals(watchlistId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = _watchlists[index];
                var symbolsToRemove = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
                var remainingSymbols = existing.Symbols.Where(s => !symbolsToRemove.Contains(s)).ToList();
                removed = existing.Symbols.Count - remainingSymbols.Count;

                if (removed > 0)
                {
                    updated = new Watchlist
                    {
                        Id = existing.Id,
                        Name = existing.Name,
                        Symbols = remainingSymbols,
                        Color = existing.Color,
                        SortOrder = existing.SortOrder,
                        IsPinned = existing.IsPinned,
                        IsActive = existing.IsActive,
                        CreatedAt = existing.CreatedAt,
                        ModifiedAt = DateTimeOffset.UtcNow
                    };
                    _watchlists[index] = updated;
                }
            }
        }

        if (updated != null)
        {
            await SaveWatchlistsAsync(ct);
            OnWatchlistsChanged(WatchlistChangeType.Updated, updated);
        }

        return removed;
    }

    /// <summary>
    /// Gets all watchlists that contain a specific symbol.
    /// </summary>
    /// <param name="symbol">The symbol to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of watchlists containing the symbol.</returns>
    public async Task<IReadOnlyList<Watchlist>> GetWatchlistsForSymbolAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        lock (_lock)
        {
            return _watchlists.Where(w => w.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase)).ToList();
        }
    }

    /// <summary>
    /// Exports a watchlist to JSON.
    /// </summary>
    /// <param name="watchlistId">The watchlist ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JSON string, or null if not found.</returns>
    public async Task<string?> ExportWatchlistAsync(string watchlistId, CancellationToken ct = default)
    {
        var watchlist = await GetWatchlistAsync(watchlistId, ct);
        if (watchlist == null)
            return null;

        return JsonSerializer.Serialize(watchlist, DesktopJsonOptions.PrettyPrint);
    }

    /// <summary>
    /// Imports a watchlist from JSON.
    /// </summary>
    /// <param name="json">JSON string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The imported watchlist, or null on failure.</returns>
    public async Task<Watchlist?> ImportWatchlistAsync(string json, CancellationToken ct = default)
    {
        try
        {
            var imported = JsonSerializer.Deserialize<Watchlist>(json, DesktopJsonOptions.Compact);

            if (imported == null)
                return null;

            return await CreateWatchlistAsync(
                imported.Name + " (Imported)",
                imported.Symbols,
                imported.Color,
                ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Syncs local watchlists with the backend API.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if sync succeeded.</returns>
    public async Task<bool> SyncWithBackendAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/watchlists", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var remoteWatchlists = JsonSerializer.Deserialize<List<Watchlist>>(json, DesktopJsonOptions.Compact);

                if (remoteWatchlists != null)
                {
                    lock (_lock)
                    {
                        _watchlists = remoteWatchlists;
                    }
                    await SaveWatchlistsAsync(ct);
                    OnWatchlistsChanged(WatchlistChangeType.Synced, null);
                    return true;
                }
            }
        }
        catch
        {
            // Sync failed - continue with local data
        }

        return false;
    }

    /// <summary>
    /// Reorders watchlists.
    /// </summary>
    /// <param name="watchlistIds">Ordered list of watchlist IDs.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ReorderWatchlistsAsync(IEnumerable<string> watchlistIds, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        lock (_lock)
        {
            var idToWatchlist = _watchlists.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var id in watchlistIds)
            {
                if (idToWatchlist.TryGetValue(id, out var watchlist))
                {
                    var updated = new Watchlist
                    {
                        Id = watchlist.Id,
                        Name = watchlist.Name,
                        Symbols = watchlist.Symbols,
                        Color = watchlist.Color,
                        SortOrder = index++,
                        IsPinned = watchlist.IsPinned,
                        IsActive = watchlist.IsActive,
                        CreatedAt = watchlist.CreatedAt,
                        ModifiedAt = watchlist.ModifiedAt
                    };
                    idToWatchlist[id] = updated;
                }
            }
            _watchlists = idToWatchlist.Values.OrderBy(w => w.SortOrder).ToList();
        }

        await SaveWatchlistsAsync(ct);
        OnWatchlistsChanged(WatchlistChangeType.Reordered, null);
    }

    /// <summary>
    /// Creates a new watchlist or updates an existing one with the given name.
    /// If a watchlist with the name exists, symbols are added to it.
    /// Otherwise, a new watchlist is created.
    /// </summary>
    /// <param name="name">The watchlist name.</param>
    /// <param name="symbols">The symbols to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public async Task<bool> CreateOrUpdateWatchlistAsync(string name, IEnumerable<string> symbols, CancellationToken ct = default)
    {
        try
        {
            await EnsureLoadedAsync(ct);

            // Check if watchlist with this name exists
            Watchlist? existing = null;
            lock (_lock)
            {
                existing = _watchlists.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            if (existing != null)
            {
                // Update existing watchlist by adding symbols
                await AddSymbolsAsync(existing.Id, symbols, ct);
            }
            else
            {
                // Create new watchlist
                await CreateWatchlistAsync(name, symbols, null, ct);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_isLoaded)
            return;

        await _loadGate.WaitAsync(ct);
        try
        {
            if (_isLoaded)
            {
                return;
            }

            if (File.Exists(_watchlistsPath))
            {
                var json = await File.ReadAllTextAsync(_watchlistsPath, ct);
                var watchlists = JsonSerializer.Deserialize<List<Watchlist>>(json, DesktopJsonOptions.Compact);

                lock (_lock)
                {
                    _watchlists = watchlists ?? new List<Watchlist>();
                    _isLoaded = true;
                }
            }
            else
            {
                lock (_lock)
                {
                    _watchlists = CreateDefaultWatchlists();
                    _isLoaded = true;
                }

                await SaveWatchlistsAsync(ct);
            }
        }
        catch
        {
            _isLoaded = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static List<Watchlist> CreateDefaultWatchlists()
    {
        var defaults = new[]
        {
            ("Tech Giants", new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "META" }, "#4CAF50"),
            ("Major ETFs", new[] { "SPY", "QQQ", "IWM", "DIA", "VTI" }, "#2196F3"),
            ("Semiconductors", new[] { "NVDA", "AMD", "INTC", "TSM", "AVGO" }, "#9C27B0")
        };

        var createdAt = DateTimeOffset.UtcNow;
        return defaults
            .Select((entry, index) => new Watchlist
            {
                Id = $"wl_{Guid.NewGuid():N}"[..16],
                Name = entry.Item1,
                Symbols = entry.Item2.ToList(),
                Color = entry.Item3,
                SortOrder = index,
                CreatedAt = createdAt,
                ModifiedAt = createdAt
            })
            .ToList();
    }

    private async Task SaveWatchlistsAsync(CancellationToken ct)
    {
        List<Watchlist> toSave;
        lock (_lock)
        {
            toSave = _watchlists.ToList();
        }

        var json = JsonSerializer.Serialize(toSave, DesktopJsonOptions.PrettyPrint);

        await File.WriteAllTextAsync(_watchlistsPath, json, ct);
    }

    private void OnWatchlistsChanged(WatchlistChangeType changeType, Watchlist? watchlist)
    {
        WatchlistsChanged?.Invoke(this, new WatchlistsChangedEventArgs(changeType, watchlist));
    }
}

/// <summary>
/// Represents a symbol watchlist.
/// </summary>
public sealed class Watchlist
{
    /// <summary>Unique watchlist identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name for the watchlist.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Symbols in this watchlist.</summary>
    public List<string> Symbols { get; init; } = new();

    /// <summary>Color for UI display (hex format).</summary>
    public string? Color { get; init; }

    /// <summary>Sort order for UI.</summary>
    public int SortOrder { get; init; }

    /// <summary>Whether this watchlist is pinned/favorited.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Whether subscriptions are active for this watchlist.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>When the watchlist was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the watchlist was last modified.</summary>
    public DateTimeOffset ModifiedAt { get; init; }
}

/// <summary>
/// Event arguments for watchlist changes.
/// </summary>
public sealed class WatchlistsChangedEventArgs : EventArgs
{
    /// <summary>Type of change that occurred.</summary>
    public WatchlistChangeType ChangeType { get; }

    /// <summary>The affected watchlist (may be null for bulk operations).</summary>
    public Watchlist? Watchlist { get; }

    public WatchlistsChangedEventArgs(WatchlistChangeType changeType, Watchlist? watchlist)
    {
        ChangeType = changeType;
        Watchlist = watchlist;
    }
}

/// <summary>
/// Type of watchlist change.
/// </summary>
public enum WatchlistChangeType : byte
{
    Created,
    Updated,
    Deleted,
    Reordered,
    Synced
}
