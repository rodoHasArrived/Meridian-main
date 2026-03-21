namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Represents a position from a broker portfolio.
/// </summary>
public sealed record PortfolioPosition(
    string Symbol,

    decimal Quantity,

    decimal? MarketValue,

    decimal? AverageCost,

    decimal? UnrealizedPnL,

    string? AssetClass,

    string? Exchange,

    string Currency = "USD",

    string Side = "long"
);

/// <summary>
/// Portfolio summary from a broker.
/// </summary>
public sealed record PortfolioSummary(
    string Broker,

    string AccountId,

    decimal? TotalValue,

    decimal? CashBalance,

    decimal? BuyingPower,

    PortfolioPosition[] Positions,

    DateTimeOffset RetrievedAt,

    string Currency = "USD"
);

/// <summary>
/// Request to import symbols from a broker portfolio.
/// </summary>
public sealed record PortfolioImportRequest(
    string Broker,

    PortfolioImportOptions Options
);

/// <summary>
/// Options for portfolio import.
/// </summary>
public sealed record PortfolioImportOptions(
    decimal? MinPositionValue = null,

    decimal? MinQuantity = null,

    string[]? AssetClasses = null,

    string[]? ExcludeSymbols = null,

    bool LongOnly = false,

    bool CreateWatchlist = false,

    string? WatchlistName = null,

    bool SubscribeTrades = true,

    bool SubscribeDepth = true,

    bool SkipExisting = true
);

/// <summary>
/// Result of portfolio import.
/// </summary>
public sealed record PortfolioImportResult(
    bool Success,

    string Broker,

    int ImportedCount,

    int SkippedCount,

    int FailedCount,

    string[] ImportedSymbols,

    string[] Errors,

    string? WatchlistId = null,

    PortfolioSummary? Portfolio = null
);

/// <summary>
/// Manual portfolio entry for direct import without broker API.
/// </summary>
public sealed record ManualPortfolioEntry(
    string Symbol,

    decimal? Quantity = null,

    string? AssetClass = null
);

/// <summary>
/// Supported broker types.
/// </summary>
public enum BrokerType : byte
{
    Alpaca,

    InteractiveBrokers,

    Manual
}
