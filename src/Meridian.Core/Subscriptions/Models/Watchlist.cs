namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// A named group of symbols for organization and bulk operations.
/// </summary>
public sealed record Watchlist(
    string Id,

    string Name,

    string? Description,

    string[] Symbols,

    string? Color,

    int SortOrder,

    bool IsPinned,

    bool IsActive,

    WatchlistDefaults Defaults,

    DateTimeOffset CreatedAt,

    DateTimeOffset ModifiedAt
);

/// <summary>
/// Default subscription settings for a watchlist.
/// </summary>
public sealed record WatchlistDefaults(
    bool SubscribeTrades = true,
    bool SubscribeDepth = true,
    int DepthLevels = 10,
    string SecurityType = "STK",
    string Exchange = "SMART",
    string Currency = "USD"
);

/// <summary>
/// Request to create a new watchlist.
/// </summary>
public sealed record CreateWatchlistRequest(
    string Name,
    string? Description = null,
    string[]? Symbols = null,
    string? Color = null,
    bool IsActive = true,
    WatchlistDefaults? Defaults = null
);

/// <summary>
/// Request to update an existing watchlist.
/// </summary>
public sealed record UpdateWatchlistRequest(
    string? Name = null,
    string? Description = null,
    string? Color = null,
    int? SortOrder = null,
    bool? IsPinned = null,
    bool? IsActive = null,
    WatchlistDefaults? Defaults = null
);

/// <summary>
/// Request to add symbols to a watchlist.
/// </summary>
public sealed record AddSymbolsToWatchlistRequest(
    string WatchlistId,
    string[] Symbols,
    bool SubscribeImmediately = true
);

/// <summary>
/// Request to remove symbols from a watchlist.
/// </summary>
public sealed record RemoveSymbolsFromWatchlistRequest(
    string WatchlistId,
    string[] Symbols,
    bool UnsubscribeIfOrphaned = false
);

/// <summary>
/// Result of watchlist operations.
/// </summary>
public sealed record WatchlistOperationResult(
    bool Success,
    string WatchlistId,
    int SymbolsAffected,
    string? Message = null,
    string[]? AffectedSymbols = null
);

/// <summary>
/// Summary of a watchlist with subscription status.
/// </summary>
public sealed record WatchlistSummary(
    string Id,
    string Name,
    string? Description,
    string? Color,
    int SymbolCount,
    int ActiveSubscriptions,
    int InactiveSubscriptions,
    bool IsPinned,
    bool IsActive,
    DateTimeOffset ModifiedAt
);

/// <summary>
/// Request to subscribe/unsubscribe an entire watchlist.
/// </summary>
public sealed record WatchlistSubscriptionRequest(
    string WatchlistId,
    bool? SubscribeTrades = null,
    bool? SubscribeDepth = null,
    int? DepthLevels = null
);
