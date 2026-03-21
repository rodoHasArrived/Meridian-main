namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Result of a bulk CSV import operation.
/// </summary>
public sealed record BulkImportResult(
    int SuccessCount,

    int FailureCount,

    int SkippedCount,

    ImportError[] Errors,

    string[] ImportedSymbols,

    long ProcessingTimeMs
);

/// <summary>
/// Error encountered during import.
/// </summary>
public sealed record ImportError(
    int LineNumber,

    string? Symbol,

    string Message
);

/// <summary>
/// Options for CSV import operation.
/// </summary>
public sealed record BulkImportOptions(
    bool SkipExisting = true,

    bool UpdateExisting = false,

    bool HasHeader = true,

    ImportDefaults? Defaults = null,

    bool ValidateSymbols = true
);

/// <summary>
/// Default values for imported symbols.
/// </summary>
public sealed record ImportDefaults(
    bool SubscribeTrades = true,
    bool SubscribeDepth = true,
    int DepthLevels = 10,
    string SecurityType = "STK",
    string Exchange = "SMART",
    string Currency = "USD"
);

/// <summary>
/// Options for CSV export operation.
/// </summary>
public sealed record BulkExportOptions(
    bool IncludeHeader = true,

    string[]? Columns = null,

    string[]? FilterSymbols = null,

    bool IncludeMetadata = false
);

/// <summary>
/// CSV column mappings for import/export.
/// </summary>
public static class CsvColumns
{
    public const string Symbol = "Symbol";
    public const string SubscribeTrades = "SubscribeTrades";
    public const string SubscribeDepth = "SubscribeDepth";
    public const string DepthLevels = "DepthLevels";
    public const string SecurityType = "SecurityType";
    public const string Exchange = "Exchange";
    public const string Currency = "Currency";
    public const string PrimaryExchange = "PrimaryExchange";
    public const string LocalSymbol = "LocalSymbol";
    public const string TradingClass = "TradingClass";
    public const string ConId = "ConId";

    // Metadata columns (optional)
    public const string Name = "Name";
    public const string Sector = "Sector";
    public const string Industry = "Industry";
    public const string MarketCap = "MarketCap";

    public static readonly string[] Required = { Symbol };

    public static readonly string[] Standard =
    {
        Symbol, SubscribeTrades, SubscribeDepth, DepthLevels,
        SecurityType, Exchange, Currency, PrimaryExchange
    };

    public static readonly string[] Full =
    {
        Symbol, SubscribeTrades, SubscribeDepth, DepthLevels,
        SecurityType, Exchange, Currency, PrimaryExchange,
        LocalSymbol, TradingClass, ConId
    };

    public static readonly string[] WithMetadata =
    {
        Symbol, Name, Sector, Industry, MarketCap,
        SubscribeTrades, SubscribeDepth, DepthLevels,
        SecurityType, Exchange, Currency, PrimaryExchange
    };
}
