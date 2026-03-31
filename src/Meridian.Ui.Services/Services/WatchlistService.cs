using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Default watchlist service for the shared UI services layer.
/// Platform-specific projects (WPF) override this with their own implementations
/// by setting the Instance property during app startup.
/// </summary>
public class WatchlistService
{
    private static WatchlistService _instance = new WatchlistService();
    private readonly SemaphoreSlim _watchlistLock = new(1, 1);
    private WatchlistData _watchlist = new();

    public static WatchlistService Instance
    {
        get => _instance;
        set => _instance = value ?? throw new ArgumentNullException(nameof(value));
    }

    public virtual async Task<WatchlistData> LoadWatchlistAsync(CancellationToken ct = default)
    {
        await _watchlistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            return Clone(_watchlist);
        }
        finally
        {
            _watchlistLock.Release();
        }
    }

    /// <summary>
    /// Creates a new watchlist or updates an existing one.
    /// Platform-specific implementations should override this method.
    /// </summary>
    /// <param name="name">The watchlist name.</param>
    /// <param name="symbols">The symbols to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public virtual async Task<bool> CreateOrUpdateWatchlistAsync(string name, IEnumerable<string> symbols, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(symbols);
        ct.ThrowIfCancellationRequested();

        var normalizedSymbols = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Select(symbol => new WatchlistItem { Symbol = symbol })
            .ToList();

        await _watchlistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _watchlist = new WatchlistData
            {
                Symbols = normalizedSymbols,
                Groups = new List<WatchlistGroup>()
            };

            return true;
        }
        finally
        {
            _watchlistLock.Release();
        }
    }

    private static WatchlistData Clone(WatchlistData source)
    {
        return new WatchlistData
        {
            Symbols = source.Symbols
                .Select(item => new WatchlistItem
                {
                    Symbol = item.Symbol,
                    Notes = item.Notes,
                    Tags = new List<string>(item.Tags)
                })
                .ToList(),
            Groups = source.Groups
                .Select(group => new WatchlistGroup
                {
                    Name = group.Name,
                    Symbols = new List<string>(group.Symbols)
                })
                .ToList()
        };
    }
}

/// <summary>
/// Watchlist data containing watched symbols.
/// </summary>
public sealed class WatchlistData
{
    public List<WatchlistItem> Symbols { get; set; } = new();
    public List<WatchlistGroup> Groups { get; set; } = new();
}

/// <summary>
/// A single item in a watchlist.
/// </summary>
public sealed class WatchlistItem
{
    public string Symbol { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// A group of symbols in a watchlist.
/// </summary>
public sealed class WatchlistGroup
{
    public string Name { get; set; } = string.Empty;
    public List<string> Symbols { get; set; } = new();
}
