using System.Diagnostics;
using System.Text.RegularExpressions;
using Meridian.Application.Config;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.UI;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for performing batch operations on symbol subscriptions.
/// Supports bulk delete, toggle, update, and other multi-symbol operations.
/// </summary>
public sealed class BatchOperationsService
{
    private readonly ConfigStore _configStore;
    private readonly WatchlistService _watchlistService;

    public BatchOperationsService(ConfigStore configStore, WatchlistService watchlistService)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _watchlistService = watchlistService ?? throw new ArgumentNullException(nameof(watchlistService));
    }

    /// <summary>
    /// Delete multiple symbols at once.
    /// </summary>
    public async Task<BatchOperationResult> DeleteSymbolsAsync(
        BatchDeleteRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var affected = new List<string>();
        var skipped = new List<string>();

        var cfg = _configStore.Load();
        var symbolsToDelete = new HashSet<string>(request.Symbols, StringComparer.OrdinalIgnoreCase);

        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in request.Symbols)
        {
            if (existingSymbols.Remove(symbol))
            {
                affected.Add(symbol);
            }
            else
            {
                skipped.Add(symbol);
            }
        }

        if (affected.Count > 0)
        {
            var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
            await _configStore.SaveAsync(next);

            // Remove from watchlists if requested
            if (request.RemoveFromWatchlists)
            {
                await RemoveFromAllWatchlistsAsync(affected.ToArray(), ct);
            }
        }

        stopwatch.Stop();

        return new BatchOperationResult(
            Success: true,
            Operation: "delete",
            AffectedCount: affected.Count,
            SkippedCount: skipped.Count,
            FailedCount: errors.Count,
            AffectedSymbols: affected.ToArray(),
            SkippedSymbols: skipped.ToArray(),
            Errors: errors.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Toggle subscription settings for multiple symbols.
    /// </summary>
    public async Task<BatchOperationResult> ToggleSubscriptionsAsync(
        BatchToggleRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var affected = new List<string>();
        var skipped = new List<string>();

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in request.Symbols)
        {
            if (existingSymbols.TryGetValue(symbol, out var existing))
            {
                var updated = existing with
                {
                    SubscribeTrades = request.SubscribeTrades ?? existing.SubscribeTrades,
                    SubscribeDepth = request.SubscribeDepth ?? existing.SubscribeDepth,
                    DepthLevels = request.DepthLevels ?? existing.DepthLevels
                };
                existingSymbols[symbol] = updated;
                affected.Add(symbol);
            }
            else
            {
                skipped.Add(symbol);
            }
        }

        if (affected.Count > 0)
        {
            var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
            await _configStore.SaveAsync(next);
        }

        stopwatch.Stop();

        return new BatchOperationResult(
            Success: true,
            Operation: "toggle",
            AffectedCount: affected.Count,
            SkippedCount: skipped.Count,
            FailedCount: errors.Count,
            AffectedSymbols: affected.ToArray(),
            SkippedSymbols: skipped.ToArray(),
            Errors: errors.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Update configuration for multiple symbols.
    /// </summary>
    public async Task<BatchOperationResult> UpdateSymbolsAsync(
        BatchUpdateRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var affected = new List<string>();
        var skipped = new List<string>();

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in request.Symbols)
        {
            if (existingSymbols.TryGetValue(symbol, out var existing))
            {
                var updated = existing with
                {
                    SecurityType = request.SecurityType ?? existing.SecurityType,
                    Exchange = request.Exchange ?? existing.Exchange,
                    Currency = request.Currency ?? existing.Currency,
                    PrimaryExchange = request.PrimaryExchange ?? existing.PrimaryExchange
                };
                existingSymbols[symbol] = updated;
                affected.Add(symbol);
            }
            else
            {
                skipped.Add(symbol);
            }
        }

        if (affected.Count > 0)
        {
            var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
            await _configStore.SaveAsync(next);
        }

        stopwatch.Stop();

        return new BatchOperationResult(
            Success: true,
            Operation: "update",
            AffectedCount: affected.Count,
            SkippedCount: skipped.Count,
            FailedCount: errors.Count,
            AffectedSymbols: affected.ToArray(),
            SkippedSymbols: skipped.ToArray(),
            Errors: errors.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Add multiple symbols at once.
    /// </summary>
    public async Task<BatchOperationResult> AddSymbolsAsync(
        BatchAddRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var affected = new List<string>();
        var skipped = new List<string>();

        var defaults = request.Defaults ?? new BatchAddDefaults();

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in request.Symbols)
        {
            var normalizedSymbol = symbol.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(normalizedSymbol))
            {
                continue;
            }

            if (existingSymbols.ContainsKey(normalizedSymbol))
            {
                if (request.SkipExisting)
                {
                    skipped.Add(normalizedSymbol);
                    continue;
                }
            }

            var symbolConfig = new SymbolConfig(
                Symbol: normalizedSymbol,
                SubscribeTrades: defaults.SubscribeTrades,
                SubscribeDepth: defaults.SubscribeDepth,
                DepthLevels: defaults.DepthLevels,
                SecurityType: defaults.SecurityType,
                Exchange: defaults.Exchange,
                Currency: defaults.Currency
            );

            existingSymbols[normalizedSymbol] = symbolConfig;
            affected.Add(normalizedSymbol);
        }

        if (affected.Count > 0)
        {
            var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
            await _configStore.SaveAsync(next);
        }

        stopwatch.Stop();

        return new BatchOperationResult(
            Success: true,
            Operation: "add",
            AffectedCount: affected.Count,
            SkippedCount: skipped.Count,
            FailedCount: errors.Count,
            AffectedSymbols: affected.ToArray(),
            SkippedSymbols: skipped.ToArray(),
            Errors: errors.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Enable trades for multiple symbols.
    /// </summary>
    public Task<BatchOperationResult> EnableTradesAsync(string[] symbols, CancellationToken ct = default)
    {
        return ToggleSubscriptionsAsync(new BatchToggleRequest(symbols, SubscribeTrades: true), ct);
    }

    /// <summary>
    /// Disable trades for multiple symbols.
    /// </summary>
    public Task<BatchOperationResult> DisableTradesAsync(string[] symbols, CancellationToken ct = default)
    {
        return ToggleSubscriptionsAsync(new BatchToggleRequest(symbols, SubscribeTrades: false), ct);
    }

    /// <summary>
    /// Enable depth for multiple symbols.
    /// </summary>
    public Task<BatchOperationResult> EnableDepthAsync(string[] symbols, CancellationToken ct = default)
    {
        return ToggleSubscriptionsAsync(new BatchToggleRequest(symbols, SubscribeDepth: true), ct);
    }

    /// <summary>
    /// Disable depth for multiple symbols.
    /// </summary>
    public Task<BatchOperationResult> DisableDepthAsync(string[] symbols, CancellationToken ct = default)
    {
        return ToggleSubscriptionsAsync(new BatchToggleRequest(symbols, SubscribeDepth: false), ct);
    }

    /// <summary>
    /// Copy settings from one symbol to multiple others.
    /// </summary>
    public async Task<BatchOperationResult> CopySettingsAsync(
        BatchCopySettingsRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var affected = new List<string>();
        var skipped = new List<string>();

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        if (!existingSymbols.TryGetValue(request.SourceSymbol, out var source))
        {
            return new BatchOperationResult(
                Success: false,
                Operation: "copySettings",
                AffectedCount: 0,
                SkippedCount: 0,
                FailedCount: 1,
                AffectedSymbols: Array.Empty<string>(),
                SkippedSymbols: Array.Empty<string>(),
                Errors: new[] { $"Source symbol '{request.SourceSymbol}' not found" },
                ProcessingTimeMs: stopwatch.ElapsedMilliseconds
            );
        }

        foreach (var symbol in request.TargetSymbols)
        {
            if (existingSymbols.TryGetValue(symbol, out var existing))
            {
                var updated = existing with
                {
                    SubscribeTrades = source.SubscribeTrades,
                    SubscribeDepth = source.SubscribeDepth,
                    DepthLevels = source.DepthLevels,
                    SecurityType = source.SecurityType,
                    Exchange = source.Exchange,
                    Currency = source.Currency,
                    PrimaryExchange = source.PrimaryExchange
                };
                existingSymbols[symbol] = updated;
                affected.Add(symbol);
            }
            else
            {
                skipped.Add(symbol);
            }
        }

        if (affected.Count > 0)
        {
            var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
            await _configStore.SaveAsync(next);
        }

        stopwatch.Stop();

        return new BatchOperationResult(
            Success: true,
            Operation: "copySettings",
            AffectedCount: affected.Count,
            SkippedCount: skipped.Count,
            FailedCount: errors.Count,
            AffectedSymbols: affected.ToArray(),
            SkippedSymbols: skipped.ToArray(),
            Errors: errors.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Get symbols matching a filter.
    /// </summary>
    public async Task<string[]> GetFilteredSymbolsAsync(BatchFilter filter, CancellationToken ct = default)
    {
        var cfg = _configStore.Load();
        var symbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).AsEnumerable();

        // Apply pattern filter
        if (!string.IsNullOrEmpty(filter.SymbolPattern))
        {
            var pattern = "^" + Regex.Escape(filter.SymbolPattern).Replace("\\*", ".*") + "$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            symbols = symbols.Where(s => regex.IsMatch(s.Symbol));
        }

        // Apply trade subscription filter
        if (filter.HasTradeSubscription.HasValue)
        {
            symbols = symbols.Where(s => s.SubscribeTrades == filter.HasTradeSubscription.Value);
        }

        // Apply depth subscription filter
        if (filter.HasDepthSubscription.HasValue)
        {
            symbols = symbols.Where(s => s.SubscribeDepth == filter.HasDepthSubscription.Value);
        }

        // Apply security type filter
        if (!string.IsNullOrEmpty(filter.SecurityType))
        {
            symbols = symbols.Where(s => s.SecurityType.Equals(filter.SecurityType, StringComparison.OrdinalIgnoreCase));
        }

        // Apply exchange filter
        if (!string.IsNullOrEmpty(filter.Exchange))
        {
            symbols = symbols.Where(s => s.Exchange.Equals(filter.Exchange, StringComparison.OrdinalIgnoreCase));
        }

        var filteredSymbols = symbols.Select(s => s.Symbol).ToList();

        // Apply watchlist filter
        if (filter.InWatchlists is { Length: > 0 })
        {
            var watchlistSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var watchlistId in filter.InWatchlists)
            {
                var watchlist = await _watchlistService.GetWatchlistAsync(watchlistId, ct);
                if (watchlist != null)
                {
                    foreach (var symbol in watchlist.Symbols)
                    {
                        watchlistSymbols.Add(symbol);
                    }
                }
            }

            filteredSymbols = filteredSymbols.Where(s => watchlistSymbols.Contains(s)).ToList();
        }

        return filteredSymbols.ToArray();
    }

    /// <summary>
    /// Perform a batch operation with a filter.
    /// </summary>
    public async Task<BatchOperationResult> PerformFilteredOperationAsync(
        BatchFilteredOperationRequest request,
        CancellationToken ct = default)
    {
        var symbols = await GetFilteredSymbolsAsync(request.Filter, ct);

        if (symbols.Length == 0)
        {
            return new BatchOperationResult(
                Success: true,
                Operation: request.Operation,
                AffectedCount: 0,
                SkippedCount: 0,
                FailedCount: 0,
                AffectedSymbols: Array.Empty<string>(),
                SkippedSymbols: Array.Empty<string>(),
                Errors: Array.Empty<string>(),
                ProcessingTimeMs: 0
            );
        }

        return request.Operation.ToLowerInvariant() switch
        {
            "delete" => await DeleteSymbolsAsync(new BatchDeleteRequest(symbols), ct),
            "enabletrades" => await EnableTradesAsync(symbols, ct),
            "disabletrades" => await DisableTradesAsync(symbols, ct),
            "enabledepth" => await EnableDepthAsync(symbols, ct),
            "disabledepth" => await DisableDepthAsync(symbols, ct),
            "toggle" => await ToggleSubscriptionsAsync(CreateToggleRequest(symbols, request.Parameters), ct),
            "update" => await UpdateSymbolsAsync(CreateUpdateRequest(symbols, request.Parameters), ct),
            _ => new BatchOperationResult(
                Success: false,
                Operation: request.Operation,
                AffectedCount: 0,
                SkippedCount: 0,
                FailedCount: 1,
                AffectedSymbols: Array.Empty<string>(),
                SkippedSymbols: Array.Empty<string>(),
                Errors: new[] { $"Unknown operation: {request.Operation}" },
                ProcessingTimeMs: 0
            )
        };
    }

    /// <summary>
    /// Move symbols to a watchlist.
    /// </summary>
    public async Task<BatchOperationResult> MoveToWatchlistAsync(
        BatchMoveToWatchlistRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        // Add to target watchlist
        var addResult = await _watchlistService.AddSymbolsAsync(new AddSymbolsToWatchlistRequest(
            WatchlistId: request.TargetWatchlistId,
            Symbols: request.Symbols,
            SubscribeImmediately: false
        ), ct);

        if (!addResult.Success)
        {
            return new BatchOperationResult(
                Success: false,
                Operation: "moveToWatchlist",
                AffectedCount: 0,
                SkippedCount: 0,
                FailedCount: request.Symbols.Length,
                AffectedSymbols: Array.Empty<string>(),
                SkippedSymbols: Array.Empty<string>(),
                Errors: new[] { addResult.Message ?? "Failed to add to target watchlist" },
                ProcessingTimeMs: stopwatch.ElapsedMilliseconds
            );
        }

        // Remove from source watchlist if requested
        if (request.RemoveFromSource && !string.IsNullOrEmpty(request.SourceWatchlistId))
        {
            await _watchlistService.RemoveSymbolsAsync(new RemoveSymbolsFromWatchlistRequest(
                WatchlistId: request.SourceWatchlistId,
                Symbols: request.Symbols,
                UnsubscribeIfOrphaned: false
            ), ct);
        }

        stopwatch.Stop();

        return new BatchOperationResult(
            Success: true,
            Operation: "moveToWatchlist",
            AffectedCount: addResult.SymbolsAffected,
            SkippedCount: request.Symbols.Length - addResult.SymbolsAffected,
            FailedCount: 0,
            AffectedSymbols: addResult.AffectedSymbols ?? Array.Empty<string>(),
            SkippedSymbols: Array.Empty<string>(),
            Errors: errors.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    private async Task RemoveFromAllWatchlistsAsync(string[] symbols, CancellationToken ct)
    {
        var watchlists = await _watchlistService.GetAllWatchlistsAsync(ct);

        foreach (var watchlist in watchlists)
        {
            var symbolsInWatchlist = symbols
                .Where(s => watchlist.Symbols.Contains(s, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (symbolsInWatchlist.Length > 0)
            {
                await _watchlistService.RemoveSymbolsAsync(new RemoveSymbolsFromWatchlistRequest(
                    WatchlistId: watchlist.Id,
                    Symbols: symbolsInWatchlist,
                    UnsubscribeIfOrphaned: false
                ), ct);
            }
        }
    }

    private static BatchToggleRequest CreateToggleRequest(string[] symbols, Dictionary<string, object?>? parameters)
    {
        bool? subscribeTrades = null;
        bool? subscribeDepth = null;
        int? depthLevels = null;

        if (parameters != null)
        {
            if (parameters.TryGetValue("subscribeTrades", out var st) && st is bool stBool)
                subscribeTrades = stBool;
            if (parameters.TryGetValue("subscribeDepth", out var sd) && sd is bool sdBool)
                subscribeDepth = sdBool;
            if (parameters.TryGetValue("depthLevels", out var dl) && dl is int dlInt)
                depthLevels = dlInt;
        }

        return new BatchToggleRequest(symbols, subscribeTrades, subscribeDepth, depthLevels);
    }

    private static BatchUpdateRequest CreateUpdateRequest(string[] symbols, Dictionary<string, object?>? parameters)
    {
        string? securityType = null;
        string? exchange = null;
        string? currency = null;
        string? primaryExchange = null;

        if (parameters != null)
        {
            if (parameters.TryGetValue("securityType", out var stVal) && stVal is string stStr)
                securityType = stStr;
            if (parameters.TryGetValue("exchange", out var exVal) && exVal is string exStr)
                exchange = exStr;
            if (parameters.TryGetValue("currency", out var curVal) && curVal is string curStr)
                currency = curStr;
            if (parameters.TryGetValue("primaryExchange", out var peVal) && peVal is string peStr)
                primaryExchange = peStr;
        }

        return new BatchUpdateRequest(symbols, securityType, exchange, currency, primaryExchange);
    }
}
