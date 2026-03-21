namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Request for batch symbol deletion.
/// </summary>
/// <param name="Symbols">Symbols to delete.</param>
/// <param name="RemoveFromWatchlists">If true, also remove from all watchlists.</param>
public sealed record BatchDeleteRequest(
    string[] Symbols,
    bool RemoveFromWatchlists = true
);

/// <summary>
/// Request for batch subscription toggle.
/// </summary>
/// <param name="Symbols">Symbols to modify.</param>
/// <param name="SubscribeTrades">Enable or disable trade subscriptions (null = don't change).</param>
/// <param name="SubscribeDepth">Enable or disable depth subscriptions (null = don't change).</param>
/// <param name="DepthLevels">Set depth levels (null = don't change).</param>
public sealed record BatchToggleRequest(
    string[] Symbols,
    bool? SubscribeTrades = null,
    bool? SubscribeDepth = null,
    int? DepthLevels = null
);

/// <summary>
/// Request for batch symbol update.
/// </summary>
/// <param name="Symbols">Symbols to update.</param>
/// <param name="SecurityType">New security type (null = don't change).</param>
/// <param name="Exchange">New exchange (null = don't change).</param>
/// <param name="Currency">New currency (null = don't change).</param>
/// <param name="PrimaryExchange">New primary exchange (null = don't change).</param>
public sealed record BatchUpdateRequest(
    string[] Symbols,
    string? SecurityType = null,
    string? Exchange = null,
    string? Currency = null,
    string? PrimaryExchange = null
);

/// <summary>
/// Request to add multiple symbols at once.
/// </summary>
/// <param name="Symbols">Symbols to add.</param>
/// <param name="Defaults">Default subscription settings.</param>
/// <param name="SkipExisting">Skip symbols that already exist.</param>
public sealed record BatchAddRequest(
    string[] Symbols,
    BatchAddDefaults? Defaults = null,
    bool SkipExisting = true
);

/// <summary>
/// Default settings for batch add operation.
/// </summary>
/// <param name="SubscribeTrades">Enable trade subscriptions by default.</param>
/// <param name="SubscribeDepth">Enable depth subscriptions by default.</param>
/// <param name="DepthLevels">Number of depth levels.</param>
/// <param name="SecurityType">Default security type.</param>
/// <param name="Exchange">Default exchange.</param>
/// <param name="Currency">Default currency.</param>
public sealed record BatchAddDefaults(
    bool SubscribeTrades = true,
    bool SubscribeDepth = true,
    int DepthLevels = 10,
    string SecurityType = "STK",
    string Exchange = "SMART",
    string Currency = "USD"
);

/// <summary>
/// Result of a batch operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Operation">Type of operation performed.</param>
/// <param name="AffectedCount">Number of symbols affected.</param>
/// <param name="SkippedCount">Number of symbols skipped.</param>
/// <param name="FailedCount">Number of failures.</param>
/// <param name="AffectedSymbols">Symbols that were affected.</param>
/// <param name="SkippedSymbols">Symbols that were skipped.</param>
/// <param name="Errors">Any error messages.</param>
/// <param name="ProcessingTimeMs">Processing time in milliseconds.</param>
public sealed record BatchOperationResult(
    bool Success,
    string Operation,
    int AffectedCount,
    int SkippedCount,
    int FailedCount,
    string[] AffectedSymbols,
    string[] SkippedSymbols,
    string[] Errors,
    long ProcessingTimeMs
);

/// <summary>
/// Request to move symbols between watchlists.
/// </summary>
/// <param name="Symbols">Symbols to move.</param>
/// <param name="TargetWatchlistId">Target watchlist ID.</param>
/// <param name="SourceWatchlistId">Source watchlist ID (null = don't remove from any).</param>
/// <param name="RemoveFromSource">Remove from source watchlist.</param>
public sealed record BatchMoveToWatchlistRequest(
    string[] Symbols,
    string TargetWatchlistId,
    string? SourceWatchlistId = null,
    bool RemoveFromSource = false
);

/// <summary>
/// Request to copy subscription settings from one symbol to others.
/// </summary>
/// <param name="SourceSymbol">Symbol to copy settings from.</param>
/// <param name="TargetSymbols">Symbols to copy settings to.</param>
public sealed record BatchCopySettingsRequest(
    string SourceSymbol,
    string[] TargetSymbols
);

/// <summary>
/// Filter criteria for batch operations.
/// </summary>
/// <param name="SymbolPattern">Only affect symbols matching this pattern (supports * wildcard).</param>
/// <param name="HasTradeSubscription">Only affect symbols subscribed to trades.</param>
/// <param name="HasDepthSubscription">Only affect symbols subscribed to depth.</param>
/// <param name="InWatchlists">Only affect symbols in these watchlists.</param>
/// <param name="SecurityType">Only affect symbols with this security type.</param>
/// <param name="Exchange">Only affect symbols on this exchange.</param>
public sealed record BatchFilter(
    string? SymbolPattern = null,
    bool? HasTradeSubscription = null,
    bool? HasDepthSubscription = null,
    string[]? InWatchlists = null,
    string? SecurityType = null,
    string? Exchange = null
);

/// <summary>
/// Request to perform batch operations with a filter.
/// </summary>
/// <param name="Filter">Filter criteria to select symbols.</param>
/// <param name="Operation">Operation to perform (toggle, delete, update).</param>
/// <param name="Parameters">Parameters for the operation.</param>
public sealed record BatchFilteredOperationRequest(
    BatchFilter Filter,
    string Operation,
    Dictionary<string, object?>? Parameters = null
);
